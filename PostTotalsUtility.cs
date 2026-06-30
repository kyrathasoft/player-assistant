using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal enum PostingCategory
    {
        DungeonMaster,
        Hero,
        Npc
    }

    internal sealed record PostTotalsRow(
        string CharacterName,
        PostingCategory Category,
        int LocalPosts,
        int? ForumPosts,
        string Tag,
        string? LastVisited,
        string LastPost);

    internal sealed record PostTotalsSummary(
        int LocalDungeonMasterPosts,
        int ForumDungeonMasterPosts,
        int LocalNpcPosts,
        int ForumNpcPosts,
        int LocalHeroPosts,
        int ForumHeroPosts,
        IReadOnlyList<PostTotalsRow> Rows);

    internal static class PostTotalsUtility
    {
        private const string PlayerTag = "player";
        private const string GameMasterTag = "GM";
        private static readonly Regex MarkdownNameRegex = new(
            @"(?im)^\s*Name:\s*(?<name>[^\r\n]+)\s*$",
            RegexOptions.Compiled);
        private static readonly Regex MarkdownTitleNameRegex = new(
            @"(?im)^\s*Name\s*&\s*Title\s+(?<name>[^,\r\n]+)",
            RegexOptions.Compiled);

        public static PostTotalsSummary BuildSummary(
            IEnumerable<TheCastLoginInfo> loginInfoRows,
            string releaseDirectory)
        {
            ArgumentNullException.ThrowIfNull(loginInfoRows);
            ArgumentException.ThrowIfNullOrWhiteSpace(releaseDirectory);

            var loginRows = loginInfoRows.ToArray();
            var loginRowsByName = loginRows.ToDictionary(
                row => row.CharacterName,
                row => row,
                StringComparer.OrdinalIgnoreCase);
            var heroNames = GetHeroNames(loginRows, releaseDirectory);
            var localCounts = GetLocalPostCounts(releaseDirectory);
            var allNames = localCounts.Keys
                .Concat(loginRowsByName.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var rows = allNames
                .Select(name =>
                {
                    localCounts.TryGetValue(name, out var localPosts);
                    loginRowsByName.TryGetValue(name, out var loginRow);
                    var category = GetCategory(name, loginRow?.Tag, heroNames);

                    return new PostTotalsRow(
                        name,
                        category,
                        localPosts,
                        loginRow?.Posts,
                        loginRow?.Tag ?? string.Empty,
                        loginRow?.LastVisited,
                        loginRow?.LastPost ?? string.Empty);
                })
                .OrderBy(row => row.Category)
                .ThenBy(row => row.CharacterName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new PostTotalsSummary(
                rows.Where(row => row.Category == PostingCategory.DungeonMaster).Sum(row => row.LocalPosts),
                rows.Where(row => row.Category == PostingCategory.DungeonMaster).Sum(row => row.ForumPosts ?? 0),
                rows.Where(row => row.Category == PostingCategory.Npc).Sum(row => row.LocalPosts),
                rows.Where(row => row.Category == PostingCategory.Npc).Sum(row => row.ForumPosts ?? 0),
                rows.Where(row => row.Category == PostingCategory.Hero).Sum(row => row.LocalPosts),
                rows.Where(row => row.Category == PostingCategory.Hero).Sum(row => row.ForumPosts ?? 0),
                rows);
        }

        private static IReadOnlyDictionary<string, int> GetLocalPostCounts(string releaseDirectory)
        {
            var icDirectory = Path.Combine(releaseDirectory, "Posts", "IC");
            var asideDirectory = Path.Combine(icDirectory, "Aside");
            var oocDirectory = Path.Combine(releaseDirectory, "Posts", "OOC");
            var htmlFiles = Enumerable.Empty<string>();

            if (Directory.Exists(icDirectory))
            {
                htmlFiles = htmlFiles.Concat(Directory.EnumerateFiles(icDirectory, "*.html", SearchOption.TopDirectoryOnly));
            }

            if (Directory.Exists(asideDirectory))
            {
                htmlFiles = htmlFiles.Concat(Directory.EnumerateFiles(asideDirectory, "*.html", SearchOption.TopDirectoryOnly));
            }

            if (Directory.Exists(oocDirectory))
            {
                htmlFiles = htmlFiles.Concat(Directory.EnumerateFiles(oocDirectory, "*.html", SearchOption.TopDirectoryOnly));
            }

            return RpolThreadPostUtility.GetPostCountsByAuthorFromSavedHtmlFiles(htmlFiles);
        }

        private static HashSet<string> GetHeroNames(
            IEnumerable<TheCastLoginInfo> loginInfoRows,
            string releaseDirectory)
        {
            var names = new HashSet<string>(
                loginInfoRows
                    .Where(row => string.Equals(row.Tag, PlayerTag, StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.CharacterName),
                StringComparer.OrdinalIgnoreCase);

            var activePcsDirectory = Path.Combine(releaseDirectory, "PCs", "active");
            if (!Directory.Exists(activePcsDirectory))
            {
                return names;
            }

            foreach (var markdownPath in Directory.EnumerateFiles(activePcsDirectory, "*.md", SearchOption.TopDirectoryOnly))
            {
                var markdown = File.ReadAllText(markdownPath);
                AddMarkdownHeroName(names, MarkdownNameRegex.Match(markdown));
                AddMarkdownHeroName(names, MarkdownTitleNameRegex.Match(markdown));
            }

            return names;
        }

        private static PostingCategory GetCategory(
            string characterName,
            string? tag,
            HashSet<string> heroNames)
        {
            if (string.Equals(characterName, RpolThreadPostUtility.DungeonMasterAuthor, StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, GameMasterTag, StringComparison.OrdinalIgnoreCase))
            {
                return PostingCategory.DungeonMaster;
            }

            return heroNames.Contains(characterName)
                ? PostingCategory.Hero
                : PostingCategory.Npc;
        }

        private static void AddMarkdownHeroName(HashSet<string> names, Match match)
        {
            if (!match.Success)
            {
                return;
            }

            var name = match.Groups["name"].Value.Trim();
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }
    }
}
