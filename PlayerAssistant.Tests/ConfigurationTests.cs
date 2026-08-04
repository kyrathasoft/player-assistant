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

internal static class ConfigurationTests
{
    internal static void AppConfigurationValidationAcceptsCompleteRuntime()
    {
        using var directory = TemporaryDirectory.Create();
        WriteRequiredRuntimeSidecars(directory.Path);

        var report = AppConfigurationValidationUtility.Validate(
            CreateValidAppSettings(includeCredentials: true),
            directory.Path);

        AssertFalse(report.HasIssues, "complete runtime configuration should not report issues");
    }

    internal static void SettingsJsonAcceptsCurrentSchemaVersion()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(
            Path.Combine(directory.Path, "settings.json"),
            """
            {
              "schema_version": 1,
              "RPOL Site": "https://rpol.net/game.php?gi=80170",
              "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
              "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
              "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons",
              "XP Tracking": "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
            }
            """);

        var settings = AppSettingsUtility.LoadSettings(directory.Path);

        AssertEqual("https://rpol.net/game.php?gi=80170", settings["RPOL Site"], "unexpected RPOL Site after schema-versioned load");
        AssertFalse(settings.ContainsKey("schema_version"), "schema_version should be treated as settings metadata");
    }

    internal static void SettingsJsonRejectsFutureSchemaVersion()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(
            Path.Combine(directory.Path, "settings.json"),
            """
            {
              "schema_version": 99,
              "RPOL Site": "https://rpol.net/game.php?gi=80170",
              "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
              "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
              "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons",
              "XP Tracking": "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
            }
            """);

        var exception = AssertThrows<InvalidOperationException>(() =>
            AppSettingsUtility.LoadSettings(directory.Path));
        AssertContains(exception.Message, "unsupported schema version 99");
    }

    internal static void AppSettingsLoadsHostedEncryptedXpTrackingUrl()
    {
        using var directory = TemporaryDirectory.Create();
        var hostedSettings = CreateSignedHostedSettingsArtifact(
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
            });

        File.WriteAllText(
            Path.Combine(directory.Path, "settings.json"),
            $$"""
            {
              "schema_version": 1,
              "Hosted Local Settings": "https://bryanmiller.us/scarlethorizons/settings.local.json",
              "RPOL Site": "https://rpol.net/game.php?gi=80170",
              "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
              "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
              "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons"
            }
            """);

        string? requestedHostedSettingsUrl = null;
        using var httpClientScope = AppSettingsUtility.UseHttpClientFactoryForTests(() =>
            NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((request, _) =>
            {
                requestedHostedSettingsUrl = request.RequestUri?.AbsoluteUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(hostedSettings.HostedSettingsJson)
                });
            })));
        using var trustedHostedSettingsKeysScope = HostedSettingsTrustUtility.UseTrustedSigningKeysForTests(
            [CreateActiveHostedSettingsSigningKey(hostedSettings.PublicKeyPem)]);

        Dictionary<string, string> settings = null!;
        WithHostedSettingsIsolation(() =>
            settings = new Dictionary<string, string>(AppSettingsUtility.LoadSettings(directory.Path), StringComparer.OrdinalIgnoreCase));

        AssertEqual(
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            settings["XP Tracking"],
            "hosted encrypted local settings should provide the XP Tracking URL");
        AssertEqual(
            "https://bryanmiller.us/scarlethorizons/settings.local.json",
            requestedHostedSettingsUrl ?? string.Empty,
            "unexpected hosted local settings URL");
        AssertFalse(
            File.Exists(Path.Combine(directory.Path, "settings.local.json")),
            "hosted local settings should not be persisted into the runtime directory");
    }

    internal static void AppSettingsMigrateHostedRpolCredentialsIntoCredentialManager()
    {
        using var directory = TemporaryDirectory.Create();
        var hostedSettings = CreateSignedHostedSettingsArtifact(
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
                ["RPOL user name"] = "example-user",
                ["RPOL password"] = "example-password"
            });

        File.WriteAllText(
            Path.Combine(directory.Path, "settings.json"),
            """
            {
              "schema_version": 1,
              "Hosted Local Settings": "https://bryanmiller.us/scarlethorizons/settings.local.json",
              "RPOL Site": "https://rpol.net/game.php?gi=80170",
              "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
              "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
              "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons"
            }
            """);

        string? requestedHostedSettingsUrl = null;
        using var httpClientScope = AppSettingsUtility.UseHttpClientFactoryForTests(() =>
            NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((request, _) =>
            {
                requestedHostedSettingsUrl = request.RequestUri?.AbsoluteUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(hostedSettings.HostedSettingsJson)
                });
            })));
        using var trustedHostedSettingsKeysScope = HostedSettingsTrustUtility.UseTrustedSigningKeysForTests(
            [CreateActiveHostedSettingsSigningKey(hostedSettings.PublicKeyPem)]);

        Dictionary<string, string> settings = null!;
        WithHostedSettingsIsolation(() =>
            settings = new Dictionary<string, string>(AppSettingsUtility.LoadSettings(directory.Path), StringComparer.OrdinalIgnoreCase));

        AssertEqual("example-user", settings["RPOL user name"], "unexpected migrated RPOL user name");
        AssertEqual("example-password", settings["RPOL password"], "unexpected migrated RPOL password");
        AssertEqual(
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            settings["XP Tracking"],
            "xp tracking should remain available after hosted local settings load");
        AssertEqual(
            "https://bryanmiller.us/scarlethorizons/settings.local.json",
            requestedHostedSettingsUrl ?? string.Empty,
            "unexpected hosted local settings URL");
        AssertEqual("example-user", RuntimeSecretStoreUtility.GetRpolUserName()!, "credential manager should store RPOL user name");
        AssertEqual("example-password", RuntimeSecretStoreUtility.GetRpolPassword()!, "credential manager should store RPOL password");
        AssertFalse(
            File.Exists(Path.Combine(directory.Path, "settings.local.json")),
            "hosted local settings should not be persisted after credential migration");
    }

    internal static void AppSettingsLoadsHostedEncryptedXpTrackingUrlFromFixtureServer()
    {
        using var directory = TemporaryDirectory.Create();
        var hostedSettings = CreateSignedHostedSettingsArtifact(
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
            });

        using var fixtureServer = new LoopbackHttpServer("/scarlethorizons/settings.local.json", hostedSettings.HostedSettingsJson);

        File.WriteAllText(
            Path.Combine(directory.Path, "settings.json"),
            $$"""
            {
              "schema_version": 1,
              "Hosted Local Settings": "{{fixtureServer.Url}}",
              "RPOL Site": "https://rpol.net/game.php?gi=80170",
              "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
              "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
              "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons"
            }
            """);

        using var allowlistScope = NetworkUrlAllowlistUtility.UseValidationOverrideForTests((uri, purpose) =>
        {
            if (purpose == NetworkUrlPurpose.PlayerAssistantHostedSettings
                && string.Equals(uri.AbsoluteUri, fixtureServer.Url, StringComparison.Ordinal))
            {
                return NetworkUrlAllowlistValidation.Allowed(uri);
            }

            return null;
        });
        using var trustedHostedSettingsKeysScope = HostedSettingsTrustUtility.UseTrustedSigningKeysForTests(
            [CreateActiveHostedSettingsSigningKey(hostedSettings.PublicKeyPem)]);

        Dictionary<string, string> settings = null!;
        WithHostedSettingsIsolation(() =>
            settings = new Dictionary<string, string>(AppSettingsUtility.LoadSettings(directory.Path), StringComparer.OrdinalIgnoreCase));

        AssertEqual(
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            settings["XP Tracking"],
            "fixture-hosted encrypted local settings should provide the XP Tracking URL");
        AssertEqual(1, fixtureServer.RequestCount, "fixture server should receive exactly one hosted settings request");
        AssertEqual("/scarlethorizons/settings.local.json", fixtureServer.LastRequestPath, "fixture server should receive the hosted settings path");
        AssertFalse(
            File.Exists(Path.Combine(directory.Path, "settings.local.json")),
            "fixture-hosted local settings should not be persisted into the runtime directory");
    }

    internal static void AppSettingsHostedSettingsFailureLogsTamperedEnvelope()
    {
        var hostedSettings = CreateSignedHostedSettingsArtifact(
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
            });
        var tamperedJson = CorruptHostedSettingsSignature(hostedSettings.HostedSettingsJson);

        using var fixtureServer = new LoopbackHttpServer("/scarlethorizons/settings.local.json", tamperedJson);
        AssertHostedSettingsFailure(
            fixtureServer.Url,
            "signature",
            [CreateActiveHostedSettingsSigningKey(hostedSettings.PublicKeyPem)],
            expectedRequestCount: 1,
            expectedRequestPath: "/scarlethorizons/settings.local.json");
    }

    internal static void AppSettingsHostedSettingsFailureLogsPlaintextPayload()
    {
        const string plaintextJson =
            """
            {
              "XP Tracking": "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
            }
            """;

        using var fixtureServer = new LoopbackHttpServer("/scarlethorizons/settings.local.json", plaintextJson);
        AssertHostedSettingsFailure(
            fixtureServer.Url,
            "signed envelope",
            expectedRequestCount: 1,
            expectedRequestPath: "/scarlethorizons/settings.local.json");
    }

    internal static void AppSettingsHostedSettingsFailureLogsOversizedPayload()
    {
        var oversizedPayload = new string('A', checked((int)NetworkResponseContentLimit.JsonCache.MaxBytes + 1024));

        using var fixtureServer = new LoopbackHttpServer("/scarlethorizons/settings.local.json", oversizedPayload);
        AssertHostedSettingsFailure(
            fixtureServer.Url,
            "JSON cache response exceeded",
            expectedRequestCount: 1,
            expectedRequestPath: "/scarlethorizons/settings.local.json");
    }

    internal static void AppSettingsHostedSettingsFailureLogsUnreachableFixtureServer()
    {
        string fixtureUrl;
        using (var fixtureServer = new LoopbackHttpServer("/scarlethorizons/settings.local.json", "{}"))
        {
            fixtureUrl = fixtureServer.Url;
        }

        AssertHostedSettingsFailure(
            fixtureUrl,
            "The network request failed:");
    }

    internal static void HostedSettingsTrustedVersionIsEncryptedAtRest()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "trusted-hosted-settings-state.json");

        HostedSettingsTrustUtility.ApplyTrustedHostedSettingsVersionPolicy(
            new Version(1, 0, 1),
            statePath);

        var encryptedJson = File.ReadAllText(statePath);
        AssertContains(encryptedJson, "\"format\": \"app-protected-v3\"");
        AssertContains(encryptedJson, "\"key_scope\":");
        AssertFalse(encryptedJson.Contains("1.0.1", StringComparison.Ordinal), "trusted hosted settings version should not be stored in plaintext");
    }

    internal static void HostedSettingsTrustedVersionRejectsTamperedPayload()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "trusted-hosted-settings-state.json");

        HostedSettingsTrustUtility.ApplyTrustedHostedSettingsVersionPolicy(
            new Version(1, 0, 1),
            statePath);

        using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(statePath)))
        {
            var payload = document.RootElement.GetProperty("payload").GetString() ?? string.Empty;
            var payloadBytes = Convert.FromBase64String(payload);
            payloadBytes[^1] ^= 0x7F;
            File.WriteAllText(
                statePath,
                $$"""
                {
                  "schema_version": 1,
                  "format": "app-protected-v3",
                  "payload": "{{Convert.ToBase64String(payloadBytes)}}"
                }
                """);
        }

        var exception = AssertThrows<InvalidOperationException>(() =>
            HostedSettingsTrustUtility.TryReadTrustedHostedSettingsVersion(statePath));
        AssertContains(exception.Message, "authenticate or decrypt");
    }

    internal static void HostedSettingsRejectsRollbackBelowTrustedVersionFloor()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "trusted-hosted-settings-state.json");
        var signedV2 = CreateSignedHostedSettingsArtifact(
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
            },
            version: "1.0.2");
        var signedV1 = CreateSignedHostedSettingsArtifact(
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
            },
            version: "1.0.1");

        _ = HostedSettingsTrustUtility.LoadAndVerifyHostedSettings(
            signedV2.HostedSettingsJson,
            "https://bryanmiller.us/scarlethorizons/settings.local.json",
            [CreateActiveHostedSettingsSigningKey(signedV2.PublicKeyPem)],
            statePath);
        var exception = AssertThrows<InvalidOperationException>(() =>
            HostedSettingsTrustUtility.LoadAndVerifyHostedSettings(
                signedV1.HostedSettingsJson,
                "https://bryanmiller.us/scarlethorizons/settings.local.json",
                [CreateActiveHostedSettingsSigningKey(signedV1.PublicKeyPem)],
                statePath));

        AssertContains(exception.Message, "downgrade");
        AssertContains(exception.Message, "1.0.2");
    }

    internal static void HostedSettingsRejectsUnexpectedSignedContentIdentity()
    {
        var signedHostedSettings = CreateSignedHostedSettingsArtifact(
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
            },
            contentId: "unexpected-hosted-settings",
            version: "1.0.0");

        var exception = AssertThrows<InvalidOperationException>(() =>
            HostedSettingsTrustUtility.LoadAndVerifyHostedSettings(
                signedHostedSettings.HostedSettingsJson,
                "https://bryanmiller.us/scarlethorizons/settings.local.json",
                [CreateActiveHostedSettingsSigningKey(signedHostedSettings.PublicKeyPem)],
                trustedHostedSettingsStatePath: null));

        AssertContains(exception.Message, "content identity");
        AssertContains(exception.Message, HostedSettingsTrustUtility.HostedSettingsContentId);
    }

    internal static void XpPasswordStoreLoadsSaltedHashSidecar()
    {
        using var directory = TemporaryDirectory.Create();
        var sidecarPath = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
        XpPasswordStoreUtility.SavePasswordHashes(
            sidecarPath,
            new Dictionary<string, string>
            {
                ["Kelpie"] = "gemstone",
                ["Jelb"] = "spell-component"
            });

        var hashes = XpPasswordStoreUtility.LoadPasswordHashes(directory.Path);

        AssertEqual(2, hashes.Count, "unexpected XP password hash count");
        AssertTrue(
            XpPasswordStoreUtility.ValidatePassword("Kelpie", "gemstone", directory.Path),
            "matching XP password should validate");
        AssertFalse(
            XpPasswordStoreUtility.ValidatePassword("Kelpie", "wrong", directory.Path),
            "wrong XP password should be rejected");
    }

    internal static void XpPasswordStoreUsesUniqueSaltsAndOmitsPlaintext()
    {
        using var directory = TemporaryDirectory.Create();
        var sidecarPath = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
        XpPasswordStoreUtility.SavePasswordHashes(
            sidecarPath,
            new Dictionary<string, string>
            {
                ["Kelpie"] = "shared-password",
                ["Jelb"] = "shared-password"
            });

        var raw = File.ReadAllText(sidecarPath);
        using var document = JsonDocument.Parse(raw);
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        var salts = entries.Select(entry => entry.GetProperty("salt").GetString()).ToArray();
        var hashes = entries.Select(entry => entry.GetProperty("hash").GetString()).ToArray();

        AssertEqual(
            XpPasswordStoreUtility.Format,
            document.RootElement.GetProperty("format").GetString() ?? string.Empty,
            "unexpected XP password hash format");
        AssertEqual(2, salts.Distinct(StringComparer.Ordinal).Count(), "each XP password must use a unique salt");
        AssertEqual(2, hashes.Distinct(StringComparer.Ordinal).Count(), "equal passwords with unique salts must produce different hashes");
        AssertFalse(raw.Contains("shared-password", StringComparison.Ordinal), "XP hash sidecar must not contain plaintext password material");
    }

    internal static void XpPasswordStoreAcceptsFirstAndFullCharacterNames()
    {
        using var directory = TemporaryDirectory.Create();
        var sidecarPath = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
        XpPasswordStoreUtility.SavePasswordHashes(
            sidecarPath,
            new Dictionary<string, string>
            {
                ["Kelpie"] = "gemstone",
                ["Jelb Garrick"] = "spell-component",
                ["Dungeon Master"] = "Lucian99!"
            });

        AssertTrue(
            XpPasswordStoreUtility.ValidatePassword("Kelpie Lawfuller", "gemstone", directory.Path),
            "full Kelpie name should validate against first-name XP credential");
        AssertTrue(
            XpPasswordStoreUtility.ValidatePassword("Jelb", "spell-component", directory.Path),
            "first Jelb name should validate against full-name XP credential");
        AssertFalse(
            XpPasswordStoreUtility.ValidatePassword("Dungeon", "Lucian99!", directory.Path),
            "Dungeon Master access should not allow a first-name shortcut");
    }

    internal static void XpPasswordStoreAcceptsHashSidecarWithUtf8Bom()
    {
        using var directory = TemporaryDirectory.Create();
        var sidecarPath = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
        XpPasswordStoreUtility.SavePasswordHashes(
            sidecarPath,
            new Dictionary<string, string>
            {
                ["Kelpie Lawfuller"] = "gemstone"
            });

        var hashBytes = File.ReadAllBytes(sidecarPath);
        File.WriteAllBytes(sidecarPath, [0xEF, 0xBB, 0xBF, .. hashBytes]);

        AssertTrue(
            XpPasswordStoreUtility.ValidatePassword("Kelpie Lawfuller", "gemstone", directory.Path),
            "matching XP password should validate when hash sidecar has a UTF-8 BOM");
    }

    internal static void XpPasswordStoreRejectsLegacyEncryptedSidecar()
    {
        using var directory = TemporaryDirectory.Create();
        var sidecarPath = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
        LocalSettingsUtility.SavePortableEncryptedSettings(
            sidecarPath,
            new Dictionary<string, string> { ["Kelpie"] = "gemstone" });

        var exception = AssertThrows<InvalidOperationException>(() =>
            XpPasswordStoreUtility.LoadPasswordHashes(directory.Path));

        AssertContains(exception.Message, XpPasswordStoreUtility.Format);
    }

    internal static void XpPasswordStoreMigratesEncryptedSidecar()
    {
        using var directory = TemporaryDirectory.Create();
        var sidecarPath = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
        LocalSettingsUtility.SavePortableEncryptedSettings(
            sidecarPath,
            new Dictionary<string, string>
            {
                ["Kelpie"] = "gemstone",
                ["Jelb"] = "spell-component"
            });

        var entryCount = XpPasswordStoreUtility.ConvertEncryptedSidecarToPasswordHashes(sidecarPath);

        AssertEqual(2, entryCount, "unexpected migrated XP password count");
        AssertTrue(XpPasswordStoreUtility.ValidatePassword("Kelpie", "gemstone", directory.Path), "migrated XP password should validate");
        var raw = File.ReadAllText(sidecarPath);
        AssertContains(raw, XpPasswordStoreUtility.Format);
        AssertFalse(raw.Contains("gemstone", StringComparison.Ordinal), "migration must remove plaintext password material");
        AssertFalse(raw.Contains("app-protected", StringComparison.Ordinal), "migration must remove the reversible encrypted envelope");
    }

    internal static void XpPasswordStoreReportsMissingSidecarByName()
    {
        using var directory = TemporaryDirectory.Create();

        var exception = AssertThrows<FileNotFoundException>(() =>
            XpPasswordStoreUtility.LoadPasswordHashes(directory.Path));

        AssertContains(exception.Message, XpPasswordStoreUtility.FileName);
        AssertContains(exception.Message, directory.Path);
        AssertFalse(
            exception.Message.Contains("settings sidecar", StringComparison.OrdinalIgnoreCase),
            "missing XP password diagnostics should not be mistaken for settings.local.json");
    }

    internal static void AppConfigurationValidationReportsMissingUrl()
    {
        using var directory = TemporaryDirectory.Create();
        WriteRequiredRuntimeSidecars(directory.Path);
        var settings = CreateValidAppSettings(includeCredentials: true);
        settings.Remove("The Cast");

        var report = AppConfigurationValidationUtility.Validate(settings, directory.Path);

        AssertTrue(report.HasIssues, "missing URL should report a configuration issue");
        AssertTrue(
            report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error
                && issue.Message.Contains("The Cast", StringComparison.Ordinal)),
            "missing The Cast URL should be an error");
        AssertEqual(
            "Settings problem: The Cast is missing or empty.",
            report.FirstUserMessage!,
            "unexpected first user-facing validation message");
        AssertContains(report.Issues[0].RepairAction, "settings.json");
        AssertContains(report.ToRemediationText(), "Repair:");
    }

    internal static void AppConfigurationValidationRejectsDisallowedNetworkHost()
    {
        using var directory = TemporaryDirectory.Create();
        WriteRequiredRuntimeSidecars(directory.Path);
        var settings = CreateValidAppSettings(includeCredentials: true);
        settings["Game Intro"] = "https://unexpected.example.test/gameinfo";

        var report = AppConfigurationValidationUtility.Validate(settings, directory.Path);

        AssertTrue(report.HasIssues, "disallowed host should report a configuration issue");
        AssertTrue(
            report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error
                && issue.Message.Contains("Game Intro", StringComparison.Ordinal)
                && issue.Message.Contains("allowed network host", StringComparison.Ordinal)),
            "disallowed Game Intro host should be an allowlist error");
    }

    internal static void AppConfigurationValidationWritesRepairGuidance()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateValidAppSettings(includeCredentials: false);
        settings["Game Intro"] = "not a url";

        var report = AppConfigurationValidationUtility.Validate(settings, directory.Path);
        AppConfigurationValidationUtility.WriteRemediationFile(report, directory.Path);

        var remediationPath = Path.Combine(directory.Path, AppConfigurationValidationUtility.RemediationFileName);
        AssertTrue(File.Exists(remediationPath), "startup remediation file should be written");
        var remediation = File.ReadAllText(remediationPath);
        AssertContains(remediation, "Player Assistant startup configuration guidance");
        AssertContains(remediation, "Game Intro is not on the allowed network host list");
        AssertContains(remediation, "URL must be absolute.");
        AssertContains(remediation, "Edit settings.json");
        AssertFalse(
            remediation.Contains("RPOL credentials", StringComparison.OrdinalIgnoreCase),
            "RPOL credential warnings should not appear unless hosted settings failed");
    }

    internal static void AppConfigurationValidationSuppressesMissingRpolCredentialsBeforeHostedSettingsFailure()
    {
        using var directory = TemporaryDirectory.Create();
        WriteRequiredRuntimeSidecars(directory.Path);

        var report = AppConfigurationValidationUtility.Validate(
            CreateValidAppSettings(includeCredentials: false),
            directory.Path);

        AssertFalse(
            report.Issues.Any(issue => issue.Message.Contains("RPOL credential", StringComparison.OrdinalIgnoreCase)),
            "missing RPOL credentials should not be reported before hosted settings failure");
        AssertFalse(
            report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error),
            "missing optional RPOL credentials should not be fatal");
    }

    internal static void AppConfigurationValidationWarnsAboutMissingRpolCredentialsAfterHostedSettingsFailure()
    {
        using var directory = TemporaryDirectory.Create();
        WriteRequiredRuntimeSidecars(directory.Path);

        var report = AppConfigurationValidationUtility.Validate(
            CreateValidAppSettings(includeCredentials: false),
            directory.Path,
            warnAboutMissingRpolCredentials: true);

        AssertTrue(
            report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Warning
                && issue.Message.Contains("Hosted RPOL credential data could not be loaded", StringComparison.Ordinal)),
            "missing RPOL credentials should be a warning after hosted settings failure");
        AssertFalse(
            report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error),
            "missing optional RPOL credentials should not be fatal");
    }

    internal static void AppConfigurationValidationWarnsAboutMissingSidecars()
    {
        using var directory = TemporaryDirectory.Create();

        var report = AppConfigurationValidationUtility.Validate(
            CreateValidAppSettings(includeCredentials: true),
            directory.Path);

        AssertTrue(
            report.Issues.Count(issue => issue.Message.Contains("missing", StringComparison.OrdinalIgnoreCase)) >= 3,
            "missing runtime sidecars should be reported");
        AssertTrue(
            report.Issues.All(issue => issue.Severity == AppConfigurationIssueSeverity.Warning),
            "missing sidecars should warn without blocking startup");
    }

    internal static void AppSettingsLoadsRpolCredentialsFromLocalSettingsSidecar()
    {
        using var directory = TemporaryDirectory.Create();
        WriteSettingsJson(directory.Path, CreateValidAppSettings(includeCredentials: false));
        WriteRequiredRuntimeSidecars(directory.Path);
        LocalSettingsUtility.SaveEncryptedSettings(
            Path.Combine(directory.Path, "settings.local.json"),
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
                ["RPOL user name"] = "example-user",
                ["RPOL password"] = "example-password"
            });

        using var credentialStoreScope = RuntimeSecretStoreUtility.UseBackendForTests(new InMemoryWindowsCredentialStoreBackend());
        var settings = new Dictionary<string, string>(AppSettingsUtility.LoadSettings(directory.Path), StringComparer.OrdinalIgnoreCase);

        AssertEqual("example-user", settings["RPOL user name"], "local settings sidecar should provide RPOL user name");
        AssertEqual("example-password", settings["RPOL password"], "local settings sidecar should provide RPOL password");
        AssertEqual("example-user", RuntimeSecretStoreUtility.GetRpolUserName()!, "local settings sidecar should prime RPOL user name");
        AssertEqual("example-password", RuntimeSecretStoreUtility.GetRpolPassword()!, "local settings sidecar should prime RPOL password");
        AssertEqual(
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            settings["XP Tracking"],
            "local settings sidecar should still provide XP Tracking");
        var report = AppConfigurationValidationUtility.Validate(settings, directory.Path, warnAboutMissingRpolCredentials: true);
        AssertFalse(
            report.Issues.Any(issue => issue.Message.Contains("Hosted RPOL credential data could not be loaded", StringComparison.Ordinal)),
            "loaded sidecar RPOL credentials should suppress the hosted-settings warning");
    }

    internal static void AppSettingsUsesLocalRpolCredentialsWhenCredentialStoreIsUnavailable()
    {
        using var directory = TemporaryDirectory.Create();
        WriteSettingsJson(directory.Path, CreateValidAppSettings(includeCredentials: false));
        WriteRequiredRuntimeSidecars(directory.Path);
        LocalSettingsUtility.SaveEncryptedSettings(
            Path.Combine(directory.Path, "settings.local.json"),
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
                ["RPOL user name"] = "sidecar-user",
                ["RPOL password"] = "sidecar-password"
            });

        using var credentialStoreScope = RuntimeSecretStoreUtility.UseBackendForTests(new ThrowingWindowsCredentialStoreBackend());
        var settings = new Dictionary<string, string>(AppSettingsUtility.LoadSettings(directory.Path), StringComparer.OrdinalIgnoreCase);

        AssertEqual("sidecar-user", settings["RPOL user name"], "local settings sidecar should provide RPOL user name when the credential store is unavailable");
        AssertEqual("sidecar-password", settings["RPOL password"], "local settings sidecar should provide RPOL password when the credential store is unavailable");
    }

    internal static void AppConfigurationValidationAcceptsValidReleaseManifest()
    {
        using var directory = TemporaryDirectory.Create();
        WriteManifestedRuntime(directory.Path);

        var report = AppConfigurationValidationUtility.Validate(
            CreateValidAppSettings(includeCredentials: true),
            directory.Path);

        AssertFalse(
            report.Issues.Any(issue => issue.Message.Contains("release-manifest.json", StringComparison.Ordinal)),
            "valid release manifest should not report an integrity issue");
    }

    internal static void AppConfigurationValidationRejectsMissingManifestFile()
    {
        using var directory = TemporaryDirectory.Create();
        WriteManifestedRuntime(directory.Path);
        File.Delete(Path.Combine(directory.Path, ".playwright", "package", "package.json"));

        var report = AppConfigurationValidationUtility.Validate(
            CreateValidAppSettings(includeCredentials: true),
            directory.Path);

        AssertTrue(
            report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error
                && issue.Message.Contains("release-manifest.json missing manifested file", StringComparison.Ordinal)
                && issue.Message.Contains("package.json", StringComparison.Ordinal)),
            "missing manifested file should report a release integrity error");
    }

    internal static void AppConfigurationValidationRejectsManifestHashMismatch()
    {
        using var directory = TemporaryDirectory.Create();
        WriteManifestedRuntime(directory.Path);
        File.AppendAllText(Path.Combine(directory.Path, "settings.json"), "tampered");

        var report = AppConfigurationValidationUtility.Validate(
            CreateValidAppSettings(includeCredentials: true),
            directory.Path);

        AssertTrue(
            report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error
                && issue.Message.Contains("release-manifest.json SHA256 mismatch", StringComparison.Ordinal)
                && issue.Message.Contains("settings.json", StringComparison.Ordinal)),
            "modified manifested file should report a release integrity hash error");
    }

    internal static void HealthArgumentSurfacesReleaseManifestIssue()
    {
        var runtimeDirectory = AppContext.BaseDirectory;
        var manifestPath = Path.Combine(runtimeDirectory, ReleaseIntegrityManifestUtility.FileName);
        var backupPath = Path.Combine(runtimeDirectory, $"{ReleaseIntegrityManifestUtility.FileName}.test-backup-{Guid.NewGuid():N}");
        var hadManifest = File.Exists(manifestPath);

        try
        {
            if (hadManifest)
            {
                File.Move(manifestPath, backupPath);
            }

            File.WriteAllText(
                manifestPath,
                """
                {
                  "schema_version": 1,
                  "hash_algorithm": "SHA256",
                  "files": [
                    {
                      "relative_path": "missing-health-sidecar.txt",
                      "length": 1,
                      "sha256": "00"
                    }
                  ]
                }
                """);

            var programType = typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.Program")
                ?? throw new InvalidOperationException("Unable to find PlayerAssistant.Program type.");
            var health = (string)InvokeStaticMethod(programType, "GetHealthText")!;

            AssertContains(health, "status: error");
            AssertContains(health, "release-manifest.json missing manifested file");
            AssertContains(health, "missing-health-sidecar.txt");
        }
        finally
        {
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }

            if (hadManifest && File.Exists(backupPath))
            {
                File.Move(backupPath, manifestPath);
            }
        }
    }

    internal static void StartupDependencyMatrixReportsBadConfigAndSidecars()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "keyword-index.json"), string.Empty);
        File.WriteAllText(Path.Combine(directory.Path, KeywordTermsFileUtility.FileName), "scarlet");
        var settings = CreateValidAppSettings(includeCredentials: true);
        settings["RPOL Site"] = "file:///not-http";
        settings["Game Intro"] = "not a url";
        var missingRuntimeDirectory = Path.Combine(directory.Path, "missing-runtime");

        var badUrlReport = AppConfigurationValidationUtility.Validate(settings, directory.Path);
        var missingRuntimeReport = AppConfigurationValidationUtility.Validate(
            CreateValidAppSettings(includeCredentials: true),
            missingRuntimeDirectory);

        AssertTrue(
            badUrlReport.Issues.Count(issue => issue.Severity == AppConfigurationIssueSeverity.Error
                && issue.Message.Contains("allowed network host", StringComparison.Ordinal)) >= 2,
            "malformed startup URLs should be reported as configuration errors");
        AssertTrue(
            badUrlReport.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Warning
                && issue.Message.Contains("keyword-index.json is empty", StringComparison.Ordinal)),
            "empty keyword index sidecar should be reported as a warning");
        AssertTrue(
            badUrlReport.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Warning
                && issue.Message.Contains("sitemap.xml is missing", StringComparison.Ordinal)),
            "missing sitemap sidecar should be reported as a warning");
        AssertTrue(
            missingRuntimeReport.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error
                && issue.Message.Contains("Runtime directory does not exist", StringComparison.Ordinal)),
            "missing runtime directory should be reported as a configuration error");
    }

    internal static void StartupDependencyMatrixIgnoresCorruptOptionalLocalSettings()
    {
        using var directory = TemporaryDirectory.Create();
        WriteSettingsJson(directory.Path, CreateValidAppSettings(includeCredentials: false));
        var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
        File.WriteAllText(localSettingsPath, "{ not valid json");

        WithPreservedStartupLog(() =>
        {
            var settings = AppSettingsUtility.LoadSettings(directory.Path);

            AssertEqual(
                "https://rpol.net/game.php?gi=80170",
                settings["RPOL Site"],
                "base settings should still load when optional local settings are corrupt");
            AssertFalse(settings.ContainsKey("RPOL user name"), "corrupt optional local settings should not inject credentials");
            AssertFalse(File.Exists(localSettingsPath), "corrupt local settings should be moved out of the active path");

            var badLocalSettingsFiles = Directory.GetFiles(directory.Path, "settings.local.bad-*.json");
            AssertEqual(1, badLocalSettingsFiles.Length, "expected one quarantined local settings file");

            var startupLog = File.ReadAllText(GetStartupLogPath());
            AssertContains(startupLog, "local settings load");
            AssertContains(startupLog, badLocalSettingsFiles[0]);
        });
    }

    internal static void ApplicationVersionMetadataMatchesHardeningRelease()
    {
        var assembly = typeof(Form1).Assembly;
        var name = assembly.GetName();
        var informationalVersion = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Single()
            .InformationalVersion;
        var fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion;

        AssertEqual(new Version(0, 9, 5, 0), name.Version!, "unexpected assembly version");
        AssertEqual("0.9.5.0", fileVersion!, "unexpected file version");
        AssertEqual("0.9.5", informationalVersion, "unexpected informational version");
    }

    internal static void ApplicationVersionArgumentReturnsVersionText()
    {
        var programType = typeof(Form1).Assembly.GetType("PlayerAssistant.Program")
            ?? throw new InvalidOperationException("Unable to find PlayerAssistant.Program.");

        AssertTrue(
            (bool)(InvokeStaticMethod(programType, "IsVersionArgument", "--version") ?? false),
            "--version should be recognized as a version argument");
        AssertTrue(
            (bool)(InvokeStaticMethod(programType, "IsVersionArgument", "/version") ?? false),
            "/version should be recognized as a version argument");
        AssertFalse(
            (bool)(InvokeStaticMethod(programType, "IsVersionArgument", "--suppress-hero-images") ?? true),
            "non-version arguments should not be recognized as version arguments");

        var versionText = (string?)InvokeStaticMethod(programType, "GetVersionText")
            ?? throw new InvalidOperationException("GetVersionText returned null.");
        AssertContains(versionText, "player-assistant");
        AssertContains(versionText, "0.9.5");
    }

    internal static void StartupManifestStatusDistinguishesSkippedAndFailed()
    {
        AssertEqual("downloaded", Form1.GetManifestStatus(downloaded: true, errorMessage: null), "unexpected downloaded status");
        AssertEqual("skipped", Form1.GetManifestStatus(downloaded: false, errorMessage: null), "unexpected skipped status");
        AssertEqual("failed", Form1.GetManifestStatus(downloaded: false, errorMessage: "boom"), "unexpected failed status");
    }

    internal static void StartupErrorLogEntryIncludesPhaseAndException()
    {
        var entry = Form1.FormatStartupErrorLogEntry(
            "ooc thread downloads RPOL password=hunter2",
            new InvalidOperationException("Missing RPoL credentials at https://user:pass@example.test/path?password=secret-password&token=secret-token Authorization: Bearer abc123"));

        AssertContains(entry, "ooc thread downloads");
        AssertContains(entry, "InvalidOperationException");
        AssertContains(entry, "Missing RPoL credentials");
        AssertFalse(entry.Contains("hunter2", StringComparison.Ordinal), "startup log phase should redact RPOL passwords");
        AssertFalse(entry.Contains("secret-password", StringComparison.Ordinal), "startup log should redact password query values");
        AssertFalse(entry.Contains("secret-token", StringComparison.Ordinal), "startup log should redact token query values");
        AssertFalse(entry.Contains("Bearer abc123", StringComparison.Ordinal), "startup log should redact bearer tokens");
        AssertFalse(entry.Contains("user:pass@", StringComparison.Ordinal), "startup log should redact credentialed URLs");
    }

    internal static void LastCrashDiagnosticWritesRedactedExceptionDetails()
    {
        WithPreservedLastCrash(() =>
        {
            var exception = new InvalidOperationException(
                "failed url=https://user:pass@example.test/path?password=secret-password&token=secret-token Authorization: Bearer abc123",
                new ApplicationException("Cookie: sessionid=abc123"));

            LastCrashDiagnosticUtility.Write("synthetic crash RPOL password=hunter2", exception);

            var crashJson = File.ReadAllText(GetLastCrashPath());
            using var document = System.Text.Json.JsonDocument.Parse(crashJson);
            var root = document.RootElement;
            var exceptionElement = root.GetProperty("exception");
            var environment = root.GetProperty("environment");

            AssertJsonNumber(root, "schema_version", LastCrashDiagnosticUtility.CurrentSchemaVersion, "last crash should include schema version");
            AssertJsonString(root, "phase", "synthetic crash RPOL password=[REDACTED]", "last crash phase should be redacted");
            AssertJsonString(exceptionElement, "type", "InvalidOperationException", "last crash should record exception type");
            AssertContains(exceptionElement.GetProperty("message").GetString() ?? string.Empty, "[REDACTED]");
            AssertFalse(crashJson.Contains("secret-password", StringComparison.Ordinal), "last crash diagnostic should redact password query values");
            AssertFalse(crashJson.Contains("secret-token", StringComparison.Ordinal), "last crash diagnostic should redact token query values");
            AssertFalse(crashJson.Contains("Bearer abc123", StringComparison.Ordinal), "last crash diagnostic should redact bearer tokens");
            AssertFalse(crashJson.Contains("sessionid=abc123", StringComparison.Ordinal), "last crash diagnostic should redact cookie headers");
            AssertFalse(crashJson.Contains("user:pass@", StringComparison.Ordinal), "last crash diagnostic should redact credentialed URLs");
            AssertJsonString(environment, "machine_name", "[REDACTED]", "last crash should redact machine name");
            AssertJsonString(environment, "user_name", "[REDACTED]", "last crash should redact user name");
        });
    }

    internal static void StartupHealthRecordsRequiredPhaseSuccess()
    {
        WithPreservedStartupHealth(() =>
        {
            StartupHealthUtility.Reset();
            StartupLoggingUtility.RunRequiredPhase("synthetic required success", () => { });

            using var document = LoadStartupHealthDocument();
            var phase = FindStartupHealthPhase(document, "synthetic required success");

            AssertJsonString(phase, "status", "succeeded", "required success should be recorded as succeeded");
            AssertJsonNumberAtLeast(phase, "elapsed_milliseconds", 0, "required success elapsed time should be recorded");
            AssertJsonNumberAtLeast(phase, "refreshed_count", 0, "required success refreshed count should be present");
            AssertJsonNumber(phase, "failed_count", 0, "required success should not increment failure count");
            AssertEqual(System.Text.Json.JsonValueKind.Null, phase.GetProperty("last_exception").ValueKind, "successful phase should not include an exception");
        });
    }

    internal static void StartupHealthWritesSchemaVersion()
    {
        WithPreservedStartupHealth(() =>
        {
            StartupHealthUtility.Reset();

            using var document = LoadStartupHealthDocument();
            AssertJsonNumber(
                document.RootElement,
                "schema_version",
                StartupHealthUtility.CurrentSchemaVersion,
                "startup health should include the current schema version");
            AssertTrue(document.RootElement.TryGetProperty("phases", out var phases), "startup health should include phases");
            AssertEqual(System.Text.Json.JsonValueKind.Array, phases.ValueKind, "startup health phases should remain an array");
        });
    }

    internal static void StartupHealthRecordsRequiredPhaseFailure()
    {
        WithPreservedStartupHealth(() =>
        {
            StartupHealthUtility.Reset();

            var exception = AssertThrows<InvalidOperationException>(() =>
                StartupLoggingUtility.RunRequiredPhase(
                    "synthetic required failure RPOL user name=secret-user",
                    () => throw new InvalidOperationException("required boom Cookie: sessionid=abc123")));

            AssertEqual("required boom Cookie: sessionid=abc123", exception.Message, "required phase should rethrow the original exception");

            using var document = LoadStartupHealthDocument();
            var phase = FindStartupHealthPhase(document, "synthetic required failure RPOL user name=[REDACTED]");
            var lastException = phase.GetProperty("last_exception");

            AssertJsonString(phase, "status", "failed", "required failure should be recorded as failed");
            AssertJsonNumber(phase, "failed_count", 1, "required failure should increment failure count");
            AssertJsonString(lastException, "type", "InvalidOperationException", "required failure should record exception type");
            AssertJsonString(lastException, "message", "required boom Cookie: [REDACTED]", "required failure should redact exception message");
        });
    }

    internal static void StartupHealthRecordsOptionalPhaseFailureWithoutThrowing()
    {
        WithPreservedStartupLog(() =>
        {
            WithPreservedStartupHealth(() =>
            {
                StartupHealthUtility.Reset();

                StartupLoggingUtility.RunOptionalPhaseAsync(
                    "synthetic optional failure",
                    () => throw new InvalidOperationException("optional boom Authorization: Bearer abc123")).GetAwaiter().GetResult();

                using var document = LoadStartupHealthDocument();
                var phase = FindStartupHealthPhase(document, "synthetic optional failure");
                var lastException = phase.GetProperty("last_exception");

                AssertJsonString(phase, "status", "failed", "optional failure should be recorded as failed");
                AssertJsonNumber(phase, "failed_count", 1, "optional failure should increment failure count");
                AssertJsonString(lastException, "message", "optional boom Authorization: Bearer [REDACTED]", "optional failure should redact exception message");
                AssertContains(File.ReadAllText(GetStartupLogPath()), "optional boom");
                AssertFalse(File.ReadAllText(GetStartupLogPath()).Contains("Bearer abc123", StringComparison.Ordinal), "startup log should redact optional phase bearer tokens");
            });
        });
    }
}
