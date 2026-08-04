using PlayerAssistant;
using Microsoft.Playwright;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Windows.Forms;
using System.Xml.Linq;

using static TestSupport;

internal static class RpolSnapshotTests
{
    internal static void RpolSnapshotSignsAndVerifiesCanonicalPayload()
    {
        var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var payload = RpolSnapshotUtility.CreatePayload(
            new Uri("https://rpol.net/game.php?gi=80170"),
            "<html>campaign</html>",
            "text/html; charset=utf-8",
            DateTimeOffset.Parse("2026-07-16T12:00:00Z"),
            signingKey);

        AssertTrue(RpolSnapshotUtility.VerifySignature(payload, signingKey), "snapshot signature should verify");
        AssertEqual(
            "<html>campaign</html>",
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload.ContentBase64)),
            "transport-wrapped snapshot content should round-trip");
        AssertFalse(
            System.Text.RegularExpressions.Regex.IsMatch(payload.ContentBase64, "[A-Za-z0-9+/]{4}"),
            "snapshot transport should not expose four-character base64 phrases to request filtering");
        AssertFalse(
            RpolSnapshotUtility.VerifySignature(payload with { ContentSha256 = new string('0', 64) }, signingKey),
            "tampered snapshot metadata should fail signature verification");
    }

    internal static void AdventureOutlineFillsEveryChapterThroughLatestIcChapter()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-1.html"),
            """
            <html><body>
            <h1>Ch 1 - Kirkilston.</h1>
            <span class="messageauthor">Dungeon Master</span>
            <div class="messagebody">The party leaves town.</div>
            </body></html>
            """);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-3.html"),
            """
            <html><body>
            <h1>Ch 3 - The Road.</h1>
            <span class="messageauthor">Kelpie</span>
            <div class="messagebody">Kelpie keeps watch while the party travels.</div>
            </body></html>
            """);

        var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
            .GetAwaiter()
            .GetResult();

        AssertContains(outline, "## Ch 1 - Kirkilston");
        AssertContains(outline, "## Ch 2");
        AssertContains(outline, "- The in-character chapter source is not available yet.");
        AssertContains(outline, "## Ch 3 - The Road");
        AssertTrue(
            outline.IndexOf("## Ch 1", StringComparison.Ordinal) < outline.IndexOf("## Ch 2", StringComparison.Ordinal)
                && outline.IndexOf("## Ch 2", StringComparison.Ordinal) < outline.IndexOf("## Ch 3", StringComparison.Ordinal),
            "adventure outline chapters should form a contiguous numeric range");
    }

    internal static void RpolSnapshotRejectsAnotherGame()
    {
        var exception = AssertThrows<InvalidOperationException>(() =>
            RpolSnapshotUtility.ValidateSourceUri(new Uri("https://rpol.net/game.php?gi=12345")));
        AssertContains(exception.Message, "80170");
    }

    internal static void RpolSnapshotSanitizesCredentialsAndLoginForm()
    {
        using var credentialStoreScope = RuntimeSecretStoreUtility.UseBackendForTests(new InMemoryWindowsCredentialStoreBackend());
        RuntimeSecretStoreUtility.SaveRpolCredentials("admin-user", "secret-password");
        var sanitized = RpolSnapshotUtility.SanitizeHtml(
            "<html>admin-user secret-password<form action='/login.cgi'><input name='password'></form>safe</html>");

        AssertFalse(sanitized.Contains("admin-user", StringComparison.OrdinalIgnoreCase), "user name should be redacted");
        AssertFalse(sanitized.Contains("secret-password", StringComparison.Ordinal), "password should be redacted");
        AssertFalse(sanitized.Contains("login.cgi", StringComparison.OrdinalIgnoreCase), "login form should be removed");
        AssertContains(sanitized, "safe");
    }

    internal static void RpolSnapshotAcceptsSanitizedCampaignContent()
    {
        var html = "<html><title>Scarlet Horizons</title><body>" + new string('x', 1200) + "</body></html>";
        AssertTrue(RpolSnapshotUtility.IsUsableSnapshotHtml(html), "campaign HTML should be accepted after sanitization");
    }

    internal static void RpolSnapshotRejectsLoginOnlyContent()
    {
        var html = "<html><title>RPoL Login</title><body>" + new string('x', 1200)
            + "<form action='/login.cgi'><input name='username'><input name='password'></form></body></html>";
        AssertFalse(RpolSnapshotUtility.IsUsableSnapshotHtml(html), "login-only HTML should not be published");
    }

    internal static void RpolChallengeDetectionIgnoresPassiveCloudflareReferences()
    {
        AssertFalse(
            RpolAuthUtility.LooksLikeCloudflareChallengePage("<html><body>Protected by Cloudflare</body></html>"),
            "a passive Cloudflare reference should not be treated as a browser challenge");
        AssertTrue(
            RpolAuthUtility.LooksLikeCloudflareChallengePage("<title>Just a moment...</title>"),
            "a concrete Cloudflare challenge marker should still be detected");
    }

    internal static void RpolVerificationRecognizesAuthenticatedBrowserTitle()
    {
        AssertTrue(
            RpolAuthUtility.IsVerifiedRpolBrowserWindowTitle("RPoL: World of Issenda - Scarlet Horizons - Google Chrome"),
            "an authenticated RPOL window title should complete manual verification");
        AssertFalse(
            RpolAuthUtility.IsVerifiedRpolBrowserWindowTitle("Just a moment... - Google Chrome"),
            "a challenge window title should remain open");
    }

    internal static void RpolDiceRollerNavigationUsesGamePageReferrer()
    {
        AssertTrue(
            string.Equals(
                AppSettingsUtility.GameForumUrl,
                RpolAuthUtility.GetNavigationReferer(
                    new Uri("https://rpol.net/usermodules/diceroller.cgi?gi=80170")),
                StringComparison.Ordinal),
            "Dice Roller navigation should carry the configured game page as its referrer");
        AssertTrue(
            RpolAuthUtility.GetNavigationReferer(
                new Uri("https://rpol.net/display.cgi?gi=80170&ti=7")) is null,
            "ordinary RPOL navigation should not receive a synthetic referrer");
    }

    internal static void SnapshotPublisherStateAdvancesOneTargetAndWraps()
    {
        var root = new Uri("https://rpol.net/game.php?gi=80170");
        var cast = new Uri("https://rpol.net/gameinfo.php?action=cast&gi=80170");
        var state = RpolSnapshotUtility.CreatePublisherState([root, cast]);

        AssertEqual(root, RpolSnapshotUtility.GetNextSourceUri(state), "the root should be the initial publisher target");
        state = RpolSnapshotUtility.AdvancePublisherState(state);
        AssertEqual(cast, RpolSnapshotUtility.GetNextSourceUri(state), "one success should advance exactly one target");
        state = RpolSnapshotUtility.AdvancePublisherState(state);
        AssertEqual(root, RpolSnapshotUtility.GetNextSourceUri(state), "the publisher queue should wrap after the last target");
    }

    internal static void SnapshotDiscoveryApprovesGameLinksAndDiceRoller()
    {
        var gameLinksApproved = (bool)(InvokeStaticMethod(
            typeof(RpolSnapshotUtility),
            "IsApprovedLinkLabel",
            "Game Links") ?? false);
        var diceRollerApproved = (bool)(InvokeStaticMethod(
            typeof(RpolSnapshotUtility),
            "IsApprovedLinkLabel",
            "Die Roller") ?? false);
        var unrelated = (bool)(InvokeStaticMethod(
            typeof(RpolSnapshotUtility),
            "IsApprovedLinkLabel",
            "Edit Game") ?? true);

        AssertTrue(gameLinksApproved, "Game Links should be included in snapshot discovery");
        AssertTrue(diceRollerApproved, "Die Roller should be included in snapshot discovery");
        AssertFalse(unrelated, "unrelated game administration links should remain excluded");
    }

    internal static void SnapshotPublisherNormalizesThreadTargetsToShowAll()
    {
        var state = RpolSnapshotUtility.CreatePublisherState(
        [
            new Uri("https://rpol.net/game.php?gi=80170"),
            new Uri("https://rpol.net/display.cgi?gi=80170&ti=7&date=1779581880&msgpage=2")
        ]);

        AssertEqual(
            "https://rpol.net/display.cgi?gi=80170&ti=7&msgpage=&show=all",
            state.SourceUrls[1],
            "new publisher state should normalize thread targets");

        var legacyState = new RpolSnapshotPublisherState(
            1,
            [
                "https://rpol.net/game.php?gi=80170",
                "https://rpol.net/display.cgi?gi=80170&ti=7&date=1779581880&msgpage=2",
                "https://rpol.net/usermodules/diceroller.cgi?gi=80170"
            ],
            1);
        var normalized = RpolSnapshotUtility.EnsureRequiredSourceUris(legacyState);

        AssertEqual(
            "https://rpol.net/display.cgi?gi=80170&ti=7&msgpage=&show=all",
            normalized.SourceUrls[normalized.NextIndex],
            "legacy publisher state should normalize its current thread without losing the cursor");
    }

    internal static void SnapshotPublisherStateInjectsRequiredDiceRollerTarget()
    {
        var root = new Uri("https://rpol.net/game.php?gi=80170");
        var cast = new Uri("https://rpol.net/gameinfo.php?action=cast&gi=80170");
        var state = RpolSnapshotUtility.AdvancePublisherState(
            RpolSnapshotUtility.CreatePublisherState([root, cast]));

        var updated = RpolSnapshotUtility.EnsureRequiredSourceUris(state);

        AssertEqual(3, updated.SourceUrls.Count, "the required Dice Roller target should be inserted once");
        AssertEqual(
            "https://rpol.net/usermodules/diceroller.cgi?gi=80170",
            updated.SourceUrls[updated.NextIndex],
            "the newly required target should be the next publisher item");
        AssertEqual(cast.AbsoluteUri, updated.SourceUrls[updated.NextIndex + 1], "the previous next target should be preserved");
        AssertTrue(
            ReferenceEquals(updated, RpolSnapshotUtility.EnsureRequiredSourceUris(updated)),
            "a current publisher queue should not be rewritten");
    }

    internal static void SnapshotPublisherStatePersistsItsCursor()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "publisher-state.json");
        var state = RpolSnapshotUtility.AdvancePublisherState(
            RpolSnapshotUtility.CreatePublisherState(
            [
                new Uri("https://rpol.net/game.php?gi=80170"),
                new Uri("https://rpol.net/gameinfo.php?action=cast&gi=80170")
            ]));

        RpolSnapshotUtility.SavePublisherStateAsync(statePath, state).GetAwaiter().GetResult();
        var loaded = RpolSnapshotUtility.LoadPublisherState(statePath)
            ?? throw new InvalidOperationException("expected persisted publisher state");

        AssertEqual(1, loaded.NextIndex, "the persisted publisher cursor should be retained");
        AssertEqual(state.SourceUrls[1], loaded.SourceUrls[1], "the persisted publisher queue should be retained");
    }

    internal static void SnapshotPublisherStateRejectsInvalidCursor()
    {
        var state = new RpolSnapshotPublisherState(
            1,
            ["https://rpol.net/game.php?gi=80170"],
            1);

        var exception = AssertThrows<InvalidOperationException>(() => RpolSnapshotUtility.GetNextSourceUri(state));
        AssertContains(exception.Message, "cursor");
    }

    internal static void NetworkAllowlistAcceptsOnlyBrokerApiPath()
    {
        var accepted = NetworkUrlAllowlistUtility.Validate(
            "https://bryanmiller.us/scarlethorizons/api/v1/snapshots/page",
            NetworkUrlPurpose.PlayerAssistantBroker);
        var rejected = NetworkUrlAllowlistUtility.Validate(
            "https://bryanmiller.us/scarlethorizons/settings.local.json",
            NetworkUrlPurpose.PlayerAssistantBroker);

        AssertTrue(accepted.IsAllowed, "broker API path should be allowed");
        AssertFalse(rejected.IsAllowed, "non-broker paths should be rejected for broker requests");
    }

    internal static void SnapshotPublisherArgumentIsRecognized()
    {
        AssertTrue(PlayerAssistant.Program.IsPublishRpolSnapshotsArgument("--publish-rpol-snapshots"), "long snapshot argument should be recognized");
        AssertTrue(PlayerAssistant.Program.IsPublishRpolSnapshotsArgument("/publish-rpol-snapshots"), "slash snapshot argument should be recognized");
    }
}
