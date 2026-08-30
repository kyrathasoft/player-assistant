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

namespace PlayerAssistant.Tests;

internal static partial class TestCases
{
    internal static void KeepAlivePolicyIsTruthfulAndObservable()
    {
        var repoRoot = GetRepositoryRoot();
        var verifier = Path.Combine(repoRoot, "verify-keep-alive.ps1");
        using var process = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{verifier}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
        AssertTrue(process is not null, "keep-alive verifier process should start");
        process!.WaitForExit();
        AssertEqual(0, process.ExitCode, (process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd()).Trim());
    }


    internal static void AppConfigurationValidationAcceptsCompleteRuntime()
    {
        using var directory = TemporaryDirectory.Create();
        WriteRequiredRuntimeSidecars(directory.Path);

        var report = AppConfigurationValidationUtility.Validate(
            CreateValidAppSettings(includeCredentials: true),
            directory.Path);

        AssertFalse(report.HasIssues, "complete runtime configuration should not report issues");
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

        AssertFalse(settings.ContainsKey("RPOL user name"), "hosted settings must not return portable RPOL user name");
        AssertFalse(settings.ContainsKey("RPOL password"), "hosted settings must not return portable RPOL password");
        AssertEqual(
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            settings["XP Tracking"],
            "xp tracking should remain available after hosted local settings load");
        AssertEqual(
            "https://bryanmiller.us/scarlethorizons/settings.local.json",
            requestedHostedSettingsUrl ?? string.Empty,
            "unexpected hosted local settings URL");
        AssertFalse(
            RuntimeSecretStoreUtility.TryGetRpolCredentials(out _, out _),
            "hosted settings must not provision RPOL credentials from a portable payload");
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
        AssertContains(encryptedJson, "\"format\": \"dpapi-current-user-v2\"");
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
                  "format": "dpapi-current-user-v2",
                  "payload": "{{Convert.ToBase64String(payloadBytes)}}"
                }
                """);
        }

        var exception = AssertThrows<InvalidOperationException>(() =>
            HostedSettingsTrustUtility.TryReadTrustedHostedSettingsVersion(statePath));
        AssertContains(exception.Message, "Unable to decrypt");
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
            XpPasswordStoreUtility.ValidatePassword("Kelpie", "gemstone", directory.Path) is not null,
            "matching XP password should validate");
        AssertFalse(
            XpPasswordStoreUtility.ValidatePassword("Kelpie", "wrong", directory.Path) is not null,
            "wrong XP password should be rejected");
        RunCanonicalIdentityRegressionCases();
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

    internal static void XpPasswordStoreRejectsNonCanonicalCharacterNames()
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

        AssertFalse(
            XpPasswordStoreUtility.ValidatePassword("Kelpie Lawfuller", "gemstone", directory.Path) is not null,
            "a non-canonical full name must not resolve a first-name credential");
        AssertFalse(
            XpPasswordStoreUtility.ValidatePassword("Jelb", "spell-component", directory.Path) is not null,
            "a first-name shortcut must not resolve a full-name credential");
        AssertFalse(
            XpPasswordStoreUtility.ValidatePassword("Dungeon", "Lucian99!", directory.Path) is not null,
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
            XpPasswordStoreUtility.ValidatePassword("Kelpie Lawfuller", "gemstone", directory.Path) is not null,
            "matching XP password should validate when hash sidecar has a UTF-8 BOM");
    }

    internal static void XpPasswordStoreRejectsLegacyEncryptedSidecar()
    {
        using var directory = TemporaryDirectory.Create();
        var sidecarPath = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
        LocalSettingsUtility.SaveEncryptedSettings(
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
        LocalSettingsUtility.SaveEncryptedSettings(
            sidecarPath,
            new Dictionary<string, string>
            {
                ["Kelpie"] = "gemstone",
                ["Jelb"] = "spell-component"
            });

        var entryCount = XpPasswordStoreUtility.ConvertEncryptedSidecarToPasswordHashes(sidecarPath);

        AssertEqual(2, entryCount, "unexpected migrated XP password count");
        AssertTrue(XpPasswordStoreUtility.ValidatePassword("Kelpie", "gemstone", directory.Path) is not null, "migrated XP password should validate");
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

    internal static void RuntimePathUtilityUsesSystemTempForExternalBrowserProfile()
    {
        var expected = Path.GetFullPath(Path.GetTempPath());
        var resolved = RuntimePathUtility.GetExternalBrowserTemporaryDirectory();

        AssertTrue(
            resolved.StartsWith(expected.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "external browser profiles must be created under the system user temporary directory");
    }

    internal static void RuntimePathUtilityUsesUserDataRootForPublishedRuntime()
    {
        var publishedDirectory = Path.Combine(Path.GetTempPath(), "player-assistant-test", "Release", "publish");
        var userDataDirectory = Path.Combine(Path.GetTempPath(), "player-assistant-test", "LocalAppData");

        var resolved = RuntimePathUtility.ResolveWritableRuntimeDirectory(
            publishedDirectory,
            userDataDirectory);

        AssertEqual(
            Path.GetFullPath(userDataDirectory),
            resolved,
            "published runtime writes must use the user data root instead of the application directory");
    }

    internal static void RuntimeHousekeepingRemovesStaleTempAndAtomicFiles()
    {
        using var directory = TemporaryDirectory.Create();
        var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        var tempDirectory = Path.Combine(directory.Path, "temp");
        Directory.CreateDirectory(tempDirectory);
        var staleTempPath = Path.Combine(tempDirectory, "old-download.tmp");
        var atomicPath = AtomicFileUtility.CreateTempPath(Path.Combine(directory.Path, "keyword-index.json"));
        File.WriteAllText(staleTempPath, "temp");
        File.WriteAllText(atomicPath, "atomic");
        SetLastWriteTimeUtc(staleTempPath, now - TimeSpan.FromHours(2));
        SetLastWriteTimeUtc(atomicPath, now - TimeSpan.FromHours(2));

        var report = RuntimeHousekeepingUtility.Clean(
            directory.Path,
            now,
            new RuntimeHousekeepingOptions
            {
                StaleTempFileAge = TimeSpan.FromHours(1),
                OrphanedAtomicFileAge = TimeSpan.FromHours(1)
            });

        AssertFalse(File.Exists(staleTempPath), "stale temp file should be removed");
        AssertFalse(File.Exists(atomicPath), "stale atomic temp file should be removed");
        AssertEqual(2, report.RemovedFileCount, "unexpected removed file count");
        AssertTrue(report.ReclaimedBytes > 0, "expected reclaimed bytes to be reported");
    }

    internal static void RuntimeHousekeepingPreservesFreshAndUnrelatedTmpFiles()
    {
        using var directory = TemporaryDirectory.Create();
        var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        var tempDirectory = Path.Combine(directory.Path, "temp");
        Directory.CreateDirectory(tempDirectory);
        var freshTempPath = Path.Combine(tempDirectory, "fresh-download.tmp");
        var unrelatedTmpPath = Path.Combine(directory.Path, "cache.txt.tmp");
        File.WriteAllText(freshTempPath, "temp");
        File.WriteAllText(unrelatedTmpPath, "not an atomic temp file");
        SetLastWriteTimeUtc(freshTempPath, now - TimeSpan.FromMinutes(5));
        SetLastWriteTimeUtc(unrelatedTmpPath, now - TimeSpan.FromDays(7));

        var report = RuntimeHousekeepingUtility.Clean(
            directory.Path,
            now,
            new RuntimeHousekeepingOptions
            {
                StaleTempFileAge = TimeSpan.FromHours(1),
                OrphanedAtomicFileAge = TimeSpan.FromHours(1)
            });

        AssertTrue(File.Exists(freshTempPath), "fresh temp file should be preserved");
        AssertTrue(File.Exists(unrelatedTmpPath), "unrelated tmp file should be preserved");
        AssertEqual(0, report.RemovedFileCount, "fresh/unrelated files should not be removed");
    }

    internal static void RuntimeHousekeepingRemovesOldQuarantinedJsonOnly()
    {
        using var directory = TemporaryDirectory.Create();
        var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        var oldBadPath = Path.Combine(directory.Path, "keyword-index.bad-20260601-010203-004.json");
        var freshBadPath = Path.Combine(directory.Path, "settings.bad-20260702-010203-004.json");
        var normalJsonPath = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(oldBadPath, "{}");
        File.WriteAllText(freshBadPath, "{}");
        File.WriteAllText(normalJsonPath, "{}");
        SetLastWriteTimeUtc(oldBadPath, now - TimeSpan.FromDays(15));
        SetLastWriteTimeUtc(freshBadPath, now - TimeSpan.FromDays(1));
        SetLastWriteTimeUtc(normalJsonPath, now - TimeSpan.FromDays(30));

        var report = RuntimeHousekeepingUtility.Clean(
            directory.Path,
            now,
            new RuntimeHousekeepingOptions
            {
                QuarantinedJsonRetention = TimeSpan.FromDays(14)
            });

        AssertFalse(File.Exists(oldBadPath), "old quarantined json should be removed");
        AssertTrue(File.Exists(freshBadPath), "fresh quarantined json should be preserved");
        AssertTrue(File.Exists(normalJsonPath), "normal json should be preserved");
        AssertEqual(1, report.RemovedFileCount, "unexpected removed quarantine count");
    }

    internal static void RuntimeHousekeepingRemovesOldBackupFilesOnly()
    {
        using var directory = TemporaryDirectory.Create();
        var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        var oldBackupPath = Path.Combine(directory.Path, "keyword-index.bak-20260601-010203-004.json");
        var freshBackupPath = Path.Combine(directory.Path, "settings.bak-20260702-010203-004.json");
        var normalJsonPath = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(oldBackupPath, "{}");
        File.WriteAllText(freshBackupPath, "{}");
        File.WriteAllText(normalJsonPath, "{}");
        SetLastWriteTimeUtc(oldBackupPath, now - TimeSpan.FromDays(31));
        SetLastWriteTimeUtc(freshBackupPath, now - TimeSpan.FromDays(1));
        SetLastWriteTimeUtc(normalJsonPath, now - TimeSpan.FromDays(60));

        var report = RuntimeHousekeepingUtility.Clean(
            directory.Path,
            now,
            new RuntimeHousekeepingOptions
            {
                RuntimeBackupRetention = TimeSpan.FromDays(30)
            });

        AssertFalse(File.Exists(oldBackupPath), "old backup should be removed");
        AssertTrue(File.Exists(freshBackupPath), "fresh backup should be preserved");
        AssertTrue(File.Exists(normalJsonPath), "normal json should be preserved");
        AssertEqual(1, report.RemovedFileCount, "unexpected removed backup count");
    }

    internal static void RuntimeHousekeepingRotatesOversizedStartupLog()
    {
        using var directory = TemporaryDirectory.Create();
        var logPath = Path.Combine(directory.Path, StartupLoggingUtility.LogFileName);
        var archivePath = Path.Combine(directory.Path, "startup-errors.log.1");
        File.WriteAllText(logPath, new string('x', 128));

        var report = RuntimeHousekeepingUtility.Clean(
            directory.Path,
            new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero),
            new RuntimeHousekeepingOptions
            {
                MaxStartupLogBytes = 32
            });

        AssertTrue(report.StartupLogRotated, "oversized startup log should be rotated");
        AssertTrue(File.Exists(logPath), "active startup log should be recreated after rotation");
        AssertTrue(File.Exists(archivePath), "startup log archive should be written");
        AssertEqual(128L, new FileInfo(archivePath).Length, "archive should contain the original log");
        AssertContains(File.ReadAllText(logPath), "rotated to startup-errors.log.1");
    }

    internal static void RuntimeHousekeepingSkipsLockedFiles()
    {
        using var directory = TemporaryDirectory.Create();
        var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        var tempDirectory = Path.Combine(directory.Path, "temp");
        Directory.CreateDirectory(tempDirectory);
        var lockedPath = Path.Combine(tempDirectory, "locked.tmp");
        File.WriteAllText(lockedPath, "locked");
        SetLastWriteTimeUtc(lockedPath, now - TimeSpan.FromDays(2));

        using (new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var report = RuntimeHousekeepingUtility.Clean(
                directory.Path,
                now,
                new RuntimeHousekeepingOptions
                {
                    StaleTempFileAge = TimeSpan.FromHours(1)
                });

            AssertTrue(File.Exists(lockedPath), "locked file should be preserved");
            AssertEqual(1, report.SkippedFileCount, "locked file should be counted as skipped");
        }
    }

    internal static void UiOperationFailureReporterLogsStatusAndDialog()
    {
        var startupLogPath = RuntimePathUtility.GetWritableRuntimePath(StartupLoggingUtility.LogFileName);
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;
        var statusMessages = new List<string>();
        var dialogs = new List<(string Title, string Message)>();

        try
        {
            if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }

            UiOperationFailureReporter.ReportAsync(
                new UiOperationFailure(
                    "login info display",
                    "Login info unavailable",
                    "Login Info Error",
                    new InvalidOperationException("cached login info is malformed"),
                    ShowDialog: true),
                statusMessages.Add,
                (title, message) => dialogs.Add((title, message))).GetAwaiter().GetResult();

            AssertEqual(1, statusMessages.Count, "expected reporter to set one status message");
            AssertEqual(
                "Login info unavailable: cached login info is malformed",
                statusMessages[0],
                "unexpected reporter status message");
            AssertEqual(1, dialogs.Count, "expected reporter to show one warning dialog");
            AssertEqual("Login Info Error", dialogs[0].Title, "unexpected dialog title");
            AssertEqual("cached login info is malformed", dialogs[0].Message, "unexpected dialog message");

            var log = File.ReadAllText(startupLogPath);
            AssertContains(log, "login info display");
            AssertContains(log, "InvalidOperationException");
            AssertContains(log, "cached login info is malformed");
        }
        finally
        {
            if (hadStartupLog)
            {
                File.WriteAllText(startupLogPath, originalStartupLog);
            }
            else if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }
        }
    }

    internal static void BackgroundTaskSupervisorSuppressesDuplicatePhases()
    {
        using var supervisor = new BackgroundTaskSupervisor();
        var releaseTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startCount = 0;

        AssertTrue(
            supervisor.TryStart("duplicate phase", async cancellationToken =>
            {
                Interlocked.Increment(ref startCount);
                await releaseTask.Task.WaitAsync(cancellationToken);
            }),
            "expected first background task to start");

        WaitForCondition(() => Volatile.Read(ref startCount) == 1, "background task did not start");
        AssertTrue(supervisor.IsRunning("duplicate phase"), "expected background phase to be running");
        AssertFalse(
            supervisor.TryStart("duplicate phase", _ =>
            {
                Interlocked.Increment(ref startCount);
                return Task.CompletedTask;
            }),
            "expected duplicate background phase to be suppressed");

        releaseTask.SetResult();
        WaitForCondition(() => !supervisor.IsRunning("duplicate phase"), "background task did not complete");
        AssertEqual(1, Volatile.Read(ref startCount), "duplicate phase should not start twice");
    }

    internal static void BackgroundTaskSupervisorLogsFailures()
    {
        var startupLogPath = RuntimePathUtility.GetWritableRuntimePath(StartupLoggingUtility.LogFileName);
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

        try
        {
            if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }

            using var supervisor = new BackgroundTaskSupervisor();
            AssertTrue(
                supervisor.TryStart("supervised failure", _ => throw new InvalidOperationException("supervised boom")),
                "expected failing background task to start");
            WaitForCondition(() => !supervisor.IsRunning("supervised failure"), "failing background task did not complete");

            var log = File.ReadAllText(startupLogPath);
            AssertContains(log, "supervised failure");
            AssertContains(log, "InvalidOperationException");
            AssertContains(log, "supervised boom");
        }
        finally
        {
            if (hadStartupLog)
            {
                File.WriteAllText(startupLogPath, originalStartupLog);
            }
            else if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }
        }
    }

    internal static void BackgroundTaskSupervisorCancelsRunningTasksOnDispose()
    {
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var supervisor = new BackgroundTaskSupervisor();

        AssertTrue(
            supervisor.TryStart("cancellable phase", async cancellationToken =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.SetResult();
                    throw;
                }
            }),
            "expected cancellable background task to start");

        supervisor.Dispose();
        AssertTrue(cancellationObserved.Task.Wait(TimeSpan.FromSeconds(2)), "expected disposal to cancel running background task");
    }

    internal static void AtomicFilePromotionPreservesExistingDestinationOnLockedReplacement()
    {
        using var directory = TemporaryDirectory.Create();
        var destinationPath = Path.Combine(directory.Path, "cache.txt");
        var tempPath = Path.Combine(directory.Path, "cache.txt.tmp");
        File.WriteAllText(destinationPath, "old cache");
        File.WriteAllText(tempPath, "new cache");

        using (new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try
            {
                AtomicFileUtility.PromoteTempFileAsync(tempPath, destinationPath).GetAwaiter().GetResult();
                throw new InvalidOperationException("expected locked destination promotion to fail");
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        AssertEqual("old cache", File.ReadAllText(destinationPath), "existing cache should survive failed promotion");
        AssertTrue(File.Exists(tempPath), "temp file should remain for caller cleanup after failed promotion");
    }

    internal static void AtomicFilePromotionCreatesBoundedRuntimeBackups()
    {
        using var directory = TemporaryDirectory.Create();
        var destinationPath = Path.Combine(directory.Path, "keyword-index.json");
        File.WriteAllText(destinationPath, """{"version":0}""");

        for (var index = 1; index <= 7; index++)
        {
            AtomicFileUtility.WriteAllText(destinationPath, $$"""{"version":{{index}}}""");
            Thread.Sleep(2);
        }

        AssertEqual("""{"version":7}""", File.ReadAllText(destinationPath), "destination should contain newest content");
        var backups = Directory.GetFiles(directory.Path, "keyword-index.bak-*.json");
        AssertEqual(5, backups.Length, "runtime backup retention should keep the newest five backups");
        AssertTrue(
            backups.Any(path => File.ReadAllText(path).Contains("\"version\":6", StringComparison.Ordinal)),
            "newest previous content should be backed up");
        AssertFalse(
            backups.Any(path => File.ReadAllText(path).Contains("\"version\":0", StringComparison.Ordinal)),
            "oldest backup should be pruned");
    }

    internal static void NetworkRequestRetriesTransientFailures()
    {
        var attempts = 0;
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler(async (_, _) =>
        {
            attempts++;
            await Task.Yield();
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok")
                };
        }));

        using var response = NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170&test=retry"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 2, TimeSpan.Zero)).GetAwaiter().GetResult();

        AssertEqual(HttpStatusCode.OK, response.StatusCode, "expected retry to return successful response");
        AssertEqual(2, attempts, "expected transient response to be retried once");
    }

    internal static void OutboundNetworkDiagnosticsRecordsSanitizedSuccessEndpoint()
    {
        var diagnosticsPath = RuntimePathUtility.GetWritableRuntimePath(
                    OutboundNetworkDiagnosticsUtility.DiagnosticsFileName);
        WithPreservedFileAbsent(diagnosticsPath, () =>
        {
            OutboundNetworkDiagnosticsUtility.Reset();
            using var allowlistScope = NetworkUrlAllowlistUtility.UseValidationOverrideForTests((uri, purpose) =>
            {
                if (purpose == NetworkUrlPurpose.Generic
                    && string.Equals(uri.Host, "example.test", StringComparison.OrdinalIgnoreCase))
                {
                    return NetworkUrlAllowlistValidation.Allowed(uri);
                }

                return null;
            });
            using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok")
                })));

            using var response = NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://example.test/search/path?token=secret-token&password=secret-password"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult();

            AssertEqual(HttpStatusCode.OK, response.StatusCode, "expected successful response");
            var diagnosticsJson = File.ReadAllText(diagnosticsPath);
            AssertFalse(diagnosticsJson.Contains("secret-token", StringComparison.Ordinal), "outbound diagnostics should not persist token query values");
            AssertFalse(diagnosticsJson.Contains("secret-password", StringComparison.Ordinal), "outbound diagnostics should not persist password query values");

            using var document = System.Text.Json.JsonDocument.Parse(diagnosticsJson);
            var endpoint = document.RootElement.GetProperty("endpoints")[0];
            AssertEqual("Generic", endpoint.GetProperty("purpose").GetString() ?? string.Empty, "unexpected network purpose");
            AssertEqual("example.test", endpoint.GetProperty("host").GetString() ?? string.Empty, "unexpected host");
            AssertEqual("/search/path", endpoint.GetProperty("path").GetString() ?? string.Empty, "diagnostics should record path without query values");
            AssertTrue(endpoint.GetProperty("query_present").GetBoolean(), "diagnostics should remember that a query string existed");
            AssertEqual(1, endpoint.GetProperty("total_requests").GetInt32(), "expected one recorded request");
            AssertEqual(1, endpoint.GetProperty("success_count").GetInt32(), "expected one successful request");
            AssertEqual(0, endpoint.GetProperty("failure_count").GetInt32(), "expected no failures");
        });
    }

    internal static void OutboundNetworkDiagnosticsRecordsFailureCounts()
    {
        var diagnosticsPath = RuntimePathUtility.GetWritableRuntimePath(
                    OutboundNetworkDiagnosticsUtility.DiagnosticsFileName);
        WithPreservedFileAbsent(diagnosticsPath, () =>
        {
            OutboundNetworkDiagnosticsUtility.Reset();
            using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    ReasonPhrase = "Service Unavailable"
                })));

            using var response = NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero),
                purpose: NetworkUrlPurpose.Rpol).GetAwaiter().GetResult();

            AssertEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode, "expected terminal 503 response");

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(diagnosticsPath));
            var endpoint = document.RootElement.GetProperty("endpoints")[0];
            AssertEqual("Rpol", endpoint.GetProperty("purpose").GetString() ?? string.Empty, "unexpected network purpose");
            AssertEqual("/game.php", endpoint.GetProperty("path").GetString() ?? string.Empty, "unexpected RPOL path");
            AssertEqual(1, endpoint.GetProperty("total_requests").GetInt32(), "expected one recorded request");
            AssertEqual(0, endpoint.GetProperty("success_count").GetInt32(), "expected no successes");
            AssertEqual(1, endpoint.GetProperty("failure_count").GetInt32(), "expected one failure");
            AssertEqual(503, endpoint.GetProperty("last_status_code").GetInt32(), "expected terminal HTTP status to be recorded");
            AssertEqual("failure", endpoint.GetProperty("last_outcome").GetString() ?? string.Empty, "expected failure outcome");
            AssertTrue(endpoint.GetProperty("last_failure_summary").GetString()?.Contains("HTTP 503", StringComparison.Ordinal) ?? false, "expected failure summary to capture status");
        });
    }

    internal static void NetworkRequestRejectsDisallowedHostBeforeSend()
    {
        var attempts = 0;
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));

        var exception = AssertThrows<InvalidOperationException>(() =>
            NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://unexpected.example.test/blocked"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult());

        AssertContains(exception.Message, "not allowed");
        AssertEqual(0, attempts, "disallowed requests should be rejected before the HTTP handler runs");
    }

    internal static void NetworkRequestFollowsAllowedRedirectAtRequestBoundary()
    {
        var requests = new List<Uri>();
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            return requests.Count == 1
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://rpol.net/game.php?gi=80170&redirected=true") }
                })
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));

        using var response = NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero),
            purpose: NetworkUrlPurpose.Rpol).GetAwaiter().GetResult();

        AssertEqual(HttpStatusCode.OK, response.StatusCode, "allowed redirect should return the final response");
        AssertEqual(2, requests.Count, "allowed redirect should send one follow-up request");
        AssertEqual("https://rpol.net/game.php?gi=80170&redirected=true", requests[1].AbsoluteUri, "unexpected redirect target");
    }

    internal static void NetworkRequestRejectsDisallowedRedirectBeforeSend()
    {
        var requests = new List<Uri>();
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("http://127.0.0.1:1/private") }
            });
        }));

        AssertThrows<InvalidOperationException>(() =>
            NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero),
                purpose: NetworkUrlPurpose.Rpol).GetAwaiter().GetResult());

        AssertEqual(1, requests.Count, "disallowed redirect target must never reach the request handler");
    }

    internal static void NetworkRequestDoesNotRetryUnauthorized()
    {
        var attempts = 0;
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }));

        using var response = NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170&test=auth"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 3, TimeSpan.Zero)).GetAwaiter().GetResult();

        AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode, "expected unauthorized response to be returned to caller");
        AssertEqual(1, attempts, "unauthorized response should not be retried");
    }

    internal static void NetworkCircuitBreakerOpensAfterRepeatedTerminalFailures()
    {
        WithPreservedStartupLog(() =>
        {
            NetworkRequestUtility.ResetCircuitBreakersForTests();
            var attempts = 0;
            using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    ReasonPhrase = "Service Unavailable"
                });
            }));

            using (NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170&test=breaker-one"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult())
            {
            }

            using (NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170&test=breaker-two"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult())
            {
            }

            var exception = AssertThrows<NetworkRequestException>(() =>
                NetworkRequestUtility.SendAsync(
                    httpClient,
                    () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170&test=breaker-three"),
                    policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult());

            AssertEqual(NetworkFailureKind.CircuitOpen, exception.Kind, "expected repeated terminal failures to open the circuit breaker");
            AssertEqual(2, attempts, "open circuit breaker should short-circuit before sending another request");
            AssertContains(File.ReadAllText(GetStartupLogPath()), "network circuit breaker");
            NetworkRequestUtility.ResetCircuitBreakersForTests();
        });
    }

    internal static void NetworkCircuitBreakerSeparatesPurposeAndEndpointFamily()
    {
        NetworkRequestUtility.ResetCircuitBreakersForTests();
        using var overrideScope = NetworkUrlAllowlistUtility.UseValidationOverrideForTests((uri, _) =>
            NetworkUrlAllowlistValidation.Allowed(uri));
        var requests = new List<string>();
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((request, _) =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.RequestUri.AbsolutePath.Contains("failure", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK));
        }));

        foreach (var path in new[] { "/failure-one", "/failure-two" })
        {
            using var response = NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://shared.example.test" + path),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), 1, TimeSpan.Zero),
                purpose: NetworkUrlPurpose.Rpol).GetAwaiter().GetResult();
        }

        using var unrelated = NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://shared.example.test/unrelated"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), 1, TimeSpan.Zero),
            purpose: NetworkUrlPurpose.PlayerAssistantBroker).GetAwaiter().GetResult();

        AssertEqual(HttpStatusCode.OK, unrelated.StatusCode, "an open breaker must not suppress a different purpose on the same authority");
        AssertEqual(3, requests.Count, "unrelated endpoint family should reach the handler");
        NetworkRequestUtility.ResetCircuitBreakersForTests();
    }

    internal static void NetworkCircuitBreakerClearsAfterSuccess()
    {
        NetworkRequestUtility.ResetCircuitBreakersForTests();
        var attempts = 0;
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK));
        }));

        using (NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://publish.obsidian.md/breaker-clear-one"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult())
        {
        }

        using (NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://publish.obsidian.md/breaker-clear-two"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult())
        {
        }

        using var response = NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://publish.obsidian.md/breaker-clear-three"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult();

        AssertEqual(HttpStatusCode.OK, response.StatusCode, "successful request should clear prior circuit-breaker failures");
        AssertEqual(3, attempts, "successful request should allow the next related request to be sent");
        NetworkRequestUtility.ResetCircuitBreakersForTests();
    }

    internal static void StartupDependencyMatrixClassifiesTerminalNetworkFailure()
    {
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
            throw new HttpRequestException("synthetic DNS failure")));

        var exception = AssertThrows<NetworkRequestException>(() =>
            NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170"),
                policy: new NetworkRequestPolicy(
                    TimeSpan.FromSeconds(1),
                    MaxAttempts: 1,
                    TimeSpan.Zero)).GetAwaiter().GetResult());

        AssertEqual(NetworkFailureKind.Unavailable, exception.Kind, "terminal request failures should be classified as unavailable");
        AssertContains(exception.Message, "synthetic DNS failure");
    }

    internal static void NetworkRequestWrapsTimeout()
    {
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var exception = AssertThrows<NetworkRequestException>(() =>
            NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170&test=timeout"),
                policy: new NetworkRequestPolicy(TimeSpan.FromMilliseconds(20), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult());

        AssertEqual(NetworkFailureKind.TimedOut, exception.Kind, "expected timeout failures to be classified");
    }

    internal static void NetworkRequestPreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        cancellation.Cancel();

        AssertThrows<OperationCanceledException>(() =>
            NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/cancel"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 3, TimeSpan.Zero),
                cancellationToken: cancellation.Token).GetAwaiter().GetResult());
    }

    internal static void NetworkAllowlistRejectsCredentialedAndEscapedHosts()
    {
        var credentialed = NetworkUrlAllowlistUtility.Validate("https://user:password@rpol.net/game.php", NetworkUrlPurpose.Rpol);
        var escapedHost = NetworkUrlAllowlistUtility.Validate("https://rpol%2enet/game.php", NetworkUrlPurpose.Rpol);
        var threadDisplay = NetworkUrlAllowlistUtility.Validate("https://rpol.net/display.cgi?gi=80170&ti=12&msgpage=&show=all", NetworkUrlPurpose.Rpol);
        var diceRoller = NetworkUrlAllowlistUtility.Validate("https://rpol.net/usermodules/diceroller.cgi?gi=80170", NetworkUrlPurpose.Rpol);
        var unrelatedUserModule = NetworkUrlAllowlistUtility.Validate("https://rpol.net/usermodules/admin.cgi?gi=80170", NetworkUrlPurpose.Rpol);

        AssertFalse(credentialed.IsAllowed, "credentialed URLs should not be allowed");
        AssertContains(credentialed.RejectionReason ?? string.Empty, "credentials");
        AssertFalse(escapedHost.IsAllowed, "escaped host URLs should not be allowed");
        AssertTrue(threadDisplay.IsAllowed, "RPOL thread display URLs should remain valid local search results");
        AssertTrue(diceRoller.IsAllowed, "the exact RPOL Dice Roller URL should be allowed");
        AssertFalse(unrelatedUserModule.IsAllowed, "unrelated RPOL user-module URLs should remain blocked");
    }

    internal static void RpolCredentialSubmissionRequiresExactTrustedHttpsOriginAndPath()
    {
        var trusted = new Uri("https://rpol.net/game.php?gi=80170");
        var wrongScheme = new Uri("http://rpol.net/game.php?gi=80170");
        var wrongHost = new Uri("https://evil.example/game.php?next=rpol.net");
        var lookalikeQuery = new Uri("https://evil.example/?next=rpol.net");
        var wrongPath = new Uri("https://rpol.net/gameinfo.php?gi=80170");
        var subdomain = new Uri("https://login.rpol.net/game.php?gi=80170");

        AssertTrue(
            NetworkUrlAllowlistUtility.IsTrustedRpolCredentialSubmissionUri(trusted),
            "the exact RPOL HTTPS game path should be trusted for credential submission");
        AssertFalse(
            NetworkUrlAllowlistUtility.IsTrustedRpolCredentialSubmissionUri(wrongScheme),
            "HTTP must never be trusted for credential submission");
        AssertFalse(
            NetworkUrlAllowlistUtility.IsTrustedRpolCredentialSubmissionUri(wrongHost),
            "a hostile matching form must not receive credentials");
        AssertFalse(
            NetworkUrlAllowlistUtility.IsTrustedRpolCredentialSubmissionUri(lookalikeQuery),
            "a URL that only mentions rpol.net in a query must not receive credentials");
        AssertFalse(
            NetworkUrlAllowlistUtility.IsTrustedRpolCredentialSubmissionUri(wrongPath),
            "a different RPOL path must not receive credentials");
        AssertFalse(
            NetworkUrlAllowlistUtility.IsTrustedRpolCredentialSubmissionUri(subdomain),
            "an RPOL subdomain must not receive credentials");
    }

    internal static void RpolWebViewNavigationRequiresApprovedHttpsPath()
    {
        AssertTrue(
            NetworkUrlAllowlistUtility.IsTrustedRpolNavigationUri(new Uri("https://rpol.net/game.php?gi=80170")),
            "the configured RPOL game page should be navigable");
        AssertTrue(
            NetworkUrlAllowlistUtility.IsTrustedRpolNavigationUri(new Uri("https://rpol.net/login.cgi?gi=80170")),
            "the exact RPOL login endpoint should be navigable after form submission");
        AssertTrue(
            NetworkUrlAllowlistUtility.IsTrustedRpolNavigationUri(new Uri("https://rpol.net/display.cgi?gi=80170&ti=12")),
            "approved RPOL content paths should remain navigable");
        AssertFalse(
            NetworkUrlAllowlistUtility.IsTrustedRpolNavigationUri(new Uri("http://rpol.net/game.php?gi=80170")),
            "HTTP RPOL navigation must be cancelled");
        AssertFalse(
            NetworkUrlAllowlistUtility.IsTrustedRpolNavigationUri(new Uri("https://evil.example/?next=rpol.net")),
            "untrusted lookalike navigation must be cancelled");
    }

    internal static void RpolWebViewNavigationStartingGuardCancelsUntrustedUrls()
    {
        AssertFalse(
            RpolWebViewVerificationDialog.ShouldCancelNavigation("https://rpol.net/game.php?gi=80170"),
            "NavigationStarting should allow the trusted RPOL game page");
        AssertFalse(
            RpolWebViewVerificationDialog.ShouldCancelNavigation("https://rpol.net/login.cgi?gi=80170"),
            "NavigationStarting should allow the exact RPOL login endpoint");
        AssertTrue(
            RpolWebViewVerificationDialog.ShouldCancelNavigation("https://evil.example/?next=rpol.net"),
            "NavigationStarting should cancel a hostile lookalike URL");
        AssertTrue(
            RpolWebViewVerificationDialog.ShouldCancelNavigation("http://rpol.net/game.php?gi=80170"),
            "NavigationStarting should cancel HTTP RPOL navigation");
        AssertTrue(
            RpolWebViewVerificationDialog.ShouldCancelNavigation(null),
            "NavigationStarting should cancel an unparsable or missing URL");
    }

    internal static void NetworkAllowlistAcceptsObsidianPublishContentHosts()
    {
        var page = NetworkUrlAllowlistUtility.Validate(
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            NetworkUrlPurpose.ObsidianPublish);
        var markdown = NetworkUrlAllowlistUtility.Validate(
            "https://publish-01.obsidian.md/access/1113217a28a5bfdcc9fbe8e6d82b27ac/Intentional%20Orphans/XP%20Tracking.md",
            NetworkUrlPurpose.ObsidianPublish);
        var rejected = NetworkUrlAllowlistUtility.Validate(
            "https://help.obsidian.md/access/1113217a28a5bfdcc9fbe8e6d82b27ac/Intentional%20Orphans/XP%20Tracking.md",
            NetworkUrlPurpose.ObsidianPublish);

        AssertTrue(page.IsAllowed, "public Obsidian Publish pages should remain allowed");
        AssertTrue(markdown.IsAllowed, "Obsidian Publish generated markdown access URLs should be allowed");
        AssertTrue(
            NetworkUrlAllowlistUtility.IsObsidianPublishHost(new Uri("https://publish-01.obsidian.md/")),
            "generated Obsidian Publish content hosts should be recognized");
        AssertFalse(rejected.IsAllowed, "non-Publish obsidian.md hosts should not be accepted");
    }

    internal static void NetworkAllowlistRejectsUnexpectedHostedSettingsPath()
    {
        var allowed = NetworkUrlAllowlistUtility.Validate(
            "https://bryanmiller.us/scarlethorizons/settings.local.json",
            NetworkUrlPurpose.PlayerAssistantHostedSettings);
        var rejected = NetworkUrlAllowlistUtility.Validate(
            "https://bryanmiller.us/scarlethorizons/other-settings.json",
            NetworkUrlPurpose.PlayerAssistantHostedSettings);

        AssertTrue(allowed.IsAllowed, "expected the configured hosted settings path to remain allowed");
        AssertFalse(rejected.IsAllowed, "unexpected hosted settings paths should be rejected");
        AssertContains(rejected.RejectionReason ?? string.Empty, "settings.local.json");
    }

    internal static void NetworkAllowlistRejectsUnexpectedUpdatePath()
    {
        var allowedManifest = NetworkUrlAllowlistUtility.Validate(
            "https://bryanmiller.us/scarlethorizons/p-assist-updates.json",
            NetworkUrlPurpose.PlayerAssistantUpdate);
        var allowedArchive = NetworkUrlAllowlistUtility.Validate(
            "https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.zip",
            NetworkUrlPurpose.PlayerAssistantUpdate);
        var rejected = NetworkUrlAllowlistUtility.Validate(
            "https://bryanmiller.us/private/p-assist-0.9.1.zip",
            NetworkUrlPurpose.PlayerAssistantUpdate);

        AssertTrue(allowedManifest.IsAllowed, "expected the signed update manifest path to remain allowed");
        AssertTrue(allowedArchive.IsAllowed, "expected approved update archive paths to remain allowed");
        AssertFalse(rejected.IsAllowed, "unexpected update paths should be rejected");
        AssertContains(rejected.RejectionReason ?? string.Empty, "/scarlethorizons/");
    }

    internal static void NetworkAllowlistGenericPolicyRejectsUnrelatedUpdateHostPaths()
    {
        var genericAllowed = NetworkUrlAllowlistUtility.Validate("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.exe");
        var regionalMap = NetworkUrlAllowlistUtility.Validate("https://bryanmiller.us/scarlethorizons/maps/northernreaches.png");
        var obsoleteRegionalMap = NetworkUrlAllowlistUtility.Validate("https://bryanmiller.us/scarlethorizons/northernreaches.png");
        var obsoleteBlogRegionalMap = NetworkUrlAllowlistUtility.Validate("https://bryanmiller.us/blog/content/bryan/blog/images/rpg-maps/northernreaches.png");
        var genericRejected = NetworkUrlAllowlistUtility.Validate("https://bryanmiller.us/random-note.txt");

        AssertTrue(genericAllowed.IsAllowed, "generic allowlist should still permit approved update artifact paths");
        AssertTrue(regionalMap.IsAllowed, "generic allowlist should permit the hosted regional map image");
        AssertFalse(obsoleteRegionalMap.IsAllowed, "generic allowlist should reject the obsolete regional map path");
        AssertFalse(obsoleteBlogRegionalMap.IsAllowed, "generic allowlist should reject the obsolete blog regional map path");
        AssertFalse(genericRejected.IsAllowed, "generic allowlist should reject unrelated paths on an otherwise approved host");
    }

    internal static void NetworkResponseLimitsDefineDefaults()
    {
        AssertTrue(NetworkResponseContentLimit.Html.MaxBytes > 0, "HTML response limit should be positive");
        AssertTrue(NetworkResponseContentLimit.Markdown.MaxBytes > 0, "markdown response limit should be positive");
        AssertTrue(NetworkResponseContentLimit.JsonCache.MaxBytes > 0, "JSON cache response limit should be positive");
        AssertTrue(NetworkResponseContentLimit.Image.MaxBytes > 0, "image response limit should be positive");
        AssertTrue(
            NetworkResponseContentLimit.Image.MaxBytes > NetworkResponseContentLimit.Markdown.MaxBytes,
            "image downloads should allow larger payloads than markdown documents");
    }

    internal static void NetworkResponseLimitRejectsOversizedHtmlHeader()
    {
        using var content = new ByteArrayContent([]);
        content.Headers.ContentLength = NetworkResponseContentLimit.Html.MaxBytes + 1;

        var exception = AssertThrows<NetworkResponseTooLargeException>(() =>
            NetworkRequestUtility.ReadStringAsync(
                content,
                NetworkResponseContentLimit.Html).GetAwaiter().GetResult());

        AssertContains(exception.Message, "HTML response");
    }

    internal static void NetworkResponseLimitRejectsOversizedMarkdownStream()
    {
        using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("abcdef"));
        using var destination = new MemoryStream();
        var limit = new NetworkResponseContentLimit("markdown response", 5);

        var exception = AssertThrows<NetworkResponseTooLargeException>(() =>
            NetworkRequestUtility.CopyToAsync(source, destination, limit).GetAwaiter().GetResult());

        AssertContains(exception.Message, "markdown response");
        AssertEqual(0L, destination.Length, "oversized markdown stream should not be written after limit breach");
    }

    internal static void NetworkResponseLimitRejectsOversizedJsonCacheStream()
    {
        using var content = new ChunkedHttpContent(System.Text.Encoding.UTF8.GetBytes("""{"oversized":true}"""));
        var limit = new NetworkResponseContentLimit("JSON cache response", 8);

        var exception = AssertThrows<NetworkResponseTooLargeException>(() =>
            NetworkRequestUtility.ReadBytesAsync(content, limit).GetAwaiter().GetResult());

        AssertContains(exception.Message, "JSON cache response");
    }

    internal static void NetworkResponseLimitRejectsOversizedImageHeader()
    {
        using var content = new ByteArrayContent([]);
        content.Headers.ContentLength = NetworkResponseContentLimit.Image.MaxBytes + 1;

        var exception = AssertThrows<NetworkResponseTooLargeException>(() =>
            NetworkRequestUtility.CopyToAsync(
                content,
                Stream.Null,
                NetworkResponseContentLimit.Image).GetAwaiter().GetResult());

        AssertContains(exception.Message, "image response");
    }

    internal static void MarkdownAsyncFetchPreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        AssertThrows<OperationCanceledException>(() =>
            MarkdownUtility.GetMarkdownFromUrlAsync(
                "https://publish.obsidian.md/cancel",
                cancellation.Token).GetAwaiter().GetResult());
    }

    internal static void RuntimeArtifactLoaderQuarantinesMalformedJson()
    {
        using var directory = TemporaryDirectory.Create();
        var artifactPath = Path.Combine(directory.Path, "runtime-cache.json");
        File.WriteAllText(artifactPath, "{ not valid json");
        var startupLogPath = RuntimePathUtility.GetWritableRuntimePath(StartupLoggingUtility.LogFileName);
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

        try
        {
            if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }

            var loaded = RuntimeArtifactUtility.TryLoadJson<Dictionary<string, string>>(
                artifactPath,
                "runtime artifact test",
                out var value);

            AssertFalse(loaded, "malformed runtime artifact should not load");
            AssertTrue(value is null, "malformed runtime artifact should return a null value");
            AssertFalse(File.Exists(artifactPath), "malformed runtime artifact should be moved out of the active path");

            var badFiles = Directory.GetFiles(directory.Path, "runtime-cache.bad-*.json");
            AssertEqual(1, badFiles.Length, "expected one quarantined runtime artifact");
            AssertEqual("{ not valid json", File.ReadAllText(badFiles[0]), "quarantined artifact should preserve original content");

            var startupLog = File.ReadAllText(startupLogPath);
            AssertContains(startupLog, "runtime artifact test");
            AssertContains(startupLog, badFiles[0]);
        }
        finally
        {
            if (hadStartupLog)
            {
                File.WriteAllText(startupLogPath, originalStartupLog);
            }
            else if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }
        }
    }

    internal static void RuntimeArtifactLoaderRestoresNewestValidBackup()
    {
        using var directory = TemporaryDirectory.Create();
        var artifactPath = Path.Combine(directory.Path, "runtime-cache.json");
        var olderBackupPath = Path.Combine(directory.Path, "runtime-cache.bak-20260701-010203-001.json");
        var invalidBackupPath = Path.Combine(directory.Path, "runtime-cache.bak-20260702-010203-001.json");
        var newestBackupPath = Path.Combine(directory.Path, "runtime-cache.bak-20260703-010203-001.json");
        File.WriteAllText(artifactPath, "{ not valid json");
        File.WriteAllText(olderBackupPath, """{"value":"old"}""");
        File.WriteAllText(invalidBackupPath, "{ also invalid");
        File.WriteAllText(newestBackupPath, """{"value":"restored"}""");
        SetLastWriteTimeUtc(olderBackupPath, new DateTimeOffset(2026, 7, 1, 1, 2, 3, TimeSpan.Zero));
        SetLastWriteTimeUtc(invalidBackupPath, new DateTimeOffset(2026, 7, 2, 1, 2, 3, TimeSpan.Zero));
        SetLastWriteTimeUtc(newestBackupPath, new DateTimeOffset(2026, 7, 3, 1, 2, 3, TimeSpan.Zero));

        WithPreservedStartupLog(() =>
        {
            var loaded = RuntimeArtifactUtility.TryLoadJson<Dictionary<string, string>>(
                artifactPath,
                "runtime artifact restore test",
                out var value);

            AssertTrue(loaded, "malformed runtime artifact should restore from the newest valid backup");
            AssertTrue(value is not null, "restored runtime artifact should return a value");
            AssertEqual("restored", value!["value"], "unexpected restored value");
            AssertEqual("""{"value":"restored"}""", File.ReadAllText(artifactPath), "active artifact should be restored from backup");
            AssertEqual(0, Directory.GetFiles(directory.Path, "runtime-cache.bad-*.json").Length, "restored artifact should not be quarantined");

            var startupLog = File.ReadAllText(GetStartupLogPath());
            AssertContains(startupLog, "runtime artifact restore test");
            AssertContains(startupLog, "Restored runtime artifact");
        });
    }

    internal static void StartupDependencyMatrixLogsLockedRuntimeArtifactFailures()
    {
        using var directory = TemporaryDirectory.Create();
        var artifactPath = Path.Combine(directory.Path, "locked-artifact.json");
        File.WriteAllText(artifactPath, "{}");

        WithPreservedStartupLog(() =>
        {
            using (new FileStream(artifactPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var loaded = RuntimeArtifactUtility.TryLoadJson<Dictionary<string, string>>(
                    artifactPath,
                    "locked runtime artifact test",
                    out var value);

                AssertFalse(loaded, "locked runtime artifact should fail without throwing");
                AssertTrue(value is null, "locked runtime artifact should return no value");
                AssertTrue(File.Exists(artifactPath), "locked runtime artifact should remain active when quarantine cannot move it");
            }

            var startupLog = File.ReadAllText(GetStartupLogPath());
            AssertContains(startupLog, "locked runtime artifact test");
            AssertContains(startupLog, "runtime artifact quarantine");
        });
    }

    internal static void AssetManifestLoadReturnsEmptyForMalformedJson()
    {
        var startupLogPath = RuntimePathUtility.GetWritableRuntimePath(StartupLoggingUtility.LogFileName);
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

        try
        {
            if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }

            var args = new object[] { "{ not valid json", null! };
            var loaded = (bool)(InvokeStaticMethod(
                typeof(PlayerCharacterAssetUtility),
                "TryDeserializeAssetManifest",
                args) ?? throw new InvalidOperationException("TryDeserializeAssetManifest returned null."));

            AssertFalse(loaded, "malformed asset manifest should not load");
            AssertTrue(args[1] is Dictionary<string, string> manifest && manifest.Count == 0, "malformed asset manifest should return an empty dictionary");

            var startupLog = File.ReadAllText(startupLogPath);
            AssertContains(startupLog, "asset manifest load");
            AssertContains(startupLog, "asset manifest could not be parsed");
        }
        finally
        {
            if (hadStartupLog)
            {
                File.WriteAllText(startupLogPath, originalStartupLog);
            }
            else if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }
        }
    }

    internal static void PublishedAssetFallbackResolvesTransclusionWithoutAttachmentIndex()
    {
        var cachePaths = new[]
        {
            "Assets/hero-tokens/neria-token.webp",
            "PCs/Neria Silverdale.md"
        };

        var assets = (Dictionary<string, string>?)InvokeStaticMethod(
            typeof(ObsidianPublishUtility),
            "GetAssetPathsByFileName",
            (object)cachePaths)
            ?? throw new InvalidOperationException("GetAssetPathsByFileName returned null.");

        AssertEqual(
            "Assets/hero-tokens/neria-token.webp",
            assets["neria-token.webp"],
            "listing transclusion should resolve directly from the published asset cache");
        AssertFalse(assets.ContainsKey("Neria Silverdale.md"), "markdown pages should not be treated as image assets");
    }

    internal static void FormerPcListingParsesThreeColumnHeroRows()
    {
        const string listingMarkdown = """
            | Name | Class | Token |
            | --- | --- | --- |
            | [[Urvan Hall, paladin of St. Ygg\|Urvan]] | Paladin | ![[urvan-token.webp\|70]] |
            | [['Slip' Harren, Thief\|Slip]] | Thief | ![[slip-token.webp\|70]] |
            | [[Narinza Izrut\|Narinza]] | Thief | ![[narinza-token.webp\|70]] |
            """;

        var rows = PlayerCharacterAssetUtility.GetHeroRows(
            listingMarkdown,
            "https://publish.obsidian.md/scarlethorizons/PCs/Former+PCs");

        AssertEqual(3, rows.Length, "all former PC rows should parse");
        AssertEqual("Urvan", rows[0].Name, "former PC alias should become the display name");
        AssertEqual(
            "urvan-token.webp",
            rows[0].TokenFileName ?? string.Empty,
            "former PC token should parse from the Token column");
        AssertContains(rows[0].CharacterPageUrl ?? string.Empty, "Urvan+Hall");
    }

    internal static void ActiveHeroListingCarriesCanonicalIdentity()
    {
        var rows = PlayerCharacterAssetUtility.GetHeroRows("""
            | Name | Canonical ID | Class | Level | HP |
            | --- | --- | --- | --- | --- |
            | [[Ari Stoneward]] | fixture-ari-stoneward-001 | Ranger | 4 | 31 |
            """);

        AssertEqual(1, rows.Length, "canonical identity listing should produce one hero row");
        AssertEqual(
            "fixture-ari-stoneward-001",
            rows[0].CanonicalId ?? string.Empty,
            "hero listing should preserve the canonical identity column");
    }

    internal static void ActiveHeroMarkdownCancellationWritesNoFiles()
    {
        using var directory = TemporaryDirectory.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        AssertThrows<OperationCanceledException>(() =>
            PlayerCharacterAssetUtility.DownloadActiveHeroMarkdownAsync(
                """
                | Name | Character | Notes | Hero |
                | --- | --- | --- | --- |
                | Alice Example | [[Alice Example]] | active | ![[alice-token.webp]] |
                """,
                "https://publish.obsidian.md/example/PCs/Player+Characters+Listing",
                directory.Path,
                cancellation.Token).GetAwaiter().GetResult());

        var activeDirectory = Path.Combine(directory.Path, "active");
        AssertFalse(
            Directory.Exists(activeDirectory) && Directory.EnumerateFiles(activeDirectory).Any(),
            "canceled hero markdown refresh should not write active hero files");
    }

    internal static void FormerHeroMarkdownCancellationWritesNoInactiveFiles()
    {
        using var directory = TemporaryDirectory.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        AssertThrows<OperationCanceledException>(() =>
            PlayerCharacterAssetUtility.DownloadFormerHeroMarkdownAsync(
                """
                | Name | Class | Token |
                | --- | --- | --- |
                | [[Urvan Hall\|Urvan]] | Paladin | ![[urvan-token.webp]] |
                """,
                "https://publish.obsidian.md/scarlethorizons/PCs/Former+PCs",
                directory.Path,
                cancellation.Token).GetAwaiter().GetResult());

        var inactiveDirectory = Path.Combine(directory.Path, "inactive");
        AssertFalse(
            Directory.Exists(inactiveDirectory) && Directory.EnumerateFiles(inactiveDirectory).Any(),
            "canceled former hero markdown download should not write inactive files");
    }

    internal static void PlayerCharacterRefreshCancellationClearsInProgressFlag()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            SetPrivateField(form, "_showWelcomeText", false);
            InvokePrivateAsync(
                form,
                "StartPlayerCharacterListingUpdateAsync",
                false,
                cancellation.Token).GetAwaiter().GetResult();

            AssertFalse(
                (bool)(GetPrivateField(form, "_playerCharacterListingUpdateStarted") ?? true),
                "canceled player-character refresh should clear the in-progress flag");
        });
    }

    internal static void HeroImageShowcaseWaitsForInitialPlayerCharacterRefresh()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: false);
            SetPrivateField(form, "_showWelcomeText", false);
            SetPrivateField(form, "_activePlayerCharacterImagePaths", new[] { "cached-nerissa-token.webp" });

            InvokePrivateMethod(form, "StartHeroImageShowcaseIfReady");
            AssertFalse(
                (bool)(GetPrivateField(form, "_heroImageIntroStarted") ?? true),
                "cached hero images should not start before the active listing has been refreshed");

            SetPrivateField(form, "_initialPlayerCharacterListingRefreshCompleted", true);
            InvokePrivateMethod(form, "StartHeroImageShowcaseIfReady");
            AssertTrue(
                (bool)(GetPrivateField(form, "_heroImageIntroStarted") ?? false),
                "the showcase should become eligible after the active listing refresh completes");
        });
    }

    internal static void GameForumStartupCancellationWritesNoManifests()
    {
        RunOnStaThread(() =>
        {
            var startupLogPath = RuntimePathUtility.GetWritableRuntimePath(StartupLoggingUtility.LogFileName);
            var manifestPaths = new[]
            {
                RuntimePathUtility.GetWritableRuntimePath("game-forum-chapter-prefixes.txt"),
                RuntimePathUtility.GetWritableRuntimePath("game-forum-chapter-downloads.txt"),
                RuntimePathUtility.GetWritableRuntimePath("game-forum-aside-downloads.txt"),
                RuntimePathUtility.GetWritableRuntimePath("game-forum-ooc-downloads.txt")
            };
            var preservedFiles = manifestPaths
                .Append(startupLogPath)
                .Select(path => (Path: path, Exists: File.Exists(path), Content: File.Exists(path) ? File.ReadAllText(path) : null))
                .ToArray();

            try
            {
                foreach (var path in manifestPaths.Append(startupLogPath))
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }

                using var form = new Form1(suppressHeroImagesForThisRun: true);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();

                AssertThrows<OperationCanceledException>(() =>
                    InvokePrivateAsync(
                        form,
                        "LoadGameForumChapterPrefixesAsync",
                        cancellation.Token).GetAwaiter().GetResult());

                foreach (var manifestPath in manifestPaths)
                {
                    AssertFalse(File.Exists(manifestPath), $"canceled game-forum startup should not write {Path.GetFileName(manifestPath)}");
                }

                AssertFalse(File.Exists(startupLogPath), "canceled game-forum startup should not be logged as a startup failure");
            }
            finally
            {
                foreach (var preservedFile in preservedFiles)
                {
                    if (preservedFile.Exists)
                    {
                        File.WriteAllText(preservedFile.Path, preservedFile.Content ?? string.Empty);
                    }
                    else if (File.Exists(preservedFile.Path))
                    {
                        File.Delete(preservedFile.Path);
                    }
                }
            }
        });
    }

    internal static void KeywordIndexLoaderQuarantinesMalformedJson()
    {
        using var directory = TemporaryDirectory.Create();
        var indexPath = Path.Combine(directory.Path, "keyword-index.json");
        File.WriteAllText(indexPath, "{ not valid json");

        var startupLogPath = RuntimePathUtility.GetWritableRuntimePath(StartupLoggingUtility.LogFileName);
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

        try
        {
            var loadTask = (Task?)InvokeStaticMethod(
                typeof(KeywordIndexCrawler),
                "LoadExistingDocumentAsync",
                indexPath,
                CancellationToken.None);
            loadTask?.GetAwaiter().GetResult();

            var badIndexFiles = Directory.GetFiles(directory.Path, "keyword-index.bad-*.json");
            AssertFalse(File.Exists(indexPath), "malformed keyword index should be moved out of the active path");
            AssertEqual(1, badIndexFiles.Length, "expected one quarantined keyword index file");
            AssertEqual("{ not valid json", File.ReadAllText(badIndexFiles[0]), "quarantined keyword index should preserve original content");

            var startupLog = File.ReadAllText(startupLogPath);
            AssertContains(startupLog, "keyword index recovery");
            AssertContains(startupLog, "could not be loaded");
            AssertContains(startupLog, badIndexFiles[0]);
        }
        finally
        {
            if (hadStartupLog)
            {
                File.WriteAllText(startupLogPath, originalStartupLog);
            }
            else if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }
        }
    }

    internal static void KeywordIndexLoaderSalvagesLegacyDisallowedUrls()
    {
        using var directory = TemporaryDirectory.Create();
        var indexPath = Path.Combine(directory.Path, "keyword-index.json");
        File.WriteAllText(
            indexPath,
            """
            {
              "index_metadata": {
                "generated_at": "2026-06-30T00:00:00.0000000+00:00",
                "total_words_indexed": 10
              },
              "urls": {
                "https://rpol.net/admin/gm.cgi?gi=80170": {
                  "source": "RPOL"
                },
                "https://publish.obsidian.md/scarlethorizons/Safe": {
                  "source": "Obsidian wiki"
                }
              },
              "words": {
                "Kelpie": {
                  "total_occurrences": 2,
                  "matches": [
                    {
                      "url": "https://rpol.net/admin/gm.cgi?gi=80170",
                      "count": 1,
                      "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                    },
                    {
                      "url": "https://publish.obsidian.md/scarlethorizons/Safe",
                      "count": 1,
                      "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                    }
                  ]
                }
              }
            }
            """);

        var loadTask = (Task)(InvokeStaticMethod(
            typeof(KeywordIndexCrawler),
            "LoadExistingDocumentAsync",
            indexPath,
            CancellationToken.None)
            ?? throw new InvalidOperationException("expected keyword index load task"));
        loadTask.GetAwaiter().GetResult();

        var result = loadTask
            .GetType()
            .GetProperty("Result")
            ?.GetValue(loadTask)
            ?? throw new InvalidOperationException("expected sanitized keyword index document");
        var words = result.GetType().GetProperty("Words")?.GetValue(result)
            ?? throw new InvalidOperationException("expected keyword index words");
        var kelpieEntry = ((System.Collections.IDictionary)words)["Kelpie"]
            ?? throw new InvalidOperationException("expected Kelpie entry to survive sanitization");
        var matches = kelpieEntry.GetType().GetProperty("Matches")?.GetValue(kelpieEntry)
            ?? throw new InvalidOperationException("expected Kelpie matches");

        AssertTrue(File.Exists(indexPath), "legacy keyword index should not be quarantined when useful entries can be salvaged");
        AssertEqual(0, Directory.GetFiles(directory.Path, "keyword-index.bad-*.json").Length, "legacy keyword index should not be moved aside");
        AssertEqual(1, ((System.Collections.ICollection)matches).Count, "expected only allowed matches to survive sanitization");
    }

    internal static void SitemapValidationRejectsPoisonedUrl()
    {
        var exception = AssertThrows<InvalidOperationException>(() =>
            SitemapUtility.ValidateSitemapXml(
                """
                <urlset>
                  <url>
                    <loc>https://user:password@publish.obsidian.md/scarlethorizons/Poison</loc>
                  </url>
                </urlset>
                """));

        AssertContains(exception.Message, "sitemap.xml contains a URL that is not allowed");
        AssertContains(exception.Message, "embedded credentials");
    }

    internal static void SitemapKeywordDictionaryPreservesExistingOutputOnRejectedUrl()
    {
        using var directory = TemporaryDirectory.Create();
        var sitemapPath = Path.Combine(directory.Path, "sitemap.xml");
        var dictionaryPath = Path.Combine(directory.Path, "sitemap-keyword-urls.json");
        File.WriteAllText(dictionaryPath, """{"safe":"https://publish.obsidian.md/scarlethorizons/Safe"}""");
        File.WriteAllText(
            sitemapPath,
            """
            <urlset>
              <url>
                <loc>https://evil.example.test/Poison</loc>
              </url>
            </urlset>
            """);

        var exception = AssertThrows<InvalidOperationException>(() =>
            SitemapUtility.WriteKeywordUrlDictionaryAsync(sitemapPath, dictionaryPath).GetAwaiter().GetResult());

        AssertContains(exception.Message, "sitemap.xml contains a URL that is not allowed");
        AssertContains(File.ReadAllText(dictionaryPath), "https://publish.obsidian.md/scarlethorizons/Safe");
    }

    internal static void SourceIntegrityRecordsFirstAcceptedSitemap()
    {
        using var directory = TemporaryDirectory.Create();
        var sitemapPath = Path.Combine(directory.Path, "sitemap.xml");
        var sitemapXml = CreateSitemapXml(
            "https://publish.obsidian.md/scarlethorizons/One",
            "https://publish.obsidian.md/scarlethorizons/Two");

        SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
            sitemapPath,
            "https://publish.obsidian.md/scarlethorizons/sitemap.xml",
            "obsidian-sitemap",
            sitemapXml,
            SourceIntegrityUtility.CreateSitemapShape(sitemapXml)).GetAwaiter().GetResult();

        var sidecarPath = SourceIntegrityUtility.GetSidecarPath(sitemapPath);
        AssertTrue(File.Exists(sidecarPath), "source integrity sidecar should be written for accepted sitemap");
        AssertContains(File.ReadAllText(sidecarPath), "\"artifact_kind\": \"obsidian-sitemap\"");
        AssertContains(File.ReadAllText(sidecarPath), "\"url_count\": 2");
    }

    internal static void SourceIntegrityRejectsCollapsedSitemapAndPreservesOutput()
    {
        using var directory = TemporaryDirectory.Create();
        var sitemapPath = Path.Combine(directory.Path, "sitemap.xml");
        var originalSitemap = CreateSitemapXml(
            "https://publish.obsidian.md/scarlethorizons/One",
            "https://publish.obsidian.md/scarlethorizons/Two",
            "https://publish.obsidian.md/scarlethorizons/Three",
            "https://publish.obsidian.md/scarlethorizons/Four");
        SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
            sitemapPath,
            "https://publish.obsidian.md/scarlethorizons/sitemap.xml",
            "obsidian-sitemap",
            originalSitemap,
            SourceIntegrityUtility.CreateSitemapShape(originalSitemap)).GetAwaiter().GetResult();

        var collapsedSitemap = CreateSitemapXml("https://publish.obsidian.md/scarlethorizons/One");
        var exception = AssertThrows<InvalidOperationException>(() =>
            SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
                sitemapPath,
                "https://publish.obsidian.md/scarlethorizons/sitemap.xml",
                "obsidian-sitemap",
                collapsedSitemap,
                SourceIntegrityUtility.CreateSitemapShape(collapsedSitemap)).GetAwaiter().GetResult());

        AssertContains(exception.Message, "Authenticated source tamper detection rejected fetched content");
        AssertContains(exception.Message, "last-known-good content was preserved");
        AssertEqual(originalSitemap, File.ReadAllText(sitemapPath), "collapsed sitemap should not replace last known good sitemap");
    }

    internal static void SourceIntegrityRejectsCollapsedMarkdownAndPreservesOutput()
    {
        using var directory = TemporaryDirectory.Create();
        var markdownPath = Path.Combine(directory.Path, "kelpie.md");
        var originalMarkdown = """
            # Kelpie Lawfuller
            ## Summary
            Useful notes.
            ## Links
            [[Allies]]
            [Map](https://publish.obsidian.md/scarlethorizons/Map)
            """;
        SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
            markdownPath,
            "https://publish.obsidian.md/scarlethorizons/PCs/Kelpie",
            "obsidian-markdown",
            originalMarkdown,
            SourceIntegrityUtility.CreateMarkdownShape(originalMarkdown)).GetAwaiter().GetResult();

        var collapsedMarkdown = "# Kelpie";
        var exception = AssertThrows<InvalidOperationException>(() =>
            SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
                markdownPath,
                "https://publish.obsidian.md/scarlethorizons/PCs/Kelpie",
                "obsidian-markdown",
                collapsedMarkdown,
                SourceIntegrityUtility.CreateMarkdownShape(collapsedMarkdown)).GetAwaiter().GetResult());

        AssertContains(exception.Message, "Authenticated source tamper detection rejected fetched content");
        AssertEqual(originalMarkdown, File.ReadAllText(markdownPath), "collapsed markdown should not replace last known good markdown");
    }

    internal static void SourceIntegrityRejectsCollapsedKeywordIndexAndPreservesOutput()
    {
        using var directory = TemporaryDirectory.Create();
        var indexPath = Path.Combine(directory.Path, "keyword-index.json");
        var originalIndex = """{"urls":{"u1":{},"u2":{},"u3":{},"u4":{}},"words":{"safe":{}}}""";
        SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
            indexPath,
            "keyword-index-crawl",
            "keyword-index",
            originalIndex,
            SourceIntegrityUtility.CreateKeywordIndexShape(4, 1, 4)).GetAwaiter().GetResult();

        var collapsedIndex = """{"urls":{"u1":{}},"words":{}}""";
        var exception = AssertThrows<InvalidOperationException>(() =>
            SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
                indexPath,
                "keyword-index-crawl",
                "keyword-index",
                collapsedIndex,
                SourceIntegrityUtility.CreateKeywordIndexShape(1, 0, 0)).GetAwaiter().GetResult());

        AssertContains(exception.Message, "Authenticated source tamper detection rejected fetched content");
        AssertEqual(originalIndex, File.ReadAllText(indexPath), "collapsed keyword index should not replace last known good index");
    }

    internal static void KeywordIndexValidationRejectsPoisonedUrlEntries()
    {
        var exception = AssertThrows<InvalidOperationException>(() =>
            KeywordIndexCrawler.ValidateKeywordIndexJson(
                """
                {
                  "index_metadata": {
                    "generated_at": "2026-07-04T00:00:00Z",
                    "total_words_indexed": 0
                  },
                  "urls": {
                    "https://evil.example.test/Poison": {
                      "source": "Obsidian wiki"
                    }
                  },
                  "words": {}
                }
                """));

        AssertContains(exception.Message, "keyword-index urls contains a URL that is not allowed");
        AssertContains(exception.Message, "Obsidian Publish page and note URLs");
    }

    internal static void KeywordIndexValidationRejectsPoisonedMatchUrls()
    {
        var exception = AssertThrows<InvalidOperationException>(() =>
            KeywordIndexCrawler.ValidateKeywordIndexJson(
                """
                {
                  "index_metadata": {
                    "generated_at": "2026-07-04T00:00:00Z",
                    "total_words_indexed": 1
                  },
                  "urls": {
                    "https://publish.obsidian.md/scarlethorizons/Safe": {
                      "source": "Obsidian wiki"
                    }
                  },
                  "words": {
                    "safe": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "file:///C:/secret.txt",
                          "count": 1,
                          "last_indexed": "2026-07-04T00:00:00Z"
                        }
                      ]
                    }
                  }
                }
                """));

        AssertContains(exception.Message, "keyword-index matches for 'safe' contains a URL that is not allowed");
        AssertContains(exception.Message, "Only HTTP and HTTPS");
    }

    internal static void KeywordTermsReleaseCopyGeneratesFromKeywordIndex()
    {
        using var directory = TemporaryDirectory.Create();
        var runtimeDirectory = Path.Combine(directory.Path, "runtime");
        Directory.CreateDirectory(runtimeDirectory);
        const string indexJson =
            """
            {
              "words": {
                "zeta": {},
                "Alpha": {},
                "beta": {}
              }
            }
            """;

        File.WriteAllText(Path.Combine(runtimeDirectory, "keyword-index.json"), indexJson);
        KeywordTermsFileUtility.EnsureReleaseCopy(runtimeDirectory);

        var termsPath = Path.Combine(runtimeDirectory, KeywordTermsFileUtility.FileName);
        AssertTrue(File.Exists(termsPath), "expected keyword terms file to be generated");
        AssertEqual(
            "Alpha|beta|zeta",
            string.Join("|", File.ReadAllLines(termsPath)),
            "generated keyword terms should be sorted from keyword index words");
    }

    internal static void KeywordTermsPublishCopyPreservesParentReleaseTerms()
    {
        using var directory = TemporaryDirectory.Create();
        var publishDirectory = Path.Combine(directory.Path, "publish");
        Directory.CreateDirectory(publishDirectory);

        var parentTermsPath = Path.Combine(directory.Path, KeywordTermsFileUtility.FileName);
        var publishTermsPath = Path.Combine(publishDirectory, KeywordTermsFileUtility.FileName);
        File.WriteAllText(parentTermsPath, "parent-term");
        File.WriteAllText(publishTermsPath, "publish-term");

        KeywordTermsFileUtility.EnsureReleaseCopy(publishDirectory);

        AssertTrue(File.Exists(parentTermsPath), "running from publish should not delete parent Release keyword terms");
        AssertTrue(File.Exists(publishTermsPath), "running from publish should keep its own keyword terms");
        AssertEqual("parent-term", File.ReadAllText(parentTermsPath), "parent Release keyword terms should be unchanged");
        AssertEqual("publish-term", File.ReadAllText(publishTermsPath), "publish keyword terms should be unchanged");
    }

    internal static void StartupStatusIncludesDownloadCountAndSize()
    {
        using var directory = TemporaryDirectory.Create();
        var firstPath = Path.Combine(directory.Path, "first.bin");
        var secondPath = Path.Combine(directory.Path, "second.bin");
        File.WriteAllBytes(firstPath, new byte[1024]);
        File.WriteAllBytes(secondPath, new byte[512]);

        FileDownloadCounters.Reset();
        FileDownloadCounters.AddCompletedDownload(firstPath);
        FileDownloadCounters.AddCompletedDownload(secondPath);

        var summary = Form1.GetStartupDownloadSummary();

        AssertContains(summary, "2 files");
        AssertContains(summary, "1.5 KB");
        AssertContains(summary, "0 MB");
    }

    internal static void AdventureOutlineFallsBackToObsidianMarkdown()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
        var requestedUrl = string.Empty;

        var updated = AdventureOutlineUtility.UpdateAdventureOutlineAsync(
            icDirectory,
            outlinePath,
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/Adventure+Outline",
            (url, _) =>
            {
                requestedUrl = url;
                return Task.FromResult(
                    """
                    # Adventure Outline

