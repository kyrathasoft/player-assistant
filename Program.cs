using System.Text.Json;
using System.Text.Encodings.Web;

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
        private const string LocalSettingsFileName = "settings.local.json";
        private const int LocalSettingsSchemaVersion = 1;
        private static readonly string[] VersionArguments = ["--version", "/version"];
        private static readonly string[] HealthArguments = ["--health", "/health"];
        private static readonly string[] UpdatePreflightArguments = ["--update-preflight", "/update-preflight"];
        private static readonly string[] EncryptLocalSettingsArguments = ["--encrypt-local-settings", "/encrypt-local-settings"];
        private static readonly string[] DecryptLocalSettingsArguments = ["--decrypt-local-settings", "/decrypt-local-settings"];

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

            if (args.Any(IsUpdatePreflightArgument))
            {
                var updatePreflightText = GetUpdatePreflightText();
                Console.WriteLine(updatePreflightText);
                StartupLoggingUtility.Append("update preflight", updatePreflightText);
                return;
            }

            if (TryRunLocalSettingsCommand(args, Console.Out))
            {
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
            OutboundNetworkDiagnosticsUtility.Reset();
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

        internal static bool IsUpdatePreflightArgument(string argument)
        {
            return UpdatePreflightArguments.Any(updatePreflightArgument =>
                string.Equals(argument, updatePreflightArgument, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsEncryptLocalSettingsArgument(string argument)
        {
            return EncryptLocalSettingsArguments.Any(encryptArgument =>
                string.Equals(argument, encryptArgument, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsDecryptLocalSettingsArgument(string argument)
        {
            return DecryptLocalSettingsArguments.Any(decryptArgument =>
                string.Equals(argument, decryptArgument, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool TryRunLocalSettingsCommand(string[] args, TextWriter output)
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(output);

            if (TryGetCommandIndex(args, IsEncryptLocalSettingsArgument, out var encryptCommandIndex))
            {
                var (sourcePath, destinationPath) = ResolveLocalSettingsCommandPaths(args, encryptCommandIndex);
                EncryptLocalSettings(sourcePath, destinationPath);
                output.WriteLine($"Encrypted '{sourcePath}' to '{destinationPath}' using portable local-settings protection.");
                return true;
            }

            if (TryGetCommandIndex(args, IsDecryptLocalSettingsArgument, out var decryptCommandIndex))
            {
                var (sourcePath, destinationPath) = ResolveLocalSettingsCommandPaths(args, decryptCommandIndex);
                var plaintextJson = GetDecryptedLocalSettingsJson(sourcePath);
                if (destinationPath is null)
                {
                    output.WriteLine(plaintextJson);
                }
                else
                {
                    File.WriteAllText(destinationPath, plaintextJson);
                    output.WriteLine($"Decrypted '{sourcePath}' to '{destinationPath}'.");
                }

                return true;
            }

            return false;
        }

        internal static void EncryptLocalSettings(string sourcePath, string? destinationPath = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

            var resolvedSourcePath = Path.GetFullPath(sourcePath);
            var resolvedDestinationPath = Path.GetFullPath(destinationPath ?? sourcePath);
            var settings = LocalSettingsUtility.LoadSettingsWithoutMigration(resolvedSourcePath);
            LocalSettingsUtility.SavePortableEncryptedSettings(resolvedDestinationPath, settings);
        }

        internal static string GetDecryptedLocalSettingsJson(string sourcePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

            var settings = LocalSettingsUtility.LoadSettingsWithoutMigration(Path.GetFullPath(sourcePath));
            var plaintextSettings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["schema_version"] = LocalSettingsSchemaVersion
            };

            foreach (var pair in settings)
            {
                plaintextSettings[pair.Key] = pair.Value;
            }

            return JsonSerializer.Serialize(
                plaintextSettings,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
        }

        private static bool TryGetCommandIndex(
            IReadOnlyList<string> args,
            Func<string, bool> matches,
            out int commandIndex)
        {
            for (var index = 0; index < args.Count; index++)
            {
                if (matches(args[index]))
                {
                    commandIndex = index;
                    return true;
                }
            }

            commandIndex = -1;
            return false;
        }

        private static (string SourcePath, string? DestinationPath) ResolveLocalSettingsCommandPaths(
            IReadOnlyList<string> args,
            int commandIndex)
        {
            var sourcePath = GetOptionalPathArgument(args, commandIndex + 1)
                ?? Path.Combine(Environment.CurrentDirectory, LocalSettingsFileName);
            var destinationPath = GetOptionalPathArgument(args, commandIndex + 2);
            return (Path.GetFullPath(sourcePath), destinationPath is null ? null : Path.GetFullPath(destinationPath));
        }

        private static string? GetOptionalPathArgument(IReadOnlyList<string> args, int index)
        {
            if (index >= args.Count)
            {
                return null;
            }

            var value = args[index];
            return IsCommandArgument(value)
                ? null
                : value;
        }

        private static bool IsCommandArgument(string value)
        {
            return IsVersionArgument(value)
                || IsHealthArgument(value)
                || IsUpdatePreflightArgument(value)
                || IsEncryptLocalSettingsArgument(value)
                || IsDecryptLocalSettingsArgument(value);
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

        internal static string GetUpdatePreflightText()
        {
            try
            {
                using var httpClient = NetworkRequestUtility.CreateHttpClient();
                var update = PlayerAssistantUpdateUtility
                    .CheckForLatestUpdateAsync(httpClient)
                    .GetAwaiter()
                    .GetResult();
                var currentVersion = PlayerAssistantUpdateUtility.GetCurrentAppVersion();
                var lines = new List<string>
                {
                    GetVersionText(),
                    $"update-manifest: {PlayerAssistantUpdateUtility.UpdateManifestUri}",
                    $"update-signature: {PlayerAssistantUpdateUtility.UpdateManifestSignatureUri}",
                    $"current-version: {currentVersion}"
                };

                if (update is null)
                {
                    lines.Add("status: no-update");
                }
                else
                {
                    lines.Add(update.IsNewerThan(currentVersion) ? "status: update-available" : "status: current");
                    lines.Add($"latest-version: {update.VersionText}");
                    lines.Add($"installer: {update.InstallerUri}");
                }

                return string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                return string.Join(
                    Environment.NewLine,
                    GetVersionText(),
                    $"update-manifest: {PlayerAssistantUpdateUtility.UpdateManifestUri}",
                    $"update-signature: {PlayerAssistantUpdateUtility.UpdateManifestSignatureUri}",
                    "status: error",
                    $"Error: {SensitiveTextRedactionUtility.Redact(ex.Message)}");
            }
        }
    }
}
