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

namespace PlayerAssistant.Tests;

internal static partial class TestCases
{
    private static bool IsSingleWord(string value)
    {
        return !value.Any(char.IsWhiteSpace);
    }

    private static bool HasAnyTag(OrcishLexiconEntry entry, params string[] tags)
    {
        return tags.Any(tag =>
            (entry.Tags ?? Array.Empty<string>())
            .Any(entryTag => string.Equals(entryTag, tag, StringComparison.OrdinalIgnoreCase)));
    }

    private static string FormatLexiconEntry(OrcishLexiconEntry entry)
    {
        return $"{entry.English}->{entry.Orcish} [{entry.PartOfSpeech ?? "?"}]";
    }

    private static void AssertThirtyPageFamilyRoot(
        IReadOnlyList<OrcishLexiconEntry> entries,
        string sourceEnglish,
        params string[] nearKin)
    {
        var sourceRoot = entries.Single(entry =>
            string.Equals(entry.English, sourceEnglish, StringComparison.OrdinalIgnoreCase)
            && HasAnyTag(entry, "thirty-page-sample")).Orcish;

        foreach (var english in nearKin)
        {
            AssertTrue(
                entries.Single(entry => string.Equals(entry.English, english, StringComparison.OrdinalIgnoreCase))
                    .Orcish.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase),
                $"{english} should retain the {sourceEnglish} family root");
        }
    }

    internal static void SettingsJsonAcceptsCurrentSchemaVersion()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(
            Path.Combine(directory.Path, "settings.json"),
            """
            {
              "schema_version": 1,
              "RPOL Site": "https://rpol.net/game.php?gi=80170",
              "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
              "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
              "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons",
              "XP Tracking": "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
            }
            """);

        var settings = AppSettingsUtility.LoadSettings(directory.Path);

        AssertEqual("https://rpol.net/game.php?gi=80170", settings["RPOL Site"], "unexpected RPOL Site after schema-versioned load");
        AssertFalse(settings.ContainsKey("schema_version"), "schema_version should be treated as settings metadata");
    }

    internal static void SettingsJsonRejectsFutureSchemaVersion()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(
            Path.Combine(directory.Path, "settings.json"),
            """
            {
              "schema_version": 99,
              "RPOL Site": "https://rpol.net/game.php?gi=80170",
              "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
              "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
              "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons",
              "XP Tracking": "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
            }
            """);

        var exception = AssertThrows<InvalidOperationException>(() =>
            AppSettingsUtility.LoadSettings(directory.Path));
        AssertContains(exception.Message, "unsupported schema version 99");
    }

    internal static void HealthArgumentSurfacesReleaseManifestIssue()
    {
        var runtimeDirectory = AppContext.BaseDirectory;
        var manifestPath = Path.Combine(runtimeDirectory, ReleaseIntegrityManifestUtility.FileName);
        var backupPath = Path.Combine(runtimeDirectory, $"{ReleaseIntegrityManifestUtility.FileName}.test-backup-{Guid.NewGuid():N}");
        var hadManifest = File.Exists(manifestPath);

        try
        {
            if (hadManifest)
            {
                File.Move(manifestPath, backupPath);
            }

            File.WriteAllText(
                manifestPath,
                """
                {
                  "schema_version": 1,
                  "hash_algorithm": "SHA256",
                  "files": [
                    {
                      "relative_path": "missing-health-sidecar.txt",
                      "length": 1,
                      "sha256": "00"
                    }
                  ]
                }
                """);

            var programType = typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.Program")
                ?? throw new InvalidOperationException("Unable to find PlayerAssistant.Program type.");
            var health = (string)InvokeStaticMethod(programType, "GetHealthText")!;

            AssertContains(health, "status: error");
            AssertContains(health, "release-manifest.json missing manifested file");
            AssertContains(health, "missing-health-sidecar.txt");
        }
        finally
        {
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }

            if (hadManifest && File.Exists(backupPath))
            {
                File.Move(backupPath, manifestPath);
            }
        }
    }

    internal static void ApplicationVersionArgumentReturnsVersionText()
    {
        var programType = typeof(Form1).Assembly.GetType("PlayerAssistant.Program")
            ?? throw new InvalidOperationException("Unable to find PlayerAssistant.Program.");

        AssertTrue(
            (bool)(InvokeStaticMethod(programType, "IsVersionArgument", "--version") ?? false),
            "--version should be recognized as a version argument");
        AssertTrue(
            (bool)(InvokeStaticMethod(programType, "IsVersionArgument", "/version") ?? false),
            "/version should be recognized as a version argument");
        AssertFalse(
            (bool)(InvokeStaticMethod(programType, "IsVersionArgument", "--suppress-hero-images") ?? true),
            "non-version arguments should not be recognized as version arguments");

        var versionText = (string?)InvokeStaticMethod(programType, "GetVersionText")
            ?? throw new InvalidOperationException("GetVersionText returned null.");
        AssertContains(versionText, "player-assistant");
        AssertContains(versionText, GetCanonicalVersion());
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

    internal static void LoginInfoCacheLoadReturnsEmptyForMalformedJson()
    {
        using var directory = TemporaryDirectory.Create();
        var loginInfoPath = Path.Combine(directory.Path, "login-info.json");
        File.WriteAllText(loginInfoPath, "{ not valid json");
        var startupLogPath = RuntimePathUtility.GetWritableRuntimePath(StartupLoggingUtility.LogFileName);
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

    internal static void ShowAllThreadUrlPreservesBaseQueryAndAddsShowAll()
    {
        const string threadUrl = "https://rpol.net/display.cgi?gi=80170&ti=17&date=1779581880";

        var showAllUrl = RpolThreadPostUtility.GetShowAllThreadUrl(threadUrl);

        AssertEqual(
            "https://rpol.net/display.cgi?gi=80170&ti=17&msgpage=&show=all",
            showAllUrl,
            "unexpected show-all thread url");
    }

    internal static void AboutMenuContainsAuthorAndUpdateItems()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var menuStrip = (MenuStrip)(GetPrivateField(form, "menuStrip")
                ?? throw new InvalidOperationException("menuStrip was null."));
            var settingsMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "settingsToolStripMenuItem")
                ?? throw new InvalidOperationException("settingsToolStripMenuItem was null."));
            var aboutMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "aboutToolStripMenuItem")
                ?? throw new InvalidOperationException("aboutToolStripMenuItem was null."));
            var authorMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "authorToolStripMenuItem")
                ?? throw new InvalidOperationException("authorToolStripMenuItem was null."));
            var checkForUpdateMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "checkForUpdateToolStripMenuItem")
                ?? throw new InvalidOperationException("checkForUpdateToolStripMenuItem was null."));
            var versionMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "versionToolStripMenuItem")
                ?? throw new InvalidOperationException("versionToolStripMenuItem was null."));

            var topLevelItems = menuStrip.Items.Cast<ToolStripItem>().ToArray();
            AssertEqual("About", aboutMenuItem.Text ?? string.Empty, "unexpected About menu text");
            AssertEqual(
                Array.IndexOf(topLevelItems, settingsMenuItem) + 1,
                Array.IndexOf(topLevelItems, aboutMenuItem),
                "About menu should be immediately to the right of Settings");
            AssertEqual("Author", authorMenuItem.Text ?? string.Empty, "unexpected Author menu item text");
            AssertEqual("Check for Updates", checkForUpdateMenuItem.Text ?? string.Empty, "unexpected update menu item text");
            AssertEqual("Version", versionMenuItem.Text ?? string.Empty, "unexpected version menu item text");
            AssertTrue(
                aboutMenuItem.DropDownItems.Cast<ToolStripItem>().SequenceEqual([authorMenuItem, checkForUpdateMenuItem, versionMenuItem]),
                "About menu should contain Author, Check for Updates, then Version");
        });
    }

    internal static void FileMenuContainsCountWordsAndTakeSnapshotsItem()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var fileMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "fileToolStripMenuItem")
                ?? throw new InvalidOperationException("fileToolStripMenuItem was null."));
            var countWordsMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "countWordsAndTakeSnapshotsToolStripMenuItem")
                ?? throw new InvalidOperationException("countWordsAndTakeSnapshotsToolStripMenuItem was null."));
            var exitMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "exitToolStripMenuItem")
                ?? throw new InvalidOperationException("exitToolStripMenuItem was null."));

            AssertEqual("Count Words && Take Snapshots", countWordsMenuItem.Text ?? string.Empty, "unexpected publisher menu item text");
            AssertTrue(
                fileMenuItem.DropDownItems.Cast<ToolStripItem>().SequenceEqual([countWordsMenuItem, exitMenuItem]),
                "File menu should contain Count Words & Take Snapshots before Exit");
        });
    }

    internal static void CountWordsAndTakeSnapshotsRejectsInvalidDungeonMasterPassword()
    {
        var launchedTasks = new List<string>();

        var authorized = Form1.RunCountWordsAndTakeSnapshotsAsync(
            "wrong password",
            password => password == "correct password",
            (taskName, _) =>
            {
                launchedTasks.Add(taskName);
                return Task.CompletedTask;
            },
            CancellationToken.None).GetAwaiter().GetResult();

        AssertFalse(authorized, "an invalid Dungeon Master password must deny manual publication");
        AssertEqual(0, launchedTasks.Count, "no scheduled task should launch after failed authentication");
    }

    internal static void CountWordsAndTakeSnapshotsLaunchesBothPublisherTasks()
    {
        var launchedTasks = new List<string>();

        var authorized = Form1.RunCountWordsAndTakeSnapshotsAsync(
            "correct password",
            password => password == "correct password",
            (taskName, _) =>
            {
                launchedTasks.Add(taskName);
                return Task.CompletedTask;
            },
            CancellationToken.None).GetAwaiter().GetResult();

        AssertTrue(authorized, "the Dungeon Master password should authorize manual publication");
        AssertTrue(
            launchedTasks.SequenceEqual(
            [
                "Player Assistant Full Word Count Publisher",
                "Player Assistant RPOL Snapshot Publisher"
            ]),
            "manual publication should launch both installed publisher tasks exactly once");
    }

    internal static void ScheduledTaskLauncherUsesNativeSchtasksArguments()
    {
        var startInfo = ScheduledTaskLaunchUtility.CreateStartInfo(Form1.FullWordCountScheduledTaskName);

        AssertTrue(
            startInfo.FileName.EndsWith("schtasks.exe", StringComparison.OrdinalIgnoreCase),
            "scheduled tasks should launch through the native schtasks executable");
        AssertTrue(
            startInfo.ArgumentList.SequenceEqual(["/Run", "/TN", Form1.FullWordCountScheduledTaskName]),
            "scheduled task arguments should not be shell-concatenated");
        AssertFalse(startInfo.UseShellExecute, "scheduled task launch must not use shell execution");
        AssertTrue(startInfo.CreateNoWindow, "scheduled task launch should not open a console window");
    }

    internal static void ScheduledTaskLauncherRejectsPreCanceledRequestBeforeProcessCreation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var processCreationCount = 0;
        ScheduledTaskLaunchUtility.ProcessFactoryForTests = startInfo =>
        {
            processCreationCount++;
            return new Process { StartInfo = startInfo };
        };

        try
        {
            AssertThrows<OperationCanceledException>(() =>
                ScheduledTaskLaunchUtility.StartAsync(
                    Form1.FullWordCountScheduledTaskName,
                    cancellation.Token).GetAwaiter().GetResult());
            AssertEqual(0, processCreationCount, "a pre-canceled launch must not create or start schtasks.exe");
        }
        finally
        {
            ScheduledTaskLaunchUtility.ProcessFactoryForTests = null;
        }
    }

    internal static void ScheduledTaskLauncherCancellationTerminatesProcess()
    {
        var pidPath = Path.Combine(Path.GetTempPath(), $"player-assistant-schtasks-test-{Guid.NewGuid():N}.pid");
        using var cancellation = new CancellationTokenSource();
        ScheduledTaskLaunchUtility.ProcessFactoryForTests = _ =>
        {
            var startInfo = CreateTestChildProcessStartInfo(["--cancellation-child", pidPath]);
            return new Process { StartInfo = startInfo };
        };

        int? processId = null;
        try
        {
            var launchTask = ScheduledTaskLaunchUtility.StartAsync(
                Form1.FullWordCountScheduledTaskName,
                cancellation.Token);
            AssertTrue(
                SpinWait.SpinUntil(() => File.Exists(pidPath), TimeSpan.FromSeconds(5)),
                "the cancellation test process should start");
            processId = int.Parse(File.ReadAllText(pidPath).Trim(), System.Globalization.CultureInfo.InvariantCulture);

            cancellation.Cancel();
            AssertThrows<OperationCanceledException>(() => launchTask.GetAwaiter().GetResult());
            AssertTrue(
                SpinWait.SpinUntil(() => !IsProcessRunning(processId.Value), TimeSpan.FromSeconds(5)),
                "cancellation should terminate the launched process");
        }
        finally
        {
            cancellation.Cancel();
            ScheduledTaskLaunchUtility.ProcessFactoryForTests = null;
            if (processId.HasValue && IsProcessRunning(processId.Value))
            {
                Process.GetProcessById(processId.Value).Kill(entireProcessTree: true);
            }

            File.Delete(pidPath);
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ProcessStartInfo CreateTestChildProcessStartInfo(IEnumerable<string> arguments)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The test process path is unavailable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(typeof(TestCases).Assembly.Location);
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    internal static void CountWordsAndTakeSnapshotsAttemptsBothTasksAfterOneFailure()
    {
        var launchedTasks = new List<string>();

        var exception = AssertThrows<AggregateException>(() =>
            Form1.RunCountWordsAndTakeSnapshotsAsync(
                "correct password",
                _ => true,
                (taskName, _) =>
                {
                    launchedTasks.Add(taskName);
                    return taskName == Form1.FullWordCountScheduledTaskName
                        ? Task.FromException(new InvalidOperationException("word-count launch failed"))
                        : Task.CompletedTask;
                },
                CancellationToken.None).GetAwaiter().GetResult());

        AssertEqual(2, launchedTasks.Count, "one failed task request must not suppress the other task request");
        AssertEqual(1, exception.InnerExceptions.Count, "only the failed task request should be reported");
    }

    internal static void FormClosureCancelsManualPublisherLaunch()
    {
        RunOnStaThread(() =>
        {
            var form = new Form1(suppressHeroImagesForThisRun: true);
            var lifetimeCancellation = (CancellationTokenSource)(GetPrivateField(form, "_formLifetimeCancellation")
                ?? throw new InvalidOperationException("_formLifetimeCancellation was null."));

            try
            {
                InvokePrivateMethod(
                    form,
                    "OnFormClosed",
                    new FormClosedEventArgs(CloseReason.UserClosing));

                AssertTrue(
                    lifetimeCancellation.IsCancellationRequested,
                    "closing the form should cancel an in-flight manual publisher launch");
                AssertFalse(
                    (bool)(InvokePrivateMethod(form, "CanContinueManualPublisherUi")
                        ?? throw new InvalidOperationException("CanContinueManualPublisherUi returned null.")),
                    "a closed form must not update manual publisher UI after an awaited launch");
            }
            finally
            {
                form.Dispose();
            }
        });
    }

    internal static void CountWordsAndTakeSnapshotsPreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var launchedTasks = new List<string>();

        AssertThrows<OperationCanceledException>(() =>
            Form1.RunCountWordsAndTakeSnapshotsAsync(
                "correct password",
                _ => true,
                (taskName, cancellationToken) =>
                {
                    launchedTasks.Add(taskName);
                    cancellation.Cancel();
                    return Task.CompletedTask;
                },
                cancellation.Token).GetAwaiter().GetResult());

        AssertEqual(1, launchedTasks.Count, "caller cancellation should stop the second task launch request");
    }

    internal static void ManualPublisherFailureReportingSkipsUiAfterClosure()
    {
        var loggingCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canUpdateUi = true;
        var uiReportCount = 0;

        var reportTask = Form1.ReportManualPublisherFailureAsync(
            () => loggingCompletion.Task,
            () => canUpdateUi,
            () => uiReportCount++);
        canUpdateUi = false;
        loggingCompletion.SetResult();
        reportTask.GetAwaiter().GetResult();

        AssertEqual(0, uiReportCount, "form closure during failure logging must suppress later UI access");
    }

    internal static void AboutAuthorTextListsDeveloperInfo()
    {
        var authorText = (string)(InvokeStaticMethod(typeof(Form1), "GetAuthorInfoText")
            ?? throw new InvalidOperationException("GetAuthorInfoText returned null."));
        AssertEqual(
            string.Join(Environment.NewLine, "Bryan Miller", "kyrathasoft@gmail.com", "bryanmiller.us"),
            authorText,
            "author info text should list developer details on separate lines");
    }

    internal static void AboutVersionTextShowsAppVersion()
    {
        var versionText = (string)(InvokeStaticMethod(typeof(Form1), "GetAppVersionText")
            ?? throw new InvalidOperationException("GetAppVersionText returned null."));
        AssertEqual($"RPOL Scarlet Horizon Campaign Assistant {GetCanonicalVersion()}", versionText, "unexpected About Version text");
    }

    internal static void LegacyTrustedUpdateStateMigratesToProtectedFormat()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "trusted-update-state.json");
        File.WriteAllText(
            statePath,
            """
            {
              "schema_version": 1,
              "highest_trusted_version": "0.9.2",
              "recorded_at": "2026-07-04T00:00:00.0000000+00:00"
            }
            """);

        var version = PlayerAssistantUpdateUtility.TryReadTrustedUpdateVersion(statePath);
        var protectedJson = File.ReadAllText(statePath);

        AssertEqual(new Version(0, 9, 2), version!, "unexpected migrated trusted update version");
        AssertContains(protectedJson, "\"format\": \"dpapi-current-user-v2\"");
        AssertContains(protectedJson, "\"key_scope\":");
    }

    internal static void TrustedUpdateStateIsEncryptedAtRest()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "trusted-update-state.json");

        PlayerAssistantUpdateUtility.ApplyTrustedUpdateVersionPolicy(
            new PlayerAssistantUpdateInfo(
                new Version(0, 9, 2),
                "0.9.2",
                new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.zip"),
                new string('A', 64),
                new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.exe"),
                new string('B', 64)),
            new Version(0, 9, 1),
            statePath);

        var encryptedJson = File.ReadAllText(statePath);
        AssertContains(encryptedJson, "\"format\": \"dpapi-current-user-v2\"");
        AssertContains(encryptedJson, "\"key_scope\":");
        AssertFalse(encryptedJson.Contains("0.9.2", StringComparison.Ordinal), "trusted version should not be stored in plaintext");
    }

    internal static void TrustedUpdateStateRejectsTamperedPayload()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "trusted-update-state.json");

        PlayerAssistantUpdateUtility.ApplyTrustedUpdateVersionPolicy(
            new PlayerAssistantUpdateInfo(
                new Version(0, 9, 2),
                "0.9.2",
                new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.zip"),
                new string('A', 64),
                new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.exe"),
                new string('B', 64)),
            new Version(0, 9, 1),
            statePath);

        using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(statePath)))
        {
            var payload = document.RootElement.GetProperty("payload").GetString() ?? string.Empty;
            var payloadBytes = Convert.FromBase64String(payload);
            payloadBytes[^1] ^= 0x7F;
            File.WriteAllText(
                statePath,
                $$"""
                {
                  "schema_version": 1,
                  "format": "dpapi-current-user-v2",
                  "payload": "{{Convert.ToBase64String(payloadBytes)}}"
                }
                """);
        }

        var exception = AssertThrows<InvalidOperationException>(() =>
            PlayerAssistantUpdateUtility.TryReadTrustedUpdateVersion(statePath));
        AssertContains(exception.Message, "Unable to decrypt");
    }

    internal static (string ManifestJson, string SignatureText, string PublicKeyPem) CreateSignedUpdateManifest(string manifestJson)
    {
        using var rsa = RSA.Create(2048);
        var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);
        var signatureBytes = rsa.SignData(
            manifestBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return (manifestJson, Convert.ToBase64String(signatureBytes), rsa.ExportSubjectPublicKeyInfoPem());
    }

    private static UpdateManifestSigningKeyTrustEntry CreateActiveSigningKey(string publicKeyPem)
    {
        return new UpdateManifestSigningKeyTrustEntry("test-key", publicKeyPem);
    }

    internal static (string HostedSettingsJson, string PublicKeyPem) CreateSignedHostedSettingsArtifact(
    IReadOnlyDictionary<string, string> settings,
    string version = "1.0.0",
    string contentId = HostedSettingsTrustUtility.HostedSettingsContentId)
    {
        using var rsa = RSA.Create(2048);
        return (
            HostedSettingsTrustUtility.CreateSignedHostedSettingsJson(settings, version, rsa, contentId),
            rsa.ExportSubjectPublicKeyInfoPem());
    }

    internal static void PortableEncryptedSettingsByteLoaderClearsSourceBuffer()
    {
        var settings = new Dictionary<string, string>
        {
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/XP"
        };
        var encryptedJson = LocalSettingsUtility.CreatePortableEncryptedSettingsJson(settings);
        var encryptedUtf8 = System.Text.Encoding.UTF8.GetBytes(encryptedJson);

        var loadedSettings = LocalSettingsUtility.LoadPortableEncryptedSettingsFromUtf8Bytes(
            encryptedUtf8,
            "test settings");

        AssertEqual("https://publish.obsidian.md/scarlethorizons/XP", loadedSettings["XP Tracking"], "unexpected setting after byte-buffer load");
        AssertTrue(encryptedUtf8.All(static value => value == 0), "portable encrypted settings buffer should be cleared after load");
    }

    internal static (int ExitCode, string Output) RunDiagnosticsCollection(
    string releaseDirectory,
    string publishDirectory,
    string outputDirectory,
    params string[] extraArguments)
    {
        var arguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(GetRepositoryRoot(), "collect-diagnostics.ps1"),
            "-ReleaseDir",
            releaseDirectory,
            "-PublishDir",
            publishDirectory,
            "-OutputDir",
            outputDirectory,
            "-ConfirmExport"
        };
        arguments.AddRange(extraArguments);
        return RunPowerShell(arguments, TimeSpan.FromSeconds(45));
    }

    internal static (int ExitCode, string Output) RunDiagnosticsVerification(string outputDirectory, string zipPath)
    {
        return RunPowerShell(
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Path.Combine(GetRepositoryRoot(), "collect-diagnostics.ps1"),
                "-OutputDir",
                outputDirectory,
                "-VerifyOnly",
                zipPath
            ],
            TimeSpan.FromSeconds(30));
    }

    private static (int ExitCode, string Output) RunDiagnosticsCollectionWithoutConfirmation(
        string releaseDirectory,
        string publishDirectory,
        string outputDirectory,
        params string[] extraArguments)
    {
        var arguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(GetRepositoryRoot(), "collect-diagnostics.ps1"),
            "-ReleaseDir",
            releaseDirectory,
            "-PublishDir",
            publishDirectory,
            "-OutputDir",
            outputDirectory
        };
        arguments.AddRange(extraArguments);
        return RunPowerShell(arguments, TimeSpan.FromSeconds(30));
    }

    internal static (int ExitCode, string Output) RunDiagnosticsRetentionCleanup(string scratchDirectory, params string[] extraArguments)
    {
        var arguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(GetRepositoryRoot(), "clean-diagnostics-retention.ps1"),
            "-ScratchDir",
            scratchDirectory
        };
        arguments.AddRange(extraArguments);
        return RunPowerShell(arguments, TimeSpan.FromSeconds(30));
    }

    private static string[] GetZipEntryNames(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return archive.Entries
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();
    }

    internal static (int ExitCode, string Output) RunReleasePublishParity(string releaseDirectory, string publishDirectory)
    {
        return RunPowerShell(
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Path.Combine(GetRepositoryRoot(), "verify-release-publish-parity.ps1"),
                "-ReleaseDir",
                releaseDirectory,
                "-PublishDir",
                publishDirectory
            ],
            TimeSpan.FromSeconds(30));
    }

    internal static (int ExitCode, string Output) RunPublishedHealthVerification(string publishDirectory)
    {
        return RunPowerShell(
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Path.Combine(GetRepositoryRoot(), "verify-published-health.ps1"),
                "-PublishDir",
                publishDirectory
            ],
            TimeSpan.FromSeconds(30));
    }

    internal static (int ExitCode, string Output) RunSecretScan(string repoRoot, bool includeHistory)
    {
        var arguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(GetRepositoryRoot(), "verify-secret-scan.ps1"),
            "-RepoRoot",
            repoRoot
        };

        if (includeHistory)
        {
            arguments.Add("-IncludeHistory");
        }

        return RunPowerShell(arguments, TimeSpan.FromSeconds(60));
    }

    internal static (int ExitCode, string Output) RunPublishVerification(string outputDirectory, params string[] extraArguments)
    {
        var repoRoot = GetRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "publish-player-assistant.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new InvalidOperationException($"Publish script is missing: {scriptPath}");
        }

        SetRuntimeSidecarsReadOnly(outputDirectory);

        var arguments = new List<string>
        {
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                scriptPath,
                "-VerifyOnly",
                "-OutputDir",
                outputDirectory
        };
        arguments.AddRange(extraArguments);

        return RunPowerShell(arguments, TimeSpan.FromSeconds(30));
    }

    internal static (int ExitCode, string Output) RunToOrcish(params string[] arguments)
    {
        var repoRoot = GetRepositoryRoot();
        var executablePath = Path.Combine(repoRoot, "Release", "to-orcish.exe");
        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException($"to-orcish executable is missing: {executablePath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start to-orcish process.");

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("to-orcish process did not exit within 30 seconds.");
        }

        return (process.ExitCode, output);
    }

    internal static (int ExitCode, string Output) RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start git process.");

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Git process did not exit within 30 seconds.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed. Output: {output}");
        }

        return (process.ExitCode, output);
    }

    internal static (int ExitCode, string Output) RunPowerShell(IEnumerable<string> arguments, TimeSpan timeout)
    {
        var powerShellExecutable = ResolvePowerShellExecutable();
        var startInfo = new ProcessStartInfo
        {
            FileName = powerShellExecutable,
            WorkingDirectory = GetRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start PowerShell process.");

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"PowerShell process did not exit within {timeout.TotalSeconds:0.#} seconds.");
        }

        return (process.ExitCode, output);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationDirectory);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static void DeleteDirectoryTree(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }

        foreach (var childDirectoryPath in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(childDirectoryPath, FileAttributes.Directory);
        }

        Directory.Delete(directoryPath, recursive: true);
    }

    private static void AssertTrue(bool actual, string message)
    {
        if (!actual)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertFalse(bool actual, string message)
    {
        if (actual)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertContains(string value, string expected)
    {
        if (!value.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected '{value}' to contain '{expected}'.");
        }
    }

    internal static void AssertEqual<T>(T expected, T actual, string message) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}. Expected '{expected}' but was '{actual}'.");
        }
    }

    internal static TException AssertThrows<TException>(Action action)
    where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is TException innerException)
        {
            return innerException;
        }

        throw new InvalidOperationException($"Expected exception of type {typeof(TException).Name}.");
    }

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while (true)
        {
            index = value.IndexOf(pattern, index, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            index += pattern.Length;
        }
    }

    private static void WaitForCondition(Func<bool> condition, string message)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(10);
        }

        throw new InvalidOperationException(message);
    }

    private static void WaitForWindowsFormsCondition(Func<bool> condition, string message)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            Application.DoEvents();
            if (condition())
            {
                return;
            }

            Thread.Sleep(10);
        }

        throw new InvalidOperationException(message);
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? capturedException = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException is not null)
        {
            throw capturedException;
        }
    }

    internal static T GetControl<T>(Form form, string fieldName) where T : Control
    {
        var field = typeof(Form1).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(form) is T control)
        {
            return control;
        }

        throw new InvalidOperationException($"Unable to find control field '{fieldName}'.");
    }

    private static Task InvokePrivateAsync(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(method =>
                string.Equals(method.Name, methodName, StringComparison.Ordinal)
                && method.GetParameters().Length == args.Length);
        if (method is null)
        {
            throw new InvalidOperationException($"Unable to find method '{methodName}'.");
        }

        if (method.Invoke(instance, args) is Task task)
        {
            return task;
        }

        throw new InvalidOperationException($"Method '{methodName}' did not return a Task.");
    }

    private static object? InvokeStaticMethod(Type type, string methodName, params object[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (method is null)
        {
            throw new InvalidOperationException($"Unable to find static method '{methodName}'.");
        }

        return method.Invoke(null, args);
    }

    private static object? InvokePrivateMethod(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(method =>
                string.Equals(method.Name, methodName, StringComparison.Ordinal)
                && method.GetParameters().Length == args.Length);
        if (method is null)
        {
            throw new InvalidOperationException($"Unable to find method '{methodName}'.");
        }

        return method.Invoke(instance, args);
    }

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is null)
        {
            throw new InvalidOperationException($"Unable to find field '{fieldName}'.");
        }

        field.SetValue(instance, value);
    }

    private static object? GetPrivateField(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is null)
        {
            throw new InvalidOperationException($"Unable to find field '{fieldName}'.");
        }

        return field.GetValue(instance);
    }

    private static void SetStaticField(Type type, string fieldName, object? value)
    {
        var field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        if (field is null)
        {
            throw new InvalidOperationException($"Unable to find static field '{fieldName}'.");
        }

        field.SetValue(null, value);
    }

    private static void WithTemporaryEncryptedTextIndex(string json, Action action)
    {
        var indexPath = GetPlayerAssistantEncryptedTextIndexPath();
        var backupPath = indexPath + ".test-backup";
        var hadOriginalIndex = File.Exists(indexPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            if (hadOriginalIndex)
            {
                File.Copy(indexPath, backupPath, overwrite: true);
            }

            File.WriteAllText(indexPath, json);
            action();
        }
        finally
        {
            if (File.Exists(indexPath))
            {
                File.Delete(indexPath);
            }

            if (hadOriginalIndex)
            {
                if (!File.Exists(backupPath))
                {
                    throw new FileNotFoundException($"Expected backup file '{backupPath}' to exist for restore.", backupPath);
                }

                File.Move(backupPath, indexPath, overwrite: true);
            }
        }
    }

    private static string GetPlayerAssistantIndexPath()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(Form1).Assembly.Location);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            throw new InvalidOperationException("Unable to resolve the player-assistant assembly directory.");
        }

        return Path.Combine(assemblyDirectory, "keyword-index.json");
    }

    private static string GetPlayerAssistantEncryptedTextIndexPath()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(Form1).Assembly.Location);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            throw new InvalidOperationException("Unable to resolve the player-assistant assembly directory.");
        }

        return Path.Combine(assemblyDirectory, TaggedNoteCipherUtility.EncryptedTextIndexFileName);
    }

    private static string GetLastCrashPath()
    {
        return RuntimePathUtility.GetWritableRuntimePath(LastCrashDiagnosticUtility.FileName);
    }

    private static void WithPreservedFileAbsent(string filePath, Action action)
    {
        var hadFile = File.Exists(filePath);
        var originalContents = hadFile ? File.ReadAllBytes(filePath) : null;

        try
        {
            if (hadFile)
            {
                File.Delete(filePath);
            }

            action();
        }
        finally
        {
            if (hadFile)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllBytes(filePath, originalContents!);
            }
        }
    }

    private static void WithPreservedLastCrash(Action action)
    {
        var lastCrashPath = GetLastCrashPath();
        var hadLastCrash = File.Exists(lastCrashPath);
        var originalLastCrash = hadLastCrash ? File.ReadAllText(lastCrashPath) : null;

        try
        {
            if (File.Exists(lastCrashPath))
            {
                File.Delete(lastCrashPath);
            }

            action();
        }
        finally
        {
            if (hadLastCrash)
            {
                File.WriteAllText(lastCrashPath, originalLastCrash);
            }
            else if (File.Exists(lastCrashPath))
            {
                File.Delete(lastCrashPath);
            }
        }
    }

    private static void AssertJsonString(
        System.Text.Json.JsonElement element,
        string propertyName,
        string expected,
        string message)
    {
        AssertEqual(expected, element.GetProperty(propertyName).GetString() ?? string.Empty, message);
    }

    private static void AssertJsonNumber(
        System.Text.Json.JsonElement element,
        string propertyName,
        long expected,
        string message)
    {
        AssertEqual(expected, element.GetProperty(propertyName).GetInt64(), message);
    }

    private static void AssertJsonNumberAtLeast(
        System.Text.Json.JsonElement element,
        string propertyName,
        long minimum,
        string message)
    {
        var actual = element.GetProperty(propertyName).GetInt64();
        if (actual < minimum)
        {
            throw new InvalidOperationException($"{message}. Expected at least '{minimum}' but was '{actual}'.");
        }
    }

    private static void WriteSettingsJson(string directoryPath, IReadOnlyDictionary<string, string> settings)
    {
        Directory.CreateDirectory(directoryPath);
        var schemaVersionedSettings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schema_version"] = 1
        };
        foreach (var setting in settings)
        {
            if (string.Equals(setting.Key, "schema_version", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            schemaVersionedSettings[setting.Key] = setting.Value;
        }

        File.WriteAllText(
            Path.Combine(directoryPath, "settings.json"),
            System.Text.Json.JsonSerializer.Serialize(
                schemaVersionedSettings,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static string GetCanonicalVersion(string propertyName = "PlayerAssistantVersion")
    {
        var metadataPath = Path.Combine(GetRepositoryRoot(), "version.props");
        var document = XDocument.Load(metadataPath);
        return document.Descendants(propertyName).Single().Value;
    }

    private static void WriteReleaseManifest(string directoryPath)
    {
        var assembly = typeof(Program).Assembly;
        var fileVersion = assembly
            .GetCustomAttributes(typeof(AssemblyFileVersionAttribute), inherit: false)
            .OfType<AssemblyFileVersionAttribute>()
            .FirstOrDefault()
            ?.Version
            ?? string.Empty;
        var informationalVersion = assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion
            ?? string.Empty;

        var files = GetReleaseManifestRelativePaths()
            .Select(relativePath => GetReleaseManifestEntry(directoryPath, relativePath))
            .ToArray();
        var manifest = new
        {
            schema_version = 1,
            generated_at = DateTimeOffset.UtcNow.ToString("O"),
            app_version = GetCanonicalVersion(),
            file_version = fileVersion,
            product_version = informationalVersion,
            hash_algorithm = "SHA256",
            files
        };

        File.WriteAllText(
            Path.Combine(directoryPath, "release-manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(
                manifest,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteReleaseProvenance(string directoryPath)
    {
        var manifestEntry = GetReleaseManifestEntry(directoryPath, "release-manifest.json");
        var inventoryEntry = GetReleaseManifestEntry(directoryPath, "release-runtime-inventory.json");
        var provenance = new
        {
            schema_version = 1,
            generated_at = DateTimeOffset.UtcNow.ToString("O"),
            app = new
            {
                version = GetCanonicalVersion(),
                file_version = GetCanonicalVersion("PlayerAssistantAssemblyVersion"),
                product_version = GetCanonicalVersion()
            },
            git = new
            {
                commit = new string('a', 40),
                commit_short = new string('a', 12),
                branch = "test",
                tags_at_commit = Array.Empty<string>(),
                dirty = true,
                status_count = 1,
                status_sha256 = new string('B', 64)
            },
            release_manifest = manifestEntry,
            runtime_inventory = inventoryEntry,
            executable_signature = new
            {
                status = "NotSigned",
                signer_subject = (string?)null,
                thumbprint = (string?)null
            },
            hash_algorithm = "SHA256"
        };

        File.WriteAllText(
            Path.Combine(directoryPath, "release-provenance.json"),
            System.Text.Json.JsonSerializer.Serialize(
                provenance,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static string[] GetReleaseManifestRelativePaths()
    {
        return
        [
            "player-assistant.exe",
            "settings.json",
            "magic-items.json",
            XpPasswordStoreUtility.FileName,
            "release-runtime-inventory.json",
            "keyword-index.json",
            KeywordTermsFileUtility.FileName,
            "sitemap.xml",
            "sitemap-keyword-urls.json",
            Path.Combine(".playwright", "node", "win32_x64", "node.exe"),
            Path.Combine(".playwright", "package", "package.json"),
            Path.Combine(".playwright", "package", "browsers.json")
        ];
    }

    private static object GetReleaseManifestEntry(string directoryPath, string relativePath)
    {
        var path = Path.Combine(directoryPath, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Release manifest fixture file '{relativePath}' was missing.", path);
        }

        return new
        {
            relative_path = relativePath.Replace(Path.DirectorySeparatorChar, '\\'),
            length = new FileInfo(path).Length,
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
        };
    }

    private static void SetLastWriteTimeUtc(string filePath, DateTimeOffset value)
    {
        File.SetLastWriteTimeUtc(filePath, value.UtcDateTime);
    }

    private static void SetDirectoryLastWriteTimeUtc(string directoryPath, DateTimeOffset value)
    {
        Directory.SetLastWriteTimeUtc(directoryPath, value.UtcDateTime);
    }

    private static void WriteVisiblePng(string filePath)
    {
        using var bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        bitmap.SetPixel(0, 0, Color.Black);
        bitmap.Save(filePath, ImageFormat.Png);

        using var padding = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None);
        padding.SetLength(600_000);
    }

    private static void WriteTransparentPng(string filePath)
    {
        using var bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        bitmap.Save(filePath, ImageFormat.Png);
    }

}