                    ## Ch 1 - Kirkilston

                    - The party seeks Nuanda.
                    """);
            }).GetAwaiter().GetResult();

        AssertTrue(updated, "fallback markdown should write adventure outline when saved IC files are unavailable");
        AssertEqual(
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/Adventure+Outline",
            requestedUrl,
            "unexpected fallback markdown URL");
        AssertContains(File.ReadAllText(outlinePath), "- The party seeks Nuanda.");
    }

    internal static void AdventureOutlineIgnoresFailedFallbackMarkdownFetch()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);

        var updated = AdventureOutlineUtility.UpdateAdventureOutlineAsync(
            icDirectory,
            outlinePath,
            AdventureOutlineUtility.FallbackMarkdownUrl,
            (_, _) => Task.FromResult($"{MarkdownUtility.UnresolvedUrlMessage}: fallback"))
            .GetAwaiter()
            .GetResult();

        AssertFalse(updated, "failed fallback markdown fetch should not update adventure outline");
        AssertFalse(File.Exists(outlinePath), "failed fallback markdown fetch should not write an outline file");
    }

    internal static void ShowMenuContainsFormerPcsItem()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
                ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
            var partyMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "partyToolStripMenuItem")
                ?? throw new InvalidOperationException("partyToolStripMenuItem was null."));
            var formerPcsMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "formerPcsToolStripMenuItem")
                ?? throw new InvalidOperationException("formerPcsToolStripMenuItem was null."));

            AssertEqual("Former PCs", formerPcsMenuItem.Text ?? string.Empty, "unexpected Former PCs menu item text");
            AssertTrue(
                showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(formerPcsMenuItem),
                "Show menu should contain the Former PCs item");
            AssertEqual(
                showMenuItem.DropDownItems.IndexOf(partyMenuItem) + 1,
                showMenuItem.DropDownItems.IndexOf(formerPcsMenuItem),
                "Former PCs should appear immediately after Party");
        });
    }

    internal static void FormerPcsViewDisplaysTokenNameAndClass()
    {
        using var directory = TemporaryDirectory.Create();
        var tokenPath = Path.Combine(directory.Path, "urvan-token.png");
        using (var token = new Bitmap(8, 8))
        {
            token.SetPixel(0, 0, Color.DarkRed);
            token.Save(tokenPath, ImageFormat.Png);
        }

        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            InvokePrivateMethod(
                form,
                "ShowFormerPcs",
                (object)new[] { new FormerPcSummary("Urvan", "Paladin of St. Ygg", tokenPath) });

            var panel = (Panel)(GetPrivateField(form, "_partyPanel")
                ?? throw new InvalidOperationException("Former PCs panel was null."));
            var controls = panel.Controls
                .Cast<Control>()
                .SelectMany(control => control.Controls.Cast<Control>().Prepend(control))
                .ToArray();

            AssertTrue(controls.OfType<Label>().Any(label => label.Text == "Urvan"), "former PC name should be displayed");
            AssertTrue(controls.OfType<Label>().Any(label => label.Text == "Paladin of St. Ygg"), "former PC class should be displayed");
            AssertTrue(controls.OfType<PictureBox>().Any(pictureBox => pictureBox.Image is not null), "former PC token image should be displayed");
        });
    }

    internal static void AdventureOutlineViewDisplaysGeneratedMarkdown()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            const string outline = """
            ---
            title: Adventure Outline
            aliases:
              - Scarlet Horizons Adventure Outline
            ---

