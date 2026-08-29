using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlayerAssistant;

namespace PlayerAssistant.Tests;

internal static partial class TestCases
{
    private sealed record SyntheticCharacterFixture(
        string FullName,
        string CanonicalId,
        string Password,
        int XpTotal,
        PartyHeroSheet PartySheet,
        string HeroBriefingData);

    private static readonly SyntheticCharacterFixture[] SyntheticIdentityFixtures =
    [
        new(
            "Ari Stoneward",
            "fixture-ari-stoneward-001",
            "synthetic-ari-stoneward-password",
            1_125,
            new PartyHeroSheet("Ari Stoneward", null, "Level 4", "Ranger", "31", "Ari Stoneward\nParty sheet: Stoneward", 1_125, "fixture-ari-stoneward-001"),
            "Briefing: Stoneward patrol at the north gate."),
        new(
            "Ari Valesong",
            "fixture-ari-valesong-002",
            "synthetic-ari-valesong-password",
            2_375,
            new PartyHeroSheet("Ari Valesong", null, "Level 7", "Bard", "48", "Ari Valesong\nParty sheet: Valesong", 2_375, "fixture-ari-valesong-002"),
            "Briefing: Valesong negotiates with the river guild.")
    ];

    internal static void IdentityFixturesAreDistinctAndSynthetic()
    {
        Require(SyntheticIdentityFixtures.Length == 2, "exactly two fixtures are required");
        Require(SyntheticIdentityFixtures.All(fixture => fixture.FullName.StartsWith("Ari ", StringComparison.Ordinal)), "fixtures must share a first name");
        Require(SyntheticIdentityFixtures.Select(fixture => fixture.FullName).Distinct(StringComparer.Ordinal).Count() == 2, "full names must be distinct");
        Require(SyntheticIdentityFixtures.Select(fixture => fixture.CanonicalId).Distinct(StringComparer.Ordinal).Count() == 2, "canonical IDs must be distinct");
        Require(SyntheticIdentityFixtures.Select(fixture => fixture.Password).Distinct(StringComparer.Ordinal).Count() == 2, "passwords must be distinct");
        Require(SyntheticIdentityFixtures[0].XpTotal != SyntheticIdentityFixtures[1].XpTotal, "XP totals must be distinct");
        Require(SyntheticIdentityFixtures[0].PartySheet.CharacterSheetText != SyntheticIdentityFixtures[1].PartySheet.CharacterSheetText, "party sheets must be distinct");
        Require(SyntheticIdentityFixtures[0].HeroBriefingData != SyntheticIdentityFixtures[1].HeroBriefingData, "hero briefings must be distinct");
        Require(SyntheticIdentityFixtures.All(fixture => fixture.Password.StartsWith("synthetic-", StringComparison.Ordinal)), "fixtures must not use production credentials");
    }

    internal static void CrossAccountPasswordAccessIsDenied()
    {
        using var directory = CreateSyntheticPasswordSidecar();
        Require(XpPasswordStoreUtility.ValidatePassword(
            SyntheticIdentityFixtures[1].CanonicalId,
            SyntheticIdentityFixtures[1].FullName,
            SyntheticIdentityFixtures[0].Password,
            directory.Path) is null, "one Ari password authenticated the other Ari account");
    }

    internal static void SuccessfulAuthenticationReturnsCanonicalIdentity()
    {
        using var directory = CreateSyntheticPasswordSidecar();
        var identity = XpPasswordStoreUtility.ValidatePassword(
            SyntheticIdentityFixtures[1].CanonicalId,
            SyntheticIdentityFixtures[1].FullName,
            SyntheticIdentityFixtures[1].Password,
            directory.Path);

        if (identity is null)
        {
            throw new InvalidOperationException("matching synthetic credentials should return an identity");
        }

        Require(identity.CanonicalId == SyntheticIdentityFixtures[1].CanonicalId, "authentication returned the wrong canonical ID");
        Require(identity.CanonicalName == SyntheticIdentityFixtures[1].FullName, "authentication returned the wrong canonical name");
        Require(identity.AccountScope == SyntheticIdentityFixtures[1].CanonicalId, "authentication returned the wrong account scope");
        Require(!identity.IsDungeonMaster, "a player identity was incorrectly granted Dungeon Master scope");
        Require(identity.Aliases.Count == 0, "a v2 sidecar unexpectedly inferred aliases");
    }

