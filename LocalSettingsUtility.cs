using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant
{
    internal static class LocalSettingsUtility
    {
        private const string LegacyEncryptedFormat = "dpapi-current-user";
        private const string CurrentEncryptedFormat = "dpapi-current-user-v2";
        private const string V1EncryptedFormat = "app-protected-v1";
        private const string V2EncryptedFormat = "app-protected-v2";
        private const string LegacyScopedEncryptedFormat = "app-protected-v3";
        private const string PublicSettingsFormat = "public-settings-v1";
        private const string FormatPropertyName = "format";
        private const string PayloadPropertyName = "payload";
        private const string KeyScopePropertyName = "key_scope";
        private const string SchemaVersionPropertyName = "schema_version";
        private const int CurrentSchemaVersion = 1;
        private const string EncryptionKeySeed = "PlayerAssistant.LocalSettings.v1";
        private const int AesIvSizeBytes = 16;
        private const int HmacSizeBytes = 32;
        private static readonly byte[] V1EncryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(EncryptionKeySeed));
        private static readonly byte[] V2EncryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(EncryptionKeySeed));
        private static readonly byte[] V2AuthenticationKey = SHA256.HashData(Encoding.UTF8.GetBytes($"{EncryptionKeySeed}.hmac"));
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static Dictionary<string, string> LoadSettings(string settingsPath)
        {
            return LoadSettingsWithBackupRestore(settingsPath, migrateToCurrentFormat: true);
        }

        internal static Dictionary<string, string> LoadSettingsWithoutMigration(string settingsPath)
        {
            return LoadSettingsWithBackupRestore(settingsPath, migrateToCurrentFormat: false);
        }

        private static Dictionary<string, string> LoadSettingsWithBackupRestore(string settingsPath, bool migrateToCurrentFormat)
        {
            try
            {
                return LoadSettingsCore(settingsPath, migrateToCurrentFormat);
            }
            catch (Exception ex) when (IsRecoverableSettingsException(ex))
            {
                if (RuntimeBackupUtility.TryRestoreLatestValidBackup(
                        settingsPath,
                        candidatePath => CanLoadSettingsBackup(candidatePath, settingsPath),
                        "local settings backup restore",
                        ex,
                        out _))
                {
                    return LoadSettingsCore(settingsPath, migrateToCurrentFormat);
                }

                throw;
            }
        }

        private static Dictionary<string, string> LoadSettingsCore(
            string settingsPath,
            bool migrateToCurrentFormat,
            string? decryptionScopePath = null)
        {
            ArgumentNullException.ThrowIfNull(settingsPath);

            var resolvedDecryptionScopePath = decryptionScopePath ?? settingsPath;
            if (!File.Exists(settingsPath))
            {
                throw new FileNotFoundException("Settings file not found.", settingsPath);
            }

            var fileContentsUtf8 = File.ReadAllBytes(settingsPath);
            try
            {
                using var document = JsonDocument.Parse(TrimUtf8Bom(fileContentsUtf8));

                if (TryReadEncryptedEnvelope(document.RootElement, out var envelope))
                {
                    var decryptedSettings = DecryptSettings(envelope, resolvedDecryptionScopePath);
                    if (migrateToCurrentFormat
                        && (!string.Equals(envelope.Format, CurrentEncryptedFormat, StringComparison.Ordinal)
                            || envelope.SchemaVersion != CurrentSchemaVersion))
                    {
                        SaveEncryptedSettings(settingsPath, decryptedSettings);
                    }

                    return decryptedSettings;
                }

                var plaintextSettings = ReadPlaintextSettings(document.RootElement, settingsPath);

                if (migrateToCurrentFormat)
                {
                    SaveEncryptedSettings(settingsPath, plaintextSettings);
                }

                return plaintextSettings;
            }
            finally
            {
                ZeroMemory(fileContentsUtf8);
            }
        }

        private static bool CanLoadSettingsBackup(string settingsPath, string decryptionScopePath)
        {
            try
            {
                _ = LoadSettingsCore(settingsPath, migrateToCurrentFormat: false, decryptionScopePath);
                return true;
            }
            catch (Exception ex) when (IsRecoverableSettingsException(ex))
            {
                return false;
            }
        }

        public static void SaveEncryptedSettings(string settingsPath, IReadOnlyDictionary<string, string> settings)
        {
            ArgumentNullException.ThrowIfNull(settingsPath);
            ArgumentNullException.ThrowIfNull(settings);

            var plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
            try
            {
                var encryptedEnvelope = CreateEncryptedEnvelope(plaintextBytes, settingsPath);
                var encryptedJson = JsonSerializer.Serialize(encryptedEnvelope, JsonOptions);
                AtomicFileUtility.WriteAllText(settingsPath, encryptedJson);
            }
            finally
            {
                ZeroMemory(plaintextBytes);
            }
        }

        public static Dictionary<string, string> LoadPortableEncryptedSettings(string settingsPath)
        {
            ArgumentNullException.ThrowIfNull(settingsPath);

            if (!File.Exists(settingsPath))
            {
                throw new FileNotFoundException("Encrypted settings sidecar file not found.", settingsPath);
            }

            return LoadPortableEncryptedSettingsFromUtf8Bytes(File.ReadAllBytes(settingsPath), settingsPath);
        }

        internal static Dictionary<string, string> LoadPortableEncryptedSettingsFromContents(string fileContents, string sourceDescription)
        {
            ArgumentNullException.ThrowIfNull(fileContents);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceDescription);

            return LoadPortableEncryptedSettingsFromUtf8Bytes(Encoding.UTF8.GetBytes(fileContents), sourceDescription);
        }

        internal static Dictionary<string, string> LoadPortableEncryptedSettingsFromUtf8Bytes(byte[] fileContentsUtf8, string sourceDescription)
        {
            ArgumentNullException.ThrowIfNull(fileContentsUtf8);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceDescription);

            try
            {
                using var document = JsonDocument.Parse(TrimUtf8Bom(fileContentsUtf8));
                if (!TryReadEncryptedEnvelope(document.RootElement, out var envelope))
                {
                    throw new InvalidOperationException($"{sourceDescription} must use an authenticated encrypted envelope.");
                }

                if (!string.Equals(envelope.Format, V2EncryptedFormat, StringComparison.Ordinal)
                    && !string.Equals(envelope.Format, PublicSettingsFormat, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{sourceDescription} must use the portable settings format '{PublicSettingsFormat}'.");
                }

                return DecryptSettings(envelope, sourceDescription);
            }
            finally
            {
                ZeroMemory(fileContentsUtf8);
            }
        }

        public static void SavePortableEncryptedSettings(string settingsPath, IReadOnlyDictionary<string, string> settings)
        {
            ArgumentNullException.ThrowIfNull(settingsPath);
            ArgumentNullException.ThrowIfNull(settings);

            var encryptedJson = CreatePortableEncryptedSettingsJson(settings);
            AtomicFileUtility.WriteAllText(settingsPath, encryptedJson);
        }

        internal static string CreatePortableEncryptedSettingsJson(
            IReadOnlyDictionary<string, string> settings,
            bool includeRpolCredentials = false)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var portableSettings = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);
            // RPOL credentials are never portable. They are provisioned into Windows Credential
            // Manager and deliberately excluded even when legacy callers pass true.
            portableSettings.Remove(AppSettingsUtility.RpolUserNameSettingsKey);
            portableSettings.Remove(AppSettingsUtility.RpolPasswordSettingsKey);

            var plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(portableSettings, JsonOptions);
            try
            {
                var encryptedEnvelope = CreatePortableEncryptedEnvelope(plaintextBytes);
                return JsonSerializer.Serialize(encryptedEnvelope, JsonOptions);
            }
            finally
            {
                ZeroMemory(plaintextBytes);
            }
        }

        internal static void SaveScopedProtectedJson<T>(string path, T value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(value);

            var plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            try
            {
                var encryptedEnvelope = CreateEncryptedEnvelope(plaintextBytes, path);
                var encryptedJson = JsonSerializer.Serialize(encryptedEnvelope, JsonOptions);
                AtomicFileUtility.WriteAllText(path, encryptedJson);
            }
            finally
            {
                ZeroMemory(plaintextBytes);
            }
        }

        internal static T LoadScopedProtectedJson<T>(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Protected settings file not found.", path);
            }

            var fileContentsUtf8 = File.ReadAllBytes(path);
            try
            {
                using var document = JsonDocument.Parse(TrimUtf8Bom(fileContentsUtf8));
                if (!TryReadEncryptedEnvelope(document.RootElement, out var envelope))
                {
                    throw new InvalidOperationException("Protected settings file must use an authenticated encrypted envelope.");
                }

                var plaintextBytes = DecryptEnvelopePayload(envelope, path);
                try
                {
                    return JsonSerializer.Deserialize<T>(plaintextBytes, JsonOptions)
                        ?? throw new InvalidOperationException("Protected settings payload could not be parsed.");
                }
                finally
                {
                    ZeroMemory(plaintextBytes);
                }
            }
            finally
            {
                ZeroMemory(fileContentsUtf8);
            }
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

        private static Dictionary<string, string> DecryptSettings(EncryptedSettingsEnvelope envelope, string settingsPath)
        {
            var plaintextBytes = DecryptEnvelopePayload(envelope, settingsPath);
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(plaintextBytes, JsonOptions)
                    ?? throw new InvalidOperationException("The authenticated encrypted settings payload could not be parsed.");
            }
            finally
            {
                ZeroMemory(plaintextBytes);
            }
        }

        private static byte[] DecryptEnvelopePayload(EncryptedSettingsEnvelope envelope, string settingsPath)
        {
            try
            {
                return envelope.Format switch
                {
                    CurrentEncryptedFormat => DecryptDpapiPayload(envelope.Payload, settingsPath: null),
                    PublicSettingsFormat => DecodePublicSettingsPayload(envelope.Payload),
                    LegacyScopedEncryptedFormat => DecryptAuthenticatedAesCbcPayload(envelope.Payload, settingsPath, LegacyScopedEncryptedFormat),
                    V2EncryptedFormat => DecryptAuthenticatedAesCbcPayload(envelope.Payload, settingsPath, V2EncryptedFormat),
                    V1EncryptedFormat => DecryptAesCbcPayload(envelope.Payload),
                    LegacyEncryptedFormat => DecryptDpapiPayload(envelope.Payload, settingsPath: null),
                    _ => throw new InvalidOperationException(
                        $"Unsupported encrypted settings format '{envelope.Format}'.")
                };
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("The encrypted settings payload is not valid base64.", ex);
            }
        }

        private static byte[] DecodePublicSettingsPayload(string payload)
        {
            try
            {
                var bytes = Convert.FromBase64String(payload);
                if (bytes.Length == 0)
                {
                    throw new InvalidOperationException("The public settings payload is empty.");
                }
                return bytes;
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("The public settings payload is not valid base64.", ex);
            }
        }

        private static byte[] DecryptAuthenticatedAesCbcPayload(string payload, string settingsPath, string format)
        {
            try
            {
                var protectedBytes = Convert.FromBase64String(payload);
                if (protectedBytes.Length < AesIvSizeBytes + HmacSizeBytes + 1)
                {
                    throw new InvalidOperationException("The authenticated encrypted settings payload is too short.");
                }

                var protectedContent = protectedBytes[..^HmacSizeBytes];
                var expectedHmac = protectedBytes[^HmacSizeBytes..];
                var keySet = GetKeySet(format, settingsPath);
                using (var hmac = new HMACSHA256(keySet.AuthenticationKey))
                {
                    var actualHmac = hmac.ComputeHash(protectedContent);
                    if (!CryptographicOperations.FixedTimeEquals(actualHmac, expectedHmac))
                    {
                        throw new CryptographicException("The encrypted settings authentication tag did not match.");
                    }
                }

                var iv = protectedContent[..AesIvSizeBytes];
                var ciphertext = protectedContent[AesIvSizeBytes..];

                using var aes = Aes.Create();
                aes.Key = keySet.EncryptionKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException(
                    "Unable to authenticate or decrypt settings.local.json. The encrypted settings file may have been tampered with, corrupted, or created for a different Windows user, machine, or install directory.",
                    ex);
            }
        }

        private static byte[] DecryptAesCbcPayload(string payload)
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
                aes.Key = V1EncryptionKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException(
                    "Unable to decrypt settings.local.json. The encrypted settings file may have been corrupted or created with a different app key.",
                    ex);
            }
        }

        private static byte[] DecryptDpapiPayload(string payload, string? settingsPath)
        {
            try
            {
                var protectedBytes = Convert.FromBase64String(payload);
                var entropy = settingsPath is null ? null : GetDpapiEntropy(settingsPath);
                try
                {
                    return ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
                }
                finally
                {
                    ZeroMemory(entropy);
                }
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException(
                    "Unable to decrypt settings.local.json. The encrypted settings file may have been corrupted or created for a different Windows user, machine, or install directory.",
                    ex);
            }
        }

        private static EncryptedSettingsEnvelope CreateEncryptedEnvelope(byte[] plaintextBytes, string settingsPath)
        {
            var protectedBytes = ProtectedData.Protect(
                plaintextBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            try
            {
                return new EncryptedSettingsEnvelope(
                    CurrentSchemaVersion,
                    CurrentEncryptedFormat,
                    Convert.ToBase64String(protectedBytes),
                    new KeyScope(
                        MachineBound: false,
                        UserBound: true,
                        InstallPathBound: false,
                        ScopeHash: string.Empty));
            }
            finally
            {
                ZeroMemory(protectedBytes);
            }
        }

        private static EncryptedSettingsEnvelope CreatePortableEncryptedEnvelope(byte[] plaintextBytes)
        {
            if (plaintextBytes.Length == 0)
            {
                throw new InvalidOperationException("Portable settings cannot be empty.");
            }

            // Portable settings contain public configuration only. Credentials are stored in
            // Windows Credential Manager, while authenticity of hosted settings comes from the
            // outer signed envelope. Never use a derivable executable-embedded key here.
            return new EncryptedSettingsEnvelope(
                CurrentSchemaVersion,
                PublicSettingsFormat,
                Convert.ToBase64String(plaintextBytes));
        }

        private static void ZeroMemory(byte[]? bytes)
        {
            if (bytes is { Length: > 0 })
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        private static ReadOnlyMemory<byte> TrimUtf8Bom(byte[] bytes)
        {
            return bytes is [0xEF, 0xBB, 0xBF, ..]
                ? bytes.AsMemory(3)
                : bytes;
        }

        private static KeySet GetKeySet(string format, string settingsPath)
        {
            if (string.Equals(format, V2EncryptedFormat, StringComparison.Ordinal))
            {
                return new KeySet(V2EncryptionKey, V2AuthenticationKey);
            }

            if (string.Equals(format, LegacyScopedEncryptedFormat, StringComparison.Ordinal))
            {
                var scope = GetDerivationScope(settingsPath);
                return new KeySet(
                    SHA256.HashData(Encoding.UTF8.GetBytes($"{EncryptionKeySeed}.v3.encryption.{scope}")),
                    SHA256.HashData(Encoding.UTF8.GetBytes($"{EncryptionKeySeed}.v3.hmac.{scope}")));
            }

            throw new InvalidOperationException($"Unsupported settings encryption format '{format}'.");
        }

        private static byte[] GetDpapiEntropy(string settingsPath)
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes($"{EncryptionKeySeed}.dpapi.{GetDerivationScope(settingsPath)}"));
        }

        private static string GetDerivationScope(string settingsPath)
        {
            var directoryPath = Path.GetDirectoryName(Path.GetFullPath(settingsPath)) ?? AppContext.BaseDirectory;
            var installPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
            var machine = Environment.MachineName.ToUpperInvariant();
            var user = $"{Environment.UserDomainName}\\{Environment.UserName}".ToUpperInvariant();
            return $"{machine}|{user}|{installPath}";
        }

        private static KeyScope GetKeyScope(string settingsPath)
        {
            var scopeHash = SHA256.HashData(Encoding.UTF8.GetBytes(GetDerivationScope(settingsPath)));
            return new KeyScope(
                MachineBound: true,
                UserBound: true,
                InstallPathBound: true,
                ScopeHash: Convert.ToHexString(scopeHash));
        }

        private static bool IsRecoverableSettingsException(Exception ex)
        {
            return ex is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or JsonException
                or FormatException
                or CryptographicException;
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
            if (!string.Equals(format, CurrentEncryptedFormat, StringComparison.Ordinal)
                && !string.Equals(format, LegacyScopedEncryptedFormat, StringComparison.Ordinal)
                && !string.Equals(format, V2EncryptedFormat, StringComparison.Ordinal)
                && !string.Equals(format, V1EncryptedFormat, StringComparison.Ordinal)
                && !string.Equals(format, PublicSettingsFormat, StringComparison.Ordinal)
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

            var schemaVersion = ReadSchemaVersion(root, "encrypted settings file");

            KeyScope? keyScope = null;
            if (root.TryGetProperty(KeyScopePropertyName, out var keyScopeElement)
                && keyScopeElement.ValueKind == JsonValueKind.Object)
            {
                keyScope = keyScopeElement.Deserialize<KeyScope>(JsonOptions);
            }

            envelope = new EncryptedSettingsEnvelope(schemaVersion, format, payload, keyScope);
            return true;
        }

        private static Dictionary<string, string> ReadPlaintextSettings(JsonElement root, string settingsPath)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Settings file '{settingsPath}' is empty or invalid.");
            }

            _ = ReadSchemaVersion(root, "plaintext settings file");

            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, SchemaVersionPropertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException(
                        $"Settings file '{settingsPath}' value '{property.Name}' must be a string.");
                }

                settings[property.Name] = property.Value.GetString() ?? string.Empty;
            }

            return settings;
        }

        private static int ReadSchemaVersion(JsonElement root, string description)
        {
            if (!root.TryGetProperty(SchemaVersionPropertyName, out var schemaVersionElement))
            {
                return 0;
            }

            if (schemaVersionElement.ValueKind != JsonValueKind.Number
                || !schemaVersionElement.TryGetInt32(out var schemaVersion)
                || schemaVersion < 0)
            {
                throw new InvalidOperationException(
                    $"The {description} has an invalid '{SchemaVersionPropertyName}' value.");
            }

            if (schemaVersion > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"The {description} uses unsupported schema version {schemaVersion}. This app supports schema version {CurrentSchemaVersion}.");
            }

            return schemaVersion;
        }

        private sealed record EncryptedSettingsEnvelope(
            [property: JsonPropertyName(SchemaVersionPropertyName)] int SchemaVersion,
            [property: JsonPropertyName(FormatPropertyName)] string Format,
            [property: JsonPropertyName(PayloadPropertyName)] string Payload,
            [property: JsonPropertyName(KeyScopePropertyName)] KeyScope? KeyScope = null);

        private sealed record KeyScope(
            [property: JsonPropertyName("machine_bound")] bool MachineBound,
            [property: JsonPropertyName("user_bound")] bool UserBound,
            [property: JsonPropertyName("install_path_bound")] bool InstallPathBound,
            [property: JsonPropertyName("scope_hash")] string ScopeHash);

        private sealed record KeySet(byte[] EncryptionKey, byte[] AuthenticationKey);
    }
}
