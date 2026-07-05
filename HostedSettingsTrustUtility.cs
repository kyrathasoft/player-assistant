using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant
{
    internal static class HostedSettingsTrustUtility
    {
        internal const string SignedEnvelopeFormat = "signed-hosted-settings-v1";
        internal const string HostedSettingsContentId = "player-assistant-hosted-settings";

        private const int SignedEnvelopeSchemaVersion = 1;
        private const int TrustedHostedSettingsStateSchemaVersion = 1;
        private const string TrustedHostedSettingsStateFileName = "trusted-hosted-settings-state.json";
        private const string TrustedHostedSettingsStateVersionPropertyName = "highest_trusted_version";
        private static IReadOnlyList<HostedSettingsSigningKeyTrustEntry>? TrustedHostedSettingsKeysOverrideForTests;

        // Public verification keys only. Keep matching private signing keys outside the repository.
        private static readonly HostedSettingsSigningKeyTrustEntry[] TrustedHostedSettingsKeys =
        [
            new(
                "hosted-settings-signing-2026-primary",
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
                """)
        ];

        public static Dictionary<string, string> LoadAndVerifyHostedSettings(
            string signedHostedSettingsJson,
            string sourceDescription)
        {
            return LoadAndVerifyHostedSettings(
                signedHostedSettingsJson,
                sourceDescription,
                TrustedHostedSettingsKeysOverrideForTests ?? TrustedHostedSettingsKeys,
                trustedHostedSettingsStatePath: null,
                nowUtc: null);
        }

        internal static Dictionary<string, string> LoadAndVerifyHostedSettings(
            string signedHostedSettingsJson,
            string sourceDescription,
            IReadOnlyList<HostedSettingsSigningKeyTrustEntry> trustedSigningKeys,
            string? trustedHostedSettingsStatePath = null,
            DateTimeOffset? nowUtc = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(signedHostedSettingsJson);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceDescription);
            ArgumentNullException.ThrowIfNull(trustedSigningKeys);

            if (trustedSigningKeys.Count == 0)
            {
                throw new InvalidOperationException("No trusted hosted settings signing keys are configured.");
            }

            var envelope = ParseSignedEnvelope(signedHostedSettingsJson);
            if (!string.Equals(envelope.Format, SignedEnvelopeFormat, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{sourceDescription} must use hosted settings signed envelope format '{SignedEnvelopeFormat}'.");
            }

            if (!string.Equals(envelope.ContentId, HostedSettingsContentId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{sourceDescription} content identity '{envelope.ContentId}' did not match expected '{HostedSettingsContentId}'.");
            }

            if (!Version.TryParse(envelope.Version, out var version))
            {
                throw new InvalidOperationException($"{sourceDescription} hosted settings version '{envelope.Version}' is invalid.");
            }

            VerifyEnvelopeSignature(envelope, trustedSigningKeys, nowUtc ?? DateTimeOffset.UtcNow);
            var settings = LocalSettingsUtility.LoadPortableEncryptedSettingsFromContents(
                envelope.EncryptedSettingsJson,
                sourceDescription);
            ApplyTrustedHostedSettingsVersionPolicy(version, trustedHostedSettingsStatePath);
            return settings;
        }

        internal static string CreateSignedHostedSettingsJson(
            IReadOnlyDictionary<string, string> settings,
            string version,
            RSA signingKey,
            string contentId = HostedSettingsContentId)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var portableEncryptedSettingsJson = LocalSettingsUtility.CreatePortableEncryptedSettingsJson(settings);
            return CreateSignedHostedSettingsJson(portableEncryptedSettingsJson, version, signingKey, contentId);
        }

        internal static string CreateSignedHostedSettingsJson(
            string portableEncryptedSettingsJson,
            string version,
            RSA signingKey,
            string contentId = HostedSettingsContentId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(portableEncryptedSettingsJson);
            ArgumentException.ThrowIfNullOrWhiteSpace(version);
            ArgumentNullException.ThrowIfNull(signingKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(contentId);

            if (!Version.TryParse(version.Trim(), out _))
            {
                throw new InvalidOperationException($"Hosted settings version '{version}' is invalid.");
            }

            var envelope = new SignedHostedSettingsEnvelope(
                SignedEnvelopeSchemaVersion,
                SignedEnvelopeFormat,
                contentId.Trim(),
                version.Trim(),
                portableEncryptedSettingsJson,
                Convert.ToBase64String(signingKey.SignData(
                    CreateSignaturePayload(
                        SignedEnvelopeFormat,
                        contentId.Trim(),
                        version.Trim(),
                        portableEncryptedSettingsJson),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1)));
            return JsonSerializer.Serialize(envelope, JsonOptions);
        }

        internal static void ApplyTrustedHostedSettingsVersionPolicy(
            Version version,
            string? trustedHostedSettingsStatePath = null)
        {
            ArgumentNullException.ThrowIfNull(version);

            var statePath = ResolveTrustedHostedSettingsStatePath(trustedHostedSettingsStatePath);
            var highestTrustedVersion = TryReadTrustedHostedSettingsVersion(statePath);
            if (highestTrustedVersion is not null && version.CompareTo(highestTrustedVersion) < 0)
            {
                throw new InvalidOperationException(
                    $"Hosted settings downgrade detected. Highest trusted hosted settings version {highestTrustedVersion} has already been observed, but the newly fetched signed hosted settings version is {version}.");
            }

            if (highestTrustedVersion is not null && version.CompareTo(highestTrustedVersion) <= 0)
            {
                return;
            }

            var state = new TrustedHostedSettingsState(
                TrustedHostedSettingsStateSchemaVersion,
                version.ToString(),
                DateTimeOffset.UtcNow.ToString("O"));
            LocalSettingsUtility.SaveScopedProtectedJson(statePath, state);
        }

        internal static Version? TryReadTrustedHostedSettingsVersion(string? trustedHostedSettingsStatePath = null)
        {
            var statePath = ResolveTrustedHostedSettingsStatePath(trustedHostedSettingsStatePath);
            if (!File.Exists(statePath))
            {
                return null;
            }

            var legacyState = TryLoadLegacyTrustedHostedSettingsState(statePath);
            TrustedHostedSettingsState state;
            if (legacyState is not null)
            {
                state = legacyState;
                LocalSettingsUtility.SaveScopedProtectedJson(statePath, state);
            }
            else
            {
                state = LocalSettingsUtility.LoadScopedProtectedJson<TrustedHostedSettingsState>(statePath);
            }

            if (state.SchemaVersion != TrustedHostedSettingsStateSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Trusted hosted settings state schema version {state.SchemaVersion} is not supported.");
            }

            if (!Version.TryParse(state.HighestTrustedVersion, out var version))
            {
                throw new InvalidOperationException("Trusted hosted settings state contains an invalid highest trusted version.");
            }

            return version;
        }

        internal static IDisposable UseTrustedSigningKeysForTests(IReadOnlyList<HostedSettingsSigningKeyTrustEntry> trustedSigningKeys)
        {
            ArgumentNullException.ThrowIfNull(trustedSigningKeys);

            var previousTrustedKeys = TrustedHostedSettingsKeysOverrideForTests;
            TrustedHostedSettingsKeysOverrideForTests = trustedSigningKeys;
            return new DelegateDisposable(() => TrustedHostedSettingsKeysOverrideForTests = previousTrustedKeys);
        }

        private static SignedHostedSettingsEnvelope ParseSignedEnvelope(string signedHostedSettingsJson)
        {
            SignedHostedSettingsEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<SignedHostedSettingsEnvelope>(signedHostedSettingsJson, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Hosted settings are not valid JSON.", ex);
            }

            if (envelope is null)
            {
                throw new InvalidOperationException("Hosted settings signed envelope is empty.");
            }

            if (envelope.SchemaVersion != SignedEnvelopeSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Hosted settings signed envelope schema version {envelope.SchemaVersion} is not supported.");
            }

            if (string.IsNullOrWhiteSpace(envelope.Format)
                || string.IsNullOrWhiteSpace(envelope.ContentId)
                || string.IsNullOrWhiteSpace(envelope.Version)
                || string.IsNullOrWhiteSpace(envelope.EncryptedSettingsJson)
                || string.IsNullOrWhiteSpace(envelope.Signature))
            {
                throw new InvalidOperationException("Hosted settings signed envelope is missing required metadata.");
            }

            return envelope;
        }

        private static void VerifyEnvelopeSignature(
            SignedHostedSettingsEnvelope envelope,
            IReadOnlyList<HostedSettingsSigningKeyTrustEntry> trustedSigningKeys,
            DateTimeOffset nowUtc)
        {
            var signatureBytes = ParseSignature(envelope.Signature);
            var payloadBytes = CreateSignaturePayload(
                envelope.Format,
                envelope.ContentId,
                envelope.Version,
                envelope.EncryptedSettingsJson);
            HostedSettingsSigningKeyTrustEntry? retiredMatch = null;

            foreach (var trustedSigningKey in trustedSigningKeys)
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(trustedSigningKey.PublicKeyPem);
                if (rsa.VerifyData(
                        payloadBytes,
                        signatureBytes,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1))
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
                    $"Hosted settings signature matched a {status} signing key ('{retiredMatch.KeyId}').");
            }

            throw new InvalidOperationException("Hosted settings signature could not be verified with a trusted signing key.");
        }

        private static byte[] CreateSignaturePayload(
            string format,
            string contentId,
            string version,
            string encryptedSettingsJson)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            WriteFramedString(writer, format);
            WriteFramedString(writer, contentId);
            WriteFramedString(writer, version);
            WriteFramedString(writer, encryptedSettingsJson);
            writer.Flush();
            return stream.ToArray();
        }

        private static void WriteFramedString(BinaryWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static bool IsWithinTrustWindow(HostedSettingsSigningKeyTrustEntry trustedSigningKey, DateTimeOffset nowUtc)
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
                throw new InvalidOperationException("Hosted settings signature must be base64-encoded raw signature bytes, not a PEM block.");
            }

            try
            {
                return Convert.FromBase64String(trimmed);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Hosted settings signature is not valid base64.", ex);
            }
        }

        private static TrustedHostedSettingsState? TryLoadLegacyTrustedHostedSettingsState(string statePath)
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

                return document.RootElement.Deserialize<TrustedHostedSettingsState>(JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string ResolveTrustedHostedSettingsStatePath(string? trustedHostedSettingsStatePath)
        {
            return string.IsNullOrWhiteSpace(trustedHostedSettingsStatePath)
                ? RuntimePathUtility.GetUserDataPath(TrustedHostedSettingsStateFileName)
                : trustedHostedSettingsStatePath;
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private sealed record SignedHostedSettingsEnvelope(
            [property: JsonPropertyName("schema_version")] int SchemaVersion,
            [property: JsonPropertyName("format")] string Format,
            [property: JsonPropertyName("content_id")] string ContentId,
            [property: JsonPropertyName("version")] string Version,
            [property: JsonPropertyName("encrypted_settings")] string EncryptedSettingsJson,
            [property: JsonPropertyName("signature")] string Signature);

        private sealed record TrustedHostedSettingsState(
            [property: JsonPropertyName("schema_version")] int SchemaVersion,
            [property: JsonPropertyName(TrustedHostedSettingsStateVersionPropertyName)] string HighestTrustedVersion,
            [property: JsonPropertyName("recorded_at")] string RecordedAt);

        private sealed class DelegateDisposable(Action onDispose) : IDisposable
        {
            private Action? onDispose = onDispose;

            public void Dispose()
            {
                Interlocked.Exchange(ref onDispose, null)?.Invoke();
            }
        }
    }

    internal sealed record HostedSettingsSigningKeyTrustEntry(
        string KeyId,
        string PublicKeyPem,
        DateTimeOffset? NotBeforeUtc = null,
        DateTimeOffset? NotAfterUtc = null,
        bool IsRevoked = false);
}
