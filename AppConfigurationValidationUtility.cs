namespace PlayerAssistant
{
    internal enum AppConfigurationIssueSeverity
    {
        Warning,
        Error
    }

    internal sealed record AppConfigurationIssue(
        AppConfigurationIssueSeverity Severity,
        string Message,
        string RepairAction);

    internal sealed record AppConfigurationValidationReport(
        IReadOnlyList<AppConfigurationIssue> Issues)
    {
        public bool HasIssues => Issues.Count > 0;

        public string? FirstUserMessage => Issues.Count == 0
            ? null
            : $"Settings problem: {Issues[0].Message}";

        public string ToRemediationText()
        {
            if (Issues.Count == 0)
            {
                return "No startup configuration problems were detected." + Environment.NewLine;
            }

            var lines = new List<string>
            {
                "Player Assistant startup configuration guidance",
                $"Generated: {DateTimeOffset.Now:O}",
                string.Empty
            };

            for (var index = 0; index < Issues.Count; index++)
            {
                var issue = Issues[index];
                lines.Add($"{index + 1}. {issue.Severity}: {issue.Message}");
                lines.Add($"   Repair: {issue.RepairAction}");
                lines.Add(string.Empty);
            }

            return string.Join(Environment.NewLine, lines);
        }
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
        public const string RemediationFileName = "startup-remediation.txt";
        private const string RpolSiteSettingsKey = "RPOL Site";
        private const string RpolUserNameSettingsKey = "RPOL user name";
        private const string RpolPasswordSettingsKey = "RPOL password";
        private const string HostedLocalSettingsSettingsKey = "Hosted Local Settings";
        private const string GameIntroSettingsKey = "Game Intro";
        private const string TheCastSettingsKey = "The Cast";
        private const string ObsidianGameVaultSettingsKey = "Obsidian Game Vault";
        private const string XpTrackingSettingsKey = "XP Tracking";

        private static readonly string[] RequiredRuntimeSidecars =
        [
            "keyword-index.json",
            KeywordTermsFileUtility.FileName,
            "sitemap.xml",
            "sitemap-keyword-urls.json",
            XpPasswordStoreUtility.FileName
        ];

        public static AppConfigurationValidationReport LatestReport { get; private set; } = new([]);

        public static AppConfigurationValidationReport ValidateCurrentAndLog()
        {
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RpolSiteSettingsKey] = AppSettingsUtility.GameForumUrl,
                [HostedLocalSettingsSettingsKey] = AppSettingsUtility.HostedLocalSettingsUrl,
                [GameIntroSettingsKey] = AppSettingsUtility.GameIntroUrl,
                [TheCastSettingsKey] = AppSettingsUtility.TheCastUrl,
                [ObsidianGameVaultSettingsKey] = AppSettingsUtility.ObsidianGameVaultUrl,
                [XpTrackingSettingsKey] = AppSettingsUtility.XpTrackingUrl
            };

            if (AppSettingsUtility.TryGetRpolCredentials(out var rpolUserName, out var rpolPassword)
                && !string.IsNullOrWhiteSpace(rpolUserName)
                && !string.IsNullOrWhiteSpace(rpolPassword))
            {
                settings[RpolUserNameSettingsKey] = rpolUserName;
                settings[RpolPasswordSettingsKey] = rpolPassword;
            }

            LatestReport = Validate(
                settings,
                AppContext.BaseDirectory,
                warnAboutMissingRpolCredentials: AppSettingsUtility.HostedLocalSettingsLoadFailed,
                checkRuntimeDirectoryWritable: false);
            if (LatestReport.HasIssues)
            {
                WriteRemediationFile(LatestReport, RuntimePathUtility.WritableRuntimeDirectory);
                StartupLoggingUtility.Append(
                    "configuration validation",
                    new AppConfigurationValidationException(LatestReport));
            }
            else
            {
                DeleteRemediationFile(RuntimePathUtility.WritableRuntimeDirectory);
            }

            return LatestReport;
        }

        public static void WriteRemediationFile(AppConfigurationValidationReport report, string runtimeDirectory)
        {
            ArgumentNullException.ThrowIfNull(report);
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);

            try
            {
                Directory.CreateDirectory(runtimeDirectory);
                AtomicFileUtility.WriteAllText(
                    RuntimePathUtility.CombineUnderBase(runtimeDirectory, RemediationFileName),
                    report.ToRemediationText());
            }
            catch
            {
            }
        }

        public static AppConfigurationValidationReport Validate(
            IReadOnlyDictionary<string, string> settings,
            string runtimeDirectory,
            bool warnAboutMissingRpolCredentials = false,
            bool checkRuntimeDirectoryWritable = true)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);

            var issues = new List<AppConfigurationIssue>();

            ValidateOptionalHttpUrlSetting(settings, HostedLocalSettingsSettingsKey, NetworkUrlPurpose.PlayerAssistantHostedSettings, issues);
            ValidateHttpUrlSetting(settings, RpolSiteSettingsKey, NetworkUrlPurpose.Rpol, issues);
            ValidateHttpUrlSetting(settings, GameIntroSettingsKey, NetworkUrlPurpose.Rpol, issues);
            ValidateHttpUrlSetting(settings, TheCastSettingsKey, NetworkUrlPurpose.Rpol, issues);
            ValidateHttpUrlSetting(settings, ObsidianGameVaultSettingsKey, NetworkUrlPurpose.ObsidianPublish, issues);
            ValidateHttpUrlSetting(settings, XpTrackingSettingsKey, NetworkUrlPurpose.ObsidianPublish, issues);
            ValidateRpolCredentials(settings, issues, warnAboutMissingRpolCredentials);
            ValidateRuntimeDirectory(runtimeDirectory, issues, checkRuntimeDirectoryWritable);
            ValidateRuntimeSidecars(runtimeDirectory, issues);
            ValidateReleaseIntegrityManifest(runtimeDirectory, issues);

            return new AppConfigurationValidationReport(issues);
        }

        private static void ValidateHttpUrlSetting(
            IReadOnlyDictionary<string, string> settings,
            string settingsKey,
            NetworkUrlPurpose purpose,
            List<AppConfigurationIssue> issues)
        {
            if (!settings.TryGetValue(settingsKey, out var value)
                || string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new AppConfigurationIssue(
                    AppConfigurationIssueSeverity.Error,
                    $"{settingsKey} is missing or empty.",
                    $"Set '{settingsKey}' in {GetSettingsRepairFileName(settingsKey)} to the expected absolute HTTP or HTTPS URL, then restart the app."));
                return;
            }

            var validation = NetworkUrlAllowlistUtility.Validate(value, purpose);
            if (!validation.IsAllowed)
            {
                issues.Add(new AppConfigurationIssue(
                    AppConfigurationIssueSeverity.Error,
                    $"{settingsKey} is not on the allowed network host list: {validation.RejectionReason}",
                    $"Edit {GetSettingsRepairFileName(settingsKey)} and replace '{settingsKey}' with the expected RPOL or Obsidian Publish URL."));
            }
        }

        private static void ValidateOptionalHttpUrlSetting(
            IReadOnlyDictionary<string, string> settings,
            string settingsKey,
            NetworkUrlPurpose purpose,
            List<AppConfigurationIssue> issues)
        {
            if (!settings.TryGetValue(settingsKey, out var value)
                || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var validation = NetworkUrlAllowlistUtility.Validate(value, purpose);
            if (!validation.IsAllowed)
            {
                issues.Add(new AppConfigurationIssue(
                    AppConfigurationIssueSeverity.Error,
                    $"{settingsKey} is not on the allowed network host list: {validation.RejectionReason}",
                    $"Edit settings.json and replace '{settingsKey}' with the expected hosted Player Assistant settings URL."));
            }
        }

        private static string GetSettingsRepairFileName(string settingsKey)
        {
            if (string.Equals(settingsKey, RpolUserNameSettingsKey, StringComparison.Ordinal)
                || string.Equals(settingsKey, RpolPasswordSettingsKey, StringComparison.Ordinal))
            {
                return "Windows Credential Manager";
            }

            return string.Equals(settingsKey, XpTrackingSettingsKey, StringComparison.Ordinal)
                ? "settings.local.json"
                : "settings.json";
        }

        private static void ValidateRpolCredentials(
            IReadOnlyDictionary<string, string> settings,
            List<AppConfigurationIssue> issues,
            bool warnAboutMissingRpolCredentials)
        {
            var hasUserName = settings.TryGetValue(RpolUserNameSettingsKey, out var userName)
                && !string.IsNullOrWhiteSpace(userName);
            var hasPassword = settings.TryGetValue(RpolPasswordSettingsKey, out var password)
                && !string.IsNullOrWhiteSpace(password);

            if (hasUserName && hasPassword)
            {
                return;
            }

            if (!warnAboutMissingRpolCredentials)
            {
                return;
            }

            issues.Add(new AppConfigurationIssue(
                AppConfigurationIssueSeverity.Warning,
                "Hosted RPOL credential data could not be loaded; authenticated RPOL downloads will be unavailable.",
                "Confirm the Hosted Local Settings URL is reachable and contains both RPOL user name and RPOL password, then restart the app."));
        }

        private static void ValidateRuntimeDirectory(
            string runtimeDirectory,
            List<AppConfigurationIssue> issues,
            bool checkWritable)
        {
            if (!Directory.Exists(runtimeDirectory))
            {
                issues.Add(new AppConfigurationIssue(
                    AppConfigurationIssueSeverity.Error,
                    $"Runtime directory does not exist: {runtimeDirectory}",
                    "Restore the Release or publish folder, or rebuild/publish the app so the runtime directory exists."));
                return;
            }

            if (!checkWritable)
            {
                return;
            }

            var testPath = RuntimePathUtility.CombineUnderBase(runtimeDirectory, $".player-assistant-write-test-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(testPath, string.Empty);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(new AppConfigurationIssue(
                    AppConfigurationIssueSeverity.Error,
                    $"Runtime directory is not writable: {runtimeDirectory}",
                    "Close other processes using the folder, check Windows permissions, and run the app from a writable Release or publish directory."));
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
                var path = RuntimePathUtility.CombineUnderBase(runtimeDirectory, fileName);
                if (!File.Exists(path))
                {
                    issues.Add(new AppConfigurationIssue(
                        AppConfigurationIssueSeverity.Warning,
                        $"{fileName} is missing; related cached features may be unavailable.",
                        $"Restore or regenerate '{fileName}'. For Release output, rebuild or rerun the startup/download workflow that creates runtime sidecars."));
                    continue;
                }

                if (new FileInfo(path).Length <= 0)
                {
                    issues.Add(new AppConfigurationIssue(
                        AppConfigurationIssueSeverity.Warning,
                        $"{fileName} is empty; related cached features may be unavailable.",
                        $"Regenerate '{fileName}' because the current file is empty."));
                }
            }
        }

        private static void ValidateReleaseIntegrityManifest(
            string runtimeDirectory,
            List<AppConfigurationIssue> issues)
        {
            foreach (var message in ReleaseIntegrityManifestUtility.ValidateIfPresent(runtimeDirectory))
            {
                issues.Add(new AppConfigurationIssue(
                    AppConfigurationIssueSeverity.Error,
                    message,
                    "Restore the published folder from a trusted build, or rerun the publish script so release-manifest.json and its listed runtime files are regenerated together."));
            }
        }

        private static void DeleteRemediationFile(string runtimeDirectory)
        {
            try
            {
                var path = RuntimePathUtility.CombineUnderBase(runtimeDirectory, RemediationFileName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
