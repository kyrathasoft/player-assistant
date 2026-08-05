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

internal static class SettingsReleaseTests
{
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
        AssertContains(encryptedJson, "\"format\": \"app-protected-v3\"");
        AssertContains(encryptedJson, "\"key_scope\":");
        AssertContains(encryptedJson, "\"install_path_bound\": true");
    }

    internal static void PortableEncryptedSettingsByteLoaderClearsSourceBuffer()
    {
        var settings = new Dictionary<string, string>
        {
            ["RPOL user name"] = "example-user",
            ["RPOL password"] = "example-password"
        };
        var encryptedJson = LocalSettingsUtility.CreatePortableEncryptedSettingsJson(settings);
        var encryptedUtf8 = System.Text.Encoding.UTF8.GetBytes(encryptedJson);

        var loadedSettings = LocalSettingsUtility.LoadPortableEncryptedSettingsFromUtf8Bytes(
            encryptedUtf8,
            "test settings");

        AssertEqual("example-user", loadedSettings["RPOL user name"], "unexpected user name after byte-buffer load");
        AssertEqual("example-password", loadedSettings["RPOL password"], "unexpected password after byte-buffer load");
        AssertTrue(encryptedUtf8.All(static value => value == 0), "portable encrypted settings buffer should be cleared after load");
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
        AssertContains(encryptedJson, "\"format\": \"app-protected-v2\"");
        AssertFalse(
            encryptedJson.Contains("Intentional+Orphans/XP+Tracking", StringComparison.Ordinal),
            "portable encrypted settings should not keep the plaintext URL on disk");

        var settings = LocalSettingsUtility.LoadPortableEncryptedSettings(localSettingsPath);
        AssertEqual(
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            settings["XP Tracking"],
            "unexpected XP Tracking value after portable encryption");
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
              "format": "app-protected-v3",
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
            File.ReadAllText(localSettingsPath).Contains("\"format\": \"app-protected-v3\"", StringComparison.Ordinal),
            "expected legacy encrypted settings to be rewritten using the scoped app-protected-v3 format");
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
        AssertContains(File.ReadAllText(localSettingsPath), "\"format\": \"app-protected-v3\"");
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
        AssertContains(encryptedJson, "\"format\": \"app-protected-v3\"");
        AssertContains(encryptedJson, "\"key_scope\":");
    }

    internal static void ScopedLocalSettingsRejectCopiedInstallPath()
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

        var exception = AssertThrows<InvalidOperationException>(() =>
            LocalSettingsUtility.LoadSettings(copiedPath));
        AssertContains(exception.Message, "different Windows user, machine, or install directory");
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
                  "format": "app-protected-v3",
                  "payload": "{{Convert.ToBase64String(payloadBytes)}}"
                }
                """);
        }

        var exception = AssertThrows<InvalidOperationException>(() =>
            LocalSettingsUtility.LoadSettings(localSettingsPath));
        AssertContains(exception.Message, "authenticate or decrypt");
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

    internal static void PublishVerificationAcceptsCurrentOutput()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            var output = RunPublishVerification(directoryPath);

            AssertEqual(0, output.ExitCode, $"publish verification should pass. Output: {output.Output}");
            AssertContains(output.Output, "Publish verification passed:");
        });
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

    internal static void PublishVerificationRejectsLastCrashArtifact()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.WriteAllText(Path.Combine(directoryPath, LastCrashDiagnosticUtility.FileName), "{}");

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when last-crash.json is present");
            AssertContains(output.Output, LastCrashDiagnosticUtility.FileName);
        });
    }

    internal static void PublishVerificationRejectsMalformedSettingsJson()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.WriteAllText(Path.Combine(directoryPath, "settings.json"), "{ not valid json");

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when settings.json is malformed");
            AssertContains(output.Output, "published settings.json is not valid JSON");
        });
    }

    internal static void PublishVerificationRejectsFutureSettingsSchema()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.WriteAllText(
                Path.Combine(directoryPath, "settings.json"),
                """
                {
                  "schema_version": 99,
                  "RPOL Site": "https://rpol.net/game.php?gi=80170",
                  "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
                  "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
                  "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons"
                }
                """);

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when settings.json uses a future schema");
            AssertContains(output.Output, "published settings.json uses unsupported schema version 99");
        });
    }

    internal static void PublishVerificationAcceptsEncryptedRpolLocalSettingsSidecar()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            LocalSettingsUtility.SaveEncryptedSettings(
                Path.Combine(directoryPath, "settings.local.json"),
                new Dictionary<string, string>
                {
                    ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
                    ["RPOL user name"] = "example-user",
                    ["RPOL password"] = "example-password"
                });
            WriteReleaseManifest(directoryPath);
            WriteReleaseProvenance(directoryPath);

            var output = RunPublishVerification(directoryPath);

            AssertEqual(
                0,
                output.ExitCode,
                $"publish verification should accept encrypted settings.local.json with RPOL credentials. Output: {output.Output}");
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
            LocalSettingsUtility.SaveEncryptedSettings(
                Path.Combine(directoryPath, "settings.local.json"),
                new Dictionary<string, string>
                {
                    ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
                    ["RPOL user name"] = "example-user",
                    ["RPOL password"] = "example-password"
                });
            WriteReleaseManifest(directoryPath);
            WriteReleaseProvenance(directoryPath);

            var output = RunPublishVerification(directoryPath);

            AssertEqual(
                0,
                output.ExitCode,
                $"publish verification should accept settings.json without Hosted Local Settings when encrypted local settings ships. Output: {output.Output}");
        });
    }

    internal static void PublishVerificationRejectsMissingXpPasswordSidecar()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.Delete(Path.Combine(directoryPath, XpPasswordStoreUtility.FileName));

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when xp-passwords.json is missing");
            AssertContains(output.Output, XpPasswordStoreUtility.FileName);
        });
    }

    internal static void PublishVerificationRejectsPlaintextXpPasswordSidecar()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.WriteAllText(
                Path.Combine(directoryPath, XpPasswordStoreUtility.FileName),
                """
                {
                  "Kelpie": "gemstone"
                }
                """);

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when xp-passwords.json is plaintext");
            AssertContains(output.Output, XpPasswordStoreUtility.Format);
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

    internal static void PublishVerificationRejectsMismatchedExecutableVersion()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.Copy(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                Path.Combine(directoryPath, "player-assistant.exe"),
                overwrite: true);

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when player-assistant.exe has the wrong version metadata");
            AssertContains(output.Output, "Published executable FileVersion");
        });
    }

    internal static void PublishVerificationRejectsStaleReleaseManifest()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            var manifestPath = Path.Combine(directoryPath, "release-manifest.json");
            var manifest = File.ReadAllText(manifestPath);
            var marker = "\"sha256\":";
            var markerIndex = manifest.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                throw new InvalidOperationException("release-manifest.json did not contain a sha256 entry.");
            }

            var valueStart = manifest.IndexOf('"', markerIndex + marker.Length);
            var valueEnd = manifest.IndexOf('"', valueStart + 1);
            var tamperedManifest = manifest[..(valueStart + 1)]
                + new string('0', valueEnd - valueStart - 1)
                + manifest[valueEnd..];
            File.WriteAllText(manifestPath, tamperedManifest);

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when release-manifest.json is stale");
            AssertContains(output.Output, "release-manifest.json SHA256 mismatch");
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

    internal static void PublishVerificationRejectsMalformedReleaseProvenance()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.WriteAllText(Path.Combine(directoryPath, "release-provenance.json"), "{ not valid json");

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should fail when release-provenance.json is malformed");
            AssertContains(output.Output, "release-provenance.json is not valid JSON");
        });
    }

    internal static void PublishVerificationRejectsUnsignedExecutableWhenSigningRequired()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            var output = RunPublishVerification(directoryPath, "-RequireCodeSigning");

            AssertFalse(output.ExitCode == 0, "publish verification should fail when code signing is required for an unsigned executable");
            AssertContains(output.Output, "Published executable Authenticode signature status");
        });
    }

    internal static void InstallerScriptsTargetProgramFilesInstallPath()
    {
        var installerPath = Path.Combine(GetRepositoryRoot(), "Installer", "install-player-assistant.ps1");
        var launcherPath = Path.Combine(GetRepositoryRoot(), "Installer", "install-player-assistant.cmd");
        var innoScriptPath = Path.Combine(GetRepositoryRoot(), "Installer", "player-assistant.iss");
        var builderPath = Path.Combine(GetRepositoryRoot(), "build-installer.ps1");
        var verifierPath = Path.Combine(GetRepositoryRoot(), "verify-installer-package.ps1");
        var versionMetadataPath = Path.Combine(GetRepositoryRoot(), "version.props");
        var versionHelperPath = Path.Combine(GetRepositoryRoot(), "version-metadata.ps1");

        AssertTrue(File.Exists(installerPath), "installer script should exist");
        AssertTrue(File.Exists(launcherPath), "installer launcher should exist");
        AssertTrue(File.Exists(innoScriptPath), "Inno Setup script should exist");
        AssertTrue(File.Exists(builderPath), "installer package builder should exist");
        AssertTrue(File.Exists(verifierPath), "installer package verifier should exist");
        AssertTrue(File.Exists(versionMetadataPath), "canonical version metadata should exist");
        AssertTrue(File.Exists(versionHelperPath), "PowerShell version helper should exist");

        var installer = File.ReadAllText(installerPath);
        AssertContains(installer, "kyrathasoft\\player-assistant");
        AssertContains(installer, "CommonPrograms");
        AssertContains(installer, "Uninstall\\KyrathaSoft Player Assistant");
        var innoScript = File.ReadAllText(innoScriptPath);
        AssertContains(innoScript, "DefaultDirName={autopf}\\kyrathasoft\\player-assistant");
        AssertContains(innoScript, "ArchitecturesInstallIn64BitMode=x64compatible");
        AssertContains(innoScript, "#define InstallerVersion Version");
        AssertContains(innoScript, "OutputBaseFilename=p-assist-{#InstallerVersion}");
        AssertContains(innoScript, ".NET Desktop Runtime 10 x64");
        AssertContains(innoScript, "https://dotnet.microsoft.com/en-us/download/dotnet/10.0");
        AssertContains(innoScript, "IsRequiredRuntimeInstalled");
        AssertContains(innoScript, "Microsoft.WindowsDesktop.App");
        AssertContains(File.ReadAllText(builderPath), "ISCC.exe");
        AssertContains(File.ReadAllText(builderPath), "Get-InstallerVersion");
        AssertContains(File.ReadAllText(builderPath), "p-assist-$InstallerVersion.exe");
        AssertContains(File.ReadAllText(builderPath), "Get-PlayerAssistantVersionMetadata");
        AssertContains(File.ReadAllText(versionHelperPath), "PlayerAssistantVersion");
        AssertContains(File.ReadAllText(builderPath), "app-protected-v2");
        AssertContains(File.ReadAllText(verifierPath), "app-protected-v2");
    }

    internal static void InstallerPackageVerificationAcceptsCurrentPackage()
    {
        var packagePath = Path.Combine(
            GetRepositoryRoot(),
            "Release",
            "installer",
            "player-assistant-0.9.1-hardening.1-installer.zip");
        if (!File.Exists(packagePath))
        {
            return;
        }

        var output = RunPowerShell(
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Path.Combine(GetRepositoryRoot(), "verify-installer-package.ps1"),
                "-PackagePath",
                packagePath
            ],
            TimeSpan.FromSeconds(60));

        AssertEqual(0, output.ExitCode, $"installer package verification should pass. Output: {output.Output}");
        AssertContains(output.Output, "Installer package verification passed:");
    }

    internal static void InstallerPackageVerificationRejectsUnsignedPayloadWhenSigningRequired()
    {
        var packagePath = Path.Combine(
            GetRepositoryRoot(),
            "Release",
            "installer",
            "player-assistant-0.9.1-hardening.1-installer.zip");
        if (!File.Exists(packagePath))
        {
            return;
        }

        var output = RunPowerShell(
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Path.Combine(GetRepositoryRoot(), "verify-installer-package.ps1"),
                "-PackagePath",
                packagePath,
                "-RequireCodeSigning"
            ],
            TimeSpan.FromSeconds(60));

        AssertFalse(output.ExitCode == 0, "installer package verification should fail when code signing is required for an unsigned payload");
        AssertContains(output.Output, "Payload executable Authenticode signature status");
    }

    internal static void ReleaseUpdateArtifactVerificationAcceptsGeneratedSignedManifest()
    {
        WithCopiedPublishDirectory(publishDirectory =>
        {
            using var outputDirectory = TemporaryDirectory.Create();
            var installerVersion = GetCanonicalVersion().Split('-', '+')[0];
            var installerPath = Path.Combine(outputDirectory.Path, $"p-assist-{installerVersion}.exe");
            File.Copy(Path.Combine(publishDirectory, "player-assistant.exe"), installerPath, overwrite: true);

            var buildOutput = RunPowerShell(
                [
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    Path.Combine(GetRepositoryRoot(), "build-release-update-artifacts.ps1"),
                    "-OutputDir",
                    outputDirectory.Path,
                    "-PublishDir",
                    publishDirectory,
                    "-InstallerPath",
                    installerPath,
                    "-GenerateEphemeralSigningKey"
                ],
                TimeSpan.FromSeconds(60));

            AssertEqual(0, buildOutput.ExitCode, $"release update artifact build should pass. Output: {buildOutput.Output}");

            var verifyOutput = RunPowerShell(
                [
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    Path.Combine(GetRepositoryRoot(), "verify-release-update-artifacts.ps1"),
                    "-PublishArchivePath",
                    Path.Combine(outputDirectory.Path, $"p-assist-{installerVersion}.zip"),
                    "-InstallerPath",
                    installerPath,
                    "-ManifestPath",
                    Path.Combine(outputDirectory.Path, "p-assist-updates.json"),
                    "-SignaturePath",
                    Path.Combine(outputDirectory.Path, "p-assist-updates.json.sig"),
                    "-PublicKeyXmlPath",
                    Path.Combine(outputDirectory.Path, "p-assist-updates.public-key.xml")
                ],
                TimeSpan.FromSeconds(60));

            AssertEqual(0, verifyOutput.ExitCode, $"release update artifact verification should pass. Output: {verifyOutput.Output}");
            AssertContains(verifyOutput.Output, "Release update artifacts verification passed:");
        });
    }

    internal static void ReleaseUpdateArtifactVerificationRejectsManifestHashMismatch()
    {
        WithCopiedPublishDirectory(publishDirectory =>
        {
            using var outputDirectory = TemporaryDirectory.Create();
            var installerVersion = GetCanonicalVersion().Split('-', '+')[0];
            var installerPath = Path.Combine(outputDirectory.Path, $"p-assist-{installerVersion}.exe");
            File.Copy(Path.Combine(publishDirectory, "player-assistant.exe"), installerPath, overwrite: true);

            var buildOutput = RunPowerShell(
                [
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    Path.Combine(GetRepositoryRoot(), "build-release-update-artifacts.ps1"),
                    "-OutputDir",
                    outputDirectory.Path,
                    "-PublishDir",
                    publishDirectory,
                    "-InstallerPath",
                    installerPath,
                    "-GenerateEphemeralSigningKey"
                ],
                TimeSpan.FromSeconds(60));

            AssertEqual(0, buildOutput.ExitCode, $"release update artifact build should pass. Output: {buildOutput.Output}");

            var manifestPath = Path.Combine(outputDirectory.Path, "p-assist-updates.json");
            var manifest = File.ReadAllText(manifestPath);
            var marker = @"""installer_sha256"": ";
            var markerIndex = manifest.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                throw new InvalidOperationException("p-assist-updates.json did not contain installer_sha256.");
            }

            var valueStart = manifest.IndexOf('"', markerIndex + marker.Length);
            var valueEnd = manifest.IndexOf('"', valueStart + 1);
            var tamperedManifest = manifest[..(valueStart + 1)]
                + new string('0', valueEnd - valueStart - 1)
                + manifest[valueEnd..];
            File.WriteAllText(manifestPath, tamperedManifest);

            var verifyOutput = RunPowerShell(
                [
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    Path.Combine(GetRepositoryRoot(), "verify-release-update-artifacts.ps1"),
                    "-PublishArchivePath",
                    Path.Combine(outputDirectory.Path, $"p-assist-{installerVersion}.zip"),
                    "-InstallerPath",
                    installerPath,
                    "-ManifestPath",
                    manifestPath,
                    "-SignaturePath",
                    Path.Combine(outputDirectory.Path, "p-assist-updates.json.sig"),
                    "-PublicKeyXmlPath",
                    Path.Combine(outputDirectory.Path, "p-assist-updates.public-key.xml")
                ],
                TimeSpan.FromSeconds(60));

            AssertFalse(verifyOutput.ExitCode == 0, "release update artifact verification should fail when the signed manifest hash is tampered");
            AssertContains(verifyOutput.Output, "signature verification failed");
        });
    }

    internal static void HardeningWorkflowBuildsAndUploadsSignedReleaseUpdateArtifacts()
    {
        var workflowPath = Path.Combine(GetRepositoryRoot(), ".github", "workflows", "hardening.yml");
        var builderPath = Path.Combine(GetRepositoryRoot(), "build-release-update-artifacts.ps1");
        var verifierPath = Path.Combine(GetRepositoryRoot(), "verify-release-update-artifacts.ps1");
        var publishScriptPath = Path.Combine(GetRepositoryRoot(), "publish-player-assistant.ps1");
        var installerSmokeScriptPath = Path.Combine(GetRepositoryRoot(), "verify-installer-clean-machine-smoke.ps1");

        AssertTrue(File.Exists(workflowPath), "hardening workflow should exist");
        AssertTrue(File.Exists(builderPath), "release update artifact builder should exist");
        AssertTrue(File.Exists(verifierPath), "release update artifact verifier should exist");
        AssertTrue(File.Exists(publishScriptPath), "publish verification script should exist");
        AssertTrue(File.Exists(installerSmokeScriptPath), "installer clean-machine smoke script should exist");

        var workflow = File.ReadAllText(workflowPath);
        AssertContains(workflow, "Build signed release update artifacts");
        AssertContains(workflow, ".\\build-release-update-artifacts.ps1");
        AssertContains(workflow, "Installer clean-machine smoke");
        AssertContains(workflow, ".\\verify-installer-clean-machine-smoke.ps1");
        AssertContains(workflow, "Verify signed release update artifacts");
        AssertContains(workflow, ".\\verify-release-update-artifacts.ps1");
        AssertContains(workflow, "Upload release installer artifacts");
        AssertContains(workflow, "p-assist-updates.json");
        AssertContains(workflow, "p-assist-updates.json.sig");
        AssertContains(workflow, "p-assist-updates.public-key.xml");
        AssertContains(workflow, "Load canonical version metadata");
        AssertContains(workflow, "UPDATE_ARCHIVE_PATH");
        AssertContains(workflow, "UPDATE_INSTALLER_PATH");
        AssertContains(workflow, "Build Release test harness");
        AssertContains(workflow, "Build Release ToOrcish");
        AssertContains(workflow, "dotnet build .\\ToOrcish\\to-orcish.csproj --configuration Release --nologo");
        AssertContains(workflow, "Run complete desktop regression harness");
        AssertContains(workflow, ".\\PlayerAssistant.Tests\\bin\\Release\\net10.0-windows\\PlayerAssistant.Tests.exe");
        AssertContains(workflow, "Run PHP broker suites");
        AssertContains(workflow, "Run browser-level PWA smoke tests");

        var builder = File.ReadAllText(builderPath);
        AssertContains(builder, "p-assist-$installerVersion.zip");
        AssertContains(builder, "p-assist-updates.json");
        AssertContains(builder, "GenerateEphemeralSigningKey");
        AssertContains(builder, "installer_sha256");

        var verifier = File.ReadAllText(verifierPath);
        AssertContains(verifier, "Signed update manifest");
        AssertContains(verifier, "p-assist-$installerVersion.zip");
        AssertContains(verifier, "p-assist-$installerVersion.exe");
        AssertContains(verifier, "Release update artifacts verification passed:");

        var publishScript = File.ReadAllText(publishScriptPath);
        AssertContains(publishScript, "build-release-update-artifacts.ps1");
        AssertContains(publishScript, "verify-release-update-artifacts.ps1");

        var installerSmokeScript = File.ReadAllText(installerSmokeScriptPath);
        AssertContains(installerSmokeScript, "--health");
        AssertContains(installerSmokeScript, "--update-preflight");
        AssertContains(installerSmokeScript, "PlayerAssistant/RPOL/UserName");
        AssertContains(installerSmokeScript, "settings.local.json");
        AssertContains(installerSmokeScript, "p-assist-updates.json");
    }

    internal static void PublishedHealthVerificationAcceptsCurrentOutput()
    {
        var output = RunPublishedHealthVerification(GetCurrentPublishDirectory());

        AssertEqual(0, output.ExitCode, $"published health verification should pass. Output: {output.Output}");
        AssertContains(output.Output, "Published health verification passed.");
        AssertContains(output.Output, "Status:");
    }

    internal static void SecretScanAcceptsCurrentRepository()
    {
        var output = RunSecretScan(GetRepositoryRoot(), includeHistory: true);

        AssertEqual(0, output.ExitCode, $"secret scan should pass. Output: {output.Output}");
        AssertContains(output.Output, "Secret scan passed.");
    }

    internal static void SecretScanRejectsTrackedEnvSecret()
    {
        var scratchRoot = Path.Combine(GetRepositoryRoot(), "codex-scratch");
        var scratchPath = Path.Combine(scratchRoot, $"secret-scan-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(scratchPath);
            RunGit(scratchPath, "init");
            RunGit(scratchPath, "config", "user.email", "test@example.invalid");
            RunGit(scratchPath, "config", "user.name", "Player Assistant Test");
            File.WriteAllText(
                Path.Combine(scratchPath, ".env"),
                "OPENAI" + "_API_KEY=sk-" + "proj-synthetic-test-secret-1234567890");
            RunGit(scratchPath, "add", ".env");
            RunGit(scratchPath, "commit", "-m", "add synthetic secret");

            var output = RunSecretScan(scratchPath, includeHistory: true);

            AssertFalse(output.ExitCode == 0, "secret scan should fail for a tracked env secret");
            AssertContains(output.Output, "Secret scan findings:");
            AssertContains(output.Output, "Forbidden tracked path");
            AssertContains(output.Output, "OpenAI API key");
        }
        finally
        {
            if (Directory.Exists(scratchPath))
            {
                DeleteDirectoryTree(scratchPath);
            }
        }
    }

    internal static void ReleasePublishParityAcceptsCurrentOutput()
    {
        var output = RunReleasePublishParity(GetCurrentReleaseDirectory(), GetCurrentPublishDirectory());

        AssertEqual(0, output.ExitCode, $"release/publish parity should pass. Output: {output.Output}");
        AssertContains(output.Output, "Release/publish parity verification passed.");
    }

    internal static void ReleasePublishParityRejectsMismatchedSidecar()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            File.AppendAllText(Path.Combine(directoryPath, KeywordTermsFileUtility.FileName), "\nsynthetic-parity-drift\n");

            var output = RunReleasePublishParity(GetCurrentReleaseDirectory(), directoryPath);

            AssertFalse(output.ExitCode == 0, "release/publish parity should fail when a published sidecar drifts");
            AssertContains(output.Output, "game-posts-key-terms.md SHA256 differs");
        });
    }

    internal static void DiagnosticBundleRedactsSensitiveValues()
    {
        WithTemporaryDiagnosticsRuntime((rootPath, releasePath, publishPath, outputPath) =>
        {
            var output = RunDiagnosticsCollection(
                releasePath,
                publishPath,
                outputPath,
                "-NoPublishVerification",
                "-NoPlanOutputs");

            AssertEqual(0, output.ExitCode, $"diagnostic collection should pass. Output: {output.Output}");
            AssertContains(output.Output, "Diagnostic bundle created:");

            var zipPath = GetDiagnosticZipPathFromOutput(output.Output);
            AssertTrue(File.Exists(zipPath), "diagnostic bundle zip should exist");

            var entries = GetZipEntryNames(zipPath);
            var expectedEntries = new[]
            {
                "metadata.json",
                "version-metadata.json",
                "runtime-sidecars.json",
                "Release/outbound-network-diagnostics.json",
                "Release/release-provenance.json",
                "Release/release-runtime-inventory.json",
                "Release/settings.redacted.json",
                "Release/startup-errors.log",
                "Release/startup-health.json",
                "Release/last-crash.json",
                "Release/startup-remediation.txt",
                "publish/outbound-network-diagnostics.json",
                "publish/release-provenance.json",
                "publish/release-runtime-inventory.json",
                "publish/settings.redacted.json",
                "publish/startup-errors.log",
                "publish/startup-health.json",
                "publish/last-crash.json",
                "publish/startup-remediation.txt"
            };

            AssertEqual(expectedEntries.Length, entries.Length, "diagnostic bundle should contain only the expected low-impact files");
            foreach (var expectedEntry in expectedEntries)
            {
                AssertTrue(entries.Contains(expectedEntry, StringComparer.Ordinal), $"diagnostic bundle is missing {expectedEntry}");
            }

            var releaseSettings = ReadZipEntryText(zipPath, "Release/settings.redacted.json");
            AssertContains(releaseSettings, "\"RPOL user name\":  \"[REDACTED]\"");
            AssertContains(releaseSettings, "\"RPOL password\":  \"[REDACTED]\"");
            AssertFalse(releaseSettings.Contains("example-user", StringComparison.Ordinal), "diagnostic settings should not contain RPOL user name");
            AssertFalse(releaseSettings.Contains("example-password", StringComparison.Ordinal), "diagnostic settings should not contain RPOL password");

            var releaseLog = ReadZipEntryText(zipPath, "Release/startup-errors.log");
            AssertFalse(releaseLog.Contains("secret-password", StringComparison.Ordinal), "diagnostic log should redact password query values");
            AssertFalse(releaseLog.Contains("secret-token", StringComparison.Ordinal), "diagnostic log should redact token query values");
            AssertFalse(releaseLog.Contains("Bearer abc123", StringComparison.Ordinal), "diagnostic log should redact bearer tokens");
            AssertFalse(releaseLog.Contains("sessionid=abc123", StringComparison.Ordinal), "diagnostic log should redact cookie headers");
            AssertFalse(releaseLog.Contains("user:pass@", StringComparison.Ordinal), "diagnostic log should redact credentialed URLs");

            var outboundDiagnostics = ReadZipEntryText(zipPath, "Release/outbound-network-diagnostics.json");
            using (var outboundDiagnosticsDocument = System.Text.Json.JsonDocument.Parse(outboundDiagnostics))
            {
                var endpointsElement = outboundDiagnosticsDocument.RootElement.GetProperty("endpoints");
                var endpointElement = endpointsElement.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? endpointsElement[0]
                    : endpointsElement;
                var purpose = endpointElement.GetProperty("purpose").GetString();
                AssertTrue(
                    string.Equals("PlayerAssistantHostedSettings", purpose, StringComparison.Ordinal),
                    "outbound diagnostics should preserve endpoint purpose metadata");
            }
            AssertFalse(outboundDiagnostics.Contains("secret-password", StringComparison.Ordinal), "outbound diagnostics should redact password query values");
            AssertFalse(outboundDiagnostics.Contains("secret-token", StringComparison.Ordinal), "outbound diagnostics should redact token query values");
            AssertFalse(outboundDiagnostics.Contains("Bearer abc123", StringComparison.Ordinal), "outbound diagnostics should redact bearer tokens");
            AssertFalse(outboundDiagnostics.Contains("sessionid=abc123", StringComparison.Ordinal), "outbound diagnostics should redact cookie headers");
            AssertFalse(outboundDiagnostics.Contains("user:pass@", StringComparison.Ordinal), "outbound diagnostics should redact credentialed URLs");

            var versionMetadata = ReadZipEntryText(zipPath, "version-metadata.json");
            AssertContains(versionMetadata, "\"authenticode_signature\":");
            AssertContains(versionMetadata, "\"status\":");

            var verifyOutput = RunDiagnosticsVerification(outputPath, zipPath);
            AssertEqual(0, verifyOutput.ExitCode, $"diagnostic verify-only should pass. Output: {verifyOutput.Output}");
            AssertContains(verifyOutput.Output, "Diagnostic bundle verification passed:");
        });
    }

    internal static void DiagnosticBundleVerifyOnlyRejectsForbiddenAuthState()
    {
        WithTemporaryDiagnosticsRuntime((rootPath, releasePath, publishPath, outputPath) =>
        {
            Directory.CreateDirectory(outputPath);
            var zipPath = Path.Combine(outputPath, "malicious-diagnostics.zip");
            var forbiddenSourcePath = Path.Combine(rootPath, "rpol-storage-state.json");
            File.WriteAllText(forbiddenSourcePath, """{"cookies":[{"name":"secret"}]}""");

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(forbiddenSourcePath, "rpol-storage-state.json");
            }

            var output = RunDiagnosticsVerification(outputPath, zipPath);

            AssertFalse(output.ExitCode == 0, "diagnostic verify-only should fail for forbidden auth state files");
            AssertContains(output.Output, "forbidden sensitive file");
        });
    }
}
