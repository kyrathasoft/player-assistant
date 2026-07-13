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
            @"<span\s+class=(?<authorQuote>[""'])messageauthor(?:\s+you)?\k<authorQuote>[^>]*>(?<author>.*?)</span>[\s\S]*?<div\s+class=(?<bodyQuote>[""'])messagebody\k<bodyQuote>[^>]*>(?<body>.*?)</div>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex HtmlBreakRegex = new(
            @"<br\s*/?>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex MarkdownChapterHeadingRegex = new(
            @"^##\s+(?<title>Ch\s+(?<number>\d+)\b.*?)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AuthorPrefixedBulletRegex = new(
            @"^-\s+[\p{L}\p{M}'-]+(?:\s+[\p{L}\p{M}'-]+){0,3}:",
            RegexOptions.Compiled);
        private static readonly Regex WeakGeneratedBulletRegex = new(
            @"^-\s+[\p{L}\p{M}'-]+(?:\s+[\p{L}\p{M}'-]+){0,3}\s+(?:advances the scene|adds dialogue that clarifies the exchange|presses for answers or a decision|reveals a concern or reaction|contributes a new development to the scene|handles practical preparations for the party|reassures Kelpie that Morrow and her own magic protect her)\.$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private const int MaximumBulletTextLength = 140;
        private const int MaximumRetainedExistingBulletLength = 180;
        private const string NoInCharacterPostsBullet = "- No in-character posts were found in this chapter file.";

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

                if (author.Length == 0 || body.Length == 0 || !ContainsSummaryText(body))
                {
                    continue;
                }

                bullets.Add($"- {BuildPostSummary(author, body)}");
            }

            if (bullets.Count == 0)
            {
                bullets.Add(NoInCharacterPostsBullet);
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
            var merged = new List<string>(existingBullets.Where(ShouldRetainExistingBullet));
            var seen = new HashSet<string>(
                merged.Select(NormalizeBullet),
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

        private static string BuildPostSummary(string author, string body)
        {
            var normalizedBody = NormalizeSummaryInput(body);
            if (IsRoleAssignmentPost(body))
            {
                return $"{author} asked players to assume the roles of Caller, Quartermaster, Mapper, and Chronicler.";
            }

            TryBuildKnownSceneSummary(author, normalizedBody, out var summary);
            return summary;
        }

        private static bool IsRoleAssignmentPost(string body)
        {
            return body.Contains("Mapper:", StringComparison.OrdinalIgnoreCase)
                && body.Contains("Caller:", StringComparison.OrdinalIgnoreCase)
                && body.Contains("Quartermaster:", StringComparison.OrdinalIgnoreCase)
                && body.Contains("Chronicler:", StringComparison.OrdinalIgnoreCase);
        }

        private static string TruncateAtSentenceBoundary(string text, int maximumLength)
        {
            if (text.Length <= maximumLength)
            {
                return text;
            }

            var sentenceEnd = text
                .Take(Math.Min(text.Length, maximumLength))
                .Select((character, index) => new { character, index })
                .Where(item => item.character is '.' or '!' or '?')
                .Select(item => item.index)
                .LastOrDefault(-1);

            if (sentenceEnd >= 40)
            {
                return text[..(sentenceEnd + 1)].Trim();
            }

            return Truncate(text, maximumLength);
        }

        private static bool TryBuildKnownSceneSummary(string author, string body, out string summary)
        {
            var lowerBody = body.ToLowerInvariant();
            var firstName = GetFirstName(author);

            if (author.Equals("Dungeon Master", StringComparison.OrdinalIgnoreCase))
            {
                if (lowerBody.Contains("with this i can get", StringComparison.OrdinalIgnoreCase)
                    || (lowerBody.Contains("glove", StringComparison.OrdinalIgnoreCase)
                        && lowerBody.Contains("nuanda", StringComparison.OrdinalIgnoreCase)))
                {
                    summary = "Nuanda uses Jelenneth's glove to strengthen her divination.";
                    return true;
                }

                if (lowerBody.Contains("wariness", StringComparison.OrdinalIgnoreCase)
                    && lowerBody.Contains("respect", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Nuanda explains that locals treat her more with wariness than respect.";
                    return true;
                }

                if (lowerBody.Contains("proclamation", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("equanimity", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Nuanda reacts to Jelb's claim and explains the limits of her help.";
                    return true;
                }

                if (lowerBody.Contains("after breakfast", StringComparison.OrdinalIgnoreCase)
                    && lowerBody.Contains("hard biscuits", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Nuanda provisions the party with biscuits and honey for the road.";
                    return true;
                }

                if (lowerBody.Contains("please continue on the in-character chapter", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Dungeon Master moves play to the next in-character chapter thread.";
                    return true;
                }

                if (lowerBody.Contains("nuanda offers stew", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Dungeon Master introduces Nuanda's supper.";
                    return true;
                }

                if (lowerBody.Contains("the party leaves town", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Dungeon Master moves the party out of town.";
                    return true;
                }

                if (lowerBody.Contains("thac0", StringComparison.OrdinalIgnoreCase)
                    && lowerBody.Contains("boar", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Dungeon Master clarifies the boar's old-school combat statistics.";
                    return true;
                }

                if (lowerBody.Contains("please continue by switching", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Dungeon Master directs play to the next chapter thread.";
                    return true;
                }

                if (lowerBody.Contains("jelb is the chronicler", StringComparison.OrdinalIgnoreCase)
                    && lowerBody.Contains("wild boar", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Dungeon Master confirms Jelb as Chronicler and reports the boar is badly wounded.";
                    return true;
                }

                if (lowerBody.Contains("wild boar comes", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("barreling through the forest", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Dungeon Master introduces a wild boar encounter in the forest.";
                    return true;
                }

                if (lowerBody.Contains("urvan lands the killing blow", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("freshly killed wild boar", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Dungeon Master reports Urvan kills the boar and the party can harvest the carcass.";
                    return true;
                }

                if (lowerBody.Contains("crooked, moss-covered cottage", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("crest a rise", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Dungeon Master reveals Nuanda's cottage in the forest.";
                    return true;
                }

                if (lowerBody.Contains("welcome, travelers", StringComparison.OrdinalIgnoreCase)
                    && lowerBody.Contains("nuanda", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Dungeon Master introduces Nuanda welcoming the party and preparing stew.";
                    return true;
                }

                if (lowerBody.Contains("where is she from", StringComparison.OrdinalIgnoreCase)
                    && lowerBody.Contains("belongs to her", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Dungeon Master has Nuanda ask about the missing woman and possible personal links.";
                    return true;
                }
            }

            if (author.Equals("Kelpie Lawfuller", StringComparison.OrdinalIgnoreCase))
            {
                if (lowerBody.Contains("thank demetra", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("quiet prayer", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie gives thanks over Nuanda's shared meal.";
                    return true;
                }

                if (lowerBody.Contains("by the hearth", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("felt safe", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie takes comfort by Nuanda's hearth while staying mindful of danger.";
                    return true;
                }

                if (lowerBody.Contains("morning light", StringComparison.OrdinalIgnoreCase)
                    && lowerBody.Contains("breakfast", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie urges an early start after breakfast.";
                    return true;
                }

                if (lowerBody.Contains("amethyst", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("hwyanthemon", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie notes the components Nuanda needs for further magic.";
                    return true;
                }

                if (lowerBody.Contains("sore shoulders", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("blisters", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie reassures Urvan about the hardships of the road.";
                    return true;
                }

                if (lowerBody.Contains("kelpie keeps watch", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie keeps watch as the party moves on.";
                    return true;
                }

                if (lowerBody.Contains("kelpie takes the lead", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie takes the lead.";
                    return true;
                }

                if (lowerBody.Contains("first light of morning", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("take the fore", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie takes the lead as the party sets out toward Nuanda.";
                    return true;
                }

                if (lowerBody.Contains("forest pressed close", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie leads the party into the tense Darkwood approach.";
                    return true;
                }

                if (lowerBody.Contains("melee ended", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("carcass", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie assesses the slain boar after the fight.";
                    return true;
                }

                if (lowerBody.Contains("plan agreed", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("quick butchery", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie helps butcher the boar before the party moves on.";
                    return true;
                }

                if (lowerBody.Contains("relief to reach", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie reaches Nuanda's cottage with relief.";
                    return true;
                }

                if (lowerBody.Contains("easy familiarity", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("entirely at home", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie settles comfortably into Nuanda's rustic hospitality.";
                    return true;
                }

                if (lowerBody.Contains("preparing a meal", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("simple rhythm", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie relaxes into helping prepare the meal.";
                    return true;
                }

                if (lowerBody.Contains("neighbourly", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("helpin", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Kelpie explains that his help comes from neighborly duty rather than kinship.";
                    return true;
                }
            }

            if (author.Equals("Jelb Garrick", StringComparison.OrdinalIgnoreCase))
            {
                if (lowerBody.Contains("check on the bread", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("brooch", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Jelb helps with the bread and offers Jelenneth's brooch as a focus.";
                    return true;
                }

                if (lowerBody.Contains("spoke of himself", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("their conversation wound down", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Jelb shares his background and asks Nuanda about her magic.";
                    return true;
                }

                if (lowerBody.Contains("wards", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("fully to understand what nuanda was doing", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Jelb studies Nuanda's warding ritual.";
                    return true;
                }

                if (lowerBody.Contains("appearance of the cat", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("averted his eyes", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Jelb reacts uneasily to Morrow's bloody offering.";
                    return true;
                }

                if (lowerBody.Contains("looking out for what you need", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Jelb promises to seek the items Nuanda needs.";
                    return true;
                }

                if (lowerBody.Contains("bade his brother farewell", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Jelb says farewell to his brother and brings his dogs along.";
                    return true;
                }

                if (lowerBody.Contains("passed the other man his second dagger", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("second dagger", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Jelb lends Urvan a dagger for butchering the boar.";
                    return true;
                }

                if (lowerBody.Contains("rumors", StringComparison.OrdinalIgnoreCase)
                    && lowerBody.Contains("witch", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Jelb weighs rumors about Nuanda before accepting her help.";
                    return true;
                }
            }

            if (author.Equals("Urvan Hall", StringComparison.OrdinalIgnoreCase))
            {
                if (lowerBody.Contains("bottle of wine", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("settled in for the meal", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Urvan cautiously settles into Nuanda's supper.";
                    return true;
                }

                if (lowerBody.Contains("knight's brother", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("what had happened", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Urvan learns more about his knight's missing brother.";
                    return true;
                }

                if (lowerBody.Contains("jelbs deft repetition", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("instructions", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Urvan listens closely to Nuanda's ritual instructions.";
                    return true;
                }

                if (lowerBody.Contains("that is...remarkable", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("with this information", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Urvan recognizes that Nuanda's divination gives the party a lead.";
                    return true;
                }

                if (lowerBody.Contains("agreed", StringComparison.OrdinalIgnoreCase)
                    && lowerBody.Contains("pack", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Urvan readies himself to leave with Nuanda's information.";
                    return true;
                }

                if (lowerBody.Contains("quiet at first", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("appreciative of kelpie", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Urvan follows quietly while appreciating Kelpie's protection.";
                    return true;
                }

                if (lowerBody.Contains("sound plan", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("no knife", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Urvan agrees to help butcher the boar despite lacking a knife.";
                    return true;
                }

                if (lowerBody.Contains("fifty paces", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("sip water", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Urvan hangs back near Nuanda's cottage and gathers himself.";
                    return true;
                }

                if (lowerBody.Contains("not seem to be expecting", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("almost blush", StringComparison.OrdinalIgnoreCase))
                {
                    summary = "Urvan is flustered by Nuanda's welcome and request.";
                    return true;
                }
            }

            if (author.Equals("Nuanda", StringComparison.OrdinalIgnoreCase)
                && lowerBody.Contains("jelenneth", StringComparison.OrdinalIgnoreCase)
                && lowerBody.Contains("bandits", StringComparison.OrdinalIgnoreCase))
            {
                summary = "Nuanda recounts Jelenneth's abduction by bandits.";
                return true;
            }

            if (author.Equals("Nuanda", StringComparison.OrdinalIgnoreCase)
                && lowerBody.Contains("blanryde hills", StringComparison.OrdinalIgnoreCase))
            {
                summary = "Nuanda points the party toward the Blanryde Hills.";
                return true;
            }

            if (author.Equals("Nuanda", StringComparison.OrdinalIgnoreCase)
                && lowerBody.Contains("roads are risky", StringComparison.OrdinalIgnoreCase))
            {
                summary = "Nuanda warns the party to prefer roads over wilderness.";
                return true;
            }

            if (author.Equals("Nuanda", StringComparison.OrdinalIgnoreCase)
                && lowerBody.Contains("shares what she learned", StringComparison.OrdinalIgnoreCase))
            {
                summary = "Nuanda briefs the party.";
                return true;
            }

            if (author.Equals("Nuanda", StringComparison.OrdinalIgnoreCase)
                && (lowerBody.Contains("morrow", StringComparison.OrdinalIgnoreCase)
                    || lowerBody.Contains("protect me", StringComparison.OrdinalIgnoreCase)))
            {
                summary = "Nuanda explains that Morrow and her own magic make the cottage safe enough.";
                return true;
            }

            summary = BuildFallbackSceneSummary(firstName, lowerBody);
            return true;
        }

        private static string BuildFallbackSceneSummary(string actor, string lowerBody)
        {
            if (ContainsAny(lowerBody, "asks", " ask ", "?", "where is", "do you have", "can you", "would you"))
            {
                return $"{actor} asks a question that narrows the party's next choice.";
            }

            if (ContainsAny(lowerBody, "says", "said", "replies", "answered", "agreed", "offered", "explains", "tells"))
            {
                return $"{actor} answers or responds in a way that redirects the conversation.";
            }

            if (ContainsAny(lowerBody, "enters", "arrives", "appears", "comes", "crest", "reaches", "approach", "journey", "road", "path", "forest"))
            {
                return $"{actor} moves the party into a new location or situation.";
            }

            if (ContainsAny(lowerBody, "attack", "hit", "fight", "melee", "killing blow", "wounded", "down to", "initiative", "combat"))
            {
                return $"{actor} changes the stakes of the current danger.";
            }

            if (ContainsAny(lowerBody, "knife", "dagger", "butcher", "carcass", "stew", "meal", "food", "water", "preparing"))
            {
                return $"{actor} helps with food, gear, or travel preparations.";
            }

            if (ContainsAny(lowerBody, "thinks", "thought", "wonders", "worried", "hopeful", "relief", "quiet", "counsel"))
            {
                return $"{actor} shows how recent events are affecting them.";
            }

            return $"{actor} adds a concrete detail that changes the party's situation.";
        }

        private static bool ContainsAny(string text, params string[] fragments)
        {
            return fragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeSummaryInput(string text)
        {
            return WhitespaceRegex.Replace(text, " ").Trim();
        }

        private static string GetFirstName(string author)
        {
            var trimmed = author.Trim();
            if (trimmed.Equals("Dungeon Master", StringComparison.OrdinalIgnoreCase))
            {
                return "Dungeon Master";
            }

            var spaceIndex = trimmed.IndexOf(' ', StringComparison.Ordinal);
            return spaceIndex > 0
                ? trimmed[..spaceIndex]
                : trimmed;
        }

        private static bool ContainsSummaryText(string text)
        {
            return text.Any(char.IsLetterOrDigit);
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

        private static bool IsNoInCharacterPostsBullet(string bullet)
        {
            return string.Equals(
                NormalizeBullet(bullet),
                NormalizeBullet(NoInCharacterPostsBullet),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldRetainExistingBullet(string bullet)
        {
            return !IsNoInCharacterPostsBullet(bullet)
                && !AuthorPrefixedBulletRegex.IsMatch(bullet)
                && !WeakGeneratedBulletRegex.IsMatch(bullet)
                && NormalizeBullet(bullet).Length <= MaximumRetainedExistingBulletLength;
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
