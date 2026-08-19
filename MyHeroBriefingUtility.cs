namespace PlayerAssistant
{
    using System.Text.RegularExpressions;

    internal sealed record MyHeroBriefingRequest(
        IReadOnlyList<PartyHeroSheet> ActiveParty,
        string? SelectedHeroName = null,
        string? AuthenticatedHeroName = null,
        string? SelectedHeroCanonicalId = null,
        string? AuthenticatedHeroCanonicalId = null,
        bool IsDungeonMaster = false,
        IReadOnlyList<MyHeroBriefingThreadPosts>? ThreadPosts = null,
        IReadOnlyList<PcXpTotal>? XpTotals = null,
        IReadOnlyList<EncryptedTextIndexEntry>? EncryptedTextIndex = null,
        IReadOnlyList<MyHeroBriefingQuickLink>? QuickLinks = null,
        XpAuthenticatedIdentity? AuthenticatedIdentity = null,
        IReadOnlyList<XpAuthenticatedIdentity>? IdentityRegistry = null);

    internal sealed record MyHeroBriefing(
        MyHeroBriefingHeroSummary? Hero,
        MyHeroBriefingHeroCard? HeroCard,
        IReadOnlyList<MyHeroBriefingHeroChoice> HeroChoices,
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
        HeroAccessContext AccessContext,
        IReadOnlyList<string> Aliases);

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

    internal sealed record MyHeroBriefingHeroChoice(
        string CanonicalId,
        string DisplayName);

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
                .Where(hero => !string.IsNullOrWhiteSpace(hero.CanonicalId)
                    && !string.IsNullOrWhiteSpace(hero.Name))
                .Select(hero => new MyHeroBriefingHeroChoice(hero.CanonicalId!, hero.Name))
                .DistinctBy(choice => choice.CanonicalId, StringComparer.Ordinal)
                .OrderBy(choice => choice.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var isDungeonMaster = request.AuthenticatedIdentity?.IsDungeonMaster == true;
            var identity = ResolveHeroIdentity(request);
            var heroSummary = identity.Hero is null
                ? null
                : CreateHeroSummary(
                    identity.Hero,
                    request.XpTotals ?? [],
                    isDungeonMaster,
                    identity.Source,
                    identity.CanonicalName,
                    identity.Aliases);
            var quickLinks = CreateQuickLinks(request, heroSummary);
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
                NeedsHeroSelection: isDungeonMaster && heroSummary is null && heroChoices.Length > 0,
                identity.Source,
                recentActivity,
                responseItems,
                unlockedNotes,
                QuickLinks: quickLinks,
                StatusMessage: CreateStatusMessage(heroSummary, heroChoices, isDungeonMaster));
        }

        private static MyHeroBriefingResolvedHero ResolveHeroIdentity(MyHeroBriefingRequest request)
        {
            if (request.AuthenticatedIdentity?.IsDungeonMaster != true)
            {
                var authenticatedCanonicalId = request.AuthenticatedIdentity?.CanonicalId;
                var authenticatedHero = FindHeroByIdentity(
                    request.ActiveParty,
                    authenticatedCanonicalId);
                if (authenticatedHero is not null)
                {
                    var aliases = request.AuthenticatedIdentity is not null
                        && string.Equals(
                            request.AuthenticatedIdentity.CanonicalId,
                            authenticatedHero.CanonicalId,
                            StringComparison.Ordinal)
                            ? request.AuthenticatedIdentity.Aliases
                            : [];
                    return new MyHeroBriefingResolvedHero(
                        authenticatedHero,
                        MyHeroBriefingHeroIdentitySource.AuthenticatedHero,
                        request.AuthenticatedIdentity?.CanonicalName,
                        aliases);
                }
            }

            if (request.AuthenticatedIdentity?.IsDungeonMaster != true)
            {
                return new MyHeroBriefingResolvedHero(null, MyHeroBriefingHeroIdentitySource.None, null, []);
            }

            var selectedHero = FindHeroByIdentity(
                request.ActiveParty,
                request.SelectedHeroCanonicalId);
            var selectedIdentity = selectedHero is null
                ? null
                : FindRegistryIdentity(request.IdentityRegistry ?? [], selectedHero);
            return selectedHero is not null
                ? new MyHeroBriefingResolvedHero(
                    selectedHero,
                    MyHeroBriefingHeroIdentitySource.SelectedHero,
                    selectedIdentity?.CanonicalName,
                    selectedIdentity?.Aliases ?? [])
                : new MyHeroBriefingResolvedHero(null, MyHeroBriefingHeroIdentitySource.None, null, []);
        }

        private static XpAuthenticatedIdentity? FindRegistryIdentity(
            IReadOnlyList<XpAuthenticatedIdentity> registry,
            PartyHeroSheet hero)
        {
            if (string.IsNullOrWhiteSpace(hero.CanonicalId))
            {
                return null;
            }

            var matches = registry
                .Where(identity => string.Equals(
                    identity.CanonicalId,
                    hero.CanonicalId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static PartyHeroSheet? FindHeroByIdentity(
            IReadOnlyList<PartyHeroSheet> activeParty,
            string? canonicalId)
        {
            if (string.IsNullOrWhiteSpace(canonicalId))
            {
                return null;
            }

            var canonicalMatches = activeParty
                .Where(hero => string.Equals(hero.CanonicalId, canonicalId.Trim(), StringComparison.Ordinal))
                .ToArray();
            return canonicalMatches.Length == 1 ? canonicalMatches[0] : null;
        }

        private static MyHeroBriefingHeroSummary CreateHeroSummary(
            PartyHeroSheet hero,
            IReadOnlyList<PcXpTotal> xpTotals,
            bool isDungeonMaster,
            MyHeroBriefingHeroIdentitySource identitySource,
            string? canonicalName,
            IReadOnlyList<string> aliases)
        {
            return new MyHeroBriefingHeroSummary(
                hero.Name,
                hero.CharacterClass,
                hero.Level,
                hero.HitPoints,
                FindVisibleXpTotal(hero, xpTotals, isDungeonMaster, identitySource),
                hero.TokenImagePath,
                hero.CharacterSheetText,
                HeroAccessContext.FromPartyHeroSheet(
                    hero with { Name = canonicalName ?? string.Empty },
                    characterAliases: aliases),
                aliases);
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

            if (!string.IsNullOrWhiteSpace(hero.CanonicalId))
            {
                var canonicalMatches = xpTotals
                    .Where(total => string.Equals(total.CanonicalId, hero.CanonicalId, StringComparison.Ordinal))
                    .Take(2)
                    .ToArray();
                return canonicalMatches.Length == 1 ? canonicalMatches[0].XpTotal : null;
            }

            return null;
        }

        private static IReadOnlyList<MyHeroBriefingQuickLink> CreateQuickLinks(
            MyHeroBriefingRequest request,
            MyHeroBriefingHeroSummary? hero)
        {
            var links = new List<MyHeroBriefingQuickLink>
            {
                new("Full Sheet", "app://show/party"),
                new("XP", "app://show/xp"),
                new("Party", "app://show/party"),
                new("Adventure Outline", "app://show/adventure-outline")
            };
            var aliases = hero is null ? [] : GetHeroAliases(hero);
            links.AddRange(request.ThreadPosts?
                .Where(thread => !string.IsNullOrWhiteSpace(thread.ThreadUrl))
                .Where(thread => hero is not null && (thread.Posts ?? []).Any(post =>
                    IsHeroAuthor(post.Author, aliases)
                    || MentionsAnyAlias(post.BodyText, aliases)))
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
            var aliases = GetHeroAliases(hero);
            return threadPosts
                .SelectMany(thread => (thread.Posts ?? [])
                    .Where(post => IsHeroAuthor(post.Author, aliases) || MentionsAnyAlias(post.BodyText, aliases))
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
            var aliases = GetHeroAliases(hero);
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

        private static string[] GetHeroAliases(MyHeroBriefingHeroSummary hero)
        {
            return new[] { hero.Name }
                .Concat(hero.Aliases)
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
            IReadOnlyList<MyHeroBriefingHeroChoice> heroChoices,
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
                ? "No authenticated hero is available for My Hero Briefing."
                : "No active heroes are available for My Hero Briefing.";
        }


        private sealed record MyHeroBriefingResolvedHero(
            PartyHeroSheet? Hero,
            MyHeroBriefingHeroIdentitySource Source,
            string? CanonicalName,
            IReadOnlyList<string> Aliases);
    }
}