            # Adventure Outline

            - Source files inspected:
              - `C:/repos/player-assistant/Release/Posts/IC/ch-1.html`

            ## Chapter 7 - The Gate Opens

            - Kelpie: Found the hidden key.

            -

            ## Ch 4 - Battle at Blightstone Pit

            - The party fights at the quarry.

            ## Ch 5 - A Betentacled Escape

            - The party escapes the pit.

            ## Ch 2 - Supper With Nuanda

            - Jelb weighs the party's options.
            - Dungeon Master frames the next choice.

            ## Ch 3 - Joining the Caravan to Raven's Pass

            - The party joins the caravan.
            """;

            InvokePrivateMethod(form, "ShowAdventureOutline", outline);

            var textBox = (RichTextBox)(GetPrivateField(form, "_adventureOutlineTextBox")
                ?? throw new InvalidOperationException("_adventureOutlineTextBox was null."));
            var adventureOutlineMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "adventureOutlineToolStripMenuItem")
                ?? throw new InvalidOperationException("adventureOutlineToolStripMenuItem was null."));

            AssertTrue(form.Controls.Contains(textBox), "adventure outline text box should be attached to the form");
            AssertTrue(textBox.ReadOnly, "adventure outline text box should be read-only");
            AssertContains(textBox.Text, "Chapter 7 - The Gate Opens");
            AssertContains(textBox.Text, "Kelpie: Found the hidden key.");
            AssertContains(textBox.Text, "Ch 2 - Supper With Nuanda");
            AssertContains(textBox.Text, "Jelb weighs the party's options.");
            AssertContains(textBox.Text, "Dungeon Master frames the next choice.");
            AssertTrue(
                textBox.Text.IndexOf("Chapter 7 - The Gate Opens", StringComparison.Ordinal)
                    < textBox.Text.IndexOf("Ch 4 - Battle at Blightstone Pit", StringComparison.Ordinal),
                "adventure outline display should keep chapter order from the markdown");
            AssertTrue(
                textBox.Text.IndexOf("Ch 4 - Battle at Blightstone Pit", StringComparison.Ordinal)
                    < textBox.Text.IndexOf("Ch 5 - A Betentacled Escape", StringComparison.Ordinal),
                "chapter 4 should display before chapter 5");
            AssertTrue(
                textBox.Text.IndexOf("Ch 5 - A Betentacled Escape", StringComparison.Ordinal)
                    < textBox.Text.IndexOf("Ch 2 - Supper With Nuanda", StringComparison.Ordinal),
                "chapter 5 should display before the later markdown chapter 2 entry in this fixture");
            AssertTrue(
                textBox.Text.IndexOf("Ch 2 - Supper With Nuanda", StringComparison.Ordinal)
                    < textBox.Text.IndexOf("Ch 3 - Joining the Caravan to Raven's Pass", StringComparison.Ordinal),
                "chapter 2 should display before chapter 3");
            AssertFalse(textBox.Text.Contains("title: Adventure Outline", StringComparison.Ordinal), "player-facing outline should hide YAML frontmatter");
            AssertFalse(textBox.Text.Contains("aliases:", StringComparison.Ordinal), "player-facing outline should hide YAML frontmatter keys");
            AssertFalse(textBox.Text.Contains("Scarlet Horizons Adventure Outline", StringComparison.Ordinal), "player-facing outline should hide YAML frontmatter values");
            AssertFalse(textBox.Lines.Any(line => line.Trim().Equals("-", StringComparison.Ordinal)), "player-facing outline should hide empty bullet marker lines");
            for (var lineIndex = 0; lineIndex < textBox.Lines.Length; lineIndex++)
            {
                if (textBox.Lines[lineIndex].Length == 0)
                {
                    var lineStart = textBox.GetFirstCharIndexFromLine(lineIndex);
                    if (lineStart < 0)
                    {
                        continue;
                    }

                    textBox.Select(lineStart, 0);
                    AssertFalse(textBox.SelectionBullet, "blank outline lines should not render as empty bullets");
                }
            }

            var chapterStart = textBox.Text.IndexOf("Ch 2 - Supper With Nuanda", StringComparison.Ordinal);
            textBox.Select(chapterStart, 1);
            AssertTrue(textBox.SelectionFont?.Bold == true, "chapter headings should be bold");
            AssertEqual(16f, textBox.SelectionFont?.Size ?? 0f, "chapter headings should use the enlarged adventure outline font");
            var jelbStart = textBox.Text.IndexOf("Jelb weighs the party's options.", StringComparison.Ordinal);
            textBox.Select(jelbStart, 1);
            AssertTrue(textBox.SelectionFont?.Bold == false, "summary bullet text should use regular font");
            AssertEqual(12f, textBox.SelectionFont?.Size ?? 0f, "summary bullet text should use the enlarged adventure outline font");
            var dungeonStart = textBox.Text.IndexOf("Dungeon Master frames the next choice.", StringComparison.Ordinal);
            textBox.Select(dungeonStart, 1);
            AssertTrue(textBox.SelectionFont?.Bold == false, "summary bullet text should not inherit heading bold");
            AssertEqual(12f, textBox.SelectionFont?.Size ?? 0f, "summary bullet text should keep the enlarged adventure outline font");
            AssertFalse(textBox.Text.Contains("Source files inspected", StringComparison.Ordinal), "player-facing outline should hide source file audit text");
            AssertFalse(textBox.Text.Contains("ch-1.html", StringComparison.Ordinal), "player-facing outline should hide source file paths");
            AssertFalse(adventureOutlineMenuItem.Enabled, "Adventure Outline menu item should be disabled while the outline is active");
        });
    }

    private static HostedSettingsSigningKeyTrustEntry CreateActiveHostedSettingsSigningKey(string publicKeyPem)
    {
        return new HostedSettingsSigningKeyTrustEntry("hosted-settings-test-key", publicKeyPem);
    }

    internal static void KeywordSearchBackfillsOnlineHitsIntoKeywordIndex()
    {
        WithTemporaryKeywordIndex(
            """
            {
              "index_metadata": {
                "total_words_indexed": 0
              },
              "words": {}
            }
            """,
            () =>
            {
                Form1.BackfillKeywordIndexWithOnlineResultsAsync(
                    ["Nimba Armstrong"],
                    ["https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong"],
                    CancellationToken.None).GetAwaiter().GetResult();

                using var document = JsonDocument.Parse(File.ReadAllText(GetPlayerAssistantIndexPath()));
                var words = document.RootElement.GetProperty("words");
                AssertTrue(words.TryGetProperty("Nimba Armstrong", out var nimbaEntry), "online hit should add the missing search term to keyword-index.json");
                AssertEqual(1, nimbaEntry.GetProperty("total_occurrences").GetInt32(), "backfilled keyword should record one occurrence");

                var match = nimbaEntry.GetProperty("matches").EnumerateArray().Single();
                AssertEqual(
                    "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                    match.GetProperty("url").GetString() ?? string.Empty,
                    "backfilled keyword should store the online hit URL");
                AssertEqual(1, match.GetProperty("count").GetInt32(), "backfilled match should use a count of one");
                AssertFalse(
                    string.IsNullOrWhiteSpace(match.GetProperty("last_indexed").GetString()),
                    "backfilled match should record a last-indexed timestamp");
            });
    }

    internal static void MyHeroBriefingLoadsCachedThreadPostsFromRuntimeArtifacts()
    {
        using var directory = TemporaryDirectory.Create();
        var threadDirectory = Path.Combine(directory.Path, "ch-2");
        Directory.CreateDirectory(threadDirectory);
        File.WriteAllText(
            Path.Combine(threadDirectory, "_source-show-all.html"),
            CreateRpolSourceHtml(
                (1, "Jelb", "Mon 1 Jan 2026", "01:00", "Jelb checks the door."),
                (2, "Dungeon Master", "Mon 1 Jan 2026", "01:05", "The lock clicks.")));
        var manifest = new RpolThreadSplitResult(
            "Chapter 2 - Supper With Nuanda",
            "https://rpol.net/display.cgi?gi=80170&ti=8&show=all",
            threadDirectory,
            2,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Dungeon Master"] = 1,
                ["Jelb"] = 1
            },
            []);
        File.WriteAllText(Path.Combine(threadDirectory, "manifest.json"), JsonSerializer.Serialize(manifest));

        Form1.MyHeroBriefingPostsDirectoryOverride = directory.Path;
        try
        {
            var threadPosts = (IReadOnlyList<MyHeroBriefingThreadPosts>)(InvokeStaticMethod(typeof(Form1), "LoadMyHeroBriefingThreadPosts")
                ?? throw new InvalidOperationException("thread posts were null."));

            AssertEqual(1, threadPosts.Count, "expected one cached thread to load");
            AssertEqual("Chapter 2 - Supper With Nuanda", threadPosts[0].ThreadTitle, "unexpected thread title");
            AssertEqual("https://rpol.net/display.cgi?gi=80170&ti=8&show=all", threadPosts[0].ThreadUrl, "unexpected thread URL");
            AssertEqual(2, threadPosts[0].Posts.Count, "unexpected cached post count");
            AssertEqual("Jelb", threadPosts[0].Posts[0].Author, "unexpected first cached post author");
            AssertEqual("The lock clicks.", threadPosts[0].Posts[1].BodyText, "unexpected second cached post body");
        }
        finally
        {
            Form1.MyHeroBriefingPostsDirectoryOverride = null;
        }
    }

    internal static void MyHeroBriefingLoadsFlatCachedThreadFilesFromRuntimeArtifacts()
    {
        using var directory = TemporaryDirectory.Create();
        var asideDirectory = Path.Combine(directory.Path, "Aside");
        Directory.CreateDirectory(asideDirectory);
        File.WriteAllText(
            Path.Combine(directory.Path, "ch-5.html"),
            CreateRpolSourceHtml(
                (1, "Jelb", "Mon 1 Jan 2026", "01:00", "I watch the passage."),
                (2, "Dungeon Master", "Mon 1 Jan 2026", "01:05", "Jelb hears footsteps.")));
        File.WriteAllText(
            Path.Combine(directory.Path, "ch-5.bak-20260713-191558-767.html"),
            CreateRpolSourceHtml((99, "Dungeon Master", "Mon 1 Jan 2026", "01:10", "Stale backup content.")));
        File.WriteAllText(
            Path.Combine(asideDirectory, "Aside - Searching the woods.html"),
            CreateRpolSourceHtml((3, "Kelpie", "Mon 1 Jan 2026", "01:15", "Kelpie searches the woods.")));

        Form1.MyHeroBriefingPostsDirectoryOverride = directory.Path;
        try
        {
            var threadPosts = (IReadOnlyList<MyHeroBriefingThreadPosts>)(InvokeStaticMethod(typeof(Form1), "LoadMyHeroBriefingThreadPosts")
                ?? throw new InvalidOperationException("thread posts were null."));

            AssertEqual(2, threadPosts.Count, "expected current flat chapter and aside files to load");
            AssertTrue(threadPosts.Any(thread => thread.ThreadTitle == "ch-5" && thread.Posts.Count == 2), "current chapter file should load");
            AssertTrue(threadPosts.Any(thread => thread.ThreadTitle == "Aside - Searching the woods" && thread.Posts.Count == 1), "aside file should load");
            AssertFalse(threadPosts.Any(thread => thread.Posts.Any(post => post.MessageNumber == 99)), "backup files should be ignored");
        }
        finally
        {
            Form1.MyHeroBriefingPostsDirectoryOverride = null;
        }
    }

    internal static void TaggedNoteCipherReportsEncryptedMarkdownBlockCounts()
    {
        var validBlock = TaggedNoteCipherUtility.TransformTaggedText(
            "{Level 8}The ward is real.{Level 8}",
            TaggedNoteCipherMode.Encrypt);
        var secondValidBlock = TaggedNoteCipherUtility.TransformTaggedText(
            "{(Level 6 && Spy 3)|Scyntarn 9}The sealed paragraph opens.{(Level 6 && Spy 3)|Scyntarn 9}",
            TaggedNoteCipherMode.Encrypt);
        var mismatchedBlock = secondValidBlock[..^"{(Level 6 && Spy 3)|Scyntarn 9}".Length] + "{Scyntarn 9}";
        var wrappedValidBlock = validBlock.Replace("PAN1:", $"{Environment.NewLine}  PAN1:", StringComparison.Ordinal);
        var markdown = string.Join(
            Environment.NewLine + Environment.NewLine,
            "Plain markdown before encrypted text.",
            wrappedValidBlock,
            mismatchedBlock,
            secondValidBlock);

        var report = TaggedNoteCipherUtility.EncryptedTextReportFromMarkdown(markdown);

        AssertEqual(
            "valid encrypted blocks: 2, mismatched tags: 1",
            report,
            "encrypted markdown report should count matching and mismatched tag wrappers");
    }

    internal static void TaggedNoteCipherIndexesEncryptedMarkdownFrontmatterTags()
    {
        var encryptedBlock = TaggedNoteCipherUtility.TransformTaggedText(
            "{Level 8}The ward is real.{Level 8}",
            TaggedNoteCipherMode.Encrypt);
        var secondEncryptedBlock = TaggedNoteCipherUtility.TransformTaggedText(
            "{Spy 4|Faction Scyntarn}The innkeeper knows the pass.{Spy 4|Faction Scyntarn}",
            TaggedNoteCipherMode.Encrypt);
        var markdown = string.Join(
            Environment.NewLine,
            "---",
            "tags:",
            "  - npc",
            "  - spy",
            "  - \"Scyntarn\"",
            "---",
            "Plain text.",
            encryptedBlock,
            secondEncryptedBlock);

        var entry = TaggedNoteCipherUtility.CreateEncryptedTextIndexEntry(
            "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
            markdown);

        if (entry is null)
        {
            throw new InvalidOperationException("encrypted markdown should produce an index entry");
        }

        AssertEqual(2, entry.EncryptedSections, "encrypted text index should count encrypted markdown blocks");
        AssertEqual(
            "npc,spy,Scyntarn",
            string.Join(",", entry.FrontmatterTags),
            "encrypted text index should preserve frontmatter tags");
        AssertEqual(
            TaggedNoteCipherUtility.EncryptedTextIndexFileName,
            "encrypted-text-index.json",
            "encrypted text index filename should match the proposed JSON artifact name");
    }

    internal static void ExternalUrlLaunchPolicyAcceptsHttpsAndRejectsHttp()
    {
        var http = ExternalUrlLaunchUtility.Validate(" http://rpol.net/path?q=one ");
        var https = ExternalUrlLaunchUtility.Validate("https://rpol.net/game.php?gi=80170");

        AssertFalse(http.IsAllowed, "HTTP URLs should be rejected");
        AssertTrue(https.IsAllowed, "HTTPS URLs should be allowed");
    }

    internal static void ExternalUrlLaunchPolicyRejectsUnsafeInputs()
    {
        var relative = ExternalUrlLaunchUtility.Validate("/relative/path");
        var file = ExternalUrlLaunchUtility.Validate("file:///C:/temp/report.html");
        var credentialed = ExternalUrlLaunchUtility.Validate("https://user:pass@example.test/private");
        var disallowed = ExternalUrlLaunchUtility.Validate("https://unexpected.example.test/private");

        AssertFalse(relative.IsAllowed, "relative URLs should not be opened externally");
        AssertContains(relative.RejectionReason ?? string.Empty, "absolute URL");
        AssertFalse(file.IsAllowed, "file URLs should not be opened from search results");
        AssertContains(file.RejectionReason ?? string.Empty, "HTTP and HTTPS");
        AssertFalse(credentialed.IsAllowed, "credentialed URLs should not be opened externally");
        AssertContains(credentialed.RejectionReason ?? string.Empty, "credentials");
        AssertFalse(disallowed.IsAllowed, "non-allowlisted URLs should not be opened externally");
        AssertContains(disallowed.RejectionReason ?? string.Empty, "allowlist");
    }

    internal static void HeroImagePathsFollowListingMarkdownTable()
    {
        using var directory = TemporaryDirectory.Create();
        var pcsDirectory = Path.Combine(directory.Path, "PCs");
        var activeDirectory = Path.Combine(pcsDirectory, "active");
        Directory.CreateDirectory(activeDirectory);

        var listedImagePath = Path.Combine(activeDirectory, "alice-token.webp");
        var strayImagePath = Path.Combine(activeDirectory, "stray-token.webp");
        File.WriteAllText(listedImagePath, "listed");
        File.WriteAllText(strayImagePath, "stray");

        var listingMarkdown = """
            | Name | Character | Notes | Hero |
            | --- | --- | --- | --- |
            | Alice | [[Alice]] | active | ![[alice-token.webp]] |
            | Bob | [[Bob]] | active | ![[bob-token.webp]] |
            """;

        var result = InvokeStaticMethod(
            typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.PlayerCharacterAssetUtility")
                ?? throw new InvalidOperationException("Unable to find PlayerCharacterAssetUtility type."),
            "GetListedActiveHeroImagePaths",
            listingMarkdown,
            pcsDirectory);

        var paths = ((string[])result!)
            .Select(path => Path.GetFileName(path) ?? string.Empty)
            .ToArray();

        AssertEqual(1, paths.Length, "expected only heroes listed in the markdown table to be selected");
        AssertEqual("alice-token.webp", paths[0], "unexpected hero image selected from active directory");
        AssertFalse(paths.Contains("stray-token.webp", StringComparer.OrdinalIgnoreCase), "unlisted hero image should not be selected");
    }

    internal static void HeroAssetPathsRejectEscapedTargets()
    {
        using var directory = TemporaryDirectory.Create();
        var activeDirectory = Path.Combine(directory.Path, "PCs", "active");
        var utilityType = typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.PlayerCharacterAssetUtility")
            ?? throw new InvalidOperationException("Unable to find PlayerCharacterAssetUtility type.");

        var safePath = (string)(InvokeStaticMethod(
            utilityType,
            "GetHeroAssetPath",
            activeDirectory,
            "alice-token.webp") ?? throw new InvalidOperationException("GetHeroAssetPath returned null."));

        AssertTrue(
            safePath.StartsWith(activeDirectory, StringComparison.OrdinalIgnoreCase),
            "safe hero asset path should remain under the active PCs directory");

        AssertThrows<InvalidOperationException>(() =>
            InvokeStaticMethod(utilityType, "GetHeroAssetPath", activeDirectory, "..\\escape.webp"));
        AssertThrows<InvalidOperationException>(() =>
            InvokeStaticMethod(utilityType, "GetHeroAssetPath", activeDirectory, "/escape.webp"));
    }

    internal static void LocalSettingsAreEncryptedOnLoad()
    {
        using var directory = TemporaryDirectory.Create();
        var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
        var plaintext = """
            {
              "RPOL user name": "example-user",
              "RPOL password": "example-password"
            }
            """;
        File.WriteAllText(localSettingsPath, plaintext);

        var settings = (Dictionary<string, string>)InvokeStaticMethod(
            typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.LocalSettingsUtility")
                ?? throw new InvalidOperationException("Unable to find LocalSettingsUtility type."),
            "LoadSettings",
            localSettingsPath)!;

        AssertEqual("example-user", settings["RPOL user name"], "unexpected user name after load");
        AssertEqual("example-password", settings["RPOL password"], "unexpected password after load");
        AssertFalse(File.ReadAllText(localSettingsPath).Contains("example-password", StringComparison.Ordinal), "plaintext password should not remain on disk");
        AssertTrue(
            (bool)InvokeStaticMethod(
                typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.LocalSettingsUtility")
                    ?? throw new InvalidOperationException("Unable to find LocalSettingsUtility type."),
                "IsEncryptedSettingsFile",
                localSettingsPath)!,
            "expected the local settings file to be encrypted after load");
        var encryptedJson = File.ReadAllText(localSettingsPath);
        AssertContains(encryptedJson, "\"schema_version\": 1");
        AssertContains(encryptedJson, "\"format\": \"dpapi-current-user-v2\"");
        AssertContains(encryptedJson, "\"key_scope\":");
        AssertContains(encryptedJson, "\"user_bound\": true");
    }

    internal static void PortableLocalSettingsOmitRpolCredentials()
    {
        using var directory = TemporaryDirectory.Create();
        var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
        File.WriteAllText(
            localSettingsPath,
            """
            {
              "XP Tracking": "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
              "RPOL user name": "example-user",
              "RPOL password": "example-password"
            }
            """);

        var programType = typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.Program")
            ?? throw new InvalidOperationException("Unable to find PlayerAssistant.Program type.");
        using var output = new StringWriter();
        _ = (bool)InvokeStaticMethod(
            programType,
            "TryRunLocalSettingsCommand",
            new[] { "--encrypt-local-settings", localSettingsPath },
            output)!;

        var settings = LocalSettingsUtility.LoadPortableEncryptedSettings(localSettingsPath);
        AssertTrue(settings.ContainsKey("XP Tracking"), "portable settings should retain public configuration");
        AssertFalse(settings.ContainsKey("RPOL user name"), "portable settings must omit the RPOL user name");
        AssertFalse(settings.ContainsKey("RPOL password"), "portable settings must omit the RPOL password");
    }

    internal static void CredentialManagerUtf8HelpersClearTransientBuffers()
    {
        var backend = new ObservedWindowsCredentialStoreBackend();
        using var backendScope = RuntimeSecretStoreUtility.UseBackendForTests(backend);

        WindowsCredentialManagerUtility.WriteSecretUtf8("PlayerAssistant/Test/Secret", "hunter2", "test");
        AssertTrue(backend.LastWriteInputBytes is not null, "expected test backend to observe the write buffer");
        AssertTrue(backend.LastWriteInputBytes!.All(static value => value == 0), "UTF-8 write buffer should be cleared after credential-manager write");

        AssertTrue(
            WindowsCredentialManagerUtility.TryReadSecretUtf8(
                "PlayerAssistant/Test/Secret",
                out var storedSecret,
                out _),
            "expected test secret to round-trip through credential manager helper");
        AssertEqual("hunter2", storedSecret ?? string.Empty, "unexpected credential-manager secret text");
        AssertTrue(backend.LastReadOutputBytes is not null, "expected test backend to expose the read buffer");
        AssertTrue(backend.LastReadOutputBytes!.All(static value => value == 0), "UTF-8 read buffer should be cleared after credential-manager read");
    }

    internal static void LocalSettingsEncryptCommandWritesPortableEnvelope()
    {
        using var directory = TemporaryDirectory.Create();
        var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
        File.WriteAllText(
            localSettingsPath,
            """
            {
              "XP Tracking": "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
            }
            """);

        var programType = typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.Program")
            ?? throw new InvalidOperationException("Unable to find PlayerAssistant.Program type.");
        using var output = new StringWriter();
        var handled = (bool)InvokeStaticMethod(
            programType,
            "TryRunLocalSettingsCommand",
            new[] { "--encrypt-local-settings", localSettingsPath },
            output)!;

        AssertTrue(handled, "expected encrypt-local-settings command to be handled");
        var encryptedJson = File.ReadAllText(localSettingsPath);
        AssertContains(encryptedJson, "\"format\": \"public-settings-v1\"");
        AssertFalse(
            encryptedJson.Contains("Intentional+Orphans/XP+Tracking", StringComparison.Ordinal),
            "portable encrypted settings should not keep the plaintext URL on disk");

        var settings = LocalSettingsUtility.LoadPortableEncryptedSettings(localSettingsPath);
        AssertEqual(
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            settings["XP Tracking"],
            "unexpected XP Tracking value after portable encryption");
    }

    internal static void PortableSettingsNeverCarryRpolCredentials()
    {
        var settings = new Dictionary<string, string>
        {
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            ["RPOL user name"] = "example-user",
            ["RPOL password"] = "example-password"
        };

        var portableJson = LocalSettingsUtility.CreatePortableEncryptedSettingsJson(settings);
        var loaded = LocalSettingsUtility.LoadPortableEncryptedSettingsFromContents(
            portableJson,
            "portable settings fixture");

        AssertTrue(loaded.ContainsKey("XP Tracking"), "portable settings should retain non-secret settings");
        AssertFalse(loaded.ContainsKey("RPOL user name"), "portable settings must not carry the RPOL user name");
        AssertFalse(loaded.ContainsKey("RPOL password"), "portable settings must not carry the RPOL password");
    }

    internal static void LocalSettingsDecryptCommandWritesPlaintextJson()
    {
        using var directory = TemporaryDirectory.Create();
        var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
        var outputPath = Path.Combine(directory.Path, "settings.local.plaintext.json");
        LocalSettingsUtility.SavePortableEncryptedSettings(
            localSettingsPath,
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
            });

        var programType = typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.Program")
            ?? throw new InvalidOperationException("Unable to find PlayerAssistant.Program type.");
        using var output = new StringWriter();
        var handled = (bool)InvokeStaticMethod(
            programType,
            "TryRunLocalSettingsCommand",
            new[] { "--decrypt-local-settings", localSettingsPath, outputPath },
            output)!;

        AssertTrue(handled, "expected decrypt-local-settings command to be handled");
        var plaintextJson = File.ReadAllText(outputPath);
        AssertContains(plaintextJson, "\"schema_version\": 1");
        using var document = System.Text.Json.JsonDocument.Parse(plaintextJson);
        AssertEqual(
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            document.RootElement.GetProperty("XP Tracking").GetString() ?? string.Empty,
            "unexpected XP Tracking value in decrypted plaintext json");
        AssertFalse(plaintextJson.Contains("\"payload\":", StringComparison.Ordinal), "decrypted output should not keep the encrypted payload envelope");
    }

    internal static void LocalSettingsRejectsFutureSchemaVersion()
    {
        using var directory = TemporaryDirectory.Create();
        var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
        File.WriteAllText(
            localSettingsPath,
            """
            {
              "schema_version": 99,
              "format": "dpapi-current-user-v2",
              "payload": "not-a-real-payload"
            }
            """);

        var exception = AssertThrows<InvalidOperationException>(() =>
            LocalSettingsUtility.LoadSettings(localSettingsPath));
        AssertContains(exception.Message, "unsupported schema version 99");
    }

    internal static void LegacyLocalSettingsMigrateToPortableEncryption()
    {
        using var directory = TemporaryDirectory.Create();
        var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
        var plaintext = """
            {
              "RPOL user name": "example-user",
              "RPOL password": "example-password"
            }
            """;
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(plaintextBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var legacyEnvelope = """
            {
              "format": "dpapi-current-user",
              "payload": "__PAYLOAD__"
            }
            """.Replace("__PAYLOAD__", Convert.ToBase64String(protectedBytes), StringComparison.Ordinal);

        File.WriteAllText(localSettingsPath, legacyEnvelope);

        var settings = (Dictionary<string, string>)InvokeStaticMethod(
            typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.LocalSettingsUtility")
                ?? throw new InvalidOperationException("Unable to find LocalSettingsUtility type."),
            "LoadSettings",
            localSettingsPath)!;

        AssertEqual("example-user", settings["RPOL user name"], "unexpected user name after migrating legacy encrypted settings");
        AssertEqual("example-password", settings["RPOL password"], "unexpected password after migrating legacy encrypted settings");
        AssertTrue(
            File.ReadAllText(localSettingsPath).Contains("\"format\": \"dpapi-current-user-v2\"", StringComparison.Ordinal),
            "expected legacy encrypted settings to be rewritten using the current-user DPAPI format");
    }

    internal static void V1LocalSettingsMigrateToAuthenticatedEncryption()
    {
        using var directory = TemporaryDirectory.Create();
        var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
        File.WriteAllText(localSettingsPath, CreateV1LocalSettingsEnvelope("example-user", "example-password"));

        var settings = (Dictionary<string, string>)InvokeStaticMethod(
            typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.LocalSettingsUtility")
                ?? throw new InvalidOperationException("Unable to find LocalSettingsUtility type."),
            "LoadSettings",
            localSettingsPath)!;

        AssertEqual("example-user", settings["RPOL user name"], "unexpected user name after migrating v1 encrypted settings");
        AssertEqual("example-password", settings["RPOL password"], "unexpected password after migrating v1 encrypted settings");
        AssertContains(File.ReadAllText(localSettingsPath), "\"format\": \"dpapi-current-user-v2\"");
    }

    internal static void V2LocalSettingsMigrateToScopedEncryption()
    {
        using var directory = TemporaryDirectory.Create();
        var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
        File.WriteAllText(localSettingsPath, CreateV2LocalSettingsEnvelope("example-user", "example-password"));

        var settings = (Dictionary<string, string>)InvokeStaticMethod(
            typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.LocalSettingsUtility")
                ?? throw new InvalidOperationException("Unable to find LocalSettingsUtility type."),
            "LoadSettings",
            localSettingsPath)!;

        AssertEqual("example-user", settings["RPOL user name"], "unexpected user name after migrating v2 encrypted settings");
        AssertEqual("example-password", settings["RPOL password"], "unexpected password after migrating v2 encrypted settings");
        var encryptedJson = File.ReadAllText(localSettingsPath);
        AssertContains(encryptedJson, "\"format\": \"dpapi-current-user-v2\"");
        AssertContains(encryptedJson, "\"key_scope\":");
    }

    internal static void CurrentUserLocalSettingsCopiedFixtureRoundTrips()
    {
        using var sourceDirectory = TemporaryDirectory.Create();
        using var copiedDirectory = TemporaryDirectory.Create();
        var sourcePath = Path.Combine(sourceDirectory.Path, "settings.local.json");
        var copiedPath = Path.Combine(copiedDirectory.Path, "settings.local.json");

        LocalSettingsUtility.SaveEncryptedSettings(
            sourcePath,
            new Dictionary<string, string>
            {
                ["RPOL user name"] = "example-user",
                ["RPOL password"] = "example-password"
            });

        File.Copy(sourcePath, copiedPath);

        var settings = LocalSettingsUtility.LoadSettings(copiedPath);
        AssertEqual("example-user", settings["RPOL user name"], "current-user DPAPI settings should round-trip from a copied fixture for the same user");
        AssertEqual("example-password", settings["RPOL password"], "current-user DPAPI password should round-trip from a copied fixture for the same user");
    }

    internal static void AuthenticatedLocalSettingsRejectTamperedPayload()
    {
        using var directory = TemporaryDirectory.Create();
        var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
        LocalSettingsUtility.SaveEncryptedSettings(
            localSettingsPath,
            new Dictionary<string, string>
            {
                ["RPOL user name"] = "example-user",
                ["RPOL password"] = "example-password"
            });

        using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(localSettingsPath)))
        {
            var payload = document.RootElement.GetProperty("payload").GetString() ?? string.Empty;
            var payloadBytes = Convert.FromBase64String(payload);
            payloadBytes[^1] ^= 0x7F;
            File.WriteAllText(
                localSettingsPath,
                $$"""
                {
                  "format": "dpapi-current-user-v2",
                  "payload": "{{Convert.ToBase64String(payloadBytes)}}"
                }
                """);
        }

        var exception = AssertThrows<InvalidOperationException>(() =>
            LocalSettingsUtility.LoadSettings(localSettingsPath));
        AssertContains(exception.Message, "Unable to decrypt");
    }

    internal static void LocalSettingsRestoresNewestValidBackup()
    {
        using var directory = TemporaryDirectory.Create();
        var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
        var validBackupPath = Path.Combine(directory.Path, "settings.local.bak-20260701-010203-001.json");
        var invalidBackupPath = Path.Combine(directory.Path, "settings.local.bak-20260702-010203-001.json");

        LocalSettingsUtility.SaveEncryptedSettings(
            localSettingsPath,
            new Dictionary<string, string>
            {
                ["RPOL user name"] = "restored-user",
                ["RPOL password"] = "restored-password"
            });
        File.Copy(localSettingsPath, validBackupPath);
        File.WriteAllText(invalidBackupPath, "{ not valid settings");
        File.WriteAllText(localSettingsPath, "{ corrupt active settings");
        SetLastWriteTimeUtc(validBackupPath, new DateTimeOffset(2026, 7, 1, 1, 2, 3, TimeSpan.Zero));
        SetLastWriteTimeUtc(invalidBackupPath, new DateTimeOffset(2026, 7, 2, 1, 2, 3, TimeSpan.Zero));

        WithPreservedStartupLog(() =>
        {
            var settings = LocalSettingsUtility.LoadSettings(localSettingsPath);

            AssertEqual("restored-user", settings["RPOL user name"], "unexpected restored user name");
            AssertEqual("restored-password", settings["RPOL password"], "unexpected restored password");
            AssertFalse(File.ReadAllText(localSettingsPath).Contains("corrupt active", StringComparison.Ordinal), "corrupt active settings should be replaced");

            var startupLog = File.ReadAllText(GetStartupLogPath());
            AssertContains(startupLog, "local settings backup restore");
            AssertContains(startupLog, "Restored runtime artifact");
        });
    }

    internal static void RuntimePathUtilityRejectsEscapedPaths()
    {
        using var directory = TemporaryDirectory.Create();

        var contained = RuntimePathUtility.CombineUnderBase(directory.Path, "child", "file.txt");
        AssertTrue(contained.StartsWith(directory.Path, StringComparison.OrdinalIgnoreCase), "contained path should remain under the base directory");

        AssertThrows<InvalidOperationException>(() =>
            RuntimePathUtility.CombineUnderBase(directory.Path, "..", "escape.txt"));
    }

    internal static void HealthArgumentReturnsStartupHealthSummary()
    {
        var programType = typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.Program")
            ?? throw new InvalidOperationException("Unable to find PlayerAssistant.Program type.");
        AssertTrue(
            (bool)InvokeStaticMethod(programType, "IsHealthArgument", "--health")!,
            "expected --health to be recognized");
        var health = (string)InvokeStaticMethod(programType, "GetHealthText")!;

        AssertContains(health, "player-assistant");
        AssertContains(health, "runtime:");
        AssertContains(health, "status:");
    }

    internal static void PublishVerificationRejectsStaleStartupLog()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.WriteAllText(Path.Combine(directoryPath, StartupLoggingUtility.LogFileName), "stale failure");

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when startup-errors.log is present");
            AssertContains(output.Output, "startup-errors.log");
        });
    }

    internal static void PublishVerificationRejectsStartupHealthArtifact()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.WriteAllText(Path.Combine(directoryPath, StartupHealthUtility.HealthFileName), "{}");

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when startup-health.json is present");
            AssertContains(output.Output, StartupHealthUtility.HealthFileName);
        });
    }

    internal static void PublishVerificationRejectsOutboundNetworkDiagnosticsArtifact()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.WriteAllText(Path.Combine(directoryPath, OutboundNetworkDiagnosticsUtility.DiagnosticsFileName), "{}");

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when outbound-network-diagnostics.json is present");
            AssertContains(output.Output, OutboundNetworkDiagnosticsUtility.DiagnosticsFileName);
        });
    }

    internal static void PublishVerificationAcceptsEncryptedRpolLocalSettingsSidecar()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            LocalSettingsUtility.SavePortableEncryptedSettings(
                Path.Combine(directoryPath, "settings.local.json"),
                new Dictionary<string, string>
                {
                    ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
                });
            WriteReleaseManifest(directoryPath);
            WriteReleaseProvenance(directoryPath);

            var output = RunPublishVerification(directoryPath);

            AssertEqual(
                0,
                output.ExitCode,
                $"publish verification should accept a secret-free encrypted settings.local.json. Output: {output.Output}");
        });
    }

    internal static void PublishVerificationRejectsPlaintextRpolLocalSettingsSidecar()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.WriteAllText(
                Path.Combine(directoryPath, "settings.local.json"),
                """
                {
                  "XP Tracking": "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
                  "RPOL user name": "example-user",
                  "RPOL password": "example-password"
                }
                """);
            WriteReleaseManifest(directoryPath);

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when settings.local.json contains plaintext RPOL credentials");
            AssertContains(output.Output, "plaintext RPOL credentials");
        });
    }

    internal static void PublishVerificationAcceptsMissingHostedLocalSettingsUrl()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            var settingsPath = Path.Combine(directoryPath, "settings.json");
            File.WriteAllText(
                settingsPath,
                File.ReadAllText(settingsPath).Replace(
                    """
      "Hosted Local Settings": "https://bryanmiller.us/scarlethorizons/settings.local.json",
    """,
                    string.Empty,
                    StringComparison.Ordinal));
            LocalSettingsUtility.SavePortableEncryptedSettings(
                Path.Combine(directoryPath, "settings.local.json"),
                new Dictionary<string, string>
                {
                    ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
                });
            WriteReleaseManifest(directoryPath);
            WriteReleaseProvenance(directoryPath);

            var output = RunPublishVerification(directoryPath);

            AssertEqual(
                0,
                output.ExitCode,
                $"publish verification should accept settings.json without Hosted Local Settings when a secret-free encrypted local settings file ships. Output: {output.Output}");
        });
    }

    internal static void PublishVerificationRejectsMalformedKeywordIndex()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.WriteAllText(
                Path.Combine(directoryPath, "keyword-index.json"),
                """
                {
                  "index_metadata": {}
                }
                """);

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when keyword-index.json has no words object");
            AssertContains(output.Output, "published keyword index must contain a words object");
        });
    }

    internal static void PublishVerificationRejectsMalformedSitemap()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.WriteAllText(Path.Combine(directoryPath, "sitemap.xml"), "not xml");

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when sitemap.xml is malformed");
            AssertContains(output.Output, "sitemap.xml is not valid XML");
        });
    }

    internal static void PublishVerificationRejectsIncompletePlaywrightRuntime()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.Delete(Path.Combine(directoryPath, ".playwright", "node", "win32_x64", "node.exe"));

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when Playwright node.exe is missing");
            AssertContains(output.Output, "published Playwright node.exe");
        });
    }

    internal static void PublishVerificationRejectsMalformedRuntimeInventory()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.WriteAllText(Path.Combine(directoryPath, "release-runtime-inventory.json"), "{ not valid json");
            WriteReleaseManifest(directoryPath);

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when release-runtime-inventory.json is malformed");
            AssertContains(output.Output, "release-runtime-inventory.json is not valid JSON");
        });
    }

    internal static void PublishedHealthVerificationAcceptsCurrentOutput()
    {
        var output = RunPublishedHealthVerification(GetCurrentPublishDirectory());

        AssertEqual(0, output.ExitCode, $"published health verification should pass. Output: {output.Output}");
        AssertContains(output.Output, "Published health verification passed.");
        AssertContains(output.Output, "Status:");
    }

    private static void SetRuntimeSidecarsReadOnly(string directoryPath)
    {
        foreach (var fileName in new[] { XpPasswordStoreUtility.FileName })
        {
            var path = Path.Combine(directoryPath, fileName);
            if (File.Exists(path))
            {
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            }
        }
    }

    private static void WithTemporaryDiagnosticsRuntime(Action<string, string, string, string> action)
    {
        var rootPath = Path.Combine(
            GetRepositoryRoot(),
            "codex-scratch",
            $"diagnostics-test-{Guid.NewGuid():N}");
        var releasePath = Path.Combine(rootPath, "Release");
        var publishPath = Path.Combine(releasePath, "publish");
        var outputPath = Path.Combine(rootPath, "out");

        try
        {
            WriteDiagnosticsRuntime(releasePath, includeSensitiveLog: true);
            WriteDiagnosticsRuntime(publishPath, includeSensitiveLog: false);
            action(rootPath, releasePath, publishPath, outputPath);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private static void WriteDiagnosticsRuntime(string directoryPath, bool includeSensitiveLog)
    {
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(Path.Combine(directoryPath, "player-assistant.exe"), "fake executable");
        WriteSettingsJson(directoryPath, CreateValidAppSettings(includeCredentials: true));
        File.WriteAllText(
            Path.Combine(directoryPath, "startup-health.json"),
            """
            {
              "schema_version": 1,
              "phases": []
            }
            """);
        File.WriteAllText(
            Path.Combine(directoryPath, OutboundNetworkDiagnosticsUtility.DiagnosticsFileName),
            """
            {
              "schema_version": 1,
              "started_at": "2026-07-05T00:00:00.0000000+00:00",
              "updated_at": "2026-07-05T00:00:01.0000000+00:00",
              "endpoints": [
                {
                  "method": "GET",
                  "purpose": "PlayerAssistantHostedSettings",
                  "scheme": "https",
                  "host": "bryanmiller.us",
                  "path": "/scarlethorizons/settings.local.json",
                  "query_present": true,
                  "total_requests": 2,
                  "success_count": 1,
                  "failure_count": 1,
                  "retry_count": 1,
                  "last_outcome": "failure",
                  "last_status_code": null,
                  "last_failure_kind": "Unavailable",
                  "last_failure_summary": "The network request failed: https://user:pass@example.test/path?password=secret-password&token=secret-token Authorization: Bearer abc123 Cookie: sessionid=abc123",
                  "first_observed_at": "2026-07-05T00:00:00.0000000+00:00",
                  "last_observed_at": "2026-07-05T00:00:01.0000000+00:00"
                }
              ]
            }
            """);
        File.WriteAllText(
            Path.Combine(directoryPath, "last-crash.json"),
            """
            {
              "schema_version": 1,
              "phase": "synthetic crash",
              "exception": {
                "type": "InvalidOperationException",
                "message": "synthetic crash"
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(directoryPath, "startup-remediation.txt"),
            """
            Player Assistant startup configuration guidance

            1. Warning: synthetic warning
               Repair: synthetic repair
            """);
        File.WriteAllText(
            Path.Combine(directoryPath, "startup-errors.log"),
            includeSensitiveLog
                ? """
                  RPOL password: hunter2
                  RPOL user name: example-user
                  Authorization: Bearer abc123
                  Cookie: sessionid=abc123
                  url=https://user:pass@example.test/path?password=secret-password&token=secret-token
                  """
                : "no startup errors");
        File.WriteAllText(
            Path.Combine(directoryPath, "keyword-index.json"),
            """
            {
              "words": {
                "scarlet": []
              },
              "index_metadata": {}
            }
            """);
        File.WriteAllText(Path.Combine(directoryPath, KeywordTermsFileUtility.FileName), "scarlet");
        File.WriteAllText(Path.Combine(directoryPath, "sitemap.xml"), "<urlset />");
        File.WriteAllText(Path.Combine(directoryPath, "sitemap-keyword-urls.json"), "{}");
        File.WriteAllText(Path.Combine(directoryPath, "game-forum-chapter-prefixes.txt"), "chapter");
        File.WriteAllText(Path.Combine(directoryPath, "game-forum-chapter-downloads.txt"), "chapter");
        File.WriteAllText(Path.Combine(directoryPath, "game-forum-aside-downloads.txt"), "aside");
        File.WriteAllText(Path.Combine(directoryPath, "game-forum-ooc-downloads.txt"), "ooc");
    }

