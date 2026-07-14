namespace PlayerAssistant
{
    using System.Text.RegularExpressions;

    internal sealed record MyHeroBriefingRequest(
        IReadOnlyList<PartyHeroSheet> ActiveParty,
        string? SelectedHeroName = null,
        string? AuthenticatedHeroName = null,
        bool IsDungeonMaster = false,
        IReadOnlyList<MyHeroBriefingThreadPosts>? ThreadPosts = null,
        IReadOnlyList<PcXpTotal>? XpTotals = null,
        IReadOnlyList<EncryptedTextIndexEntry>? EncryptedTextIndex = null,
        IReadOnlyList<MyHeroBriefingQuickLink>? QuickLinks = null);

    internal sealed record MyHeroBriefing(
        MyHeroBriefingHeroSummary? Hero,
        MyHeroBriefingHeroCard? HeroCard,
        IReadOnlyList<string> HeroChoices,
        bool NeedsHeroSelection,
        MyHeroBriefingHeroIdentitySource HeroIdentitySource,
        IReadOnlyList<MyHeroBriefingActivityItem> RecentActivity,
        IReadOnlyList<MyHeroBriefingResponseItem> LikelyResponseItems,
        IReadOnlyList<MyHeroBriefingUnlockedNoteItem> UnlockedNotes,
        IReadOnlyList<MyHeroBriefingQuickLink> QuickLinks,
        string StatusMessage);

    internal sealed record MyHeroBriefingHeroSummary(
        string Name,
        string CharacterClass,
        string Level,
        string HitPoints,
        int? XpTotal,
        string? TokenImagePath,
        string CharacterSheetText,
        HeroAccessContext AccessContext);

    internal sealed record MyHeroBriefingHeroCard(
        string Name,
        string CharacterClass,
        string Level,
        string HitPoints,
        int? XpTotal,
        string XpTotalLabel,
        string? TokenImagePath,
        string CharacterSheetText,
        IReadOnlyList<MyHeroBriefingQuickLink> QuickLinks);

    internal sealed record MyHeroBriefingThreadPosts(
        string ThreadTitle,
        string ThreadUrl,
        IReadOnlyList<RpolThreadPost> Posts);

    internal sealed record MyHeroBriefingActivityItem(
        string ThreadTitle,
        string ThreadUrl,
        int MessageNumber,
        string Author,
        string PostedDate,
        string PostedTime,
        string Excerpt);

    internal sealed record MyHeroBriefingResponseItem(
        string ThreadTitle,
        string ThreadUrl,
        int MessageNumber,
        string Author,
        string PostedDate,
        string PostedTime,
        string Reason,
        string Excerpt);

    internal sealed record MyHeroBriefingUnlockedNoteItem(
        string Title,
        string Url,
        string Excerpt);

    internal sealed record MyHeroBriefingQuickLink(
        string Label,
        string Target);

    internal enum MyHeroBriefingHeroIdentitySource
    {
        None,
        AuthenticatedHero,
        SelectedHero
    }

    internal static class MyHeroBriefingUtility
    {
        public static MyHeroBriefing Build(MyHeroBriefingRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.ActiveParty);

            var heroChoices = request.ActiveParty
                .Select(hero => hero.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var identity = ResolveHeroIdentity(request);
            var heroSummary = identity.Hero is null
                ? null
                : CreateHeroSummary(
                    identity.Hero,
                    request.XpTotals ?? [],
                    request.IsDungeonMaster,
                    identity.Source);
            var quickLinks = CreateQuickLinks(request);
            var heroCard = heroSummary is null
                ? null
                : CreateHeroCard(heroSummary, quickLinks);
            var recentActivity = heroSummary is null
                ? []
                : BuildRecentActivity(heroSummary, request.ThreadPosts ?? []);
            var responseItems = heroSummary is null
                ? []
                : BuildLikelyResponseItems(heroSummary, request.ThreadPosts ?? []);
            var unlockedNotes = heroSummary is null
                ? []
                : BuildUnlockedNotes(heroSummary, request.EncryptedTextIndex ?? []);

            return new MyHeroBriefing(
                heroSummary,
                heroCard,
                heroChoices,
                NeedsHeroSelection: heroSummary is null && heroChoices.Length > 0,
                identity.Source,
                recentActivity,
                responseItems,
                unlockedNotes,
                QuickLinks: quickLinks,
                StatusMessage: CreateStatusMessage(heroSummary, heroChoices, request.IsDungeonMaster));
        }

        private static MyHeroBriefingResolvedHero ResolveHeroIdentity(MyHeroBriefingRequest request)
        {
            if (!request.IsDungeonMaster)
            {
                var authenticatedHero = FindHeroByNameOrFirstName(request.ActiveParty, request.AuthenticatedHeroName);
                if (authenticatedHero is not null)
                {
                    return new MyHeroBriefingResolvedHero(
                        authenticatedHero,
                        MyHeroBriefingHeroIdentitySource.AuthenticatedHero);
                }
            }

            var selectedHero = FindHeroByNameOrFirstName(request.ActiveParty, request.SelectedHeroName);
            return selectedHero is not null
                ? new MyHeroBriefingResolvedHero(selectedHero, MyHeroBriefingHeroIdentitySource.SelectedHero)
                : new MyHeroBriefingResolvedHero(null, MyHeroBriefingHeroIdentitySource.None);
        }

        private static PartyHeroSheet? FindHeroByNameOrFirstName(
            IReadOnlyList<PartyHeroSheet> activeParty,
            string? heroName)
        {
            if (string.IsNullOrWhiteSpace(heroName))
            {
                return null;
            }

            var trimmedName = heroName.Trim();
            var exactMatch = activeParty.FirstOrDefault(hero =>
                string.Equals(hero.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch is not null)
            {
                return exactMatch;
            }

            var firstNameMatches = activeParty
                .Where(hero => string.Equals(GetFirstName(hero.Name), trimmedName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return firstNameMatches.Length == 1 ? firstNameMatches[0] : null;
        }

        private static MyHeroBriefingHeroSummary CreateHeroSummary(
            PartyHeroSheet hero,
            IReadOnlyList<PcXpTotal> xpTotals,
            bool isDungeonMaster,
            MyHeroBriefingHeroIdentitySource identitySource)
        {
            return new MyHeroBriefingHeroSummary(
                hero.Name,
                hero.CharacterClass,
                hero.Level,
                hero.HitPoints,
                FindVisibleXpTotal(hero, xpTotals, isDungeonMaster, identitySource),
                hero.TokenImagePath,
                hero.CharacterSheetText,
                HeroAccessContext.FromPartyHeroSheet(hero));
        }

        private static MyHeroBriefingHeroCard CreateHeroCard(
            MyHeroBriefingHeroSummary hero,
            IReadOnlyList<MyHeroBriefingQuickLink> quickLinks)
        {
            return new MyHeroBriefingHeroCard(
                hero.Name,
                hero.CharacterClass,
                hero.Level,
                hero.HitPoints,
                hero.XpTotal,
                hero.XpTotal is null ? "XP Total: hidden" : $"XP Total: {hero.XpTotal.Value:N0}",
                hero.TokenImagePath,
                hero.CharacterSheetText,
                quickLinks);
        }

        private static int? FindVisibleXpTotal(
            PartyHeroSheet hero,
            IReadOnlyList<PcXpTotal> xpTotals,
            bool isDungeonMaster,
            MyHeroBriefingHeroIdentitySource identitySource)
        {
            if (hero.XpTotal is not null)
            {
                return hero.XpTotal;
            }

            if (!isDungeonMaster && identitySource != MyHeroBriefingHeroIdentitySource.AuthenticatedHero)
            {
                return null;
            }

            return xpTotals.FirstOrDefault(total =>
                    string.Equals(total.Name, hero.Name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(GetFirstName(total.Name), GetFirstName(hero.Name), StringComparison.OrdinalIgnoreCase))
                    ?.XpTotal;
        }

        private static IReadOnlyList<MyHeroBriefingQuickLink> CreateQuickLinks(MyHeroBriefingRequest request)
        {
            var links = new List<MyHeroBriefingQuickLink>
            {
                new("Full Sheet", "app://show/party"),
                new("XP", "app://show/xp"),
                new("Party", "app://show/party"),
                new("Adventure Outline", "app://show/adventure-outline")
            };
            links.AddRange(request.ThreadPosts?
                .Where(thread => !string.IsNullOrWhiteSpace(thread.ThreadUrl))
                .Select(thread => new MyHeroBriefingQuickLink(
                    string.IsNullOrWhiteSpace(thread.ThreadTitle) ? "RPOL Thread" : thread.ThreadTitle,
                    thread.ThreadUrl)) ?? []);
            links.AddRange(request.QuickLinks ?? []);

            return links
                .Where(link => !string.IsNullOrWhiteSpace(link.Label) && !string.IsNullOrWhiteSpace(link.Target))
                .DistinctBy(link => $"{link.Label}\n{link.Target}", StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IReadOnlyList<MyHeroBriefingActivityItem> BuildRecentActivity(
            MyHeroBriefingHeroSummary hero,
            IReadOnlyList<MyHeroBriefingThreadPosts> threadPosts)
        {
            var aliases = GetHeroAliases(hero.Name);
            return threadPosts
                .SelectMany(thread => (thread.Posts ?? [])
                    .Where(post => MentionsAnyAlias(post.BodyText, aliases))
                    .Select(post => new MyHeroBriefingActivityItem(
                        thread.ThreadTitle,
                        thread.ThreadUrl,
                        post.MessageNumber,
                        post.Author,
                        post.PostedDate,
                        post.PostedTime,
                        CreateExcerpt(post.BodyText))))
                .OrderByDescending(item => item.MessageNumber)
                .Take(10)
                .ToArray();
        }

        private static IReadOnlyList<MyHeroBriefingResponseItem> BuildLikelyResponseItems(
            MyHeroBriefingHeroSummary hero,
            IReadOnlyList<MyHeroBriefingThreadPosts> threadPosts)
        {
            var aliases = GetHeroAliases(hero.Name);
            return threadPosts
                .SelectMany(thread => BuildLikelyResponseItemsForThread(thread, aliases))
                .OrderBy(item => GetResponsePriority(item.Reason))
                .ThenBy(item => item.ThreadTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.MessageNumber)
                .Take(10)
                .ToArray();
        }

        private static IEnumerable<MyHeroBriefingResponseItem> BuildLikelyResponseItemsForThread(
            MyHeroBriefingThreadPosts thread,
            IReadOnlyList<string> aliases)
        {
            var posts = (thread.Posts ?? [])
                .OrderBy(post => post.MessageNumber)
                .ToArray();
            var lastHeroMessageNumber = posts
                .Where(post => IsHeroAuthor(post.Author, aliases))
                .Select(post => (int?)post.MessageNumber)
                .LastOrDefault();
            if (lastHeroMessageNumber is null)
            {
                return [];
            }

            return posts
                .Where(post => post.MessageNumber > lastHeroMessageNumber.Value)
                .Where(post => !IsHeroAuthor(post.Author, aliases))
                .Select(post => new MyHeroBriefingResponseItem(
                    thread.ThreadTitle,
                    thread.ThreadUrl,
                    post.MessageNumber,
                    post.Author,
                    post.PostedDate,
                    post.PostedTime,
                    GetResponseReason(post.BodyText, aliases),
                    CreateExcerpt(post.BodyText)));
        }

        private static bool IsHeroAuthor(string author, IReadOnlyList<string> aliases)
        {
            return aliases.Any(alias => string.Equals(author, alias, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetResponseReason(string text, IReadOnlyList<string> aliases)
        {
            if (MentionsAnyAlias(text, aliases))
            {
                return "Direct mention after your last post";
            }

            if (IsQuestionLike(text))
            {
                return "Question after your last post";
            }

            return "Recent post after your last post";
        }

        private static int GetResponsePriority(string reason)
        {
            return reason switch
            {
                "Direct mention after your last post" => 0,
                "Question after your last post" => 1,
                _ => 2
            };
        }

        private static bool IsQuestionLike(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && text.Contains('?', StringComparison.Ordinal);
        }

        private static IReadOnlyList<MyHeroBriefingUnlockedNoteItem> BuildUnlockedNotes(
            MyHeroBriefingHeroSummary hero,
            IReadOnlyList<EncryptedTextIndexEntry> encryptedTextIndex)
        {
            return encryptedTextIndex
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Url))
                .Where(entry => entry.EncryptedSections > 0)
                .Where(entry => TaggedNoteCipherUtility.CanAccessAnyTag(entry.FrontmatterTags, hero.AccessContext))
                .Select(entry => new MyHeroBriefingUnlockedNoteItem(
                    GetNoteTitle(entry.Url),
                    entry.Url,
                    entry.EncryptedSections == 1
                        ? "1 unlocked encrypted section may be relevant."
                        : $"{entry.EncryptedSections} unlocked encrypted sections may be relevant."))
                .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToArray();
        }

        private static string GetNoteTitle(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return url.Trim();
            }

            var segment = uri.Segments.LastOrDefault()?.Trim('/') ?? uri.Host;
            return Uri.UnescapeDataString(segment)
                .Replace('+', ' ')
                .Trim();
        }

        private static string[] GetHeroAliases(string heroName)
        {
            return new[] { heroName, GetFirstName(heroName) }
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool MentionsAnyAlias(string text, IReadOnlyList<string> aliases)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return aliases.Any(alias =>
                Regex.IsMatch(
                    text,
                    $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(alias)}(?![\p{{L}}\p{{N}}])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }

        private static string CreateExcerpt(string text)
        {
            var normalized = Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
            const int maximumLength = 180;
            if (normalized.Length <= maximumLength)
            {
                return normalized;
            }

            return $"{normalized[..maximumLength].TrimEnd()}...";
        }

        private static string CreateStatusMessage(
            MyHeroBriefingHeroSummary? heroSummary,
            IReadOnlyList<string> heroChoices,
            bool isDungeonMaster)
        {
            if (heroSummary is not null)
            {
                return $"My Hero Briefing ready for {heroSummary.Name}.";
            }

            if (isDungeonMaster && heroChoices.Count > 0)
            {
                return "Choose a hero to build My Hero Briefing for Dungeon Master view.";
            }

            return heroChoices.Count > 0
                ? "Choose a hero to build My Hero Briefing."
                : "No active heroes are available for My Hero Briefing.";
        }

        private static string GetFirstName(string name)
        {
            return name
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;
        }

        private sealed record MyHeroBriefingResolvedHero(
            PartyHeroSheet? Hero,
            MyHeroBriefingHeroIdentitySource Source);
    }
}
