using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal static class PlayerCharacterAssetUtility
    {
        private static string AssetManifestUrl => $"{AppSettingsUtility.ObsidianGameVaultUrl}/asset-manifest";
        private static string PlayerCharactersListingMarkdownCacheFileName => "player-characters-listing.md";
        private static readonly TimeSpan HeroMarkdownDownloadInterval = TimeSpan.FromHours(1);
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly string[] HeroImageExtensions =
        [
            ".avif",
            ".bmp",
            ".gif",
            ".ico",
            ".jpeg",
            ".jpg",
            ".png",
            ".svg",
            ".tif",
            ".tiff",
            ".webp"
        ];
        private static readonly Regex ObsidianImageRegex = new(@"!\[\[(?<target>[^\]#|]+)(?:[#|][^\]]*)?\]\]", RegexOptions.Compiled);
        private static readonly Regex MarkdownLinkRegex = new(@"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]", RegexOptions.Compiled);
        private static readonly Regex InlineLinkRegex = new(@"\[([^\]]+)\]\((?<url>[^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex InvalidFileNameCharacterRegex = new(@"[^a-z0-9_-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static async Task<string[]> DownloadActiveHeroImagesAsync(
            string listingUrl,
            string pcsDirectory,
            CancellationToken cancellationToken = default)
        {
            var activeDirectory = GetActivePlayerCharactersDirectory(pcsDirectory);
            var listingMarkdown = await MarkdownUtility.GetMarkdownFromUrlAsync(listingUrl, cancellationToken);
            var manifestMarkdown = await MarkdownUtility.GetMarkdownFromUrlAsync(AssetManifestUrl, cancellationToken);

            ThrowIfMarkdownFetchFailed(listingMarkdown, listingUrl);
            ThrowIfMarkdownFetchFailed(manifestMarkdown, AssetManifestUrl);

            if (!TryDeserializeAssetManifest(manifestMarkdown, out var manifest))
            {
                return [];
            }

            var heroes = GetHeroRows(listingMarkdown).ToArray();
            var downloadedFiles = new List<string>();

            foreach (var hero in heroes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (hero.TokenFileName is null
                    || !TryGetSafeHeroImageFileName(hero.TokenFileName, out var tokenFileName)
                    || !manifest.TryGetValue(hero.TokenFileName, out var vaultPath))
                {
                    continue;
                }

                var imageUrl = await ObsidianPublishUtility.GetAccessUrlAsync(listingUrl, vaultPath, cancellationToken);
                var destinationPath = GetActiveHeroAssetPath(activeDirectory, tokenFileName);

                await DownloadFileAsync(imageUrl, destinationPath, cancellationToken);
                downloadedFiles.Add(destinationPath);
            }

            return downloadedFiles.ToArray();
        }

        public static async Task<string[]> DownloadActiveHeroMarkdownAsync(
            string listingMarkdown,
            string listingUrl,
            string pcsDirectory,
            CancellationToken cancellationToken = default)
        {
            ThrowIfMarkdownFetchFailed(listingMarkdown, listingUrl);

            var activeDirectory = GetActivePlayerCharactersDirectory(pcsDirectory);
            var downloadedFiles = new List<string>();

            foreach (var hero in GetHeroRows(listingMarkdown, listingUrl))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (hero.CharacterPageUrl is null)
                {
                    continue;
                }

                var fileName = GetHeroMarkdownFileName(hero.Name);
                if (fileName.Length == 0)
                {
                    continue;
                }

                var destinationPath = RuntimePathUtility.CombineUnderBase(activeDirectory, $"{fileName}.md");
                if (!ShouldDownloadHeroMarkdown(destinationPath))
                {
                    continue;
                }

                var heroMarkdown = await MarkdownUtility.GetMarkdownFromUrlAsync(
                    hero.CharacterPageUrl,
                    cancellationToken);
                ThrowIfMarkdownFetchFailed(heroMarkdown, hero.CharacterPageUrl);

                await AtomicFileUtility.WriteAllTextAsync(destinationPath, heroMarkdown, cancellationToken);
                FileDownloadCounters.AddCompletedDownload(destinationPath);
                downloadedFiles.Add(destinationPath);
            }

            return downloadedFiles.ToArray();
        }

        public static string[] GetListedActiveHeroImagePaths(
            string listingMarkdown,
            string pcsDirectory)
        {
            ArgumentNullException.ThrowIfNull(listingMarkdown);
            ArgumentNullException.ThrowIfNull(pcsDirectory);

            var activeDirectory = GetActivePlayerCharactersDirectory(pcsDirectory);
            if (!Directory.Exists(activeDirectory))
            {
                return [];
            }

            var listedTokenFileNames = GetHeroRows(listingMarkdown)
                .Select(hero => hero.TokenFileName)
                .Where(tokenFileName => !string.IsNullOrWhiteSpace(tokenFileName))
                .Select(tokenFileName => tokenFileName!)
                .Where(tokenFileName => TryGetSafeHeroImageFileName(tokenFileName, out _))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return Directory.GetFiles(activeDirectory)
                .Where(path => HeroImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Where(path => listedTokenFileNames.Contains(Path.GetFileName(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static string GetPlayerCharactersListingMarkdownCachePath(string pcsDirectory)
        {
            ArgumentNullException.ThrowIfNull(pcsDirectory);
            return RuntimePathUtility.CombineUnderBase(
                pcsDirectory,
                PlayerCharactersListingMarkdownCacheFileName);
        }

        public static PlayerCharacterHeroRow[] GetHeroRows(string markdown, string? listingUrl = null)
        {
            ArgumentNullException.ThrowIfNull(markdown);

            return markdown
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith('|') && line.EndsWith('|'))
                .Select(SplitMarkdownTableRow)
                .Where(cells => cells.Length >= 4)
                .Where(cells => !IsHeaderOrSeparator(cells[0]))
                .Select(cells => new PlayerCharacterHeroRow(
                    CleanMarkdownCell(cells[0]),
                    GetObsidianImageFileName(cells[3]),
                    GetCharacterPageUrl(cells[0], listingUrl)))
                .Where(row => !string.IsNullOrWhiteSpace(row.Name))
                .ToArray();
        }

        private static void ThrowIfMarkdownFetchFailed(string markdown, string url)
        {
            if (markdown.StartsWith(MarkdownUtility.InvalidUrlMessage, StringComparison.Ordinal)
                || markdown.StartsWith(MarkdownUtility.UnresolvedUrlMessage, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Markdown could not be fetched from {url}.");
            }
        }

        private static Dictionary<string, string> DeserializeAssetManifest(string manifestMarkdown)
        {
            var manifestJson = GetJsonPayloadFromMarkdown(manifestMarkdown);

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(manifestJson)
                    ?? throw new InvalidOperationException("The asset manifest could not be parsed as a JSON dictionary.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    "The asset manifest could not be parsed as JSON after removing optional Markdown front matter.",
                    ex);
            }
        }

        private static bool TryDeserializeAssetManifest(
            string manifestMarkdown,
            out Dictionary<string, string> manifest)
        {
            try
            {
                manifest = DeserializeAssetManifest(manifestMarkdown);
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                manifest = [];
                StartupLoggingUtility.Append("asset manifest load", ex);
                return false;
            }
        }

        private static string GetJsonPayloadFromMarkdown(string markdown)
        {
            var content = markdown.Trim('\uFEFF', ' ', '\t', '\r', '\n');

            if (content.StartsWith("---", StringComparison.Ordinal))
            {
                var frontMatterEnd = content.IndexOf("\n---", 3, StringComparison.Ordinal);
                if (frontMatterEnd >= 0)
                {
                    var contentStart = frontMatterEnd + "\n---".Length;
                    if (content.Length > contentStart && content[contentStart] == '\r')
                    {
                        contentStart++;
                    }

                    if (content.Length > contentStart && content[contentStart] == '\n')
                    {
                        contentStart++;
                    }

                    content = content[contentStart..].Trim();
                }
            }

            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLineEnd = content.IndexOf('\n', StringComparison.Ordinal);
                var closingFenceStart = content.LastIndexOf("```", StringComparison.Ordinal);

                if (firstLineEnd >= 0 && closingFenceStart > firstLineEnd)
                {
                    content = content[(firstLineEnd + 1)..closingFenceStart].Trim();
                }
            }

            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');

            return jsonStart >= 0 && jsonEnd >= jsonStart
                ? content[jsonStart..(jsonEnd + 1)]
                : content;
        }

        private static string[] SplitMarkdownTableRow(string line)
        {
            var cells = new List<string>();
            var current = new List<char>();
            var escaped = false;

            foreach (var ch in line.Trim().Trim('|'))
            {
                if (escaped)
                {
                    current.Add(ch);
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    current.Add(ch);
                    continue;
                }

                if (ch == '|')
                {
                    cells.Add(new string(current.ToArray()));
                    current.Clear();
                    continue;
                }

                current.Add(ch);
            }

            cells.Add(new string(current.ToArray()));
            return cells.Select(cell => cell.Trim()).ToArray();
        }

        private static bool IsHeaderOrSeparator(string value)
        {
            return value.Equals("Name", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Character", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(value, @"^:?-{3,}:?$");
        }

        private static string CleanMarkdownCell(string value)
        {
            value = ObsidianImageRegex.Replace(value, string.Empty);
            value = MarkdownLinkRegex.Replace(value, match =>
                match.Groups[2].Success ? match.Groups[2].Value : match.Groups[1].Value);
            value = InlineLinkRegex.Replace(value, "$1");
            return value.Trim();
        }

        private static string? GetCharacterPageUrl(string value, string? listingUrl)
        {
            if (string.IsNullOrWhiteSpace(listingUrl))
            {
                return null;
            }

            var wikiLinkMatch = MarkdownLinkRegex.Match(value);
            if (wikiLinkMatch.Success)
            {
                return BuildObsidianPublishPageUrl(wikiLinkMatch.Groups[1].Value, listingUrl);
            }

            var inlineLinkMatch = InlineLinkRegex.Match(value);
            if (inlineLinkMatch.Success)
            {
                return ResolveInlineLinkUrl(inlineLinkMatch.Groups["url"].Value, listingUrl);
            }

            return null;
        }

        private static string? BuildObsidianPublishPageUrl(string target, string listingUrl)
        {
            target = target.Split('#', 2)[0].Trim().TrimEnd('\\').Replace('\\', '/').TrimStart('/');

            if (target.Length == 0)
            {
                return null;
            }

            if (Uri.TryCreate(target, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (!Uri.TryCreate(listingUrl, UriKind.Absolute, out var listingUri))
            {
                return null;
            }

            var listingSegments = listingUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (listingSegments.Length == 0)
            {
                return null;
            }

            var vaultRoot = listingSegments[0];
            var targetPath = target.Contains('/')
                ? target
                : string.Join('/', listingSegments.Skip(1).SkipLast(1).Append(target));
            var encodedTargetPath = string.Join(
                '/',
                targetPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(EncodeObsidianPublishPathSegment));

            return $"{listingUri.Scheme}://{listingUri.Host}/{vaultRoot}/{encodedTargetPath}";
        }

        private static string? ResolveInlineLinkUrl(string target, string listingUrl)
        {
            target = target.Trim();

            if (Uri.TryCreate(target, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }

            return Uri.TryCreate(listingUrl, UriKind.Absolute, out var listingUri)
                && Uri.TryCreate(listingUri, target, out var resolvedUri)
                    ? resolvedUri.ToString()
                    : null;
        }

        private static string EncodeObsidianPublishPathSegment(string segment)
        {
            return Uri.EscapeDataString(Uri.UnescapeDataString(segment)).Replace("%20", "+");
        }

        private static string GetHeroMarkdownFileName(string heroName)
        {
            var firstName = heroName
                .Split(',', 2)[0]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;
            var fileName = InvalidFileNameCharacterRegex.Replace(firstName.ToLowerInvariant(), "-").Trim('-');

            return fileName;
        }

        private static bool ShouldDownloadHeroMarkdown(string markdownPath)
        {
            return !File.Exists(markdownPath)
                || DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(markdownPath) >= HeroMarkdownDownloadInterval;
        }

        private static string? GetObsidianImageFileName(string value)
        {
            var match = ObsidianImageRegex.Match(value);

            return match.Success
                ? match.Groups["target"].Value.Trim().TrimEnd('\\')
                : null;
        }

        private static string GetActivePlayerCharactersDirectory(string pcsDirectory)
        {
            return RuntimePathUtility.CombineUnderBase(pcsDirectory, "active");
        }

        private static string GetActiveHeroAssetPath(string activeDirectory, string tokenFileName)
        {
            if (!TryGetSafeHeroImageFileName(tokenFileName, out var safeFileName))
            {
                throw new InvalidOperationException($"Hero image target '{tokenFileName}' is not a safe file name.");
            }

            return RuntimePathUtility.CombineUnderBase(activeDirectory, safeFileName);
        }

        private static bool TryGetSafeHeroImageFileName(string tokenFileName, out string safeFileName)
        {
            safeFileName = string.Empty;
            if (string.IsNullOrWhiteSpace(tokenFileName))
            {
                return false;
            }

            var candidate = tokenFileName.Trim().TrimEnd('\\', '/');
            if (candidate.Length == 0
                || Path.IsPathRooted(candidate)
                || candidate.Contains(Path.DirectorySeparatorChar)
                || candidate.Contains(Path.AltDirectorySeparatorChar))
            {
                return false;
            }

            safeFileName = candidate;
            return true;
        }

        private static async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
        {
            using var response = await NetworkRequestUtility.SendAsync(
                HttpClient,
                () => new HttpRequestMessage(HttpMethod.Get, url),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken: cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                await AtomicFileUtility.WriteFileAsync(
                    destinationPath,
                    destination => NetworkRequestUtility.CopyToAsync(
                        source,
                        destination,
                        NetworkResponseContentLimit.Image,
                        cancellationToken),
                    cancellationToken);
            }

            FileDownloadCounters.AddCompletedDownload(destinationPath);
        }

        private static HttpClient CreateHttpClient()
        {
            var httpClient = NetworkRequestUtility.CreateHttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            return httpClient;
        }
    }

    internal sealed record PlayerCharacterHeroRow(string Name, string? TokenFileName, string? CharacterPageUrl = null);
}
