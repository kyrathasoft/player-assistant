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
            var postsDirectory = Path.Combine(AppContext.BaseDirectory, PostsDirectoryName);

            Directory.CreateDirectory(postsDirectory);
            Directory.CreateDirectory(Path.Combine(postsDirectory, IcDirectoryName));
            Directory.CreateDirectory(Path.Combine(postsDirectory, IcDirectoryName, AsideDirectoryName));
            Directory.CreateDirectory(Path.Combine(postsDirectory, OocDirectoryName));
            Directory.CreateDirectory(Path.Combine(postsDirectory, OocDirectoryName, AsideDirectoryName));
        }

        internal static bool IsVersionArgument(string argument)
        {
            return VersionArguments.Any(versionArgument =>
                string.Equals(argument, versionArgument, StringComparison.OrdinalIgnoreCase));
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
    }
}
