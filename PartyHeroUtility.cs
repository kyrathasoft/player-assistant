using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal sealed record PartyHeroSheet(
        string Name,
        string? TokenImagePath,
        string Level,
        string CharacterClass,
        string HitPoints,
        string CharacterSheetText,
        int? XpTotal = null,
        string? AccountId = null,
        IReadOnlyList<string>? Aliases = null);

    internal static class PartyHeroUtility
    {
        private static readonly Regex FrontMatterRegex = new(@"\A---\s*\r?\n.*?\r?\n---\s*", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex ObsidianImageRegex = new(@"!\[\[(?<target>[^\]#|]+)(?:[#|][^\]]*)?\]\]", RegexOptions.Compiled);
        private static readonly Regex MarkdownLinkRegex = new(@"\[\[(?<target>[^\]|]+)(?:\|(?<alias>[^\]]+))?\]\]", RegexOptions.Compiled);
        private static readonly Regex ClassLevelLineRegex = new(@"^Class\s*&\s*Level\s+(?<value>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AttainedLevelRegex = new(@"\bAttained\s+level\s+(?<level>\d+)\s+(?<class>[A-Za-z][A-Za-z /\-]+?)(?:\s+after|\s+\d|,|;|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static IReadOnlyList<PartyHeroSheet> LoadActiveParty(string pcsDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pcsDirectory);

            var activeDirectory = Path.Combine(pcsDirectory, "active");
            var listingPath = PlayerCharacterAssetUtility.GetPlayerCharactersListingMarkdownCachePath(pcsDirectory);
            if (File.Exists(listingPath))
            {
                var heroes = PlayerCharacterAssetUtility
                    .GetHeroRows(File.ReadAllText(listingPath))
                    .Select(hero => LoadHeroFromListingRow(activeDirectory, hero))
                    .ToArray();
                if (heroes.Length > 0)
                {
                    return heroes;
                }
            }

            if (!Directory.Exists(activeDirectory))
            {
                return [];
            }

            return Directory.GetFiles(activeDirectory, "*.md")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => ParseHeroSheet(
                    File.ReadAllText(path),
                    Path.GetFileNameWithoutExtension(path),
                    FindTokenImageForMarkdown(activeDirectory, path)))
                .ToArray();
        }

        public static IReadOnlyList<PartyHeroSheet> WithVisibleXpTotals(
            IReadOnlyList<PartyHeroSheet> heroes,
            IReadOnlyList<PcXpTotal> xpTotals,
            XpAuthenticatedIdentity authenticatedIdentity,
            bool isDungeonMaster)
        {
            ArgumentNullException.ThrowIfNull(heroes);
            ArgumentNullException.ThrowIfNull(xpTotals);
            ArgumentNullException.ThrowIfNull(authenticatedIdentity);

            return heroes
                .Select(hero =>
                {
                    var authorized = isDungeonMaster
                        || (!string.IsNullOrWhiteSpace(hero.AccountId)
                            && string.Equals(hero.AccountId, authenticatedIdentity.AccountId, StringComparison.Ordinal));
                    var xpTotal = authorized ? FindXpTotalForCharacter(xpTotals, hero) : null;
                    return hero with { XpTotal = xpTotal?.XpTotal };
                })
                .ToArray();
        }

        internal static PartyHeroSheet ParseHeroSheet(
            string markdown,
            string fallbackName,
            string? tokenImagePath = null)
        {
            ArgumentNullException.ThrowIfNull(markdown);
            ArgumentException.ThrowIfNullOrWhiteSpace(fallbackName);

            var cleanedMarkdown = CleanMarkdown(markdown);
            var lines = cleanedMarkdown
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
            var name = ExtractName(lines, fallbackName);
            var (characterClass, level) = ExtractClassAndLevel(lines);
            (characterClass, level) = ApplyAttainedLevelNotes(lines, characterClass, level);
            var hitPoints = ExtractHitPoints(lines);
            var resolvedTokenImagePath = tokenImagePath;
            var sheetText = BuildDisplayText(lines);

            return new PartyHeroSheet(
                name,
                resolvedTokenImagePath,
                level,
                characterClass,
                hitPoints,
                sheetText);
        }

        private static PartyHeroSheet LoadHeroFromListingRow(string activeDirectory, PlayerCharacterHeroRow hero)
        {
            var markdownPath = GetHeroMarkdownPath(activeDirectory, hero.Name);
            var tokenImagePath = !string.IsNullOrWhiteSpace(hero.TokenFileName)
                ? Path.Combine(activeDirectory, hero.TokenFileName)
                : null;

            if (File.Exists(markdownPath))
            {
                var sheet = ParseHeroSheet(
                    File.ReadAllText(markdownPath),
                    hero.Name,
                    File.Exists(tokenImagePath) ? tokenImagePath : null);
                return ApplyListingSummary(sheet, hero);
            }

            return new PartyHeroSheet(
                hero.Name,
                File.Exists(tokenImagePath) ? tokenImagePath : null,
                hero.Level,
                hero.CharacterClass,
                hero.HitPoints,
                "Character sheet markdown is not available.");
        }

        private static PartyHeroSheet ApplyListingSummary(PartyHeroSheet sheet, PlayerCharacterHeroRow hero)
        {
            return sheet with
            {
                Level = FirstNonBlank(hero.Level, sheet.Level),
                CharacterClass = FirstNonBlank(hero.CharacterClass, sheet.CharacterClass),
                HitPoints = FirstNonBlank(hero.HitPoints, sheet.HitPoints)
            };
        }

        private static string FirstNonBlank(string preferred, string fallback)
        {
            return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
        }

        private static string? FindTokenImageForMarkdown(string activeDirectory, string markdownPath)
        {
            var baseName = Path.GetFileNameWithoutExtension(markdownPath);
            foreach (var extension in new[] { ".webp", ".png", ".jpg", ".jpeg", ".gif", ".bmp" })
            {
                var tokenPath = Path.Combine(activeDirectory, $"{baseName}-token{extension}");
                if (File.Exists(tokenPath))
                {
                    return tokenPath;
                }
            }

            return null;
        }

        private static string CleanMarkdown(string markdown)
        {
            var cleaned = FrontMatterRegex.Replace(markdown, string.Empty);
            cleaned = ObsidianImageRegex.Replace(cleaned, string.Empty);
            return MarkdownLinkRegex.Replace(cleaned, match =>
                match.Groups["alias"].Success
                    ? match.Groups["alias"].Value
                    : match.Groups["target"].Value);
        }

        private static string ExtractName(string[] lines, string fallbackName)
        {
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed.StartsWith("Name & Title", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed["Name & Title".Length..].Trim().Split(',', 2)[0].Trim();
                }

                if (TryGetLabelValue(trimmed, "Name", out var name))
                {
                    return name.Split(',', 2)[0].Trim();
                }
            }

            return fallbackName.Trim();
        }

        private static (string CharacterClass, string Level) ExtractClassAndLevel(string[] lines)
        {
            var characterClass = string.Empty;
            var level = string.Empty;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var classLevelMatch = ClassLevelLineRegex.Match(trimmed);
                if (classLevelMatch.Success)
                {
                    (characterClass, level) = SplitClassLevel(classLevelMatch.Groups["value"].Value);
                    continue;
                }

                if (TryGetLabelValue(trimmed, "Class", out var classValue))
                {
                    characterClass = classValue.Trim();
                    continue;
                }

                if (TryGetLabelValue(trimmed, "Level", out var levelValue))
                {
                    level = levelValue.Trim();
                }
            }

            return (characterClass, level);
        }

        private static (string CharacterClass, string Level) ApplyAttainedLevelNotes(
            string[] lines,
            string characterClass,
            string level)
        {
            foreach (var match in lines.SelectMany(line => AttainedLevelRegex.Matches(line).Cast<Match>()))
            {
                var parsedLevel = match.Groups["level"].Value.TrimStart('0');
                level = parsedLevel.Length == 0 ? "0" : parsedLevel;
                characterClass = match.Groups["class"].Value.Trim();
            }

            return (characterClass, level);
        }

        private static string ExtractHitPoints(string[] lines)
        {
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (TryGetLabelValue(trimmed, "Hit Points", out var hitPoints)
                    || TryGetLabelValue(trimmed, "HP", out hitPoints))
                {
                    return hitPoints.Trim();
                }
            }

            return string.Empty;
        }

        private static (string CharacterClass, string Level) SplitClassLevel(string value)
        {
            value = value.Trim();
            var slashIndex = value.LastIndexOf('/');
            if (slashIndex > 0 && slashIndex < value.Length - 1)
            {
                return (value[..slashIndex].Trim(), value[(slashIndex + 1)..].Trim());
            }

            return (value, string.Empty);
        }

        private static bool TryGetLabelValue(string line, string label, out string value)
        {
            value = string.Empty;

            if (line.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
            {
                value = line[(label.Length + 1)..].Trim();
                return value.Length > 0;
            }

            if (!line.StartsWith(label, StringComparison.OrdinalIgnoreCase)
                || line.Length <= label.Length
                || !char.IsWhiteSpace(line[label.Length]))
            {
                return false;
            }

            var whitespaceLength = 0;
            for (var index = label.Length; index < line.Length && char.IsWhiteSpace(line[index]); index++)
            {
                whitespaceLength++;
            }

            if (whitespaceLength < 2)
            {
                return false;
            }

            value = line[(label.Length + whitespaceLength)..].Trim();
            return value.Length > 0;
        }

        private static string BuildDisplayText(string[] lines)
        {
            var selectedLines = lines
                .Select(line => line.TrimEnd())
                .Where(line => !IsXpTotalLine(line))
                .ToArray();
            return string.Join(Environment.NewLine, selectedLines).Trim();
        }

        private static bool IsXpTotalLine(string line)
        {
            var trimmed = line.TrimStart();
            return Regex.IsMatch(trimmed, @"^XP(?:\s*\([^)]*\))?\s*:?\s*\d", RegexOptions.IgnoreCase)
                || Regex.IsMatch(trimmed, @"^XP\s*\([^)]*\)\s*$", RegexOptions.IgnoreCase)
                || Regex.IsMatch(trimmed, @"^XP\s*:\s*$", RegexOptions.IgnoreCase);
        }

        private static string GetHeroMarkdownFileName(string heroName)
        {
            return Regex.Replace(heroName.Trim().ToLowerInvariant(), @"[^a-z0-9_-]+", "-").Trim('-');
        }

        private static string GetHeroMarkdownPath(string activeDirectory, string heroName)
        {
            var canonicalPath = Path.Combine(activeDirectory, $"{GetHeroMarkdownFileName(heroName)}.md");
            if (File.Exists(canonicalPath)) return canonicalPath;

            // Migration adapter for existing first-name files. New downloads use the
            // canonical full-name path, so same-first-name heroes cannot overwrite it.
            var firstName = heroName.Trim().Split(' ', 2)[0];
            var legacyStem = Regex.Replace(firstName.ToLowerInvariant(), @"[^a-z0-9_-]+", "-").Trim('-');
            var legacyPath = Path.Combine(activeDirectory, $"{legacyStem}.md");
            return File.Exists(legacyPath) ? legacyPath : canonicalPath;
        }

        private static PcXpTotal? FindXpTotalForCharacter(
            IReadOnlyList<PcXpTotal> totals,
            PartyHeroSheet hero)
        {
            return totals.FirstOrDefault(row =>
                (!string.IsNullOrWhiteSpace(hero.AccountId)
                    && !string.IsNullOrWhiteSpace(row.AccountId)
                    && string.Equals(row.AccountId, hero.AccountId, StringComparison.Ordinal))
                || (string.IsNullOrWhiteSpace(row.AccountId)
                    && string.Equals(row.Name, hero.Name.Trim(), StringComparison.OrdinalIgnoreCase)));
        }
    }
}
