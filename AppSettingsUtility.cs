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
        private static readonly Lazy<IReadOnlyDictionary<string, string>> Settings = new(LoadSettings);

        public static string GameForumUrl => Settings.Value[RpolSiteSettingsKey];
        public static string? RpolUserName => GetOptionalSetting(RpolUserNameSettingsKey);
        public static string? RpolPassword => GetOptionalSetting(RpolPasswordSettingsKey);
        public static string GameIntroUrl => Settings.Value[GameIntroSettingsKey];
        public static string TheCastUrl => Settings.Value[TheCastSettingsKey];
        public static string ObsidianGameVaultUrl => Settings.Value[ObsidianGameVaultSettingsKey].TrimEnd('/');

        public static void Load()
        {
            _ = Settings.Value;
        }

        private static IReadOnlyDictionary<string, string> LoadSettings()
        {
            var settings = LoadSettingsFile(SettingsFileName);
            var localSettingsPath = Path.Combine(AppContext.BaseDirectory, LocalSettingsFileName);

            if (File.Exists(localSettingsPath))
            {
                foreach (var pair in LocalSettingsUtility.LoadSettings(localSettingsPath))
                {
                    settings[pair.Key] = pair.Value;
                }
            }

            ValidateHttpUrlSetting(settings, RpolSiteSettingsKey);
            ValidateHttpUrlSetting(settings, GameIntroSettingsKey);
            ValidateHttpUrlSetting(settings, TheCastSettingsKey);
            ValidateHttpUrlSetting(settings, ObsidianGameVaultSettingsKey);

            return settings;
        }

        private static Dictionary<string, string> LoadSettingsFile(string fileName)
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, fileName);

            if (!File.Exists(settingsPath))
            {
                throw new FileNotFoundException($"Settings file '{fileName}' was not found.", settingsPath);
            }

            using var settingsStream = File.OpenRead(settingsPath);
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(
                settingsStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (settings is null)
            {
                throw new InvalidOperationException($"Settings file '{fileName}' is empty or invalid.");
            }

            return settings;
        }

        private static string? GetOptionalSetting(string settingsKey)
        {
            return Settings.Value.TryGetValue(settingsKey, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }

        private static void ValidateHttpUrlSetting(
            IReadOnlyDictionary<string, string> settings,
            string settingsKey)
        {
            if (!settings.TryGetValue(settingsKey, out var url) ||
                string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException(
                    $"Settings file '{SettingsFileName}' must contain a '{settingsKey}' URL.");
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"Settings value '{settingsKey}' must be a valid HTTP or HTTPS URL.");
            }
        }
    }
}
