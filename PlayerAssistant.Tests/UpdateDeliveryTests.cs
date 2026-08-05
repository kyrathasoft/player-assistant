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

internal static class UpdateDeliveryTests
{
    internal static void UpdateCheckVerifiesSignedPAssistManifest()
    {
        var signed = CreateSignedUpdateManifest(
            """
            {
              "schema_version": 1,
              "updates": [
                {
                  "version": "0.9.0",
                  "url": "https://bryanmiller.us/scarlethorizons/p-assist-0.9.0.zip",
                  "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                  "installer_url": "https://bryanmiller.us/scarlethorizons/p-assist-0.9.0.exe",
                  "installer_sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
                }
              ]
            }
            """);
        var update = PlayerAssistantUpdateUtility.FindLatestUpdateFromSignedManifest(
            signed.ManifestJson,
            signed.SignatureText,
            PlayerAssistantUpdateUtility.UpdateManifestUri,
            [CreateActiveSigningKey(signed.PublicKeyPem)]);

        AssertTrue(update is not null, "expected signed update archive to be detected");
        AssertEqual("0.9.0", update!.VersionText, "unexpected parsed update version text");
        AssertEqual(new Version(0, 9, 0), update.Version, "unexpected parsed update version");
        AssertEqual(
            "https://bryanmiller.us/scarlethorizons/p-assist-0.9.0.zip",
            update.DownloadUri.AbsoluteUri,
            "unexpected update download URL");
        AssertEqual(
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            update.Sha256,
            "unexpected update SHA256");
        AssertEqual(
            "https://bryanmiller.us/scarlethorizons/p-assist-0.9.0.exe",
            update.InstallerUri.AbsoluteUri,
            "unexpected installer URL");
        AssertEqual(
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            update.InstallerSha256,
            "unexpected installer SHA256");
    }

