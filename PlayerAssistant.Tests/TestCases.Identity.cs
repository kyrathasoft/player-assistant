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
            new XpAuthenticatedIdentity(
                SyntheticIdentityFixtures[0].CanonicalId,
                SyntheticIdentityFixtures[0].FullName,
                [],
                false,
                SyntheticIdentityFixtures[0].CanonicalId));

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

    internal static void CollidingHeroDisplayNamesAreDenied()
    {
        var colliding = new[]
        {
            SyntheticIdentityFixtures[0].PartySheet with { Name = "Ari" },
            SyntheticIdentityFixtures[1].PartySheet with { Name = "Ari" }
        };
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(colliding, SelectedHeroName: "Ari"));
        Require(briefing.Hero is null, "a colliding hero display name selected the wrong hero");
    }

    internal static void CanonicalIdsSelectMatchingXpAndBriefingData()
    {
        var xpTotals = SyntheticIdentityFixtures
            .Select(fixture => new PcXpTotal(fixture.FullName, fixture.XpTotal, fixture.CanonicalId))
            .ToArray();
        var visible = PartyHeroUtility.WithVisibleXpTotals(
            SyntheticIdentityFixtures.Select(fixture => fixture.PartySheet with { XpTotal = null }).ToArray(),
            xpTotals,
            new XpAuthenticatedIdentity(
                SyntheticIdentityFixtures[1].CanonicalId,
                SyntheticIdentityFixtures[1].FullName,
                [],
                false,
                SyntheticIdentityFixtures[1].CanonicalId));
        Require(visible[0].XpTotal is null && visible[1].XpTotal == SyntheticIdentityFixtures[1].XpTotal,
            "canonical XP authorization selected the wrong same-first-name character");

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            visible,
            AuthenticatedHeroCanonicalId: SyntheticIdentityFixtures[1].CanonicalId,
            XpTotals: xpTotals));
        Require(briefing.Hero?.Name == SyntheticIdentityFixtures[1].FullName
            && briefing.Hero.XpTotal == SyntheticIdentityFixtures[1].XpTotal,
            "canonical briefing authorization selected the wrong character data");
    }

    internal static void RunCanonicalIdentityRegressionCases()
    {
        IdentityFixturesAreDistinctAndSynthetic();
        SuccessfulAuthenticationReturnsCanonicalIdentity();
        ExplicitAliasesAuthenticateOnlyTheirOwner();
        SidecarRejectsAmbiguousOrMalformedAliases();
        SidecarRejectsLegacySchemaAndDuplicateIdentities();
        CrossAccountPasswordAccessIsDenied();
        AmbiguousFirstNameAliasesAreDenied();
        UnknownCanonicalIdsAreDenied();
        MismatchedPasswordsAreDenied();
        CollidingHeroDisplayNamesAreDenied();
        CanonicalIdsSelectMatchingXpAndBriefingData();
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
