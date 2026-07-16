using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal static class ObsidianPublishUtility
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly Regex SiteUidRegex = new(@"""uid"":""(?<uid>[^""]+)""", RegexOptions.Compiled);
        private static readonly Regex SiteHostRegex = new(@"""host"":""(?<host>[^""]+)""", RegexOptions.Compiled);
        private static readonly Regex AttachmentIndexFileNameRegex = new(@"^(?<name>.+)-\d{17,}(?<extension>\.[^.]+)\.md$", RegexOptions.Compiled);

        public static async Task<Dictionary<string, string>> GetAttachmentImagePathsByFileNameAsync(
            string siteUrl,
            string indexDirectoryName = "index",
            CancellationToken cancellationToken = default)
        {
            var siteInfo = await GetSiteInfoAsync(siteUrl, cancellationToken);
            var cache = await GetPublishedCacheAsync(siteInfo, cancellationToken);
            var assetPathsByFileName = GetAssetPathsByFileName(cache);
            var imagePathsByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in cache)
            {
                if (!IsIndexMarkdownPath(path, indexDirectoryName))
                {
                    continue;
                }

                var imageFileName = GetImageFileNameFromAttachmentIndexPath(path);
                if (imageFileName is null || !assetPathsByFileName.TryGetValue(imageFileName, out var assetPath))
                {
                    continue;
                }

                imagePathsByFileName[imageFileName] = BuildAccessUrl(siteInfo, assetPath);
            }

            return imagePathsByFileName;
        }

        public static async Task<Dictionary<string, string>> GetPublishedAssetUrlsByFileNameAsync(
            string siteUrl,
            CancellationToken cancellationToken = default)
        {
            var siteInfo = await GetSiteInfoAsync(siteUrl, cancellationToken);
            var cache = await GetPublishedCacheAsync(siteInfo, cancellationToken);

            return GetAssetPathsByFileName(cache)
                .ToDictionary(
                    pair => pair.Key,
                    pair => BuildAccessUrl(siteInfo, pair.Value),
                    StringComparer.OrdinalIgnoreCase);
        }

        public static async Task<string> GetAccessUrlAsync(
            string siteUrl,
            string vaultPath,
            CancellationToken cancellationToken = default)
        {
            var siteInfo = await GetSiteInfoAsync(siteUrl, cancellationToken);
            return BuildAccessUrl(siteInfo, vaultPath);
        }

        private static async Task<ObsidianPublishSiteInfo> GetSiteInfoAsync(
            string siteUrl,
            CancellationToken cancellationToken)
        {
            var siteValidation = NetworkUrlAllowlistUtility.Validate(siteUrl, NetworkUrlPurpose.ObsidianPublish);
            if (!siteValidation.IsAllowed)
            {
                throw new InvalidOperationException($"Obsidian Publish site URL is not allowed: {siteValidation.RejectionReason}");
            }

            var html = await HtmlUtility.GetHtmlFromUrlAsync(siteUrl, cancellationToken);
            var uidMatch = SiteUidRegex.Match(html);
            var hostMatch = SiteHostRegex.Match(html);

            if (!uidMatch.Success || !hostMatch.Success)
            {
                throw new InvalidOperationException("Obsidian Publish site information could not be found.");
            }

            var siteInfo = new ObsidianPublishSiteInfo(
                uidMatch.Groups["uid"].Value,
                hostMatch.Groups["host"].Value);
            NetworkUrlAllowlistUtility.EnsureAllowed(new Uri($"https://{siteInfo.Host}/"), NetworkUrlPurpose.ObsidianPublish);
            return siteInfo;
        }

        private static async Task<string[]> GetPublishedCacheAsync(
            ObsidianPublishSiteInfo siteInfo,
            CancellationToken cancellationToken)
        {
            var cacheUrl = $"https://{siteInfo.Host}/cache/{siteInfo.Uid}";
            using var response = await NetworkRequestUtility.SendAsync(
                HttpClient,
                () => new HttpRequestMessage(HttpMethod.Get, cacheUrl),
                purpose: NetworkUrlPurpose.ObsidianPublish,
                cancellationToken: cancellationToken);
            response.EnsureSuccessStatusCode();

            var cacheBytes = await NetworkRequestUtility.ReadBytesAsync(
                response.Content,
                NetworkResponseContentLimit.JsonCache,
                cancellationToken);
            using var cacheJson = JsonDocument.Parse(cacheBytes);

            return cacheJson.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray();
        }

        private static Dictionary<string, string> GetAssetPathsByFileName(string[] cache)
        {
            var assetPathsByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in cache)
            {
                if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fileName = Path.GetFileName(path.Replace('\\', '/'));

                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    assetPathsByFileName[fileName] = path;
                }
            }

            return assetPathsByFileName;
        }

        private static bool IsIndexMarkdownPath(string path, string indexDirectoryName)
        {
            return path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                && path.StartsWith($"{indexDirectoryName}/", StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetImageFileNameFromAttachmentIndexPath(string path)
        {
            var indexFileName = Path.GetFileName(path.Replace('\\', '/'));
            var match = AttachmentIndexFileNameRegex.Match(indexFileName);

            return match.Success
                ? $"{match.Groups["name"].Value}{match.Groups["extension"].Value}"
                : null;
        }

        private static string BuildAccessUrl(ObsidianPublishSiteInfo siteInfo, string path)
        {
            var accessUrl = $"https://{siteInfo.Host}/access/{siteInfo.Uid}/{EscapePath(path)}";
            NetworkUrlAllowlistUtility.EnsureAllowed(new Uri(accessUrl), NetworkUrlPurpose.ObsidianPublish);
            return accessUrl;
        }

        private static string EscapePath(string path)
        {
            return string.Join(
                "/",
                path.Replace('\\', '/')
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));
        }

        private static HttpClient CreateHttpClient()
        {
            return NetworkRequestUtility.CreateHttpClient();
        }

        private sealed record ObsidianPublishSiteInfo(string Uid, string Host);
    }
}