    internal static void UpdateCheckAcceptsManifestSignatureMadeBeforeTrailingNewline()
    {
        const string manifestJson =
            """
            {
              "schema_version": 1,
              "updates": [
                {
                  "version": "0.9.5",
                  "url": "p-assist-0.9.5.zip",
                  "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                  "installer_url": "p-assist-0.9.5.exe",
                  "installer_sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
                }
              ]
            }
            """;
        using var rsa = RSA.Create(2048);
        var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);
        var downloadedManifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson + Environment.NewLine);
        var signatureBytes = rsa.SignData(
            manifestBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var update = PlayerAssistantUpdateUtility.FindLatestUpdateFromSignedManifest(
            downloadedManifestBytes,
            Convert.ToBase64String(signatureBytes),
            PlayerAssistantUpdateUtility.UpdateManifestUri,
            [CreateActiveSigningKey(rsa.ExportSubjectPublicKeyInfoPem())]);

        AssertTrue(update is not null, "expected update manifest with trailing newline to verify");
        AssertEqual("0.9.5", update!.VersionText, "unexpected parsed update version");
    }

    internal static void UpdateCheckChoosesNewestSignedManifestEntry()
    {
        var signed = CreateSignedUpdateManifest(
            """
            {
              "schema_version": 1,
              "updates": [
                {
                  "version": "0.9.0",
                  "url": "p-assist-0.9.0.zip",
                  "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                  "installer_url": "p-assist-0.9.0.exe",
                  "installer_sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
                },
                {
                  "version": "0.10.0",
                  "url": "p-assist-0.10.0.zip",
                  "sha256": "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
                  "installer_url": "p-assist-0.10.0.exe",
                  "installer_sha256": "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD"
                },
                {
                  "version": "99.0.0",
                  "url": "https://unexpected.example.test/p-assist-99.0.0.zip",
                  "sha256": "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE",
                  "installer_url": "https://unexpected.example.test/p-assist-99.0.0.exe",
                  "installer_sha256": "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"
                },
                {
                  "version": "0.11.0",
                  "url": "p-assist-0.10.0.zip",
                  "sha256": "1111111111111111111111111111111111111111111111111111111111111111",
                  "installer_url": "p-assist-0.11.0.exe",
                  "installer_sha256": "2222222222222222222222222222222222222222222222222222222222222222"
                }
              ]
            }
            """);
        var update = PlayerAssistantUpdateUtility.FindLatestUpdateFromSignedManifest(
            signed.ManifestJson,
            signed.SignatureText,
            PlayerAssistantUpdateUtility.UpdateManifestUri,
            [CreateActiveSigningKey(signed.PublicKeyPem)]);

        AssertTrue(update is not null, "expected latest signed update archive to be detected");
        AssertEqual("0.10.0", update!.VersionText, "expected newest valid allowed archive version");
        AssertEqual(
            "https://bryanmiller.us/scarlethorizons/p-assist-0.10.0.zip",
            update.DownloadUri.AbsoluteUri,
            "unexpected newest update URL");
        AssertEqual(
            "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD",
            update.InstallerSha256,
            "unexpected newest installer SHA256");
    }

    internal static void UpdateCheckRejectsTamperedSignedManifest()
    {
        const string manifestJson =
            """
            {
              "schema_version": 1,
              "updates": [
                {
                  "version": "0.9.0",
                  "url": "p-assist-0.9.0.zip",
                  "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                  "installer_url": "p-assist-0.9.0.exe",
                  "installer_sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
                }
              ]
            }
            """;
        var signed = CreateSignedUpdateManifest(manifestJson);
        var tamperedManifestJson = manifestJson.Replace("0.9.0", "0.9.1", StringComparison.Ordinal);

        var exception = AssertThrows<InvalidOperationException>(() =>
            PlayerAssistantUpdateUtility.FindLatestUpdateFromSignedManifest(
                tamperedManifestJson,
                signed.SignatureText,
                PlayerAssistantUpdateUtility.UpdateManifestUri,
                [CreateActiveSigningKey(signed.PublicKeyPem)]));

        AssertContains(exception.Message, "signature");
    }

    internal static void UpdateCheckRejectsRetiredManifestSigningKey()
    {
        var signed = CreateSignedUpdateManifest(
            """
            {
              "schema_version": 1,
              "updates": [
                {
                  "version": "0.9.5",
                  "url": "p-assist-0.9.5.zip",
                  "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                  "installer_url": "p-assist-0.9.5.exe",
                  "installer_sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
                }
              ]
            }
            """);
        var retiredKeys = new[]
        {
            new UpdateManifestSigningKeyTrustEntry(
                "retired-key",
                signed.PublicKeyPem,
                NotAfterUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
        };
        var nowUtc = new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero);

        var exception = AssertThrows<InvalidOperationException>(() =>
            PlayerAssistantUpdateUtility.FindLatestUpdateFromSignedManifest(
                System.Text.Encoding.UTF8.GetBytes(signed.ManifestJson),
                signed.SignatureText,
                PlayerAssistantUpdateUtility.UpdateManifestUri,
                retiredKeys,
                nowUtc));

        AssertContains(exception.Message, "retired");
        AssertContains(exception.Message, "retired-key");
    }

    internal static void UpdateCheckComparesAgainstCurrentAppVersion()
    {
        var currentVersion = PlayerAssistantUpdateUtility.GetCurrentAppVersion();
        var expectedVersion = Version.Parse(GetCanonicalVersion().Split('-', '+')[0]);
        AssertEqual(expectedVersion, currentVersion, "unexpected current app update-comparison version");

        var sameVersion = new PlayerAssistantUpdateInfo(
            expectedVersion,
            expectedVersion.ToString(),
            new Uri($"https://bryanmiller.us/scarlethorizons/p-assist-{expectedVersion}.zip"),
            new string('A', 64),
            new Uri($"https://bryanmiller.us/scarlethorizons/p-assist-{expectedVersion}.exe"),
            new string('B', 64));
        var nextVersion = new Version(expectedVersion.Major, expectedVersion.Minor, expectedVersion.Build + 1);
        var newerVersion = new PlayerAssistantUpdateInfo(
            nextVersion,
            nextVersion.ToString(),
            new Uri($"https://bryanmiller.us/scarlethorizons/p-assist-{nextVersion}.zip"),
            new string('C', 64),
            new Uri($"https://bryanmiller.us/scarlethorizons/p-assist-{nextVersion}.exe"),
            new string('D', 64));

        AssertFalse(sameVersion.IsNewerThan(currentVersion), "same version should not be offered as an update");
        AssertTrue(newerVersion.IsNewerThan(currentVersion), "newer version should be offered as an update");
    }

    internal static void UpdateCheckReportsLatestVersionMessage()
    {
        var message = (string)(InvokeStaticMethod(typeof(Form1), "GetLatestVersionMessage")
            ?? throw new InvalidOperationException("GetLatestVersionMessage returned null."));
        AssertEqual(
            "You are using the latest version of this software.",
            message,
            "unexpected no-update message text");
    }

    internal static void UpdateCheckFetchesSignedManifestFromAllowedUpdateHost()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "trusted-update-state.json");
        var signed = CreateSignedUpdateManifest(
            """
            {
              "schema_version": 1,
              "updates": [
                {
                  "version": "0.9.5",
                  "url": "p-assist-0.9.5.zip",
                  "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                  "installer_url": "p-assist-0.9.5.exe",
                  "installer_sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
                }
              ]
            }
            """);
        var requestUris = new List<string>();
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((request, _) =>
        {
            requestUris.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
            if (request.RequestUri == PlayerAssistantUpdateUtility.UpdateManifestUri)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(signed.ManifestJson))
                });
            }

            if (request.RequestUri == PlayerAssistantUpdateUtility.UpdateManifestSignatureUri)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(signed.SignatureText)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request
            });
        }));

        var update = PlayerAssistantUpdateUtility
            .CheckForLatestUpdateAsync(httpClient, [CreateActiveSigningKey(signed.PublicKeyPem)], statePath)
            .GetAwaiter()
            .GetResult();
        AssertEqual(2, requestUris.Count, "expected manifest and signature requests");
        AssertEqual(
            PlayerAssistantUpdateUtility.UpdateManifestUri.AbsoluteUri,
            requestUris[0],
            "unexpected update manifest request URL");
        AssertEqual(
            PlayerAssistantUpdateUtility.UpdateManifestSignatureUri.AbsoluteUri,
            requestUris[1],
            "unexpected update manifest signature request URL");
        AssertTrue(update is not null, "expected update archive from scripted signed manifest");
        AssertEqual("0.9.5", update!.VersionText, "unexpected fetched update version");
        AssertEqual(
            "https://bryanmiller.us/scarlethorizons/p-assist-0.9.5.exe",
            update.InstallerUri.AbsoluteUri,
            "unexpected fetched installer URL");

        var launchValidation = ExternalUrlLaunchUtility.Validate(update.DownloadUri.AbsoluteUri);
        AssertTrue(launchValidation.IsAllowed, launchValidation.RejectionReason ?? "update download URL should be launchable");
    }

    internal static void UpdateCheckRemembersHighestTrustedVersionObserved()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "trusted-update-state.json");
        var currentVersion = new Version(0, 9, 1);
        var update = new PlayerAssistantUpdateInfo(
            new Version(0, 9, 2),
            "0.9.2",
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.zip"),
            new string('A', 64),
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.exe"),
            new string('B', 64));

        var result = PlayerAssistantUpdateUtility.ApplyTrustedUpdateVersionPolicy(update, currentVersion, statePath);
        var storedVersion = PlayerAssistantUpdateUtility.TryReadTrustedUpdateVersion(statePath);

        AssertTrue(result is not null, "expected trusted update to remain available");
        AssertEqual("0.9.2", result!.VersionText, "unexpected trusted update version");
        AssertEqual(new Version(0, 9, 2), storedVersion!, "expected highest trusted version to be recorded");
    }

    internal static void LegacyTrustedUpdateStateMigratesToProtectedFormat()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "trusted-update-state.json");
        File.WriteAllText(
            statePath,
            """
            {
              "schema_version": 1,
              "highest_trusted_version": "0.9.2",
              "recorded_at": "2026-07-04T00:00:00.0000000+00:00"
            }
            """);

        var version = PlayerAssistantUpdateUtility.TryReadTrustedUpdateVersion(statePath);
        var protectedJson = File.ReadAllText(statePath);

        AssertEqual(new Version(0, 9, 2), version!, "unexpected migrated trusted update version");
        AssertContains(protectedJson, "\"format\": \"app-protected-v3\"");
        AssertContains(protectedJson, "\"key_scope\":");
    }

    internal static void TrustedUpdateStateIsEncryptedAtRest()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "trusted-update-state.json");

        PlayerAssistantUpdateUtility.ApplyTrustedUpdateVersionPolicy(
            new PlayerAssistantUpdateInfo(
                new Version(0, 9, 2),
                "0.9.2",
                new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.zip"),
                new string('A', 64),
                new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.exe"),
                new string('B', 64)),
            new Version(0, 9, 1),
            statePath);

        var encryptedJson = File.ReadAllText(statePath);
        AssertContains(encryptedJson, "\"format\": \"app-protected-v3\"");
        AssertContains(encryptedJson, "\"key_scope\":");
        AssertFalse(encryptedJson.Contains("0.9.2", StringComparison.Ordinal), "trusted version should not be stored in plaintext");
    }

    internal static void TrustedUpdateStateRejectsTamperedPayload()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "trusted-update-state.json");

        PlayerAssistantUpdateUtility.ApplyTrustedUpdateVersionPolicy(
            new PlayerAssistantUpdateInfo(
                new Version(0, 9, 2),
                "0.9.2",
                new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.zip"),
                new string('A', 64),
                new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.exe"),
                new string('B', 64)),
            new Version(0, 9, 1),
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
            PlayerAssistantUpdateUtility.TryReadTrustedUpdateVersion(statePath));
        AssertContains(exception.Message, "authenticate or decrypt");
    }

    internal static void UpdateCheckRejectsSignedManifestRollbackBelowTrustedVersionFloor()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "trusted-update-state.json");
        var currentVersion = new Version(0, 9, 1);
        var newerTrustedUpdate = new PlayerAssistantUpdateInfo(
            new Version(0, 9, 2),
            "0.9.2",
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.zip"),
            new string('A', 64),
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.exe"),
            new string('B', 64));
        var rolledBackUpdate = new PlayerAssistantUpdateInfo(
            new Version(0, 9, 1),
            "0.9.1",
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.zip"),
            new string('C', 64),
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.exe"),
            new string('D', 64));

        PlayerAssistantUpdateUtility.ApplyTrustedUpdateVersionPolicy(newerTrustedUpdate, currentVersion, statePath);
        var exception = AssertThrows<InvalidOperationException>(() =>
            PlayerAssistantUpdateUtility.ApplyTrustedUpdateVersionPolicy(rolledBackUpdate, currentVersion, statePath));

        AssertContains(exception.Message, "downgrade");
        AssertContains(exception.Message, "0.9.2");
    }

    internal static void VerifiedUpdaterDownloadsInstallerToControlledPath()
    {
        using var directory = TemporaryDirectory.Create();
        var update = new PlayerAssistantUpdateInfo(
            new Version(0, 9, 1),
            "0.9.1",
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.zip"),
            new string('A', 64),
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.exe"),
            new string('B', 64));
        var installerBytes = System.Text.Encoding.UTF8.GetBytes("signed installer bytes");
        var installerSha256 = Convert.ToHexString(SHA256.HashData(installerBytes));
        update = update with { InstallerSha256 = installerSha256 };

        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((request, _) =>
        {
            AssertEqual(update.InstallerUri.AbsoluteUri, request.RequestUri?.AbsoluteUri ?? string.Empty, "unexpected installer download URL");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(installerBytes)
            });
        }));

        var result = VerifiedInstallerUpdateUtility.DownloadVerifiedInstallerAsync(
            httpClient,
            update,
            new AuthenticodeSignaturePolicy("CN=KyrathaSoft", "ABC123"),
            _ => new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "ABC123"),
            directory.Path).GetAwaiter().GetResult();

        AssertTrue(result.InstallerPath.StartsWith(directory.Path, StringComparison.OrdinalIgnoreCase), "installer should download under the controlled directory");
        AssertTrue(File.Exists(result.InstallerPath), "verified installer should exist on disk");
        AssertEqual(installerSha256, result.Sha256, "unexpected downloaded installer SHA256");
        AssertFalse(result.ReusedExistingFile, "fresh installer download should not report reuse");
    }

    internal static void UpdateHostCertificatePinningAcceptsTrustedLeafPin()
    {
        var isValid = CertificatePinningUtility.ValidatePinnedRequest(
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-updates.json"),
            ["Cs2RWBFFnGtCidcPrPVbM4awHfkwOQAdfcF2KohmJFc="],
            SslPolicyErrors.None,
            new CertificatePinningPolicy(
                "bryanmiller.us",
                [
                    new CertificatePinTrustEntry("Cs2RWBFFnGtCidcPrPVbM4awHfkwOQAdfcF2KohmJFc="),
                    new CertificatePinTrustEntry("nWN7PSep5XDQdge5zK24CnCRXHr3KvzhKEGxsdqCX9E=")
                ]));

        AssertTrue(isValid, "expected trusted leaf pin to satisfy update host pinning");
    }

    internal static void UpdateHostCertificatePinningAcceptsTrustedIntermediatePin()
    {
        var isValid = CertificatePinningUtility.ValidatePinnedRequest(
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-updates.json"),
            ["nWN7PSep5XDQdge5zK24CnCRXHr3KvzhKEGxsdqCX9E="],
            SslPolicyErrors.None,
            new CertificatePinningPolicy(
                "bryanmiller.us",
                [
                    new CertificatePinTrustEntry("Cs2RWBFFnGtCidcPrPVbM4awHfkwOQAdfcF2KohmJFc="),
                    new CertificatePinTrustEntry("nWN7PSep5XDQdge5zK24CnCRXHr3KvzhKEGxsdqCX9E=")
                ]));

        AssertTrue(isValid, "expected trusted intermediate pin to satisfy update host pinning");
    }

    internal static void UpdateHostCertificatePinningSupportsRotationWindow()
    {
        var now = new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero);
        var isValid = CertificatePinningUtility.ValidatePinnedRequest(
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-updates.json"),
            ["ROTATEDPINAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="],
            SslPolicyErrors.None,
            new CertificatePinningPolicy(
                "bryanmiller.us",
                [
                    new CertificatePinTrustEntry(
                        "OLDPINAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                        NotAfterUtc: now.AddDays(7)),
                    new CertificatePinTrustEntry(
                        "ROTATEDPINAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                        NotBeforeUtc: now.AddDays(-7))
                ]),
            now);

        AssertTrue(isValid, "expected rotated pin within overlap window to be trusted");
    }

    internal static void UpdateHostCertificatePinningRejectsRetiredPin()
    {
        var isValid = CertificatePinningUtility.ValidatePinnedRequest(
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-updates.json"),
            ["OLDPINAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="],
            SslPolicyErrors.None,
            new CertificatePinningPolicy(
                "bryanmiller.us",
                [
                    new CertificatePinTrustEntry(
                        "OLDPINAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                        NotAfterUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                    new CertificatePinTrustEntry(
                        "NEWPINAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                        NotBeforeUtc: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero))
                ]),
            new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero));

        AssertFalse(isValid, "expected retired pin outside its rotation window to be rejected");
    }

    internal static void UpdateHostCertificatePinningRejectsMismatchedPin()
    {
        var isValid = CertificatePinningUtility.ValidatePinnedRequest(
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-updates.json"),
            ["AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="],
            SslPolicyErrors.None,
            new CertificatePinningPolicy(
                "bryanmiller.us",
                [
                    new CertificatePinTrustEntry("Cs2RWBFFnGtCidcPrPVbM4awHfkwOQAdfcF2KohmJFc="),
                    new CertificatePinTrustEntry("nWN7PSep5XDQdge5zK24CnCRXHr3KvzhKEGxsdqCX9E=")
                ]));

        AssertFalse(isValid, "expected mismatched pin to be rejected for update host");
    }

    internal static void UpdateHostCertificateValidationAllowsTrustedTlsWithPinMismatch()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://bryanmiller.us/scarlethorizons/p-assist-updates.json");

        AssertTrue(
            CertificatePinningUtility.ValidateServerCertificate(
                request,
                certificate: null,
                chain: null,
                SslPolicyErrors.None),
            "trusted TLS should be accepted even when update-host pins are unavailable or mismatched because manifests are signed");
        AssertFalse(
            CertificatePinningUtility.ValidateServerCertificate(
                request,
                certificate: null,
                chain: null,
                SslPolicyErrors.RemoteCertificateChainErrors),
            "TLS validation errors should still be rejected for update checks");
    }

    internal static void CertificateValidationSkipsPinExtractionForNonUpdateHosts()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking");
        using var mapRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "https://bryanmiller.us/blog/content/bryan/blog/images/rpg-maps/northernreaches.png");

        AssertTrue(
            CertificatePinningUtility.ValidateServerCertificate(
                request,
                certificate: null,
                chain: null,
                SslPolicyErrors.None),
            "non-update hosts should use normal TLS validation without requiring Player Assistant update pins");
        AssertTrue(
            CertificatePinningUtility.ValidateServerCertificate(
                mapRequest,
                certificate: null,
                chain: null,
                SslPolicyErrors.None),
            "non-update paths on the update host should use normal TLS validation without requiring update pins");
    }

    internal static void VerifiedUpdaterRejectsInstallerSha256Mismatch()
    {
        using var directory = TemporaryDirectory.Create();
        var update = new PlayerAssistantUpdateInfo(
            new Version(0, 9, 1),
            "0.9.1",
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.zip"),
            new string('A', 64),
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.exe"),
            new string('B', 64));
        var installerBytes = System.Text.Encoding.UTF8.GetBytes("mismatched installer bytes");

        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(installerBytes)
            })));

        var exception = AssertThrows<InvalidOperationException>(() =>
            VerifiedInstallerUpdateUtility.DownloadVerifiedInstallerAsync(
                httpClient,
                update,
                new AuthenticodeSignaturePolicy("CN=KyrathaSoft", "ABC123"),
                _ => new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "ABC123"),
                directory.Path).GetAwaiter().GetResult());

        AssertContains(exception.Message, "SHA256");
    }

    internal static void VerifiedUpdaterRejectsInstallerSignerMismatch()
    {
        using var directory = TemporaryDirectory.Create();
        var installerBytes = System.Text.Encoding.UTF8.GetBytes("signed installer bytes");
        var update = new PlayerAssistantUpdateInfo(
            new Version(0, 9, 1),
            "0.9.1",
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.zip"),
            new string('A', 64),
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.exe"),
            Convert.ToHexString(SHA256.HashData(installerBytes)));

        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(installerBytes)
            })));

        var exception = AssertThrows<InvalidOperationException>(() =>
            VerifiedInstallerUpdateUtility.DownloadVerifiedInstallerAsync(
                httpClient,
                update,
                new AuthenticodeSignaturePolicy("CN=KyrathaSoft", "ABC123"),
                _ => new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "XYZ999"),
                directory.Path).GetAwaiter().GetResult());

        AssertContains(exception.Message, "thumbprint");
    }

    internal static void VerifiedInstallerLaunchReverifiesBeforeExecution()
    {
        using var directory = TemporaryDirectory.Create();
        var installerPath = Path.Combine(directory.Path, "p-assist-0.9.1.exe");
        var installerBytes = System.Text.Encoding.UTF8.GetBytes("signed installer bytes");
        File.WriteAllBytes(installerPath, installerBytes);

        var signature = new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "ABC123");
        var policy = new AuthenticodeSignaturePolicy("CN=KyrathaSoft", "ABC123");
        var download = new VerifiedInstallerDownloadResult(
            installerPath,
            Convert.ToHexString(SHA256.HashData(installerBytes)),
            signature,
            ReusedExistingFile: false);

        var ticket = VerifiedInstallerLaunchUtility.CreateLaunchTicket(
            download,
            policy,
            _ => signature,
            () => InstallerLaunchElevationContext.StandardUser);
        var startInfo = VerifiedInstallerLaunchUtility.CreateStartInfo(
            ticket,
            _ => signature,
            () => InstallerLaunchElevationContext.StandardUser);

        AssertEqual(Path.GetFullPath(installerPath), startInfo.FileName, "unexpected verified installer launch path");
        AssertTrue(startInfo.UseShellExecute, "verified installer launch should use shell execute");
        AssertEqual("open", startInfo.Verb, "verified installer launch should preserve the shell open verb");
        AssertEqual(directory.Path, startInfo.WorkingDirectory, "verified installer launch should use the installer directory as the working directory");
    }

    internal static void VerifiedInstallerLaunchRejectsSignerChangesAfterVerification()
    {
        using var directory = TemporaryDirectory.Create();
        var installerPath = Path.Combine(directory.Path, "p-assist-0.9.1.exe");
        var installerBytes = System.Text.Encoding.UTF8.GetBytes("signed installer bytes");
        File.WriteAllBytes(installerPath, installerBytes);

        var initialSignature = new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "ABC123");
        var changedSignature = new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "XYZ999");
        var policy = new AuthenticodeSignaturePolicy("CN=KyrathaSoft", null);
        var download = new VerifiedInstallerDownloadResult(
            installerPath,
            Convert.ToHexString(SHA256.HashData(installerBytes)),
            initialSignature,
            ReusedExistingFile: false);

        var ticket = VerifiedInstallerLaunchUtility.CreateLaunchTicket(
            download,
            policy,
            _ => initialSignature,
            () => InstallerLaunchElevationContext.StandardUser);

        var exception = AssertThrows<InvalidOperationException>(() =>
            VerifiedInstallerLaunchUtility.CreateStartInfo(
                ticket,
                _ => changedSignature,
                () => InstallerLaunchElevationContext.StandardUser));

        AssertContains(exception.Message, "signer details changed");
    }

    internal static void VerifiedInstallerLaunchRejectsElevationChangesAfterVerification()
    {
        using var directory = TemporaryDirectory.Create();
        var installerPath = Path.Combine(directory.Path, "p-assist-0.9.1.exe");
        var installerBytes = System.Text.Encoding.UTF8.GetBytes("signed installer bytes");
        File.WriteAllBytes(installerPath, installerBytes);

        var signature = new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "ABC123");
        var policy = new AuthenticodeSignaturePolicy("CN=KyrathaSoft", "ABC123");
        var download = new VerifiedInstallerDownloadResult(
            installerPath,
            Convert.ToHexString(SHA256.HashData(installerBytes)),
            signature,
            ReusedExistingFile: false);

        var ticket = VerifiedInstallerLaunchUtility.CreateLaunchTicket(
            download,
            policy,
            _ => signature,
            () => InstallerLaunchElevationContext.StandardUser);

        var exception = AssertThrows<InvalidOperationException>(() =>
            VerifiedInstallerLaunchUtility.CreateStartInfo(
                ticket,
                _ => signature,
                () => InstallerLaunchElevationContext.ElevatedAdministrator));

        AssertContains(exception.Message, "elevation context changed");
    }
}
