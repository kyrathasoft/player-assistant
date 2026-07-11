using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text;
using System.Xml.Linq;

namespace PlayerAssistant
{
    internal readonly record struct SitemapIndexResult(int KeywordCount, int NodeCount);

    internal static class SitemapUtility
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static async Task DownloadSitemapAsync(
            string sitemapUrl,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(sitemapUrl, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("A valid absolute sitemap URL is required.", nameof(sitemapUrl));
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("Only HTTP and HTTPS sitemap URLs are supported.", nameof(sitemapUrl));
            }

            using var response = await NetworkRequestUtility.SendAsync(
                HttpClient,
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.1));
                    return request;
                },
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken: cancellationToken);
            response.EnsureSuccessStatusCode();

            var sitemapBytes = await NetworkRequestUtility.ReadBytesAsync(
                response.Content,
                NetworkResponseContentLimit.JsonCache,
                cancellationToken);
            ValidateSitemapXml(sitemapBytes);

            await SourceIntegrityUtility.ValidateAndWriteBytesFileAsync(
                destinationPath,
                uri.ToString(),
                "obsidian-sitemap",
                sitemapBytes,
                SourceIntegrityUtility.CreateSitemapShape(Encoding.UTF8.GetString(sitemapBytes)),
                cancellationToken);

            FileDownloadCounters.AddCompletedDownload(destinationPath);
        }

        public static async Task<SitemapIndexResult> WriteKeywordUrlDictionaryAsync(
            string sitemapPath,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            var sitemapIndex = await Task.Run(() => BuildSitemapIndex(sitemapPath), cancellationToken);

            await AtomicFileUtility.WriteFileAsync(
                destinationPath,
                destination => JsonSerializer.SerializeAsync(
                    destination,
                    sitemapIndex.KeywordUrls,
                    new JsonSerializerOptions
                    {
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        WriteIndented = true
                    },
                    cancellationToken),
                cancellationToken);

            return new SitemapIndexResult(sitemapIndex.KeywordUrls.Count, sitemapIndex.NodeCount);
        }

        public static Task<string[]> ReadUrlsFromSitemapAsync(
            string sitemapPath,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => ReadUrlsFromSitemap(sitemapPath), cancellationToken);
        }

        private static (Dictionary<string, string> KeywordUrls, int NodeCount) BuildSitemapIndex(string sitemapPath)
        {
            using var sitemapStream = File.OpenRead(sitemapPath);
            var document = XDocument.Load(sitemapStream);
            ValidateSitemapDocument(document);
            var keywordUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var nodeCount = 0;

            foreach (var loc in document.Descendants().Where(element => element.Name.LocalName == "loc"))
            {
                nodeCount++;

                var url = loc.Value.Trim();
                var keyword = GetKeywordFromUrl(url);

                if (keyword.Length > 0)
                {
                    keywordUrls.TryAdd(keyword, url);
                }
            }

            return (keywordUrls, nodeCount);
        }

        private static string[] ReadUrlsFromSitemap(string sitemapPath)
        {
            using var sitemapStream = File.OpenRead(sitemapPath);
            var document = XDocument.Load(sitemapStream);
            ValidateSitemapDocument(document);

            return document.Descendants()
                .Where(element => element.Name.LocalName == "loc")
                .Select(element => element.Value.Trim())
                .Where(url => url.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string GetKeywordFromUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            var pageSegment = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();

            if (string.IsNullOrWhiteSpace(pageSegment))
            {
                return string.Empty;
            }

            return Uri.UnescapeDataString(pageSegment.Replace('+', ' ')).Trim();
        }

        internal static void ValidateSitemapXml(string sitemapXml)
        {
            ArgumentNullException.ThrowIfNull(sitemapXml);
            ValidateSitemapDocument(XDocument.Parse(sitemapXml));
        }

        internal static void ValidateSitemapXml(byte[] sitemapBytes)
        {
            ArgumentNullException.ThrowIfNull(sitemapBytes);
            ValidateSitemapXml(Encoding.UTF8.GetString(sitemapBytes));
        }

        private static void ValidateSitemapDocument(XDocument document)
        {
            foreach (var loc in document.Descendants().Where(element => element.Name.LocalName == "loc"))
            {
                ValidateStoredUrl(loc.Value.Trim(), "sitemap.xml");
            }
        }

        private static void ValidateStoredUrl(string url, string description)
        {
            var validation = NetworkUrlAllowlistUtility.Validate(url);
            if (!validation.IsAllowed)
            {
                throw new InvalidOperationException(
                    $"{description} contains a URL that is not allowed: {url}. {validation.RejectionReason}");
            }
        }

        private static HttpClient CreateHttpClient()
        {
            return NetworkRequestUtility.CreateHttpClient();
        }
    }
}
