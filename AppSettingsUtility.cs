using System.Text.Json;
using System.Net.Http;

namespace PlayerAssistant
{
    internal static class AppSettingsUtility
    {
        private const string SettingsFileName = "settings.json";
        private const string LocalSettingsFileName = "settings.local.json";
        private const string HostedLocalSettingsSettingsKey = "Hosted Local Settings";
        private const string RpolSiteSettingsKey = "RPOL Site";
        private const string GameIntroSettingsKey = "Game Intro";
        private const string TheCastSettingsKey = "The Cast";
        private const string ObsidianGameVaultSettingsKey = "Obsidian Game Vault";
        private const string XpTrackingSettingsKey = "XP Tracking";
        private const string SchemaVersionSettingsKey = "schema_version";
        private const int CurrentSettingsSchemaVersion = 1;
        private static readonly NetworkRequestPolicy HostedLocalSettingsRequestPolicy = new(
            TimeSpan.FromSeconds(5),
            MaxAttempts: 1,
            TimeSpan.Zero);
        private static readonly Lazy<IReadOnlyDictionary<string, string>> Settings = new(LoadSettings);
        private static Func<HttpClient>? HttpClientFactoryOverride;
        private static Func<string, NetworkUrlAllowlistValidation>? HostedLocalSettingsValidationOverride;

        public const string RpolUserNameSettingsKey = "RPOL user name";
        public const string RpolPasswordSettingsKey = "RPOL password";

        public static string GameForumUrl => Settings.Value[RpolSiteSettingsKey];
        public static string HostedLocalSettingsUrl => Settings.Value[HostedLocalSettingsSettingsKey];
        public static string? RpolUserName => RuntimeSecretStoreUtility.GetRpolUserName() ?? GetOptionalSetting(RpolUserNameSettingsKey);
        public static string? RpolPassword => RuntimeSecretStoreUtility.GetRpolPassword() ?? GetOptionalSetting(RpolPasswordSettingsKey);
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

            var runtimeSettingsPath = RuntimePathUtility.CombineUnderBase(runtimeDirectory, SettingsFileName);
            var settings = LoadSettingsFile(File.Exists(runtimeSettingsPath)
                ? runtimeSettingsPath
                : RuntimePathUtility.ResolveApplicationFileForRead(SettingsFileName));
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

                    try
                    {
                        _ = RuntimeSecretStoreUtility.TryMigrateRpolSecretsFromLocalSettings(localSettings, localSettingsPath);
                    }
                    catch (Exception ex) when (IsRecoverableLocalSettingsException(ex))
                    {
                        StartupLoggingUtility.Append(
                            "local settings secret-store migration",
                            new InvalidOperationException(
                                $"Unable to migrate RPOL credentials from '{localSettingsPath}' into Windows Credential Manager. Continuing with legacy sidecar values for this run.",
                                ex));
                    }

                    foreach (var pair in localSettings)
                    {
                        settings[pair.Key] = pair.Value;
                    }

                    if (RuntimeSecretStoreUtility.GetRpolUserName() is { Length: > 0 } storedUserName)
                    {
                        settings[RpolUserNameSettingsKey] = storedUserName;
                    }

                    if (RuntimeSecretStoreUtility.GetRpolPassword() is { Length: > 0 } storedPassword)
                    {
                        settings[RpolPasswordSettingsKey] = storedPassword;
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

            MergeHostedLocalSettings(settings);

            ValidateHostedLocalSettingsSetting(settings);
            ValidateHttpUrlSetting(settings, RpolSiteSettingsKey, NetworkUrlPurpose.Rpol);
            ValidateHttpUrlSetting(settings, GameIntroSettingsKey, NetworkUrlPurpose.Rpol);
            ValidateHttpUrlSetting(settings, TheCastSettingsKey, NetworkUrlPurpose.Rpol);
            ValidateHttpUrlSetting(settings, ObsidianGameVaultSettingsKey, NetworkUrlPurpose.ObsidianPublish);
            ValidateHttpUrlSetting(settings, XpTrackingSettingsKey, NetworkUrlPurpose.ObsidianPublish);

            return settings;
        }

        private static string ResolveLocalSettingsPath(string preferredLocalSettingsPath, string runtimeDirectory)
        {
            var runtimeLocalSettingsPath = RuntimePathUtility.CombineUnderBase(runtimeDirectory, LocalSettingsFileName);
            if (File.Exists(runtimeLocalSettingsPath))
            {
                return runtimeLocalSettingsPath;
            }

            if (File.Exists(preferredLocalSettingsPath))
            {
                return preferredLocalSettingsPath;
            }

            return RuntimePathUtility.ResolveUserDataFileForRead(LocalSettingsFileName);
        }

