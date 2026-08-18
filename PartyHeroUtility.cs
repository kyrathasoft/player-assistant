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
        string? CanonicalId = null);

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
                var heroRows = PlayerCharacterAssetUtility.GetHeroRows(File.ReadAllText(listingPath));
                var heroes = heroRows
                    .Select(hero => LoadHeroFromListingRow(activeDirectory, hero, heroRows))
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
            XpAuthenticatedIdentity authenticatedIdentity)
        {
            ArgumentNullException.ThrowIfNull(heroes);
            ArgumentNullException.ThrowIfNull(xpTotals);
            ArgumentNullException.ThrowIfNull(authenticatedIdentity);

            return heroes
                .Select(hero =>
                {
                    var isUniqueAuthenticatedHero = !string.IsNullOrWhiteSpace(hero.CanonicalId)
                        && string.Equals(
                            hero.CanonicalId,
                            authenticatedIdentity.CanonicalId,
                            StringComparison.Ordinal)
                        && heroes.Count(candidate => string.Equals(
                            candidate.CanonicalId,
                            authenticatedIdentity.CanonicalId,
                            StringComparison.Ordinal)) == 1;
                    var xpTotal = authenticatedIdentity.IsDungeonMaster
                        ? FindXpTotalForCharacter(xpTotals, hero)
                        : isUniqueAuthenticatedHero
                            ? FindXpTotalForCharacter(xpTotals, hero)
                            : null;
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

        private static PartyHeroSheet LoadHeroFromListingRow(
            string activeDirectory,
            PlayerCharacterHeroRow hero,
            IReadOnlyList<PlayerCharacterHeroRow> roster)
        {
            var markdownPath = FindHeroMarkdownPath(activeDirectory, hero, roster);
            var tokenImagePath = !string.IsNullOrWhiteSpace(hero.TokenFileName)
                ? Path.Combine(activeDirectory, hero.TokenFileName)
                : null;

            if (markdownPath is not null)
            {
                var sheet = ParseHeroSheet(
                    File.ReadAllText(markdownPath),
                    hero.Name,
                    File.Exists(tokenImagePath) ? tokenImagePath : null);
                return ApplyListingSummary(sheet with { CanonicalId = hero.CanonicalId }, hero);
            }

            return new PartyHeroSheet(
                hero.Name,
                File.Exists(tokenImagePath) ? tokenImagePath : null,
                hero.Level,
                hero.CharacterClass,
                hero.HitPoints,
                "Character sheet markdown is not available.",
                CanonicalId: hero.CanonicalId);
        }

        private static string? FindHeroMarkdownPath(
            string activeDirectory,
            PlayerCharacterHeroRow hero,
            IReadOnlyList<PlayerCharacterHeroRow> roster)
        {
            var canonicalFileName = GetStableHeroFileName(hero.CanonicalId);
            var candidates = new[]
            {
                canonicalFileName,
                GetStableHeroFileName(hero.Name),
                GetLegacyHeroFileName(hero.Name)
            }
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var fileName in candidates)
            {
                var path = Path.Combine(activeDirectory, $"{fileName}.md");
                if (!File.Exists(path))
                {
                    continue;
                }

                if (!string.Equals(fileName, canonicalFileName, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(
                            fileName,
                            GetLegacyHeroFileName(hero.Name),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        var sameFirstNameCount = roster.Count(candidate =>
                            string.Equals(
                                GetFirstName(candidate.Name),
                                GetFirstName(hero.Name),
                                StringComparison.OrdinalIgnoreCase));
                        if (sameFirstNameCount != 1)
                        {
                            continue;
                        }
                    }

                }

                var parsed = ParseHeroSheet(File.ReadAllText(path), hero.Name);
                if (!string.Equals(parsed.Name, hero.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return path;
            }

            return null;
        }

        private static PartyHeroSheet ApplyListingSummary(PartyHeroSheet sheet, PlayerCharacterHeroRow hero)
        {
            return sheet with
            {
                Level = FirstNonBlank(hero.Level, sheet.Level),
                CharacterClass = FirstNonBlank(hero.CharacterClass, sheet.CharacterClass),
                HitPoints = FirstNonBlank(hero.HitPoints, sheet.HitPoints),
                CanonicalId = hero.CanonicalId
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

        private static string GetStableHeroFileName(string? identity)
        {
            return string.IsNullOrWhiteSpace(identity)
                ? string.Empty
                : Regex.Replace(identity.Trim().ToLowerInvariant(), @"[^a-z0-9_-]+", "-").Trim('-');
        }

        private static string GetLegacyHeroFileName(string heroName)
        {
            return GetStableHeroFileName(GetFirstName(heroName));
        }

        private static PcXpTotal? FindXpTotalForCharacter(
            IReadOnlyList<PcXpTotal> totals,
            PartyHeroSheet hero)
        {
            if (string.IsNullOrWhiteSpace(hero.CanonicalId))
            {
                return null;
            }

            var canonicalMatches = totals
                .Where(row => string.Equals(row.CanonicalId, hero.CanonicalId, StringComparison.Ordinal))
                .ToArray();
            return canonicalMatches.Length == 1 ? canonicalMatches[0] : null;
        }


        private static string GetFirstName(string value)
        {
            var trimmedValue = value.Trim();
            var spaceIndex = trimmedValue.IndexOf(' ');
            return spaceIndex < 0
                ? trimmedValue
                : trimmedValue[..spaceIndex];
        }
    }
}
