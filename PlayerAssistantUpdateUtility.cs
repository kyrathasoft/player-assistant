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
        string Sha256)
    {
        public bool IsNewerThan(Version currentVersion)
        {
            ArgumentNullException.ThrowIfNull(currentVersion);
            return Version.CompareTo(currentVersion) > 0;
        }
    }

    internal static partial class PlayerAssistantUpdateUtility
    {
        private const int UpdateManifestSchemaVersion = 1;
        private const string UpdateManifestFileName = "p-assist-updates.json";
        private const string UpdateManifestSignatureFileName = "p-assist-updates.json.sig";
        // Public verification keys only. Keep the matching private update-signing key outside the repository.
        private static readonly string[] TrustedUpdateManifestPublicKeys =
        [
            """
            -----BEGIN PUBLIC KEY-----
            MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA1O6Lb1iZWkxwzEE69NiX
            t3Dhyf0ZK2tr7UrJZNGJ3wmS8SyKWi4PYn1ymWxpJ3QmyqJhem3d52B3C6Prp8oq
            0RpBZia7K2qo4VRoNqQfxGGHHkZv18v5Q+NOhIZET8LRG6RwOuKvP3vg76hylgBj
            wC/WlNaxXPg981j0UAh2tLwJAN2+GroBzVMCwX4LEfUwZ6pqN+TgOJ1ZFHowvH3F
            IZ9EBqQAM/HGiTHb8gA5YMZj/UApeek6T7Mkw9WUYE3CR10kMFqzgiNirCNJHbs6
            h5sx4M4HZoAMWcd4317uuayoOeue+Ggq7q1UVj4w274x3N51wHKT61cHyx5GdSW/
            2QIDAQAB
            -----END PUBLIC KEY-----
            """
        ];

        public static Uri UpdateListingUri { get; } = new("https://bryanmiller.us/scarlethorizons/");
        public static Uri UpdateManifestUri { get; } = new(UpdateListingUri, UpdateManifestFileName);
        public static Uri UpdateManifestSignatureUri { get; } = new(UpdateListingUri, UpdateManifestSignatureFileName);

        public static async Task<PlayerAssistantUpdateInfo?> CheckForLatestUpdateAsync(
            HttpClient httpClient,
            CancellationToken cancellationToken = default)
        {
            return await CheckForLatestUpdateAsync(
                httpClient,
                TrustedUpdateManifestPublicKeys,
                cancellationToken).ConfigureAwait(false);
        }

        internal static async Task<PlayerAssistantUpdateInfo?> CheckForLatestUpdateAsync(
            HttpClient httpClient,
            IReadOnlyList<string> trustedPublicKeys,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentNullException.ThrowIfNull(trustedPublicKeys);

            var manifestBytes = await FetchUpdateManifestBytesAsync(httpClient, cancellationToken).ConfigureAwait(false);
            var signatureText = await FetchUpdateManifestSignatureAsync(httpClient, cancellationToken).ConfigureAwait(false);
            return FindLatestUpdateFromSignedManifest(
                manifestBytes,
                signatureText,
                UpdateManifestUri,
                trustedPublicKeys);
        }

        private static async Task<byte[]> FetchUpdateManifestBytesAsync(
            HttpClient httpClient,
            CancellationToken cancellationToken)
        {
            using var response = await NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, UpdateManifestUri),
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
            IReadOnlyList<string> trustedPublicKeys)
        {
            ArgumentNullException.ThrowIfNull(manifestJson);
            return FindLatestUpdateFromSignedManifest(
                Encoding.UTF8.GetBytes(manifestJson),
                signatureText,
                manifestUri,
                trustedPublicKeys);
        }

        internal static PlayerAssistantUpdateInfo? FindLatestUpdateFromSignedManifest(
            byte[] manifestBytes,
            string signatureText,
            Uri manifestUri,
            IReadOnlyList<string> trustedPublicKeys)
        {
            ArgumentNullException.ThrowIfNull(manifestBytes);
            ArgumentException.ThrowIfNullOrWhiteSpace(signatureText);
            ArgumentNullException.ThrowIfNull(manifestUri);
            ArgumentNullException.ThrowIfNull(trustedPublicKeys);

            if (trustedPublicKeys.Count == 0)
            {
                throw new InvalidOperationException("No trusted update manifest signing keys are configured.");
            }

            NetworkUrlAllowlistUtility.EnsureAllowed(manifestUri, NetworkUrlPurpose.PlayerAssistantUpdate);
            VerifyManifestSignature(manifestBytes, signatureText, trustedPublicKeys);

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

            return new PlayerAssistantUpdateInfo(version, match.Groups["version"].Value, uri, string.Empty);
        }

        private static PlayerAssistantUpdateInfo? TryCreateUpdateInfo(UpdateManifestEntry entry, Uri manifestUri)
        {
            if (string.IsNullOrWhiteSpace(entry.Version) ||
                string.IsNullOrWhiteSpace(entry.Url) ||
                string.IsNullOrWhiteSpace(entry.Sha256) ||
                !Uri.TryCreate(manifestUri, entry.Url.Trim(), out var uri))
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
            if (!match.Success ||
                !Version.TryParse(match.Groups["version"].Value, out var fileVersion) ||
                !Version.TryParse(entry.Version.Trim(), out var manifestVersion) ||
                fileVersion != manifestVersion ||
                !IsSha256Hex(entry.Sha256))
            {
                return null;
            }

            return new PlayerAssistantUpdateInfo(
                manifestVersion,
                entry.Version.Trim(),
                uri,
                entry.Sha256.Trim().ToUpperInvariant());
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
            IReadOnlyList<string> trustedPublicKeys)
        {
            var signatureBytes = ParseSignature(signatureText);
            foreach (var publicKey in trustedPublicKeys)
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(publicKey);
                if (rsa.VerifyData(
                        manifestBytes,
                        signatureBytes,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1))
                {
                    return;
                }
            }

            throw new InvalidOperationException("Update manifest signature could not be verified with a trusted signing key.");
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

        [GeneratedRegex(@"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
        private static partial Regex Sha256Regex();

        private sealed record UpdateManifest(
            [property: JsonPropertyName("schema_version")] int SchemaVersion,
            [property: JsonPropertyName("updates")] UpdateManifestEntry[] Updates);

        private sealed record UpdateManifestEntry(
            [property: JsonPropertyName("version")] string Version,
            [property: JsonPropertyName("url")] string Url,
            [property: JsonPropertyName("sha256")] string Sha256);
    }
}
