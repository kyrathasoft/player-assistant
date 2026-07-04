using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal sealed record Hyperlink(string Url, string Text);

    internal static class HtmlUtility
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly Regex HtmlImageRegex = new(@"<img\b[^>]*\bsrc\s*=\s*(?:""(?<url>[^""]+)""|'(?<url>[^']+)'|(?<url>[^\s>]+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlImageTagRegex = new(@"<img\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlAnchorRegex = new(@"<a\b(?<attributes>[^>]*)>(?<text>.*?)</a>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex HrefAttributeRegex = new(@"(?:^|\s)href\s*=\s*(?:""(?<url>[^""]*)""|'(?<url>[^']*)'|(?<url>[^\s>]+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

        public static async Task<string> GetHtmlFromUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("A valid absolute URL is required.", nameof(url));
            }

            return await GetHtmlFromUrlAsync(uri, cancellationToken);
        }

        public static async Task<string> GetHtmlFromUrlAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("Only HTTP and HTTPS URLs are supported.", nameof(uri));
            }

            if (RpolAuthUtility.IsRpolUri(uri))
            {
                return await RpolAuthUtility.GetHtmlFromUrlAsync(uri, cancellationToken);
            }

            using var response = await NetworkRequestUtility.SendAsync(
                HttpClient,
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.1));
                    return request;
                },
                cancellationToken: cancellationToken);
            response.EnsureSuccessStatusCode();

            return await NetworkRequestUtility.ReadStringAsync(
                response.Content,
                NetworkResponseContentLimit.Html,
                cancellationToken);
        }

        public static Task<Hyperlink[]> GetRpolGameHyperlinksAsync(CancellationToken cancellationToken = default)
        {
            return GetHyperlinksFromUrlAsync(AppSettingsUtility.GameForumUrl, cancellationToken);
        }

        public static async Task<Hyperlink[]> GetHyperlinksFromUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("A valid absolute URL is required.", nameof(url));
            }

            return await GetHyperlinksFromUrlAsync(uri, cancellationToken);
        }

        public static async Task<Hyperlink[]> GetHyperlinksFromUrlAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            var html = await GetHtmlFromUrlAsync(uri, cancellationToken);
            return GetHyperlinksFromHtml(html, uri);
        }

        public static Hyperlink[] GetHyperlinksFromHtml(string html, Uri? baseUri = null)
        {
            ArgumentNullException.ThrowIfNull(html);

            var hyperlinks = new List<Hyperlink>();

            foreach (Match anchorMatch in HtmlAnchorRegex.Matches(html))
            {
                var hrefMatch = HrefAttributeRegex.Match(anchorMatch.Groups["attributes"].Value);
                if (!hrefMatch.Success)
                {
                    continue;
                }

                var url = ResolveUri(WebUtility.HtmlDecode(hrefMatch.Groups["url"].Value), baseUri);
                if (url.Length == 0)
                {
                    continue;
                }

                hyperlinks.Add(new Hyperlink(
                    url,
                    GetUserFacingLinkText(anchorMatch.Groups["text"].Value)));
            }

            return hyperlinks.ToArray();
        }

        public static Hyperlink[] GetHyperlinksFromHtml(string html, string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new ArgumentException("A valid absolute base URL is required.", nameof(baseUrl));
            }

            return GetHyperlinksFromHtml(html, baseUri);
        }

        public static string[] GetImageUrisFromHtml(string html, Uri? baseUri = null)
        {
            ArgumentNullException.ThrowIfNull(html);

            var imageUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in HtmlImageRegex.Matches(html))
            {
                AddImageUri(imageUris, match.Groups["url"].Value, baseUri);
            }

            return imageUris.ToArray();
        }

        public static string[] GetImageUrisFromHtml(string html, string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new ArgumentException("A valid absolute base URL is required.", nameof(baseUrl));
            }

            return GetImageUrisFromHtml(html, baseUri);
        }

        public static async Task<string> RemoveImagesFromHtmlFileAsync(
            string htmlFilePath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(htmlFilePath);

            var html = await File.ReadAllTextAsync(htmlFilePath, cancellationToken);
            return RemoveImagesFromHtml(html);
        }

        public static string RemoveImagesFromHtml(string html)
        {
            ArgumentNullException.ThrowIfNull(html);

            return HtmlImageTagRegex.Replace(html, string.Empty);
        }

        public static string GetPlainTextFromHtml(string html)
        {
            ArgumentNullException.ThrowIfNull(html);

            var withoutTags = HtmlTagRegex.Replace(html, " ");
            var decoded = WebUtility.HtmlDecode(withoutTags)
                .Replace('\u00A0', ' ')
                .Replace('\u202F', ' ');

            return WhitespaceRegex.Replace(decoded, " ").Trim();
        }

        private static string GetUserFacingLinkText(string html)
        {
            return GetPlainTextFromHtml(html);
        }

        private static string ResolveUri(string url, Uri? baseUri)
        {
            url = url.Trim();

            if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (baseUri is not null && Uri.TryCreate(baseUri, url, out var resolvedUri))
            {
                return resolvedUri.ToString();
            }

            return url;
        }

        private static void AddImageUri(HashSet<string> imageUris, string url, Uri? baseUri)
        {
            url = url.Trim();

            if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
            {
                imageUris.Add(absoluteUri.ToString());
                return;
            }

            if (baseUri is not null && Uri.TryCreate(baseUri, url, out var resolvedUri))
            {
                imageUris.Add(resolvedUri.ToString());
            }
        }

        private static HttpClient CreateHttpClient()
        {
            return NetworkRequestUtility.CreateHttpClient();
        }
    }
}
