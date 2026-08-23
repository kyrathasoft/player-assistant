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
        private static readonly string[] HashXpPasswordsArguments = ["--hash-xp-passwords", "/hash-xp-passwords"];
        private static readonly string[] PublishRpolSnapshotsArguments = ["--publish-rpol-snapshots", "/publish-rpol-snapshots"];
        private static readonly string[] RpolStateProofArguments = ["--rpol-state-proof", "/rpol-state-proof"];
        private const string RpolRunIdArgument = "--rpol-run-id";
        private const string RpolResultPathArgument = "--rpol-result-path";
        private const string RpolCdpEndpointArgument = "--rpol-cdp-endpoint";

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

            if (args.Any(IsRpolStateProofArgument))
            {
                RunRpolStateProof(GetRpolCdpEndpoint(args));
                return;
            }

            if (args.Any(IsPublishRpolSnapshotsArgument))
            {
                RunRpolSnapshotPublisher(args);
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

            _ = OrcishTranslatorWarmupUtility.StartPreloading();
            _ = ElvenTranslatorWarmupUtility.StartPreloading();

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

        internal static bool IsPublishRpolSnapshotsArgument(string argument)
        {
            return PublishRpolSnapshotsArguments.Any(candidate =>
                string.Equals(argument, candidate, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsRpolStateProofArgument(string argument)
        {
            return RpolStateProofArguments.Any(candidate =>
                string.Equals(argument, candidate, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsHashXpPasswordsArgument(string argument)
        {
            return HashXpPasswordsArguments.Any(hashArgument =>
                string.Equals(argument, hashArgument, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool TryRunLocalSettingsCommand(string[] args, TextWriter output)
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(output);

            if (TryGetCommandIndex(args, IsHashXpPasswordsArgument, out var hashXpPasswordsCommandIndex))
            {
                var sourcePath = GetOptionalPathArgument(args, hashXpPasswordsCommandIndex + 1)
                    ?? Path.Combine(Environment.CurrentDirectory, XpPasswordStoreUtility.FileName);
                var destinationPath = GetOptionalPathArgument(args, hashXpPasswordsCommandIndex + 2);
                var entryCount = XpPasswordStoreUtility.ConvertEncryptedSidecarToPasswordHashes(
                    Path.GetFullPath(sourcePath),
                    destinationPath is null ? null : Path.GetFullPath(destinationPath));
                output.WriteLine(
                    $"Converted {entryCount} XP password entries to salted hashes in " +
                    $"'{Path.GetFullPath(destinationPath ?? sourcePath)}'.");
                return true;
            }

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
                || IsDecryptLocalSettingsArgument(value)
                || IsHashXpPasswordsArgument(value)
                || IsPublishRpolSnapshotsArgument(value)
                || IsRpolStateProofArgument(value)
                || string.Equals(value, RpolRunIdArgument, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, RpolResultPathArgument, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, RpolCdpEndpointArgument, StringComparison.OrdinalIgnoreCase);
        }

        private static void RunRpolStateProof(string? cdpEndpoint)
        {
            try
            {
                AppSettingsUtility.Load();
                RpolAuthUtility.VerifyCandidateStorageStateInPublisherProcessAsync(
                    CancellationToken.None,
                    cdpEndpoint).GetAwaiter().GetResult();
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                StartupLoggingUtility.Append("RPOL candidate proof", SensitiveTextRedactionUtility.Redact(ex.Message));
                Environment.ExitCode = 1;
            }
        }

        private static string? GetRpolCdpEndpoint(IReadOnlyList<string> args)
        {
            for (var index = 0; index + 1 < args.Count; index++)
            {
                if (!string.Equals(args[index], RpolCdpEndpointArgument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = args[index + 1];
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                    || !string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                    || (uri.Host is not ("127.0.0.1" or "localhost"))
                    || uri.AbsolutePath != "/")
                {
                    throw new ArgumentException("The RPOL CDP endpoint must be an HTTP loopback endpoint.", nameof(args));
                }
                return value;
            }
            return null;
        }

        private static void RunRpolSnapshotPublisher(string[] args)
        {
            RunRpolSnapshotPublisherAsync(args).GetAwaiter().GetResult();
        }

        private static async Task RunRpolSnapshotPublisherAsync(string[] args)
        {
            var runId = GetRpolRunId(args) ?? Guid.NewGuid().ToString("N");
            if (!Guid.TryParseExact(runId, "N", out _))
            {
                throw new ArgumentException("The RPOL run ID must be a GUID in N format.", nameof(args));
            }
            var startedAt = DateTimeOffset.UtcNow;
            var reportPath = GetRpolResultPath(args, runId);
            var stage = "starting";
            var timeoutCategory = (string?)null;
            var cleanupErrors = new List<string>();
            RpolSnapshotPublishReport? report = null;
            using var deadline = RpolOperationDeadline.Create(
                TimeSpan.FromMinutes(10),
                TimeSpan.FromSeconds(30));
            RpolCrossProcessLock? operationLock = null;
            try
            {
                stage = "lock";
                operationLock = await RpolCrossProcessLock.AcquireAsync(
                    RpolCrossProcessLock.AuthAndPublisherName,
                    TimeSpan.FromSeconds(5),
                    deadline.OperationToken);
                stage = "settings";
                await Task.Run(AppSettingsUtility.Load, deadline.OperationToken).WaitAsync(deadline.OperationToken);
                stage = "publishing";
                report = await RpolSnapshotUtility.PublishAsync(deadline.OperationToken, operationLock).WaitAsync(deadline.OperationToken);
            }
            catch (OperationCanceledException) when (deadline.OperationToken.IsCancellationRequested)
            {
                timeoutCategory = "end-to-end-deadline";
                report = new RpolSnapshotPublishReport(
                    -1,
                    0,
                    1,
                    ["RPOL publishing exceeded its end-to-end deadline."],
                    Attempted: 0,
                    TargetOutcomes: []);
            }
            catch (TimeoutException ex)
            {
                timeoutCategory = "operation-timeout";
                report = new RpolSnapshotPublishReport(-1, 0, 1, [SensitiveTextRedactionUtility.Redact(ex.Message)], 0, []);
            }
            catch (Exception ex)
            {
                report = new RpolSnapshotPublishReport(-1, 0, 1, [SensitiveTextRedactionUtility.Redact(ex.Message)], 0, []);
            }
            finally
            {
                if (report is not null)
                {
                    stage = RpolSnapshotUtility.IsSuccessfulPublishReport(report)
                        ? "published"
                        : stage == "publishing" ? "target-failed" : stage;
                }

                try
                {
                    await RpolAuthUtility.DisposeCurrentSessionAsync(deadline.CleanupToken);
                }
                catch (Exception ex)
                {
                    cleanupErrors.Add(SensitiveTextRedactionUtility.Redact(ex.Message));
                    stage = "disposal";
                }

                try
                {
                    operationLock?.Dispose();
                }
                catch (Exception ex)
                {
                    cleanupErrors.Add(SensitiveTextRedactionUtility.Redact(ex.Message));
                    stage = "disposal";
                }
            }

            report ??= new RpolSnapshotPublishReport(-1, 0, 1, ["RPOL publisher ended without a result."], 0, []);
            var endedAt = DateTimeOffset.UtcNow;
            var result = RpolPublishResultRecord.FromReport(
                report,
                runId,
                startedAt,
                endedAt,
                stage,
                timeoutCategory,
                cleanupErrors);
            try
            {
                RpolPublishResultValidator.WriteAtomic(reportPath, result);
            }
            catch
            {
                // The wrapper owns the crash/timeout fallback when the application cannot write its result.
            }

            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = result.TerminalStatus == "success" ? 0 : 1;
        }

        private static string? GetRpolRunId(IReadOnlyList<string> args)
        {
            for (var index = 0; index + 1 < args.Count; index++)
            {
                if (string.Equals(args[index], RpolRunIdArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrWhiteSpace(args[index + 1]) ? null : args[index + 1];
                }
            }

            return null;
        }

        internal static string GetRpolResultPathForTests(IReadOnlyList<string> args, string runId)
            => GetRpolResultPath(args, runId);

        private static string GetRpolResultPath(IReadOnlyList<string> args, string runId)
        {
            if (!Guid.TryParseExact(runId, "N", out _))
            {
                throw new InvalidOperationException("The RPOL result path requires the exact current run ID in N-format GUID form.");
            }

            string? suppliedPath = null;
            for (var index = 0; index + 1 < args.Count; index++)
            {
                if (string.Equals(args[index], RpolResultPathArgument, StringComparison.OrdinalIgnoreCase))
                {
                    suppliedPath = args[index + 1];
                    break;
                }
            }

            var expectedPath = RuntimePathUtility.CombineUnderBase(
                AppContext.BaseDirectory,
                "rpol-results",
                runId,
                "result.json");
            var path = suppliedPath is null ? expectedPath : Path.GetFullPath(suppliedPath);
            if (!string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The RPOL result path must be exactly '<exe>/rpol-results/<run-id>/result.json'.");
            }

            return path;
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
