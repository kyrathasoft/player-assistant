using System.IO.Compression;
using System.Text;

namespace PlayerAssistant
{
    internal static class RuntimeSecretStoreUtility
    {
        private const string RpolUserNameTarget = "PlayerAssistant/RPOL/UserName";
        private const string RpolPasswordTarget = "PlayerAssistant/RPOL/Password";
        private const string RpolStorageStateTarget = "PlayerAssistant/RPOL/StorageState";
        private const string RpolComment = "Player Assistant RPOL credential";
        private const string RpolStorageStateComment = "Player Assistant RPOL browser storage state";

        public static bool TryGetRpolCredentials(out string? userName, out string? password)
        {
            var hasUserName = WindowsCredentialManagerUtility.TryReadSecretUtf8(RpolUserNameTarget, out userName, out _);
            var hasPassword = WindowsCredentialManagerUtility.TryReadSecretUtf8(RpolPasswordTarget, out password, out _);

            if (!hasUserName)
            {
                userName = null;
            }

            if (!hasPassword)
            {
                password = null;
            }

            return hasUserName && hasPassword;
        }

        public static string? GetRpolUserName()
        {
            return WindowsCredentialManagerUtility.TryReadSecretUtf8(RpolUserNameTarget, out var userName, out _)
                ? userName
                : null;
        }

        public static string? GetRpolPassword()
        {
            return WindowsCredentialManagerUtility.TryReadSecretUtf8(RpolPasswordTarget, out var password, out _)
                ? password
                : null;
        }

        public static void SaveRpolCredentials(string userName, string password)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userName);
            ArgumentException.ThrowIfNullOrWhiteSpace(password);

            WindowsCredentialManagerUtility.WriteSecretUtf8(RpolUserNameTarget, userName.Trim(), RpolComment);
            WindowsCredentialManagerUtility.WriteSecretUtf8(RpolPasswordTarget, password, RpolComment);
        }

        public static void DeleteRpolCredentials()
        {
            WindowsCredentialManagerUtility.DeleteSecret(RpolUserNameTarget);
            WindowsCredentialManagerUtility.DeleteSecret(RpolPasswordTarget);
        }

        public static bool TryGetRpolStorageState(out string? storageStateJson, out DateTimeOffset lastWritten)
        {
            if (!WindowsCredentialManagerUtility.TryReadSecret(RpolStorageStateTarget, out var compressedBytes, out lastWritten))
            {
                storageStateJson = null;
                return false;
            }

            storageStateJson = DecompressUtf8(compressedBytes);
            return true;
        }

        public static void SaveRpolStorageState(string storageStateJson)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storageStateJson);
            WindowsCredentialManagerUtility.WriteSecret(
                RpolStorageStateTarget,
                CompressUtf8(storageStateJson),
                RpolStorageStateComment);
        }

        public static void DeleteRpolStorageState()
        {
            WindowsCredentialManagerUtility.DeleteSecret(RpolStorageStateTarget);
        }

        public static bool TryMigrateRpolSecretsFromLocalSettings(
            IDictionary<string, string> localSettings,
            string localSettingsPath)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            ArgumentException.ThrowIfNullOrWhiteSpace(localSettingsPath);

            var hasUserName = localSettings.TryGetValue(AppSettingsUtility.RpolUserNameSettingsKey, out var userName);
            var hasPassword = localSettings.TryGetValue(AppSettingsUtility.RpolPasswordSettingsKey, out var password);
            if (!hasUserName && !hasPassword)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(userName))
            {
                WindowsCredentialManagerUtility.WriteSecretUtf8(RpolUserNameTarget, userName.Trim(), RpolComment);
            }
            else
            {
                WindowsCredentialManagerUtility.DeleteSecret(RpolUserNameTarget);
            }

            if (!string.IsNullOrWhiteSpace(password))
            {
                WindowsCredentialManagerUtility.WriteSecretUtf8(RpolPasswordTarget, password, RpolComment);
            }
            else
            {
                WindowsCredentialManagerUtility.DeleteSecret(RpolPasswordTarget);
            }

            localSettings.Remove(AppSettingsUtility.RpolUserNameSettingsKey);
            localSettings.Remove(AppSettingsUtility.RpolPasswordSettingsKey);
            LocalSettingsUtility.SaveEncryptedSettings(
                localSettingsPath,
                new Dictionary<string, string>(localSettings, StringComparer.OrdinalIgnoreCase));
            return true;
        }

        internal static IDisposable UseBackendForTests(IWindowsCredentialStoreBackend backend)
        {
            return WindowsCredentialManagerUtility.PushBackendForTests(backend);
        }

        private static byte[] CompressUtf8(string value)
        {
            var plaintextBytes = Encoding.UTF8.GetBytes(value);
            using var destination = new MemoryStream();
            using (var gzipStream = new GZipStream(destination, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzipStream.Write(plaintextBytes, 0, plaintextBytes.Length);
            }

            return destination.ToArray();
        }

        private static string DecompressUtf8(byte[] compressedBytes)
        {
            using var source = new MemoryStream(compressedBytes);
            using var gzipStream = new GZipStream(source, CompressionMode.Decompress);
            using var destination = new MemoryStream();
            gzipStream.CopyTo(destination);
            return Encoding.UTF8.GetString(destination.ToArray());
        }
    }
}
