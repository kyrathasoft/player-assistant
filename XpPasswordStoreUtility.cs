using System.Security.Cryptography;
using System.Text;

namespace PlayerAssistant
{
    internal static class XpPasswordStoreUtility
    {
        public const string FileName = "xp-passwords.json";
        private const string DungeonMasterAccessName = "Dungeon Master";

        public static IReadOnlyDictionary<string, string> LoadPasswords(string? runtimeDirectory = null)
        {
            var resolvedRuntimeDirectory = string.IsNullOrWhiteSpace(runtimeDirectory)
                ? AppContext.BaseDirectory
                : runtimeDirectory;
            var sidecarPath = RuntimePathUtility.CombineUnderBase(resolvedRuntimeDirectory, FileName);
            if (!File.Exists(sidecarPath))
            {
                throw new FileNotFoundException(
                    $"Encrypted XP password sidecar '{FileName}' was not found at '{sidecarPath}'.",
                    sidecarPath);
            }

            var loadedPasswords = LocalSettingsUtility.LoadPortableEncryptedSettings(sidecarPath);
            var passwords = new Dictionary<string, string>(loadedPasswords, StringComparer.OrdinalIgnoreCase);

            if (passwords.Count == 0)
            {
                throw new InvalidOperationException($"{FileName} does not contain any PC password entries.");
            }

            foreach (var pair in passwords)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    throw new InvalidOperationException($"{FileName} contains a blank PC name.");
                }

                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    throw new InvalidOperationException($"{FileName} contains a blank password for '{pair.Key}'.");
                }
            }

            return passwords;
        }

        public static bool ValidatePassword(string pcName, string password, string? runtimeDirectory = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pcName);
            ArgumentNullException.ThrowIfNull(password);

            var passwords = LoadPasswords(runtimeDirectory);
            foreach (var candidateName in GetCandidateNames(pcName, passwords.Keys))
            {
                if (passwords.TryGetValue(candidateName, out var expectedPassword)
                    && FixedTimeEquals(password, expectedPassword))
                {
                    return true;
                }
            }

            return false;
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

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
    }
}
