using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal static class MarkdownUtility
    {
        public const string InvalidUrlMessage = "invalid URL passed to GetMarkdownFromURL";
        public const string UnresolvedUrlMessage = "URL could not be resolved by GetMarkdownFromURL";

        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly Regex InlineImageRegex = new(@"!\[[^\]]*\]\((?<url><[^>]+>|[^)\s]+)(?:\s+""[^""]*"")?\)", RegexOptions.Compiled);
        private static readonly Regex ReferenceImageRegex = new(@"!\[[^\]]*\]\[(?<id>[^\]]*)\]", RegexOptions.Compiled);
        private static readonly Regex ReferenceDefinitionRegex = new(@"^\s*\[(?<id>[^\]]+)\]:\s*(?<url><[^>]+>|\S+)", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex HtmlImageRegex = new(@"<img\b[^>]*\bsrc\s*=\s*(?:""(?<url>[^""]+)""|'(?<url>[^']+)'|(?<url>[^\s>]+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ObsidianImageRegex = new(@"!\[\[(?<target>[^\]#|]+)(?:[#|][^\]]*)?\]\]", RegexOptions.Compiled);
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
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
        };

        public static string GetMarkdownFromURL(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return $"{InvalidUrlMessage}: {url}";
            }

            try
            {
                return GetMarkdownFromResponse(
                    response: NetworkRequestUtility.Send(
                        HttpClient,
                        () =>
                        {
                            var request = new HttpRequestMessage(HttpMethod.Get, uri);
                            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
                            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
                            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.1));
                            return request;
                        }),
                    originalUrl: url);
            }
            catch (HttpRequestException)
            {
                return $"{UnresolvedUrlMessage}: {url}";
            }
            catch (NetworkRequestException)
            {
                return $"{UnresolvedUrlMessage}: {url}";
            }
            catch (TaskCanceledException)
            {
                return $"{UnresolvedUrlMessage}: {url}";
            }
        }

        public static async Task<string> GetMarkdownFromUrlAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return $"{InvalidUrlMessage}: {url}";
            }

            try
            {
                using var response = await NetworkRequestUtility.SendAsync(
                    HttpClient,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, uri);
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.1));
                        return request;
                    },
                    cancellationToken: cancellationToken);

                return await GetMarkdownFromResponseAsync(response, url, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return $"{UnresolvedUrlMessage}: {url}";
            }
            catch (NetworkRequestException)
            {
                return $"{UnresolvedUrlMessage}: {url}";
            }
            catch (TaskCanceledException)
            {
                return $"{UnresolvedUrlMessage}: {url}";
            }
        }

        public static string[] GetImageUrisFromMarkdown(string markdown, Uri? baseUri = null)
        {
            ArgumentNullException.ThrowIfNull(markdown);

            var imageUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var referenceDefinitions = GetReferenceDefinitions(markdown);

            foreach (Match match in InlineImageRegex.Matches(markdown))
            {
                AddImageUri(imageUris, match.Groups["url"].Value, baseUri);
            }

            foreach (Match match in ReferenceImageRegex.Matches(markdown))
            {
                var referenceId = match.Groups["id"].Value;
                if (string.IsNullOrWhiteSpace(referenceId))
                {
                    continue;
                }

                if (referenceDefinitions.TryGetValue(referenceId, out var url))
                {
                    AddImageUri(imageUris, url, baseUri);
                }
            }

            foreach (Match match in HtmlImageRegex.Matches(markdown))
            {
                AddImageUri(imageUris, match.Groups["url"].Value, baseUri);
            }

            return imageUris.ToArray();
        }

        public static string[] GetImageUrisFromMarkdown(string markdown, string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new ArgumentException("A valid absolute base URL is required.", nameof(baseUrl));
            }

            return GetImageUrisFromMarkdown(markdown, baseUri);
        }

        public static string[] GetImageFileNamesFromMarkdown(string markdown)
        {
            ArgumentNullException.ThrowIfNull(markdown);

            var imageFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var referenceDefinitions = GetReferenceDefinitions(markdown);

            foreach (Match match in ObsidianImageRegex.Matches(markdown))
            {
                AddImageFileName(imageFileNames, match.Groups["target"].Value);
            }

            foreach (Match match in InlineImageRegex.Matches(markdown))
            {
                AddImageFileName(imageFileNames, match.Groups["url"].Value);
            }

            foreach (Match match in ReferenceImageRegex.Matches(markdown))
            {
                var referenceId = match.Groups["id"].Value;
                if (string.IsNullOrWhiteSpace(referenceId))
                {
                    continue;
                }

                if (referenceDefinitions.TryGetValue(referenceId, out var url))
                {
                    AddImageFileName(imageFileNames, url);
                }
            }

            foreach (Match match in HtmlImageRegex.Matches(markdown))
            {
                AddImageFileName(imageFileNames, match.Groups["url"].Value);
            }

            return imageFileNames.ToArray();
        }

        private static string GetMarkdownFromResponse(HttpResponseMessage response, string originalUrl)
        {
            using (response)
            {
                response.EnsureSuccessStatusCode();
                var initialLimit = GetInitialResponseLimit(response);
                var content = NetworkRequestUtility.ReadStringAsync(
                    response.Content,
                    initialLimit,
                    CancellationToken.None).GetAwaiter().GetResult();

                if (!IsHtmlResponse(response, content))
                {
                    EnsureMarkdownContentWithinLimit(content, initialLimit);
                    return content;
                }

                var markdownUrl = GetObsidianPublishMarkdownUrl(content);
                if (markdownUrl is null)
                {
                    return $"{UnresolvedUrlMessage}: {originalUrl}";
                }

                using var markdownResponse = NetworkRequestUtility.Send(
                    HttpClient,
                    () =>
                    {
                        var markdownRequest = new HttpRequestMessage(HttpMethod.Get, markdownUrl);
                        markdownRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
                        markdownRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
                        return markdownRequest;
                    });
                markdownResponse.EnsureSuccessStatusCode();

                return NetworkRequestUtility.ReadStringAsync(
                    markdownResponse.Content,
                    NetworkResponseContentLimit.Markdown,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        private static async Task<string> GetMarkdownFromResponseAsync(
            HttpResponseMessage response,
            string originalUrl,
            CancellationToken cancellationToken)
        {
            response.EnsureSuccessStatusCode();
            var initialLimit = GetInitialResponseLimit(response);
            var content = await NetworkRequestUtility.ReadStringAsync(
                response.Content,
                initialLimit,
                cancellationToken);

            if (!IsHtmlResponse(response, content))
            {
                EnsureMarkdownContentWithinLimit(content, initialLimit);
                return content;
            }

            var markdownUrl = GetObsidianPublishMarkdownUrl(content);
            if (markdownUrl is null)
            {
                return $"{UnresolvedUrlMessage}: {originalUrl}";
            }

            using var markdownResponse = await NetworkRequestUtility.SendAsync(
                HttpClient,
                () =>
                {
                    var markdownRequest = new HttpRequestMessage(HttpMethod.Get, markdownUrl);
                    markdownRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
                    markdownRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
                    return markdownRequest;
                },
                cancellationToken: cancellationToken);
            markdownResponse.EnsureSuccessStatusCode();

            return await NetworkRequestUtility.ReadStringAsync(
                markdownResponse.Content,
                NetworkResponseContentLimit.Markdown,
                cancellationToken);
        }

        private static bool IsHtmlResponse(HttpResponseMessage response, string content)
        {
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            return string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
                || content.TrimStart().StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
                || content.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase);
        }

        private static NetworkResponseContentLimit GetInitialResponseLimit(HttpResponseMessage response)
        {
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            return string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
                ? NetworkResponseContentLimit.Html
                : NetworkResponseContentLimit.Markdown;
        }

        private static void EnsureMarkdownContentWithinLimit(
            string content,
            NetworkResponseContentLimit appliedLimit)
        {
            if (ReferenceEquals(appliedLimit, NetworkResponseContentLimit.Markdown))
            {
                return;
            }

            NetworkRequestUtility.EnsureByteCountWithinLimit(
                Encoding.UTF8.GetByteCount(content),
                NetworkResponseContentLimit.Markdown);
        }

        private static Uri? GetObsidianPublishMarkdownUrl(string html)
        {
            var match = Regex.Match(html, "window\\.preloadPage=f\\(\"(?<url>https://[^\"]+\\.md)\"\\)");
            return match.Success && Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var uri)
                ? uri
                : null;
        }

        private static Dictionary<string, string> GetReferenceDefinitions(string markdown)
        {
            var referenceDefinitions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in ReferenceDefinitionRegex.Matches(markdown))
            {
                referenceDefinitions[match.Groups["id"].Value] = match.Groups["url"].Value;
            }

            return referenceDefinitions;
        }

        private static void AddImageUri(HashSet<string> imageUris, string url, Uri? baseUri)
        {
            url = TrimMarkdownUrl(url);

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

        private static void AddImageFileName(HashSet<string> imageFileNames, string imagePath)
        {
            var fileName = GetImageFileName(imagePath);

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                imageFileNames.Add(fileName);
            }
        }

        private static string? GetImageFileName(string imagePath)
        {
            imagePath = TrimMarkdownUrl(imagePath);
            imagePath = imagePath.Split('#', 2)[0].Split('?', 2)[0].Split('|', 2)[0].Trim();

            if (Uri.TryCreate(imagePath, UriKind.Absolute, out var uri))
            {
                imagePath = uri.LocalPath;
            }

            imagePath = Uri.UnescapeDataString(imagePath).Replace('\\', '/');
            var fileName = imagePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

            return fileName is not null && ImageExtensions.Contains(Path.GetExtension(fileName))
                ? fileName
                : null;
        }

        private static string TrimMarkdownUrl(string url)
        {
            url = url.Trim();
            return url.Length >= 2 && url[0] == '<' && url[^1] == '>'
                ? url[1..^1].Trim()
                : url;
        }

        private static HttpClient CreateHttpClient()
        {
            return NetworkRequestUtility.CreateHttpClient();
        }
    }
}
