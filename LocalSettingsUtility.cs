using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant
{
    internal static class LocalSettingsUtility
    {
        private const string LegacyEncryptedFormat = "dpapi-current-user";
        private const string EncryptedFormat = "app-protected-v1";
        private const string FormatPropertyName = "format";
        private const string PayloadPropertyName = "payload";
        private const string EncryptionKeySeed = "PlayerAssistant.LocalSettings.v1";
        private const int AesIvSizeBytes = 16;
        private static readonly byte[] EncryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(EncryptionKeySeed));
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static Dictionary<string, string> LoadSettings(string settingsPath)
        {
            ArgumentNullException.ThrowIfNull(settingsPath);

            if (!File.Exists(settingsPath))
            {
                throw new FileNotFoundException("Settings file not found.", settingsPath);
            }

            var fileContents = File.ReadAllText(settingsPath);
            using var document = JsonDocument.Parse(fileContents);

            if (TryReadEncryptedEnvelope(document.RootElement, out var envelope))
            {
                var decryptedSettings = DecryptSettings(envelope);
                if (string.Equals(envelope.Format, LegacyEncryptedFormat, StringComparison.Ordinal))
                {
                    SaveEncryptedSettings(settingsPath, decryptedSettings);
                }

                return decryptedSettings;
            }

            var plaintextSettings = JsonSerializer.Deserialize<Dictionary<string, string>>(
                document.RootElement.GetRawText(),
                JsonOptions);

            if (plaintextSettings is null)
            {
                throw new InvalidOperationException($"Settings file '{settingsPath}' is empty or invalid.");
            }

            SaveEncryptedSettings(settingsPath, plaintextSettings);
            return plaintextSettings;
        }

        public static void SaveEncryptedSettings(string settingsPath, IReadOnlyDictionary<string, string> settings)
        {
            ArgumentNullException.ThrowIfNull(settingsPath);
            ArgumentNullException.ThrowIfNull(settings);

            var plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
            var encryptedEnvelope = CreateEncryptedEnvelope(plaintextBytes);

            var encryptedJson = JsonSerializer.Serialize(encryptedEnvelope, JsonOptions);
            File.WriteAllText(settingsPath, encryptedJson);
        }

        public static bool IsEncryptedSettingsFile(string settingsPath)
        {
            if (!File.Exists(settingsPath))
            {
                return false;
            }

            using var stream = File.OpenRead(settingsPath);
            using var document = JsonDocument.Parse(stream);
            return TryReadEncryptedEnvelope(document.RootElement, out _);
        }

        private static Dictionary<string, string> DecryptSettings(EncryptedSettingsEnvelope envelope)
        {
            try
            {
                return envelope.Format switch
                {
                    EncryptedFormat => DecryptAesSettings(envelope.Payload),
                    LegacyEncryptedFormat => DecryptDpapiSettings(envelope.Payload),
                    _ => throw new InvalidOperationException(
                        $"Unsupported encrypted settings format '{envelope.Format}'.")
                };
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("The encrypted settings payload is not valid base64.", ex);
            }
        }

        private static Dictionary<string, string> DecryptAesSettings(string payload)
        {
            try
            {
                var protectedBytes = Convert.FromBase64String(payload);
                if (protectedBytes.Length < AesIvSizeBytes + 1)
                {
                    throw new InvalidOperationException("The encrypted settings payload is too short.");
                }

                var iv = protectedBytes[..AesIvSizeBytes];
                var ciphertext = protectedBytes[AesIvSizeBytes..];

                using var aes = Aes.Create();
                aes.Key = EncryptionKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                var plaintextBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

                return JsonSerializer.Deserialize<Dictionary<string, string>>(plaintextBytes, JsonOptions)
                    ?? throw new InvalidOperationException("The encrypted settings payload could not be parsed.");
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException(
                    "Unable to decrypt settings.local.json. The encrypted settings file may have been corrupted or created with a different app key.",
                    ex);
            }
        }

        private static Dictionary<string, string> DecryptDpapiSettings(string payload)
        {
            try
            {
                var protectedBytes = Convert.FromBase64String(payload);
                var plaintextBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(plaintextBytes, JsonOptions)
                    ?? throw new InvalidOperationException("The encrypted settings payload could not be parsed.");
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException(
                    "Unable to decrypt settings.local.json. The legacy dpapi-current-user format is tied to the original Windows user profile and cannot be decrypted on a different machine. Replace it with plaintext once or with an app-protected-v1 file.",
                    ex);
            }
        }

        private static EncryptedSettingsEnvelope CreateEncryptedEnvelope(byte[] plaintextBytes)
        {
            var iv = RandomNumberGenerator.GetBytes(AesIvSizeBytes);

            using var aes = Aes.Create();
            aes.Key = EncryptionKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

            var payloadBytes = new byte[iv.Length + ciphertext.Length];
            Buffer.BlockCopy(iv, 0, payloadBytes, 0, iv.Length);
            Buffer.BlockCopy(ciphertext, 0, payloadBytes, iv.Length, ciphertext.Length);

            return new EncryptedSettingsEnvelope(
                EncryptedFormat,
                Convert.ToBase64String(payloadBytes));
        }

        private static bool TryReadEncryptedEnvelope(JsonElement root, out EncryptedSettingsEnvelope envelope)
        {
            envelope = null!;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty(FormatPropertyName, out var formatElement)
                || formatElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var format = formatElement.GetString() ?? string.Empty;
            if (!string.Equals(format, EncryptedFormat, StringComparison.Ordinal)
                && !string.Equals(format, LegacyEncryptedFormat, StringComparison.Ordinal))
            {
                return false;
            }

            if (!root.TryGetProperty(PayloadPropertyName, out var payloadElement)
                || payloadElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("The encrypted settings file is missing its payload.");
            }

            var payload = payloadElement.GetString() ?? string.Empty;
            if (payload.Length == 0)
            {
                return false;
            }

            envelope = new EncryptedSettingsEnvelope(format, payload);
            return true;
        }

        private sealed record EncryptedSettingsEnvelope(
            [property: JsonPropertyName(FormatPropertyName)] string Format,
            [property: JsonPropertyName(PayloadPropertyName)] string Payload);
    }
}
