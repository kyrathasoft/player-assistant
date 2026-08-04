using PlayerAssistant;
using Microsoft.Playwright;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Windows.Forms;
using System.Xml.Linq;

using static TestSupport;

internal static class RuntimeNetworkTests
{
    internal static void RuntimeHousekeepingRemovesStaleTempAndAtomicFiles()
    {
        using var directory = TemporaryDirectory.Create();
        var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        var tempDirectory = Path.Combine(directory.Path, "temp");
        Directory.CreateDirectory(tempDirectory);
        var staleTempPath = Path.Combine(tempDirectory, "old-download.tmp");
        var atomicPath = AtomicFileUtility.CreateTempPath(Path.Combine(directory.Path, "keyword-index.json"));
        File.WriteAllText(staleTempPath, "temp");
        File.WriteAllText(atomicPath, "atomic");
        SetLastWriteTimeUtc(staleTempPath, now - TimeSpan.FromHours(2));
        SetLastWriteTimeUtc(atomicPath, now - TimeSpan.FromHours(2));

        var report = RuntimeHousekeepingUtility.Clean(
            directory.Path,
            now,
            new RuntimeHousekeepingOptions
            {
                StaleTempFileAge = TimeSpan.FromHours(1),
                OrphanedAtomicFileAge = TimeSpan.FromHours(1)
            });

        AssertFalse(File.Exists(staleTempPath), "stale temp file should be removed");
        AssertFalse(File.Exists(atomicPath), "stale atomic temp file should be removed");
        AssertEqual(2, report.RemovedFileCount, "unexpected removed file count");
        AssertTrue(report.ReclaimedBytes > 0, "expected reclaimed bytes to be reported");
    }

    internal static void RuntimeHousekeepingPreservesFreshAndUnrelatedTmpFiles()
    {
        using var directory = TemporaryDirectory.Create();
        var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        var tempDirectory = Path.Combine(directory.Path, "temp");
        Directory.CreateDirectory(tempDirectory);
        var freshTempPath = Path.Combine(tempDirectory, "fresh-download.tmp");
        var unrelatedTmpPath = Path.Combine(directory.Path, "cache.txt.tmp");
        File.WriteAllText(freshTempPath, "temp");
        File.WriteAllText(unrelatedTmpPath, "not an atomic temp file");
        SetLastWriteTimeUtc(freshTempPath, now - TimeSpan.FromMinutes(5));
        SetLastWriteTimeUtc(unrelatedTmpPath, now - TimeSpan.FromDays(7));

        var report = RuntimeHousekeepingUtility.Clean(
            directory.Path,
            now,
            new RuntimeHousekeepingOptions
            {
                StaleTempFileAge = TimeSpan.FromHours(1),
                OrphanedAtomicFileAge = TimeSpan.FromHours(1)
            });

        AssertTrue(File.Exists(freshTempPath), "fresh temp file should be preserved");
        AssertTrue(File.Exists(unrelatedTmpPath), "unrelated tmp file should be preserved");
        AssertEqual(0, report.RemovedFileCount, "fresh/unrelated files should not be removed");
    }

    internal static void RuntimeHousekeepingRemovesOldQuarantinedJsonOnly()
    {
        using var directory = TemporaryDirectory.Create();
        var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        var oldBadPath = Path.Combine(directory.Path, "keyword-index.bad-20260601-010203-004.json");
        var freshBadPath = Path.Combine(directory.Path, "settings.bad-20260702-010203-004.json");
        var normalJsonPath = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(oldBadPath, "{}");
        File.WriteAllText(freshBadPath, "{}");
        File.WriteAllText(normalJsonPath, "{}");
        SetLastWriteTimeUtc(oldBadPath, now - TimeSpan.FromDays(15));
        SetLastWriteTimeUtc(freshBadPath, now - TimeSpan.FromDays(1));
        SetLastWriteTimeUtc(normalJsonPath, now - TimeSpan.FromDays(30));

        var report = RuntimeHousekeepingUtility.Clean(
            directory.Path,
            now,
            new RuntimeHousekeepingOptions
            {
                QuarantinedJsonRetention = TimeSpan.FromDays(14)
            });

        AssertFalse(File.Exists(oldBadPath), "old quarantined json should be removed");
        AssertTrue(File.Exists(freshBadPath), "fresh quarantined json should be preserved");
        AssertTrue(File.Exists(normalJsonPath), "normal json should be preserved");
        AssertEqual(1, report.RemovedFileCount, "unexpected removed quarantine count");
    }

    internal static void RuntimeHousekeepingRemovesOldBackupFilesOnly()
    {
        using var directory = TemporaryDirectory.Create();
        var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        var oldBackupPath = Path.Combine(directory.Path, "keyword-index.bak-20260601-010203-004.json");
        var freshBackupPath = Path.Combine(directory.Path, "settings.bak-20260702-010203-004.json");
        var normalJsonPath = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(oldBackupPath, "{}");
        File.WriteAllText(freshBackupPath, "{}");
        File.WriteAllText(normalJsonPath, "{}");
        SetLastWriteTimeUtc(oldBackupPath, now - TimeSpan.FromDays(31));
        SetLastWriteTimeUtc(freshBackupPath, now - TimeSpan.FromDays(1));
        SetLastWriteTimeUtc(normalJsonPath, now - TimeSpan.FromDays(60));

        var report = RuntimeHousekeepingUtility.Clean(
            directory.Path,
            now,
            new RuntimeHousekeepingOptions
            {
                RuntimeBackupRetention = TimeSpan.FromDays(30)
            });

        AssertFalse(File.Exists(oldBackupPath), "old backup should be removed");
        AssertTrue(File.Exists(freshBackupPath), "fresh backup should be preserved");
        AssertTrue(File.Exists(normalJsonPath), "normal json should be preserved");
        AssertEqual(1, report.RemovedFileCount, "unexpected removed backup count");
    }

    internal static void RuntimeHousekeepingRotatesOversizedStartupLog()
    {
        using var directory = TemporaryDirectory.Create();
        var logPath = Path.Combine(directory.Path, StartupLoggingUtility.LogFileName);
        var archivePath = Path.Combine(directory.Path, "startup-errors.log.1");
        File.WriteAllText(logPath, new string('x', 128));

        var report = RuntimeHousekeepingUtility.Clean(
            directory.Path,
            new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero),
            new RuntimeHousekeepingOptions
            {
                MaxStartupLogBytes = 32
            });

