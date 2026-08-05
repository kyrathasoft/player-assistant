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


internal static class TestSupport
{
    internal static bool IsSingleWord(string value)
    {
        return !value.Any(char.IsWhiteSpace);
    }

    internal static bool HasAnyTag(OrcishLexiconEntry entry, params string[] tags)
    {
        return tags.Any(tag =>
            (entry.Tags ?? Array.Empty<string>())
            .Any(entryTag => string.Equals(entryTag, tag, StringComparison.OrdinalIgnoreCase)));
    }

    internal static IEnumerable<string> SplitOrcishSegments(string orcish)
    {
        return orcish
            .Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    internal static string FormatLexiconEntry(OrcishLexiconEntry entry)
    {
        return $"{entry.English}->{entry.Orcish} [{entry.PartOfSpeech ?? "?"}]";
    }

    internal static void AssertThirtyPageFamilyRoot(
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

    internal static string[] GetWeakAdventureOutlineSummaryPhrases()
    {
        return
        [
            "advances the scene",
            "adds dialogue that clarifies the exchange",
            "presses for answers or a decision",
            "reveals a concern or reaction",
            "contributes a new development to the scene",
            "handles practical preparations for the party",
            "reassures Kelpie that Morrow and her own magic protect her"
        ];
    }

    internal static void AssertDerivedElvenForm(string language, string root, string tag, string expected)
    {
        AssertTrue(
            ElvenMorphologyUtility.TryCreateDerivedForm(language, root, [tag], out var actual),
            $"{language} {tag} should be supported for '{root}'");
        AssertEqual(expected, actual, $"unexpected {language} {tag} for '{root}'");
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

    internal static UpdateManifestSigningKeyTrustEntry CreateActiveSigningKey(string publicKeyPem)
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

    internal static HostedSettingsSigningKeyTrustEntry CreateActiveHostedSettingsSigningKey(string publicKeyPem)
    {
        return new HostedSettingsSigningKeyTrustEntry("hosted-settings-test-key", publicKeyPem);
    }

    internal static RpolThreadPost CreateRpolThreadPost(int messageNumber, string author, string bodyText)
    {
        return new RpolThreadPost(
            messageNumber,
            author,
            string.Empty,
            "Mon 1 Jan 2026",
            $"{messageNumber:00}:00",
            $"{messageNumber:000}.html",
            "<div></div>",
            "<p></p>",
            bodyText);
    }

    internal static MyHeroBriefing CreateMyHeroBriefingDisplayFixture()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Jelb Garrick", "jelb-token.webp", "3", "Illusionist", "8", "Jelb sheet")
        };
        var posts = new[]
        {
            new MyHeroBriefingThreadPosts(
                "Chapter 2",
                "https://rpol.net/display.cgi?gi=80170&ti=8",
                [
                    CreateRpolThreadPost(1, "Jelb", "Jelb checks the suspicious door."),
                    CreateRpolThreadPost(2, "Dungeon Master", "Jelb hears a lock click. What do you do?")
                ])
        };
        var encryptedIndex = new[]
        {
            new EncryptedTextIndexEntry(
                "https://publish.obsidian.md/scarlethorizons/Secrets/Jelb+Only",
                1,
                ["Hero Jelb"])
        };
        return MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            AuthenticatedHeroName: "Jelb",
            ThreadPosts: posts,
            XpTotals: [new PcXpTotal("Jelb Garrick", 1234)],
            EncryptedTextIndex: encryptedIndex));
    }

    internal static string CreateRpolSourceHtml(params (int MessageNumber, string Author, string Date, string Time, string BodyText)[] posts)
    {
        return string.Join(
            Environment.NewLine,
            posts.Select(post => $"""
            <div class='message'>
            <span class='messageauthor'>{WebUtility.HtmlEncode(post.Author)}</span>
            <ul><li>msg #{post.MessageNumber}</li></ul>
            {post.Date} at {post.Time}
            <div class='messagebody' id='msg{post.MessageNumber}'>{WebUtility.HtmlEncode(post.BodyText)}</div>
            </div><!-- 1 -->
            """));
    }

    internal static void DiagnosticRetentionCleanupRemovesOldDiagnosticsAndPreservesUnrelatedScratchFiles()
    {
        var scratchPath = Path.Combine(
            GetRepositoryRoot(),
            "codex-scratch",
            $"retention-test-{Guid.NewGuid():N}");
        var diagnosticsPath = Path.Combine(scratchPath, "diagnostics");
        var oldZipPath = Path.Combine(diagnosticsPath, "player-assistant-diagnostics-20260601-010101.zip");
        var newZipPath = Path.Combine(diagnosticsPath, "player-assistant-diagnostics-20260702-010101.zip");
        var oldStagingPath = Path.Combine(diagnosticsPath, "player-assistant-diagnostics-20260601-010101");
        var oldDiagnosticsTestPath = Path.Combine(scratchPath, "diagnostics-test-old");
        var oldPublishVerificationPath = Path.Combine(scratchPath, "publish-verification-old");
        var unrelatedScratchFilePath = Path.Combine(scratchPath, "candidates.txt");
        var unrelatedScratchDirectoryPath = Path.Combine(scratchPath, "manual-notes");

        try
        {
            Directory.CreateDirectory(diagnosticsPath);
            File.WriteAllText(oldZipPath, "old diagnostic zip");
            File.WriteAllText(newZipPath, "new diagnostic zip");
            Directory.CreateDirectory(oldStagingPath);
            File.WriteAllText(Path.Combine(oldStagingPath, "marker.txt"), "old staging");
            Directory.CreateDirectory(oldDiagnosticsTestPath);
            File.WriteAllText(Path.Combine(oldDiagnosticsTestPath, "marker.txt"), "old diagnostics test");
            Directory.CreateDirectory(oldPublishVerificationPath);
            File.WriteAllText(Path.Combine(oldPublishVerificationPath, "marker.txt"), "old publish verification");
            File.WriteAllText(unrelatedScratchFilePath, "lexicon backlog should stay");
            Directory.CreateDirectory(unrelatedScratchDirectoryPath);
            File.WriteAllText(Path.Combine(unrelatedScratchDirectoryPath, "marker.txt"), "manual scratch should stay");

            var oldTime = DateTimeOffset.UtcNow - TimeSpan.FromDays(30);
            SetLastWriteTimeUtc(oldZipPath, oldTime);
            SetDirectoryLastWriteTimeUtc(oldStagingPath, oldTime);
            SetDirectoryLastWriteTimeUtc(oldDiagnosticsTestPath, oldTime);
            SetDirectoryLastWriteTimeUtc(oldPublishVerificationPath, oldTime);
            SetLastWriteTimeUtc(unrelatedScratchFilePath, oldTime);
            SetDirectoryLastWriteTimeUtc(unrelatedScratchDirectoryPath, oldTime);

            var output = RunDiagnosticsRetentionCleanup(
                scratchPath,
                "-DiagnosticRetentionDays",
                "14",
                "-ScratchRetentionDays",
                "7",
                "-MaxDiagnosticZipCount",
                "10");

            AssertEqual(0, output.ExitCode, $"diagnostic retention cleanup should pass. Output: {output.Output}");
            AssertContains(output.Output, "Diagnostic retention cleanup removed");
            AssertFalse(File.Exists(oldZipPath), "old diagnostic zip should be removed");
            AssertTrue(File.Exists(newZipPath), "fresh diagnostic zip should be preserved");
            AssertFalse(Directory.Exists(oldStagingPath), "old diagnostic staging directory should be removed");
            AssertFalse(Directory.Exists(oldDiagnosticsTestPath), "old diagnostics test scratch directory should be removed");
            AssertFalse(Directory.Exists(oldPublishVerificationPath), "old publish verification scratch directory should be removed");
            AssertTrue(File.Exists(unrelatedScratchFilePath), "unrelated scratch file should be preserved");
            AssertTrue(Directory.Exists(unrelatedScratchDirectoryPath), "unrelated scratch directory should be preserved");
        }
        finally
        {
            if (Directory.Exists(scratchPath))
            {
                Directory.Delete(scratchPath, recursive: true);
            }
        }
    }

    internal static void WithCopiedPublishDirectory(Action<string> action)
    {
        var directoryPath = Path.Combine(
            GetRepositoryRoot(),
            "codex-scratch",
            $"publish-verification-{Guid.NewGuid():N}");

        try
        {
            var currentPublishDirectory = Path.Combine(GetCurrentReleaseDirectory(), "publish");
            var currentPublishExecutablePath = Path.Combine(currentPublishDirectory, "player-assistant.exe");
            if (File.Exists(currentPublishExecutablePath))
            {
                CopyDirectory(currentPublishDirectory, directoryPath);
                ClearReadOnlyAttributes(directoryPath);
            }
            else
            {
                WriteManifestedRuntime(directoryPath);
                var currentReleaseDirectory = GetCurrentReleaseDirectory();
                foreach (var fileName in new[]
                {
                    "player-assistant.exe",
                    "player-assistant.dll",
                    "player-assistant.deps.json",
                    "player-assistant.runtimeconfig.json",
                    "playwright.ps1",
                    "Microsoft.Playwright.dll"
                })
                {
                    var sourcePath = Path.Combine(currentReleaseDirectory, fileName);
                    if (File.Exists(sourcePath))
                    {
                        File.Copy(sourcePath, Path.Combine(directoryPath, fileName), overwrite: true);
                    }
                }
            }
            File.Copy(
                Path.Combine(GetRepositoryRoot(), "settings.json"),
                Path.Combine(directoryPath, "settings.json"),
                overwrite: true);
            WriteRequiredRuntimeSidecars(directoryPath);
            var shippedLocalSettingsPath = Path.Combine(directoryPath, "settings.local.json");
            LocalSettingsUtility.SaveEncryptedSettings(
                shippedLocalSettingsPath,
                new Dictionary<string, string>
                {
                    ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
                    ["RPOL user name"] = "example-user",
                    ["RPOL password"] = "example-password"
                });
            WriteReleaseRuntimeInventory(directoryPath);
            WriteReleaseManifest(directoryPath);
            WriteReleaseProvenance(directoryPath);
            action(directoryPath);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                ClearReadOnlyAttributes(directoryPath);
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    internal static void ClearReadOnlyAttributes(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }

    internal static void SetRuntimeSidecarsReadOnly(string directoryPath)
    {
        foreach (var fileName in new[] { XpPasswordStoreUtility.FileName })
        {
            var path = Path.Combine(directoryPath, fileName);
            if (File.Exists(path))
            {
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            }
        }
    }

    internal static void WithTemporaryDiagnosticsRuntime(Action<string, string, string, string> action)
    {
        var rootPath = Path.Combine(
            GetRepositoryRoot(),
            "codex-scratch",
            $"diagnostics-test-{Guid.NewGuid():N}");
        var releasePath = Path.Combine(rootPath, "Release");
        var publishPath = Path.Combine(releasePath, "publish");
        var outputPath = Path.Combine(rootPath, "out");

        try
        {
            WriteDiagnosticsRuntime(releasePath, includeSensitiveLog: true);
            WriteDiagnosticsRuntime(publishPath, includeSensitiveLog: false);
            action(rootPath, releasePath, publishPath, outputPath);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    internal static void WriteDiagnosticsRuntime(string directoryPath, bool includeSensitiveLog)
    {
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(Path.Combine(directoryPath, "player-assistant.exe"), "fake executable");
        WriteSettingsJson(directoryPath, CreateValidAppSettings(includeCredentials: true));
        File.WriteAllText(
            Path.Combine(directoryPath, "startup-health.json"),
            """
            {
              "schema_version": 1,
              "phases": []
            }
            """);
        File.WriteAllText(
            Path.Combine(directoryPath, OutboundNetworkDiagnosticsUtility.DiagnosticsFileName),
            """
            {
              "schema_version": 1,
              "started_at": "2026-07-05T00:00:00.0000000+00:00",
              "updated_at": "2026-07-05T00:00:01.0000000+00:00",
              "endpoints": [
                {
                  "method": "GET",
                  "purpose": "PlayerAssistantHostedSettings",
                  "scheme": "https",
                  "host": "bryanmiller.us",
                  "path": "/scarlethorizons/settings.local.json",
                  "query_present": true,
                  "total_requests": 2,
                  "success_count": 1,
                  "failure_count": 1,
                  "retry_count": 1,
                  "last_outcome": "failure",
                  "last_status_code": null,
                  "last_failure_kind": "Unavailable",
                  "last_failure_summary": "The network request failed: https://user:pass@example.test/path?password=secret-password&token=secret-token Authorization: Bearer abc123 Cookie: sessionid=abc123",
                  "first_observed_at": "2026-07-05T00:00:00.0000000+00:00",
                  "last_observed_at": "2026-07-05T00:00:01.0000000+00:00"
                }
              ]
            }
            """);
        File.WriteAllText(
            Path.Combine(directoryPath, "last-crash.json"),
            """
            {
              "schema_version": 1,
              "phase": "synthetic crash",
              "exception": {
                "type": "InvalidOperationException",
                "message": "synthetic crash"
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(directoryPath, "startup-remediation.txt"),
            """
            Player Assistant startup configuration guidance

            1. Warning: synthetic warning
               Repair: synthetic repair
            """);
        File.WriteAllText(
            Path.Combine(directoryPath, "startup-errors.log"),
            includeSensitiveLog
                ? """
                  RPOL password: hunter2
                  RPOL user name: example-user
                  Authorization: Bearer abc123
                  Cookie: sessionid=abc123
                  url=https://user:pass@example.test/path?password=secret-password&token=secret-token
                  """
                : "no startup errors");
        File.WriteAllText(
            Path.Combine(directoryPath, "keyword-index.json"),
            """
            {
              "words": {
                "scarlet": []
              },
              "index_metadata": {}
            }
            """);
        File.WriteAllText(Path.Combine(directoryPath, KeywordTermsFileUtility.FileName), "scarlet");
        File.WriteAllText(Path.Combine(directoryPath, "sitemap.xml"), "<urlset />");
        File.WriteAllText(Path.Combine(directoryPath, "sitemap-keyword-urls.json"), "{}");
        File.WriteAllText(Path.Combine(directoryPath, "game-forum-chapter-prefixes.txt"), "chapter");
        File.WriteAllText(Path.Combine(directoryPath, "game-forum-chapter-downloads.txt"), "chapter");
        File.WriteAllText(Path.Combine(directoryPath, "game-forum-aside-downloads.txt"), "aside");
        File.WriteAllText(Path.Combine(directoryPath, "game-forum-ooc-downloads.txt"), "ooc");
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
            outputDirectory
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

    internal static string GetDiagnosticZipPathFromOutput(string output)
    {
        var line = output
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.StartsWith("Diagnostic bundle created:", StringComparison.Ordinal));
        if (line is null)
        {
            throw new InvalidOperationException($"Unable to find diagnostic bundle path in output: {output}");
        }

        return line["Diagnostic bundle created:".Length..].Trim();
    }

    internal static string[] GetZipEntryNames(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return archive.Entries
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string ReadZipEntryText(string zipPath, string entryName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.SingleOrDefault(entry =>
            string.Equals(entry.FullName.Replace('\\', '/'), entryName, StringComparison.Ordinal));
        if (entry is null)
        {
            throw new InvalidOperationException($"Zip entry '{entryName}' was not found in {zipPath}.");
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static string GetCurrentPublishDirectory()
    {
        var publishDirectory = Path.Combine(GetRepositoryRoot(), "Release", "publish");
        if (!Directory.Exists(publishDirectory))
        {
            throw new InvalidOperationException($"Publish directory is missing: {publishDirectory}");
        }

        return publishDirectory;
    }

    internal static string GetCurrentReleaseDirectory()
    {
        var releaseDirectory = Path.Combine(GetRepositoryRoot(), "Release");
        if (!Directory.Exists(releaseDirectory))
        {
            throw new InvalidOperationException($"Release directory is missing: {releaseDirectory}");
        }

        return releaseDirectory;
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

    internal static string ResolvePowerShellExecutable()
    {
        var candidates = new[]
        {
            Environment.ProcessPath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "WindowsApps",
                "pwsh.exe")
        };

        foreach (var candidate in candidates)
        {
            if (IsPowerShellExecutablePath(candidate))
            {
                return candidate;
            }
        }

        foreach (var commandName in new[] { "pwsh.exe", "powershell.exe", "powershell" })
        {
            string? resolved = Environment.GetEnvironmentVariable("PATH")?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => Path.Combine(path.Trim(), commandName))
                .FirstOrDefault(IsPowerShellExecutablePath);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        throw new InvalidOperationException("Unable to locate a PowerShell executable.");
    }

    internal static bool IsPowerShellExecutablePath([NotNullWhen(true)] string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        return string.Equals(fileName, "pwsh.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "powershell.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "powershell", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "pwsh", StringComparison.OrdinalIgnoreCase);
    }

    internal static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "publish-player-assistant.ps1")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }

    internal static string GetCanonicalVersion(string propertyName = "PlayerAssistantVersion")
    {
        var metadataPath = Path.Combine(GetRepositoryRoot(), "version.props");
        var document = System.Xml.Linq.XDocument.Load(metadataPath);
        return document.Descendants(propertyName).Single().Value;
    }

    internal static void CopyDirectory(string sourceDirectory, string destinationDirectory)
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

    internal static void DeleteDirectoryTree(string directoryPath)
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

    internal static void AssertTrue(bool actual, string message)
    {
        if (!actual)
        {
            throw new InvalidOperationException(message);
        }
    }

    internal static void AssertFalse(bool actual, string message)
    {
        if (actual)
        {
            throw new InvalidOperationException(message);
        }
    }

    internal static void AssertContains(string value, string expected)
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

    internal static int CountOccurrences(string value, string pattern)
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

    internal static void WaitForCondition(Func<bool> condition, string message)
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

    internal static void WaitForWindowsFormsCondition(Func<bool> condition, string message)
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

    internal static void ResetRpolAuthFailureCache()
    {
        SetStaticField(typeof(RpolAuthUtility), "_cachedFatalAuthFailure", null);
        SetStaticField(typeof(RpolAuthUtility), "_cachedFatalAuthFailureLogged", false);
    }

    internal static void RunOnStaThread(Action action)
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

    internal static Task InvokePrivateAsync(object instance, string methodName, params object[] args)
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

    internal static object? InvokeStaticMethod(Type type, string methodName, params object[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (method is null)
        {
            throw new InvalidOperationException($"Unable to find static method '{methodName}'.");
        }

        return method.Invoke(null, args);
    }

    internal static object? InvokePrivateMethod(object instance, string methodName, params object[] args)
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

    internal static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is null)
        {
            throw new InvalidOperationException($"Unable to find field '{fieldName}'.");
        }

        field.SetValue(instance, value);
    }

    internal static object? GetPrivateField(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is not null)
        {
            return field.GetValue(instance);
        }

        var property = instance.GetType().GetProperty(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (property is not null)
        {
            return property.GetValue(instance);
        }

        throw new InvalidOperationException($"Unable to find field or property '{fieldName}'.");
    }

    internal static void SetStaticField(Type type, string fieldName, object? value)
    {
        var field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        if (field is null)
        {
            throw new InvalidOperationException($"Unable to find static field '{fieldName}'.");
        }

        field.SetValue(null, value);
    }

    internal static void WithTemporaryKeywordIndex(string json, Action action)
    {
        var indexPath = GetPlayerAssistantIndexPath();
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

    internal static void WithTemporaryEncryptedTextIndex(string json, Action action)
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

    internal static string GetPlayerAssistantIndexPath()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(Form1).Assembly.Location);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            throw new InvalidOperationException("Unable to resolve the player-assistant assembly directory.");
        }

        return Path.Combine(assemblyDirectory, "keyword-index.json");
    }

    internal static string GetPlayerAssistantEncryptedTextIndexPath()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(Form1).Assembly.Location);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            throw new InvalidOperationException("Unable to resolve the player-assistant assembly directory.");
        }

        return Path.Combine(assemblyDirectory, TaggedNoteCipherUtility.EncryptedTextIndexFileName);
    }

    internal static string GetStartupLogPath()
    {
        return Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
    }

    internal static string GetStartupHealthPath()
    {
        return Path.Combine(AppContext.BaseDirectory, StartupHealthUtility.HealthFileName);
    }

    internal static string GetLastCrashPath()
    {
        return Path.Combine(AppContext.BaseDirectory, LastCrashDiagnosticUtility.FileName);
    }

    internal static void WithPreservedStartupLog(Action action)
    {
        var startupLogPath = GetStartupLogPath();
        var hadStartupLog = File.Exists(startupLogPath);
        var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

        try
        {
            if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }

            action();
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

    internal static void WithPreservedFileAbsent(string filePath, Action action)
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

    internal static void WithHostedSettingsIsolation(Action action)
    {
        WithPreservedFileAbsent(
            RuntimePathUtility.GetApplicationPath("settings.local.json"),
            () => WithPreservedFileAbsent(
                RuntimePathUtility.GetUserDataPath("settings.local.json"),
                () => WithPreservedFileAbsent(
                    RuntimePathUtility.GetUserDataPath("trusted-hosted-settings-state.json"),
                    action)));
    }

    internal static void WithPreservedStartupHealth(Action action)
    {
        var startupHealthPath = GetStartupHealthPath();
        var hadStartupHealth = File.Exists(startupHealthPath);
        var originalStartupHealth = hadStartupHealth ? File.ReadAllText(startupHealthPath) : null;

        try
        {
            if (File.Exists(startupHealthPath))
            {
                File.Delete(startupHealthPath);
            }

            action();
        }
        finally
        {
            if (hadStartupHealth)
            {
                File.WriteAllText(startupHealthPath, originalStartupHealth);
            }
            else if (File.Exists(startupHealthPath))
            {
                File.Delete(startupHealthPath);
            }
        }
    }

    internal static void WithPreservedLastCrash(Action action)
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

    internal static System.Text.Json.JsonDocument LoadStartupHealthDocument()
    {
        return System.Text.Json.JsonDocument.Parse(File.ReadAllText(GetStartupHealthPath()));
    }

    internal static System.Text.Json.JsonElement FindStartupHealthPhase(
        System.Text.Json.JsonDocument document,
        string phaseName)
    {
        foreach (var phase in document.RootElement.GetProperty("phases").EnumerateArray())
        {
            if (string.Equals(phase.GetProperty("phase").GetString(), phaseName, StringComparison.Ordinal))
            {
                return phase;
            }
        }

        throw new InvalidOperationException($"Startup health phase '{phaseName}' was not found.");
    }

    internal static void AssertJsonString(
        System.Text.Json.JsonElement element,
        string propertyName,
        string expected,
        string message)
    {
        AssertEqual(expected, element.GetProperty(propertyName).GetString() ?? string.Empty, message);
    }

    internal static void AssertJsonNumber(
        System.Text.Json.JsonElement element,
        string propertyName,
        long expected,
        string message)
    {
        AssertEqual(expected, element.GetProperty(propertyName).GetInt64(), message);
    }

    internal static void AssertJsonNumberAtLeast(
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

    internal static Dictionary<string, string> CreateValidAppSettings(bool includeCredentials)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RPOL Site"] = "https://rpol.net/game.php?gi=80170",
            ["Game Intro"] = "https://rpol.net/gameinfo.php?gi=80170",
            ["The Cast"] = "https://rpol.net/gameinfo.php?action=cast&gi=80170",
            ["Obsidian Game Vault"] = "https://publish.obsidian.md/scarlethorizons",
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
        };

        if (includeCredentials)
        {
            settings["RPOL user name"] = "example-user";
            settings["RPOL password"] = "example-password";
        }

        return settings;
    }

    internal static void WriteSettingsJson(string directoryPath, IReadOnlyDictionary<string, string> settings)
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

    internal static void WriteRpolStorageState(string storageStatePath, string contents, DateTimeOffset lastWriteUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storageStatePath)!);
        File.WriteAllText(storageStatePath, contents);
        File.SetLastWriteTimeUtc(storageStatePath, lastWriteUtc.UtcDateTime);
    }

    internal static void WriteRequiredRuntimeSidecars(string directoryPath)
    {
        File.Copy(
            Path.Combine(GetRepositoryRoot(), "pwa", "magic-items.json"),
            Path.Combine(directoryPath, "magic-items.json"),
            overwrite: true);
        File.WriteAllText(
            Path.Combine(directoryPath, "keyword-index.json"),
            """
            {
              "words": {
                "scarlet": []
              },
              "index_metadata": {}
            }
            """);
        File.WriteAllText(Path.Combine(directoryPath, KeywordTermsFileUtility.FileName), "scarlet");
        File.WriteAllText(Path.Combine(directoryPath, "sitemap.xml"), "<urlset />");
        File.WriteAllText(Path.Combine(directoryPath, "sitemap-keyword-urls.json"), "{}");
        XpPasswordStoreUtility.SavePasswordHashes(
            Path.Combine(directoryPath, XpPasswordStoreUtility.FileName),
            new Dictionary<string, string>
            {
                ["Kelpie"] = "gemstone"
            });
    }

    internal static void WriteManifestedRuntime(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
        WriteRequiredRuntimeSidecars(directoryPath);
        File.WriteAllText(Path.Combine(directoryPath, "player-assistant.exe"), "synthetic executable");
        File.WriteAllText(Path.Combine(directoryPath, "settings.json"), "{}");
        LocalSettingsUtility.SaveEncryptedSettings(
            Path.Combine(directoryPath, "settings.local.json"),
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
                ["RPOL user name"] = "example-user",
                ["RPOL password"] = "example-password"
            });

        Directory.CreateDirectory(Path.Combine(directoryPath, ".playwright", "node", "win32_x64"));
        Directory.CreateDirectory(Path.Combine(directoryPath, ".playwright", "package"));
        File.WriteAllText(Path.Combine(directoryPath, ".playwright", "node", "win32_x64", "node.exe"), "synthetic node");
        File.WriteAllText(Path.Combine(directoryPath, ".playwright", "package", "package.json"), "{}");
        File.WriteAllText(Path.Combine(directoryPath, ".playwright", "package", "browsers.json"), "{}");
        WriteReleaseRuntimeInventory(directoryPath);
        WriteReleaseManifest(directoryPath);
        WriteReleaseProvenance(directoryPath);
    }

    internal static void WriteReleaseRuntimeInventory(string directoryPath)
    {
        var project = XDocument.Load(Path.Combine(GetRepositoryRoot(), "player-assistant.csproj"));
        var packages = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => new
            {
                name = element.Attribute("Include")?.Value ?? string.Empty,
                version = element.Attribute("Version")?.Value ?? string.Empty
            })
            .Where(package => !string.IsNullOrWhiteSpace(package.name))
            .OrderBy(package => package.name, StringComparer.Ordinal)
            .ToArray();

        string GetProjectProperty(string name)
        {
            return project
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == name)
                ?.Value
                ?? string.Empty;
        }

        var inventory = new
        {
            schema_version = 1,
            generated_at = DateTimeOffset.UtcNow.ToString("O"),
            app = new
            {
                version = GetCanonicalVersion(),
                file_version = GetCanonicalVersion("PlayerAssistantAssemblyVersion"),
                product_version = GetCanonicalVersion()
            },
            runtime = new
            {
                target_framework = GetProjectProperty("TargetFramework"),
                runtime_identifier = GetProjectProperty("RuntimeIdentifier"),
                self_contained = GetProjectProperty("SelfContained"),
                publish_single_file = "false",
                publish_runtime_identifier = "win-x64",
                publish_self_contained = "false"
            },
            packages,
            scripts = new[]
            {
                new { relative_path = "publish-player-assistant.ps1", length = 1L, sha256 = new string('A', 64) }
            },
            hash_algorithm = "SHA256"
        };

        File.WriteAllText(
            Path.Combine(directoryPath, "release-runtime-inventory.json"),
            System.Text.Json.JsonSerializer.Serialize(
                inventory,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void WriteReleaseManifest(string directoryPath)
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

    internal static void WriteReleaseProvenance(string directoryPath)
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

    internal static string[] GetReleaseManifestRelativePaths()
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

    internal static object GetReleaseManifestEntry(string directoryPath, string relativePath)
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

    internal static void SetLastWriteTimeUtc(string filePath, DateTimeOffset value)
    {
        File.SetLastWriteTimeUtc(filePath, value.UtcDateTime);
    }

    internal static void SetDirectoryLastWriteTimeUtc(string directoryPath, DateTimeOffset value)
    {
        Directory.SetLastWriteTimeUtc(directoryPath, value.UtcDateTime);
    }

    internal static void WriteVisiblePng(string filePath)
    {
        using var bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        bitmap.SetPixel(0, 0, Color.Black);
        bitmap.Save(filePath, ImageFormat.Png);

        using var padding = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None);
        padding.SetLength(600_000);
    }

    internal static void WriteTransparentPng(string filePath)
    {
        using var bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        bitmap.Save(filePath, ImageFormat.Png);
    }

    internal static string CreateSampleRpolThreadHtml()
    {
        return """
            <html><body>
            <div class='message'>
                <ul><li>msg #1</li></ul>
                <span class='messageauthor'>Alice</span>
                <div class='characterdetails'>Scout</div>
                <span>Mon 1 Jan 2024 at 12:00</span>
                <div class='messagebody' id='msg1'>Hello from Alice.</div>
            </div><!-- 1 -->
            </div><!-- 2 -->
            <div class='message'>
                <ul><li>msg #2</li></ul>
                <span class='messageauthor'>Bob</span>
                <div class='characterdetails'>Wizard</div>
                <span>Tue 2 Jan 2024 at 13:30</span>
                <div class='messagebody' id='msg2'>Hello from Bob.</div>
            </div><!-- 1 -->
            </div><!-- 2 -->
            </body></html>
            """;
    }

    internal static string CreateSitemapXml(params string[] urls)
    {
        var entries = string.Join(
            Environment.NewLine,
            urls.Select(url => $"  <url><loc>{System.Security.SecurityElement.Escape(url)}</loc></url>"));
        return $"<urlset>{Environment.NewLine}{entries}{Environment.NewLine}</urlset>";
    }

    internal static string CreateV1LocalSettingsEnvelope(string userName, string password)
    {
        var plaintext = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, string>
            {
                ["RPOL user name"] = userName,
                ["RPOL password"] = password
            },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var key = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("PlayerAssistant.LocalSettings.v1"));
        var iv = RandomNumberGenerator.GetBytes(16);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        var payloadBytes = new byte[iv.Length + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, payloadBytes, 0, iv.Length);
        Buffer.BlockCopy(ciphertext, 0, payloadBytes, iv.Length, ciphertext.Length);

        return $$"""
            {
              "format": "app-protected-v1",
              "payload": "{{Convert.ToBase64String(payloadBytes)}}"
            }
            """;
    }

    internal static string CreateV2LocalSettingsEnvelope(string userName, string password)
    {
        var plaintext = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, string>
            {
                ["RPOL user name"] = userName,
                ["RPOL password"] = password
            },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var key = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("PlayerAssistant.LocalSettings.v1"));
        var authenticationKey = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("PlayerAssistant.LocalSettings.v1.hmac"));
        var iv = RandomNumberGenerator.GetBytes(16);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        var protectedContent = new byte[iv.Length + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, protectedContent, 0, iv.Length);
        Buffer.BlockCopy(ciphertext, 0, protectedContent, iv.Length, ciphertext.Length);

        byte[] tag;
        using (var hmac = new HMACSHA256(authenticationKey))
        {
            tag = hmac.ComputeHash(protectedContent);
        }

        var payloadBytes = new byte[protectedContent.Length + tag.Length];
        Buffer.BlockCopy(protectedContent, 0, payloadBytes, 0, protectedContent.Length);
        Buffer.BlockCopy(tag, 0, payloadBytes, protectedContent.Length, tag.Length);

        return $$"""
            {
              "format": "app-protected-v2",
              "payload": "{{Convert.ToBase64String(payloadBytes)}}"
            }
            """;
    }

    internal static void WriteSettingsJsonWithHostedLocalSettings(string directoryPath, string hostedLocalSettingsUrl)
    {
        File.WriteAllText(
            Path.Combine(directoryPath, "settings.json"),
            $$"""
            {
              "schema_version": 1,
              "Hosted Local Settings": "{{hostedLocalSettingsUrl}}",
              "RPOL Site": "https://rpol.net/game.php?gi=80170",
              "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
              "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
              "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons"
            }
            """);
    }

    internal static void AssertHostedSettingsFailure(
        string hostedLocalSettingsUrl,
        string expectedLogFragment,
        IReadOnlyList<HostedSettingsSigningKeyTrustEntry>? trustedSigningKeys = null,
        int? expectedRequestCount = null,
        string? expectedRequestPath = null)
    {
        using var directory = TemporaryDirectory.Create();
        WriteSettingsJsonWithHostedLocalSettings(directory.Path, hostedLocalSettingsUrl);

        using var allowlistScope = NetworkUrlAllowlistUtility.UseValidationOverrideForTests((uri, purpose) =>
        {
            if (purpose == NetworkUrlPurpose.PlayerAssistantHostedSettings
                && string.Equals(uri.AbsoluteUri, hostedLocalSettingsUrl, StringComparison.Ordinal))
            {
                return NetworkUrlAllowlistValidation.Allowed(uri);
            }

            return null;
        });
        using var trustedHostedSettingsKeysScope = trustedSigningKeys is null
            ? null
            : HostedSettingsTrustUtility.UseTrustedSigningKeysForTests(trustedSigningKeys);

        WithHostedSettingsIsolation(() =>
            WithPreservedStartupLog(() =>
            {
                var exception = AssertThrows<InvalidOperationException>(() =>
                    AppSettingsUtility.LoadSettings(directory.Path));

                AssertContains(exception.Message, "XP Tracking");
                var startupLog = File.ReadAllText(GetStartupLogPath());
                AssertContains(startupLog, "hosted local settings load");
                AssertContains(startupLog, hostedLocalSettingsUrl);
                AssertContains(startupLog, expectedLogFragment);
                AssertFalse(
                    File.Exists(Path.Combine(directory.Path, "settings.local.json")),
                    "failed hosted local settings should not be persisted into the runtime directory");
            }));

        if (expectedRequestCount is not null || expectedRequestPath is not null)
        {
            var requestObservation = LoopbackHttpServer.GetObservation(hostedLocalSettingsUrl);
            AssertTrue(requestObservation is not null, "expected fixture server observation for hosted settings request");

            if (expectedRequestCount is not null)
            {
                AssertEqual(expectedRequestCount.Value, requestObservation!.RequestCount, "unexpected hosted settings fixture request count");
            }

            if (expectedRequestPath is not null)
            {
                AssertEqual(expectedRequestPath, requestObservation!.LastRequestPath, "unexpected hosted settings fixture request path");
            }
        }
    }

    internal static string CorruptHostedSettingsSignature(string hostedSettingsJson)
    {
        using var document = System.Text.Json.JsonDocument.Parse(hostedSettingsJson);
        var root = document.RootElement;
        var signature = root.GetProperty("signature").GetString()
            ?? throw new InvalidOperationException("Hosted settings signature was missing.");
        var signatureBytes = Convert.FromBase64String(signature);
        signatureBytes[^1] ^= 0x01;
        var tamperedSignature = Convert.ToBase64String(signatureBytes);

        return $$"""
            {
              "schema_version": {{root.GetProperty("schema_version").GetInt32()}},
              "format": "{{root.GetProperty("format").GetString()}}",
              "content_id": "{{root.GetProperty("content_id").GetString()}}",
              "version": "{{root.GetProperty("version").GetString()}}",
              "encrypted_settings": {{System.Text.Json.JsonSerializer.Serialize(root.GetProperty("encrypted_settings").GetString())}},
              "signature": "{{tamperedSignature}}"
            }
            """;
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    private TemporaryDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TemporaryDirectory Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return new TemporaryDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public ScriptedHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return _handler(request, cancellationToken);
    }
}

internal sealed class ChunkedHttpContent : HttpContent
{
    private readonly byte[] _bytes;

    public ChunkedHttpContent(byte[] bytes)
    {
        _bytes = bytes;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return stream.WriteAsync(_bytes, 0, _bytes.Length);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override Stream CreateContentReadStream(CancellationToken cancellationToken)
    {
        return new MemoryStream(_bytes, writable: false);
    }
}

internal sealed class LoopbackHttpServer : IDisposable
{
    private static readonly object ObservationSyncRoot = new();
    private static readonly Dictionary<string, RequestObservation> Observations = new(StringComparer.Ordinal);
    private readonly TcpListener _listener;
    private readonly Task _serverTask;
    private readonly string _expectedPath;
    private readonly byte[] _responseBytes;
    private readonly string _contentType;
    private int _requestCount;

    public LoopbackHttpServer(string expectedPath, string responseBody, string contentType = "application/json; charset=utf-8")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPath);
        ArgumentNullException.ThrowIfNull(responseBody);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        _expectedPath = expectedPath;
        _responseBytes = System.Text.Encoding.UTF8.GetBytes(responseBody);
        _contentType = contentType;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Url = $"http://127.0.0.1:{port}{expectedPath}";
        _serverTask = Task.Run(ServeSingleRequest);
    }

    public string Url { get; }

    public int RequestCount => Volatile.Read(ref _requestCount);

    public string LastRequestPath { get; private set; } = string.Empty;

    public static RequestObservation? GetObservation(string url)
    {
        lock (ObservationSyncRoot)
        {
            return Observations.TryGetValue(url, out var observation)
                ? observation
                : null;
        }
    }

    public void Dispose()
    {
        _listener.Stop();

        try
        {
            _serverTask.GetAwaiter().GetResult();
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ServeSingleRequest()
    {
        using var client = _listener.AcceptTcpClient();
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var requestLine = reader.ReadLine() ?? throw new InvalidOperationException("Fixture server did not receive an HTTP request line.");
        var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        LastRequestPath = requestParts.Length >= 2 ? requestParts[1] : string.Empty;

        string? headerLine;
        do
        {
            headerLine = reader.ReadLine();
        }
        while (!string.IsNullOrEmpty(headerLine));

        Interlocked.Increment(ref _requestCount);
        lock (ObservationSyncRoot)
        {
            Observations[Url] = new RequestObservation(RequestCount, LastRequestPath);
        }

        var statusLine = string.Equals(LastRequestPath, _expectedPath, StringComparison.Ordinal)
            ? "HTTP/1.1 200 OK"
            : "HTTP/1.1 404 Not Found";
        var responseBody = string.Equals(LastRequestPath, _expectedPath, StringComparison.Ordinal)
            ? _responseBytes
            : System.Text.Encoding.UTF8.GetBytes("not found");
        var responseHeaders = string.Join(
            "\r\n",
            [
                statusLine,
                $"Content-Type: {_contentType}",
                $"Content-Length: {responseBody.Length}",
                "Connection: close",
                string.Empty,
                string.Empty
            ]);
        var headerBytes = System.Text.Encoding.ASCII.GetBytes(responseHeaders);
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(responseBody, 0, responseBody.Length);
        stream.Flush();
    }

    internal sealed record RequestObservation(int RequestCount, string LastRequestPath);
}

internal sealed class TestTranslatorBackend : ITranslatorBackend
{
    public bool IsReady(TranslatorTargetLanguage targetLanguage) =>
        targetLanguage == TranslatorTargetLanguage.Elven;

    public Task<OrcishTranslatorWarmupResult> WaitUntilReadyAsync(
        TranslatorTargetLanguage targetLanguage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OrcishTranslatorWarmupResult(42, TimeSpan.Zero));
    }

    public Task<int> StartPreloadingAsync(TranslatorTargetLanguage targetLanguage)
    {
        return Task.FromResult(37);
    }

    public string Translate(
        string input,
        TranslatorTargetLanguage targetLanguage,
        bool targetToEnglish)
    {
        return $"translated:{targetLanguage}:{targetToEnglish}:{input}";
    }
}

internal sealed class InMemoryWindowsCredentialStoreBackend : IWindowsCredentialStoreBackend
{
    private readonly Dictionary<string, StoredSecret> _secrets = new(StringComparer.Ordinal);

    public bool TryRead(string targetName, out StoredSecret? storedSecret)
    {
        if (_secrets.TryGetValue(targetName, out var existingSecret))
        {
            storedSecret = existingSecret with { SecretBytes = [.. existingSecret.SecretBytes] };
            return true;
        }

        storedSecret = null;
        return false;
    }

    public void Write(string targetName, byte[] secretBytes, string? comment = null)
    {
        _secrets[targetName] = new StoredSecret([.. secretBytes], DateTimeOffset.UtcNow);
    }

    public void Delete(string targetName)
    {
        _secrets.Remove(targetName);
    }
}

internal sealed class ThrowingWindowsCredentialStoreBackend : IWindowsCredentialStoreBackend
{
    public bool TryRead(string targetName, out StoredSecret? storedSecret)
    {
        storedSecret = null;
        throw new InvalidOperationException("Credential store unavailable for test.");
    }

    public void Write(string targetName, byte[] secretBytes, string? comment = null)
    {
        throw new InvalidOperationException("Credential store unavailable for test.");
    }

    public void Delete(string targetName)
    {
        throw new InvalidOperationException("Credential store unavailable for test.");
    }
}

internal sealed class ObservedWindowsCredentialStoreBackend : IWindowsCredentialStoreBackend
{
    private readonly Dictionary<string, StoredSecret> _secrets = new(StringComparer.Ordinal);

    public byte[]? LastWriteInputBytes { get; private set; }

    public byte[]? LastReadOutputBytes { get; private set; }

    public bool TryRead(string targetName, out StoredSecret? storedSecret)
    {
        if (_secrets.TryGetValue(targetName, out var existingSecret))
        {
            LastReadOutputBytes = [.. existingSecret.SecretBytes];
            storedSecret = new StoredSecret(LastReadOutputBytes, existingSecret.LastWritten);
            return true;
        }

        LastReadOutputBytes = null;
        storedSecret = null;
        return false;
    }

    public void Write(string targetName, byte[] secretBytes, string? comment = null)
    {
        LastWriteInputBytes = secretBytes;
        _secrets[targetName] = new StoredSecret([.. secretBytes], DateTimeOffset.UtcNow);
    }

    public void Delete(string targetName)
    {
        _secrets.Remove(targetName);
    }
}
