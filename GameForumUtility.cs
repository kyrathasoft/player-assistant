using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal sealed record GameForumChapterDownload(
        string LinkText,
        string Prefix,
        string FilePath,
        bool Downloaded,
        string? ErrorMessage = null);

    internal sealed record GameForumPostDownload(
        string LinkText,
        string FilePath,
        bool Downloaded,
        string? ErrorMessage = null);

    internal sealed record DieRollEntry(string RollId, string Line);

    internal sealed record TheCastLoginInfo(
        [property: JsonPropertyName("Character Name")] string CharacterName,
        [property: JsonPropertyName("Posts")] int? Posts,
        [property: JsonPropertyName("Tag")] string Tag,
        [property: JsonPropertyName("Last Visited")] string? LastVisited,
        [property: JsonPropertyName("Last Post")] string LastPost);

    internal static class GameForumUtility
    {
        private static readonly TimeSpan ChapterHtmlRefreshInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan GameIntroHtmlRefreshInterval = TimeSpan.FromHours(24);
        private static readonly TimeSpan RegionalMapRefreshInterval = TimeSpan.FromHours(1);
        internal const string RegionalMapUrl = "https://bryanmiller.us/scarlethorizons/maps/northernreaches.png";
        private const long MinimumRegionalMapFileSizeBytes = 500_000;
        private static readonly TimeSpan TheCastHtmlRefreshInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan RpolDownloadAttemptInterval = TimeSpan.FromMilliseconds(5000);
        private static readonly SemaphoreSlim RpolDownloadAttemptSemaphore = new(1, 1);
        private static readonly Regex TheCastRowSplitRegex = new(@"(?=<div class=(['""])hovershow\1>)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TheCastCellRegex = new(@"<div class=(['""])(?=[^'""]*\btd\b)[^'""]*\1[^>]*>(?<cell>[\s\S]*?)</div>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HiddenCellContentRegex = new(@"<div\b[^>]*class=['""][^'""]*\bhidden\b[^'""]*['""][\s\S]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlBreakRegex = new(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlParagraphRegex = new(@"<p\b[^>]*>(?<content>[\s\S]*?)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex DieRollLineRegex = new(
            @"^(?<line>(?:\d{1,2}:\d{2},\s+)?[^:\r\n]+:\s+.+?\s+rolled\s+.+?\s+using\s+.+?\.(?:\s+.*?)?\s+[–-]\s+\[roll=(?<rollId>\d+\.\d+\.\d+)\])$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DieRollWithoutIdRegex = new(
            @"^(?<line>(?:\d{1,2}:\d{2},\s+)?[^:\r\n]+:\s+.+?\s+rolled\s+.+?\s+using\s+.+?\.(?:\s+.*)?)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static DateTimeOffset? _lastRpolDownloadAttemptUtc;

        public static async Task<string[]> GetChapterLinkPrefixesAsync(CancellationToken cancellationToken = default)
        {
            var hyperlinks = await HtmlUtility.GetRpolGameHyperlinksAsync(cancellationToken);

            return hyperlinks
                .Select(link => GetChapterLinkPrefix(link.Text))
                .Where(prefix => prefix.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static Task<string> GetRpolHtmlWithRateLimitAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);
            return GetHtmlFromUrlWithRateLimitAsync(url, cancellationToken);
        }

        public static async Task<GameForumChapterDownload[]> DownloadChapterHtmlAsync(
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            var hyperlinks = await HtmlUtility.GetRpolGameHyperlinksAsync(cancellationToken);
            return await DownloadChapterHtmlAsync(hyperlinks, outputDirectory, cancellationToken);
        }

        public static async Task<GameForumChapterDownload[]> DownloadChapterHtmlAsync(
            IEnumerable<Hyperlink> hyperlinks,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            return await DownloadChapterHtmlAsync(
                hyperlinks,
                outputDirectory,
                GetHtmlFromUrlWithRateLimitAsync,
                cancellationToken);
        }

        internal static async Task<GameForumChapterDownload[]> DownloadChapterHtmlAsync(
            IEnumerable<Hyperlink> hyperlinks,
            string outputDirectory,
            Func<string, CancellationToken, Task<string>> htmlFetcher,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(hyperlinks);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
            ArgumentNullException.ThrowIfNull(htmlFetcher);

            Directory.CreateDirectory(outputDirectory);

            var downloads = new List<GameForumChapterDownload>();

            foreach (var hyperlink in hyperlinks.Where(link => link.Text.StartsWith("Ch ", StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var prefix = GetChapterLinkPrefix(hyperlink.Text);
                if (prefix.Length == 0)
                {
                    continue;
                }

                var filePath = Path.Combine(outputDirectory, $"{GetFileNameSlug(prefix)}.html");
                try
                {
                    var downloaded = false;

                    if (ShouldDownload(filePath))
                    {
                        var showAllUrl = RpolThreadPostUtility.GetShowAllThreadUrl(hyperlink.Url);
                        var html = await htmlFetcher(showAllUrl, cancellationToken);
                        await AtomicFileUtility.WriteAllTextAsync(filePath, html, cancellationToken);
                        FileDownloadCounters.AddCompletedDownload(filePath);
                        downloaded = true;
                    }

                    downloads.Add(new GameForumChapterDownload(hyperlink.Text, prefix, filePath, downloaded));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    downloads.Add(new GameForumChapterDownload(hyperlink.Text, prefix, filePath, false, ex.Message));
                }
            }

            return downloads.ToArray();
        }

        public static async Task<GameForumPostDownload[]> DownloadAsideHtmlAsync(
            IEnumerable<Hyperlink> hyperlinks,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            return await DownloadAsideHtmlAsync(
                hyperlinks,
                outputDirectory,
                GetHtmlFromUrlWithRateLimitAsync,
                cancellationToken);
        }

        internal static async Task<GameForumPostDownload[]> DownloadAsideHtmlAsync(
            IEnumerable<Hyperlink> hyperlinks,
            string outputDirectory,
            Func<string, CancellationToken, Task<string>> htmlFetcher,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(hyperlinks);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
            ArgumentNullException.ThrowIfNull(htmlFetcher);

            Directory.CreateDirectory(outputDirectory);

            var asideHyperlinks = hyperlinks
                .Where(link => IsAsideLinkText(link.Text))
                .DistinctBy(link => link.Url, StringComparer.OrdinalIgnoreCase);
            var downloads = new List<GameForumPostDownload>();

            foreach (var hyperlink in asideHyperlinks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var filePath = Path.Combine(outputDirectory, $"{GetFileName(hyperlink.Text)}.html");
                try
                {
                    var showAllUrl = RpolThreadPostUtility.GetShowAllThreadUrl(hyperlink.Url);
                    var html = HtmlUtility.RemoveImagesFromHtml(
                        await htmlFetcher(showAllUrl, cancellationToken));
                    var downloaded = !File.Exists(filePath) || !string.Equals(
                        await File.ReadAllTextAsync(filePath, cancellationToken),
                        html,
                        StringComparison.Ordinal);

                    if (downloaded)
                    {
                        await AtomicFileUtility.WriteAllTextAsync(
                            filePath,
                            html,
                            cancellationToken);
                        FileDownloadCounters.AddCompletedDownload(filePath);
                    }

                    downloads.Add(new GameForumPostDownload(hyperlink.Text, filePath, downloaded));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    downloads.Add(new GameForumPostDownload(hyperlink.Text, filePath, false, ex.Message));
                }
            }

            return downloads.ToArray();
        }

        private static bool IsAsideLinkText(string linkText)
        {
            return linkText.StartsWith("Aside -", StringComparison.Ordinal)
                || linkText.StartsWith("Notice: Aside -", StringComparison.Ordinal);
        }

        public static async Task<GameForumPostDownload[]> DownloadOutOfCharacterHtmlAsync(
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            var hyperlinks = await HtmlUtility.GetRpolGameHyperlinksAsync(cancellationToken);
            return await DownloadOutOfCharacterHtmlAsync(hyperlinks, outputDirectory, cancellationToken);
        }

        public static async Task<GameForumPostDownload[]> DownloadOutOfCharacterHtmlAsync(
            IEnumerable<Hyperlink> hyperlinks,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

            Directory.CreateDirectory(outputDirectory);

            var downloads = new List<GameForumPostDownload>();

            foreach (var hyperlink in hyperlinks.Where(link => link.Text.StartsWith("OOC", StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var filePath = Path.Combine(outputDirectory, $"{GetFileName(hyperlink.Text)}.html");
                try
                {
                    var showAllUrl = RpolThreadPostUtility.GetShowAllThreadUrl(hyperlink.Url);
                    var html = await GetHtmlFromUrlWithRateLimitAsync(showAllUrl, cancellationToken);
                    var downloaded = !File.Exists(filePath) || !string.Equals(
                        await File.ReadAllTextAsync(filePath, cancellationToken),
                        html,
                        StringComparison.Ordinal);

                    if (downloaded)
                    {
                        await AtomicFileUtility.WriteAllTextAsync(filePath, html, cancellationToken);
                        FileDownloadCounters.AddCompletedDownload(filePath);
                    }

                    downloads.Add(new GameForumPostDownload(hyperlink.Text, filePath, downloaded));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    downloads.Add(new GameForumPostDownload(hyperlink.Text, filePath, false, ex.Message));
                }
            }

            return downloads.ToArray();
        }

        public static async Task<GameForumPostDownload> DownloadDieRollsHtmlAsync(
            IEnumerable<Hyperlink> hyperlinks,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(hyperlinks);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

            Directory.CreateDirectory(outputDirectory);

            const string linkText = "Die Roller";
            var filePath = Path.Combine(outputDirectory, "dice-rolls.html");

            try
            {
                var dieRollerUrl = await GetDieRollerUrlAsync(hyperlinks, cancellationToken);
                var sourceHtml = await GetHtmlFromUrlWithRateLimitAsync(dieRollerUrl, cancellationToken);
                var fileExists = File.Exists(filePath);
                var appendedCount = await AppendNewDieRollEntriesAsync(sourceHtml, filePath, cancellationToken);
                var downloaded = appendedCount > 0 || !fileExists;

                if (downloaded)
                {
                    FileDownloadCounters.AddCompletedDownload(filePath);
                }

                return new GameForumPostDownload(linkText, filePath, downloaded);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new GameForumPostDownload(linkText, filePath, false, ex.Message);
            }
        }

        public static async Task<GameForumPostDownload?> DownloadHouseRulesHtmlAsync(
            IEnumerable<Hyperlink> hyperlinks,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            return await DownloadHouseRulesHtmlAsync(
                hyperlinks,
                outputDirectory,
                GetHtmlFromUrlWithRateLimitAsync,
                cancellationToken);
        }

        internal static async Task<GameForumPostDownload?> DownloadHouseRulesHtmlAsync(
            IEnumerable<Hyperlink> hyperlinks,
            string outputDirectory,
            Func<string, CancellationToken, Task<string>> htmlFetcher,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(hyperlinks);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
            ArgumentNullException.ThrowIfNull(htmlFetcher);

            Directory.CreateDirectory(outputDirectory);

            var hyperlink = hyperlinks.FirstOrDefault(link =>
                link.Text.Contains("House Rules", StringComparison.Ordinal));
            if (hyperlink is null)
            {
                return null;
            }

            var filePath = Path.Combine(outputDirectory, "house-rules.html");
            try
            {
                var downloaded = false;

                if (ShouldDownload(filePath))
                {
                    var showAllUrl = RpolThreadPostUtility.GetShowAllThreadUrl(hyperlink.Url);
                    var html = await htmlFetcher(showAllUrl, cancellationToken);
                    await AtomicFileUtility.WriteAllTextAsync(filePath, html, cancellationToken);
                    FileDownloadCounters.AddCompletedDownload(filePath);
                    downloaded = true;
                }

                return new GameForumPostDownload(hyperlink.Text, filePath, downloaded);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new GameForumPostDownload(hyperlink.Text, filePath, false, ex.Message);
            }
        }

        public static async Task<GameForumPostDownload> DownloadGameIntroHtmlAsync(
            string gameIntroUrl,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(gameIntroUrl);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

            Directory.CreateDirectory(outputDirectory);

            const string linkText = "Game Intro";
            var filePath = Path.Combine(outputDirectory, "game-intro.html");
            try
            {
                var downloaded = false;

                if (ShouldDownload(filePath, GameIntroHtmlRefreshInterval))
                {
                    var html = await GetHtmlFromUrlWithRateLimitAsync(gameIntroUrl, cancellationToken);
                    await AtomicFileUtility.WriteAllTextAsync(filePath, html, cancellationToken);
                    FileDownloadCounters.AddCompletedDownload(filePath);
                    downloaded = true;
                }

                return new GameForumPostDownload(linkText, filePath, downloaded);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new GameForumPostDownload(linkText, filePath, false, ex.Message);
            }
        }

        public static async Task<GameForumPostDownload> DownloadTheCastHtmlAsync(
            string theCastUrl,
            string outputDirectory,
            bool forceDownload,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(theCastUrl);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

            Directory.CreateDirectory(outputDirectory);

            const string linkText = "The Cast";
            var filePath = Path.Combine(outputDirectory, "the-cast.html");
            try
            {
                var downloaded = false;

                if (forceDownload || ShouldDownload(filePath, TheCastHtmlRefreshInterval))
                {
                    var html = await GetHtmlFromUrlWithRateLimitAsync(theCastUrl, cancellationToken);
                    await AtomicFileUtility.WriteAllTextAsync(filePath, html, cancellationToken);
                    FileDownloadCounters.AddCompletedDownload(filePath);
                    downloaded = true;
                }

                return new GameForumPostDownload(linkText, filePath, downloaded);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new GameForumPostDownload(linkText, filePath, false, ex.Message);
            }
        }

        public static Task<GameForumPostDownload> DownloadTheCastHtmlAsync(
            string theCastUrl,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            return DownloadTheCastHtmlAsync(theCastUrl, outputDirectory, forceDownload: false, cancellationToken);
        }

        public static async Task<GameForumPostDownload> DownloadRegionalMapAsync(
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

            Directory.CreateDirectory(outputDirectory);

            const string linkText = "Regional Map";
            var filePath = Path.Combine(outputDirectory, "northernreaches.png");

            if (!ShouldDownloadRegionalMap(filePath))
            {
                return new GameForumPostDownload(linkText, filePath, false);
            }

            try
            {
                await ImageDownloadUtility.DownloadImageFileAsPngAsync(
                    RegionalMapUrl,
                    filePath,
                    cancellationToken);

                return new GameForumPostDownload(linkText, filePath, true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new GameForumPostDownload(linkText, filePath, false, ex.Message);
            }
        }

        internal static bool ShouldDownloadRegionalMap(string filePath)
        {
            return ShouldDownload(filePath, RegionalMapRefreshInterval)
                || !File.Exists(filePath)
                || new FileInfo(filePath).Length < MinimumRegionalMapFileSizeBytes
                || !ImageDownloadUtility.HasVisiblePixels(filePath);
        }

        internal static DieRollEntry[] ExtractDieRollEntries(string html)
        {
            ArgumentNullException.ThrowIfNull(html);

            var entries = new List<DieRollEntry>();
            var paragraphMatches = HtmlParagraphRegex.Matches(html);

            if (paragraphMatches.Count > 0)
            {
                foreach (Match paragraphMatch in paragraphMatches)
                {
                    AddDieRollEntry(entries, paragraphMatch.Groups["content"].Value, allowSyntheticId: true);
                }
            }
            else
            {
                foreach (var line in HtmlBreakRegex.Replace(html, Environment.NewLine)
                             .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    AddDieRollEntry(entries, line, allowSyntheticId: false);
                }
            }

            return entries.DistinctBy(entry => entry.RollId, StringComparer.Ordinal).ToArray();
        }

        internal static async Task<int> AppendNewDieRollEntriesAsync(
            string sourceHtml,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sourceHtml);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

            var existingEntries = Array.Empty<DieRollEntry>();
            if (File.Exists(outputPath))
            {
                var existingHtml = await File.ReadAllTextAsync(outputPath, cancellationToken);
                existingEntries = ExtractDieRollEntries(existingHtml);
            }

            var savedRollIds = new HashSet<string>(
                existingEntries.Select(entry => entry.RollId),
                StringComparer.Ordinal);
            var newEntries = ExtractDieRollEntries(sourceHtml)
                .Where(entry => savedRollIds.Add(entry.RollId))
                .ToArray();

            if (!File.Exists(outputPath) || newEntries.Length > 0)
            {
                var outputDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                var allEntries = existingEntries.Concat(newEntries).ToArray();
                await AtomicFileUtility.WriteAllTextAsync(
                    outputPath,
                    BuildDieRollHtml(allEntries),
                    cancellationToken);
            }

            return newEntries.Length;
        }

        internal static string NormalizeDieRollSnapshotHtml(string sourceHtml)
        {
            ArgumentNullException.ThrowIfNull(sourceHtml);
            var entries = ExtractDieRollEntries(sourceHtml);
            if (entries.Length == 0)
            {
                throw new InvalidOperationException("The RPOL Dice Roller page did not contain any recognizable saved rolls.");
            }

            return BuildDieRollHtml(entries);
        }

        public static async Task<TheCastLoginInfo[]> WriteTheCastLoginInfoJsonAsync(
            string theCastHtmlPath,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(theCastHtmlPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

            var html = await File.ReadAllTextAsync(theCastHtmlPath, cancellationToken);
            var loginInfo = GetTheCastLoginInfoFromHtml(html);
            var outputDirectory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await AtomicFileUtility.WriteAllTextAsync(
                outputPath,
                JsonSerializer.Serialize(loginInfo, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);

            return loginInfo;
        }

        public static TheCastLoginInfo[] GetTheCastLoginInfoFromHtml(string html)
        {
            ArgumentNullException.ThrowIfNull(html);

            var rows = new List<TheCastLoginInfo>();

            foreach (var rowHtml in TheCastRowSplitRegex
                .Split(html)
                .Where(part =>
                    part.StartsWith("<div class='hovershow'>", StringComparison.OrdinalIgnoreCase)
                    || part.StartsWith("<div class=\"hovershow\">", StringComparison.OrdinalIgnoreCase)))
            {
                var cells = TheCastCellRegex
                    .Matches(rowHtml)
                    .Select(match => GetCellText(match.Groups["cell"].Value))
                    .Where(cell => cell.Length > 0)
                    .ToArray();

                if (cells.Length == 5)
                {
                    rows.Add(new TheCastLoginInfo(
                        cells[0],
                        ParseNullableInt(cells[1]),
                        cells[2],
                        cells[3],
                        cells[4]));
                }
                else if (cells.Length == 4)
                {
                    rows.Add(new TheCastLoginInfo(
                        cells[0],
                        ParseNullableInt(cells[1]),
                        cells[2],
                        null,
                        cells[3]));
                }
            }

            return rows.ToArray();
        }

        private static async Task<string> GetRequiredLinkUrlAsync(
            string pageUrl,
            string linkText,
            CancellationToken cancellationToken)
        {
            var html = await GetHtmlFromUrlWithRateLimitAsync(pageUrl, cancellationToken);
            var hyperlinks = HtmlUtility.GetHyperlinksFromHtml(html, pageUrl);
            var hyperlink = hyperlinks.FirstOrDefault(link =>
                string.Equals(link.Text, linkText, StringComparison.OrdinalIgnoreCase));

            return hyperlink?.Url
                ?? throw new InvalidOperationException($"Link '{linkText}' was not found at {pageUrl}.");
        }

        private static async Task<string> GetDieRollerUrlAsync(
            IEnumerable<Hyperlink> hyperlinks,
            CancellationToken cancellationToken)
        {
            var dieRollerUrl = hyperlinks
                .FirstOrDefault(link => string.Equals(link.Text, "Die Roller", StringComparison.OrdinalIgnoreCase))
                ?.Url;

            return !string.IsNullOrWhiteSpace(dieRollerUrl)
                ? dieRollerUrl
                : await GetRequiredLinkUrlAsync(AppSettingsUtility.GameForumUrl, "Die Roller", cancellationToken);
        }

        private static string GetChapterLinkPrefix(string linkText)
        {
            if (!linkText.StartsWith("Ch ", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var hyphenIndex = linkText.IndexOf('-');
            return hyphenIndex >= 0
                ? linkText[..hyphenIndex].Trim()
                : string.Empty;
        }

        private static string NormalizeDieRollLine(string line)
        {
            line = HtmlBreakRegex.Replace(line, " ");
            line = WebUtility.HtmlDecode(line)
                .Replace('\u00A0', ' ')
                .Replace('\u202F', ' ');
            line = HtmlTagRegex.Replace(line, string.Empty);

            return WhitespaceRegex.Replace(
                    line
                        .Replace("â€“", "–")
                        .Replace(" - [roll=", " – [roll=")
                        .Trim(),
                    " ")
                .Trim();
        }

        private static void AddDieRollEntry(
            List<DieRollEntry> entries,
            string rawContent,
            bool allowSyntheticId)
        {
            var formattedLine = NormalizeDieRollLine(rawContent);
            if (formattedLine.Length == 0)
            {
                return;
            }

            var match = DieRollLineRegex.Match(formattedLine);
            if (match.Success)
            {
                entries.Add(new DieRollEntry(
                    match.Groups["rollId"].Value,
                    match.Groups["line"].Value));
                return;
            }

            if (!allowSyntheticId
                || formattedLine.Contains("[roll=", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            match = DieRollWithoutIdRegex.Match(formattedLine);
            if (!match.Success)
            {
                return;
            }

            var line = match.Groups["line"].Value;
            entries.Add(new DieRollEntry(
                CreateSyntheticRollId(line),
                line));
        }

        private static string CreateSyntheticRollId(string line)
        {
            var hash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(line));
            return string.Join(
                '.',
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(0, 4)),
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(4, 4)),
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(8, 4)));
        }

        private static string BuildDieRollHtml(IEnumerable<DieRollEntry> entries)
        {
            var encodedLines = entries
                .Select(entry => entry.Line.Contains("[roll=", StringComparison.OrdinalIgnoreCase)
                    ? entry.Line
                    : $"{entry.Line} – [roll={entry.RollId}]")
                .Select(WebUtility.HtmlEncode)
                .ToArray();
            var preformattedBody = encodedLines.Length == 0
                ? string.Empty
                : string.Join(Environment.NewLine, encodedLines) + Environment.NewLine;

            return
                $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8">
                    <meta name="player-assistant-snapshot" content="dice-rolls">
                    <title>Scarlet Horizons - Die Rolls</title>
                </head>
                <body>
                    <h1>Die Rolls</h1>
                    <pre>{{preformattedBody}}</pre>
                </body>
                </html>
                """;
        }

        private static bool ShouldDownload(string filePath)
        {
            return ShouldDownload(filePath, ChapterHtmlRefreshInterval);
        }

        private static bool ShouldDownload(string filePath, TimeSpan refreshInterval)
        {
            if (!File.Exists(filePath))
            {
                return true;
            }

            var elapsed = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(filePath);
            return elapsed >= refreshInterval;
        }

        private static async Task<string> GetHtmlFromUrlWithRateLimitAsync(
            string url,
            CancellationToken cancellationToken)
        {
            await WaitForRpolDownloadSlotAsync(cancellationToken);
            return await HtmlUtility.GetHtmlFromUrlAsync(url, cancellationToken);
        }

        private static async Task WaitForRpolDownloadSlotAsync(CancellationToken cancellationToken)
        {
            await RpolDownloadAttemptSemaphore.WaitAsync(cancellationToken);
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (_lastRpolDownloadAttemptUtc is { } lastAttempt)
                {
                    var nextAttempt = lastAttempt + RpolDownloadAttemptInterval;
                    var delay = nextAttempt - now;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken);
                        now = DateTimeOffset.UtcNow;
                    }
                }

                _lastRpolDownloadAttemptUtc = now;
            }
            finally
            {
                RpolDownloadAttemptSemaphore.Release();
            }
        }

        private static string GetFileNameSlug(string value)
        {
            var characters = value.ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray();
            var slug = string.Join(
                "-",
                new string(characters).Split('-', StringSplitOptions.RemoveEmptyEntries));

            return slug.Length == 0
                ? "unknown"
                : slug[..Math.Min(slug.Length, 48)];
        }

        private static string GetFileName(string value)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var characters = value
                .Select(character => invalidCharacters.Contains(character) ? '-' : character)
                .ToArray();
            var fileName = new string(characters).Trim();

            return fileName.Length == 0
                ? "unknown"
                : fileName;
        }

        private static string GetDownloadFileName(string url, string fallbackFileName)
        {
            var fileName = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                ? Path.GetFileName(uri.LocalPath)
                : Path.GetFileName(url);

            fileName = GetFileName(fileName);
            return fileName.Equals("unknown", StringComparison.Ordinal)
                ? fallbackFileName
                : fileName;
        }

        private static string GetCellText(string html)
        {
            var withoutHiddenContent = HiddenCellContentRegex.Replace(html, string.Empty);
            var withoutTags = HtmlTagRegex.Replace(withoutHiddenContent, " ");
            var decoded = WebUtility.HtmlDecode(withoutTags);

            return WhitespaceRegex.Replace(decoded, " ").Trim();
        }

        private static int? ParseNullableInt(string value)
        {
            return int.TryParse(value, out var number)
                ? number
                : null;
        }
    }
}
