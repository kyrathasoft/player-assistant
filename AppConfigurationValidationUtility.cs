namespace PlayerAssistant
{
    internal enum AppConfigurationIssueSeverity
    {
        Warning,
        Error
    }

    internal sealed record AppConfigurationIssue(
        AppConfigurationIssueSeverity Severity,
        string Message);

    internal sealed record AppConfigurationValidationReport(
        IReadOnlyList<AppConfigurationIssue> Issues)
    {
        public bool HasIssues => Issues.Count > 0;

        public string? FirstUserMessage => Issues.Count == 0
            ? null
            : $"Settings problem: {Issues[0].Message}";
    }

    internal sealed class AppConfigurationValidationException : InvalidOperationException
    {
        public AppConfigurationValidationException(AppConfigurationValidationReport report)
            : base(string.Join(Environment.NewLine, report.Issues.Select(issue => $"{issue.Severity}: {issue.Message}")))
        {
            Report = report;
        }

        public AppConfigurationValidationReport Report { get; }
    }

    internal static class AppConfigurationValidationUtility
    {
        private const string RpolSiteSettingsKey = "RPOL Site";
        private const string RpolUserNameSettingsKey = "RPOL user name";
        private const string RpolPasswordSettingsKey = "RPOL password";
        private const string GameIntroSettingsKey = "Game Intro";
        private const string TheCastSettingsKey = "The Cast";
        private const string ObsidianGameVaultSettingsKey = "Obsidian Game Vault";

        private static readonly string[] RequiredRuntimeSidecars =
        [
            "keyword-index.json",
            KeywordTermsFileUtility.FileName,
            "sitemap.xml"
        ];

        public static AppConfigurationValidationReport LatestReport { get; private set; } = new([]);

        public static AppConfigurationValidationReport ValidateCurrentAndLog()
        {
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RpolSiteSettingsKey] = AppSettingsUtility.GameForumUrl,
                [GameIntroSettingsKey] = AppSettingsUtility.GameIntroUrl,
                [TheCastSettingsKey] = AppSettingsUtility.TheCastUrl,
                [ObsidianGameVaultSettingsKey] = AppSettingsUtility.ObsidianGameVaultUrl
            };

            if (!string.IsNullOrWhiteSpace(AppSettingsUtility.RpolUserName))
            {
                settings[RpolUserNameSettingsKey] = AppSettingsUtility.RpolUserName;
            }

            if (!string.IsNullOrWhiteSpace(AppSettingsUtility.RpolPassword))
            {
                settings[RpolPasswordSettingsKey] = AppSettingsUtility.RpolPassword;
            }

            LatestReport = Validate(settings, AppContext.BaseDirectory);
            if (LatestReport.HasIssues)
            {
                StartupLoggingUtility.Append(
                    "configuration validation",
                    new AppConfigurationValidationException(LatestReport));
            }

            return LatestReport;
        }

        public static AppConfigurationValidationReport Validate(
            IReadOnlyDictionary<string, string> settings,
            string runtimeDirectory)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);

            var issues = new List<AppConfigurationIssue>();

            ValidateHttpUrlSetting(settings, RpolSiteSettingsKey, issues);
            ValidateHttpUrlSetting(settings, GameIntroSettingsKey, issues);
            ValidateHttpUrlSetting(settings, TheCastSettingsKey, issues);
            ValidateHttpUrlSetting(settings, ObsidianGameVaultSettingsKey, issues);
            ValidateRpolCredentials(settings, issues);
            ValidateRuntimeDirectory(runtimeDirectory, issues);
            ValidateRuntimeSidecars(runtimeDirectory, issues);

            return new AppConfigurationValidationReport(issues);
        }

        private static void ValidateHttpUrlSetting(
            IReadOnlyDictionary<string, string> settings,
            string settingsKey,
            List<AppConfigurationIssue> issues)
        {
            if (!settings.TryGetValue(settingsKey, out var value)
                || string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new AppConfigurationIssue(
                    AppConfigurationIssueSeverity.Error,
                    $"{settingsKey} is missing or empty."));
                return;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                issues.Add(new AppConfigurationIssue(
                    AppConfigurationIssueSeverity.Error,
                    $"{settingsKey} must be an absolute HTTP or HTTPS URL."));
            }
        }

        private static void ValidateRpolCredentials(
            IReadOnlyDictionary<string, string> settings,
            List<AppConfigurationIssue> issues)
        {
            var hasUserName = settings.TryGetValue(RpolUserNameSettingsKey, out var userName)
                && !string.IsNullOrWhiteSpace(userName);
            var hasPassword = settings.TryGetValue(RpolPasswordSettingsKey, out var password)
                && !string.IsNullOrWhiteSpace(password);

            if (hasUserName && hasPassword)
            {
                return;
            }

            issues.Add(new AppConfigurationIssue(
                AppConfigurationIssueSeverity.Warning,
                "RPOL credentials are incomplete; authenticated RPOL downloads will be unavailable."));
        }

        private static void ValidateRuntimeDirectory(string runtimeDirectory, List<AppConfigurationIssue> issues)
        {
            if (!Directory.Exists(runtimeDirectory))
            {
                issues.Add(new AppConfigurationIssue(
                    AppConfigurationIssueSeverity.Error,
                    $"Runtime directory does not exist: {runtimeDirectory}"));
                return;
            }

            var testPath = Path.Combine(runtimeDirectory, $".player-assistant-write-test-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(testPath, string.Empty);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(new AppConfigurationIssue(
                    AppConfigurationIssueSeverity.Error,
                    $"Runtime directory is not writable: {runtimeDirectory}"));
            }
            finally
            {
                try
                {
                    if (File.Exists(testPath))
                    {
                        File.Delete(testPath);
                    }
                }
                catch
                {
                }
            }
        }

        private static void ValidateRuntimeSidecars(string runtimeDirectory, List<AppConfigurationIssue> issues)
        {
            foreach (var fileName in RequiredRuntimeSidecars)
            {
                var path = Path.Combine(runtimeDirectory, fileName);
                if (!File.Exists(path))
                {
                    issues.Add(new AppConfigurationIssue(
                        AppConfigurationIssueSeverity.Warning,
                        $"{fileName} is missing; related cached features may be unavailable."));
                    continue;
                }

                if (new FileInfo(path).Length <= 0)
                {
                    issues.Add(new AppConfigurationIssue(
                        AppConfigurationIssueSeverity.Warning,
                        $"{fileName} is empty; related cached features may be unavailable."));
                }
            }
        }
    }
}
