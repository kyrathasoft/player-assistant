using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant
{
    internal static class XpPasswordStoreUtility
    {
        public const string FileName = "xp-passwords.json";
        public const string Format = "xp-password-hashes-v1";
        public const string Algorithm = "PBKDF2-HMAC-SHA256";
        public const int SchemaVersion = 1;
        public const int MinimumIterations = 600_000;

        private const int SaltSize = 16;
        private const int HashSize = 32;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        internal static IReadOnlyDictionary<string, PasswordHashRecord> LoadPasswordHashes(
            string? runtimeDirectory = null)
        {
            var resolvedRuntimeDirectory = string.IsNullOrWhiteSpace(runtimeDirectory)
                ? AppContext.BaseDirectory
                : runtimeDirectory;
            var sidecarPath = RuntimePathUtility.CombineUnderBase(resolvedRuntimeDirectory, FileName);
            if (!File.Exists(sidecarPath))
            {
                throw new FileNotFoundException(
                    $"XP password hash sidecar '{FileName}' was not found at '{sidecarPath}'.",
                    sidecarPath);
            }

            PasswordHashDocument document;
            try
            {
                document = JsonSerializer.Deserialize<PasswordHashDocument>(
                    File.ReadAllText(sidecarPath),
                    JsonOptions)
                    ?? throw new JsonException("The document was empty.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"{FileName} must use the salted password hash format '{Format}'.",
                    ex);
            }

            if (document.SchemaVersion != SchemaVersion)
            {
                throw new InvalidOperationException(
                    $"{FileName} must declare schema_version {SchemaVersion}.");
            }

            if (!string.Equals(document.Format, Format, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{FileName} must use the salted password hash format '{Format}'.");
            }

            if (document.Entries is not { Count: > 0 })
            {
                throw new InvalidOperationException($"{FileName} does not contain any PC password hash entries.");
            }

            var hashes = new Dictionary<string, PasswordHashRecord>(StringComparer.OrdinalIgnoreCase);
            var salts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in document.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || !string.Equals(entry.Name, entry.Name.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{FileName} contains a blank or untrimmed PC name.");
                }

                if (!string.Equals(entry.Algorithm, Algorithm, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{FileName} entry '{entry.Name}' must use algorithm '{Algorithm}'.");
                }

                if (entry.Iterations < MinimumIterations)
                {
                    throw new InvalidOperationException(
                        $"{FileName} entry '{entry.Name}' must use at least {MinimumIterations:N0} iterations.");
                }

                var salt = DecodeBase64(entry.Salt, "salt", entry.Name);
                var hash = DecodeBase64(entry.Hash, "hash", entry.Name);
                if (salt.Length < SaltSize)
                {
                    throw new InvalidOperationException(
                        $"{FileName} entry '{entry.Name}' must use a salt of at least {SaltSize} bytes.");
                }

                if (hash.Length != HashSize)
                {
                    throw new InvalidOperationException(
                        $"{FileName} entry '{entry.Name}' must use a {HashSize}-byte hash.");
                }

                if (!salts.Add(entry.Salt!))
                {
                    throw new InvalidOperationException($"{FileName} contains a reused password salt.");
                }

                var hasCanonicalId = !string.IsNullOrWhiteSpace(entry.CanonicalId);
                var canonicalId = hasCanonicalId ? entry.CanonicalId!.Trim() : entry.Name;
                if (hasCanonicalId && !IsValidCanonicalId(canonicalId))
                {
                    throw new InvalidOperationException(
                        $"{FileName} entry '{entry.Name}' has an invalid canonical ID.");
                }

                if (!hashes.TryAdd(canonicalId, new PasswordHashRecord(
                    canonicalId,
                    entry.Name,
                    entry.Iterations,
                    salt,
                    hash)))
                {
                    throw new InvalidOperationException($"{FileName} contains duplicate canonical ID '{canonicalId}'.");
                }
            }

            return hashes;
        }

        public static XpAuthenticatedIdentity? ValidatePassword(string pcName, string password, string? runtimeDirectory = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pcName);
            ArgumentNullException.ThrowIfNull(password);

            return ValidatePassword(null, pcName, password, runtimeDirectory);
        }

        public static XpAuthenticatedIdentity? ValidatePassword(
            string? canonicalId,
            string displayName,
            string password,
            string? runtimeDirectory = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
            ArgumentNullException.ThrowIfNull(password);

            var hashes = LoadPasswordHashes(runtimeDirectory);
            if (!string.IsNullOrWhiteSpace(canonicalId))
            {
                if (!hashes.TryGetValue(canonicalId.Trim(), out var expectedHash)
                    || !string.Equals(expectedHash.Name, displayName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return VerifyPassword(password, expectedHash)
                    ? expectedHash.ToAuthenticatedIdentity()
                    : null;
            }

            var nameMatch = hashes.Values.FirstOrDefault(record =>
                string.Equals(record.Name, displayName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (nameMatch is not null && VerifyPassword(password, nameMatch))
            {
                return nameMatch.ToAuthenticatedIdentity();
            }

            return null;
        }

        internal static void SavePasswordHashes(
            string path,
            IReadOnlyDictionary<string, string> passwords)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(passwords);
            if (passwords.Count == 0)
            {
                throw new InvalidOperationException("At least one XP password is required.");
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<PasswordHashEntry>(passwords.Count);
            foreach (var pair in passwords)
            {
                var name = pair.Key?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException("XP passwords contain a blank PC name.");
                }

                if (!names.Add(name))
                {
                    throw new InvalidOperationException($"XP passwords contain duplicate PC name '{name}'.");
                }

                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    throw new InvalidOperationException($"XP passwords contain a blank password for '{name}'.");
                }

                var salt = RandomNumberGenerator.GetBytes(SaltSize);
                var hash = Rfc2898DeriveBytes.Pbkdf2(
                    pair.Value,
                    salt,
                    MinimumIterations,
                    HashAlgorithmName.SHA256,
                    HashSize);
                entries.Add(new PasswordHashEntry
                {
                    Name = name,
                    CanonicalId = CreateGeneratedCanonicalId(name),
                    Algorithm = Algorithm,
                    Iterations = MinimumIterations,
                    Salt = Convert.ToBase64String(salt),
                    Hash = Convert.ToBase64String(hash)
                });
                CryptographicOperations.ZeroMemory(hash);
            }

            var document = new PasswordHashDocument
            {
                SchemaVersion = SchemaVersion,
                Format = Format,
                Entries = entries
            };
            AtomicFileUtility.WriteAllText(
                Path.GetFullPath(path),
                JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine);
        }

        internal static int ConvertEncryptedSidecarToPasswordHashes(
            string sourcePath,
            string? destinationPath = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

            var resolvedSourcePath = Path.GetFullPath(sourcePath);
            var resolvedDestinationPath = Path.GetFullPath(destinationPath ?? sourcePath);
            AssertLegacyEncryptedEnvelope(resolvedSourcePath);
            var passwords = LocalSettingsUtility.LoadSettingsWithoutMigration(resolvedSourcePath);
            SavePasswordHashes(resolvedDestinationPath, passwords);
            return passwords.Count;
        }

        private static bool VerifyPassword(string password, PasswordHashRecord expectedHash)
        {
            var candidateHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                expectedHash.Salt,
                expectedHash.Iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Hash.Length);
            try
            {
                return CryptographicOperations.FixedTimeEquals(candidateHash, expectedHash.Hash);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(candidateHash);
            }
        }

        private static void AssertLegacyEncryptedEnvelope(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("format", out var formatElement)
                || formatElement.ValueKind != JsonValueKind.String
                || formatElement.GetString() is not ("app-protected-v1" or "app-protected-v2" or "app-protected-v3"))
            {
                throw new InvalidOperationException(
                    $"{FileName} migration requires a legacy authenticated app-protected envelope.");
            }
        }

        private static byte[] DecodeBase64(string? value, string fieldName, string entryName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"{FileName} entry '{entryName}' has an empty {fieldName}.");
            }

            try
            {
                return Convert.FromBase64String(value);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"{FileName} entry '{entryName}' has an invalid base64 {fieldName}.",
                    ex);
            }
        }


        private static bool IsValidCanonicalId(string value)
        {
            return value.Length is >= 3 and <= 128
                && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
        }

        private static string CreateGeneratedCanonicalId(string name)
        {
            var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name.Trim().ToUpperInvariant()));
            return $"generated-{Convert.ToHexString(digest).ToLowerInvariant()}";
        }

        internal sealed record PasswordHashRecord(
            string CanonicalId,
            string Name,
            int Iterations,
            byte[] Salt,
            byte[] Hash)
        {
            internal XpAuthenticatedIdentity ToAuthenticatedIdentity() =>
                new(
                    CanonicalId,
                    Name,
                    [],
                    string.Equals(Name, "Dungeon Master", StringComparison.OrdinalIgnoreCase),
                    CanonicalId);
        }

        private sealed class PasswordHashDocument
        {
            [JsonPropertyName("schema_version")]
            public int SchemaVersion { get; init; }

            [JsonPropertyName("format")]
            public string? Format { get; init; }

            [JsonPropertyName("entries")]
            public List<PasswordHashEntry>? Entries { get; init; }
        }

        private sealed class PasswordHashEntry
        {
            [JsonPropertyName("canonical_id")]
            public string? CanonicalId { get; init; }

            [JsonPropertyName("name")]
            public string? Name { get; init; }

            [JsonPropertyName("algorithm")]
            public string? Algorithm { get; init; }

            [JsonPropertyName("iterations")]
            public int Iterations { get; init; }

            [JsonPropertyName("salt")]
            public string? Salt { get; init; }

            [JsonPropertyName("hash")]
            public string? Hash { get; init; }
        }
    }
}
