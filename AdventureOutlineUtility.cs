using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal static class AdventureOutlineUtility
    {
        public const string FileName = "adventure-outline.md";
        public const string FallbackMarkdownUrl = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/Adventure+Outline";

        private static readonly Regex ChapterFileNameRegex = new(
            @"^ch-(?<number>\d+)\.html$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ChapterTitleRegex = new(
            @"<h1>\s*(?<title>Ch\s+\d+\s+-\s+.*?)\.?\s*</h1>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex MessageRegex = new(
            @"<span\s+class=([""'])messageauthor(?:\s+you)?\1[^>]*>(?<author>.*?)</span>[\s\S]*?<div\s+class=([""'])messagebody\2[^>]*>(?<body>.*?)</div>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlBreakRegex = new(
            @"<br\s*/?>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex MarkdownChapterHeadingRegex = new(
            @"^##\s+(?<title>Ch\s+(?<number>\d+)\b.*?)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private const int MaximumBulletTextLength = 280;

        public static async Task<bool> UpdateAdventureOutlineAsync(
            string icPostsDirectory,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            return await UpdateAdventureOutlineAsync(
                icPostsDirectory,
                outputPath,
                FallbackMarkdownUrl,
                MarkdownUtility.GetMarkdownFromUrlAsync,
                cancellationToken).ConfigureAwait(false);
        }

        internal static async Task<bool> UpdateAdventureOutlineAsync(
            string icPostsDirectory,
            string outputPath,
            string fallbackMarkdownUrl,
            Func<string, CancellationToken, Task<string>> fetchMarkdownAsync,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(icPostsDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(fallbackMarkdownUrl);
            ArgumentNullException.ThrowIfNull(fetchMarkdownAsync);

            IReadOnlyList<ChapterOutline> generatedChapters;
            try
            {
                generatedChapters = await LoadChapterOutlinesAsync(icPostsDirectory, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                generatedChapters = [];
            }

            var existingOutline = File.Exists(outputPath)
                ? await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false)
                : string.Empty;

            var updatedOutline = generatedChapters.Count == 0
                ? await GetFallbackOutlineMarkdownAsync(fallbackMarkdownUrl, fetchMarkdownAsync, cancellationToken)
                    .ConfigureAwait(false)
                : ComposeMergedOutline(generatedChapters, existingOutline);

            if (updatedOutline.Length == 0
                || string.Equals(existingOutline, updatedOutline, StringComparison.Ordinal))
            {
                return false;
            }

            await WriteOutlineAsync(outputPath, updatedOutline, cancellationToken).ConfigureAwait(false);
            return true;
        }

        private static async Task<string> GetFallbackOutlineMarkdownAsync(
            string fallbackMarkdownUrl,
            Func<string, CancellationToken, Task<string>> fetchMarkdownAsync,
            CancellationToken cancellationToken)
        {
            var markdown = await fetchMarkdownAsync(fallbackMarkdownUrl, cancellationToken)
                .ConfigureAwait(false);
            if (IsMarkdownFetchFailure(markdown))
            {
                return string.Empty;
            }

            return NormalizeFallbackMarkdown(markdown);
        }

        private static async Task WriteOutlineAsync(
            string outputPath,
            string outline,
            CancellationToken cancellationToken)
        {
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await AtomicFileUtility.WriteAllTextAsync(outputPath, outline, cancellationToken)
                .ConfigureAwait(false);
        }

        public static async Task<string> BuildAdventureOutlineAsync(
            string icPostsDirectory,
            CancellationToken cancellationToken = default)
        {
            var generatedChapters = await LoadChapterOutlinesAsync(icPostsDirectory, cancellationToken)
                .ConfigureAwait(false);

            return generatedChapters.Count == 0
                ? string.Empty
                : ComposeMergedOutline(generatedChapters, existingOutline: string.Empty);
        }

        private static async Task<IReadOnlyList<ChapterOutline>> LoadChapterOutlinesAsync(
            string icPostsDirectory,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(icPostsDirectory);

            if (!Directory.Exists(icPostsDirectory))
            {
                return [];
            }

            return await Task.Run(async () =>
            {
                var chapterFiles = Directory
                    .EnumerateFiles(icPostsDirectory, "ch-*.html", SearchOption.TopDirectoryOnly)
                    .Select(path => new
                    {
                        Path = path,
                        Match = ChapterFileNameRegex.Match(System.IO.Path.GetFileName(path))
                    })
                    .Where(file => file.Match.Success)
                    .Select(file => new
                    {
                        file.Path,
                        Number = int.Parse(file.Match.Groups["number"].Value)
                    })
                    .OrderBy(file => file.Number)
                    .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (chapterFiles.Length == 0)
                {
                    return (IReadOnlyList<ChapterOutline>)[];
                }

                var chapters = new List<ChapterOutline>();
                foreach (var chapterFile in chapterFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var html = await File.ReadAllTextAsync(chapterFile.Path, cancellationToken)
                        .ConfigureAwait(false);
                    chapters.Add(BuildChapterOutline(html, chapterFile.Number, chapterFile.Path));
                }

                return chapters;
            }, cancellationToken).ConfigureAwait(false);
        }

        private static ChapterOutline BuildChapterOutline(string html, int chapterNumber, string sourcePath)
        {
            var bullets = new List<string>();
            foreach (Match messageMatch in MessageRegex.Matches(html))
            {
                var author = GetPlainText(messageMatch.Groups["author"].Value);
                var body = GetPlainText(messageMatch.Groups["body"].Value);

                if (author.Length == 0 || body.Length == 0)
                {
                    continue;
                }

                bullets.Add($"- {author}: {Truncate(body, MaximumBulletTextLength)}");
            }

            if (bullets.Count == 0)
            {
                bullets.Add("- No in-character posts were found in this chapter file.");
            }

            return new ChapterOutline(
                chapterNumber,
                GetChapterTitle(html, chapterNumber),
                sourcePath,
                bullets);
        }

        private static string ComposeMergedOutline(
            IReadOnlyList<ChapterOutline> generatedChapters,
            string existingOutline)
        {
            var existingChapters = ParseExistingChapterSections(existingOutline);
            var existingChaptersByNumber = existingChapters
                .Where(chapter => chapter.Number > 0)
                .GroupBy(chapter => chapter.Number)
                .ToDictionary(group => group.Key, group => group.First());
            var generatedChapterNumbers = generatedChapters
                .Select(chapter => chapter.Number)
                .ToHashSet();

            var builder = new StringBuilder();
            builder.AppendLine("# Adventure Outline");
            builder.AppendLine();
            builder.AppendLine("- Source files inspected:");

            foreach (var chapter in generatedChapters)
            {
                builder.AppendLine($"  - `{NormalizeMarkdownPath(chapter.SourcePath)}`");
            }

            foreach (var generatedChapter in generatedChapters)
            {
                builder.AppendLine();
                builder.AppendLine($"## {generatedChapter.Title}");
                builder.AppendLine();

                var mergedBullets = MergeBullets(
                    existingChaptersByNumber.TryGetValue(generatedChapter.Number, out var existingChapter)
                        ? existingChapter.Bullets
                        : [],
                    generatedChapter.Bullets);

                foreach (var bullet in mergedBullets)
                {
                    builder.AppendLine(bullet);
                }
            }

            foreach (var existingChapter in existingChapters.Where(chapter => !generatedChapterNumbers.Contains(chapter.Number)))
            {
                builder.AppendLine();
                builder.AppendLine(existingChapter.Heading);
                builder.AppendLine();

                foreach (var bullet in existingChapter.Bullets)
                {
                    builder.AppendLine(bullet);
                }
            }

            return builder.ToString();
        }

        private static IReadOnlyList<string> MergeBullets(
            IReadOnlyList<string> existingBullets,
            IReadOnlyList<string> generatedBullets)
        {
            var merged = new List<string>(existingBullets);
            var seen = new HashSet<string>(
                existingBullets.Select(NormalizeBullet),
                StringComparer.OrdinalIgnoreCase);

            foreach (var generatedBullet in generatedBullets)
            {
                if (seen.Add(NormalizeBullet(generatedBullet)))
                {
                    merged.Add(generatedBullet);
                }
            }

            return merged;
        }

        private static IReadOnlyList<ExistingChapterSection> ParseExistingChapterSections(string outline)
        {
            if (string.IsNullOrWhiteSpace(outline))
            {
                return [];
            }

            var sections = new List<ExistingChapterSection>();
            string? currentHeading = null;
            var currentNumber = 0;
            var currentBullets = new List<string>();

            foreach (var line in outline.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var headingMatch = MarkdownChapterHeadingRegex.Match(line);
                if (headingMatch.Success)
                {
                    AddCurrentSection();
                    currentHeading = line.TrimEnd();
                    currentNumber = int.Parse(headingMatch.Groups["number"].Value);
                    currentBullets = [];
                    continue;
                }

                if (currentHeading is not null && line.StartsWith("- ", StringComparison.Ordinal))
                {
                    currentBullets.Add(line.TrimEnd());
                }
            }

            AddCurrentSection();
            return sections;

            void AddCurrentSection()
            {
                if (currentHeading is null)
                {
                    return;
                }

                sections.Add(new ExistingChapterSection(currentNumber, currentHeading, currentBullets.ToArray()));
            }
        }

        private static string GetChapterTitle(string html, int chapterNumber)
        {
            var titleMatch = ChapterTitleRegex.Match(html);
            return titleMatch.Success
                ? GetPlainText(titleMatch.Groups["title"].Value)
                : $"Ch {chapterNumber}";
        }

        private static string GetPlainText(string html)
        {
            var withLineBreaks = HtmlBreakRegex.Replace(html, " ");
            var withoutTags = HtmlTagRegex.Replace(withLineBreaks, " ");
            var decoded = WebUtility.HtmlDecode(withoutTags)
                .Replace('\u00A0', ' ')
                .Replace('\u202F', ' ');

            return WhitespaceRegex.Replace(decoded, " ").Trim();
        }

        private static string Truncate(string text, int maximumLength)
        {
            if (text.Length <= maximumLength)
            {
                return text;
            }

            var trimmed = text[..maximumLength].TrimEnd();
            return $"{trimmed}...";
        }

        private static string NormalizeMarkdownPath(string path)
        {
            return path.Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
        }

        private static string NormalizeBullet(string bullet)
        {
            var text = bullet.StartsWith("- ", StringComparison.Ordinal)
                ? bullet[2..]
                : bullet;
            return WhitespaceRegex.Replace(text, " ").Trim();
        }

        private static bool IsMarkdownFetchFailure(string markdown)
        {
            return string.IsNullOrWhiteSpace(markdown)
                || markdown.StartsWith(MarkdownUtility.InvalidUrlMessage, StringComparison.Ordinal)
                || markdown.StartsWith(MarkdownUtility.UnresolvedUrlMessage, StringComparison.Ordinal);
        }

        private static string NormalizeFallbackMarkdown(string markdown)
        {
            markdown = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (markdown.Length == 0)
            {
                return string.Empty;
            }

            return markdown + Environment.NewLine;
        }

        private sealed record ChapterOutline(
            int Number,
            string Title,
            string SourcePath,
            IReadOnlyList<string> Bullets);

        private sealed record ExistingChapterSection(
            int Number,
            string Heading,
            IReadOnlyList<string> Bullets);
    }
}