        private static void MergeHostedLocalSettings(Dictionary<string, string> settings)
        {
            if (!settings.TryGetValue(HostedLocalSettingsSettingsKey, out var hostedLocalSettingsUrl)
                || string.IsNullOrWhiteSpace(hostedLocalSettingsUrl))
            {
                return;
            }

            try
            {
                var hostedLocalSettings = LoadHostedLocalSettings(hostedLocalSettingsUrl);

                try
                {
                    _ = RuntimeSecretStoreUtility.TryMigrateRpolSecretsFromSettings(hostedLocalSettings);
                }
                catch (Exception ex) when (IsRecoverableLocalSettingsException(ex))
                {
                    StartupLoggingUtility.Append(
                        "hosted local settings secret-store migration",
                        new InvalidOperationException(
                            $"Unable to migrate RPOL credentials from hosted local settings '{hostedLocalSettingsUrl}' into Windows Credential Manager. Continuing with hosted values for this run.",
                            ex));
                }

                foreach (var pair in hostedLocalSettings)
                {
                    settings[pair.Key] = pair.Value;
                }

                if (RuntimeSecretStoreUtility.GetRpolUserName() is { Length: > 0 } storedUserName)
                {
                    settings[RpolUserNameSettingsKey] = storedUserName;
                }

                if (RuntimeSecretStoreUtility.GetRpolPassword() is { Length: > 0 } storedPassword)
                {
                    settings[RpolPasswordSettingsKey] = storedPassword;
                }
            }
            catch (Exception ex) when (IsRecoverableHostedLocalSettingsException(ex))
            {
                StartupLoggingUtility.Append(
                    "hosted local settings load",
                    new InvalidOperationException(
                        $"Unable to load hosted local settings from '{hostedLocalSettingsUrl}'. Continuing without hosted local settings for this run.",
                        ex));
            }
        }

        private static Dictionary<string, string> LoadHostedLocalSettings(string hostedLocalSettingsUrl)
        {
            var validation = ValidateHostedLocalSettingsUrl(hostedLocalSettingsUrl);
            if (!validation.IsAllowed || validation.Uri is null)
            {
                throw new InvalidOperationException(
                    $"Hosted local settings URL is not allowed: {validation.RejectionReason}");
            }

            using var httpClient = CreateHttpClient();
            using var response = NetworkRequestUtility.Send(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, validation.Uri),
                HttpCompletionOption.ResponseHeadersRead,
                HostedLocalSettingsRequestPolicy,
                purpose: NetworkUrlPurpose.PlayerAssistantHostedSettings);
            response.EnsureSuccessStatusCode();
            var fileContentsUtf8 = NetworkRequestUtility.ReadBytesAsync(
                response.Content,
                NetworkResponseContentLimit.JsonCache).GetAwaiter().GetResult();
            return HostedSettingsTrustUtility.LoadAndVerifyHostedSettings(
                fileContentsUtf8,
                hostedLocalSettingsUrl);
        }

        internal static bool TryGetRpolCredentials(out string? userName, out string? password)
        {
            if (RuntimeSecretStoreUtility.TryGetRpolCredentials(out userName, out password))
            {
                return !string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(password);
            }

            userName = GetOptionalSetting(RpolUserNameSettingsKey);
            password = GetOptionalSetting(RpolPasswordSettingsKey);
            return !string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(password);
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

        private static bool IsRecoverableHostedLocalSettingsException(Exception ex)
        {
            return ex is InvalidOperationException
                or JsonException
                or HttpRequestException
                or NetworkRequestException;
        }

        private static HttpClient CreateHttpClient()
        {
            return HttpClientFactoryOverride?.Invoke()
                ?? NetworkRequestUtility.CreateHttpClient();
        }

        internal static IDisposable UseHttpClientFactoryForTests(Func<HttpClient> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);

            var previousFactory = HttpClientFactoryOverride;
            HttpClientFactoryOverride = factory;
            return new DelegateDisposable(() => HttpClientFactoryOverride = previousFactory);
        }

        internal static IDisposable UseHostedLocalSettingsValidationOverrideForTests(
            Func<string, NetworkUrlAllowlistValidation> validator)
        {
            ArgumentNullException.ThrowIfNull(validator);

            var previousValidator = HostedLocalSettingsValidationOverride;
            HostedLocalSettingsValidationOverride = validator;
            return new DelegateDisposable(() => HostedLocalSettingsValidationOverride = previousValidator);
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

        private static void ValidateOptionalHttpUrlSetting(
            IReadOnlyDictionary<string, string> settings,
            string settingsKey,
            NetworkUrlPurpose purpose)
        {
            if (!settings.TryGetValue(settingsKey, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var validation = NetworkUrlAllowlistUtility.Validate(value, purpose);
            if (!validation.IsAllowed)
            {
                throw new InvalidOperationException(
                    $"Settings value '{settingsKey}' is not allowed: {validation.RejectionReason}");
            }
        }

        private static void ValidateHostedLocalSettingsSetting(IReadOnlyDictionary<string, string> settings)
        {
            if (!settings.TryGetValue(HostedLocalSettingsSettingsKey, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var validation = ValidateHostedLocalSettingsUrl(value);
            if (!validation.IsAllowed)
            {
                throw new InvalidOperationException(
                    $"Settings value '{HostedLocalSettingsSettingsKey}' is not allowed: {validation.RejectionReason}");
            }
        }

        private static NetworkUrlAllowlistValidation ValidateHostedLocalSettingsUrl(string value)
        {
            return HostedLocalSettingsValidationOverride?.Invoke(value)
                ?? NetworkUrlAllowlistUtility.Validate(value, NetworkUrlPurpose.PlayerAssistantHostedSettings);
        }

        private sealed class DelegateDisposable(Action onDispose) : IDisposable
        {
            private Action? onDispose = onDispose;

            public void Dispose()
            {
                Interlocked.Exchange(ref onDispose, null)?.Invoke();
            }
        }
    }
}
