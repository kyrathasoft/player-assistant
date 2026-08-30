using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant
{
    internal static class RuntimeSecretStoreUtility
    {
        private const string RpolUserNameTarget = "PlayerAssistant/RPOL/UserName";
        private const string RpolPasswordTarget = "PlayerAssistant/RPOL/Password";
        private const string RpolCredentialRecordTarget = "PlayerAssistant/RPOL/Credentials";
        private const string RpolStorageStateTarget = "PlayerAssistant/RPOL/StorageState";
        private const string RpolStorageStateCandidateTarget = "PlayerAssistant/RPOL/StorageStateCandidate";
        private const string RpolStorageStateSlotATarget = "PlayerAssistant/RPOL/StorageStateActiveA";
        private const string RpolStorageStateSlotBTarget = "PlayerAssistant/RPOL/StorageStateActiveB";
        private const string RpolStorageStatePointerTarget = "PlayerAssistant/RPOL/StorageStateActivePointer";
        private const string BrokerTokenTarget = "PlayerAssistant/Broker/Token";
        private const string BrokerAdminKeyTarget = "PlayerAssistant/Broker/AdminKey";
        private const string SnapshotSigningKeyTarget = "PlayerAssistant/Broker/SnapshotSigningKey";
        private const string RpolComment = "Player Assistant RPOL credential";
        private const string RpolStorageStateComment = "Player Assistant RPOL browser storage state";

        public static bool TryGetRpolCredentials(out string? userName, out string? password)
        {
            if (WindowsCredentialManagerUtility.TryReadSecret(RpolCredentialRecordTarget, out var recordBytes, out _))
            {
                try
                {
                    var record = JsonSerializer.Deserialize<RpolCredentialRecord>(recordBytes);
                    if (record?.Version == 1 && !string.IsNullOrWhiteSpace(record.UserName) && !string.IsNullOrWhiteSpace(record.Password))
                    { userName = record.UserName; password = record.Password; return true; }
                }
                catch (JsonException) { }
            }
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
            return TryGetRpolCredentials(out var userName, out _) ? userName : null;
        }

        public static string? GetRpolPassword()
        {
            return TryGetRpolCredentials(out _, out var password) ? password : null;
        }

        public static void SaveRpolCredentials(string userName, string password)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userName);
            ArgumentException.ThrowIfNullOrWhiteSpace(password);

            var recordBytes = JsonSerializer.SerializeToUtf8Bytes(new RpolCredentialRecord(1, userName.Trim(), password));
            try { WindowsCredentialManagerUtility.WriteSecret(RpolCredentialRecordTarget, recordBytes, RpolComment + " versioned"); }
            finally { Array.Clear(recordBytes, 0, recordBytes.Length); }
        }

        public static void DeleteRpolCredentials()
        {
            WindowsCredentialManagerUtility.DeleteSecret(RpolCredentialRecordTarget);
            WindowsCredentialManagerUtility.DeleteSecret(RpolUserNameTarget);
            WindowsCredentialManagerUtility.DeleteSecret(RpolPasswordTarget);
        }

        public static bool TryGetRpolStorageState(out string? storageStateJson, out DateTimeOffset lastWritten)
        {
            if (WindowsCredentialManagerUtility.TryReadSecret(RpolStorageStatePointerTarget, out _, out _)
                && !TryGetActivePointer(out _, out _))
            {
                storageStateJson = null;
                lastWritten = DateTimeOffset.MinValue;
                return false;
            }
            if (TryGetActivePointer(out var pointer, out lastWritten))
            {
                if (RpolVersionedStateTransaction.TryReadNormalActiveState(
                        pointer!,
                        ReadActiveSlot,
                        out storageStateJson))
                {
                    return true;
                }

                storageStateJson = null;
                return false;
            }

            if (!WindowsCredentialManagerUtility.TryReadSecret(RpolStorageStateTarget, out var compressedBytes, out lastWritten))
            {
                storageStateJson = null;
                return false;
            }

            storageStateJson = DecompressUtf8(compressedBytes);
            return !string.IsNullOrWhiteSpace(storageStateJson);
        }

        public static void SaveRpolStorageState(string storageStateJson)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storageStateJson);
            var nextVersion = 1;
            var nextSlot = "A";
            if (TryGetActivePointer(out var current, out _)
                && current is not null)
            {
                nextVersion = checked(current.Version + 1);
                nextSlot = string.Equals(current.ActiveSlot, "A", StringComparison.Ordinal) ? "B" : "A";
            }
            WriteActiveSlot(nextSlot, storageStateJson);
            WriteActivePointer(new RpolActiveStatePointer(
                nextVersion,
                nextSlot,
                TryGetActivePointer(out var previous, out _) ? previous?.ActiveSlot : null,
                Verified: true,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(storageStateJson)))));
            WindowsCredentialManagerUtility.DeleteSecret(RpolStorageStateTarget);
        }

        internal static void SaveRpolStorageStateCandidate(string storageStateJson)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storageStateJson);
            WindowsCredentialManagerUtility.WriteSecret(
                RpolStorageStateCandidateTarget,
                CompressUtf8(storageStateJson),
                RpolStorageStateComment + " candidate");
        }

        internal static bool TryGetRpolStorageStateCandidate(out string? storageStateJson)
        {
            if (!WindowsCredentialManagerUtility.TryReadSecret(RpolStorageStateCandidateTarget, out var compressedBytes, out _))
            {
                storageStateJson = null;
                return false;
            }

            storageStateJson = DecompressUtf8(compressedBytes);
            return true;
        }

        internal static bool PromoteRpolStorageStateCandidate(out string? error)
        {
            error = null;
            if (!TryGetRpolStorageStateCandidate(out var candidate) || string.IsNullOrWhiteSpace(candidate))
            {
                error = "The RPOL candidate state is missing.";
                return false;
            }

            var pointerArtifactExists = WindowsCredentialManagerUtility.TryReadSecret(RpolStorageStatePointerTarget, out _, out _);
            if (!TryGetActivePointer(out var pointer, out _))
            {
                if (pointerArtifactExists)
                {
                    error = "The RPOL active state pointer is malformed; promotion is blocked.";
                    return false;
                }
                if (WindowsCredentialManagerUtility.TryReadSecret(RpolStorageStateTarget, out var legacyBytes, out _))
                {
                    SaveRpolStorageState(DecompressUtf8(legacyBytes));
                    TryGetActivePointer(out pointer, out _);
                }
            }

            var promoted = RpolVersionedStateTransaction.TryPromote(
                candidate,
                () => TryGetActivePointer(out var current, out _) ? current : null,
                ReadActiveSlot,
                WriteActiveSlot,
                WriteActivePointer,
                out _,
                out error,
                clearPointer: () => WindowsCredentialManagerUtility.DeleteSecret(RpolStorageStatePointerTarget));
            if (promoted)
            {
                return true;
            }
            error ??= "The RPOL active state candidate could not be promoted.";
            return false;
        }


        internal static RpolActiveStatePointer? CaptureRpolActiveStatePointer()
        {
            return TryGetActivePointer(out var pointer, out _) ? pointer : null;
        }

        internal static void RestoreRpolActiveStatePointer(RpolActiveStatePointer? pointer)
        {
            if (pointer is null)
            {
                WindowsCredentialManagerUtility.DeleteSecret(RpolStorageStatePointerTarget);
                return;
            }
            WriteActivePointer(pointer);
        }

        internal static bool VerifyRpolActiveStateRestored(
            RpolActiveStatePointer? expectedPointer,
            string? expectedState,
            out string reason)
        {
            var actualPointer = TryGetActivePointer(out var pointer, out _) ? pointer : null;
            if (!Equals(actualPointer, expectedPointer))
            {
                reason = "The restored RPOL active pointer did not match the captured prior pointer.";
                return false;
            }

            var hasState = TryGetRpolStorageState(out var actualState, out _);
            if (expectedState is null ? hasState : !hasState || !string.Equals(actualState, expectedState, StringComparison.Ordinal))
            {
                reason = "The restored RPOL state could not be proven through the normal active credential-store loader.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal static void MarkRpolStorageStateVerified()
        {
            if (!TryGetActivePointer(out var pointer, out _) || pointer is null)
            {
                throw new InvalidOperationException("The RPOL active state pointer is missing.");
            }

            RpolVersionedStateTransaction.MarkVerified(pointer, () =>
                TryGetActivePointer(out var current, out _) ? current : null, WriteActivePointer);
        }

        private static bool TryGetActivePointer(out RpolActiveStatePointer? pointer, out DateTimeOffset lastWritten)
        {
            pointer = null;
            if (!WindowsCredentialManagerUtility.TryReadSecret(RpolStorageStatePointerTarget, out var bytes, out lastWritten))
            {
                return false;
            }

            try
            {
                pointer = JsonSerializer.Deserialize<RpolActiveStatePointer>(Encoding.UTF8.GetString(bytes));
                return pointer is not null
                    && pointer.Version > 0
                    && pointer.ActiveSlot is "A" or "B"
                    && !string.IsNullOrWhiteSpace(pointer.ContentHash);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string? ReadActiveSlot(string slot)
        {
            var target = string.Equals(slot, "A", StringComparison.Ordinal)
                ? RpolStorageStateSlotATarget
                : RpolStorageStateSlotBTarget;
            return WindowsCredentialManagerUtility.TryReadSecret(target, out var bytes, out _)
                ? DecompressUtf8(bytes)
                : null;
        }

        private static void WriteActiveSlot(string slot, string state)
        {
            var target = string.Equals(slot, "A", StringComparison.Ordinal)
                ? RpolStorageStateSlotATarget
                : RpolStorageStateSlotBTarget;
            WindowsCredentialManagerUtility.WriteSecret(target, CompressUtf8(state), RpolStorageStateComment + " versioned");
        }

        private static void WriteActivePointer(RpolActiveStatePointer pointer)
        {
            WindowsCredentialManagerUtility.WriteSecret(
                RpolStorageStatePointerTarget,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(pointer)),
                RpolStorageStateComment + " pointer");
        }

        internal static void DeleteRpolStorageStateCandidate()
        {
            WindowsCredentialManagerUtility.DeleteSecret(RpolStorageStateCandidateTarget);
        }

        public static void DeleteRpolStorageState()
        {
            WindowsCredentialManagerUtility.DeleteSecret(RpolStorageStateTarget);
            WindowsCredentialManagerUtility.DeleteSecret(RpolStorageStateSlotATarget);
            WindowsCredentialManagerUtility.DeleteSecret(RpolStorageStateSlotBTarget);
            WindowsCredentialManagerUtility.DeleteSecret(RpolStorageStatePointerTarget);
            WindowsCredentialManagerUtility.DeleteSecret(RpolStorageStateCandidateTarget);
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

            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password)) return false;
            var original = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);
            try
            {
                SaveRpolCredentials(userName, password);
                settings.Remove(AppSettingsUtility.RpolUserNameSettingsKey);
                settings.Remove(AppSettingsUtility.RpolPasswordSettingsKey);
                if (!string.IsNullOrWhiteSpace(persistedSettingsPath))
                    LocalSettingsUtility.SaveEncryptedSettings(persistedSettingsPath, new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase));
                WindowsCredentialManagerUtility.DeleteSecret(RpolUserNameTarget);
                WindowsCredentialManagerUtility.DeleteSecret(RpolPasswordTarget);
                return true;
            }
            catch
            {
                settings.Clear(); foreach (var pair in original) settings[pair.Key] = pair.Value;
                if (!string.IsNullOrWhiteSpace(persistedSettingsPath))
                {
                    try { LocalSettingsUtility.SaveEncryptedSettings(persistedSettingsPath, original); } catch { }
                }
                try { DeleteRpolCredentials(); } catch { }
                throw;
            }
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

        private sealed record RpolCredentialRecord(
            [property: JsonPropertyName("version")] int Version,
            [property: JsonPropertyName("user_name")] string UserName,
            [property: JsonPropertyName("password")] string Password);

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
