// This file is the application entry point.
namespace PlayerAssistant
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Global\PlayerAssistant.SingleInstance";
        private const string PostsDirectoryName = "Posts";
        private const string IcDirectoryName = "IC";
        private const string OocDirectoryName = "OOC";
        private const string AsideDirectoryName = "Aside";
        private const string SuppressHeroImagesArgument = "--suppress-hero-images";
        private static readonly string[] VersionArguments = ["--version", "/version"];
        private static readonly string[] HealthArguments = ["--health", "/health"];

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            RegisterUnhandledExceptionLogging();

            try
            {
                Run(args);
            }
            catch (Exception ex)
            {
                StartupLoggingUtility.Append("startup", ex);
                LastCrashDiagnosticUtility.Write("startup", ex, overwrite: false);
                MessageBox.Show(
                    ex.Message,
                    "Player Assistant Startup Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void Run(string[] args)
        {
            if (args.Any(IsVersionArgument))
            {
                var versionText = GetVersionText();
                Console.WriteLine(versionText);
                StartupLoggingUtility.Append("version", versionText);
                return;
            }

            if (args.Any(IsHealthArgument))
            {
                var healthText = GetHealthText();
                Console.WriteLine(healthText);
                StartupLoggingUtility.Append("health", healthText);
                return;
            }

            using Mutex singleInstanceMutex = new(true, SingleInstanceMutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "Player Assistant is already running.",
                    "Player Assistant",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            StartupHealthUtility.Reset();
            StartupLoggingUtility.RunRequiredPhase("settings load", AppSettingsUtility.Load);
            UserPreferencesUtility.Load();
            FileDownloadCounters.Reset();
            EnsurePostsDirectory();
            KeywordTermsFileUtility.EnsureReleaseCopy();
            StartupLoggingUtility.RunOptionalPhaseAsync(
                "runtime housekeeping",
                () =>
                {
                    RuntimeHousekeepingUtility.CleanCurrentRuntimeAndLog();
                    return Task.CompletedTask;
                }).GetAwaiter().GetResult();
            StartupLoggingUtility.RunOptionalPhaseAsync(
                "configuration validation",
                () =>
                {
                    AppConfigurationValidationUtility.ValidateCurrentAndLog();
                    return Task.CompletedTask;
                }).GetAwaiter().GetResult();
            ApplicationConfiguration.Initialize();
            var suppressHeroImages = args.Any(arg =>
                string.Equals(arg, SuppressHeroImagesArgument, StringComparison.OrdinalIgnoreCase))
                || UserPreferencesUtility.SkipHeroImageParadeAtStartup;
            Application.Run(new Form1(suppressHeroImages));
        }

        private static void RegisterUnhandledExceptionLogging()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                StartupLoggingUtility.Append("UI thread exception", e.Exception);
                LastCrashDiagnosticUtility.Write("UI thread exception", e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    StartupLoggingUtility.Append("unhandled exception", ex);
                    LastCrashDiagnosticUtility.Write("unhandled exception", ex, e.IsTerminating);
                }
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                StartupLoggingUtility.Append("unobserved task exception", e.Exception);
                LastCrashDiagnosticUtility.Write("unobserved task exception", e.Exception);
                e.SetObserved();
            };
        }

        private static void EnsurePostsDirectory()
        {
            var postsDirectory = RuntimePathUtility.GetWritableRuntimePath(PostsDirectoryName);

            Directory.CreateDirectory(postsDirectory);
            Directory.CreateDirectory(RuntimePathUtility.CombineUnderBase(postsDirectory, IcDirectoryName));
            Directory.CreateDirectory(RuntimePathUtility.CombineUnderBase(postsDirectory, IcDirectoryName, AsideDirectoryName));
            Directory.CreateDirectory(RuntimePathUtility.CombineUnderBase(postsDirectory, OocDirectoryName));
            Directory.CreateDirectory(RuntimePathUtility.CombineUnderBase(postsDirectory, OocDirectoryName, AsideDirectoryName));
        }

        internal static bool IsVersionArgument(string argument)
        {
            return VersionArguments.Any(versionArgument =>
                string.Equals(argument, versionArgument, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsHealthArgument(string argument)
        {
            return HealthArguments.Any(healthArgument =>
                string.Equals(argument, healthArgument, StringComparison.OrdinalIgnoreCase));
        }

        internal static string GetVersionText()
        {
            var assembly = typeof(Program).Assembly;
            var informationalVersion = assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()
                ?.InformationalVersion;

            return string.IsNullOrWhiteSpace(informationalVersion)
                ? "player-assistant version unknown"
                : $"player-assistant {informationalVersion}";
        }

        internal static string GetHealthText()
        {
            try
            {
                AppSettingsUtility.Load();
                var report = AppConfigurationValidationUtility.ValidateCurrentAndLog();
                var status = report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error)
                    ? "error"
                    : report.HasIssues ? "warning" : "ok";
                var lines = new List<string>
                {
                    GetVersionText(),
                    $"runtime: {AppContext.BaseDirectory}",
                    $"status: {status}",
                    $"issues: {report.Issues.Count}"
                };

                lines.AddRange(report.Issues.Select(issue => $"{issue.Severity}: {issue.Message}"));
                return string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                return string.Join(
                    Environment.NewLine,
                    GetVersionText(),
                    $"runtime: {AppContext.BaseDirectory}",
                    "status: error",
                    $"Error: {SensitiveTextRedactionUtility.Redact(ex.Message)}");
            }
        }
    }
}