    private static void WithTemporaryKeywordIndex(string json, Action action)
    {
        var indexPath = GetPlayerAssistantIndexPath();
        var backupPath = indexPath + ".test-backup";
        var hadOriginalIndex = File.Exists(indexPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            if (hadOriginalIndex)
            {
                File.Copy(indexPath, backupPath, overwrite: true);
            }

            File.WriteAllText(indexPath, json);
            action();
        }
        finally
        {
            if (File.Exists(indexPath))
            {
                File.Delete(indexPath);
            }

            if (hadOriginalIndex)
            {
                if (!File.Exists(backupPath))
                {
                    throw new FileNotFoundException($"Expected backup file '{backupPath}' to exist for restore.", backupPath);
                }

                File.Move(backupPath, indexPath, overwrite: true);
            }
        }
    }

    private static string GetStartupLogPath()
    {
        return RuntimePathUtility.GetWritableRuntimePath(StartupLoggingUtility.LogFileName);
    }

    private static string GetStartupHealthPath()
    {
        return RuntimePathUtility.GetWritableRuntimePath(StartupHealthUtility.HealthFileName);
    }

    private static void WithPreservedStartupLog(Action action)
    {
        var startupLogPath = GetStartupLogPath();
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

        try
        {
            if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }

            action();
        }
        finally
        {
            if (hadStartupLog)
            {
                File.WriteAllText(startupLogPath, originalStartupLog);
            }
            else if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }
        }
    }

    private static void WithHostedSettingsIsolation(Action action)
    {
        WithPreservedFileAbsent(
            RuntimePathUtility.GetApplicationPath("settings.local.json"),
            () => WithPreservedFileAbsent(
                RuntimePathUtility.GetUserDataPath("settings.local.json"),
                () => WithPreservedFileAbsent(
                    RuntimePathUtility.GetUserDataPath("trusted-hosted-settings-state.json"),
                    action)));
    }

    private static void WithPreservedStartupHealth(Action action)
    {
        var startupHealthPath = GetStartupHealthPath();
        var hadStartupHealth = File.Exists(startupHealthPath);
        var originalStartupHealth = hadStartupHealth ? File.ReadAllText(startupHealthPath) : null;

        try
        {
            if (File.Exists(startupHealthPath))
            {
                File.Delete(startupHealthPath);
            }

            action();
        }
        finally
        {
            if (hadStartupHealth)
            {
                File.WriteAllText(startupHealthPath, originalStartupHealth);
            }
            else if (File.Exists(startupHealthPath))
            {
                File.Delete(startupHealthPath);
            }
        }
    }

    private static System.Text.Json.JsonDocument LoadStartupHealthDocument()
    {
        return System.Text.Json.JsonDocument.Parse(File.ReadAllText(GetStartupHealthPath()));
    }

    private static System.Text.Json.JsonElement FindStartupHealthPhase(
        System.Text.Json.JsonDocument document,
        string phaseName)
    {
        foreach (var phase in document.RootElement.GetProperty("phases").EnumerateArray())
        {
            if (string.Equals(phase.GetProperty("phase").GetString(), phaseName, StringComparison.Ordinal))
            {
                return phase;
            }
        }

        throw new InvalidOperationException($"Startup health phase '{phaseName}' was not found.");
    }

    private static Dictionary<string, string> CreateValidAppSettings(bool includeCredentials)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RPOL Site"] = "https://rpol.net/game.php?gi=80170",
            ["Game Intro"] = "https://rpol.net/gameinfo.php?gi=80170",
            ["The Cast"] = "https://rpol.net/gameinfo.php?action=cast&gi=80170",
            ["Obsidian Game Vault"] = "https://publish.obsidian.md/scarlethorizons",
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
        };

        if (includeCredentials)
        {
            settings["RPOL user name"] = "example-user";
            settings["RPOL password"] = "example-password";
        }

        return settings;
    }

    private static void WriteRequiredRuntimeSidecars(string directoryPath)
    {
        File.Copy(
            Path.Combine(GetRepositoryRoot(), "pwa", "magic-items.json"),
            Path.Combine(directoryPath, "magic-items.json"),
            overwrite: true);
        File.WriteAllText(
            Path.Combine(directoryPath, "keyword-index.json"),
            """
            {
              "words": {
                "scarlet": []
              },
              "index_metadata": {}
            }
            """);
        File.WriteAllText(Path.Combine(directoryPath, KeywordTermsFileUtility.FileName), "scarlet");
        File.WriteAllText(Path.Combine(directoryPath, "sitemap.xml"), "<urlset />");
        File.WriteAllText(Path.Combine(directoryPath, "sitemap-keyword-urls.json"), "{}");
        XpPasswordStoreUtility.SavePasswordHashes(
            Path.Combine(directoryPath, XpPasswordStoreUtility.FileName),
            new Dictionary<string, string>
            {
                ["Kelpie"] = "gemstone"
            });
    }

    private static void WriteManifestedRuntime(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
        WriteRequiredRuntimeSidecars(directoryPath);
        File.WriteAllText(Path.Combine(directoryPath, "player-assistant.exe"), "synthetic executable");
        File.WriteAllText(Path.Combine(directoryPath, "settings.json"), "{}");
        LocalSettingsUtility.SavePortableEncryptedSettings(
            Path.Combine(directoryPath, "settings.local.json"),
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
                ["RPOL user name"] = "example-user",
                ["RPOL password"] = "example-password"
            });

        Directory.CreateDirectory(Path.Combine(directoryPath, ".playwright", "node", "win32_x64"));
        Directory.CreateDirectory(Path.Combine(directoryPath, ".playwright", "package"));
        File.WriteAllText(Path.Combine(directoryPath, ".playwright", "node", "win32_x64", "node.exe"), "synthetic node");
        File.WriteAllText(Path.Combine(directoryPath, ".playwright", "package", "package.json"), "{}");
        File.WriteAllText(Path.Combine(directoryPath, ".playwright", "package", "browsers.json"), "{}");
        WriteReleaseRuntimeInventory(directoryPath);
        WriteReleaseManifest(directoryPath);
        WriteReleaseProvenance(directoryPath);
    }

    private static void WriteReleaseRuntimeInventory(string directoryPath)
    {
        var project = XDocument.Load(Path.Combine(GetRepositoryRoot(), "player-assistant.csproj"));
        var packages = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => new
            {
                name = element.Attribute("Include")?.Value ?? string.Empty,
                version = element.Attribute("Version")?.Value ?? string.Empty
            })
            .Where(package => !string.IsNullOrWhiteSpace(package.name))
            .OrderBy(package => package.name, StringComparer.Ordinal)
            .ToArray();

        string GetProjectProperty(string name)
        {
            return project
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == name)
                ?.Value
                ?? string.Empty;
        }

        var inventory = new
        {
            schema_version = 1,
            generated_at = DateTimeOffset.UtcNow.ToString("O"),
            app = new
            {
                version = GetCanonicalVersion(),
                file_version = GetCanonicalVersion("PlayerAssistantAssemblyVersion"),
                product_version = GetCanonicalVersion()
            },
            runtime = new
            {
                target_framework = GetProjectProperty("TargetFramework"),
                runtime_identifier = GetProjectProperty("RuntimeIdentifier"),
                self_contained = GetProjectProperty("SelfContained"),
                publish_single_file = "false",
                publish_runtime_identifier = "win-x64",
                publish_self_contained = "false"
            },
            packages,
            scripts = new[]
            {
                new { relative_path = "publish-player-assistant.ps1", length = 1L, sha256 = new string('A', 64) }
            },
            hash_algorithm = "SHA256"
        };

        File.WriteAllText(
            Path.Combine(directoryPath, "release-runtime-inventory.json"),
            System.Text.Json.JsonSerializer.Serialize(
                inventory,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static string CreateSitemapXml(params string[] urls)
    {
        var entries = string.Join(
            Environment.NewLine,
            urls.Select(url => $"  <url><loc>{System.Security.SecurityElement.Escape(url)}</loc></url>"));
        return $"<urlset>{Environment.NewLine}{entries}{Environment.NewLine}</urlset>";
    }

    private static string CreateV1LocalSettingsEnvelope(string userName, string password)
    {
        var plaintext = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, string>
            {
                ["RPOL user name"] = userName,
                ["RPOL password"] = password
            },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var key = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("PlayerAssistant.LocalSettings.v1"));
        var iv = RandomNumberGenerator.GetBytes(16);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        var payloadBytes = new byte[iv.Length + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, payloadBytes, 0, iv.Length);
        Buffer.BlockCopy(ciphertext, 0, payloadBytes, iv.Length, ciphertext.Length);

        return $$"""
            {
              "format": "app-protected-v1",
              "payload": "{{Convert.ToBase64String(payloadBytes)}}"
            }
            """;
    }

    private static string CreateV2LocalSettingsEnvelope(string userName, string password)
    {
        var plaintext = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, string>
            {
                ["RPOL user name"] = userName,
                ["RPOL password"] = password
            },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var key = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("PlayerAssistant.LocalSettings.v1"));
        var authenticationKey = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("PlayerAssistant.LocalSettings.v1.hmac"));
        var iv = RandomNumberGenerator.GetBytes(16);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        var protectedContent = new byte[iv.Length + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, protectedContent, 0, iv.Length);
        Buffer.BlockCopy(ciphertext, 0, protectedContent, iv.Length, ciphertext.Length);

        byte[] tag;
        using (var hmac = new HMACSHA256(authenticationKey))
        {
            tag = hmac.ComputeHash(protectedContent);
        }

        var payloadBytes = new byte[protectedContent.Length + tag.Length];
        Buffer.BlockCopy(protectedContent, 0, payloadBytes, 0, protectedContent.Length);
        Buffer.BlockCopy(tag, 0, payloadBytes, protectedContent.Length, tag.Length);

        return $$"""
            {
              "format": "app-protected-v2",
              "payload": "{{Convert.ToBase64String(payloadBytes)}}"
            }
            """;
    }

    private static void WriteSettingsJsonWithHostedLocalSettings(string directoryPath, string hostedLocalSettingsUrl)
    {
        File.WriteAllText(
            Path.Combine(directoryPath, "settings.json"),
            $$"""
            {
              "schema_version": 1,
              "Hosted Local Settings": "{{hostedLocalSettingsUrl}}",
              "RPOL Site": "https://rpol.net/game.php?gi=80170",
              "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
              "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
              "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons"
            }
            """);
    }

    private static void AssertHostedSettingsFailure(
        string hostedLocalSettingsUrl,
        string expectedLogFragment,
        IReadOnlyList<HostedSettingsSigningKeyTrustEntry>? trustedSigningKeys = null,
        int? expectedRequestCount = null,
        string? expectedRequestPath = null)
    {
        using var directory = TemporaryDirectory.Create();
        WriteSettingsJsonWithHostedLocalSettings(directory.Path, hostedLocalSettingsUrl);

        using var allowlistScope = NetworkUrlAllowlistUtility.UseValidationOverrideForTests((uri, purpose) =>
        {
            if (purpose == NetworkUrlPurpose.PlayerAssistantHostedSettings
                && string.Equals(uri.AbsoluteUri, hostedLocalSettingsUrl, StringComparison.Ordinal))
            {
                return NetworkUrlAllowlistValidation.Allowed(uri);
            }

            return null;
        });
        using var trustedHostedSettingsKeysScope = trustedSigningKeys is null
            ? null
            : HostedSettingsTrustUtility.UseTrustedSigningKeysForTests(trustedSigningKeys);

        WithHostedSettingsIsolation(() =>
            WithPreservedStartupLog(() =>
            {
                var exception = AssertThrows<InvalidOperationException>(() =>
                    AppSettingsUtility.LoadSettings(directory.Path));

                AssertContains(exception.Message, "XP Tracking");
                var startupLog = File.ReadAllText(GetStartupLogPath());
                AssertContains(startupLog, "hosted local settings load");
                AssertContains(startupLog, hostedLocalSettingsUrl);
                AssertContains(startupLog, expectedLogFragment);
                AssertFalse(
                    File.Exists(Path.Combine(directory.Path, "settings.local.json")),
                    "failed hosted local settings should not be persisted into the runtime directory");
            }));

        if (expectedRequestCount is not null || expectedRequestPath is not null)
        {
            var requestObservation = LoopbackHttpServer.GetObservation(hostedLocalSettingsUrl);
            AssertTrue(requestObservation is not null, "expected fixture server observation for hosted settings request");

            if (expectedRequestCount is not null)
            {
                AssertEqual(expectedRequestCount.Value, requestObservation!.RequestCount, "unexpected hosted settings fixture request count");
            }

            if (expectedRequestPath is not null)
            {
                AssertEqual(expectedRequestPath, requestObservation!.LastRequestPath, "unexpected hosted settings fixture request path");
            }
        }
    }

    private static string CorruptHostedSettingsSignature(string hostedSettingsJson)
    {
        using var document = System.Text.Json.JsonDocument.Parse(hostedSettingsJson);
        var root = document.RootElement;
        var signature = root.GetProperty("signature").GetString()
            ?? throw new InvalidOperationException("Hosted settings signature was missing.");
        var signatureBytes = Convert.FromBase64String(signature);
        signatureBytes[^1] ^= 0x01;
        var tamperedSignature = Convert.ToBase64String(signatureBytes);

        return $$"""
            {
              "schema_version": {{root.GetProperty("schema_version").GetInt32()}},
              "format": "{{root.GetProperty("format").GetString()}}",
              "content_id": "{{root.GetProperty("content_id").GetString()}}",
              "version": "{{root.GetProperty("version").GetString()}}",
              "encrypted_settings": {{System.Text.Json.JsonSerializer.Serialize(root.GetProperty("encrypted_settings").GetString())}},
              "signature": "{{tamperedSignature}}"
            }
            """;
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

}
