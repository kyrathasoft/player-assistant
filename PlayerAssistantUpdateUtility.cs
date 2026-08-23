using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal sealed record PlayerAssistantUpdateInfo(
        Version Version,
        string VersionText,
        Uri DownloadUri,
        string Sha256,
        Uri InstallerUri,
        string InstallerSha256)
    {
        public bool IsNewerThan(Version currentVersion)
        {
            ArgumentNullException.ThrowIfNull(currentVersion);
            return Version.CompareTo(currentVersion) > 0;
        }
    }

    internal static partial class PlayerAssistantUpdateUtility
    {
        private const string UpdateBaseUrlOverrideEnvironmentVariable = "PLAYER_ASSISTANT_UPDATE_BASE_URL";
        private const string AdditionalTrustedKeyPemEnvironmentVariable = "PLAYER_ASSISTANT_UPDATE_MANIFEST_PUBLIC_KEY_PEM";
        private const int UpdateManifestSchemaVersion = 1;
        private const int TrustedUpdateStateSchemaVersion = 1;
        private const string UpdateManifestFileName = "p-assist-updates.json";
        private const string UpdateManifestSignatureFileName = "p-assist-updates.json.sig";
        private const string TrustedUpdateStateFileName = "trusted-update-state.json";
        // Public verification keys only. Keep matching private signing keys outside the repository.
        private static readonly UpdateManifestSigningKeyTrustEntry[] TrustedUpdateManifestKeys =
        [
            new(
                "update-signing-2026-rotated",
                """
                -----BEGIN PUBLIC KEY-----
                MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAsal4hZqzJMwcYpU+pkZp
                2hWMPxo0rXEhMax5evHqNwQ98UGSW9wwR7Oo3/KnNZKDyuRl52F8mL+H5AOEVo9J
                axCYVicXr4CaJC6nUdOwzeHobgEconFkMJE5IId3GeaV8lC3pI8GWsuAJWlr/QWR
                rnM/jm0oKJICme6NTIViVo2InvjnLYHcPjWtmryfxEP7rNSMGysUy1FCQVPZgJFb
                Vaj/2ACGKs5OuzlameUViWWM/p1bBPi3EvUONoEE3Wq1Xxl7QXTMaIFSgcTzb68Y
                d7I/BTSJvqfzNt6TNsBs+yxsMW3a+5ENFzPKc1ugqYnOdxP/DGnOWBQyY56COYqo
                NOkTOHU1wtv9y12p9aFNo2HqiNYGv8iUHOajwNCAy6UAPoOT7FJiDDWA4fSIXqhs
                vHTCxkHDbGyjJM/qUewfj2biij1wiCi49RCce1MoFN8A9b2ROH0Ye10/1m64g0cr
                eYjkDtbmDJ8g0n6T4Rz44Vhv0rZ09XSFyB5OW0Lh9lAVAgMBAAE=
                -----END PUBLIC KEY-----
                """)
        ];

        public static Uri UpdateListingUri => GetUpdateListingUri();
        public static Uri UpdateManifestUri => new(UpdateListingUri, UpdateManifestFileName);
        public static Uri UpdateManifestSignatureUri => new(UpdateListingUri, UpdateManifestSignatureFileName);

        public static async Task<PlayerAssistantUpdateInfo?> CheckForLatestUpdateAsync(
            HttpClient httpClient,
            CancellationToken cancellationToken = default)
        {
            return await CheckForLatestUpdateAsync(
                httpClient,
                GetTrustedUpdateManifestKeys(),
                cancellationToken).ConfigureAwait(false);
        }

        internal static async Task<PlayerAssistantUpdateInfo?> CheckForLatestUpdateAsync(
            HttpClient httpClient,
            IReadOnlyList<UpdateManifestSigningKeyTrustEntry> trustedSigningKeys,
            CancellationToken cancellationToken = default)
        {
            return await CheckForLatestUpdateAsync(
                httpClient,
                trustedSigningKeys,
                trustedUpdateStatePath: null,
                cancellationToken).ConfigureAwait(false);
        }

        internal static async Task<PlayerAssistantUpdateInfo?> CheckForLatestUpdateAsync(
            HttpClient httpClient,
            IReadOnlyList<UpdateManifestSigningKeyTrustEntry> trustedSigningKeys,
            string? trustedUpdateStatePath,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentNullException.ThrowIfNull(trustedSigningKeys);

            var manifestBytes = await FetchUpdateManifestBytesAsync(httpClient, cancellationToken).ConfigureAwait(false);
            var signatureText = await FetchUpdateManifestSignatureAsync(httpClient, cancellationToken).ConfigureAwait(false);
            var latestUpdate = FindLatestUpdateFromSignedManifest(
                manifestBytes,
                signatureText,
                UpdateManifestUri,
                trustedSigningKeys);
            return ApplyTrustedUpdateVersionPolicy(
                latestUpdate,
                GetCurrentAppVersion(),
                trustedUpdateStatePath);
        }

        private static async Task<byte[]> FetchUpdateManifestBytesAsync(
            HttpClient httpClient,
            CancellationToken cancellationToken)
        {
            using var response = await NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, UpdateManifestUri),
                purpose: NetworkUrlPurpose.PlayerAssistantUpdate,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            return await NetworkRequestUtility.ReadBytesAsync(
                response.Content,
                NetworkResponseContentLimit.JsonCache,
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> FetchUpdateManifestSignatureAsync(
            HttpClient httpClient,
            CancellationToken cancellationToken)
        {
            using var response = await NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, UpdateManifestSignatureUri),
                purpose: NetworkUrlPurpose.PlayerAssistantUpdate,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            return await NetworkRequestUtility.ReadStringAsync(
                response.Content,
                NetworkResponseContentLimit.JsonCache,
                cancellationToken).ConfigureAwait(false);
        }

        internal static PlayerAssistantUpdateInfo? FindLatestUpdateFromSignedManifest(
            string manifestJson,
            string signatureText,
            Uri manifestUri,
            IReadOnlyList<UpdateManifestSigningKeyTrustEntry> trustedSigningKeys)
        {
            ArgumentNullException.ThrowIfNull(manifestJson);
            return FindLatestUpdateFromSignedManifest(
                Encoding.UTF8.GetBytes(manifestJson),
                signatureText,
                manifestUri,
                trustedSigningKeys);
        }

        internal static PlayerAssistantUpdateInfo? FindLatestUpdateFromSignedManifest(
            byte[] manifestBytes,
            string signatureText,
            Uri manifestUri,
            IReadOnlyList<UpdateManifestSigningKeyTrustEntry> trustedSigningKeys,
            DateTimeOffset? nowUtc = null)
        {
            ArgumentNullException.ThrowIfNull(manifestBytes);
            ArgumentException.ThrowIfNullOrWhiteSpace(signatureText);
            ArgumentNullException.ThrowIfNull(manifestUri);
            ArgumentNullException.ThrowIfNull(trustedSigningKeys);

            if (trustedSigningKeys.Count == 0)
            {
                throw new InvalidOperationException("No trusted update manifest signing keys are configured.");
            }

            NetworkUrlAllowlistUtility.EnsureAllowed(manifestUri, NetworkUrlPurpose.PlayerAssistantUpdate);
            VerifyManifestSignature(manifestBytes, signatureText, trustedSigningKeys, nowUtc ?? DateTimeOffset.UtcNow);

            var manifest = ParseUpdateManifest(manifestBytes);
            if (manifest.SchemaVersion != UpdateManifestSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Update manifest schema version {manifest.SchemaVersion} is not supported.");
            }

            if (manifest.Updates is null)
            {
                throw new InvalidOperationException("Update manifest is missing the updates list.");
            }

            return manifest.Updates
                .Select(entry => TryCreateUpdateInfo(entry, manifestUri))
                .Where(update => update is not null)
                .Select(update => update!)
                .GroupBy(update => update.DownloadUri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(update => update.Version)
                .FirstOrDefault();
        }

        public static PlayerAssistantUpdateInfo? FindLatestUpdate(string? listingContent, Uri listingUri)
        {
            ArgumentNullException.ThrowIfNull(listingUri);
            if (string.IsNullOrWhiteSpace(listingContent))
            {
                return null;
            }

            return EnumerateArchiveReferences(listingContent)
                .Select(reference => TryCreateUpdateInfo(reference, listingUri))
                .Where(update => update is not null)
                .Select(update => update!)
                .GroupBy(update => update.DownloadUri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(update => update.Version)
                .FirstOrDefault();
        }

        public static Version GetCurrentAppVersion()
        {
            var informationalVersion = typeof(PlayerAssistantUpdateUtility).Assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), inherit: false)
                .OfType<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()
                ?.InformationalVersion;

            return TryParseVersionPrefix(informationalVersion, out var version)
                ? version
                : typeof(PlayerAssistantUpdateUtility).Assembly.GetName().Version ?? new Version(0, 0, 0);
        }

        public static bool TryParseVersionPrefix(string? value, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var match = VersionPrefixRegex().Match(value.Trim());
            if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var parsed))
            {
                return false;
            }

            version = parsed;
            return true;
        }

        internal static PlayerAssistantUpdateInfo? ApplyTrustedUpdateVersionPolicy(
            PlayerAssistantUpdateInfo? latestUpdate,
            Version currentVersion,
            string? trustedUpdateStatePath = null)
        {
            ArgumentNullException.ThrowIfNull(currentVersion);

            var statePath = ResolveTrustedUpdateStatePath(trustedUpdateStatePath);
            var highestTrustedVersion = GetHighestTrustedVersion(statePath, currentVersion);
            if (latestUpdate is not null && latestUpdate.Version.CompareTo(highestTrustedVersion) < 0)
            {
                throw new InvalidOperationException(
                    $"Signed update channel downgrade detected. Highest trusted version {highestTrustedVersion} has already been observed, but the latest signed manifest version is {latestUpdate.VersionText}.");
            }

            if (latestUpdate is not null && latestUpdate.Version.CompareTo(highestTrustedVersion) > 0)
            {
                highestTrustedVersion = latestUpdate.Version;
            }

            PersistHighestTrustedVersion(statePath, highestTrustedVersion);
            return latestUpdate;
        }

        internal static Version? TryReadTrustedUpdateVersion(string? trustedUpdateStatePath = null)
        {
            var statePath = ResolveTrustedUpdateStatePath(trustedUpdateStatePath);
            if (!File.Exists(statePath))
            {
                return null;
            }

            var state = TryLoadLegacyTrustedUpdateState(statePath);
            if (state is not null)
            {
                LocalSettingsUtility.SaveScopedProtectedJson(statePath, state);
            }
            else
            {
                state = LocalSettingsUtility.LoadScopedProtectedJson<TrustedUpdateState>(statePath);
            }

            if (state.SchemaVersion != TrustedUpdateStateSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Trusted update state schema version {state.SchemaVersion} is not supported.");
            }

            if (!Version.TryParse(state.HighestTrustedVersion, out var version))
            {
                throw new InvalidOperationException("Trusted update state contains an invalid highest trusted version.");
            }

            return version;
        }

        private static TrustedUpdateState? TryLoadLegacyTrustedUpdateState(string statePath)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(statePath));
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || document.RootElement.TryGetProperty("format", out _)
                    || document.RootElement.TryGetProperty("payload", out _))
                {
                    return null;
                }

                return document.RootElement.Deserialize<TrustedUpdateState>();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string ResolveTrustedUpdateStatePath(string? trustedUpdateStatePath)
        {
            return string.IsNullOrWhiteSpace(trustedUpdateStatePath)
                ? RuntimePathUtility.GetUserDataPath(TrustedUpdateStateFileName)
                : trustedUpdateStatePath;
        }

        private static Version GetHighestTrustedVersion(string statePath, Version currentVersion)
        {
            var storedVersion = TryReadTrustedUpdateVersion(statePath);
            return storedVersion is not null && storedVersion.CompareTo(currentVersion) > 0
                ? storedVersion
                : currentVersion;
        }

        private static void PersistHighestTrustedVersion(string statePath, Version highestTrustedVersion)
        {
            var storedVersion = TryReadTrustedUpdateVersion(statePath);
            if (storedVersion is not null && storedVersion.CompareTo(highestTrustedVersion) >= 0)
            {
                return;
            }

            var state = new TrustedUpdateState(
                TrustedUpdateStateSchemaVersion,
                highestTrustedVersion.ToString(),
                DateTimeOffset.UtcNow.ToString("O"));
            LocalSettingsUtility.SaveScopedProtectedJson(statePath, state);
        }

        private static IReadOnlyList<UpdateManifestSigningKeyTrustEntry> GetTrustedUpdateManifestKeys()
        {
            var additionalTrustedKeyPem = Environment.GetEnvironmentVariable(AdditionalTrustedKeyPemEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(additionalTrustedKeyPem))
            {
                return TrustedUpdateManifestKeys;
            }

            return
            [
                .. TrustedUpdateManifestKeys,
                new UpdateManifestSigningKeyTrustEntry("environment-update-signing-key", additionalTrustedKeyPem.Trim())
            ];
        }

        private static Uri GetUpdateListingUri()
        {
            var overrideValue = Environment.GetEnvironmentVariable(UpdateBaseUrlOverrideEnvironmentVariable);
            if (Uri.TryCreate(overrideValue, UriKind.Absolute, out var overrideUri)
                && overrideUri.IsAbsoluteUri)
            {
                return overrideUri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
                    ? overrideUri
                    : new Uri($"{overrideUri.AbsoluteUri.TrimEnd('/')}/");
            }

            return new Uri("https://bryanmiller.us/scarlethorizons/");
        }

        private static IEnumerable<string> EnumerateArchiveReferences(string listingContent)
        {
            foreach (Match match in HrefRegex().Matches(listingContent))
            {
                yield return WebUtility.HtmlDecode(match.Groups["url"].Value);
            }

            foreach (Match match in ArchiveReferenceRegex().Matches(listingContent))
            {
                yield return WebUtility.HtmlDecode(match.Groups["url"].Value);
            }
        }

        private static PlayerAssistantUpdateInfo? TryCreateUpdateInfo(string reference, Uri listingUri)
        {
            if (string.IsNullOrWhiteSpace(reference) ||
                !Uri.TryCreate(listingUri, reference.Trim(), out var uri))
            {
                return null;
            }

            var allowlistValidation = NetworkUrlAllowlistUtility.Validate(uri, NetworkUrlPurpose.PlayerAssistantUpdate);
            if (!allowlistValidation.IsAllowed)
            {
                return null;
            }

            var fileName = Path.GetFileName(uri.LocalPath);
            var match = ArchiveFileNameRegex().Match(fileName);
            if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var version))
            {
                return null;
            }

            return new PlayerAssistantUpdateInfo(version, match.Groups["version"].Value, uri, string.Empty, uri, string.Empty);
        }

        private static PlayerAssistantUpdateInfo? TryCreateUpdateInfo(UpdateManifestEntry entry, Uri manifestUri)
        {
            if (string.IsNullOrWhiteSpace(entry.Version) ||
                string.IsNullOrWhiteSpace(entry.Url) ||
                string.IsNullOrWhiteSpace(entry.Sha256) ||
                string.IsNullOrWhiteSpace(entry.InstallerUrl) ||
                string.IsNullOrWhiteSpace(entry.InstallerSha256) ||
                !Uri.TryCreate(manifestUri, entry.Url.Trim(), out var uri))
            {
                return null;
            }

            if (!Uri.TryCreate(manifestUri, entry.InstallerUrl.Trim(), out var installerUri))
            {
                return null;
            }

            var allowlistValidation = NetworkUrlAllowlistUtility.Validate(uri, NetworkUrlPurpose.PlayerAssistantUpdate);
            var installerAllowlistValidation = NetworkUrlAllowlistUtility.Validate(installerUri, NetworkUrlPurpose.PlayerAssistantUpdate);
            if (!allowlistValidation.IsAllowed || !installerAllowlistValidation.IsAllowed)
            {
                return null;
            }

            var fileName = Path.GetFileName(uri.LocalPath);
            var installerFileName = Path.GetFileName(installerUri.LocalPath);
            var match = ArchiveFileNameRegex().Match(fileName);
            var installerMatch = InstallerFileNameRegex().Match(installerFileName);
            if (!match.Success ||
                !installerMatch.Success ||
                !Version.TryParse(match.Groups["version"].Value, out var fileVersion) ||
                !Version.TryParse(installerMatch.Groups["version"].Value, out var installerVersion) ||
                !Version.TryParse(entry.Version.Trim(), out var manifestVersion) ||
                fileVersion != manifestVersion ||
                installerVersion != manifestVersion ||
                !IsSha256Hex(entry.Sha256) ||
                !IsSha256Hex(entry.InstallerSha256))
            {
                return null;
            }

            return new PlayerAssistantUpdateInfo(
                manifestVersion,
                entry.Version.Trim(),
                uri,
                entry.Sha256.Trim().ToUpperInvariant(),
                installerUri,
                entry.InstallerSha256.Trim().ToUpperInvariant());
        }

        private static UpdateManifest ParseUpdateManifest(byte[] manifestBytes)
        {
            try
            {
                return JsonSerializer.Deserialize<UpdateManifest>(manifestBytes)
                    ?? throw new InvalidOperationException("Update manifest is empty.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Update manifest is not valid JSON.", ex);
            }
        }

        private static void VerifyManifestSignature(
            byte[] manifestBytes,
            string signatureText,
            IReadOnlyList<UpdateManifestSigningKeyTrustEntry> trustedSigningKeys,
            DateTimeOffset nowUtc)
        {
            var signatureBytes = ParseSignature(signatureText);
            UpdateManifestSigningKeyTrustEntry? retiredMatch = null;
            var signedPayloadCandidates = GetSignedPayloadCandidates(manifestBytes);
            foreach (var trustedSigningKey in trustedSigningKeys)
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(trustedSigningKey.PublicKeyPem);
                if (signedPayloadCandidates.Any(candidate =>
                        rsa.VerifyData(
                            candidate,
                            signatureBytes,
                            HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pkcs1)))
                {
                    if (!trustedSigningKey.IsRevoked && IsWithinTrustWindow(trustedSigningKey, nowUtc))
                    {
                        return;
                    }

                    retiredMatch = trustedSigningKey;
                }
            }

            if (retiredMatch is not null)
            {
                var status = retiredMatch.IsRevoked ? "revoked" : "retired";
                throw new InvalidOperationException(
                    $"Update manifest signature matched a {status} signing key ('{retiredMatch.KeyId}').");
            }

            throw new InvalidOperationException("Update manifest signature could not be verified with a trusted signing key.");
        }

        private static byte[][] GetSignedPayloadCandidates(byte[] manifestBytes)
        {
            var trimmedLength = manifestBytes.Length;
            while (trimmedLength > 0
                && manifestBytes[trimmedLength - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                trimmedLength--;
            }

            return trimmedLength == manifestBytes.Length
                ? [manifestBytes]
                : [manifestBytes, manifestBytes[..trimmedLength]];
        }

        private static bool IsWithinTrustWindow(UpdateManifestSigningKeyTrustEntry trustedSigningKey, DateTimeOffset nowUtc)
        {
            if (trustedSigningKey.NotBeforeUtc is not null && nowUtc < trustedSigningKey.NotBeforeUtc.Value)
            {
                return false;
            }

            if (trustedSigningKey.NotAfterUtc is not null && nowUtc > trustedSigningKey.NotAfterUtc.Value)
            {
                return false;
            }

            return true;
        }

        private static byte[] ParseSignature(string signatureText)
        {
            var trimmed = signatureText.Trim();
            if (trimmed.StartsWith("-----BEGIN", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Update manifest signature must be base64-encoded raw signature bytes, not a PEM block.");
            }

            try
            {
                return Convert.FromBase64String(trimmed);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Update manifest signature is not valid base64.", ex);
            }
        }

        private static bool IsSha256Hex(string value)
        {
            return Sha256Regex().IsMatch(value.Trim());
        }

        [GeneratedRegex(@"^\s*(?<version>\d+\.\d+\.\d+)", RegexOptions.CultureInvariant)]
        private static partial Regex VersionPrefixRegex();

        [GeneratedRegex(@"href\s*=\s*[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex HrefRegex();

        [GeneratedRegex(@"(?<url>(?:https?://[^\s""'<>]+/)?p-assist-[^\s""'<>]+\.zip)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ArchiveReferenceRegex();

        [GeneratedRegex(@"^p-assist-(?<version>\d+\.\d+\.\d+)[^/\\]*\.zip$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ArchiveFileNameRegex();

        [GeneratedRegex(@"^p-assist-(?<version>\d+\.\d+\.\d+)[^/\\]*\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex InstallerFileNameRegex();

        [GeneratedRegex(@"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
        private static partial Regex Sha256Regex();

        private sealed record UpdateManifest(
            [property: JsonPropertyName("schema_version")] int SchemaVersion,
            [property: JsonPropertyName("updates")] UpdateManifestEntry[] Updates);

        private sealed record UpdateManifestEntry(
            [property: JsonPropertyName("version")] string Version,
            [property: JsonPropertyName("url")] string Url,
            [property: JsonPropertyName("sha256")] string Sha256,
            [property: JsonPropertyName("installer_url")] string InstallerUrl,
            [property: JsonPropertyName("installer_sha256")] string InstallerSha256);

        private sealed record TrustedUpdateState(
            [property: JsonPropertyName("schema_version")] int SchemaVersion,
            [property: JsonPropertyName("highest_trusted_version")] string HighestTrustedVersion,
            [property: JsonPropertyName("recorded_at")] string RecordedAt);
    }

    internal sealed record UpdateManifestSigningKeyTrustEntry(
        string KeyId,
        string PublicKeyPem,
        DateTimeOffset? NotBeforeUtc = null,
        DateTimeOffset? NotAfterUtc = null,
        bool IsRevoked = false);
}
