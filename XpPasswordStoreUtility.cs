using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant
{
    internal sealed record XpAuthenticatedIdentity(
        string AccountId,
        string CanonicalCharacterName,
        IReadOnlyList<string> Aliases,
        bool IsDungeonMaster)
    {
        public bool IsValid => !string.IsNullOrWhiteSpace(AccountId);
        public static XpAuthenticatedIdentity Invalid { get; } = new("", "", [], false);
    }

    internal sealed record XpPasswordIdentityDefinition(
        string AccountId,
        string CanonicalCharacterName,
        IReadOnlyList<string> Aliases,
        string Password,
        bool IsDungeonMaster = false);

    internal static class XpPasswordStoreUtility
    {
        public const string FileName = "xp-passwords.json";
        public const string Format = "xp-password-hashes-v1";
        public const string IdentityFormat = "xp-password-hashes-v2";
        public const string Algorithm = "PBKDF2-HMAC-SHA256";
        public const int SchemaVersion = 1;
        public const int MinimumIterations = 600_000;

        private const string DungeonMasterAccessName = "Dungeon Master";
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

                if (!hashes.TryAdd(entry.Name, new PasswordHashRecord(entry.Iterations, salt, hash)))
                {
                    throw new InvalidOperationException($"{FileName} contains duplicate PC name '{entry.Name}'.");
                }
            }

            return hashes;
        }

        public static bool ValidatePassword(string pcName, string password, string? runtimeDirectory = null)
        {
            return AuthenticatePassword(pcName, password, runtimeDirectory).IsValid;
        }

        internal static XpAuthenticatedIdentity AuthenticatePassword(
            string enteredName,
            string password,
            string? runtimeDirectory = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(enteredName);
            ArgumentNullException.ThrowIfNull(password);
            var resolvedRuntimeDirectory = string.IsNullOrWhiteSpace(runtimeDirectory)
                ? AppContext.BaseDirectory
                : runtimeDirectory;
            var sidecarPath = RuntimePathUtility.CombineUnderBase(resolvedRuntimeDirectory, FileName);
            using var document = JsonDocument.Parse(File.ReadAllText(sidecarPath));
            var format = document.RootElement.TryGetProperty("format", out var formatElement)
                ? formatElement.GetString()
                : null;
            if (string.Equals(format, IdentityFormat, StringComparison.Ordinal))
            {
                return AuthenticateIdentityDocument(document.RootElement, enteredName.Trim(), password);
            }

            var hashes = LoadPasswordHashes(resolvedRuntimeDirectory);
            var exactName = enteredName.Trim();
            return hashes.TryGetValue(exactName, out var expectedHash) && VerifyPassword(password, expectedHash)
                ? new XpAuthenticatedIdentity(IdentityKey(exactName), exactName, [], string.Equals(exactName, DungeonMasterAccessName, StringComparison.OrdinalIgnoreCase))
                : XpAuthenticatedIdentity.Invalid;
        }

        internal static void SavePasswordIdentities(string path, IReadOnlyList<XpPasswordIdentityDefinition> identities)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(identities);
            if (identities.Count == 0) throw new InvalidOperationException("At least one XP password identity is required.");
            var canonicalIds = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(identities.Select(identity => identity.CanonicalCharacterName?.Trim() ?? ""), StringComparer.OrdinalIgnoreCase);
            if (names.Any(string.IsNullOrWhiteSpace) || names.Count != identities.Count)
                throw new InvalidOperationException("XP identities contain a blank or duplicate canonical name.");
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<PasswordIdentityEntry>(identities.Count);
            foreach (var identity in identities)
            {
                var accountId = identity.AccountId?.Trim();
                var name = identity.CanonicalCharacterName?.Trim();
                if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(name) || !canonicalIds.Add(accountId))
                    throw new InvalidOperationException("XP identities contain a blank or duplicate canonical account ID.");
                if ((identity.Aliases ?? []).Any(alias => string.IsNullOrWhiteSpace(alias) || !string.Equals(alias, alias.Trim(), StringComparison.Ordinal)))
                    throw new InvalidOperationException($"XP identity '{name}' contains a blank or untrimmed alias.");
                var normalizedAliases = (identity.Aliases ?? [])
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Select(alias => alias.Trim())
                    .ToArray();
                foreach (var alias in normalizedAliases)
                {
                    if (string.Equals(alias, name, StringComparison.OrdinalIgnoreCase) || names.Contains(alias) || !aliases.TryAdd(alias, accountId))
                        throw new InvalidOperationException($"XP identities contain an ambiguous alias '{alias}'.");
                }
                if (string.IsNullOrWhiteSpace(identity.Password)) throw new InvalidOperationException($"XP identity '{name}' has a blank password.");
                var salt = RandomNumberGenerator.GetBytes(SaltSize);
                var hash = Rfc2898DeriveBytes.Pbkdf2(identity.Password, salt, MinimumIterations, HashAlgorithmName.SHA256, HashSize);
                entries.Add(new PasswordIdentityEntry
                {
                    AccountId = accountId, Name = name, Aliases = normalizedAliases,
                    IsDungeonMaster = identity.IsDungeonMaster, Algorithm = Algorithm,
                    Iterations = MinimumIterations, Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(hash)
                });
                CryptographicOperations.ZeroMemory(hash);
            }
            var serialized = JsonSerializer.Serialize(new PasswordIdentityDocument { SchemaVersion = 2, Format = IdentityFormat, Entries = entries }, JsonOptions) + Environment.NewLine;
            AtomicFileUtility.WriteAllText(Path.GetFullPath(path), serialized);
        }

        private static XpAuthenticatedIdentity AuthenticateIdentityDocument(JsonElement root, string enteredName, string password)
        {
            if (!root.TryGetProperty("schema_version", out var schema)
                || schema.GetInt32() != 2
                || !root.TryGetProperty("format", out var format)
                || !string.Equals(format.GetString(), IdentityFormat, StringComparison.Ordinal)
                || !root.TryGetProperty("entries", out var entries)
                || entries.ValueKind != JsonValueKind.Array
                || entries.GetArrayLength() == 0)
            {
                throw new InvalidOperationException($"{FileName} must declare the validated identity schema '{IdentityFormat}'.");
            }

            var accountIds = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var salts = new HashSet<string>(StringComparer.Ordinal);
            var matches = new List<(JsonElement Entry, string Name, string[] Aliases)>();
            foreach (var entry in entries.EnumerateArray())
            {
                var accountId = entry.GetProperty("account_id").GetString();
                var name = entry.GetProperty("canonical_name").GetString();
                if (string.IsNullOrWhiteSpace(accountId) || !string.Equals(accountId, accountId.Trim(), StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(name) || !string.Equals(name, name.Trim(), StringComparison.Ordinal)
                    || !accountIds.Add(accountId!) || !names.Add(name!))
                {
                    throw new InvalidOperationException($"{FileName} contains a blank, untrimmed, or duplicate canonical identity.");
                }

                var entryAliases = entry.TryGetProperty("aliases", out var aliasElement)
                    && aliasElement.ValueKind == JsonValueKind.Array
                    ? aliasElement.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray()
                    : [];
                var entryAliasSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var alias in entryAliases)
                {
                    if (string.IsNullOrWhiteSpace(alias)
                        || !string.Equals(alias, alias.Trim(), StringComparison.Ordinal)
                        || !entryAliasSet.Add(alias)
                        || names.Contains(alias)
                        || !aliases.Add(alias))
                    {
                        throw new InvalidOperationException($"{FileName} contains an ambiguous alias.");
                    }
                }

                var algorithm = entry.GetProperty("algorithm").GetString();
                var iterations = entry.GetProperty("iterations").GetInt32();
                var salt = entry.GetProperty("salt").GetString();
                var hash = entry.GetProperty("hash").GetString();
                if (!string.Equals(algorithm, Algorithm, StringComparison.Ordinal)
                    || iterations < MinimumIterations
                    || string.IsNullOrWhiteSpace(salt)
                    || string.IsNullOrWhiteSpace(hash)
                    || !salts.Add(salt!))
                {
                    throw new InvalidOperationException($"{FileName} contains an invalid password hash entry.");
                }
                if (DecodeBase64(salt, "salt", name!).Length < SaltSize
                    || DecodeBase64(hash, "hash", name!).Length != HashSize)
                {
                    throw new InvalidOperationException($"{FileName} contains an invalid password hash entry.");
                }

                matches.Add((entry, name!, entryAliases));
            }

            // Canonical names must also reserve the alias namespace, regardless of entry order.
            if (matches.Any(match => match.Aliases.Any(alias => names.Contains(alias))))
            {
                throw new InvalidOperationException($"{FileName} contains an alias matching another canonical name.");
            }

            var match = matches.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, enteredName, StringComparison.OrdinalIgnoreCase)
                || candidate.Aliases.Any(alias => string.Equals(alias, enteredName, StringComparison.OrdinalIgnoreCase)));
            if (match.Entry.ValueKind == JsonValueKind.Undefined)
            {
                return XpAuthenticatedIdentity.Invalid;
            }

            var record = new PasswordHashRecord(
                match.Entry.GetProperty("iterations").GetInt32(),
                DecodeBase64(match.Entry.GetProperty("salt").GetString(), "salt", match.Name),
                DecodeBase64(match.Entry.GetProperty("hash").GetString(), "hash", match.Name));
            if (!VerifyPassword(password, record))
            {
                return XpAuthenticatedIdentity.Invalid;
            }

            return new XpAuthenticatedIdentity(
                match.Entry.GetProperty("account_id").GetString() ?? string.Empty,
                match.Name,
                match.Aliases,
                match.Entry.TryGetProperty("is_dungeon_master", out var dm) && dm.GetBoolean());
        }

        private static string IdentityKey(string value)
        {
            var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant()));
            return Convert.ToHexString(bytes[..16]).ToLowerInvariant();
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
            SavePasswordIdentities(
                resolvedDestinationPath,
                passwords.Select(pair => new XpPasswordIdentityDefinition(
                    IdentityKey(pair.Key),
                    pair.Key.Trim(),
                    [],
                    pair.Value,
                    string.Equals(pair.Key.Trim(), DungeonMasterAccessName, StringComparison.OrdinalIgnoreCase))).ToArray());
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

        private static IEnumerable<string> GetCandidateNames(
            string pcName,
            IEnumerable<string> storedNames)
        {
            var trimmedName = pcName.Trim();
            yield return trimmedName;

            if (string.Equals(trimmedName, DungeonMasterAccessName, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            var firstName = GetFirstName(trimmedName);
            if (!string.Equals(firstName, trimmedName, StringComparison.OrdinalIgnoreCase))
            {
                yield return firstName;
            }

            foreach (var storedName in storedNames)
            {
                if (string.Equals(storedName, DungeonMasterAccessName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(GetFirstName(storedName), firstName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return storedName;
                }
            }
        }

        private static string GetFirstName(string value)
        {
            var trimmedValue = value.Trim();
            var spaceIndex = trimmedValue.IndexOf(' ');
            return spaceIndex < 0
                ? trimmedValue
                : trimmedValue[..spaceIndex];
        }

        internal sealed record PasswordHashRecord(int Iterations, byte[] Salt, byte[] Hash);

        private sealed class PasswordIdentityDocument
        {
            [JsonPropertyName("schema_version")] public int SchemaVersion { get; init; }
            [JsonPropertyName("format")] public string? Format { get; init; }
            [JsonPropertyName("entries")] public List<PasswordIdentityEntry>? Entries { get; init; }
        }

        private sealed class PasswordIdentityEntry
        {
            [JsonPropertyName("account_id")] public string? AccountId { get; init; }
            [JsonPropertyName("canonical_name")] public string? Name { get; init; }
            [JsonPropertyName("aliases")] public IReadOnlyList<string> Aliases { get; init; } = [];
            [JsonPropertyName("is_dungeon_master")] public bool IsDungeonMaster { get; init; }
            [JsonPropertyName("algorithm")] public string? Algorithm { get; init; }
            [JsonPropertyName("iterations")] public int Iterations { get; init; }
            [JsonPropertyName("salt")] public string? Salt { get; init; }
            [JsonPropertyName("hash")] public string? Hash { get; init; }
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
