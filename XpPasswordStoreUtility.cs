using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant
{
    internal static class XpPasswordStoreUtility
    {
        public const string FileName = "xp-passwords.json";
        public const string Format = "xp-password-hashes-v2";
        public const string Algorithm = "PBKDF2-HMAC-SHA256";
        public const int SchemaVersion = 2;
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

            var canonicalNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in document.Entries)
            {
                var canonicalName = ValidateCanonicalName(entry.CanonicalName);
                if (!canonicalNames.Add(NormalizeIdentityKey(canonicalName)))
                {
                    throw new InvalidOperationException($"{FileName} contains duplicate canonical name '{canonicalName}'.");
                }
            }

            var hashes = new Dictionary<string, PasswordHashRecord>(StringComparer.OrdinalIgnoreCase);
            var salts = new HashSet<string>(StringComparer.Ordinal);
            var allAliases = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in document.Entries)
            {
                var canonicalName = ValidateCanonicalName(entry.CanonicalName);
                if (string.IsNullOrWhiteSpace(entry.CanonicalId)
                    || !string.Equals(entry.CanonicalId, entry.CanonicalId.Trim(), StringComparison.Ordinal)
                    || !IsValidCanonicalId(entry.CanonicalId))
                {
                    throw new InvalidOperationException($"{FileName} entry '{canonicalName}' has an invalid canonical ID.");
                }

                if (!string.Equals(entry.Algorithm, Algorithm, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{FileName} entry '{canonicalName}' must use algorithm '{Algorithm}'.");
                }

                if (entry.Iterations < MinimumIterations)
                {
                    throw new InvalidOperationException(
                        $"{FileName} entry '{canonicalName}' must use at least {MinimumIterations:N0} iterations.");
                }

                var salt = DecodeBase64(entry.Salt, "salt", canonicalName);
                var hash = DecodeBase64(entry.Hash, "hash", canonicalName);
                if (salt.Length < SaltSize)
                {
                    throw new InvalidOperationException(
                        $"{FileName} entry '{canonicalName}' must use a salt of at least {SaltSize} bytes.");
                }

                if (hash.Length != HashSize)
                {
                    throw new InvalidOperationException(
                        $"{FileName} entry '{canonicalName}' must use a {HashSize}-byte hash.");
                }

                if (!salts.Add(entry.Salt!))
                {
                    throw new InvalidOperationException($"{FileName} contains a reused password salt.");
                }

                var aliases = ValidateAliases(entry.Aliases, canonicalName, canonicalNames, allAliases);
                if (!hashes.TryAdd(entry.CanonicalId, new PasswordHashRecord(
                    entry.CanonicalId,
                    canonicalName,
                    aliases,
                    entry.Iterations,
                    salt,
                    hash)))
                {
                    throw new InvalidOperationException($"{FileName} contains duplicate canonical ID '{entry.CanonicalId}'.");
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
                    || !expectedHash.MatchesName(NormalizeIdentityKey(displayName)))
                {
                    return null;
                }

                return VerifyPassword(password, expectedHash)
                    ? expectedHash.ToAuthenticatedIdentity()
                    : null;
            }

            var normalizedDisplayName = NormalizeIdentityKey(displayName);
            var nameMatches = hashes.Values
                .Where(record => record.MatchesName(normalizedDisplayName))
                .Take(2)
                .ToArray();
            var nameMatch = nameMatches.Length == 1 ? nameMatches[0] : null;
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
            var identities = passwords.Select(pair => new PasswordIdentityInput(
                CreateGeneratedCanonicalId(pair.Key),
                pair.Key,
                pair.Value,
                [])).ToArray();
            SavePasswordHashes(path, identities);
        }

        internal static void SavePasswordHashes(
            string path,
            IReadOnlyList<PasswordIdentityInput> identities)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(identities);
            if (identities.Count == 0)
            {
                throw new InvalidOperationException("At least one XP password is required.");
            }

            var entries = new List<PasswordHashEntry>(identities.Count);
            foreach (var identity in identities)
            {
                var name = ValidateCanonicalName(identity.CanonicalName);
                if (!IsValidCanonicalId(identity.CanonicalId)) throw new InvalidOperationException($"XP passwords contain invalid canonical ID '{identity.CanonicalId}'.");
                if (string.IsNullOrEmpty(identity.Password)) throw new InvalidOperationException($"XP passwords contain a blank password for '{name}'.");

                var salt = RandomNumberGenerator.GetBytes(SaltSize);
                var hash = Rfc2898DeriveBytes.Pbkdf2(
                    identity.Password,
                    salt,
                    MinimumIterations,
                    HashAlgorithmName.SHA256,
                    HashSize);
                entries.Add(new PasswordHashEntry
                {
                    CanonicalName = name,
                    CanonicalId = identity.CanonicalId,
                    Aliases = identity.Aliases.ToList(),
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

        internal static string NormalizeIdentityKey(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .ToUpperInvariant();
        }

        private static string ValidateCanonicalName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{FileName} contains a blank or untrimmed canonical name.");
            }

            return value;
        }

        private static IReadOnlyList<string> ValidateAliases(
            IReadOnlyList<string>? aliases,
            string canonicalName,
            IReadOnlySet<string> canonicalNames,
            ISet<string> allAliases)
        {
            if (aliases is null)
            {
                throw new InvalidOperationException($"{FileName} entry '{canonicalName}' must declare an aliases array.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var validated = new List<string>(aliases.Count);
            foreach (var alias in aliases)
            {
                if (string.IsNullOrWhiteSpace(alias)
                    || !string.Equals(alias, alias.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{FileName} entry '{canonicalName}' contains a blank or untrimmed alias.");
                }

                var normalizedAlias = NormalizeIdentityKey(alias);
                if (canonicalNames.Contains(normalizedAlias)
                    || !seen.Add(normalizedAlias)
                    || !allAliases.Add(normalizedAlias))
                {
                    throw new InvalidOperationException($"{FileName} entry '{canonicalName}' contains a duplicate or colliding alias '{alias}'.");
                }

                validated.Add(alias);
            }

            return validated;
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
            string CanonicalName,
            IReadOnlyList<string> Aliases,
            int Iterations,
            byte[] Salt,
            byte[] Hash)
        {
            internal bool MatchesName(string normalizedName) =>
                string.Equals(NormalizeIdentityKey(CanonicalName), normalizedName, StringComparison.Ordinal)
                || Aliases.Any(alias => string.Equals(NormalizeIdentityKey(alias), normalizedName, StringComparison.Ordinal));

            internal XpAuthenticatedIdentity ToAuthenticatedIdentity() =>
                new(
                    CanonicalId,
                    CanonicalName,
                    Aliases,
                    string.Equals(CanonicalName, "Dungeon Master", StringComparison.OrdinalIgnoreCase),
                    CanonicalId);
        }

        internal sealed record PasswordIdentityInput(
            string CanonicalId,
            string CanonicalName,
            string Password,
            IReadOnlyList<string> Aliases);

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

            [JsonPropertyName("canonical_name")]
            public string? CanonicalName { get; init; }

            [JsonPropertyName("aliases")]
            public List<string>? Aliases { get; init; }

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
