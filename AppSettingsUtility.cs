using System.Text.Json;

namespace PlayerAssistant
{
    internal static class AppSettingsUtility
    {
        private const string SettingsFileName = "settings.json";
        private const string LocalSettingsFileName = "settings.local.json";
        private const string RpolSiteSettingsKey = "RPOL Site";
        private const string RpolUserNameSettingsKey = "RPOL user name";
        private const string RpolPasswordSettingsKey = "RPOL password";
        private const string GameIntroSettingsKey = "Game Intro";
        private const string TheCastSettingsKey = "The Cast";
        private const string ObsidianGameVaultSettingsKey = "Obsidian Game Vault";
        private const string XpTrackingSettingsKey = "XP Tracking";
        private const string SchemaVersionSettingsKey = "schema_version";
        private const int CurrentSettingsSchemaVersion = 1;
        private static readonly Lazy<IReadOnlyDictionary<string, string>> Settings = new(LoadSettings);

        public static string GameForumUrl => Settings.Value[RpolSiteSettingsKey];
        public static string? RpolUserName => GetOptionalSetting(RpolUserNameSettingsKey);
        public static string? RpolPassword => GetOptionalSetting(RpolPasswordSettingsKey);
        public static string GameIntroUrl => Settings.Value[GameIntroSettingsKey];
        public static string TheCastUrl => Settings.Value[TheCastSettingsKey];
        public static string ObsidianGameVaultUrl => Settings.Value[ObsidianGameVaultSettingsKey].TrimEnd('/');
        public static string XpTrackingUrl => Settings.Value[XpTrackingSettingsKey];

        public static void Load()
        {
            _ = Settings.Value;
        }

        private static IReadOnlyDictionary<string, string> LoadSettings()
        {
            return LoadSettings(RuntimePathUtility.ApplicationDirectory);
        }

        internal static IReadOnlyDictionary<string, string> LoadSettings(string runtimeDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);

            var settings = LoadSettingsFile(RuntimePathUtility.ResolveApplicationFileForRead(SettingsFileName));
            var preferredLocalSettingsPath = RuntimePathUtility.GetUserDataPath(LocalSettingsFileName);
            var localSettingsPath = ResolveLocalSettingsPath(preferredLocalSettingsPath, runtimeDirectory);

            if (File.Exists(localSettingsPath))
            {
                try
                {
                    var localSettings = string.Equals(
                        localSettingsPath,
                        preferredLocalSettingsPath,
                        StringComparison.OrdinalIgnoreCase)
                            ? LocalSettingsUtility.LoadSettings(localSettingsPath)
                            : LocalSettingsUtility.LoadSettingsWithoutMigration(localSettingsPath);

                    foreach (var pair in localSettings)
                    {
                        settings[pair.Key] = pair.Value;
                    }

                    if (!string.Equals(localSettingsPath, preferredLocalSettingsPath, StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(preferredLocalSettingsPath)!);
                        LocalSettingsUtility.SaveEncryptedSettings(preferredLocalSettingsPath, localSettings);
                    }
                }
                catch (Exception ex) when (IsRecoverableLocalSettingsException(ex))
                {
                    RuntimeArtifactUtility.QuarantineAndLog(localSettingsPath, "local settings load", ex);
                }
            }

            ValidateHttpUrlSetting(settings, RpolSiteSettingsKey, NetworkUrlPurpose.Rpol);
            ValidateHttpUrlSetting(settings, GameIntroSettingsKey, NetworkUrlPurpose.Rpol);
            ValidateHttpUrlSetting(settings, TheCastSettingsKey, NetworkUrlPurpose.Rpol);
            ValidateHttpUrlSetting(settings, ObsidianGameVaultSettingsKey, NetworkUrlPurpose.ObsidianPublish);
            ValidateHttpUrlSetting(settings, XpTrackingSettingsKey, NetworkUrlPurpose.ObsidianPublish);

            return settings;
        }

        private static string ResolveLocalSettingsPath(string preferredLocalSettingsPath, string runtimeDirectory)
        {
            if (File.Exists(preferredLocalSettingsPath))
            {
                return preferredLocalSettingsPath;
            }

            var runtimeLocalSettingsPath = RuntimePathUtility.CombineUnderBase(runtimeDirectory, LocalSettingsFileName);
            if (File.Exists(runtimeLocalSettingsPath))
            {
                return runtimeLocalSettingsPath;
            }

            return RuntimePathUtility.ResolveUserDataFileForRead(LocalSettingsFileName);
        }

        private static Dictionary<string, string> LoadSettingsFile(string settingsPath)
        {
            if (!File.Exists(settingsPath))
            {
                throw new FileNotFoundException($"Settings file '{SettingsFileName}' was not found.", settingsPath);
            }

            using var settingsStream = File.OpenRead(settingsPath);
            using var settingsDocument = JsonDocument.Parse(settingsStream);
            if (settingsDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Settings file '{SettingsFileName}' is empty or invalid.");
            }

            ValidateSchemaVersion(settingsDocument.RootElement, SettingsFileName);

            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in settingsDocument.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, SchemaVersionSettingsKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException(
                        $"Settings file '{SettingsFileName}' value '{property.Name}' must be a string.");
                }

                settings[property.Name] = property.Value.GetString() ?? string.Empty;
            }

            return settings;
        }

        private static void ValidateSchemaVersion(JsonElement root, string fileName)
        {
            if (!root.TryGetProperty(SchemaVersionSettingsKey, out var schemaVersionElement))
            {
                return;
            }

            if (schemaVersionElement.ValueKind != JsonValueKind.Number
                || !schemaVersionElement.TryGetInt32(out var schemaVersion)
                || schemaVersion < 0)
            {
                throw new InvalidOperationException(
                    $"Settings file '{fileName}' has an invalid '{SchemaVersionSettingsKey}' value.");
            }

            if (schemaVersion > CurrentSettingsSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Settings file '{fileName}' uses unsupported schema version {schemaVersion}. This app supports schema version {CurrentSettingsSchemaVersion}.");
            }
        }

        private static bool IsRecoverableLocalSettingsException(Exception ex)
        {
            return ex is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or JsonException;
        }

        private static string? GetOptionalSetting(string settingsKey)
        {
            return Settings.Value.TryGetValue(settingsKey, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }

        private static void ValidateHttpUrlSetting(
            IReadOnlyDictionary<string, string> settings,
            string settingsKey,
            NetworkUrlPurpose purpose)
        {
            if (!settings.TryGetValue(settingsKey, out var url) ||
                string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException(
                    $"Settings file '{SettingsFileName}' must contain a '{settingsKey}' URL.");
            }

            var validation = NetworkUrlAllowlistUtility.Validate(url, purpose);
            if (!validation.IsAllowed)
            {
                throw new InvalidOperationException(
                    $"Settings value '{settingsKey}' is not allowed: {validation.RejectionReason}");
            }
        }
    }
}
