using System.Security.Cryptography;
using System.Text;

namespace PlayerAssistant
{
    internal static class XpPasswordStoreUtility
    {
        public const string FileName = "xp-passwords.json";

        public static IReadOnlyDictionary<string, string> LoadPasswords(string? runtimeDirectory = null)
        {
            var resolvedRuntimeDirectory = string.IsNullOrWhiteSpace(runtimeDirectory)
                ? AppContext.BaseDirectory
                : runtimeDirectory;
            var sidecarPath = RuntimePathUtility.CombineUnderBase(resolvedRuntimeDirectory, FileName);
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
            if (!passwords.TryGetValue(pcName, out var expectedPassword))
            {
                return false;
            }

            return FixedTimeEquals(password, expectedPassword);
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
    }
}