    internal static void AccountSwitchingReturnsNewCanonicalIdentity()
    {
        using var directory = CreateSyntheticPasswordSidecar();
        var firstIdentity = XpPasswordStoreUtility.ValidatePassword(
            SyntheticIdentityFixtures[0].FullName,
            SyntheticIdentityFixtures[0].Password,
            directory.Path);
        var secondIdentity = XpPasswordStoreUtility.ValidatePassword(
            SyntheticIdentityFixtures[1].FullName,
            SyntheticIdentityFixtures[1].Password,
            directory.Path);

        Require(firstIdentity?.CanonicalId == SyntheticIdentityFixtures[0].CanonicalId,
            "the first account did not authenticate to its canonical identity");
        Require(secondIdentity?.CanonicalId == SyntheticIdentityFixtures[1].CanonicalId,
            "account switching did not return the new canonical identity");
        Require(firstIdentity?.CanonicalId != secondIdentity?.CanonicalId,
            "account switching retained the previous canonical identity");
        Require(secondIdentity?.AccountScope == SyntheticIdentityFixtures[1].CanonicalId,
            "account switching retained the previous account scope");
    }

    internal static void ExplicitAliasesAuthenticateOnlyTheirOwner()
    {
        using var directory = TemporaryDirectory.Create();
        XpPasswordStoreUtility.SavePasswordHashes(
            Path.Combine(directory.Path, XpPasswordStoreUtility.FileName),
            [
                new XpPasswordStoreUtility.PasswordIdentityInput(
                    SyntheticIdentityFixtures[0].CanonicalId,
                    SyntheticIdentityFixtures[0].FullName,
                    SyntheticIdentityFixtures[0].Password,
                    ["Stonewarden"]),
                new XpPasswordStoreUtility.PasswordIdentityInput(
                    SyntheticIdentityFixtures[1].CanonicalId,
                    SyntheticIdentityFixtures[1].FullName,
                    SyntheticIdentityFixtures[1].Password,
                    ["Valesong Bard"])
            ]);

        var identity = XpPasswordStoreUtility.ValidatePassword(
            "Stonewarden",
            SyntheticIdentityFixtures[0].Password,
            directory.Path);
        Require(identity?.CanonicalId == SyntheticIdentityFixtures[0].CanonicalId, "explicit alias resolved the wrong account");
        Require(XpPasswordStoreUtility.ValidatePassword("Ari", SyntheticIdentityFixtures[0].Password, directory.Path) is null,
            "an inferred first-name alias authenticated an account");
    }

    internal static void DungeonMasterScopeUsesStableCanonicalId()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
        XpPasswordStoreUtility.SavePasswordHashes(
            path,
            [
                new XpPasswordStoreUtility.PasswordIdentityInput(
                    "ordinary-player",
                    "Dungeon Master",
                    "ordinary player password",
                    []),
                new XpPasswordStoreUtility.PasswordIdentityInput(
                    "dungeon-master",
                    "Game Referee",
                    "game referee password",
                    [],
                    true)
            ]);

        var displayOnly = XpPasswordStoreUtility.ValidatePassword(
            "Dungeon Master",
            "ordinary player password",
            directory.Path);
        var stableIdentity = XpPasswordStoreUtility.ValidatePassword(
            "Game Referee",
            "game referee password",
            directory.Path);

