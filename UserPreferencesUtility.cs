using System.Text.Json;

namespace PlayerAssistant
{
    internal static class UserPreferencesUtility
    {
        private const string PreferencesDirectoryName = "PlayerAssistant";
        private const string PreferencesFileName = "preferences.json";
        private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

        public static bool WhiteMarbleBackgroundTilingEnabled { get; set; } = true;
        public static bool SkipHeroImageParadeAtStartup { get; set; }

        public static void Load()
        {
            var preferencesPath = GetPreferencesPath();

            if (!File.Exists(preferencesPath))
            {
                return;
            }

            try
            {
                using var preferencesStream = File.OpenRead(preferencesPath);
                var preferences = JsonSerializer.Deserialize<UserPreferences>(
                    preferencesStream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (preferences is not null)
                {
                    WhiteMarbleBackgroundTilingEnabled = preferences.WhiteMarbleBackgroundTilingEnabled;
                    SkipHeroImageParadeAtStartup = preferences.SkipHeroImageParadeAtStartup;
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public static void Save()
        {
            var preferencesPath = GetPreferencesPath();

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
                using var preferencesStream = File.Create(preferencesPath);
                JsonSerializer.Serialize(
                    preferencesStream,
                    new UserPreferences(
                        WhiteMarbleBackgroundTilingEnabled,
                        SkipHeroImageParadeAtStartup),
                    SerializerOptions);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public static bool ConsumeSkipHeroImageParadeAtStartup()
        {
            if (!SkipHeroImageParadeAtStartup)
            {
                return false;
            }

            SkipHeroImageParadeAtStartup = false;
            Save();
            return true;
        }

        private static string GetPreferencesPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                PreferencesDirectoryName,
                PreferencesFileName);
        }

        private sealed record UserPreferences(
            bool WhiteMarbleBackgroundTilingEnabled,
            bool SkipHeroImageParadeAtStartup);
    }
}
