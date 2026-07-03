using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal sealed record RpolThreadPost(
        int MessageNumber,
        string Author,
        string CharacterDetails,
        string PostedDate,
        string PostedTime,
        string FileName,
        string PostHtml,
        string BodyHtml,
        string BodyText);

    internal sealed record RpolThreadPostFile(
        int MessageNumber,
        string Author,
        string PostedDate,
        string PostedTime,
        string FileName,
        int BodyCharacterCount);

    internal sealed record RpolThreadSplitResult(
        string ThreadTitle,
        string SourceUrl,
        string OutputDirectory,
        int PostCount,
        IReadOnlyDictionary<string, int> CountsByAuthor,
        IReadOnlyList<RpolThreadPostFile> Posts);

    internal static class RpolThreadPostUtility
    {
        public const string Ch1KirkilstonShowAllUrl = "https://rpol.net/display.cgi?gi=80170&ti=7&date=1779581880&msgpage=&show=all";
        public const string DungeonMasterAuthor = "Dungeon Master";
        public const string NuandaAuthor = "Nuanda";
        public const string NuandaNemereAuthor = "Nuanda Nemere";
        public const string BillworthTurgenAuthor = "Billworth Turgen";
        public const string ThurganNewlAuthor = "Thurgan Newl";
        public const string TheArchonAuthor = "The-Archon";

        private static readonly Regex MessageNumberRegex = new(@"<li>msg #(?<number>\d+)</li>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MessageAuthorRegex = new(@"<span class='messageauthor'>(?<author>[\s\S]*?)</span>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CharacterDetailsRegex = new(@"<div class='characterdetails'>(?<details>[\s\S]*?)</div>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MessageBodyRegex = new(@"<div class='messagebody' id='msg\d+'>(?<body>[\s\S]*?)</div>\s*</div><!-- 1 -->", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PostedAtRegex = new(@"(?<date>(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun) \d+ \w+ \d{4}) at (?<time>\d{2}:\d{2})", RegexOptions.Compiled);
        private static readonly Regex BreakRegex = new(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RuleRegex = new(@"<hr\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

        public static Task<RpolThreadSplitResult> WriteCh1KirkilstonPostsAsync(
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            return WriteThreadPostsFromUrlAsync(
                Ch1KirkilstonShowAllUrl,
                outputDirectory,
                "Ch 1 - Kirkilston",
                cancellationToken);
        }

        public static Task<RpolThreadSplitResult> WriteThreadPostsFromLinkTextAsync(
            string linkText,
            string outputRootDirectory,
            CancellationToken cancellationToken = default)
        {
            return WriteThreadPostsFromLinkTextAsync(
                AppSettingsUtility.GameForumUrl,
                linkText,
                outputRootDirectory,
                cancellationToken);
        }

        public static async Task<RpolThreadSplitResult> WriteThreadPostsFromLinkTextAsync(
            string gameUrl,
            string linkText,
            string outputRootDirectory,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(linkText);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputRootDirectory);

            var hyperlinks = await HtmlUtility.GetHyperlinksFromUrlAsync(gameUrl, cancellationToken);
            var hyperlink = hyperlinks.FirstOrDefault(link =>
                string.Equals(link.Text, linkText, StringComparison.OrdinalIgnoreCase));

            if (hyperlink is null)
            {
                throw new InvalidOperationException($"No hyperlink with text '{linkText}' was found at {gameUrl}.");
            }

            var outputDirectory = Path.Combine(outputRootDirectory, GetFileNameSlug(linkText));
            return await WriteThreadPostsFromUrlAsync(
                GetShowAllThreadUrl(hyperlink.Url),
                outputDirectory,
                linkText,
                cancellationToken);
        }

        public static async Task<RpolThreadSplitResult> WriteThreadPostsFromUrlAsync(
            string url,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            return await WriteThreadPostsFromUrlAsync(
                url,
                outputDirectory,
                GetThreadTitleFromUrl(url),
                cancellationToken);
        }

        public static async Task<RpolThreadSplitResult> WriteThreadPostsFromUrlAsync(
            string url,
            string outputDirectory,
            string threadTitle,
            CancellationToken cancellationToken = default)
        {
            var html = await HtmlUtility.GetHtmlFromUrlAsync(url, cancellationToken);
            return await WriteThreadPostsFromHtmlAsync(html, url, outputDirectory, threadTitle, cancellationToken);
        }

        public static async Task<RpolThreadSplitResult> WriteThreadPostsFromHtmlAsync(
            string html,
            string sourceUrl,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            return await WriteThreadPostsFromHtmlAsync(
                html,
                sourceUrl,
                outputDirectory,
                GetThreadTitleFromUrl(sourceUrl),
                cancellationToken);
        }

        public static async Task<RpolThreadSplitResult> WriteThreadPostsFromHtmlAsync(
            string html,
            string sourceUrl,
            string outputDirectory,
            string threadTitle,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(html);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceUrl);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(threadTitle);

            var posts = GetThreadPostsFromHtml(html).ToArray();
            var fullOutputDirectory = Path.GetFullPath(outputDirectory);
            var outputParentDirectory = Path.GetDirectoryName(fullOutputDirectory) ?? Directory.GetCurrentDirectory();
            fullOutputDirectory = RuntimePathUtility.EnsurePathUnderBase(outputParentDirectory, fullOutputDirectory);
            var stagingDirectory = CreateSiblingWorkingDirectory(fullOutputDirectory, "staging");

            try
            {
                Directory.CreateDirectory(stagingDirectory);
                await File.WriteAllTextAsync(RuntimePathUtility.CombineUnderBase(stagingDirectory, "_source-show-all.html"), html, cancellationToken);

                var postFiles = new List<RpolThreadPostFile>();

                foreach (var post in posts)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var postDocument = CreatePostDocument(post, threadTitle);
                    await File.WriteAllTextAsync(
                        RuntimePathUtility.CombineUnderBase(stagingDirectory, post.FileName),
                        postDocument,
                        cancellationToken);

                    postFiles.Add(new RpolThreadPostFile(
                        post.MessageNumber,
                        post.Author,
                        post.PostedDate,
                        post.PostedTime,
                        post.FileName,
                        post.BodyText.Length));
                }

                var countsByAuthor = posts
                    .GroupBy(post => post.Author, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

                var result = new RpolThreadSplitResult(
                    threadTitle,
                    sourceUrl,
                    fullOutputDirectory,
                    posts.Length,
                    countsByAuthor,
                    postFiles);

                await File.WriteAllTextAsync(
                    RuntimePathUtility.CombineUnderBase(stagingDirectory, "index.html"),
                    CreateIndexDocument(result),
                    cancellationToken);

                await File.WriteAllTextAsync(
                    RuntimePathUtility.CombineUnderBase(stagingDirectory, "manifest.json"),
                    JsonSerializer.Serialize(result, ManifestJsonOptions),
                    cancellationToken);

                ValidateStagedThreadExport(stagingDirectory, result);
                cancellationToken.ThrowIfCancellationRequested();
                CommitStagedDirectory(stagingDirectory, fullOutputDirectory);

                return result;
            }
            finally
            {
                TryDeleteDirectoryIfPresent(stagingDirectory);
            }
        }

        public static RpolThreadPost[] GetThreadPostsFromHtml(string html)
        {
            ArgumentNullException.ThrowIfNull(html);

            var posts = new List<RpolThreadPost>();
            var blocks = Regex.Split(html, "(?=<div class='message'>)", RegexOptions.IgnoreCase);

            foreach (var block in blocks.Where(block => block.StartsWith("<div class='message'>", StringComparison.OrdinalIgnoreCase)))
            {
                var postHtml = TrimToPostBlock(block);
                var messageNumberMatch = MessageNumberRegex.Match(postHtml);
                var authorMatch = MessageAuthorRegex.Match(postHtml);
                var bodyMatch = MessageBodyRegex.Match(postHtml);

                if (!messageNumberMatch.Success || !authorMatch.Success || !bodyMatch.Success)
                {
                    continue;
                }

                var messageNumber = int.Parse(messageNumberMatch.Groups["number"].Value);
                var author = GetPlainText(authorMatch.Groups["author"].Value);
                var bodyHtml = bodyMatch.Groups["body"].Value;
                var postedAtMatch = PostedAtRegex.Match(GetPlainText(postHtml));
                var characterDetailsMatch = CharacterDetailsRegex.Match(postHtml);

                posts.Add(new RpolThreadPost(
                    messageNumber,
                    author,
                    characterDetailsMatch.Success ? GetPlainText(characterDetailsMatch.Groups["details"].Value) : string.Empty,
                    postedAtMatch.Success ? postedAtMatch.Groups["date"].Value : string.Empty,
                    postedAtMatch.Success ? postedAtMatch.Groups["time"].Value : string.Empty,
                    $"{messageNumber:000}-{GetFileNameSlug(author)}.html",
                    postHtml,
                    bodyHtml,
                    GetPlainText(bodyHtml)));
            }

            return posts
                .OrderBy(post => post.MessageNumber)
                .ToArray();
        }

        public static IReadOnlyDictionary<string, int> GetPostCountsByAuthorFromSavedHtmlFiles(
            IEnumerable<string> htmlFilePaths)
        {
            ArgumentNullException.ThrowIfNull(htmlFilePaths);

            return GetPostCountsByAuthor(
                htmlFilePaths.SelectMany(GetThreadPostsFromHtmlFile),
                _ => true);
        }

        public static IReadOnlyDictionary<string, int> GetAdjustedPostTalliesFromSavedHtmlFiles(
            IEnumerable<string> htmlFilePaths)
        {
            ArgumentNullException.ThrowIfNull(htmlFilePaths);

            return GetAdjustedPostTallies(htmlFilePaths.SelectMany(GetThreadPostsFromHtmlFile));
        }

        public static IReadOnlyDictionary<string, int> GetAdjustedPostTalliesFromSavedHtmlDirectories(
            string postsDirectory,
            string? asideDirectory = null,
            string? outOfCharacterDirectory = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(postsDirectory);

            var htmlFilePaths = Directory
                .EnumerateFiles(postsDirectory, "*.html", SearchOption.TopDirectoryOnly);

            if (!string.IsNullOrWhiteSpace(asideDirectory) && Directory.Exists(asideDirectory))
            {
                htmlFilePaths = htmlFilePaths.Concat(
                    Directory.EnumerateFiles(asideDirectory, "*.html", SearchOption.TopDirectoryOnly));
            }

            if (!string.IsNullOrWhiteSpace(outOfCharacterDirectory) && Directory.Exists(outOfCharacterDirectory))
            {
                htmlFilePaths = htmlFilePaths.Concat(
                    Directory.EnumerateFiles(outOfCharacterDirectory, "*.html", SearchOption.TopDirectoryOnly));
            }

            return GetAdjustedPostTalliesFromSavedHtmlFiles(htmlFilePaths);
        }

        private static string TrimToPostBlock(string block)
        {
            const string postEndMarker = "</div><!-- 2 -->";
            var postEnd = block.IndexOf(postEndMarker, StringComparison.OrdinalIgnoreCase);
            return postEnd >= 0
                ? block[..(postEnd + postEndMarker.Length)]
                : block;
        }

        public static string GetShowAllThreadUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("A valid absolute URL is required.", nameof(url));
            }

            var queryValues = ParseQueryString(uri.Query);
            queryValues["msgpage"] = string.Empty;
            queryValues["show"] = "all";

            var builder = new UriBuilder(uri)
            {
                Query = string.Join("&", queryValues.Select(pair =>
                    pair.Value.Length == 0
                        ? Uri.EscapeDataString(pair.Key) + "="
                        : $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
            };

            return builder.Uri.ToString();
        }

        private static string CreatePostDocument(RpolThreadPost post, string threadTitle)
        {
            var title = $"{threadTitle} msg #{post.MessageNumber} - {post.Author}";
            var date = string.Join(" ", new[] { post.PostedDate, post.PostedTime }.Where(value => value.Length > 0));
            var metadata = $"""
                <p>
                    <strong>Message:</strong> #{post.MessageNumber}<br>
                    <strong>Author:</strong> {WebUtility.HtmlEncode(post.Author)}<br>
                    <strong>Date:</strong> {WebUtility.HtmlEncode(date)}
                </p>
                """;

            return CreateHtmlDocument(title, $"{metadata}{Environment.NewLine}{post.PostHtml}");
        }

        private static string CreateIndexDocument(RpolThreadSplitResult result)
        {
            var countItems = string.Join(
                Environment.NewLine,
                result.CountsByAuthor.Select(pair => $"<li>{WebUtility.HtmlEncode(pair.Key)}: {pair.Value}</li>"));

            var rows = string.Join(
                Environment.NewLine,
                result.Posts.Select(post =>
                {
                    var date = string.Join(" ", new[] { post.PostedDate, post.PostedTime }.Where(value => value.Length > 0));
                    return $"<tr><td>{post.MessageNumber}</td><td>{WebUtility.HtmlEncode(post.Author)}</td><td>{WebUtility.HtmlEncode(date)}</td><td>{post.BodyCharacterCount}</td><td><a href=\"{WebUtility.HtmlEncode(post.FileName)}\">{WebUtility.HtmlEncode(post.FileName)}</a></td></tr>";
                }));

            var body = $"""
                <h1>{WebUtility.HtmlEncode(result.ThreadTitle)} split posts</h1>
                <p>Source: <a href="{WebUtility.HtmlEncode(result.SourceUrl)}">{WebUtility.HtmlEncode(result.SourceUrl)}</a></p>
                <p>Total posts: {result.PostCount}</p>
                <ul>
                {countItems}
                </ul>
                <table border="1" cellspacing="0" cellpadding="4">
                    <thead>
                        <tr><th>Msg</th><th>Author</th><th>Date</th><th>Body chars</th><th>File</th></tr>
                    </thead>
                    <tbody>
                {rows}
                    </tbody>
                </table>
                """;

            return CreateHtmlDocument($"{result.ThreadTitle} split posts", body);
        }

        private static string CreateHtmlDocument(string title, string body)
        {
            return $"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8">
                    <title>{WebUtility.HtmlEncode(title)}</title>
                    <link rel="stylesheet" href="https://rpol.net/lib/responsive.css?1718502917">
                    <link rel="stylesheet" href="https://rpol.net/lib/themes/white.css?1703640629">
                    <link rel="stylesheet" href="https://rpol.net/lib/colours/light.css?1703640629">
                </head>
                <body>
                {body}
                </body>
                </html>
                """;
        }

        private static void ValidateStagedThreadExport(string stagingDirectory, RpolThreadSplitResult expected)
        {
            if (!File.Exists(RuntimePathUtility.CombineUnderBase(stagingDirectory, "_source-show-all.html")))
            {
                throw new InvalidOperationException("Staged RPOL thread export is missing the source HTML file.");
            }

            if (!File.Exists(RuntimePathUtility.CombineUnderBase(stagingDirectory, "index.html")))
            {
                throw new InvalidOperationException("Staged RPOL thread export is missing index.html.");
            }

            var manifestPath = RuntimePathUtility.CombineUnderBase(stagingDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException("Staged RPOL thread export is missing manifest.json.");
            }

            var manifest = JsonSerializer.Deserialize<RpolThreadSplitResult>(
                File.ReadAllText(manifestPath),
                ManifestJsonOptions);
            if (manifest is null
                || manifest.PostCount != expected.PostCount
                || manifest.Posts.Count != expected.Posts.Count)
            {
                throw new InvalidOperationException("Staged RPOL thread export manifest did not match the generated posts.");
            }

            foreach (var post in expected.Posts)
            {
                if (!File.Exists(RuntimePathUtility.CombineUnderBase(stagingDirectory, post.FileName)))
                {
                    throw new InvalidOperationException($"Staged RPOL thread export is missing post file '{post.FileName}'.");
                }
            }
        }

        private static void CommitStagedDirectory(string stagingDirectory, string outputDirectory)
        {
            var backupDirectory = CreateSiblingWorkingDirectory(outputDirectory, "backup");
            var movedExistingToBackup = false;

            try
            {
                if (Directory.Exists(outputDirectory))
                {
                    Directory.Move(outputDirectory, backupDirectory);
                    movedExistingToBackup = true;
                }

                Directory.Move(stagingDirectory, outputDirectory);
                TryDeleteDirectoryIfPresent(backupDirectory);
            }
            catch
            {
                if (movedExistingToBackup && Directory.Exists(backupDirectory))
                {
                    DeleteDirectoryIfPresent(outputDirectory);
                    Directory.Move(backupDirectory, outputDirectory);
                }

                throw;
            }
        }

        private static string CreateSiblingWorkingDirectory(string outputDirectory, string purpose)
        {
            var parentDirectory = Path.GetDirectoryName(outputDirectory);
            if (string.IsNullOrWhiteSpace(parentDirectory))
            {
                parentDirectory = Directory.GetCurrentDirectory();
            }

            Directory.CreateDirectory(parentDirectory);
            return Path.Combine(
                parentDirectory,
                $"{Path.GetFileName(outputDirectory)}.{purpose}-{Guid.NewGuid():N}");
        }

        private static void DeleteDirectoryIfPresent(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static void TryDeleteDirectoryIfPresent(string directory)
        {
            try
            {
                DeleteDirectoryIfPresent(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StartupLoggingUtility.Append("RPOL thread export cleanup", ex);
            }
        }

        private static string GetPlainText(string html)
        {
            var withLineBreaks = BreakRegex.Replace(html, "\n");
            withLineBreaks = RuleRegex.Replace(withLineBreaks, "\n---\n");
            var withoutTags = HtmlTagRegex.Replace(withLineBreaks, " ");
            var decoded = WebUtility.HtmlDecode(withoutTags);
            return WhitespaceRegex.Replace(decoded, " ").Trim();
        }

        private static string GetFileNameSlug(string value)
        {
            var slug = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return slug.Length == 0
                ? "unknown"
                : slug[..Math.Min(slug.Length, 48)];
        }

        private static IEnumerable<RpolThreadPost> GetThreadPostsFromHtmlFile(string htmlFilePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(htmlFilePath);

            var html = File.ReadAllText(htmlFilePath);
            return GetThreadPostsFromHtml(html);
        }

        private static IReadOnlyDictionary<string, int> GetPostCountsByAuthor(
            IEnumerable<RpolThreadPost> posts,
            Func<RpolThreadPost, bool> includePost)
        {
            return posts
                .Where(includePost)
                .GroupBy(post => post.Author, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.First().Author,
                    group => group.Count(),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyDictionary<string, int> GetAdjustedPostTallies(
            IEnumerable<RpolThreadPost> posts)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [BillworthTurgenAuthor] = 0
            };

            foreach (var post in posts)
            {
                AddCount(counts, post.Author);

                if (string.Equals(post.Author, NuandaAuthor, StringComparison.OrdinalIgnoreCase))
                {
                    AddCount(counts, NuandaNemereAuthor);
                    AddCount(counts, DungeonMasterAuthor);
                    continue;
                }

                if (string.Equals(post.Author, NuandaNemereAuthor, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(post.Author, BillworthTurgenAuthor, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(post.Author, ThurganNewlAuthor, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(post.Author, TheArchonAuthor, StringComparison.OrdinalIgnoreCase))
                {
                    AddCount(counts, DungeonMasterAuthor);
                }
            }

            return counts
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        private static void AddCount(IDictionary<string, int> counts, string author)
        {
            if (counts.TryGetValue(author, out var count))
            {
                counts[author] = count + 1;
                return;
            }

            counts[author] = 1;
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = part.IndexOf('=');
                var key = separatorIndex >= 0 ? part[..separatorIndex] : part;
                var value = separatorIndex >= 0 ? part[(separatorIndex + 1)..] : string.Empty;
                values[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
            }

            return values;
        }

        private static string GetThreadTitleFromUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                ? uri.AbsolutePath.Trim('/').Length == 0 ? uri.Host : uri.AbsolutePath.Trim('/').Replace('/', ' ')
                : "RPOL thread";
        }
    }
}
