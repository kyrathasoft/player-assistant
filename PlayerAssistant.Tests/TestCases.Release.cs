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
using System.Text.Json.Nodes;
using System.Windows.Forms;
using System.Xml.Linq;

namespace PlayerAssistant.Tests;

internal static partial class TestCases
{
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

        var expectedVersion = GetCanonicalVersion();
        var expectedAssemblyVersion = GetCanonicalVersion("PlayerAssistantAssemblyVersion");
        AssertEqual(Version.Parse(expectedAssemblyVersion), name.Version!, "unexpected assembly version");
        AssertEqual(expectedAssemblyVersion, fileVersion!, "unexpected file version");
        AssertEqual(expectedVersion, informationalVersion, "unexpected informational version");
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

    internal static void InstallerProtectsInstalledApplicationTree()
    {
        var installerPath = Path.Combine(GetRepositoryRoot(), "Installer", "install-player-assistant.ps1");
        var script = File.ReadAllText(installerPath);

        AssertContains(script, "function Protect-AppDirectory");
        AssertContains(script, "/inheritance:r");
        AssertContains(script, "${usersSid}:(OI)(CI)RX");
        AssertContains(script, "${systemSid}:F");
        AssertContains(script, "${administratorsSid}:F");
        AssertFalse(script.Contains("${usersSid}:(OI)(CI)M", StringComparison.Ordinal), "installer must not grant Users modify access to the application tree");
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

    internal static void PublishVerificationRejectsMalformedCanonicalId()
    {
        WithCopiedPublishDirectory(directoryPath =>
        {
            var sidecarPath = Path.Combine(directoryPath, XpPasswordStoreUtility.FileName);
            var document = JsonNode.Parse(File.ReadAllText(sidecarPath))!.AsObject();
            document["entries"]!.AsArray()[0]!.AsObject()["canonical_id"] = "invalid/canonical-id";
            File.WriteAllText(sidecarPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var output = RunPublishVerification(directoryPath);

            AssertFalse(output.ExitCode == 0, "publish verification should reject malformed canonical IDs");
            AssertContains(output.Output, "malformed");
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

    internal static void DiagnosticRetentionCleanupRemovesOldDiagnosticsAndPreservesUnrelatedScratchFiles()
    {
        var scratchPath = Path.Combine(
            GetRepositoryRoot(),
            "codex-scratch",
            $"retention-test-{Guid.NewGuid():N}");
        var diagnosticsPath = Path.Combine(scratchPath, "diagnostics");
        var oldZipPath = Path.Combine(diagnosticsPath, "player-assistant-diagnostics-20260601-010101.zip");
        var newZipPath = Path.Combine(diagnosticsPath, "player-assistant-diagnostics-20260702-010101.zip");
        var oldStagingPath = Path.Combine(diagnosticsPath, "player-assistant-diagnostics-20260601-010101");
        var oldDiagnosticsTestPath = Path.Combine(scratchPath, "diagnostics-test-old");
        var oldPublishVerificationPath = Path.Combine(scratchPath, "publish-verification-old");
        var unrelatedScratchFilePath = Path.Combine(scratchPath, "candidates.txt");
        var unrelatedScratchDirectoryPath = Path.Combine(scratchPath, "manual-notes");

        try
        {
            Directory.CreateDirectory(diagnosticsPath);
            File.WriteAllText(oldZipPath, "old diagnostic zip");
            File.WriteAllText(newZipPath, "new diagnostic zip");
            Directory.CreateDirectory(oldStagingPath);
            File.WriteAllText(Path.Combine(oldStagingPath, "marker.txt"), "old staging");
            Directory.CreateDirectory(oldDiagnosticsTestPath);
            File.WriteAllText(Path.Combine(oldDiagnosticsTestPath, "marker.txt"), "old diagnostics test");
            Directory.CreateDirectory(oldPublishVerificationPath);
            File.WriteAllText(Path.Combine(oldPublishVerificationPath, "marker.txt"), "old publish verification");
            File.WriteAllText(unrelatedScratchFilePath, "lexicon backlog should stay");
            Directory.CreateDirectory(unrelatedScratchDirectoryPath);
            File.WriteAllText(Path.Combine(unrelatedScratchDirectoryPath, "marker.txt"), "manual scratch should stay");

            var oldTime = DateTimeOffset.UtcNow - TimeSpan.FromDays(30);
            SetLastWriteTimeUtc(oldZipPath, oldTime);
            SetDirectoryLastWriteTimeUtc(oldStagingPath, oldTime);
            SetDirectoryLastWriteTimeUtc(oldDiagnosticsTestPath, oldTime);
            SetDirectoryLastWriteTimeUtc(oldPublishVerificationPath, oldTime);
            SetLastWriteTimeUtc(unrelatedScratchFilePath, oldTime);
            SetDirectoryLastWriteTimeUtc(unrelatedScratchDirectoryPath, oldTime);

            var output = RunDiagnosticsRetentionCleanup(
                scratchPath,
                "-DiagnosticRetentionDays",
                "14",
                "-ScratchRetentionDays",
                "7",
                "-MaxDiagnosticZipCount",
                "10");

            AssertEqual(0, output.ExitCode, $"diagnostic retention cleanup should pass. Output: {output.Output}");
            AssertContains(output.Output, "Diagnostic retention cleanup removed");
            AssertFalse(File.Exists(oldZipPath), "old diagnostic zip should be removed");
            AssertTrue(File.Exists(newZipPath), "fresh diagnostic zip should be preserved");
            AssertFalse(Directory.Exists(oldStagingPath), "old diagnostic staging directory should be removed");
            AssertFalse(Directory.Exists(oldDiagnosticsTestPath), "old diagnostics test scratch directory should be removed");
            AssertFalse(Directory.Exists(oldPublishVerificationPath), "old publish verification scratch directory should be removed");
            AssertTrue(File.Exists(unrelatedScratchFilePath), "unrelated scratch file should be preserved");
            AssertTrue(Directory.Exists(unrelatedScratchDirectoryPath), "unrelated scratch directory should be preserved");
        }
        finally
        {
            if (Directory.Exists(scratchPath))
            {
                Directory.Delete(scratchPath, recursive: true);
            }
        }
    }

    private static void WithCopiedPublishDirectory(Action<string> action)
    {
        var directoryPath = Path.Combine(
            GetRepositoryRoot(),
            "codex-scratch",
            $"publish-verification-{Guid.NewGuid():N}");

        try
        {
            var currentPublishDirectory = Path.Combine(GetCurrentReleaseDirectory(), "publish");
            var currentPublishExecutablePath = Path.Combine(currentPublishDirectory, "player-assistant.exe");
            if (File.Exists(currentPublishExecutablePath))
            {
                CopyDirectory(currentPublishDirectory, directoryPath);
                ClearReadOnlyAttributes(directoryPath);
            }
            else
            {
                WriteManifestedRuntime(directoryPath);
                var currentReleaseDirectory = GetCurrentReleaseDirectory();
                foreach (var fileName in new[]
                {
                    "player-assistant.exe",
                    "player-assistant.dll",
                    "player-assistant.deps.json",
                    "player-assistant.runtimeconfig.json",
                    "playwright.ps1",
                    "Microsoft.Playwright.dll"
                })
                {
                    var sourcePath = Path.Combine(currentReleaseDirectory, fileName);
                    if (File.Exists(sourcePath))
                    {
                        File.Copy(sourcePath, Path.Combine(directoryPath, fileName), overwrite: true);
                    }
                }
            }
            File.Copy(
                Path.Combine(GetRepositoryRoot(), "settings.json"),
                Path.Combine(directoryPath, "settings.json"),
                overwrite: true);
            WriteRequiredRuntimeSidecars(directoryPath);
            var shippedLocalSettingsPath = Path.Combine(directoryPath, "settings.local.json");
            LocalSettingsUtility.SaveEncryptedSettings(
                shippedLocalSettingsPath,
                new Dictionary<string, string>
                {
                    ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
                    ["RPOL user name"] = "example-user",
                    ["RPOL password"] = "example-password"
                });
            WriteReleaseRuntimeInventory(directoryPath);
            WriteReleaseManifest(directoryPath);
            WriteReleaseProvenance(directoryPath);
            action(directoryPath);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                ClearReadOnlyAttributes(directoryPath);
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    private static void ClearReadOnlyAttributes(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }

    private static string GetDiagnosticZipPathFromOutput(string output)
    {
        var line = output
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.StartsWith("Diagnostic bundle created:", StringComparison.Ordinal));
        if (line is null)
        {
            throw new InvalidOperationException($"Unable to find diagnostic bundle path in output: {output}");
        }

        return line["Diagnostic bundle created:".Length..].Trim();
    }

    private static string ReadZipEntryText(string zipPath, string entryName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.SingleOrDefault(entry =>
            string.Equals(entry.FullName.Replace('\\', '/'), entryName, StringComparison.Ordinal));
        if (entry is null)
        {
            throw new InvalidOperationException($"Zip entry '{entryName}' was not found in {zipPath}.");
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string GetCurrentPublishDirectory()
    {
        var publishDirectory = Path.Combine(GetRepositoryRoot(), "Release", "publish");
        if (!Directory.Exists(publishDirectory))
        {
            throw new InvalidOperationException($"Publish directory is missing: {publishDirectory}");
        }

        return publishDirectory;
    }

    private static string GetCurrentReleaseDirectory()
    {
        var releaseDirectory = Path.Combine(GetRepositoryRoot(), "Release");
        if (!Directory.Exists(releaseDirectory))
        {
            throw new InvalidOperationException($"Release directory is missing: {releaseDirectory}");
        }

        return releaseDirectory;
    }

    private static string ResolvePowerShellExecutable()
    {
        var candidates = new[]
        {
            Environment.ProcessPath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "WindowsApps",
                "pwsh.exe")
        };

        foreach (var candidate in candidates)
        {
            if (IsPowerShellExecutablePath(candidate))
            {
                return candidate;
            }
        }

        foreach (var commandName in new[] { "pwsh.exe", "powershell.exe", "powershell" })
        {
            string? resolved = Environment.GetEnvironmentVariable("PATH")?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => Path.Combine(path.Trim(), commandName))
                .FirstOrDefault(IsPowerShellExecutablePath);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        throw new InvalidOperationException("Unable to locate a PowerShell executable.");
    }

    private static bool IsPowerShellExecutablePath([NotNullWhen(true)] string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        return string.Equals(fileName, "pwsh.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "powershell.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "powershell", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "pwsh", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "publish-player-assistant.ps1")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }

}
