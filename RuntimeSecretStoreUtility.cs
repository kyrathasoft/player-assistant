using System.IO.Compression;
using System.Text;

namespace PlayerAssistant
{
    internal static class RuntimeSecretStoreUtility
    {
        private const string RpolUserNameTarget = "PlayerAssistant/RPOL/UserName";
        private const string RpolPasswordTarget = "PlayerAssistant/RPOL/Password";
        private const string RpolStorageStateTarget = "PlayerAssistant/RPOL/StorageState";
        private const string BrokerTokenTarget = "PlayerAssistant/Broker/Token";
        private const string BrokerAdminKeyTarget = "PlayerAssistant/Broker/AdminKey";
        private const string SnapshotSigningKeyTarget = "PlayerAssistant/Broker/SnapshotSigningKey";
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

        public static string? GetBrokerToken()
        {
            return ReadSecret(BrokerTokenTarget);
        }

        public static string? GetBrokerAdminKey()
        {
            return ReadSecret(BrokerAdminKeyTarget);
        }

        public static string? GetSnapshotSigningKey()
        {
            return ReadSecret(SnapshotSigningKeyTarget);
        }

        public static void SaveBrokerToken(string token)
        {
            SaveSecret(BrokerTokenTarget, token, "Player Assistant broker client token");
        }

        public static void SaveSnapshotSigningKey(string signingKey)
        {
            SaveSecret(SnapshotSigningKeyTarget, signingKey, "Player Assistant snapshot signing key");
        }

        private static string? ReadSecret(string target)
        {
            return WindowsCredentialManagerUtility.TryReadSecretUtf8(target, out var value, out _)
                ? value
                : null;
        }

        private static void SaveSecret(string target, string value, string comment)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            WindowsCredentialManagerUtility.WriteSecretUtf8(target, value, comment);
        }

        public static bool TryMigrateRpolSecretsFromLocalSettings(
            IDictionary<string, string> localSettings,
            string localSettingsPath)
        {
            return TryMigrateRpolSecretsFromSettings(localSettings, localSettingsPath);
        }

        public static bool TryMigrateRpolSecretsFromSettings(
            IDictionary<string, string> settings,
            string? persistedSettingsPath = null)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var hasUserName = settings.TryGetValue(AppSettingsUtility.RpolUserNameSettingsKey, out var userName);
            var hasPassword = settings.TryGetValue(AppSettingsUtility.RpolPasswordSettingsKey, out var password);
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

            settings.Remove(AppSettingsUtility.RpolUserNameSettingsKey);
            settings.Remove(AppSettingsUtility.RpolPasswordSettingsKey);
            if (!string.IsNullOrWhiteSpace(persistedSettingsPath))
            {
                LocalSettingsUtility.SaveEncryptedSettings(
                    persistedSettingsPath,
                    new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase));
            }

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