        Require(displayOnly?.IsDungeonMaster == false, "Dungeon Master display text granted DM scope");
        Require(stableIdentity?.IsDungeonMaster == true, "stable Dungeon Master identity did not grant DM scope");
    }

    internal static void IdentityRegistryLoadsCanonicalAliases()
    {
        using var directory = CreateSyntheticPasswordSidecar(
            aliasesForFirst: ["Stonewarden"],
            aliasesForSecond: ["Valesong Bard"]);

        var registry = XpPasswordStoreUtility.LoadIdentityRegistry(directory.Path);

        Require(registry.Count == 2, "identity registry returned the wrong account count");
        Require(
            registry.Any(identity =>
                identity.CanonicalId == SyntheticIdentityFixtures[0].CanonicalId
                && identity.CanonicalName == SyntheticIdentityFixtures[0].FullName
                && identity.Aliases.SequenceEqual(["Stonewarden"], StringComparer.Ordinal)),
            "identity registry did not preserve the first account's canonical identity and aliases");
    }

    internal static void OpaqueDungeonMasterCanonicalIdPreservesScope()
    {
        using var directory = TemporaryDirectory.Create();
        var password = "opaque dungeon master password";
        var salt = Encoding.UTF8.GetBytes("synthetic-opaque-dm-salt-16");
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, XpPasswordStoreUtility.MinimumIterations,
            HashAlgorithmName.SHA256, 32);
        File.WriteAllText(
            Path.Combine(directory.Path, XpPasswordStoreUtility.FileName),
            JsonSerializer.Serialize(new
            {
                schema_version = XpPasswordStoreUtility.SchemaVersion,
                format = XpPasswordStoreUtility.Format,
                entries = new[]
                {
                    new
                    {
                        canonical_id = "d722dd35dd775c91a5d55339b62c45bc",
                        canonical_name = "Dungeon Master",
                        aliases = Array.Empty<string>(),
                        is_dungeon_master = true,
                        algorithm = XpPasswordStoreUtility.Algorithm,
                        iterations = XpPasswordStoreUtility.MinimumIterations,
                        salt = Convert.ToBase64String(salt),
                        hash = Convert.ToBase64String(hash)
                    }
                }
            }));

        var identity = XpPasswordStoreUtility.ValidatePassword("Dungeon Master", password, directory.Path);
        Require(identity?.CanonicalId == "d722dd35dd775c91a5d55339b62c45bc", "opaque Dungeon Master canonical ID did not authenticate");
        Require(identity?.IsDungeonMaster == true, "Dungeon Master scope was inferred from a retired literal ID instead of migrated identity data");
    }

    internal static void SidecarRejectsAmbiguousOrMalformedAliases()
    {
        foreach (var aliases in new[]
        {
            new[] { "Ari Valesong" },
            new[] { "Shared", "shared" },
            new[] { " Shared" },
        })
        {
            using var directory = CreateSyntheticPasswordSidecar(aliases, aliasesForSecond: ["Shared"]);
            var exception = AssertThrows<InvalidOperationException>(() => XpPasswordStoreUtility.LoadPasswordHashes(directory.Path));
            Require(exception.Message.Contains("alias", StringComparison.OrdinalIgnoreCase), "invalid alias rejection was not explicit");
        }
    }

    internal static void SidecarRejectsLegacySchemaAndDuplicateIdentities()
    {
        using (var legacyDirectory = CreateSyntheticPasswordSidecar(schemaVersion: 1, format: "xp-password-hashes-v1"))
        {
            var exception = AssertThrows<InvalidOperationException>(() => XpPasswordStoreUtility.LoadPasswordHashes(legacyDirectory.Path));
            Require(exception.Message.Contains("schema_version 2", StringComparison.Ordinal), "legacy sidecar schema was accepted");
        }

        using (var duplicateIdDirectory = CreateSyntheticPasswordSidecar(canonicalIdForSecond: SyntheticIdentityFixtures[0].CanonicalId))
        {
            AssertThrows<InvalidOperationException>(() => XpPasswordStoreUtility.LoadPasswordHashes(duplicateIdDirectory.Path));
        }

        using (var duplicateNameDirectory = CreateSyntheticPasswordSidecar(canonicalNameForSecond: SyntheticIdentityFixtures[0].FullName))
        {
            AssertThrows<InvalidOperationException>(() => XpPasswordStoreUtility.LoadPasswordHashes(duplicateNameDirectory.Path));
        }

    }

    internal static void LegacyNameLookupReproducesCrossIdentityLeak()
    {
        using var directory = CreateSyntheticPasswordSidecar();
        Require(
            XpPasswordStoreUtility.ValidatePassword(
                null,
                SyntheticIdentityFixtures[0].FullName,
                SyntheticIdentityFixtures[0].Password,
                directory.Path) is not null,
            "synthetic Character A should authenticate for the baseline harness");

        var characterAWithoutIdentity = SyntheticIdentityFixtures[0].PartySheet with
        {
            CanonicalId = null,
            XpTotal = null
        };
        var protectedView = PartyHeroUtility.WithVisibleXpTotals(
            [characterAWithoutIdentity],
            [new PcXpTotal(
                SyntheticIdentityFixtures[1].FullName,
                SyntheticIdentityFixtures[1].XpTotal,
                SyntheticIdentityFixtures[1].CanonicalId)],
            XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(SyntheticIdentityFixtures[0].CanonicalId, SyntheticIdentityFixtures[0].FullName, [], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player)));

        Require(
            protectedView[0].XpTotal is null,
            "an authenticated identity must not authorize XP for a different canonical ID");
    }

    internal static void AmbiguousFirstNameAliasesAreDenied()
    {
        using var directory = CreateSyntheticPasswordSidecar();
        Require(XpPasswordStoreUtility.ValidatePassword(
            null,
            "Ari",
            SyntheticIdentityFixtures[0].Password,
            directory.Path) is null, "an ambiguous first-name alias authenticated an account");
    }

    internal static void UnknownCanonicalIdsAreDenied()
    {
        using var directory = CreateSyntheticPasswordSidecar();
        Require(XpPasswordStoreUtility.ValidatePassword(
            "fixture-ari-missing-999",
            SyntheticIdentityFixtures[0].FullName,
            SyntheticIdentityFixtures[0].Password,
            directory.Path) is null, "an unknown canonical ID authenticated an account");
    }

    internal static void MismatchedPasswordsAreDenied()
    {
        using var directory = CreateSyntheticPasswordSidecar();
        Require(XpPasswordStoreUtility.ValidatePassword(
            SyntheticIdentityFixtures[0].CanonicalId,
            SyntheticIdentityFixtures[0].FullName,
            SyntheticIdentityFixtures[1].Password,
            directory.Path) is null, "a mismatched password authenticated an account");
    }

    internal static void CollidingHeroDisplayNamesResolveOnlyByCanonicalId()
    {
        var colliding = new[]
        {
            SyntheticIdentityFixtures[0].PartySheet with { Name = "Ari" },
            SyntheticIdentityFixtures[1].PartySheet with { Name = "Ari" }
        };
        var dungeonMasterIdentity = XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("dm", "Dungeon Master", [], true ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player));
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            colliding,
            SelectedHeroName: "Ari",
            SelectedHeroCanonicalId: SyntheticIdentityFixtures[1].CanonicalId,
            AuthenticatedIdentity: dungeonMasterIdentity));

        Require(
            briefing.Hero?.CharacterClass == SyntheticIdentityFixtures[1].PartySheet.CharacterClass
                && briefing.Hero.Level == SyntheticIdentityFixtures[1].PartySheet.Level,
            "a colliding display name overrode the selected stable identity");
    }

    internal static void CanonicalIdsSelectMatchingXpAndBriefingData()
    {
        var xpTotals = new[]
        {
            new PcXpTotal(
                SyntheticIdentityFixtures[1].FullName,
                SyntheticIdentityFixtures[0].XpTotal,
                SyntheticIdentityFixtures[0].CanonicalId),
            new PcXpTotal(
                SyntheticIdentityFixtures[0].FullName,
                SyntheticIdentityFixtures[1].XpTotal,
                SyntheticIdentityFixtures[1].CanonicalId)
        };
        var visible = PartyHeroUtility.WithVisibleXpTotals(
            SyntheticIdentityFixtures.Select(fixture => fixture.PartySheet with { XpTotal = null }).ToArray(),
            xpTotals,
            XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(SyntheticIdentityFixtures[1].CanonicalId, SyntheticIdentityFixtures[1].FullName, [], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player)));
        Require(visible[0].XpTotal is null && visible[1].XpTotal == SyntheticIdentityFixtures[1].XpTotal,
            "canonical XP authorization selected the wrong same-first-name character");

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            SyntheticIdentityFixtures.Select(fixture => fixture.PartySheet with { XpTotal = null }).ToArray(),
            XpTotals: xpTotals,
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(SyntheticIdentityFixtures[1].CanonicalId, SyntheticIdentityFixtures[1].FullName, [], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player))));
        Require(briefing.Hero?.Name == SyntheticIdentityFixtures[1].FullName
            && briefing.Hero.XpTotal == SyntheticIdentityFixtures[1].XpTotal,
            "canonical briefing authorization selected the wrong character data");
    }

    internal static void MyHeroBriefingRejectsNameOnlyProtectedIdentity()
    {
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            SyntheticIdentityFixtures.Select(fixture => fixture.PartySheet).ToArray(),
            AuthenticatedHeroName: SyntheticIdentityFixtures[0].FullName,
            XpTotals:
            [
                new PcXpTotal(
                    SyntheticIdentityFixtures[0].FullName,
                    SyntheticIdentityFixtures[0].XpTotal,
                    SyntheticIdentityFixtures[0].CanonicalId)
            ],
            ThreadPosts:
            [
                new MyHeroBriefingThreadPosts(
                    "Synthetic thread",
                    "https://example.invalid/thread",
                    [CreateRpolThreadPost(1, SyntheticIdentityFixtures[0].FullName, SyntheticIdentityFixtures[0].HeroBriefingData)])
            ],
            EncryptedTextIndex:
            [
                new EncryptedTextIndexEntry(
                    "https://example.invalid/secret",
                    1,
                    [$"Hero {SyntheticIdentityFixtures[0].FullName}"])
            ]));

        Require(briefing.Hero is null, "a display name without canonical identity resolved protected briefing data");
        Require(briefing.HeroCard is null, "a display name without canonical identity produced a protected hero card");
        Require(briefing.RecentActivity.Count == 0, "a display name without canonical identity exposed activity");
        Require(briefing.LikelyResponseItems.Count == 0, "a display name without canonical identity exposed response data");
        Require(briefing.UnlockedNotes.Count == 0, "a display name without canonical identity exposed encrypted-note metadata");
    }

    internal static void MyHeroBriefingRejectsUnauthenticatedCanonicalId()
    {
        var hero = SyntheticIdentityFixtures[0];
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            [hero.PartySheet],
            AuthenticatedHeroCanonicalId: hero.CanonicalId,
            XpTotals: [new PcXpTotal(hero.FullName, hero.XpTotal, hero.CanonicalId)]));

        Require(briefing.Hero is null, "an unauthenticated canonical ID resolved protected briefing data");
        Require(briefing.HeroCard is null, "an unauthenticated canonical ID produced a protected hero card");
    }

    internal static void MyHeroBriefingRejectsNameOnlyDungeonMasterSelection()
    {
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            SyntheticIdentityFixtures.Select(fixture => fixture.PartySheet).ToArray(),
            SelectedHeroName: SyntheticIdentityFixtures[0].FullName,
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("dungeon-master", "Game Referee", [], true ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player))));

        Require(briefing.Hero is null, "a Dungeon Master display-name selection resolved protected briefing data");
        Require(briefing.HeroCard is null, "a Dungeon Master display-name selection produced a protected hero card");
    }

    internal static void MyHeroBriefingDoesNotInferFirstNameAliases()
    {
        var hero = SyntheticIdentityFixtures[0];
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            [hero.PartySheet],
            ThreadPosts:
            [
                new MyHeroBriefingThreadPosts(
                    "Synthetic thread",
                    "https://example.invalid/thread",
                    [
                        CreateRpolThreadPost(1, "Ari", "Ari examines the gate."),
                        CreateRpolThreadPost(2, SyntheticIdentityFixtures[1].FullName, "Ari, what did you find?")
                    ])
            ],
            EncryptedTextIndex:
            [
                new EncryptedTextIndexEntry(
                    "https://example.invalid/ari-only",
                    1,
                    ["Hero Ari"])
            ],
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(hero.CanonicalId, hero.FullName, [], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player))));

        Require(briefing.Hero?.Name == hero.FullName, "canonical identity did not resolve the intended hero");
        Require(briefing.RecentActivity.Count == 0, "an inferred first-name alias exposed another hero's activity");
        Require(briefing.LikelyResponseItems.Count == 0, "an inferred first-name alias exposed another hero's response items");
        Require(briefing.UnlockedNotes.Count == 0, "an inferred first-name alias exposed encrypted-note metadata");
    }

    internal static void MyHeroBriefingIgnoresStalePartyDisplayNameForNoteAccess()
    {
        var hero = SyntheticIdentityFixtures[0];
        var rival = SyntheticIdentityFixtures[1];
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            [hero.PartySheet with { Name = rival.FullName }],
            EncryptedTextIndex:
            [
                new EncryptedTextIndexEntry(
                    "https://example.invalid/rival-only",
                    1,
                    [$"Hero {rival.FullName}"])
            ],
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(hero.CanonicalId, hero.FullName, [], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player))));

        Require(briefing.Hero is not null, "canonical identity did not resolve the stale party row");
        Require(briefing.UnlockedNotes.Count == 0, "stale party display name granted rival note access");
    }

    internal static void MyHeroBriefingUsesExplicitIdentityAliases()
    {
        var hero = SyntheticIdentityFixtures[0];
        var rival = SyntheticIdentityFixtures[1];
        var identity = XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(hero.CanonicalId, hero.FullName, ["Stonewarden"], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player));
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            [hero.PartySheet with { XpTotal = null }, rival.PartySheet with { XpTotal = null }],
            AuthenticatedIdentity: identity,
            XpTotals: [new PcXpTotal(hero.FullName, hero.XpTotal, hero.CanonicalId)],
            ThreadPosts:
            [
                new MyHeroBriefingThreadPosts(
                    "Synthetic thread",
                    "https://example.invalid/thread",
                    [
                        CreateRpolThreadPost(0, "Valesong Bard", "I sing about another road."),
                        CreateRpolThreadPost(1, "Stonewarden", "I examine the gate."),
                        CreateRpolThreadPost(2, rival.FullName, "Stonewarden, what did you find?")
                    ])
            ],
            EncryptedTextIndex:
            [
                new EncryptedTextIndexEntry(
                    "https://example.invalid/stonewarden-only",
                    1,
                    ["Hero Stonewarden"]),
                new EncryptedTextIndexEntry(
                    "https://example.invalid/valesong-only",
                    1,
                    ["Hero Valesong Bard"])
            ]));

        Require(briefing.Hero?.Name == hero.FullName, "explicit identity did not resolve the canonical hero");
        Require(briefing.Hero?.XpTotal == hero.XpTotal, "explicit identity did not resolve canonical XP");
        Require(briefing.RecentActivity.Count == 2, "explicit identity alias did not match hero activity");
        Require(briefing.LikelyResponseItems.Count == 1, "explicit identity alias did not detect a response");
        Require(briefing.UnlockedNotes.Count == 1, "explicit identity alias did not authorize encrypted-note metadata");
        Require(
            briefing.UnlockedNotes[0].Url == "https://example.invalid/stonewarden-only",
            "explicit identity inherited a rival hero's encrypted-note metadata");
        Require(
            briefing.RecentActivity.All(item => !item.Excerpt.Contains("another road", StringComparison.OrdinalIgnoreCase)),
            "explicit identity inherited a rival hero's activity");
    }

    internal static void MyHeroBriefingDungeonMasterChoicesCarryCanonicalIds()
    {
        var dungeonMasterIdentity = XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("fixture-dungeon-master-001", "Dungeon Master", [], true ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player));
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            SyntheticIdentityFixtures.Select(fixture => fixture.PartySheet).ToArray(),
            AuthenticatedIdentity: dungeonMasterIdentity));

        Require(briefing.NeedsHeroSelection, "Dungeon Master briefing did not request a hero selection");
        Require(briefing.HeroChoices.Count == 2, "Dungeon Master briefing returned the wrong choice count");
        Require(
            briefing.HeroChoices.Any(choice =>
                choice.CanonicalId == SyntheticIdentityFixtures[0].CanonicalId
                && choice.DisplayName == SyntheticIdentityFixtures[0].FullName),
            "Dungeon Master choice did not carry the first hero's stable identity");
        Require(
            briefing.HeroChoices.Any(choice =>
                choice.CanonicalId == SyntheticIdentityFixtures[1].CanonicalId
                && choice.DisplayName == SyntheticIdentityFixtures[1].FullName),
            "Dungeon Master choice did not carry the second hero's stable identity");
    }

    internal static void MyHeroBriefingDungeonMasterSelectionUsesSelectedIdentityAliases()
    {
        var selectedHero = SyntheticIdentityFixtures[0];
        var selectedIdentity = XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(selectedHero.CanonicalId, selectedHero.FullName, ["Stonewarden"], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player));
        var dungeonMasterIdentity = XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("fixture-dungeon-master-001", "Dungeon Master", [], true ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player));
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            SyntheticIdentityFixtures.Select(fixture => fixture.PartySheet with { XpTotal = null }).ToArray(),
            SelectedHeroCanonicalId: selectedHero.CanonicalId,
            XpTotals: [new PcXpTotal(selectedHero.FullName, selectedHero.XpTotal, selectedHero.CanonicalId)],
            ThreadPosts:
            [
                new MyHeroBriefingThreadPosts(
                    "Synthetic thread",
                    "https://example.invalid/thread",
                    [CreateRpolThreadPost(1, "Stonewarden", "I examine the gate.")])
            ],
            EncryptedTextIndex:
            [
                new EncryptedTextIndexEntry(
                    "https://example.invalid/stonewarden-only",
                    1,
                    ["Hero Stonewarden"])
            ],
            AuthenticatedIdentity: dungeonMasterIdentity,
            IdentityRegistry: [selectedIdentity, dungeonMasterIdentity]));

        Require(briefing.Hero?.Name == selectedHero.FullName, "Dungeon Master stable-ID selection resolved the wrong hero");
        Require(briefing.Hero?.XpTotal == selectedHero.XpTotal, "Dungeon Master selection resolved the wrong XP total");
        Require(briefing.RecentActivity.Count == 1, "selected identity alias did not match hero activity");
        Require(briefing.UnlockedNotes.Count == 1, "selected identity alias did not authorize encrypted-note metadata");
    }

    internal static void MyHeroBriefingDungeonMasterSelectionUsesCanonicalIdAfterRosterRename()
    {
        var selectedHero = SyntheticIdentityFixtures[0];
        var selectedIdentity = XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(selectedHero.CanonicalId, selectedHero.FullName, ["Stonewarden"], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player));
        var dungeonMasterIdentity = XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("fixture-dungeon-master-001", "Dungeon Master", [], true ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player));
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            [selectedHero.PartySheet with { Name = "Ari Renamed" }],
            SelectedHeroCanonicalId: selectedHero.CanonicalId,
            EncryptedTextIndex:
            [
                new EncryptedTextIndexEntry(
                    "https://example.invalid/stonewarden-only",
                    1,
                    ["Hero Stonewarden"])
            ],
            AuthenticatedIdentity: dungeonMasterIdentity,
            IdentityRegistry: [selectedIdentity, dungeonMasterIdentity]));

        Require(briefing.Hero is not null, "stable-ID selection did not resolve the renamed roster hero");
        Require(
            briefing.UnlockedNotes.Count == 1,
            "a mutable roster display name blocked the selected stable identity's explicit alias");
    }

    internal static void MyHeroBriefingQuickLinksFollowResolvedIdentity()
    {
        var hero = SyntheticIdentityFixtures[0];
        var identity = XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(hero.CanonicalId, hero.FullName, ["Stonewarden"], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player));
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            [hero.PartySheet],
            ThreadPosts:
            [
                new MyHeroBriefingThreadPosts(
                    "Stoneward patrol",
                    "https://example.invalid/relevant",
                    [CreateRpolThreadPost(1, "Stonewarden", "I inspect the road.")]),
                new MyHeroBriefingThreadPosts(
                    "Valesong council",
                    "https://example.invalid/unrelated",
                    [CreateRpolThreadPost(1, SyntheticIdentityFixtures[1].FullName, "The council convenes.")])
            ],
            AuthenticatedIdentity: identity));

        Require(
            briefing.QuickLinks.Any(link => link.Target == "https://example.invalid/relevant"),
            "resolved identity did not retain its relevant thread quick link");
        Require(
            briefing.QuickLinks.All(link => link.Target != "https://example.invalid/unrelated"),
            "resolved identity inherited another hero's thread quick link");
    }

    internal static void FirstNameOnlyInputsCannotAuthenticateOrSelectProtectedHero()
    {
        using var directory = CreateSyntheticPasswordSidecar();
        Require(
            XpPasswordStoreUtility.ValidatePassword("Ari", SyntheticIdentityFixtures[0].Password, directory.Path) is null,
            "a first-name-only login input authenticated an ambiguous identity");

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            SyntheticIdentityFixtures.Select(fixture => fixture.PartySheet).ToArray(),
            SelectedHeroName: "Ari",
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(SyntheticIdentityFixtures[0].CanonicalId, SyntheticIdentityFixtures[0].FullName, [], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player))));

        Require(briefing.Hero?.Name == SyntheticIdentityFixtures[0].FullName,
            "an authenticated canonical identity was incorrectly replaced by a first-name selection");
        Require(briefing.Hero!.Name != "Ari Valesong",
            "a first-name-only selection crossed into the other same-first-name hero");
    }

    internal static void AccountSwitchingDoesNotRetainPreviousIdentity()
    {
        var first = SyntheticIdentityFixtures[0];
        var second = SyntheticIdentityFixtures[1];
        var firstBriefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            SyntheticIdentityFixtures.Select(fixture => fixture.PartySheet with { XpTotal = null }).ToArray(),
            XpTotals: [new PcXpTotal(first.FullName, first.XpTotal, first.CanonicalId)],
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(first.CanonicalId, first.FullName, [], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player))));
        var secondBriefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            SyntheticIdentityFixtures.Select(fixture => fixture.PartySheet with { XpTotal = null }).ToArray(),
            XpTotals: [new PcXpTotal(second.FullName, second.XpTotal, second.CanonicalId)],
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(second.CanonicalId, second.FullName, [], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player))));

        Require(firstBriefing.Hero?.Name == first.FullName && firstBriefing.Hero!.XpTotal == first.XpTotal,
            "the first account did not receive its own canonical briefing data");
        Require(secondBriefing.Hero?.Name == second.FullName && secondBriefing.Hero!.XpTotal == second.XpTotal,
            "the switched account did not receive its own canonical briefing data");
        Require(secondBriefing.Hero!.Name != first.FullName,
            "account switching retained the previous account's hero identity");
    }

    internal static void AuthenticatedIdentityFactoryDerivesRoleAndScope()
    {
        var player = XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(
            "fixture-player-001", "Ari Stoneward", [], XpIdentityRole.Player));
        var dungeonMaster = XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(
            "fixture-dm-001", "Dungeon Master", [], XpIdentityRole.DungeonMaster));

        Require(!player.IsDungeonMaster && player.AccountScope == player.CanonicalId,
            "player role and account scope were not derived from the canonical identity record");
        Require(dungeonMaster.IsDungeonMaster && dungeonMaster.AccountScope == XpAuthenticatedIdentity.DungeonMasterScope,
            "Dungeon Master role and scope were not derived from the canonical identity record");
    }

    internal static void AuthenticatedIdentityFactoryRejectsImpossibleCanonicalRole()
    {
        Require(typeof(XpAuthenticatedIdentity).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .All(constructor => constructor.IsPrivate),
            "authenticated identity exposes a forgeable constructor");
        Require(typeof(XpAuthenticatedIdentity).GetProperty(nameof(XpAuthenticatedIdentity.AccountScope))?.CanWrite != true,
            "authenticated account scope is independently writable");
        Require(typeof(XpAuthenticatedIdentity).GetProperty(nameof(XpAuthenticatedIdentity.IsDungeonMaster))?.CanWrite != true,
            "authenticated role is independently writable");
    }

    internal static void ProtectedBoundariesRejectForgedIdentityState()
    {
        var forged = XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord(
            SyntheticIdentityFixtures[0].CanonicalId, SyntheticIdentityFixtures[0].FullName, [], XpIdentityRole.Player));
        var heroes = SyntheticIdentityFixtures.Select(fixture => fixture.PartySheet with { XpTotal = null }).ToArray();
        var totals = SyntheticIdentityFixtures.Select(fixture => new PcXpTotal(fixture.FullName, fixture.XpTotal, fixture.CanonicalId)).ToArray();
        var party = PartyHeroUtility.WithVisibleXpTotals(heroes, totals, forged);
        Require(party.Single(hero => hero.CanonicalId == forged.CanonicalId).XpTotal == SyntheticIdentityFixtures[0].XpTotal,
            "party boundary did not enforce the factory-derived player scope");
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes, AuthenticatedIdentity: forged, XpTotals: totals, SelectedHeroCanonicalId: SyntheticIdentityFixtures[1].CanonicalId));
        Require(briefing.Hero?.Name == forged.CanonicalName && !briefing.NeedsHeroSelection,
            "briefing boundary accepted a player identity as Dungeon Master scope");
        Require(XpTrackingUtility.FindXpTotalForIdentity(totals, forged)?.CanonicalId == forged.CanonicalId,
            "XP boundary did not retain canonical identity enforcement");
    }

    // Compatibility names retained for existing catalog entries during merge resolution.
    internal static void XpIdentityRejectsSameFirstNameCrossAccountPassword()
        => CrossAccountPasswordAccessIsDenied();

    internal static void XpIdentityAcceptsExplicitUniqueAlias()
        => ExplicitAliasesAuthenticateOnlyTheirOwner();

    internal static void RunCanonicalIdentityRegressionCases()
    {
        IdentityFixturesAreDistinctAndSynthetic();
        SuccessfulAuthenticationReturnsCanonicalIdentity();
        AccountSwitchingReturnsNewCanonicalIdentity();
        ExplicitAliasesAuthenticateOnlyTheirOwner();
        DungeonMasterScopeUsesStableCanonicalId();
        OpaqueDungeonMasterCanonicalIdPreservesScope();
        IdentityRegistryLoadsCanonicalAliases();
        SidecarRejectsAmbiguousOrMalformedAliases();
        SidecarRejectsLegacySchemaAndDuplicateIdentities();
        CrossAccountPasswordAccessIsDenied();
        AmbiguousFirstNameAliasesAreDenied();
        UnknownCanonicalIdsAreDenied();
        MismatchedPasswordsAreDenied();
        CollidingHeroDisplayNamesResolveOnlyByCanonicalId();
        CanonicalIdsSelectMatchingXpAndBriefingData();
        MyHeroBriefingRejectsNameOnlyProtectedIdentity();
        MyHeroBriefingRejectsUnauthenticatedCanonicalId();
        MyHeroBriefingDoesNotInferFirstNameAliases();
        MyHeroBriefingIgnoresStalePartyDisplayNameForNoteAccess();
        MyHeroBriefingUsesExplicitIdentityAliases();
        MyHeroBriefingDungeonMasterChoicesCarryCanonicalIds();
        MyHeroBriefingDungeonMasterSelectionUsesSelectedIdentityAliases();
        MyHeroBriefingDungeonMasterSelectionUsesCanonicalIdAfterRosterRename();
        MyHeroBriefingQuickLinksFollowResolvedIdentity();
        FirstNameOnlyInputsCannotAuthenticateOrSelectProtectedHero();
        AccountSwitchingDoesNotRetainPreviousIdentity();
    }

    private static TemporaryDirectory CreateSyntheticPasswordSidecar(
        IReadOnlyList<string>? aliasesForFirst = null,
        IReadOnlyList<string>? aliasesForSecond = null,
        int schemaVersion = XpPasswordStoreUtility.SchemaVersion,
        string? format = null,
        string? canonicalIdForSecond = null,
        string? canonicalNameForSecond = null)
    {
        var directory = TemporaryDirectory.Create();
        var entries = SyntheticIdentityFixtures.Select((fixture, index) => new
        {
            canonical_name = index == 1 && canonicalNameForSecond is not null ? canonicalNameForSecond : fixture.FullName,
            canonical_id = index == 1 && canonicalIdForSecond is not null ? canonicalIdForSecond : fixture.CanonicalId,
            aliases = (index == 0 ? aliasesForFirst : aliasesForSecond) ?? [],
            is_dungeon_master = false,
            algorithm = XpPasswordStoreUtility.Algorithm,
            iterations = XpPasswordStoreUtility.MinimumIterations,
            salt = Convert.ToBase64String(Encoding.UTF8.GetBytes($"synthetic-salt-{index:00}-fixed")),
            hash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
                fixture.Password,
                Encoding.UTF8.GetBytes($"synthetic-salt-{index:00}-fixed"),
                XpPasswordStoreUtility.MinimumIterations,
                HashAlgorithmName.SHA256,
                32))
        }).ToArray();
        var document = new { schema_version = schemaVersion, format = format ?? XpPasswordStoreUtility.Format, entries };
        File.WriteAllText(
            System.IO.Path.Combine(directory.Path, XpPasswordStoreUtility.FileName),
            JsonSerializer.Serialize(document));
        return directory;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