        AssertTrue(report.StartupLogRotated, "oversized startup log should be rotated");
        AssertTrue(File.Exists(logPath), "active startup log should be recreated after rotation");
        AssertTrue(File.Exists(archivePath), "startup log archive should be written");
        AssertEqual(128L, new FileInfo(archivePath).Length, "archive should contain the original log");
        AssertContains(File.ReadAllText(logPath), "rotated to startup-errors.log.1");
    }

    internal static void RuntimeHousekeepingSkipsLockedFiles()
    {
        using var directory = TemporaryDirectory.Create();
        var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        var tempDirectory = Path.Combine(directory.Path, "temp");
        Directory.CreateDirectory(tempDirectory);
        var lockedPath = Path.Combine(tempDirectory, "locked.tmp");
        File.WriteAllText(lockedPath, "locked");
        SetLastWriteTimeUtc(lockedPath, now - TimeSpan.FromDays(2));

        using (new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var report = RuntimeHousekeepingUtility.Clean(
                directory.Path,
                now,
                new RuntimeHousekeepingOptions
                {
                    StaleTempFileAge = TimeSpan.FromHours(1)
                });

            AssertTrue(File.Exists(lockedPath), "locked file should be preserved");
            AssertEqual(1, report.SkippedFileCount, "locked file should be counted as skipped");
        }
    }

    internal static void UiOperationFailureReporterLogsStatusAndDialog()
    {
        var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;
        var statusMessages = new List<string>();
        var dialogs = new List<(string Title, string Message)>();

        try
        {
            if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }

            UiOperationFailureReporter.ReportAsync(
                new UiOperationFailure(
                    "login info display",
                    "Login info unavailable",
                    "Login Info Error",
                    new InvalidOperationException("cached login info is malformed"),
                    ShowDialog: true),
                statusMessages.Add,
                (title, message) => dialogs.Add((title, message))).GetAwaiter().GetResult();

            AssertEqual(1, statusMessages.Count, "expected reporter to set one status message");
            AssertEqual(
                "Login info unavailable: cached login info is malformed",
                statusMessages[0],
                "unexpected reporter status message");
            AssertEqual(1, dialogs.Count, "expected reporter to show one warning dialog");
            AssertEqual("Login Info Error", dialogs[0].Title, "unexpected dialog title");
            AssertEqual("cached login info is malformed", dialogs[0].Message, "unexpected dialog message");

            var log = File.ReadAllText(startupLogPath);
            AssertContains(log, "login info display");
            AssertContains(log, "InvalidOperationException");
            AssertContains(log, "cached login info is malformed");
        }
        finally
        {
            if (hadStartupLog)
            {
                File.WriteAllText(startupLogPath, originalStartupLog);
            }
            else if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }
        }
    }

    internal static void StatusBarActivityIndicatorTracksAsyncOperations()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var indicator = (ToolStripStatusLabel)(GetPrivateField(form, "statusActivityToolStripStatusLabel")
                ?? throw new InvalidOperationException("status activity indicator was null."));

            AssertEqual(0, (int)(GetPrivateField(form, "_activeAsyncOperationCount") ?? -1), "activity count should start at zero");
            AssertEqual(string.Empty, indicator.Text ?? string.Empty, "activity indicator text should start empty");
            using var firstActivity = (IDisposable)(InvokePrivateMethod(form, "BeginStatusBarActivity")
                ?? throw new InvalidOperationException("first activity scope was null."));

            AssertEqual(1, (int)(GetPrivateField(form, "_activeAsyncOperationCount") ?? -1), "activity count should increment while async work is active");
            AssertFalse(string.IsNullOrWhiteSpace(indicator.Text), "activity indicator should display an animation frame while active");
            var firstFrame = indicator.Text ?? string.Empty;
            InvokePrivateMethod(form, "AdvanceStatusActivityIndicator");
            AssertFalse(
                string.Equals(firstFrame, indicator.Text, StringComparison.Ordinal),
                "activity indicator should advance animation frames");

            using (var secondActivity = (IDisposable)(InvokePrivateMethod(form, "BeginStatusBarActivity")
                ?? throw new InvalidOperationException("second activity scope was null.")))
            {
                firstActivity.Dispose();
                AssertEqual(1, (int)(GetPrivateField(form, "_activeAsyncOperationCount") ?? -1), "activity count should remain positive until all async work completes");
            }

            AssertEqual(0, (int)(GetPrivateField(form, "_activeAsyncOperationCount") ?? -1), "activity count should return to zero after all async work completes");
            AssertEqual(string.Empty, indicator.Text ?? string.Empty, "activity indicator text should clear when idle");
        });
    }

    internal static void BackgroundTaskSupervisorSuppressesDuplicatePhases()
    {
        using var supervisor = new BackgroundTaskSupervisor();
        var releaseTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startCount = 0;

        AssertTrue(
            supervisor.TryStart("duplicate phase", async cancellationToken =>
            {
                Interlocked.Increment(ref startCount);
                await releaseTask.Task.WaitAsync(cancellationToken);
            }),
            "expected first background task to start");

        WaitForCondition(() => Volatile.Read(ref startCount) == 1, "background task did not start");
        AssertTrue(supervisor.IsRunning("duplicate phase"), "expected background phase to be running");
        AssertFalse(
            supervisor.TryStart("duplicate phase", _ =>
            {
                Interlocked.Increment(ref startCount);
                return Task.CompletedTask;
            }),
            "expected duplicate background phase to be suppressed");

        releaseTask.SetResult();
        WaitForCondition(() => !supervisor.IsRunning("duplicate phase"), "background task did not complete");
        AssertEqual(1, Volatile.Read(ref startCount), "duplicate phase should not start twice");
    }

    internal static void BackgroundTaskSupervisorLogsFailures()
    {
        var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

        try
        {
            if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }

            using var supervisor = new BackgroundTaskSupervisor();
            AssertTrue(
                supervisor.TryStart("supervised failure", _ => throw new InvalidOperationException("supervised boom")),
                "expected failing background task to start");
            WaitForCondition(() => !supervisor.IsRunning("supervised failure"), "failing background task did not complete");

            var log = File.ReadAllText(startupLogPath);
            AssertContains(log, "supervised failure");
            AssertContains(log, "InvalidOperationException");
            AssertContains(log, "supervised boom");
        }
        finally
        {
            if (hadStartupLog)
            {
                File.WriteAllText(startupLogPath, originalStartupLog);
            }
            else if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }
        }
    }

    internal static void BackgroundTaskSupervisorCancelsRunningTasksOnDispose()
    {
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var supervisor = new BackgroundTaskSupervisor();

        AssertTrue(
            supervisor.TryStart("cancellable phase", async cancellationToken =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.SetResult();
                    throw;
                }
            }),
            "expected cancellable background task to start");

        supervisor.Dispose();
        AssertTrue(cancellationObserved.Task.Wait(TimeSpan.FromSeconds(2)), "expected disposal to cancel running background task");
    }

    internal static void AtomicFilePromotionPreservesExistingDestinationOnLockedReplacement()
    {
        using var directory = TemporaryDirectory.Create();
        var destinationPath = Path.Combine(directory.Path, "cache.txt");
        var tempPath = Path.Combine(directory.Path, "cache.txt.tmp");
        File.WriteAllText(destinationPath, "old cache");
        File.WriteAllText(tempPath, "new cache");

        using (new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try
            {
                AtomicFileUtility.PromoteTempFileAsync(tempPath, destinationPath).GetAwaiter().GetResult();
                throw new InvalidOperationException("expected locked destination promotion to fail");
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        AssertEqual("old cache", File.ReadAllText(destinationPath), "existing cache should survive failed promotion");
        AssertTrue(File.Exists(tempPath), "temp file should remain for caller cleanup after failed promotion");
    }

    internal static void AtomicFilePromotionCreatesBoundedRuntimeBackups()
    {
        using var directory = TemporaryDirectory.Create();
        var destinationPath = Path.Combine(directory.Path, "keyword-index.json");
        File.WriteAllText(destinationPath, """{"version":0}""");

        for (var index = 1; index <= 7; index++)
        {
            AtomicFileUtility.WriteAllText(destinationPath, $$"""{"version":{{index}}}""");
            Thread.Sleep(2);
        }

        AssertEqual("""{"version":7}""", File.ReadAllText(destinationPath), "destination should contain newest content");
        var backups = Directory.GetFiles(directory.Path, "keyword-index.bak-*.json");
        AssertEqual(5, backups.Length, "runtime backup retention should keep the newest five backups");
        AssertTrue(
            backups.Any(path => File.ReadAllText(path).Contains("\"version\":6", StringComparison.Ordinal)),
            "newest previous content should be backed up");
        AssertFalse(
            backups.Any(path => File.ReadAllText(path).Contains("\"version\":0", StringComparison.Ordinal)),
            "oldest backup should be pruned");
    }

    internal static void NetworkRequestRetriesTransientFailures()
    {
        var attempts = 0;
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler(async (_, _) =>
        {
            attempts++;
            await Task.Yield();
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok")
                };
        }));

        using var response = NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170&test=retry"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 2, TimeSpan.Zero)).GetAwaiter().GetResult();

        AssertEqual(HttpStatusCode.OK, response.StatusCode, "expected retry to return successful response");
        AssertEqual(2, attempts, "expected transient response to be retried once");
    }

    internal static void OutboundNetworkDiagnosticsRecordsSanitizedSuccessEndpoint()
    {
        var diagnosticsPath = Path.Combine(
            Path.GetDirectoryName(typeof(NetworkRequestUtility).Assembly.Location)
                ?? throw new InvalidOperationException("Unable to resolve test assembly directory."),
            OutboundNetworkDiagnosticsUtility.DiagnosticsFileName);
        WithPreservedFileAbsent(diagnosticsPath, () =>
        {
            OutboundNetworkDiagnosticsUtility.Reset();
            using var allowlistScope = NetworkUrlAllowlistUtility.UseValidationOverrideForTests((uri, purpose) =>
            {
                if (purpose == NetworkUrlPurpose.Generic
                    && string.Equals(uri.Host, "example.test", StringComparison.OrdinalIgnoreCase))
                {
                    return NetworkUrlAllowlistValidation.Allowed(uri);
                }

                return null;
            });
            using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok")
                })));

            using var response = NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://example.test/search/path?token=secret-token&password=secret-password"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult();

            AssertEqual(HttpStatusCode.OK, response.StatusCode, "expected successful response");
            var diagnosticsJson = File.ReadAllText(diagnosticsPath);
            AssertFalse(diagnosticsJson.Contains("secret-token", StringComparison.Ordinal), "outbound diagnostics should not persist token query values");
            AssertFalse(diagnosticsJson.Contains("secret-password", StringComparison.Ordinal), "outbound diagnostics should not persist password query values");

            using var document = System.Text.Json.JsonDocument.Parse(diagnosticsJson);
            var endpoint = document.RootElement.GetProperty("endpoints")[0];
            AssertEqual("Generic", endpoint.GetProperty("purpose").GetString() ?? string.Empty, "unexpected network purpose");
            AssertEqual("example.test", endpoint.GetProperty("host").GetString() ?? string.Empty, "unexpected host");
            AssertEqual("/search/path", endpoint.GetProperty("path").GetString() ?? string.Empty, "diagnostics should record path without query values");
            AssertTrue(endpoint.GetProperty("query_present").GetBoolean(), "diagnostics should remember that a query string existed");
            AssertEqual(1, endpoint.GetProperty("total_requests").GetInt32(), "expected one recorded request");
            AssertEqual(1, endpoint.GetProperty("success_count").GetInt32(), "expected one successful request");
            AssertEqual(0, endpoint.GetProperty("failure_count").GetInt32(), "expected no failures");
        });
    }

    internal static void OutboundNetworkDiagnosticsRecordsFailureCounts()
    {
        var diagnosticsPath = Path.Combine(
            Path.GetDirectoryName(typeof(NetworkRequestUtility).Assembly.Location)
                ?? throw new InvalidOperationException("Unable to resolve test assembly directory."),
            OutboundNetworkDiagnosticsUtility.DiagnosticsFileName);
        WithPreservedFileAbsent(diagnosticsPath, () =>
        {
            OutboundNetworkDiagnosticsUtility.Reset();
            using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    ReasonPhrase = "Service Unavailable"
                })));

            using var response = NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero),
                purpose: NetworkUrlPurpose.Rpol).GetAwaiter().GetResult();

            AssertEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode, "expected terminal 503 response");

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(diagnosticsPath));
            var endpoint = document.RootElement.GetProperty("endpoints")[0];
            AssertEqual("Rpol", endpoint.GetProperty("purpose").GetString() ?? string.Empty, "unexpected network purpose");
            AssertEqual("/game.php", endpoint.GetProperty("path").GetString() ?? string.Empty, "unexpected RPOL path");
            AssertEqual(1, endpoint.GetProperty("total_requests").GetInt32(), "expected one recorded request");
            AssertEqual(0, endpoint.GetProperty("success_count").GetInt32(), "expected no successes");
            AssertEqual(1, endpoint.GetProperty("failure_count").GetInt32(), "expected one failure");
            AssertEqual(503, endpoint.GetProperty("last_status_code").GetInt32(), "expected terminal HTTP status to be recorded");
            AssertEqual("failure", endpoint.GetProperty("last_outcome").GetString() ?? string.Empty, "expected failure outcome");
            AssertTrue(endpoint.GetProperty("last_failure_summary").GetString()?.Contains("HTTP 503", StringComparison.Ordinal) ?? false, "expected failure summary to capture status");
        });
    }

    internal static void NetworkRequestRejectsDisallowedHostBeforeSend()
    {
        var attempts = 0;
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));

        var exception = AssertThrows<InvalidOperationException>(() =>
            NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://unexpected.example.test/blocked"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult());

        AssertContains(exception.Message, "not allowed");
        AssertEqual(0, attempts, "disallowed requests should be rejected before the HTTP handler runs");
    }

    internal static void NetworkRequestDoesNotRetryUnauthorized()
    {
        var attempts = 0;
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }));

        using var response = NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170&test=auth"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 3, TimeSpan.Zero)).GetAwaiter().GetResult();

        AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode, "expected unauthorized response to be returned to caller");
        AssertEqual(1, attempts, "unauthorized response should not be retried");
    }

    internal static void NetworkCircuitBreakerOpensAfterRepeatedTerminalFailures()
    {
        WithPreservedStartupLog(() =>
        {
            NetworkRequestUtility.ResetCircuitBreakersForTests();
            var attempts = 0;
            using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    ReasonPhrase = "Service Unavailable"
                });
            }));

            using (NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170&test=breaker-one"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult())
            {
            }

            using (NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170&test=breaker-two"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult())
            {
            }

            var exception = AssertThrows<NetworkRequestException>(() =>
                NetworkRequestUtility.SendAsync(
                    httpClient,
                    () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170&test=breaker-three"),
                    policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult());

            AssertEqual(NetworkFailureKind.CircuitOpen, exception.Kind, "expected repeated terminal failures to open the circuit breaker");
            AssertEqual(2, attempts, "open circuit breaker should short-circuit before sending another request");
            AssertContains(File.ReadAllText(GetStartupLogPath()), "network circuit breaker");
            NetworkRequestUtility.ResetCircuitBreakersForTests();
        });
    }

    internal static void NetworkCircuitBreakerClearsAfterSuccess()
    {
        NetworkRequestUtility.ResetCircuitBreakersForTests();
        var attempts = 0;
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK));
        }));

        using (NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://publish.obsidian.md/breaker-clear-one"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult())
        {
        }

        using (NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://publish.obsidian.md/breaker-clear-two"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult())
        {
        }

        using var response = NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://publish.obsidian.md/breaker-clear-three"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult();

        AssertEqual(HttpStatusCode.OK, response.StatusCode, "successful request should clear prior circuit-breaker failures");
        AssertEqual(3, attempts, "successful request should allow the next related request to be sent");
        NetworkRequestUtility.ResetCircuitBreakersForTests();
    }

    internal static void StartupDependencyMatrixClassifiesTerminalNetworkFailure()
    {
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
            throw new HttpRequestException("synthetic DNS failure")));

        var exception = AssertThrows<NetworkRequestException>(() =>
            NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170"),
                policy: new NetworkRequestPolicy(
                    TimeSpan.FromSeconds(1),
                    MaxAttempts: 1,
                    TimeSpan.Zero)).GetAwaiter().GetResult());

        AssertEqual(NetworkFailureKind.Unavailable, exception.Kind, "terminal request failures should be classified as unavailable");
        AssertContains(exception.Message, "synthetic DNS failure");
    }

    internal static void NetworkRequestWrapsTimeout()
    {
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var exception = AssertThrows<NetworkRequestException>(() =>
            NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170&test=timeout"),
                policy: new NetworkRequestPolicy(TimeSpan.FromMilliseconds(20), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult());

        AssertEqual(NetworkFailureKind.TimedOut, exception.Kind, "expected timeout failures to be classified");
    }

    internal static void NetworkRequestPreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        cancellation.Cancel();

        AssertThrows<OperationCanceledException>(() =>
            NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/cancel"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 3, TimeSpan.Zero),
                cancellationToken: cancellation.Token).GetAwaiter().GetResult());
    }

    internal static void NetworkAllowlistRejectsCredentialedAndEscapedHosts()
    {
        var credentialed = NetworkUrlAllowlistUtility.Validate("https://user:password@rpol.net/game.php", NetworkUrlPurpose.Rpol);
        var escapedHost = NetworkUrlAllowlistUtility.Validate("https://rpol%2enet/game.php", NetworkUrlPurpose.Rpol);
        var threadDisplay = NetworkUrlAllowlistUtility.Validate("https://rpol.net/display.cgi?gi=80170&ti=12&msgpage=&show=all", NetworkUrlPurpose.Rpol);
        var diceRoller = NetworkUrlAllowlistUtility.Validate("https://rpol.net/usermodules/diceroller.cgi?gi=80170", NetworkUrlPurpose.Rpol);
        var unrelatedUserModule = NetworkUrlAllowlistUtility.Validate("https://rpol.net/usermodules/admin.cgi?gi=80170", NetworkUrlPurpose.Rpol);

        AssertFalse(credentialed.IsAllowed, "credentialed URLs should not be allowed");
        AssertContains(credentialed.RejectionReason ?? string.Empty, "credentials");
        AssertFalse(escapedHost.IsAllowed, "escaped host URLs should not be allowed");
        AssertTrue(threadDisplay.IsAllowed, "RPOL thread display URLs should remain valid local search results");
        AssertTrue(diceRoller.IsAllowed, "the exact RPOL Dice Roller URL should be allowed");
        AssertFalse(unrelatedUserModule.IsAllowed, "unrelated RPOL user-module URLs should remain blocked");
    }

    internal static void NetworkAllowlistAcceptsObsidianPublishContentHosts()
    {
        var page = NetworkUrlAllowlistUtility.Validate(
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            NetworkUrlPurpose.ObsidianPublish);
        var markdown = NetworkUrlAllowlistUtility.Validate(
            "https://publish-01.obsidian.md/access/1113217a28a5bfdcc9fbe8e6d82b27ac/Intentional%20Orphans/XP%20Tracking.md",
            NetworkUrlPurpose.ObsidianPublish);
        var rejected = NetworkUrlAllowlistUtility.Validate(
            "https://help.obsidian.md/access/1113217a28a5bfdcc9fbe8e6d82b27ac/Intentional%20Orphans/XP%20Tracking.md",
            NetworkUrlPurpose.ObsidianPublish);

        AssertTrue(page.IsAllowed, "public Obsidian Publish pages should remain allowed");
        AssertTrue(markdown.IsAllowed, "Obsidian Publish generated markdown access URLs should be allowed");
        AssertTrue(
            NetworkUrlAllowlistUtility.IsObsidianPublishHost(new Uri("https://publish-01.obsidian.md/")),
            "generated Obsidian Publish content hosts should be recognized");
        AssertFalse(rejected.IsAllowed, "non-Publish obsidian.md hosts should not be accepted");
    }

    internal static void NetworkAllowlistRejectsUnexpectedHostedSettingsPath()
    {
        var allowed = NetworkUrlAllowlistUtility.Validate(
            "https://bryanmiller.us/scarlethorizons/settings.local.json",
            NetworkUrlPurpose.PlayerAssistantHostedSettings);
        var rejected = NetworkUrlAllowlistUtility.Validate(
            "https://bryanmiller.us/scarlethorizons/other-settings.json",
            NetworkUrlPurpose.PlayerAssistantHostedSettings);

        AssertTrue(allowed.IsAllowed, "expected the configured hosted settings path to remain allowed");
        AssertFalse(rejected.IsAllowed, "unexpected hosted settings paths should be rejected");
        AssertContains(rejected.RejectionReason ?? string.Empty, "settings.local.json");
    }

    internal static void NetworkAllowlistRejectsUnexpectedUpdatePath()
    {
        var allowedManifest = NetworkUrlAllowlistUtility.Validate(
            "https://bryanmiller.us/scarlethorizons/p-assist-updates.json",
            NetworkUrlPurpose.PlayerAssistantUpdate);
        var allowedArchive = NetworkUrlAllowlistUtility.Validate(
            "https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.zip",
            NetworkUrlPurpose.PlayerAssistantUpdate);
        var rejected = NetworkUrlAllowlistUtility.Validate(
            "https://bryanmiller.us/private/p-assist-0.9.1.zip",
            NetworkUrlPurpose.PlayerAssistantUpdate);

        AssertTrue(allowedManifest.IsAllowed, "expected the signed update manifest path to remain allowed");
        AssertTrue(allowedArchive.IsAllowed, "expected approved update archive paths to remain allowed");
        AssertFalse(rejected.IsAllowed, "unexpected update paths should be rejected");
        AssertContains(rejected.RejectionReason ?? string.Empty, "/scarlethorizons/");
    }

    internal static void NetworkAllowlistGenericPolicyRejectsUnrelatedUpdateHostPaths()
    {
        var genericAllowed = NetworkUrlAllowlistUtility.Validate("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.exe");
        var regionalMap = NetworkUrlAllowlistUtility.Validate("https://bryanmiller.us/scarlethorizons/northernreaches.png");
        var blogRegionalMap = NetworkUrlAllowlistUtility.Validate("https://bryanmiller.us/blog/content/bryan/blog/images/rpg-maps/northernreaches.png");
        var genericRejected = NetworkUrlAllowlistUtility.Validate("https://bryanmiller.us/random-note.txt");

        AssertTrue(genericAllowed.IsAllowed, "generic allowlist should still permit approved update artifact paths");
        AssertTrue(regionalMap.IsAllowed, "generic allowlist should permit the hosted regional map image");
        AssertTrue(blogRegionalMap.IsAllowed, "generic allowlist should permit the hosted blog regional map image");
        AssertFalse(genericRejected.IsAllowed, "generic allowlist should reject unrelated paths on an otherwise approved host");
    }

    internal static void NetworkResponseLimitsDefineDefaults()
    {
        AssertTrue(NetworkResponseContentLimit.Html.MaxBytes > 0, "HTML response limit should be positive");
        AssertTrue(NetworkResponseContentLimit.Markdown.MaxBytes > 0, "markdown response limit should be positive");
        AssertTrue(NetworkResponseContentLimit.JsonCache.MaxBytes > 0, "JSON cache response limit should be positive");
        AssertTrue(NetworkResponseContentLimit.Image.MaxBytes > 0, "image response limit should be positive");
        AssertTrue(
            NetworkResponseContentLimit.Image.MaxBytes > NetworkResponseContentLimit.Markdown.MaxBytes,
            "image downloads should allow larger payloads than markdown documents");
    }

    internal static void NetworkResponseLimitRejectsOversizedHtmlHeader()
    {
        using var content = new ByteArrayContent([]);
        content.Headers.ContentLength = NetworkResponseContentLimit.Html.MaxBytes + 1;

        var exception = AssertThrows<NetworkResponseTooLargeException>(() =>
            NetworkRequestUtility.ReadStringAsync(
                content,
                NetworkResponseContentLimit.Html).GetAwaiter().GetResult());

        AssertContains(exception.Message, "HTML response");
    }

    internal static void NetworkResponseLimitRejectsOversizedMarkdownStream()
    {
        using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("abcdef"));
        using var destination = new MemoryStream();
        var limit = new NetworkResponseContentLimit("markdown response", 5);

        var exception = AssertThrows<NetworkResponseTooLargeException>(() =>
            NetworkRequestUtility.CopyToAsync(source, destination, limit).GetAwaiter().GetResult());

        AssertContains(exception.Message, "markdown response");
        AssertEqual(0L, destination.Length, "oversized markdown stream should not be written after limit breach");
    }

    internal static void NetworkResponseLimitRejectsOversizedJsonCacheStream()
    {
        using var content = new ChunkedHttpContent(System.Text.Encoding.UTF8.GetBytes("""{"oversized":true}"""));
        var limit = new NetworkResponseContentLimit("JSON cache response", 8);

        var exception = AssertThrows<NetworkResponseTooLargeException>(() =>
            NetworkRequestUtility.ReadBytesAsync(content, limit).GetAwaiter().GetResult());

        AssertContains(exception.Message, "JSON cache response");
    }

    internal static void NetworkResponseLimitRejectsOversizedImageHeader()
    {
        using var content = new ByteArrayContent([]);
        content.Headers.ContentLength = NetworkResponseContentLimit.Image.MaxBytes + 1;

        var exception = AssertThrows<NetworkResponseTooLargeException>(() =>
            NetworkRequestUtility.CopyToAsync(
                content,
                Stream.Null,
                NetworkResponseContentLimit.Image).GetAwaiter().GetResult());

        AssertContains(exception.Message, "image response");
    }

    internal static void MarkdownAsyncFetchPreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        AssertThrows<OperationCanceledException>(() =>
            MarkdownUtility.GetMarkdownFromUrlAsync(
                "https://publish.obsidian.md/cancel",
                cancellation.Token).GetAwaiter().GetResult());
    }

    internal static void RuntimeArtifactLoaderQuarantinesMalformedJson()
    {
        using var directory = TemporaryDirectory.Create();
        var artifactPath = Path.Combine(directory.Path, "runtime-cache.json");
        File.WriteAllText(artifactPath, "{ not valid json");
        var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

        try
        {
            if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }

            var loaded = RuntimeArtifactUtility.TryLoadJson<Dictionary<string, string>>(
                artifactPath,
                "runtime artifact test",
                out var value);

            AssertFalse(loaded, "malformed runtime artifact should not load");
            AssertTrue(value is null, "malformed runtime artifact should return a null value");
            AssertFalse(File.Exists(artifactPath), "malformed runtime artifact should be moved out of the active path");

            var badFiles = Directory.GetFiles(directory.Path, "runtime-cache.bad-*.json");
            AssertEqual(1, badFiles.Length, "expected one quarantined runtime artifact");
            AssertEqual("{ not valid json", File.ReadAllText(badFiles[0]), "quarantined artifact should preserve original content");

            var startupLog = File.ReadAllText(startupLogPath);
            AssertContains(startupLog, "runtime artifact test");
            AssertContains(startupLog, badFiles[0]);
        }
        finally
        {
            if (hadStartupLog)
            {
                File.WriteAllText(startupLogPath, originalStartupLog);
            }
            else if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }
        }
    }

    internal static void RuntimeArtifactLoaderRestoresNewestValidBackup()
    {
        using var directory = TemporaryDirectory.Create();
        var artifactPath = Path.Combine(directory.Path, "runtime-cache.json");
        var olderBackupPath = Path.Combine(directory.Path, "runtime-cache.bak-20260701-010203-001.json");
        var invalidBackupPath = Path.Combine(directory.Path, "runtime-cache.bak-20260702-010203-001.json");
        var newestBackupPath = Path.Combine(directory.Path, "runtime-cache.bak-20260703-010203-001.json");
        File.WriteAllText(artifactPath, "{ not valid json");
        File.WriteAllText(olderBackupPath, """{"value":"old"}""");
        File.WriteAllText(invalidBackupPath, "{ also invalid");
        File.WriteAllText(newestBackupPath, """{"value":"restored"}""");
        SetLastWriteTimeUtc(olderBackupPath, new DateTimeOffset(2026, 7, 1, 1, 2, 3, TimeSpan.Zero));
        SetLastWriteTimeUtc(invalidBackupPath, new DateTimeOffset(2026, 7, 2, 1, 2, 3, TimeSpan.Zero));
        SetLastWriteTimeUtc(newestBackupPath, new DateTimeOffset(2026, 7, 3, 1, 2, 3, TimeSpan.Zero));

        WithPreservedStartupLog(() =>
        {
            var loaded = RuntimeArtifactUtility.TryLoadJson<Dictionary<string, string>>(
                artifactPath,
                "runtime artifact restore test",
                out var value);

            AssertTrue(loaded, "malformed runtime artifact should restore from the newest valid backup");
            AssertTrue(value is not null, "restored runtime artifact should return a value");
            AssertEqual("restored", value!["value"], "unexpected restored value");
            AssertEqual("""{"value":"restored"}""", File.ReadAllText(artifactPath), "active artifact should be restored from backup");
            AssertEqual(0, Directory.GetFiles(directory.Path, "runtime-cache.bad-*.json").Length, "restored artifact should not be quarantined");

            var startupLog = File.ReadAllText(GetStartupLogPath());
            AssertContains(startupLog, "runtime artifact restore test");
            AssertContains(startupLog, "Restored runtime artifact");
        });
    }

    internal static void StartupDependencyMatrixLogsLockedRuntimeArtifactFailures()
    {
        using var directory = TemporaryDirectory.Create();
        var artifactPath = Path.Combine(directory.Path, "locked-artifact.json");
        File.WriteAllText(artifactPath, "{}");

        WithPreservedStartupLog(() =>
        {
            using (new FileStream(artifactPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var loaded = RuntimeArtifactUtility.TryLoadJson<Dictionary<string, string>>(
                    artifactPath,
                    "locked runtime artifact test",
                    out var value);

                AssertFalse(loaded, "locked runtime artifact should fail without throwing");
                AssertTrue(value is null, "locked runtime artifact should return no value");
                AssertTrue(File.Exists(artifactPath), "locked runtime artifact should remain active when quarantine cannot move it");
            }

            var startupLog = File.ReadAllText(GetStartupLogPath());
            AssertContains(startupLog, "locked runtime artifact test");
            AssertContains(startupLog, "runtime artifact quarantine");
        });
    }

    internal static void LoginInfoCacheLoadReturnsEmptyForMalformedJson()
    {
        using var directory = TemporaryDirectory.Create();
        var loginInfoPath = Path.Combine(directory.Path, "login-info.json");
        File.WriteAllText(loginInfoPath, "{ not valid json");
        var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

        try
        {
            if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }

            var rows = (TheCastLoginInfo[]?)InvokeStaticMethod(typeof(Form1), "LoadLoginInfoJson", loginInfoPath)
                ?? throw new InvalidOperationException("LoadLoginInfoJson returned null.");

            AssertEqual(0, rows.Length, "malformed login-info cache should return an empty row set");
            AssertFalse(File.Exists(loginInfoPath), "malformed login-info cache should be moved out of the active path");

            var badFiles = Directory.GetFiles(directory.Path, "login-info.bad-*.json");
            AssertEqual(1, badFiles.Length, "expected one quarantined login-info cache");

            var startupLog = File.ReadAllText(startupLogPath);
            AssertContains(startupLog, "login info cache load");
            AssertContains(startupLog, badFiles[0]);
        }
        finally
        {
            if (hadStartupLog)
            {
                File.WriteAllText(startupLogPath, originalStartupLog);
            }
            else if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }
        }
    }

    internal static void AssetManifestLoadReturnsEmptyForMalformedJson()
    {
        var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

        try
        {
            if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }

            var args = new object[] { "{ not valid json", null! };
            var loaded = (bool)(InvokeStaticMethod(
                typeof(PlayerCharacterAssetUtility),
                "TryDeserializeAssetManifest",
                args) ?? throw new InvalidOperationException("TryDeserializeAssetManifest returned null."));

            AssertFalse(loaded, "malformed asset manifest should not load");
            AssertTrue(args[1] is Dictionary<string, string> manifest && manifest.Count == 0, "malformed asset manifest should return an empty dictionary");

            var startupLog = File.ReadAllText(startupLogPath);
            AssertContains(startupLog, "asset manifest load");
            AssertContains(startupLog, "asset manifest could not be parsed");
        }
        finally
        {
            if (hadStartupLog)
            {
                File.WriteAllText(startupLogPath, originalStartupLog);
            }
            else if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }
        }
    }

    internal static void PublishedAssetFallbackResolvesTransclusionWithoutAttachmentIndex()
    {
        var cachePaths = new[]
        {
            "Assets/hero-tokens/neria-token.webp",
            "PCs/Neria Silverdale.md"
        };

        var assets = (Dictionary<string, string>?)InvokeStaticMethod(
            typeof(ObsidianPublishUtility),
            "GetAssetPathsByFileName",
            (object)cachePaths)
            ?? throw new InvalidOperationException("GetAssetPathsByFileName returned null.");

        AssertEqual(
            "Assets/hero-tokens/neria-token.webp",
            assets["neria-token.webp"],
            "listing transclusion should resolve directly from the published asset cache");
        AssertFalse(assets.ContainsKey("Neria Silverdale.md"), "markdown pages should not be treated as image assets");
    }

    internal static void HeroTokenFileNameResolvesListingDisplayName()
    {
        const string listingMarkdown = """
            | Name | Class | Level | Token |
            | --- | --- | --- | --- |
            | [[Neria Silverdale\|Neria]] | Paladin | 1 | ![[neria-token.webp\|70]] |
            """;

        var heroName = PlayerCharacterAssetUtility.GetHeroNameForTokenFileName(
            listingMarkdown,
            "NERIA-TOKEN.WEBP")
            ?? throw new InvalidOperationException("Expected the hero token filename to resolve.");

        AssertEqual("Neria", heroName, "token filename should resolve the listing display name case-insensitively");
    }

    internal static void FormerPcListingParsesThreeColumnHeroRows()
    {
        const string listingMarkdown = """
            | Name | Class | Token |
            | --- | --- | --- |
            | [[Urvan Hall, paladin of St. Ygg\|Urvan]] | Paladin | ![[urvan-token.webp\|70]] |
            | [['Slip' Harren, Thief\|Slip]] | Thief | ![[slip-token.webp\|70]] |
            | [[Narinza Izrut\|Narinza]] | Thief | ![[narinza-token.webp\|70]] |
            """;

        var rows = PlayerCharacterAssetUtility.GetHeroRows(
            listingMarkdown,
            "https://publish.obsidian.md/scarlethorizons/PCs/Former+PCs");

        AssertEqual(3, rows.Length, "all former PC rows should parse");
        AssertEqual("Urvan", rows[0].Name, "former PC alias should become the display name");
        AssertEqual(
            "urvan-token.webp",
            rows[0].TokenFileName ?? string.Empty,
            "former PC token should parse from the Token column");
        AssertContains(rows[0].CharacterPageUrl ?? string.Empty, "Urvan+Hall");
    }

    internal static void ActiveHeroMarkdownCancellationWritesNoFiles()
    {
        using var directory = TemporaryDirectory.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        AssertThrows<OperationCanceledException>(() =>
            PlayerCharacterAssetUtility.DownloadActiveHeroMarkdownAsync(
                """
                | Name | Character | Notes | Hero |
                | --- | --- | --- | --- |
                | Alice Example | [[Alice Example]] | active | ![[alice-token.webp]] |
                """,
                "https://publish.obsidian.md/example/PCs/Player+Characters+Listing",
                directory.Path,
                cancellation.Token).GetAwaiter().GetResult());

        var activeDirectory = Path.Combine(directory.Path, "active");
        AssertFalse(
            Directory.Exists(activeDirectory) && Directory.EnumerateFiles(activeDirectory).Any(),
            "canceled hero markdown refresh should not write active hero files");
    }

    internal static void FormerHeroMarkdownCancellationWritesNoInactiveFiles()
    {
        using var directory = TemporaryDirectory.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        AssertThrows<OperationCanceledException>(() =>
            PlayerCharacterAssetUtility.DownloadFormerHeroMarkdownAsync(
                """
                | Name | Class | Token |
                | --- | --- | --- |
                | [[Urvan Hall\|Urvan]] | Paladin | ![[urvan-token.webp]] |
                """,
                "https://publish.obsidian.md/scarlethorizons/PCs/Former+PCs",
                directory.Path,
                cancellation.Token).GetAwaiter().GetResult());

        var inactiveDirectory = Path.Combine(directory.Path, "inactive");
        AssertFalse(
            Directory.Exists(inactiveDirectory) && Directory.EnumerateFiles(inactiveDirectory).Any(),
            "canceled former hero markdown download should not write inactive files");
    }

    internal static void PlayerCharacterRefreshCancellationClearsInProgressFlag()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            SetPrivateField(form, "_showWelcomeText", false);
            InvokePrivateAsync(
                form,
                "StartPlayerCharacterListingUpdateAsync",
                false,
                cancellation.Token).GetAwaiter().GetResult();

            AssertFalse(
                (bool)(GetPrivateField(form, "_playerCharacterListingUpdateStarted") ?? true),
                "canceled player-character refresh should clear the in-progress flag");
        });
    }

    internal static void HeroImageShowcaseWaitsForInitialPlayerCharacterRefresh()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: false);
            SetPrivateField(form, "_showWelcomeText", false);
            SetPrivateField(form, "_activePlayerCharacterImagePaths", new[] { "cached-nerissa-token.webp" });

            InvokePrivateMethod(form, "StartHeroImageShowcaseIfReady");
            AssertFalse(
                (bool)(GetPrivateField(form, "_heroImageIntroStarted") ?? true),
                "cached hero images should not start before the active listing has been refreshed");

            SetPrivateField(form, "_initialPlayerCharacterListingRefreshCompleted", true);
            InvokePrivateMethod(form, "StartHeroImageShowcaseIfReady");
            AssertTrue(
                (bool)(GetPrivateField(form, "_heroImageIntroStarted") ?? false),
                "the showcase should become eligible after the active listing refresh completes");
        });
    }

    internal static void GameForumStartupCancellationWritesNoManifests()
    {
        RunOnStaThread(() =>
        {
            var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
            var manifestPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "game-forum-chapter-prefixes.txt"),
                Path.Combine(AppContext.BaseDirectory, "game-forum-chapter-downloads.txt"),
                Path.Combine(AppContext.BaseDirectory, "game-forum-aside-downloads.txt"),
                Path.Combine(AppContext.BaseDirectory, "game-forum-ooc-downloads.txt")
            };
            var preservedFiles = manifestPaths
                .Append(startupLogPath)
                .Select(path => (Path: path, Exists: File.Exists(path), Content: File.Exists(path) ? File.ReadAllText(path) : null))
                .ToArray();

            try
            {
                foreach (var path in manifestPaths.Append(startupLogPath))
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }

                using var form = new Form1(suppressHeroImagesForThisRun: true);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();

                AssertThrows<OperationCanceledException>(() =>
                    InvokePrivateAsync(
                        form,
                        "LoadGameForumChapterPrefixesAsync",
                        cancellation.Token).GetAwaiter().GetResult());

                foreach (var manifestPath in manifestPaths)
                {
                    AssertFalse(File.Exists(manifestPath), $"canceled game-forum startup should not write {Path.GetFileName(manifestPath)}");
                }

                AssertFalse(File.Exists(startupLogPath), "canceled game-forum startup should not be logged as a startup failure");
            }
            finally
            {
                foreach (var preservedFile in preservedFiles)
                {
                    if (preservedFile.Exists)
                    {
                        File.WriteAllText(preservedFile.Path, preservedFile.Content ?? string.Empty);
                    }
                    else if (File.Exists(preservedFile.Path))
                    {
                        File.Delete(preservedFile.Path);
                    }
                }
            }
        });
    }

    internal static void KeywordIndexLoaderQuarantinesMalformedJson()
    {
        using var directory = TemporaryDirectory.Create();
        var indexPath = Path.Combine(directory.Path, "keyword-index.json");
        File.WriteAllText(indexPath, "{ not valid json");

        var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

        try
        {
            var loadTask = (Task?)InvokeStaticMethod(
                typeof(KeywordIndexCrawler),
                "LoadExistingDocumentAsync",
                indexPath,
                CancellationToken.None);
            loadTask?.GetAwaiter().GetResult();

            var badIndexFiles = Directory.GetFiles(directory.Path, "keyword-index.bad-*.json");
            AssertFalse(File.Exists(indexPath), "malformed keyword index should be moved out of the active path");
            AssertEqual(1, badIndexFiles.Length, "expected one quarantined keyword index file");
            AssertEqual("{ not valid json", File.ReadAllText(badIndexFiles[0]), "quarantined keyword index should preserve original content");

            var startupLog = File.ReadAllText(startupLogPath);
            AssertContains(startupLog, "keyword index recovery");
            AssertContains(startupLog, "could not be loaded");
            AssertContains(startupLog, badIndexFiles[0]);
        }
        finally
        {
            if (hadStartupLog)
            {
                File.WriteAllText(startupLogPath, originalStartupLog);
            }
            else if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }
        }
    }

    internal static void KeywordIndexLoaderSalvagesLegacyDisallowedUrls()
    {
        using var directory = TemporaryDirectory.Create();
        var indexPath = Path.Combine(directory.Path, "keyword-index.json");
        File.WriteAllText(
            indexPath,
            """
            {
              "index_metadata": {
                "generated_at": "2026-06-30T00:00:00.0000000+00:00",
                "total_words_indexed": 10
              },
              "urls": {
                "https://rpol.net/admin/gm.cgi?gi=80170": {
                  "source": "RPOL"
                },
                "https://publish.obsidian.md/scarlethorizons/Safe": {
                  "source": "Obsidian wiki"
                }
              },
              "words": {
                "Kelpie": {
                  "total_occurrences": 2,
                  "matches": [
                    {
                      "url": "https://rpol.net/admin/gm.cgi?gi=80170",
                      "count": 1,
                      "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                    },
                    {
                      "url": "https://publish.obsidian.md/scarlethorizons/Safe",
                      "count": 1,
                      "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                    }
                  ]
                }
              }
            }
            """);

        var loadTask = (Task)(InvokeStaticMethod(
            typeof(KeywordIndexCrawler),
            "LoadExistingDocumentAsync",
            indexPath,
            CancellationToken.None)
            ?? throw new InvalidOperationException("expected keyword index load task"));
        loadTask.GetAwaiter().GetResult();

        var result = loadTask
            .GetType()
            .GetProperty("Result")
            ?.GetValue(loadTask)
            ?? throw new InvalidOperationException("expected sanitized keyword index document");
        var words = result.GetType().GetProperty("Words")?.GetValue(result)
            ?? throw new InvalidOperationException("expected keyword index words");
        var kelpieEntry = ((System.Collections.IDictionary)words)["Kelpie"]
            ?? throw new InvalidOperationException("expected Kelpie entry to survive sanitization");
        var matches = kelpieEntry.GetType().GetProperty("Matches")?.GetValue(kelpieEntry)
            ?? throw new InvalidOperationException("expected Kelpie matches");

        AssertTrue(File.Exists(indexPath), "legacy keyword index should not be quarantined when useful entries can be salvaged");
        AssertEqual(0, Directory.GetFiles(directory.Path, "keyword-index.bad-*.json").Length, "legacy keyword index should not be moved aside");
        AssertEqual(1, ((System.Collections.ICollection)matches).Count, "expected only allowed matches to survive sanitization");
    }

    internal static void SitemapValidationRejectsPoisonedUrl()
    {
        var exception = AssertThrows<InvalidOperationException>(() =>
            SitemapUtility.ValidateSitemapXml(
                """
                <urlset>
                  <url>
                    <loc>https://user:password@publish.obsidian.md/scarlethorizons/Poison</loc>
                  </url>
                </urlset>
                """));

        AssertContains(exception.Message, "sitemap.xml contains a URL that is not allowed");
        AssertContains(exception.Message, "embedded credentials");
    }

    internal static void SitemapKeywordDictionaryPreservesExistingOutputOnRejectedUrl()
    {
        using var directory = TemporaryDirectory.Create();
        var sitemapPath = Path.Combine(directory.Path, "sitemap.xml");
        var dictionaryPath = Path.Combine(directory.Path, "sitemap-keyword-urls.json");
        File.WriteAllText(dictionaryPath, """{"safe":"https://publish.obsidian.md/scarlethorizons/Safe"}""");
        File.WriteAllText(
            sitemapPath,
            """
            <urlset>
              <url>
                <loc>https://evil.example.test/Poison</loc>
              </url>
            </urlset>
            """);

        var exception = AssertThrows<InvalidOperationException>(() =>
            SitemapUtility.WriteKeywordUrlDictionaryAsync(sitemapPath, dictionaryPath).GetAwaiter().GetResult());

        AssertContains(exception.Message, "sitemap.xml contains a URL that is not allowed");
        AssertContains(File.ReadAllText(dictionaryPath), "https://publish.obsidian.md/scarlethorizons/Safe");
    }

    internal static void SourceIntegrityRecordsFirstAcceptedSitemap()
    {
        using var directory = TemporaryDirectory.Create();
        var sitemapPath = Path.Combine(directory.Path, "sitemap.xml");
        var sitemapXml = CreateSitemapXml(
            "https://publish.obsidian.md/scarlethorizons/One",
            "https://publish.obsidian.md/scarlethorizons/Two");

        SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
            sitemapPath,
            "https://publish.obsidian.md/scarlethorizons/sitemap.xml",
            "obsidian-sitemap",
            sitemapXml,
            SourceIntegrityUtility.CreateSitemapShape(sitemapXml)).GetAwaiter().GetResult();

        var sidecarPath = SourceIntegrityUtility.GetSidecarPath(sitemapPath);
        AssertTrue(File.Exists(sidecarPath), "source integrity sidecar should be written for accepted sitemap");
        AssertContains(File.ReadAllText(sidecarPath), "\"artifact_kind\": \"obsidian-sitemap\"");
        AssertContains(File.ReadAllText(sidecarPath), "\"url_count\": 2");
    }

    internal static void SourceIntegrityRejectsCollapsedSitemapAndPreservesOutput()
    {
        using var directory = TemporaryDirectory.Create();
        var sitemapPath = Path.Combine(directory.Path, "sitemap.xml");
        var originalSitemap = CreateSitemapXml(
            "https://publish.obsidian.md/scarlethorizons/One",
            "https://publish.obsidian.md/scarlethorizons/Two",
            "https://publish.obsidian.md/scarlethorizons/Three",
            "https://publish.obsidian.md/scarlethorizons/Four");
        SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
            sitemapPath,
            "https://publish.obsidian.md/scarlethorizons/sitemap.xml",
            "obsidian-sitemap",
            originalSitemap,
            SourceIntegrityUtility.CreateSitemapShape(originalSitemap)).GetAwaiter().GetResult();

        var collapsedSitemap = CreateSitemapXml("https://publish.obsidian.md/scarlethorizons/One");
        var exception = AssertThrows<InvalidOperationException>(() =>
            SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
                sitemapPath,
                "https://publish.obsidian.md/scarlethorizons/sitemap.xml",
                "obsidian-sitemap",
                collapsedSitemap,
                SourceIntegrityUtility.CreateSitemapShape(collapsedSitemap)).GetAwaiter().GetResult());

        AssertContains(exception.Message, "Authenticated source tamper detection rejected fetched content");
        AssertContains(exception.Message, "last-known-good content was preserved");
        AssertEqual(originalSitemap, File.ReadAllText(sitemapPath), "collapsed sitemap should not replace last known good sitemap");
    }

    internal static void SourceIntegrityRejectsCollapsedMarkdownAndPreservesOutput()
    {
        using var directory = TemporaryDirectory.Create();
        var markdownPath = Path.Combine(directory.Path, "kelpie.md");
        var originalMarkdown = """
            # Kelpie Lawfuller
            ## Summary
            Useful notes.
            ## Links
            [[Allies]]
            [Map](https://publish.obsidian.md/scarlethorizons/Map)
            """;
        SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
            markdownPath,
            "https://publish.obsidian.md/scarlethorizons/PCs/Kelpie",
            "obsidian-markdown",
            originalMarkdown,
            SourceIntegrityUtility.CreateMarkdownShape(originalMarkdown)).GetAwaiter().GetResult();

        var collapsedMarkdown = "# Kelpie";
        var exception = AssertThrows<InvalidOperationException>(() =>
            SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
                markdownPath,
                "https://publish.obsidian.md/scarlethorizons/PCs/Kelpie",
                "obsidian-markdown",
                collapsedMarkdown,
                SourceIntegrityUtility.CreateMarkdownShape(collapsedMarkdown)).GetAwaiter().GetResult());

        AssertContains(exception.Message, "Authenticated source tamper detection rejected fetched content");
        AssertEqual(originalMarkdown, File.ReadAllText(markdownPath), "collapsed markdown should not replace last known good markdown");
    }

    internal static void SourceIntegrityRejectsCollapsedKeywordIndexAndPreservesOutput()
    {
        using var directory = TemporaryDirectory.Create();
        var indexPath = Path.Combine(directory.Path, "keyword-index.json");
        var originalIndex = """{"urls":{"u1":{},"u2":{},"u3":{},"u4":{}},"words":{"safe":{}}}""";
        SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
            indexPath,
            "keyword-index-crawl",
            "keyword-index",
            originalIndex,
            SourceIntegrityUtility.CreateKeywordIndexShape(4, 1, 4)).GetAwaiter().GetResult();

        var collapsedIndex = """{"urls":{"u1":{}},"words":{}}""";
        var exception = AssertThrows<InvalidOperationException>(() =>
            SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
                indexPath,
                "keyword-index-crawl",
                "keyword-index",
                collapsedIndex,
                SourceIntegrityUtility.CreateKeywordIndexShape(1, 0, 0)).GetAwaiter().GetResult());

        AssertContains(exception.Message, "Authenticated source tamper detection rejected fetched content");
        AssertEqual(originalIndex, File.ReadAllText(indexPath), "collapsed keyword index should not replace last known good index");
    }

    internal static void KeywordIndexValidationRejectsPoisonedUrlEntries()
    {
        var exception = AssertThrows<InvalidOperationException>(() =>
            KeywordIndexCrawler.ValidateKeywordIndexJson(
                """
                {
                  "index_metadata": {
                    "generated_at": "2026-07-04T00:00:00Z",
                    "total_words_indexed": 0
                  },
                  "urls": {
                    "https://evil.example.test/Poison": {
                      "source": "Obsidian wiki"
                    }
                  },
                  "words": {}
                }
                """));

        AssertContains(exception.Message, "keyword-index urls contains a URL that is not allowed");
        AssertContains(exception.Message, "Obsidian Publish page and note URLs");
    }

    internal static void KeywordIndexValidationRejectsPoisonedMatchUrls()
    {
        var exception = AssertThrows<InvalidOperationException>(() =>
            KeywordIndexCrawler.ValidateKeywordIndexJson(
                """
                {
                  "index_metadata": {
                    "generated_at": "2026-07-04T00:00:00Z",
                    "total_words_indexed": 1
                  },
                  "urls": {
                    "https://publish.obsidian.md/scarlethorizons/Safe": {
                      "source": "Obsidian wiki"
                    }
                  },
                  "words": {
                    "safe": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "file:///C:/secret.txt",
                          "count": 1,
                          "last_indexed": "2026-07-04T00:00:00Z"
                        }
                      ]
                    }
                  }
                }
                """));

        AssertContains(exception.Message, "keyword-index matches for 'safe' contains a URL that is not allowed");
        AssertContains(exception.Message, "Only HTTP and HTTPS");
    }

    internal static void KeywordTermsReleaseCopyGeneratesFromKeywordIndex()
    {
        using var directory = TemporaryDirectory.Create();
        var runtimeDirectory = Path.Combine(directory.Path, "runtime");
        Directory.CreateDirectory(runtimeDirectory);
        const string indexJson =
            """
            {
              "words": {
                "zeta": {},
                "Alpha": {},
                "beta": {}
              }
            }
            """;

        File.WriteAllText(Path.Combine(runtimeDirectory, "keyword-index.json"), indexJson);
        KeywordTermsFileUtility.EnsureReleaseCopy(runtimeDirectory);

        var termsPath = Path.Combine(runtimeDirectory, KeywordTermsFileUtility.FileName);
        AssertTrue(File.Exists(termsPath), "expected keyword terms file to be generated");
        AssertEqual(
            "Alpha|beta|zeta",
            string.Join("|", File.ReadAllLines(termsPath)),
            "generated keyword terms should be sorted from keyword index words");
    }

    internal static void KeywordTermsPublishCopyPreservesParentReleaseTerms()
    {
        using var directory = TemporaryDirectory.Create();
        var publishDirectory = Path.Combine(directory.Path, "publish");
        Directory.CreateDirectory(publishDirectory);

        var parentTermsPath = Path.Combine(directory.Path, KeywordTermsFileUtility.FileName);
        var publishTermsPath = Path.Combine(publishDirectory, KeywordTermsFileUtility.FileName);
        File.WriteAllText(parentTermsPath, "parent-term");
        File.WriteAllText(publishTermsPath, "publish-term");

        KeywordTermsFileUtility.EnsureReleaseCopy(publishDirectory);

        AssertTrue(File.Exists(parentTermsPath), "running from publish should not delete parent Release keyword terms");
        AssertTrue(File.Exists(publishTermsPath), "running from publish should keep its own keyword terms");
        AssertEqual("parent-term", File.ReadAllText(parentTermsPath), "parent Release keyword terms should be unchanged");
        AssertEqual("publish-term", File.ReadAllText(publishTermsPath), "publish keyword terms should be unchanged");
    }

    internal static void RpolAuthDetectsLoginPageFallback()
    {
        var loginHtml =
            """
            <html>
              <body>
                <form action='/login.cgi'>
                  <input name='username'>
                  <input name='password' type='password'>
                </form>
              </body>
            </html>
            """;

        AssertTrue(RpolAuthUtility.LooksLikeLoginPage(loginHtml), "expected RPOL login page markup to be detected");
        AssertTrue(
            RpolAuthUtility.LooksLikeLoginResponse("text/html; charset=utf-8", System.Text.Encoding.UTF8.GetBytes(loginHtml)),
            "expected HTML login response to be detected");
        AssertFalse(
            RpolAuthUtility.LooksLikeLoginResponse("image/png", System.Text.Encoding.UTF8.GetBytes(loginHtml)),
            "non-HTML responses should not be treated as expired auth pages");
        AssertFalse(
            RpolAuthUtility.LooksLikeLoginPage("<html><body>normal game page</body></html>"),
            "ordinary RPOL pages should not be treated as login pages");
    }

    internal static void RpolAuthDistinguishesBlockedAndRemoteFailures()
    {
        var uri = new Uri("https://rpol.net/display.cgi?gi=80170");

        var forbidden = RpolAuthUtility.CreateUnsuccessfulResponseException(uri, 403, "Forbidden");
        var cloudflareChallenge = RpolAuthUtility.CreateUnsuccessfulResponseException(
            uri,
            403,
            "Forbidden",
            "https://rpol.net/display.cgi?gi=80170&__cf_chl_rt_tk=challenge-token");
        var rateLimited = RpolAuthUtility.CreateUnsuccessfulResponseException(uri, 429, "Too Many Requests");
        var unavailable = RpolAuthUtility.CreateUnsuccessfulResponseException(uri, 503, "Service Unavailable");

        AssertEqual(RpolAuthFailureKind.RpolBlocked, forbidden.Kind, "403 should be classified as RPOL blocking");
        AssertContains(forbidden.Message, "blocked authenticated access");
        AssertEqual(RpolAuthFailureKind.CloudflareChallenge, cloudflareChallenge.Kind, "Cloudflare challenge 403 should trigger headed browser recovery");
        AssertFalse(RpolAuthUtility.IsFatalAuthFailure(cloudflareChallenge), "Cloudflare challenges should be retryable instead of cached as fatal");
        AssertEqual(RpolAuthFailureKind.RpolBlocked, rateLimited.Kind, "429 should be classified as RPOL blocking");
        AssertEqual(RpolAuthFailureKind.RemoteUnavailable, unavailable.Kind, "503 should remain a transient remote failure");
    }

    internal static void RpolAuthPrefersInstalledBrowsersBeforePlaywrightChromium()
    {
        var normalOptions = (BrowserTypeLaunchOptions[])(InvokeStaticMethod(
            typeof(RpolAuthUtility),
            "CreateRpolBrowserLaunchOptions",
            false) ?? throw new InvalidOperationException("CreateRpolBrowserLaunchOptions returned null."));
        var verificationOptions = (BrowserTypeLaunchOptions[])(InvokeStaticMethod(
            typeof(RpolAuthUtility),
            "CreateRpolBrowserLaunchOptions",
            true) ?? throw new InvalidOperationException("CreateRpolBrowserLaunchOptions returned null."));

        AssertEqual(3, normalOptions.Length, "normal RPOL auth should try Edge, Chrome, then Playwright Chromium");
        AssertEqual("msedge", normalOptions[0].Channel ?? string.Empty, "Edge should be tried before default Playwright Chromium");
        AssertEqual("chrome", normalOptions[1].Channel ?? string.Empty, "Chrome should be tried before default Playwright Chromium");
        AssertTrue(string.IsNullOrWhiteSpace(normalOptions[2].Channel), "default Playwright Chromium should remain the final fallback");
        AssertTrue(normalOptions.All(option => option.Headless == true), "normal RPOL auth should launch browsers headless");
        AssertTrue(verificationOptions.All(option => option.Headless == false), "manual RPOL browser verification should launch browsers headed");
    }

    internal static void RpolAuthEnforcesBrowserTlsValidation()
    {
        var contextOptions = (BrowserNewContextOptions)(InvokeStaticMethod(
            typeof(RpolAuthUtility),
            "CreateBrowserContextOptions",
            null!,
            true) ?? throw new InvalidOperationException("CreateBrowserContextOptions returned null."));

        AssertFalse(contextOptions.IgnoreHTTPSErrors == true, "RPOL browser contexts must reject HTTPS certificate errors");
    }

    internal static void RpolAuthClassifiesTransportSecurityFailures()
    {
        AssertTrue(
            RpolAuthUtility.IsTransportSecurityFailureMessage("net::ERR_CERT_AUTHORITY_INVALID at https://rpol.net/"),
            "invalid certificate authorities should be classified as transport-security failures");
        AssertTrue(
            RpolAuthUtility.IsTransportSecurityFailureMessage("net::ERR_CERT_COMMON_NAME_INVALID at https://rpol.net/"),
            "certificate hostname mismatches should be classified as transport-security failures");
        AssertTrue(
            RpolAuthUtility.IsTransportSecurityFailureMessage("net::ERR_SSL_VERSION_OR_CIPHER_MISMATCH"),
            "TLS protocol failures should be classified as transport-security failures");
        AssertFalse(
            RpolAuthUtility.IsTransportSecurityFailureMessage("net::ERR_CONNECTION_RESET at https://rpol.net/"),
            "ordinary network failures should not be classified as certificate failures");

        var transportException = (RpolAuthException)(InvokeStaticMethod(
            typeof(RpolAuthUtility),
            "CreateTransportSecurityException",
            new PlaywrightException("net::ERR_CERT_AUTHORITY_INVALID at https://rpol.net/game.php?gi=1"))
            ?? throw new InvalidOperationException("CreateTransportSecurityException returned null."));
        AssertEqual(
            RpolAuthFailureKind.TransportSecurityFailure,
            transportException.Kind,
            "certificate errors should become transport-security failures");
        AssertFalse(
            transportException.Message.Contains("https://", StringComparison.OrdinalIgnoreCase),
            "transport-security messages shown to users should not echo request URLs");
        AssertTrue(
            RpolAuthUtility.IsFatalAuthFailure(new RpolAuthException(
                RpolAuthFailureKind.TransportSecurityFailure,
                "TLS failure for test.")),
            "transport-security failures should stop authentication retries for the current process");
    }

    internal static void RpolAuthCachedFailureShortCircuitsHtmlFetch()
    {
        ResetRpolAuthFailureCache();
        var cachedFailure = new RpolAuthException(
            RpolAuthFailureKind.MissingCredentials,
            "Missing RPoL credentials for test.");

        try
        {
            InvokeStaticMethod(typeof(RpolAuthUtility), "CacheFatalAuthFailure", cachedFailure);

            var exception = AssertThrows<RpolAuthException>(() =>
                RpolAuthUtility.GetHtmlFromUrlAsync(new Uri("https://rpol.net/display.cgi?gi=1")).GetAwaiter().GetResult());
            AssertEqual(RpolAuthFailureKind.MissingCredentials, exception.Kind, "expected cached missing-credentials failure");
            AssertEqual(cachedFailure.Message, exception.Message, "expected cached failure message to be reused");

            exception = AssertThrows<RpolAuthException>(() =>
                RpolAuthUtility.GetResponseAsync(new Uri("https://rpol.net/c-webp/example.webp")).GetAwaiter().GetResult());
            AssertEqual(RpolAuthFailureKind.MissingCredentials, exception.Kind, "expected cached missing-credentials response failure");
        }
        finally
        {
            ResetRpolAuthFailureCache();
        }
    }

    internal static void RpolAuthCachedFailureLogsOnce()
    {
        ResetRpolAuthFailureCache();
        var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

        try
        {
            if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }

            var firstFailure = new RpolAuthException(
                RpolAuthFailureKind.MissingCredentials,
                "Missing RPoL credentials for test.");
            var secondFailure = new RpolAuthException(
                RpolAuthFailureKind.LoginRejected,
                "RPoL login was rejected for test.");

            InvokeStaticMethod(typeof(RpolAuthUtility), "CacheFatalAuthFailure", firstFailure);
            InvokeStaticMethod(typeof(RpolAuthUtility), "CacheFatalAuthFailure", secondFailure);

            var log = File.ReadAllText(startupLogPath);
            AssertEqual(1, CountOccurrences(log, "RPOL authentication"), "expected one RPOL auth log entry");
            AssertContains(log, "Missing RPoL credentials for test.");
            AssertFalse(log.Contains("RPoL login was rejected for test.", StringComparison.Ordinal), "second fatal auth failure should reuse first cached entry");
        }
        finally
        {
            ResetRpolAuthFailureCache();
            if (hadStartupLog)
            {
                File.WriteAllText(startupLogPath, originalStartupLog);
            }
            else if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }
        }
    }

    internal static void RpolAuthCachesBlockedAndExpiredSessionFailures()
    {
        ResetRpolAuthFailureCache();

        try
        {
            var blocked = new RpolAuthException(
                RpolAuthFailureKind.RpolBlocked,
                "RPoL blocked authenticated access for test.");
            var cached = (RpolAuthException?)InvokeStaticMethod(typeof(RpolAuthUtility), "CacheFatalAuthFailure", blocked)
                ?? throw new InvalidOperationException("CacheFatalAuthFailure returned null.");

            AssertEqual(RpolAuthFailureKind.RpolBlocked, cached.Kind, "blocked RPOL access should be cacheable as a fatal auth failure");
            AssertEqual(blocked.Message, cached.Message, "blocked RPOL failure should be cached as-is");
            AssertTrue(RpolAuthUtility.IsFatalAuthFailure(blocked), "blocked RPOL access should be treated as fatal until settings or site state changes");

            ResetRpolAuthFailureCache();

            var expiredSession = new RpolAuthException(
                RpolAuthFailureKind.AuthSessionExpired,
                "RPoL returned a login page after authenticated navigation for test.");
            cached = (RpolAuthException?)InvokeStaticMethod(typeof(RpolAuthUtility), "CacheFatalAuthFailure", expiredSession)
                ?? throw new InvalidOperationException("CacheFatalAuthFailure returned null.");

            AssertEqual(RpolAuthFailureKind.AuthSessionExpired, cached.Kind, "expired auth session should be cacheable as a fatal auth failure");
            AssertTrue(RpolAuthUtility.IsFatalAuthFailure(expiredSession), "expired authenticated sessions should be treated as fatal after retry");
            AssertFalse(
                RpolAuthUtility.IsFatalAuthFailure(new RpolAuthException(RpolAuthFailureKind.RemoteUnavailable, "remote outage")),
                "remote RPOL outages should remain transient and uncached");
        }
        finally
        {
            ResetRpolAuthFailureCache();
        }
    }

    internal static void RpolStorageStateValidationAcceptsCurrentRpolCookies()
    {
        using var directory = TemporaryDirectory.Create();
        var storageStatePath = Path.Combine(directory.Path, "rpol-storage-state.json");
        WriteRpolStorageState(
            storageStatePath,
            """
            {
              "cookies": [
                {
                  "name": "rpol_session",
                  "value": "cookie-value",
                  "domain": ".rpol.net",
                  "path": "/"
                }
              ],
              "origins": []
            }
            """,
            DateTimeOffset.UtcNow.AddDays(-1));

        var valid = RpolAuthUtility.TryPrepareStorageStateFile(
            storageStatePath,
            DateTimeOffset.UtcNow);

        AssertTrue(valid, "current RPOL storage state should be usable");
        AssertTrue(File.Exists(storageStatePath), "valid RPOL storage state should be preserved");
    }

    internal static void RpolStorageStateValidationDeletesMalformedState()
    {
        WithPreservedStartupLog(() =>
        {
            using var directory = TemporaryDirectory.Create();
            var storageStatePath = Path.Combine(directory.Path, "rpol-storage-state.json");
            WriteRpolStorageState(storageStatePath, "{ not valid json", DateTimeOffset.UtcNow);

            var valid = RpolAuthUtility.TryPrepareStorageStateFile(
                storageStatePath,
                DateTimeOffset.UtcNow);

            AssertFalse(valid, "malformed RPOL storage state should not be usable");
            AssertFalse(File.Exists(storageStatePath), "malformed RPOL storage state should be deleted");
        });
    }

    internal static void RpolStorageStateValidationDeletesStaleState()
    {
        WithPreservedStartupLog(() =>
        {
            using var directory = TemporaryDirectory.Create();
            var storageStatePath = Path.Combine(directory.Path, "rpol-storage-state.json");
            WriteRpolStorageState(
                storageStatePath,
                """
                {
                  "cookies": [
                    {
                      "name": "rpol_session",
                      "value": "cookie-value",
                      "domain": "rpol.net",
                      "path": "/"
                    }
                  ]
                }
                """,
                DateTimeOffset.UtcNow.AddDays(-45));

            var valid = RpolAuthUtility.TryPrepareStorageStateFile(
                storageStatePath,
                DateTimeOffset.UtcNow);

            AssertFalse(valid, "stale RPOL storage state should not be usable");
            AssertFalse(File.Exists(storageStatePath), "stale RPOL storage state should be deleted");
        });
    }

    internal static void RpolStorageStateValidationDeletesNonRpolState()
    {
        WithPreservedStartupLog(() =>
        {
            using var directory = TemporaryDirectory.Create();
            var storageStatePath = Path.Combine(directory.Path, "rpol-storage-state.json");
            WriteRpolStorageState(
                storageStatePath,
                """
                {
                  "cookies": [
                    {
                      "name": "session",
                      "value": "cookie-value",
                      "domain": "example.test",
                      "path": "/"
                    }
                  ]
                }
                """,
                DateTimeOffset.UtcNow);

            var valid = RpolAuthUtility.TryPrepareStorageStateFile(
                storageStatePath,
                DateTimeOffset.UtcNow);

            AssertFalse(valid, "non-RPOL storage state should not be usable");
            AssertFalse(File.Exists(storageStatePath), "non-RPOL storage state should be deleted");
        });
    }

    internal static void ShowAllThreadUrlPreservesBaseQueryAndAddsShowAll()
    {
        const string threadUrl = "https://rpol.net/display.cgi?gi=80170&ti=17&date=1779581880";

        var showAllUrl = RpolThreadPostUtility.GetShowAllThreadUrl(threadUrl);

        AssertEqual(
            "https://rpol.net/display.cgi?gi=80170&ti=17&msgpage=&show=all",
            showAllUrl,
            "unexpected show-all thread url");
    }

    internal static void ChapterDownloadsUseShowAllThreadUrls()
    {
        using var directory = TemporaryDirectory.Create();
        var requestedUrls = new List<string>();
        var downloads = GameForumUtility.DownloadChapterHtmlAsync(
            [new Hyperlink("https://rpol.net/display.cgi?gi=80170&ti=7&date=1779581880&msgpage=2", "Ch 1 - Kirkilston")],
            directory.Path,
            (url, _) =>
            {
                requestedUrls.Add(url);
                return Task.FromResult("<html><body>all chapter posts</body></html>");
            }).GetAwaiter().GetResult();

        AssertEqual(1, requestedUrls.Count, "chapter downloader should fetch one URL");
        AssertEqual(
            "https://rpol.net/display.cgi?gi=80170&ti=7&msgpage=&show=all",
            requestedUrls[0],
            "chapter downloader should request the complete thread");
        AssertTrue(downloads[0].Downloaded, "missing chapter file should be downloaded");
        AssertContains(File.ReadAllText(downloads[0].FilePath), "all chapter posts");
    }

    internal static void AsideDownloadsUseShowAllUrlsAndRefreshExistingFiles()
    {
        using var directory = TemporaryDirectory.Create();
        const string linkText = "Aside - Searching the woods";
        var existingPath = Path.Combine(directory.Path, $"{linkText}.html");
        File.WriteAllText(existingPath, "<html><body>stale aside page</body></html>");
        var requestedUrls = new List<string>();

        var downloads = GameForumUtility.DownloadAsideHtmlAsync(
            [new Hyperlink("https://rpol.net/display.cgi?gi=80170&ti=17&msgpage=1", linkText)],
            directory.Path,
            (url, _) =>
            {
                requestedUrls.Add(url);
                return Task.FromResult("<html><body><img src='secret.png'>all aside posts</body></html>");
            }).GetAwaiter().GetResult();

        AssertEqual(1, requestedUrls.Count, "aside downloader should refresh an existing file");
        AssertEqual(
            "https://rpol.net/display.cgi?gi=80170&ti=17&msgpage=&show=all",
            requestedUrls[0],
            "aside downloader should request the complete thread");
        AssertTrue(downloads[0].Downloaded, "changed existing aside should be refreshed");
        AssertContains(File.ReadAllText(existingPath), "all aside posts");
        AssertFalse(
            File.ReadAllText(existingPath).Contains("<img", StringComparison.OrdinalIgnoreCase),
            "aside refresh should continue removing images");
    }

    internal static void RpolThreadExportPreservesExistingOutputOnCancellation()
    {
        using var directory = TemporaryDirectory.Create();
        var outputDirectory = Path.Combine(directory.Path, "thread-export");
        Directory.CreateDirectory(outputDirectory);
        var markerPath = Path.Combine(outputDirectory, "last-good.txt");
        File.WriteAllText(markerPath, "keep me");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        AssertThrows<OperationCanceledException>(() =>
            RpolThreadPostUtility.WriteThreadPostsFromHtmlAsync(
                CreateSampleRpolThreadHtml(),
                "https://rpol.net/display.cgi?gi=80170&ti=17&show=all",
                outputDirectory,
                "Synthetic Thread",
                cancellation.Token).GetAwaiter().GetResult());

        AssertTrue(File.Exists(markerPath), "existing RPOL thread export should survive a cancelled replacement");
        AssertEqual("keep me", File.ReadAllText(markerPath), "existing RPOL thread export marker should remain unchanged");
        AssertEqual(0, Directory.GetDirectories(directory.Path, "thread-export.staging-*").Length, "cancelled export should clean staging directories");
        AssertEqual(0, Directory.GetDirectories(directory.Path, "thread-export.backup-*").Length, "cancelled export should not leave backup directories");
    }

    internal static void RpolThreadExportCommitsStagedOutput()
    {
        using var directory = TemporaryDirectory.Create();
        var outputDirectory = Path.Combine(directory.Path, "thread-export");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "stale.txt"), "old");

        var result = RpolThreadPostUtility.WriteThreadPostsFromHtmlAsync(
            CreateSampleRpolThreadHtml(),
            "https://rpol.net/display.cgi?gi=80170&ti=17&show=all",
            outputDirectory,
            "Synthetic Thread").GetAwaiter().GetResult();

        AssertEqual(2, result.PostCount, "expected staged RPOL export to include both posts");
        AssertFalse(File.Exists(Path.Combine(outputDirectory, "stale.txt")), "successful staged export should replace stale output");
        AssertTrue(File.Exists(Path.Combine(outputDirectory, "_source-show-all.html")), "successful staged export should include source HTML");
        AssertTrue(File.Exists(Path.Combine(outputDirectory, "index.html")), "successful staged export should include index.html");
        AssertTrue(File.Exists(Path.Combine(outputDirectory, "manifest.json")), "successful staged export should include manifest.json");
        AssertTrue(File.Exists(Path.Combine(outputDirectory, "001-alice.html")), "successful staged export should include the first post");
        AssertTrue(File.Exists(Path.Combine(outputDirectory, "002-bob.html")), "successful staged export should include the second post");
        AssertEqual(0, Directory.GetDirectories(directory.Path, "thread-export.staging-*").Length, "successful export should clean staging directories");
        AssertEqual(0, Directory.GetDirectories(directory.Path, "thread-export.backup-*").Length, "successful export should clean backup directories");
    }

    internal static void RpolThreadExportRejectsCollapsedSourceAndPreservesExistingOutput()
    {
        using var directory = TemporaryDirectory.Create();
        var outputDirectory = Path.Combine(directory.Path, "thread-export");
        var sourceUrl = "https://rpol.net/display.cgi?gi=80170&ti=17&show=all";
        var originalResult = RpolThreadPostUtility.WriteThreadPostsFromHtmlAsync(
            CreateSampleRpolThreadHtml(),
            sourceUrl,
            outputDirectory,
            "Synthetic Thread").GetAwaiter().GetResult();

        AssertEqual(2, originalResult.PostCount, "expected baseline RPOL export to include both posts");
        var originalSourceHtml = File.ReadAllText(Path.Combine(outputDirectory, "_source-show-all.html"));
        var collapsedHtml = """
            <html><body>
            <div class='message'>
                <ul><li>msg #1</li></ul>
                <span class='messageauthor'>Alice</span>
                <div class='messagebody' id='msg1'>Only one post survived.</div>
            </div><!-- 1 -->
            </div><!-- 2 -->
            </body></html>
            """;

        var exception = AssertThrows<InvalidOperationException>(() =>
            RpolThreadPostUtility.WriteThreadPostsFromHtmlAsync(
                collapsedHtml,
                sourceUrl,
                outputDirectory,
                "Synthetic Thread").GetAwaiter().GetResult());

        AssertContains(exception.Message, "Authenticated source tamper detection rejected fetched content");
        AssertEqual(originalSourceHtml, File.ReadAllText(Path.Combine(outputDirectory, "_source-show-all.html")), "collapsed RPOL source should not replace last known good export");
        AssertTrue(File.Exists(Path.Combine(outputDirectory, "002-bob.html")), "last known good RPOL post files should remain available");
        AssertEqual(0, Directory.GetDirectories(directory.Path, "thread-export.staging-*").Length, "rejected export should clean staging directories");
    }

    internal static void DieRollExtractionKeepsOnlySavedLogLines()
    {
        const string html = """
            <html><body>
            <div>18:37, Today: Dungeon Master rolled 4 using 1d6.  orcs' init for rnd 2 abandoned rock quarry. – [roll=1782257860.18744.396686]</div>
            <div>18:05, Today: Jelb Garrick rolled 6 using 2d4.  Fire Damage. – [roll=1782255916.99298.396653]</div>
            <div>15:41, Today: Kelpie Lawfuller rolled 17,8 using d20+2,d8+2.  Sword attack (held action). – [roll=1782247280.40694.396648]</div>
            <div>18:44, Today: Maximilian Yragerne rolled 16 using 1d20. dex.</div>
            <div>Dice are fun.</div>
            </body></html>
            """;

        var entries = GameForumUtility.ExtractDieRollEntries(html);

        AssertEqual(3, entries.Length, "unexpected die roll entry count");
        AssertEqual("1782257860.18744.396686", entries[0].RollId, "unexpected first roll id");
        AssertContains(entries[1].Line, "Jelb Garrick rolled 6 using 2d4.");
        AssertContains(entries[2].Line, "[roll=1782247280.40694.396648]");
    }

    internal static void DieRollExtractionHandlesLiveRpolParagraphMarkup()
    {
        const string html = """
            <div class="info_box">
            <p style="margin-left: 2em; text-indent: -2em;">18:37, Today: Dungeon Master rolled 4 using 1d6.&nbsp; orcs' init for rnd 2 abandoned rock quarry.
             –&nbsp;<span class="link-colour">[roll=1782257860.18744.396686]</span></p>
            <p style="margin-left: 2em; text-indent: -2em;">18:05, Today: Jelb Garrick rolled 6 using 2d4.&nbsp; Fire Damage.
             –&nbsp;<span class="link-colour">[roll=1782255916.99298.396653]</span></p>
            <p style="margin-left: 2em; text-indent: -2em;">15:41, Today: Kelpie Lawfuller rolled 17,8 using d20+2,d8+2.&nbsp; Sword attack (held action).
             –&nbsp;<span class="link-colour">[roll=1782247280.40694.396648]</span></p>
            </div>
            """;

        var entries = GameForumUtility.ExtractDieRollEntries(html);

        AssertEqual(3, entries.Length, "unexpected die roll entry count from paragraph markup");
        AssertEqual("1782257860.18744.396686", entries[0].RollId, "unexpected first roll id from paragraph markup");
        AssertContains(entries[0].Line, "abandoned rock quarry. – [roll=1782257860.18744.396686]");
        AssertContains(entries[2].Line, "Kelpie Lawfuller rolled 17,8 using d20+2,d8+2.");
    }

    internal static void DieRollExtractionDerivesStableIdsWhenRpolOmitsRollIds()
    {
        const string html = """
            <div class="info_box">
            <p style="margin-left: 2em; text-indent: -2em;">04:24, Tue 21 July: Shade rolled 1 using 1d20.&nbsp; Recall.</p>
            <p style="margin-left: 2em; text-indent: -2em;">05:33, Sun 19 July: Maximilian Yragerne rolled 13 using 1d20.&nbsp; charisma.</p>
            <p style="margin-left: 2em; text-indent: -2em;">05:25, Sun 19 July: Maximilian Yragerne rolled 15 using 1d20.</p>
            <p style="margin-left: 2em; text-indent: -2em;">04:24, Tue 21 July: Shade rolled 1 using 1d20.&nbsp; Recall.</p>
            </div>
            """;

        var first = GameForumUtility.ExtractDieRollEntries(html);
        var second = GameForumUtility.ExtractDieRollEntries(html);

        AssertEqual(3, first.Length, "identifier-free duplicate rolls should be collapsed");
        AssertEqual(first[0].RollId, second[0].RollId, "synthetic roll IDs should be stable");
        AssertTrue(
            System.Text.RegularExpressions.Regex.IsMatch(first[0].RollId, @"^\d+\.\d+\.\d+$"),
            "synthetic roll IDs should preserve the saved roll ID shape");
        AssertContains(first[0].Line, "Shade rolled 1 using 1d20.");

        using var directory = TemporaryDirectory.Create();
        var cachePath = Path.Combine(directory.Path, "dice-rolls.html");
        var appended = GameForumUtility.AppendNewDieRollEntriesAsync(html, cachePath).GetAwaiter().GetResult();
        var saved = GameForumUtility.ExtractDieRollEntries(File.ReadAllText(cachePath));
        var normalizedSnapshot = GameForumUtility.NormalizeDieRollSnapshotHtml(html);
        var normalizedSnapshotEntries = GameForumUtility.ExtractDieRollEntries(normalizedSnapshot);
        AssertEqual(3, appended, "identifier-free live rolls should be saved");
        AssertEqual(3, saved.Length, "saved synthetic roll IDs should remain parseable");
        AssertEqual(first[0].RollId, saved[0].RollId, "saved synthetic roll IDs should remain stable");
        AssertEqual(3, normalizedSnapshotEntries.Length, "normalized snapshots should retain every unique live roll");
        AssertContains(normalizedSnapshot, $"[roll={first[0].RollId}]");
    }

    internal static void DieRollSyncAppendsOnlyUnsavedRolls()
    {
        using var directory = TemporaryDirectory.Create();
        var filePath = Path.Combine(directory.Path, "Posts", "OOC", "dice-rolls.html");
        const string initialHtml = """
            <div>18:37, Today: Dungeon Master rolled 4 using 1d6.  orcs' init for rnd 2 abandoned rock quarry. – [roll=1782257860.18744.396686]</div>
            <div>18:05, Today: Jelb Garrick rolled 6 using 2d4.  Fire Damage. – [roll=1782255916.99298.396653]</div>
            """;
        const string nextHtml = """
            <div>18:05, Today: Jelb Garrick rolled 6 using 2d4.  Fire Damage. – [roll=1782255916.99298.396653]</div>
            <div>15:41, Today: Kelpie Lawfuller rolled 17,8 using d20+2,d8+2.  Sword attack (held action). – [roll=1782247280.40694.396648]</div>
            """;

        var firstAppendCount = GameForumUtility.AppendNewDieRollEntriesAsync(initialHtml, filePath).GetAwaiter().GetResult();
        var secondAppendCount = GameForumUtility.AppendNewDieRollEntriesAsync(nextHtml, filePath).GetAwaiter().GetResult();
        var savedHtml = File.ReadAllText(filePath);
        var savedEntries = GameForumUtility.ExtractDieRollEntries(savedHtml);

        AssertEqual(2, firstAppendCount, "unexpected initial append count");
        AssertEqual(1, secondAppendCount, "unexpected incremental append count");
        AssertEqual(3, savedEntries.Length, "unexpected saved die roll count");
        AssertEqual("1782257860.18744.396686", savedEntries[0].RollId, "unexpected first saved roll id");
        AssertEqual("1782247280.40694.396648", savedEntries[2].RollId, "unexpected final saved roll id");
    }
}
