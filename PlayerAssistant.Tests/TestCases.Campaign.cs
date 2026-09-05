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
    internal static void RpolCredentialSubmissionRejectsFormSubstitution()
    {
        var gameUri = new Uri("https://rpol.net/game.php?gi=80170");
        AssertFalse(
            RpolCredentialSubmissionPolicy.TryValidateLoginForm(
                gameUri,
                "https://evil.example/login.cgi",
                "POST",
                "_self",
                out _),
            "credential submission must reject an untrusted form action");
        AssertFalse(
            RpolCredentialSubmissionPolicy.TryValidateLoginForm(
                gameUri,
                "https://rpol.net/login.cgi",
                "GET",
                "_self",
                out _),
            "credential submission must reject a non-POST form");
        AssertFalse(
            RpolCredentialSubmissionPolicy.TryValidateLoginForm(
                gameUri,
                "https://rpol.net/login.cgi",
                "POST",
                "_blank",
                out _),
            "credential submission must reject a cross-frame form target");
    }

    internal static void RpolCredentialSubmissionAcceptsExactForm()
    {
        AssertTrue(
            RpolCredentialSubmissionPolicy.TryValidateLoginForm(
                new Uri("https://rpol.net/game.php?gi=80170"),
                "/login.cgi",
                "post",
                "",
                out _),
            "the exact RPOL login form should be accepted");
    }

    internal static void RpolCredentialSubmissionRejectsNavigationRace()
    {
        AssertFalse(
            RpolCredentialSubmissionPolicy.CanSubmitAfterAwaitedOperation(
                new Uri("https://rpol.net/game.php?gi=80170"),
                new Uri("https://rpol.net/display.cgi?gi=80170"),
                new Uri("https://rpol.net/display.cgi?gi=80170"),
                "/login.cgi",
                "POST",
                "",
                out _),
            "a navigation race between fill and submit must abort credential submission");
    }

    internal static void RpolCredentialSubmissionRequestGuardValidatesDestinationMethodAndFrame()
    {
        var pageUri = new Uri("https://rpol.net/game.php?gi=80170");
        var loginUri = new Uri("https://rpol.net/login.cgi");
        AssertTrue(
            RpolCredentialSubmissionPolicy.TryValidateCredentialRequest(pageUri, loginUri, "POST", true, out _),
            "the exact main-frame RPOL POST must be accepted at transmission");
        AssertFalse(
            RpolCredentialSubmissionPolicy.TryValidateCredentialRequest(pageUri, loginUri, "POST", false, out _),
            "a credential request from a child frame must be blocked");
        AssertFalse(
            RpolCredentialSubmissionPolicy.TryValidateCredentialRequest(pageUri, new Uri("https://evil.example/login.cgi"), "POST", true, out _),
            "a credential request to an unrelated destination must be blocked");
        AssertFalse(
            RpolCredentialSubmissionPolicy.TryValidateCredentialRequest(pageUri, loginUri, "GET", true, out _),
            "a non-POST credential request must be blocked");
    }

    internal static void RpolCanonicalProbeRejectsEmbeddedLoginAndExposesExactIdentity()
    {
        var probe = RpolProtectedResourceUtility.CanonicalDiceRollerProbe;
        AssertEqual(80170, probe.GameId, "the canonical RPOL probe must target game 80170");
        AssertEqual("/usermodules/diceroller.cgi", probe.Uri.AbsolutePath, "the canonical probe path must be the Dice Roller resource");
        AssertEqual("?gi=80170", probe.Uri.Query, "the canonical probe must use the exact game query");
        var html = "<html><head><title>Dice Roller</title></head><body><form action='/login.cgi'><input name='username'><input name='password' type='password'></form><div>Step 1: Choose the Dice</div><div>Roll the Dice</div></body></html>";
        var classification = RpolProtectedResourceUtility.Classify(
            probe.Uri, probe.Uri, 200, "text/html", html);
        AssertEqual(RpolProtectedResourceKind.LoginRequired, classification.Kind, "an embedded login form must never prove protected authentication");
    }

    internal static void RpolCanonicalProbeRejectsWrongGameAndCookieOnlyEvidence()
    {
        var probe = RpolProtectedResourceUtility.CanonicalDiceRollerProbe;
        var wrongGame = new Uri("https://rpol.net/usermodules/diceroller.cgi?gi=99999");
        var classification = RpolProtectedResourceUtility.Classify(
            wrongGame, wrongGame, 200, "text/html", "<html><title>Dice Roller</title><body>Step 1: Choose the Dice Roll the Dice</body></html>");
        AssertEqual(RpolProtectedResourceKind.UntrustedNavigation, classification.Kind, "a Dice Roller response for another game must not prove the canonical resource");
        classification = RpolProtectedResourceUtility.Classify(
            probe.Uri, probe.Uri, 200, "text/html", "<html><title>Dice Roller</title><body>Step 1: Choose the Dice Roll the Dice</body></html>");
        AssertEqual(RpolProtectedResourceKind.AuthenticatedProtectedContent, classification.Kind, "only the live canonical page shape, not cookie presence, is proof");
    }

    internal static void RpolProtectedProbeRejectsResponseOrSettledUrlChanges()
    {
        var requested = RpolAuthUtility.ProtectedDiceRollerUri;
        var protectedHtml = "<html><title>RPoL: Die Roller</title><body><h1>Die Roller</h1><pre>roll</pre></body></html>";
        var responseRedirect = RpolAuthUtility.ClassifyProtectedResource(
            requested,
            new Uri("https://rpol.net/login.cgi"),
            requested,
            200,
            "text/html",
            protectedHtml);
        var settledRedirect = RpolAuthUtility.ClassifyProtectedResource(
            requested,
            requested,
            new Uri("https://rpol.net/display.cgi?gi=80170"),
            200,
            "text/html",
            protectedHtml);
        AssertEqual(RpolProtectedResourceKind.LoginRequired, responseRedirect.Kind, "response redirect must fail closed");
        AssertEqual(RpolProtectedResourceKind.UntrustedNavigation, settledRedirect.Kind, "settled URL change must fail closed");
    }

    internal static void RpolPublisherEquivalentProofUsesSeparateProcessArchitecture()
    {
        var arguments = RpolPublisherEquivalentProcessProof.CreateChildArguments();
        AssertTrue(arguments.Contains("--rpol-state-proof", StringComparer.Ordinal), "publisher proof must launch a dedicated proof process");
        var cdpArguments = RpolPublisherEquivalentProcessProof.CreateChildArguments("http://127.0.0.1:54808");
        AssertTrue(cdpArguments.Contains("--rpol-cdp-endpoint", StringComparer.Ordinal), "publisher proof must carry the authenticated browser CDP option");
        AssertTrue(cdpArguments.Contains("http://127.0.0.1:54808", StringComparer.Ordinal), "publisher proof must carry only the loopback CDP endpoint");
        AssertTrue(
            RpolPublisherEquivalentProcessProof.IsSeparateProcessProof(
                new RpolPublisherEquivalentProcessProofMetadata("publisher", "separate-process", true)),
            "publisher proof metadata must identify a separate process");
    }

    internal static void RpolSeparateProcessNormalActiveLoaderProofRuns()
    {
        using var directory = TemporaryDirectory.Create();
        var proofPath = Path.Combine(directory.Path, "normal-active-proof.txt");
        var startInfo = CreateTestChildProcessStartInfo(["--rpol-normal-active-loader-child", proofPath]);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The normal active-loader proof process could not be started.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        AssertEqual(0, process.ExitCode, $"the separate process must prove the normal active loader: {error}");
        AssertEqual("separate-process-prior", File.ReadAllText(proofPath), "the separate process normal active loader must fail closed to the prior verified state");
    }

    internal static void RpolCandidatePromotionPreservesActiveOnFailure()
    {
        var active = "active-state";
        var candidate = "candidate-state";
        var restored = active;
        var writeCount = 0;
        var promoted = RpolStorageStateTransaction.TryPromote(
            candidate,
            () => restored,
            value =>
            {
                restored = value;
                if (++writeCount == 1)
                {
                    throw new IOException("synthetic promotion failure");
                }
            },
            out _);
        AssertFalse(promoted, "a failed candidate promotion must report failure");
        AssertEqual(active, restored, "a failed candidate promotion must preserve active state");
    }

    internal static void RpolVersionedPromotionPreservesVerifiedPointerAtEveryFaultBoundary()
    {
        var slots = new Dictionary<string, string> { ["A"] = "active-state" };
        var pointer = new RpolActiveStatePointer(1, "A", null, true, HashText("active-state"));
        foreach (var fault in new[] { "slot", "pointer", "readback" })
        {
            var localSlots = new Dictionary<string, string>(slots);
            var localPointer = pointer;
            var pointerReadCount = 0;
            var threw = false;
            var promoted = RpolVersionedStateTransaction.TryPromote(
                "candidate-state",
                () =>
                {
                    pointerReadCount++;
                    if (fault == "readback" && pointerReadCount >= 2)
                    {
                        threw = true;
                        return localPointer with { Version = localPointer.Version + 99 };
                    }

                    return localPointer;
                },
                slot => localSlots.TryGetValue(slot, out var value) ? value : null,
                (slot, value) =>
                {
                    if (fault == "slot")
                    {
                        threw = true;
                        throw new IOException("slot fault");
                    }
                    localSlots[slot] = value;
                },
                value =>
                {
                    if (fault == "pointer")
                    {
                        threw = true;
                        throw new IOException("pointer fault");
                    }
                    localPointer = value;
                },
                out _,
                out _);
            AssertFalse(promoted, $"fault boundary '{fault}' must not report promotion success");
            AssertTrue(threw, $"fault boundary '{fault}' must be exercised");
            AssertEqual("A", localPointer.ActiveSlot, $"fault boundary '{fault}' must preserve the verified pointer");
            AssertTrue(localPointer.Verified, $"fault boundary '{fault}' must preserve verified state");
            AssertEqual("active-state", localSlots["A"], $"fault boundary '{fault}' must preserve active bytes");
        }

        static string HashText(string value) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    }

    internal static void RpolVersionedPromotionRequiresPostPromotionVerification()
    {
        var slots = new Dictionary<string, string> { ["A"] = "active-state" };
        var pointer = new RpolActiveStatePointer(1, "A", null, true, HashText("active-state"));
        AssertTrue(
            RpolVersionedStateTransaction.TryPromote(
                "candidate-state",
                () => pointer,
                slot => slots.TryGetValue(slot, out var value) ? value : null,
                (slot, value) => slots[slot] = value,
                value => pointer = value,
                out _,
                out _),
            "candidate slot and pending pointer should promote atomically");
        AssertFalse(pointer.Verified, "promotion must remain pending until normal active-load proof succeeds");
        AssertTrue(
            RpolVersionedStateTransaction.TryReadVerifiedState(pointer, slot => slots[slot], out var state)
                && string.Equals("active-state", state, StringComparison.Ordinal),
            "the verified-state rollback reader must retain prior state while pending");
        AssertTrue(
            RpolVersionedStateTransaction.TryReadNormalActiveState(pointer, slot => slots[slot], out state)
                && string.Equals("active-state", state, StringComparison.Ordinal),
            "the normal publisher active loader must never expose an unverified pending candidate");
        RpolVersionedStateTransaction.MarkVerified(pointer, () => pointer, value => pointer = value);
        AssertTrue(pointer.Verified, "post-promotion proof must explicitly mark the active pointer verified");
        AssertTrue(
            RpolVersionedStateTransaction.TryReadVerifiedState(pointer, slot => slots[slot], out state)
                && string.Equals("candidate-state", state, StringComparison.Ordinal),
            "verified normal active load must read the promoted state");

        static string HashText(string value) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    }

    internal static void RpolCredentialStoreRollbackProofUsesNormalActiveLoader()
    {
        using var backendScope = RuntimeSecretStoreUtility.UseBackendForTests(new InMemoryWindowsCredentialStoreBackend());
        RuntimeSecretStoreUtility.SaveRpolStorageState("prior-state");
        var priorPointer = RuntimeSecretStoreUtility.CaptureRpolActiveStatePointer()
            ?? throw new InvalidOperationException("the credential-store backend must expose the prior active pointer");
        RuntimeSecretStoreUtility.SaveRpolStorageStateCandidate("candidate-state");
        AssertTrue(RuntimeSecretStoreUtility.PromoteRpolStorageStateCandidate(out var promotionError), promotionError ?? "candidate promotion must succeed in the credential-store backend");
        AssertTrue(
            RuntimeSecretStoreUtility.TryGetRpolStorageState(out var pendingState, out _)
            && string.Equals(pendingState, "prior-state", StringComparison.Ordinal),
            "the normal credential-store loader must expose only the prior verified state while promotion is pending");
        RuntimeSecretStoreUtility.RestoreRpolActiveStatePointer(priorPointer);
        AssertTrue(
            RuntimeSecretStoreUtility.VerifyRpolActiveStateRestored(priorPointer, "prior-state", out var reason),
            reason);
        AssertFalse(
            RuntimeSecretStoreUtility.VerifyRpolActiveStateRestored(priorPointer with { Version = priorPointer.Version + 1 }, "prior-state", out _),
            "rollback proof must fail when the read-back pointer does not match the captured prior pointer");
    }

    internal static void RpolExternalBrowserUsesEphemeralPortAndAuthenticatedEndpoint()
    {
        using var temporary = TemporaryDirectory.Create();
        var args = RpolExternalBrowserConnection.CreateLaunchArguments(temporary.Path, "C:/notice.html");
        AssertTrue(args.Contains("--remote-debugging-port=0", StringComparer.Ordinal), "external browser must choose its own CDP port while holding the profile lease");
        AssertTrue(args.Contains("--remote-debugging-address=127.0.0.1", StringComparer.Ordinal), "external browser CDP must bind only to loopback");
        AssertFalse(args.Any(argument => argument.StartsWith("--remote-debugging-port=") && !argument.Equals("--remote-debugging-port=0", StringComparison.Ordinal)), "the caller must not preallocate and release a port");

        File.WriteAllText(Path.Combine(temporary.Path, "DevToolsActivePort"), "54808\n/devtools/browser/test-token\n");
        var endpoint = RpolExternalBrowserConnection.ReadEndpoint(temporary.Path);
        AssertEqual("http://127.0.0.1:54808/", endpoint.AbsoluteUri, "the endpoint must be derived from the browser-owned active-port file");
        AssertTrue(RpolExternalBrowserConnection.IsLoopbackEndpoint(endpoint), "only loopback CDP endpoints are accepted");
    }

    internal static void RpolExternalBrowserRejectsForgedOrMalformedEndpoint()
    {
        using var temporary = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temporary.Path, "DevToolsActivePort"), "54808\n/devtools/browser/test-token\n");
        AssertThrows<InvalidDataException>(() => RpolExternalBrowserConnection.ValidateEndpoint(new Uri("http://10.0.0.5:54808/")));
        File.WriteAllText(Path.Combine(temporary.Path, "DevToolsActivePort"), "not-a-port\n");
        AssertThrows<InvalidDataException>(() => RpolExternalBrowserConnection.ReadEndpoint(temporary.Path));
    }

    internal static void RpolExternalBrowserContinuesAfterLauncherParentExit()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "RpolAuthUtility.cs"));
        var connectStart = source.IndexOf("ConnectToExternalBrowserAsync", StringComparison.Ordinal);
        var connectEnd = source.IndexOf("WaitForExternalBrowserAuthenticationAsync", connectStart, StringComparison.Ordinal);
        AssertTrue(connectStart >= 0 && connectEnd > connectStart, "the external-browser connection method must remain present");
        var connectSource = source[connectStart..connectEnd];
        AssertFalse(connectSource.Contains("verificationProcess.HasExited", StringComparison.Ordinal), "launcher-parent exit must not abort the browser-owned CDP rendezvous");
        AssertTrue(connectSource.Contains("ReadEndpoint(profileDirectory)", StringComparison.Ordinal), "connection must continue polling the browser-owned endpoint");
    }

    internal static void RpolOperationDeadlineExposesOneMonotonicBudget()
    {
        using var deadline = RpolOperationDeadline.Create(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        AssertTrue(deadline.RemainingOperation > TimeSpan.Zero, "the operation budget must be observable");
        AssertTrue(deadline.CleanupDeadlineUtc > deadline.OperationDeadlineUtc, "cleanup must have a separate bounded margin");
        AssertThrows<TimeoutException>(() => deadline.ThrowIfExpired(DateTimeOffset.UtcNow.AddSeconds(3)));
    }

    internal static void RpolCrossProcessLockExcludesConcurrentRun()
    {
        var lockName = $"PlayerAssistant.Tests.Rpol.{Guid.NewGuid():N}";
        using var held = RpolCrossProcessLock.TryAcquire(lockName);
        AssertTrue(held is not null, "the first RPOL operation should acquire its lock");
        RpolCrossProcessLock? second;
        using (ExecutionContext.SuppressFlow())
        {
            second = Task.Run(() => RpolCrossProcessLock.TryAcquire(lockName)).GetAwaiter().GetResult();
        }
        AssertTrue(second is null, "a concurrent RPOL operation should be excluded by the cross-process lock");
    }

    internal static void RpolPublisherRefreshCarriesLockOwnership()
    {
        var root = GetRepositoryRoot();
        var authSource = File.ReadAllText(Path.Combine(root, "RpolAuthUtility.cs"));
        var snapshotSource = File.ReadAllText(Path.Combine(root, "RpolSnapshotUtility.cs"));
        var programSource = File.ReadAllText(Path.Combine(root, "Program.cs"));
        AssertTrue(authSource.Contains("AcquireReentrant", StringComparison.Ordinal), "auth persistence must support an owned reentrant lease");
        AssertTrue(authSource.Contains("PersistStorageStateAsync(storageStateJson, cancellationToken, lockOwner, cdpEndpoint)", StringComparison.Ordinal), "auth refresh must pass publisher lock ownership to persistence");
        AssertTrue(snapshotSource.Contains("GetSnapshotResponseAsync(sourceUri, cancellationToken, lockOwner)", StringComparison.Ordinal), "publisher snapshot fetch must carry lock ownership into auth refresh");
        AssertTrue(programSource.Contains("PublishAsync(deadline.OperationToken, operationLock)", StringComparison.Ordinal), "the production publisher must pass its existing lock into refresh");
    }

    internal static void RpolInitialAndStaleDiscoveryRefreshAuthUnderOwnedPublisherLock()
    {
        var persistedOwners = new List<RpolCrossProcessLock>();
        var responseOwners = new List<RpolCrossProcessLock>();
        var lockName = RpolCrossProcessLock.AuthAndPublisherName;
        RpolAuthUtility.SnapshotResponseOverrideForTests = async (_, cancellationToken, owner) =>
        {
            responseOwners.Add(owner);
            await RpolAuthUtility.PersistVerifiedStorageStateJsonAsync(
                "test-auth-state",
                cancellationToken,
                owner);
            return new RpolResponse(
                System.Text.Encoding.UTF8.GetBytes("<html><body>Game Links</body></html>"),
                "text/html; charset=utf-8");
        };
        RpolAuthUtility.PersistVerifiedStorageStateJsonOverrideForTests = (_, _, owner) =>
        {
            persistedOwners.Add(owner);
            return Task.CompletedTask;
        };

        try
        {
            using var publisherOwner = RpolCrossProcessLock.Acquire(lockName, TimeSpan.FromSeconds(1));
            using var separateOwner = RpolCrossProcessLock.TryAcquire(lockName);
            AssertTrue(separateOwner is null, "a separate publisher owner must remain excluded while discovery owns the lock");

            var initial = RpolSnapshotUtility.DiscoverSourceUrisAsync(
                CancellationToken.None,
                publisherOwner).GetAwaiter().GetResult();
            var staleStartup = RpolSnapshotUtility.DiscoverSourceUrisAsync(
                CancellationToken.None,
                publisherOwner).GetAwaiter().GetResult();

            AssertTrue(initial.SourceUris.Count > 0, "initial publisher discovery must return validated sources");
            AssertTrue(staleStartup.SourceUris.Count > 0, "stale-startup discovery must return validated sources");
            AssertEqual(2, responseOwners.Count, "both discovery paths must perform authenticated source discovery");
            AssertEqual(2, persistedOwners.Count, "both discovery paths must persist refreshed auth");
            AssertTrue(responseOwners.All(owner => ReferenceEquals(owner, publisherOwner)), "discovery must forward the existing publisher owner");
            AssertTrue(persistedOwners.All(owner => ReferenceEquals(owner, publisherOwner)), "auth persistence must retain the existing publisher owner");
        }
        finally
        {
            RpolAuthUtility.SnapshotResponseOverrideForTests = null;
            RpolAuthUtility.PersistVerifiedStorageStateJsonOverrideForTests = null;
        }
    }

    internal static void RpolPublisherEntryPointsShareOneCrossProcessLock()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = RpolSnapshotUtility.ExecuteWithPublisherLockAsync(
            async _ =>
            {
                entered.SetResult(true);
                await release.Task;
            },
            CancellationToken.None);
        AssertTrue(entered.Task.Wait(TimeSpan.FromSeconds(2)), "the startup/direct publisher lock path must enter its protected operation");

        var secondStarted = false;
        var second = RpolSnapshotUtility.ExecuteWithPublisherLockAsync(
            async _ =>
            {
                secondStarted = true;
                await Task.CompletedTask;
            },
            CancellationToken.None);
        Thread.Sleep(100);
        AssertFalse(secondStarted, "a scheduled publisher must wait while a startup publisher owns the shared lock");
        release.SetResult(true);
        AssertTrue(Task.WhenAll(first, second).Wait(TimeSpan.FromSeconds(3)), "both publisher entry paths must complete after ownership is released");
        AssertTrue(secondStarted, "the waiting publisher must eventually acquire the shared lock");
    }

    internal static void RpolResultRejectsStaleOrMismatchedRun()
    {
        var record = RpolPublishResultRecord.Create("run-a", DateTimeOffset.Parse("2026-08-22T10:00:00Z")) with
        {
            EndedAt = "2026-08-22T10:00:01Z",
            TerminalStatus = "success",
            TerminalStage = "published",
            Discovered = 2,
            Attempted = 1,
            Published = 1,
            Failed = 0,
            TargetOutcomes = [new RpolTargetOutcome("target-a", "published", null)],
            UploadCompleted = true,
            CursorPersisted = true
        };
        AssertTrue(RpolPublishResultValidator.Validate(record, "run-a", DateTimeOffset.Parse("2026-08-22T09:59:59Z"), out _), "a fresh matching result should validate");
        AssertFalse(RpolPublishResultValidator.Validate(record, "run-b", DateTimeOffset.Parse("2026-08-22T09:59:59Z"), out _), "a mismatched run ID must be rejected");
        AssertFalse(RpolPublishResultValidator.Validate(record, "run-a", DateTimeOffset.Parse("2026-08-22T10:00:02Z"), out _), "a stale result must be rejected");
    }

    internal static void RpolResultRejectsUnknownDiscoveryAndCountMismatch()
    {
        var record = RpolPublishResultRecord.Create("run-a", DateTimeOffset.UtcNow) with
        {
            EndedAt = DateTimeOffset.UtcNow.ToString("O"),
            TerminalStatus = "success",
            TerminalStage = "published",
            Discovered = -1,
            Attempted = 1,
            Published = 1,
            Failed = 0,
            TargetOutcomes = [new RpolTargetOutcome("target-a", "published", null)],
            UploadCompleted = true,
            CursorPersisted = true
        };
        AssertFalse(RpolPublishResultValidator.Validate(record, "run-a", DateTimeOffset.UtcNow.AddMinutes(-1), out _), "unknown discovery must not validate as a successful result");
        AssertFalse(
            RpolPublishResultValidator.Validate(
                record with { Discovered = 2, Published = 0, Failed = 0 },
                "run-a",
                DateTimeOffset.UtcNow.AddMinutes(-1),
                out _),
            "published plus failed must equal attempted");
    }

    internal static void RpolResultAcceptsWrapperTimeoutFallback()
    {
        var started = DateTimeOffset.UtcNow.AddSeconds(-1);
        var record = RpolPublishResultRecord.Create("run-timeout", started) with
        {
            EndedAt = DateTimeOffset.UtcNow.ToString("O"),
            TerminalStatus = "timeout",
            TerminalStage = "wrapper-timeout",
            TimeoutCategory = "wrapper-deadline",
            Discovered = -1,
            Attempted = 0,
            Published = 0,
            Failed = 1,
            TargetOutcomes = []
        };
        AssertTrue(
            RpolPublishResultValidator.Validate(record, "run-timeout", started.AddMilliseconds(-1), out _),
            "a wrapper timeout fallback must validate without claiming discovery or an attempted target");
    }

    internal static void RpolResultPathRejectsTraversalAndUnrelatedRuntimeOverwrite()
    {
        var runId = Guid.NewGuid().ToString("N");
        var expected = RuntimePathUtility.GetWritableRuntimePath("rpol-results", runId, "result.json");
        AssertEqual(
            Path.GetFullPath(expected),
            PlayerAssistant.Program.GetRpolResultPathForTests([], runId),
            "the default result path must be exactly below the executable results directory for this run");
        AssertThrows<InvalidOperationException>(
            () => PlayerAssistant.Program.GetRpolResultPathForTests(["--rpol-result-path", Path.Combine(AppContext.BaseDirectory, "runtime.json")], runId));
        AssertThrows<InvalidOperationException>(
            () => PlayerAssistant.Program.GetRpolResultPathForTests(["--rpol-result-path", Path.Combine(AppContext.BaseDirectory, "rpol-results", Guid.NewGuid().ToString("N"), "result.json")], runId));
        AssertThrows<InvalidOperationException>(
            () => PlayerAssistant.Program.GetRpolResultPathForTests(["--rpol-result-path", Path.Combine(AppContext.BaseDirectory, "rpol-results", runId, "..", "other.json")], runId));
    }

    internal static void RpolPublisherWrapperPreservesReadableTerminalEvidenceOnContractMismatch()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "publish-rpol-snapshots.ps1"));
        AssertTrue(
            source.Contains("Test-ResultEvidenceIdentity", StringComparison.Ordinal),
            "publisher wrapper must identify readable terminal child evidence before writing a fallback");
        AssertTrue(
            source.Contains("$resultWasRead -and (Test-ResultEvidenceIdentity", StringComparison.Ordinal),
            "publisher wrapper must not replace readable terminal child evidence with a misleading fallback");
    }

    internal static void RpolPublisherWrapperContainsAtomicSupervisionContract()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "publish-rpol-snapshots.ps1"));
        foreach (var required in new[] { "schema_version", "run_id", "started_at", "ended_at", "target_outcomes", "Write-AtomicJsonResult", "Kill($true)", "WaitForExit" })
        {
            AssertTrue(source.Contains(required, StringComparison.Ordinal), $"publisher wrapper must contain '{required}'");
        }
        AssertFalse(source.Contains("Start-Process", StringComparison.Ordinal) && source.Contains("-Wait", StringComparison.Ordinal), "publisher wrapper must not delegate deadline control to an unbounded synchronous wait");
    }

    internal static void RpolPublisherWrapperAllowsBoundedClockSkew()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "publish-rpol-snapshots.ps1"));
        AssertTrue(
            source.Contains("$StartedAt.AddSeconds(-5)", StringComparison.Ordinal),
            "publisher wrapper should tolerate a bounded five-second wall-clock adjustment because the exact per-run GUID already prevents stale-result reuse");
        AssertTrue(
            source.Contains("$resultEndedAt -lt $resultStartedAt", StringComparison.Ordinal),
            "publisher wrapper must still reject results whose end precedes their start");
    }

    internal static void RpolProtectedProbeDoesNotRequireNetworkIdle()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "RpolAuthUtility.cs"));
        var methodStart = source.IndexOf("private static async Task<RpolProtectedResourceClassification> VerifyAuthenticatedContextAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static RpolAuthException CreateProtectedProbeException", methodStart, StringComparison.Ordinal);
        AssertTrue(methodStart >= 0 && methodEnd > methodStart, "protected authentication probe method should be present");
        var methodSource = source[methodStart..methodEnd];
        AssertFalse(
            methodSource.Contains("LoadState.NetworkIdle", StringComparison.Ordinal),
            "protected authentication must not require NetworkIdle because RPOL pages can retain background network activity");
        AssertTrue(
            methodSource.Contains("RpolNavigationStability.WaitForStableAsync", StringComparison.Ordinal),
            "protected authentication must retain its bounded URL, document-identity, and final-DOM stability proof");
    }

    internal static void RpolProtectedProbeUsesJsonDomBoundary()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "RpolAuthUtility.cs"));
        AssertFalse(
            source.Contains("EvaluateAsync<RpolNavigationDomSnapshot>", StringComparison.Ordinal),
            "Playwright must not directly map an evaluated JavaScript object to the internal DOM snapshot record");
        AssertTrue(
            source.Contains("EvaluateAsync<string>", StringComparison.Ordinal)
                && source.Contains("JsonSerializer.Deserialize<RpolNavigationDomSnapshot>", StringComparison.Ordinal),
            "Playwright DOM evidence should cross the runtime boundary as JSON and then be deserialized explicitly");
    }

    internal static void RpolProtectedProbeRetriesTransientNavigationObservation()
    {
        AssertTrue(
            RpolAuthUtility.IsTransientProtectedProbeObservationFailure(
                "Unable to retrieve content because the page is navigating and changing the content."),
            "the live Playwright navigation race should be treated as a transient protected-probe observation");
        AssertTrue(
            RpolAuthUtility.IsTransientProtectedProbeObservationFailure(
                "Execution context was destroyed, most likely because of a navigation."),
            "a destroyed execution context during navigation should be treated as transient");
        AssertFalse(
            RpolAuthUtility.IsTransientProtectedProbeObservationFailure(
                "Target page, context or browser has been closed"),
            "a closed browser must remain a terminal failure rather than being retried as navigation");
    }

    internal static void RpolLoginFormJsonPreservesFrameFlag()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "RpolAuthUtility.cs"));
        AssertTrue(
            source.Contains("JSON.stringify({ Action: form.action, Method: form.method, Target: form.target, SameFrame: window.top === window.self })", StringComparison.Ordinal),
            "login-form JSON must use the C# contract property names so a true same-frame observation cannot deserialize as false");
    }

    internal static void RpolProcessSupervisorKillsTimedOutProcessTree()
    {
        using var directory = TemporaryDirectory.Create();
        var pidPath = Path.Combine(directory.Path, "child.pid");
        var startInfo = CreateTestChildProcessStartInfo(["--cancellation-child", pidPath]);
        var result = RpolProcessSupervisor.RunAsync(startInfo, TimeSpan.FromMilliseconds(500), CancellationToken.None).GetAwaiter().GetResult();
        AssertTrue(result.TimedOut, "the process supervisor should report a timeout");
        AssertTrue(result.ProcessTreeTerminated, "the process supervisor should terminate and wait for the process tree");
    }

    internal static void RpolPublisherValidateOnlyWrapperUsesValidResultsRoot()
    {
        var scriptPath = Path.Combine(GetRepositoryRoot(), "publish-rpol-snapshots.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath, "-ValidateOnly" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("PowerShell could not start the RPOL validation wrapper.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        AssertEqual(0, process.ExitCode, $"the harmless RPOL wrapper validation path must succeed: {error}");
        using var document = JsonDocument.Parse(output);
        var resultsRoot = document.RootElement.GetProperty("results_root").GetString();
        var expectedRoot = Path.Combine(GetRepositoryRoot(), "Release", "rpol-results");
        AssertEqual(Path.GetFullPath(expectedRoot), Path.GetFullPath(resultsRoot ?? string.Empty), "the wrapper must report the current valid resultsRoot path");
        AssertTrue(Directory.Exists(expectedRoot), "the wrapper validation path must create the valid resultsRoot directory");
    }

    internal static void RpolSecureStorageStateCleanupRunsOnFailure()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "state.json");
        AssertThrows<InvalidOperationException>(() =>
            RpolSecureStorageStateFile.WriteAndRun(
                statePath,
                "synthetic-state",
                _ => throw new InvalidOperationException("synthetic failure")));
        AssertFalse(File.Exists(statePath), "temporary storage state must be removed on every exit path");
    }

    internal static void RpolCleanupContinuesAfterFailure()
    {
        var completed = new List<string>();
        var errors = RpolCleanupUtility.DisposeIndependently(
            ("first", () =>
            {
                completed.Add("first");
                throw new IOException("synthetic cleanup failure");
            }
        ),
            ("second", () => completed.Add("second")));

        AssertEqual(1, errors.Count, "cleanup must report the failed resource");
        AssertTrue(completed.SequenceEqual(["first", "second"]), "cleanup must continue disposing later resources after a failure");
    }

    internal static void RpolOperationDeadlineCarriesCleanupMargin()
    {
        using var deadline = RpolOperationDeadline.Create(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(200));
        Thread.Sleep(90);
        AssertTrue(deadline.OperationToken.IsCancellationRequested, "the operation deadline must expire first");
        AssertFalse(deadline.CleanupToken.IsCancellationRequested, "cleanup must retain the shared deadline margin");
        Thread.Sleep(180);
        AssertTrue(deadline.CleanupToken.IsCancellationRequested, "cleanup must eventually expire at the run boundary");
    }

    internal static void RpolWebViewProfileLeaseIsExclusiveAndResets()
    {
        using var directory = TemporaryDirectory.Create();
        var profilePath = Path.Combine(directory.Path, "profile");
        using var first = RpolWebViewProfileLease.Acquire(profilePath);
        AssertThrows<IOException>(() => RpolWebViewProfileLease.Acquire(profilePath));
        first.Dispose();
        AssertFalse(Directory.Exists(profilePath), "a completed WebView verification must reset its authenticated profile");
    }

    internal static void RpolWebViewProfileCleanupCanRetryAfterCancelledToken()
    {
        using var directory = TemporaryDirectory.Create();
        var profilePath = Path.Combine(directory.Path, "profile");
        using var lease = RpolWebViewProfileLease.Acquire(profilePath);
        File.WriteAllText(Path.Combine(profilePath, "leftover.txt"), "synthetic");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        AssertThrows<AggregateException>(() => lease.Dispose(cancelled.Token));
        AssertTrue(Directory.Exists(profilePath), "failed cleanup must leave the profile available for a retry");
        lease.Dispose(CancellationToken.None);
        AssertFalse(Directory.Exists(profilePath), "a later independent cleanup token must be able to finish profile cleanup");
    }

    internal static void RpolWebViewLifetimeHonorsMaxWaitAndDisposal()
    {
        using var lifetime = RpolWebViewLifetime.Create(TimeSpan.FromMilliseconds(50), CancellationToken.None);
        Thread.Sleep(100);
        AssertFalse(lifetime.IsAlive, "WebView work must be cancelled at the requested maximum wait");
        AssertThrows<OperationCanceledException>(lifetime.ThrowIfNotAlive);
    }

    internal static void RpolWebViewCredentialScriptRevalidatesBeforeSubmit()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "RpolWebViewVerificationDialog.cs"));
        var authSource = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "RpolAuthUtility.cs"));
        AssertTrue(source.Contains("RpolCredentialSubmissionScript.Source", StringComparison.Ordinal), "WebView must execute the shared atomic credential script");
        foreach (var required in new[] { "window.top === window.self", "form.method", "form.action", "location.href", "dispatchEvent(new Event('change'", "HTMLFormElement.prototype.submit.call" })
        {
            AssertTrue(RpolCredentialSubmissionScript.Source.Contains(required, StringComparison.Ordinal), $"the shared credential script must contain '{required}'");
        }
        foreach (var required in new[] { "WebResourceRequested", "NewWindowRequested", "TryValidateCredentialRequest", "CreateWebResourceResponse" })
        {
            AssertTrue(source.Contains(required, StringComparison.Ordinal), $"the WebView credential guard must contain '{required}'");
        }
        AssertTrue(authSource.Contains("RouteAsync", StringComparison.Ordinal), "Playwright credential submission must install a request route guard");
        AssertTrue(authSource.Contains("AbortAsync", StringComparison.Ordinal), "the Playwright route guard must fail closed by aborting invalid transmission");
        AssertFalse(
            RpolCredentialSubmissionScript.Source.Contains("setTimeout", StringComparison.Ordinal),
            "credential submission must not schedule a native submit after the transmission guard is torn down");
    }

    internal static void RpolExternalProfileCleanupUsesIndependentDeadlineOwnership()
    {
        using var directory = TemporaryDirectory.Create();
        var profile = Path.Combine(directory.Path, RpolExternalProfileCleanup.ProfilePrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "leftover.txt"), "profile");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        RpolExternalProfileCleanup.CleanupProfile(profile, cancelled.Token);
        AssertFalse(Directory.Exists(profile), "profile deletion must use independent cleanup ownership instead of the canceled operation token");
    }

    internal static void RpolCleanupProtectsTargetsOfReparseFixtures()
    {
        using var directory = TemporaryDirectory.Create();
        var target = Path.Combine(directory.Path, "outside-target");
        Directory.CreateDirectory(target);
        var targetFile = Path.Combine(target, "keep.txt");
        File.WriteAllText(targetFile, "must remain");
        var rootLink = Path.Combine(directory.Path, RpolExternalProfileCleanup.ProfilePrefix + "root-link");
        CreateDirectoryReparseLink(rootLink, target);
        AssertThrows<IOException>(() => RpolCleanupUtility.DeleteDirectoryBounded(rootLink, TimeSpan.FromSeconds(1), CancellationToken.None));
        AssertTrue(File.Exists(targetFile), "reparse-point profile roots must not traverse their targets");
        Directory.Delete(rootLink);
        var profile = Path.Combine(directory.Path, RpolExternalProfileCleanup.ProfilePrefix + "nested");
        Directory.CreateDirectory(profile);
        CreateDirectoryReparseLink(Path.Combine(profile, "nested-link"), target);
        CreateFileLink(Path.Combine(profile, "file-link"), targetFile);
        RpolCleanupUtility.DeleteDirectoryBounded(profile, TimeSpan.FromSeconds(1), CancellationToken.None);
        AssertTrue(File.Exists(targetFile), "nested reparse links must be deleted without traversing their targets");
        var stale = Path.Combine(directory.Path, RpolExternalProfileCleanup.ProfilePrefix + "ordinary");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "stale.txt"), "remove");
        RpolCleanupUtility.DeleteDirectoryBounded(stale, TimeSpan.FromSeconds(1), CancellationToken.None);
        AssertFalse(Directory.Exists(stale), "ordinary stale profiles must still be removed");
    }

    private static void CreateDirectoryReparseLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        RunMklink("/J", linkPath, targetPath);
    }

    private static void CreateFileLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return;
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        RunMklink("/H", linkPath, targetPath);
    }

    private static void RunMklink(string linkType, string linkPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add(linkType);
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Windows reparse fixture process could not be started.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The Windows reparse fixture could not create a {linkType} link.");
        }
    }

    internal static void RpolExternalProfileScavengerSkipsActiveLockedProfiles()
    {
        using var directory = TemporaryDirectory.Create();
        var stale = Path.Combine(directory.Path, RpolExternalProfileCleanup.ProfilePrefix + "stale");
        var active = Path.Combine(directory.Path, RpolExternalProfileCleanup.ProfilePrefix + "active");
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(active);
        File.WriteAllText(Path.Combine(stale, "stale.txt"), "stale");
        var activeLockPath = Path.Combine(active, ".rpol-profile.lock");
        using var activeLock = new FileStream(activeLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var old = DateTime.UtcNow.AddHours(-2);
        Directory.SetLastWriteTimeUtc(stale, old);
        Directory.SetLastWriteTimeUtc(active, old);
        var errors = RpolExternalProfileCleanup.ScavengeStaleProfiles(directory.Path, DateTimeOffset.UtcNow.AddHours(-1));
        AssertEqual(0, errors.Count, "active profile locks must be skipped without producing cleanup errors");
        AssertFalse(Directory.Exists(stale), "an unlocked stale verification profile must be scavenged");
        AssertTrue(Directory.Exists(active), "an active locked verification profile must never be scavenged");
    }

    internal static void RpolUploadRecoveryFaultsRemainTruthful()
    {
        foreach (var faultStage in new[] { "intent-write", "upload", "uploaded-stage-write", "cursor-write", "recovery-cleanup" })
        {
            var events = new List<string>();
            var uploadCalls = 0;
            var result = RpolUploadRecoveryTransaction.ExecuteAsync(
                () => FaultedStep("intent-write"),
                () =>
                {
                    events.Add("upload");
                    uploadCalls++;
                    return FaultedStep("upload");
                },
                () => FaultedStep("uploaded-stage-write"),
                () => FaultedStep("cursor-write"),
                () => FaultedStep("recovery-cleanup"),
                CancellationToken.None).GetAwaiter().GetResult();

            AssertFalse(result.Succeeded, $"fault '{faultStage}' must not report success");
            AssertEqual(faultStage, result.RecoveryStage, $"fault '{faultStage}' must identify its durable recovery stage");
            AssertEqual(
                faultStage is "uploaded-stage-write" or "cursor-write" or "recovery-cleanup",
                result.UploadCompleted,
                $"fault '{faultStage}' must preserve confirmed upload truthfully");
            AssertEqual(faultStage is "recovery-cleanup", result.CursorPersisted, $"fault '{faultStage}' cursor truth must be preserved");
            AssertEqual(faultStage is "intent-write" ? 0 : 1, uploadCalls, $"fault '{faultStage}' must not republish ambiguously");
            AssertEqual(faultStage != "intent-write", events.Contains("upload"), $"fault '{faultStage}' must expose the upload boundary only when reached");

            async Task FaultedStep(string stage)
            {
                events.Add(stage);
                if (stage == faultStage)
                {
                    throw new IOException("synthetic recovery fault");
                }

                await Task.CompletedTask;
            }
        }
    }

    internal static void RpolPublishRecoveryReconcilesBeforeUpload()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "RpolSnapshotUtility.cs"));
        AssertTrue(source.Contains("return await RecoverPendingUploadAsync", StringComparison.Ordinal), "production PublishAsync must execute the tested recovery branch");
        var uploadCalls = 0;
        var sourceUrl = "https://rpol.net/game.php?gi=80170";
        var payload = new RpolSnapshotPayload(1, "80170", sourceUrl, "2026-08-22T00:00:00Z", "text/html", "hash", Convert.ToBase64String("html"u8.ToArray()), "HMAC-SHA256", "signature");
        var recovery = new RpolSnapshotCursorRecovery(
            1,
            sourceUrl,
            payload.ContentSha256,
            new RpolSnapshotPublisherState(1, [sourceUrl], 0),
            "2026-08-22T00:00:00Z",
            payload,
            "intent");

        var report = RpolSnapshotUtility.RecoverPendingUploadAsync(
            recovery,
            "synthetic-admin-key",
            "synthetic-state-path",
            "synthetic-recovery-path",
            CancellationToken.None,
            (_, _) => Task.FromResult(true),
            (_, _, _) =>
            {
                uploadCalls++;
                return Task.CompletedTask;
            },
            (_, _, _) => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask,
            _ => { }).GetAwaiter().GetResult();
        AssertTrue(report.UploadCompleted && report.CursorPersisted, "the production recovery branch must report recovered upload and cursor truth");
        AssertEqual(0, uploadCalls, "a matching readback must suppress an ambiguous reupload");

        RpolSnapshotUtility.RecoverPendingUploadAsync(
            recovery,
            "synthetic-admin-key",
            "synthetic-state-path",
            "synthetic-recovery-path",
            CancellationToken.None,
            (_, _) => Task.FromResult(false),
            (_, _, _) =>
            {
                uploadCalls++;
                return Task.CompletedTask;
            },
            (_, _, _) => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask,
            _ => { }).GetAwaiter().GetResult();
        AssertEqual(1, uploadCalls, "a missing or mismatched readback must perform exactly one owned reupload");
    }

    internal static void RpolSnapshotPublisherReportRequiresOneAttempt()
    {
        AssertTrue(
            RpolSnapshotUtility.IsSuccessfulPublishReport(new RpolSnapshotPublishReport(16, 1, 0, [], UploadCompleted: true, CursorPersisted: true)),
            "a one-target publisher invocation succeeds only when its target was published");
        AssertFalse(
            RpolSnapshotUtility.IsSuccessfulPublishReport(new RpolSnapshotPublishReport(16, 0, 0, [])),
            "an invocation with no published target must not be reported as success");
        AssertFalse(
            RpolSnapshotUtility.IsSuccessfulPublishReport(new RpolSnapshotPublishReport(16, 1, 1, ["failure"])),
            "publisher output with errors must not be reported as success");
        AssertFalse(
            RpolSnapshotUtility.IsSuccessfulPublishReport(new RpolSnapshotPublishReport(0, 0, 0, [])),
            "unknown discovery must not be reported as successful publishing");
    }

    internal static void RpolProtectedProbeRejectsPublicCampaignContent()
    {
        var protectedUri = RpolAuthUtility.ProtectedDiceRollerUri;
        var publicCampaignHtml = "<html><title>Scarlet Horizons</title><body>"
            + "<div>Campaign content and Dice Roller</div>"
            + "</body></html>";

        var result = RpolAuthUtility.ClassifyProtectedResource(
            protectedUri,
            protectedUri,
            200,
            "text/html; charset=utf-8",
            publicCampaignHtml);

        AssertEqual(
            RpolProtectedResourceKind.UnexpectedContent,
            result.Kind,
            "public campaign-shaped content must not prove protected Dice Roller access");
    }

    internal static void RpolProtectedProbeRejectsLoginRedirect()
    {
        var requestedUri = RpolAuthUtility.ProtectedDiceRollerUri;
        var finalUri = new Uri("https://rpol.net/login.cgi?gi=80170");

        var result = RpolAuthUtility.ClassifyProtectedResource(
            requestedUri,
            finalUri,
            200,
            "text/html; charset=utf-8",
            "<html><body>login</body></html>");

        AssertEqual(
            RpolProtectedResourceKind.LoginRequired,
            result.Kind,
            "a protected probe redirected to login must be rejected");
    }

    internal static void RpolProtectedProbeRejectsUntrustedUris()
    {
        var protectedUri = RpolAuthUtility.ProtectedDiceRollerUri;
        var invalidUris = new[]
        {
            new Uri("http://rpol.net/usermodules/diceroller.cgi?gi=80170"),
            new Uri("https://www.rpol.net/usermodules/diceroller.cgi?gi=80170"),
            new Uri("https://user:password@rpol.net/usermodules/diceroller.cgi?gi=80170"),
            new Uri("https://rpol.net/usermodules/diceroller.cgi?gi=80171"),
            new Uri("https://rpol.net/usermodules/diceroller.cgi?gi=80170&gi=80170"),
            new Uri("https://rpol.net/usermodules/diceroller.cgi?gi=80170#fragment"),
            new Uri("https://rpol.net/display.cgi?gi=80170")
        };

        foreach (var invalidUri in invalidUris)
        {
            var result = RpolAuthUtility.ClassifyProtectedResource(
                invalidUri,
                protectedUri,
                200,
                "text/html; charset=utf-8",
                "<html><title>Die Roller</title><body>roll</body></html>");

            AssertEqual(
                RpolProtectedResourceKind.UntrustedNavigation,
                result.Kind,
                $"protected probe must reject '{invalidUri}'");
        }
    }

    internal static void RpolProtectedProbeClassifiesCloudflareChallenge()
    {
        var protectedUri = RpolAuthUtility.ProtectedDiceRollerUri;
        var result = RpolAuthUtility.ClassifyProtectedResource(
            protectedUri,
            protectedUri,
            403,
            "text/html; charset=utf-8",
            "<html><title>Just a moment...</title><body>Verify you are human</body></html>");

        AssertEqual(
            RpolProtectedResourceKind.CloudflareChallenge,
            result.Kind,
            "Cloudflare challenge must remain distinct from login rejection");

        var challengeWithSuccessfulTransport = RpolAuthUtility.ClassifyProtectedResource(
            protectedUri,
            protectedUri,
            200,
            "text/html; charset=utf-8",
            "<html><title>Just a moment...</title><body>cf-challenge</body></html>");
        AssertEqual(
            RpolProtectedResourceKind.CloudflareChallenge,
            challengeWithSuccessfulTransport.Kind,
            "challenge content must not be accepted merely because transport returned 200");
    }

    internal static void RpolProtectedProbeHandlesDynamicLoginFixture()
    {
        var protectedUri = RpolAuthUtility.ProtectedDiceRollerUri;
        var challengeHtml = "<html><body><form id='unrelated'></form>"
            + "<form id='login'><input name='username'><input name='password'></form>"
            + "<div class='cf-challenge'>Verify you are human</div></body></html>";
        var challenge = RpolAuthUtility.ClassifyProtectedResource(
            protectedUri, protectedUri, 200, "text/html; charset=utf-8", challengeHtml);
        AssertEqual(
            RpolProtectedResourceKind.CloudflareChallenge,
            challenge.Kind,
            "a challenge page with multiple dynamically replaced forms must remain a challenge");

        var authenticatedHtml = "<html><head><title>Dice Roller - World of Issenda - Scarlet Horizons - RPoL</title></head>"
            + "<body><form id='unrelated'></form>"
            + "<form id='login'><input name='username'><input name='password'></form>"
            + "<div>Step 1: Choose the Dice</div><div>Roll the Dice</div>"
            + "<div>Only Players May Roll Dice - Example Screen Only</div></body></html>";
        var authenticated = RpolAuthUtility.ClassifyProtectedResource(
            protectedUri, protectedUri, 200, "text/html; charset=utf-8", authenticatedHtml);
        AssertEqual(
            RpolProtectedResourceKind.LoginRequired,
            authenticated.Kind,
            "the protected probe must reject authenticated-looking content that retains a login form");
    }

    internal static void RpolProtectedProbeClassifiesMissingResponse()
    {
        var protectedUri = RpolAuthUtility.ProtectedDiceRollerUri;
        var result = RpolAuthUtility.ClassifyProtectedResource(
            protectedUri,
            null,
            null,
            null,
            null);

        AssertEqual(
            RpolProtectedResourceKind.RemoteFailure,
            result.Kind,
            "a missing protected-probe response must be a bounded remote failure");
    }

    internal static void RpolProtectedProbeAcceptsExactDiceRollerContract()
    {
        var protectedUri = RpolAuthUtility.ProtectedDiceRollerUri;
        var protectedHtml = "<html><head><title>Dice Roller - World of Issenda - Scarlet Horizons - RPoL</title></head>"
            + "<body><div>Step 1: Choose the Dice</div><div>Roll the Dice</div>"
            + "<div>Kelpie rolled 1d20 using d20. [roll=1.2.3]</div></body></html>";

        var result = RpolAuthUtility.ClassifyProtectedResource(
            protectedUri,
            protectedUri,
            200,
            "text/html; charset=utf-8",
            protectedHtml);

        AssertEqual(
            RpolProtectedResourceKind.AuthenticatedProtectedContent,
            result.Kind,
            "the exact protected Dice Roller contract should prove authentication");
    }

    internal static void RpolProtectedProbeDoesNotTreatCookieShapeAsAuthentication()
    {
        AssertFalse(
            RpolAuthUtility.IsStorageStateSemanticProof(
                "{\"cookies\":[{\"name\":\"rpol_session\",\"value\":\"opaque\",\"domain\":\".rpol.net\",\"path\":\"/\"}],\"origins\":[]}"),
            "an rpol.net cookie-shaped state is not semantic authentication proof");
    }

    internal static void RpolProtectedProbeRejectsDiceRollerLoginControls()
    {
        var protectedUri = RpolAuthUtility.ProtectedDiceRollerUri;
        var loginHtml = "<html><title>Dice Roller - Scarlet Horizons</title><body>"
            + "<div>Dice Roller campaign results</div>"
            + "<form action='/login.cgi'><input name='username'><input name='password'></form>"
            + "</body></html>";

        var result = RpolAuthUtility.ClassifyProtectedResource(
            protectedUri,
            protectedUri,
            200,
            "text/html; charset=utf-8",
            loginHtml);

        AssertEqual(
            RpolProtectedResourceKind.LoginRequired,
            result.Kind,
            "Dice Roller-looking content with login controls must be classified as login-required");
    }

    internal static void RpolLoginClassifierHandlesHtmlAttributeVariants()
    {
        var variants = new[]
        {
            "<FORM ACTION=\"https://rpol.net/login.cgi\"><INPUT NAME=\"username\"><INPUT NAME=\"password\"></FORM>",
            "<form action='/login.cgi' method='post'><input name='username'><input name='password' type='password'></form>",
            "<form action=https://rpol.net/login.cgi><input name=username><input name=password></form>"
        };

        foreach (var html in variants)
        {
            AssertTrue(
                RpolAuthUtility.LooksLikeLoginPage(html),
                "every structurally equivalent RPOL login form must be classified as login-required");
        }
    }

    internal static void RpolProtectedProbeRequiresUniqueDiceRollerStructure()
    {
        var requested = RpolAuthUtility.ProtectedDiceRollerUri;
        var spoofedHtml = "<html><title>RPoL: Die Roller</title><body>"
            + "<h1>Die Roller</h1><p>Kelpie rolled a d20.</p></body></html>";

        var result = RpolAuthUtility.ClassifyProtectedResource(
            requested,
            requested,
            200,
            "text/html; charset=utf-8",
            spoofedHtml);

        AssertEqual(
            RpolProtectedResourceKind.UnexpectedContent,
            result.Kind,
            "generic title, heading, and roll text must not prove the unique Dice Roller contract");
    }

    internal static void RpolProtectedProbeRequiresObservedRefererAndSettlement()
    {
        var evidence = new RpolProtectedProbeEvidence(
            RpolAuthUtility.ProtectedDiceRollerUri,
            RpolAuthUtility.ProtectedDiceRollerUri,
            RpolAuthUtility.ProtectedDiceRollerUri,
            200,
            "text/html; charset=utf-8",
            "<html><title>RPoL: Die Roller</title><body><h1>Die Roller</h1><pre>[roll=1] Kelpie rolled a d20.</pre></body></html>",
            null,
            true);

        var result = RpolProtectedResourceUtility.ClassifyEvidence(evidence);

        AssertEqual(
            RpolProtectedResourceKind.UntrustedNavigation,
            result.Kind,
            "protected content without an observed main-frame Referer must fail closed");
        AssertEqual(
            RpolProtectedResourceKind.RemoteFailure,
            RpolProtectedResourceUtility.ClassifyEvidence(evidence with
            {
                MainFrameReferer = AppSettingsUtility.GameForumUrl,
                SettledAfterStabilization = false
            }).Kind,
            "protected content without a stabilization boundary must fail closed");
    }

    internal static void RpolCrossProcessLockIsReentrantAcrossAwaitAndThreads()
    {
        var lockName = $"PlayerAssistant.Tests.Rpol.Reentrant.{Guid.NewGuid():N}";
        using var outer = RpolCrossProcessLock.Acquire(lockName, TimeSpan.FromSeconds(1));
        var nested = Task.Run(async () =>
        {
            await Task.Yield();
            using var inner = outer.AcquireReentrant(TimeSpan.FromMilliseconds(250));
            await Task.Delay(10);
            return true;
        });

        AssertTrue(nested.Wait(TimeSpan.FromSeconds(2)), "explicit nested ownership must not deadlock across an await");
        AssertTrue(nested.Result, "explicit nested ownership must succeed");

        using var contender = RpolCrossProcessLock.TryAcquire(lockName);
        AssertTrue(contender is null, "the outer lease must retain ownership after a nested Task.Run lease is released");
    }

    internal static void RpolCrossProcessLockAsyncAcquireReleaseIsStandaloneAndConcurrent()
    {
        var lockName = $"PlayerAssistant.Tests.Rpol.Async.{Guid.NewGuid():N}";
        using var first = RpolCrossProcessLock.Acquire(lockName, TimeSpan.FromSeconds(1));
        var blocked = Task.Run(async () =>
        {
            using var second = await RpolCrossProcessLock.AcquireAsync(lockName, TimeSpan.FromSeconds(2));
            return true;
        });
        Thread.Sleep(100);
        AssertFalse(blocked.IsCompleted, "a concurrent process-style acquire must remain blocked while the first lease is held");
        first.Dispose();
        AssertTrue(blocked.Wait(TimeSpan.FromSeconds(3)), "the async acquire must complete after release");
        AssertTrue(blocked.Result, "the async lease must be usable and releasable on its worker thread");

        using var final = RpolCrossProcessLock.TryAcquire(lockName);
        AssertTrue(final is not null, "released async ownership must not leak into a subsequent acquire");
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

    internal static void RpolAuthRetriesInitialLoginRejectionWithHeadedBrowser()
    {
        var loginRejected = new RpolAuthException(RpolAuthFailureKind.LoginRejected, "login rejected");
        var challenge = new RpolAuthException(RpolAuthFailureKind.CloudflareChallenge, "challenge");
        var remoteFailure = new RpolAuthException(RpolAuthFailureKind.RemoteUnavailable, "remote failure");

        AssertTrue(RpolAuthUtility.ShouldRetryWithHeadedBrowser(loginRejected, 0),
            "the first headless login rejection should receive one visible-browser retry");
        AssertFalse(RpolAuthUtility.ShouldRetryWithHeadedBrowser(loginRejected, 1),
            "a second login rejection must remain fatal");
        AssertTrue(RpolAuthUtility.ShouldRetryWithHeadedBrowser(challenge, 0),
            "the first Cloudflare challenge should receive a visible-browser retry");
        AssertFalse(RpolAuthUtility.ShouldRetryWithHeadedBrowser(remoteFailure, 0),
            "ordinary remote failures should not launch the verification browser");
    }

    internal static void RpolAuthAwaitsManualPersistentHeadedLoginPage()
    {
        AssertFalse(RpolAuthUtility.ShouldAwaitManualExternalLogin(0),
            "the headed browser should submit configured credentials once");
        AssertTrue(RpolAuthUtility.ShouldAwaitManualExternalLogin(1),
            "a login page that remains after one guarded submission should wait for the user to correct or complete login manually");
    }

    internal static void RpolAuthClassifiesEmbeddedLoginFormByCampaignContent()
    {
        var authenticatedHtml = "<html><title>RPoL: World of Issenda - Scarlet Horizons</title><body>"
            + "<div class='threadstate'>Authenticated campaign thread list</div>"
            + "<a href='/game.php?gi=80170'>Campaign</a>"
            + new string('x', 1200)
            + "<form action='/login.cgi'><input name='username'><input name='password'></form></body></html>";
        var disguisedLoginHtml = "<html><title>RPoL: World of Issenda - Scarlet Horizons</title><body>"
            + "<meta name=\"player-assistant-snapshot\" content=\"dice-rolls\">"
            + "<a href='/game.php?gi=80170'>Campaign</a>"
            + new string('x', 1200)
            + "<form action='/login.cgi'><input name='username'><input name='password'></form></body></html>";
        var wrongGameHtml = authenticatedHtml.Replace("gi=80170", "gi=801700", StringComparison.Ordinal);

        AssertFalse(RpolAuthUtility.ShouldTreatExternalPageAsLogin(authenticatedHtml),
            "an authenticated campaign page may retain RPOL's embedded login form");
        AssertTrue(RpolAuthUtility.ShouldTreatExternalPageAsLogin(disguisedLoginHtml),
            "campaign-looking title and links without RPOL campaign structure must remain a login page");
        AssertTrue(RpolAuthUtility.ShouldTreatExternalPageAsLogin(wrongGameHtml),
            "a neighboring game ID must not satisfy the Scarlet Horizons identity check");
        AssertEqual("café", RpolAuthUtility.DecodeHtmlBody(
            System.Text.Encoding.Latin1.GetBytes("café"),
            "text/html; charset=iso-8859-1"),
            "RPOL response decoding should honor the declared charset");
        AssertFalse(RpolAuthUtility.ShouldTreatResponseAsLogin(
            "text/html; charset=utf-8",
            System.Text.Encoding.UTF8.GetBytes(authenticatedHtml),
            allowEmbeddedLoginForm: true),
            "snapshot responses should allow the authenticated campaign page's embedded login form");
        AssertTrue(RpolAuthUtility.ShouldTreatResponseAsLogin(
            "text/html; charset=utf-8",
            System.Text.Encoding.UTF8.GetBytes(disguisedLoginHtml),
            allowEmbeddedLoginForm: true),
            "snapshot responses must still reject a full or disguised login response");
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
        var startupLogPath = RuntimePathUtility.GetWritableRuntimePath(StartupLoggingUtility.LogFileName);
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

    internal static void HouseRulesDownloadUsesShowAllThreadUrl()
    {
        using var directory = TemporaryDirectory.Create();
        var requestedUrls = new List<string>();

        var download = GameForumUtility.DownloadHouseRulesHtmlAsync(
            [new Hyperlink(
                "https://rpol.net/display.cgi?gi=80170&ti=3&date=1774814820&msgpage=2",
                "Notice: Welcome & House Rules")],
            directory.Path,
            (url, _) =>
            {
                requestedUrls.Add(url);
                return Task.FromResult("<html><body>all house rules posts</body></html>");
            }).GetAwaiter().GetResult();

        AssertEqual(1, requestedUrls.Count, "house rules downloader should fetch one URL");
        AssertEqual(
            "https://rpol.net/display.cgi?gi=80170&ti=3&msgpage=&show=all",
            requestedUrls[0],
            "house rules downloader should request the canonical complete thread");
        AssertTrue(download?.Downloaded == true, "missing House Rules file should be downloaded");
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
        AssertTrue(
            RpolSnapshotUtility.IsUsableSnapshotHtml(normalizedSnapshot),
            "normalized Dice Roller HTML should pass snapshot publisher validation");
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

    internal static void RegionalMapDownloadsWhenMissing()
    {
        var filePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "Images", "Maps", "northernreaches.png");

        AssertTrue(GameForumUtility.ShouldDownloadRegionalMap(filePath), "missing regional map should be downloaded");
    }

    internal static void RegionalMapUsesHostedMapsUrl()
    {
        AssertEqual(
            "https://bryanmiller.us/scarlethorizons/maps/northernreaches.png",
            GameForumUtility.RegionalMapUrl,
            "regional map downloads should use the canonical hosted maps URL");
    }

    internal static void RegionalMapDownloadsWhenOlderThanOneHour()
    {
        using var directory = TemporaryDirectory.Create();
        var filePath = Path.Combine(directory.Path, "Images", "Maps", "northernreaches.png");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        WriteVisiblePng(filePath);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow - TimeSpan.FromMinutes(61));

        AssertTrue(GameForumUtility.ShouldDownloadRegionalMap(filePath), "regional map older than one hour should be downloaded");
    }

    internal static void RegionalMapSkipsWhenNewerThanOneHour()
    {
        using var directory = TemporaryDirectory.Create();
        var filePath = Path.Combine(directory.Path, "Images", "Maps", "northernreaches.png");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        WriteVisiblePng(filePath);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow - TimeSpan.FromMinutes(59));

        AssertFalse(GameForumUtility.ShouldDownloadRegionalMap(filePath), "regional map newer than one hour should not be downloaded");
    }

    internal static void RegionalMapDownloadsWhenNewerButTransparent()
    {
        using var directory = TemporaryDirectory.Create();
        var filePath = Path.Combine(directory.Path, "Images", "Maps", "northernreaches.png");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        WriteTransparentPng(filePath);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow - TimeSpan.FromMinutes(1));

        AssertTrue(GameForumUtility.ShouldDownloadRegionalMap(filePath), "transparent regional map should be downloaded");
    }

    internal static void AdventureOutlineBuildsFromSavedIcHtml()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);

        File.WriteAllText(
            Path.Combine(icDirectory, "ch-10.html"),
            """
            <html><body>
            <h1>Ch 10 - Later Trouble.</h1>
            <span class="messageauthor">Kelpie Lawfuller</span>
            <div class="messagebody" id="msg2">Kelpie keeps watch.<br>Then moves on.</div>
            </body></html>
            """);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-2.html"),
            """
            <html><body>
            <h1>Ch 2 - Supper With Nuanda.</h1>
            <span class="messageauthor you"><a href="/gm">Dungeon Master</a></span>
            <div class="messagebody" id="msg1">Nuanda offers stew &amp; hard biscuits.</div>
            </body></html>
            """);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-2.bak-20260707.html"),
            """
            <html><body>
            <h1>Ch 2 - Old Backup.</h1>
            <span class="messageauthor">Backup</span>
            <div class="messagebody" id="msg1">This should not appear.</div>
            </body></html>
            """);

        var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
            .GetAwaiter()
            .GetResult();

        AssertContains(outline, "# Adventure Outline");
        AssertContains(outline, "## Ch 2 - Supper With Nuanda");
        AssertContains(outline, "- Dungeon Master introduces Nuanda's supper.");
        AssertContains(outline, "## Ch 10 - Later Trouble");
        AssertContains(outline, "- Kelpie keeps watch as the party moves on.");
        AssertFalse(outline.Contains("Old Backup", StringComparison.Ordinal), "backup chapter files should be ignored");
        AssertTrue(
            outline.IndexOf("## Ch 2", StringComparison.Ordinal) < outline.IndexOf("## Ch 10", StringComparison.Ordinal),
            "chapter files should sort by numeric chapter number");
    }

    internal static void AdventureOutlineParsesRpolLinkedAuthorExports()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);

        File.WriteAllText(
            Path.Combine(icDirectory, "ch-1.html"),
            """
            <html><body>
            <div class="threadheader">
                <h1>Ch 1 - Kirkilston.</h1>
            </div>
            <div class="message">
                <span class="messageauthor you"><a href="/gameinfo.php?action=viewdescription&amp;ci=396686">Dungeon Master</a></span>
                <div class="messagebody" id="msg37">Mapper: Slip?<br>Caller: Kelpie?</div>
            </div>
            <div class="message">
                <span class="messageauthor"><a href="/gameinfo.php?action=viewdescription&amp;ci=396648">Kelpie Lawfuller</a></span>
                <div class="messagebody" id="msg38">
                Kelpie was already prepared.<br>
                <span class="blue">I will take the fore</span>
                </div>
            </div>
            </body></html>
            """);

        var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
            .GetAwaiter()
            .GetResult();

        AssertContains(outline, "## Ch 1 - Kirkilston");
        AssertContains(outline, "- Dungeon Master asks a question that narrows the party's next choice.");
        AssertContains(outline, "- Kelpie takes the lead as the party sets out toward Nuanda.");
        AssertFalse(
            outline.Contains("No in-character posts were found", StringComparison.Ordinal),
            "linked author RPOL exports should produce post summaries");
    }

    internal static void AdventureOutlineSummarizesTableRolesConcisely()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-1.html"),
            """
            <html><body>
            <h1>Ch 1 - Kirkilston.</h1>
            <span class="messageauthor you"><a href="/gm">Dungeon Master</a></span>
            <div class="messagebody" id="msg1">
            • Mapper: Slip?<br>
            • Caller: Kelpie?<br>
            • Quartermaster: Urvan<br>
            • Chronicler: Jelb?<br>
            Whoever is acting as the Caller can let me know where the party heads off to.
            </div>
            </body></html>
            """);

        var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
        File.WriteAllText(
            outlinePath,
            """
            # Adventure Outline

            ## Ch 1 - Kirkilston

            - Dungeon Master: • Mapper: Slip? • Caller: Kelpie? • Quartermaster: Urvan • Chronicler: Jelb? Whoever is acting as the Caller can let me know where the party heads off to, any preparation they make, etc. If you are seeking Nuanda, you can get there in under an hour, and I just need a d6 roll from...
            """);

        var updated = AdventureOutlineUtility.UpdateAdventureOutlineAsync(icDirectory, outlinePath)
            .GetAwaiter()
            .GetResult();
        var outline = File.ReadAllText(outlinePath);

        AssertTrue(updated, "role-assignment outline should replace stale overlong bullets");
        AssertContains(outline, "- Dungeon Master asked players to assume the roles of Caller, Quartermaster, Mapper, and Chronicler.");
        AssertFalse(outline.Contains("Whoever is acting as the Caller", StringComparison.Ordinal), "role-assignment outline should not retain the long excerpt");
    }

    internal static void AdventureOutlineSkipsEmptyBulletMarkerPosts()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-1.html"),
            """
            <html><body>
            <h1>Ch 1 - Kirkilston.</h1>
            <span class="messageauthor">Kelpie Lawfuller</span>
            <div class="messagebody" id="msg1">•<br>-<br></div>
            <span class="messageauthor">Dungeon Master</span>
            <div class="messagebody" id="msg2">The party leaves town.</div>
            </body></html>
            """);

        var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
            .GetAwaiter()
            .GetResult();

        AssertContains(outline, "- Dungeon Master moves the party out of town.");
        AssertFalse(outline.Contains("Kelpie", StringComparison.Ordinal), "empty bullet marker posts should not produce outline bullets");
        AssertFalse(outline.Contains("advances the scene", StringComparison.OrdinalIgnoreCase), "outline summaries should explain how the scene advanced");
    }

    internal static void AdventureOutlineRejectsWeakGeneratedSummaries()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-2.html"),
            """
            <html><body>
            <h1>Ch 2 - Supper With Nuanda.</h1>
            <span class="messageauthor">Jelb Garrick</span>
            <div class="messagebody" id="msg1">Jelb agreed to check on the bread, bringing it out to cool when it was ready. I do have something of hers. Her brooch.</div>
            <span class="messageauthor">Nuanda</span>
            <div class="messagebody" id="msg2">The girl, Jelenneth. She was picking berries when bandits abducted her.</div>
            <span class="messageauthor">Urvan Hall</span>
            <div class="messagebody" id="msg3">That is...remarkable. With this information we could look for her.</div>
            </body></html>
            """);

        var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
        File.WriteAllText(
            outlinePath,
            """
            # Adventure Outline

            ## Ch 2 - Supper With Nuanda

            - Jelb advances the scene.
            - Nuanda contributes a new development to the scene.
            - Urvan presses for answers or a decision.
            - Nuanda reassures Kelpie that Morrow and her own magic protect her.
            """);

        AdventureOutlineUtility.UpdateAdventureOutlineAsync(icDirectory, outlinePath)
            .GetAwaiter()
            .GetResult();
        var outline = File.ReadAllText(outlinePath);

        AssertContains(outline, "- Jelb helps with the bread and offers Jelenneth's brooch as a focus.");
        AssertContains(outline, "- Nuanda recounts Jelenneth's abduction by bandits.");
        AssertContains(outline, "- Urvan recognizes that Nuanda's divination gives the party a lead.");

        foreach (var weakSummary in GetWeakAdventureOutlineSummaryPhrases())
        {
            AssertFalse(
                outline.Contains(weakSummary, StringComparison.OrdinalIgnoreCase),
                $"outline should not contain weak generated summary '{weakSummary}'");
        }
    }

    private static string[] GetWeakAdventureOutlineSummaryPhrases()
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

    internal static void AdventureOutlineFallbackSummariesPreserveSceneSpecifics()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-5.html"),
            """
            <html><body>
            <h1>Ch 5 - A Betentacled Escape.</h1>
            <span class="messageauthor">Maximilian</span>
            <div class="messagebody" id="msg1">Maximilian studies the recovered scroll and says it names Red Tusk and the Deep Friends.</div>
            <span class="messageauthor">Algorn Druff</span>
            <div class="messagebody" id="msg2">Algorn says the Raven's Pass trail should lead them to Nimba at The Mason's Apron.</div>
            <span class="messageauthor">Billworth Turgen</span>
            <div class="messagebody" id="msg3">Billworth checks the caravan wagons, mules, and remaining cargo before they move again.</div>
            </body></html>
            """);

        var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
            .GetAwaiter()
            .GetResult();

        AssertContains(outline, "- Maximilian connects the current threat to Red Tusk, the Deep Friends, or the Toothbreakers.");
        AssertContains(outline, "- Algorn points the party toward Raven's Pass contacts and support.");
        AssertContains(outline, "- Billworth focuses the scene on the caravan, its route, or its cargo.");

        foreach (var weakSummary in GetWeakAdventureOutlineSummaryPhrases())
        {
            AssertFalse(
                outline.Contains(weakSummary, StringComparison.OrdinalIgnoreCase),
                $"fallback outline should not contain weak generated summary '{weakSummary}'");
        }
    }

    internal static void AdventureOutlineMergesNewSavedIcBullets()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-1.html"),
            """
            <html><body>
            <h1>Ch 1 - Kirkilston.</h1>
            <span class="messageauthor">Dungeon Master</span>
            <div class="messagebody" id="msg1">The party leaves town.</div>
            <span class="messageauthor">Kelpie Lawfuller</span>
            <div class="messagebody" id="msg2">Kelpie takes the lead.</div>
            </body></html>
            """);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-2.html"),
            """
            <html><body>
            <h1>Ch 2 - Supper With Nuanda.</h1>
            <span class="messageauthor">Nuanda</span>
            <div class="messagebody" id="msg3">Nuanda shares what she learned.</div>
            </body></html>
            """);

        var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
        File.WriteAllText(
            outlinePath,
            """
            # Adventure Outline

            ## Ch 1 - Kirkilston

            - Dungeon Master: The party leaves town.
            """);

        var updated = AdventureOutlineUtility.UpdateAdventureOutlineAsync(icDirectory, outlinePath)
            .GetAwaiter()
            .GetResult();
        var outline = File.ReadAllText(outlinePath);

        AssertTrue(updated, "existing adventure outline should be updated with missing material");
        AssertContains(outline, "- Dungeon Master moves the party out of town.");
        AssertFalse(outline.Contains("- Dungeon Master: The party leaves town.", StringComparison.Ordinal), "stale author-prefixed excerpts should be replaced");
        AssertContains(outline, "- Kelpie takes the lead.");
        AssertContains(outline, "## Ch 2 - Supper With Nuanda");
        AssertContains(outline, "- Nuanda briefs the party.");
        AssertTrue(
            outline.IndexOf("- Kelpie takes the lead.", StringComparison.Ordinal)
                < outline.IndexOf("## Ch 2 - Supper With Nuanda", StringComparison.Ordinal),
            "missing chapter 1 bullet should remain before chapter 2");
    }

    internal static void AdventureOutlinePrefersSavedIcHtmlOverFallback()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-1.html"),
            """
            <html><body>
            <h1>Ch 1 - Kirkilston.</h1>
            <span class="messageauthor">Dungeon Master</span>
            <div class="messagebody" id="msg1">Local chapter material.</div>
            </body></html>
            """);
        var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
        var fallbackFetchCount = 0;

        AdventureOutlineUtility.UpdateAdventureOutlineAsync(
            icDirectory,
            outlinePath,
            AdventureOutlineUtility.FallbackMarkdownUrl,
            (_, _) =>
            {
                fallbackFetchCount++;
                return Task.FromResult("# Adventure Outline\n\n- Fallback material.");
            }).GetAwaiter().GetResult();

        var outline = File.ReadAllText(outlinePath);
        AssertEqual(0, fallbackFetchCount, "fallback markdown should not be fetched when saved IC HTML builds an outline");
        AssertContains(outline, "- Dungeon Master adds a concrete detail that changes the party's situation.");
        AssertFalse(outline.Contains("Fallback material", StringComparison.Ordinal), "fallback content should not replace local IC outline");
    }

    internal static void AdjustedPostTalliesAggregateSavedIcHtml()
    {
        using var directory = TemporaryDirectory.Create();
        var postsDirectory = Path.Combine(directory.Path, "Posts", "IC");
        var asideDirectory = Path.Combine(postsDirectory, "Aside");
        var outOfCharacterDirectory = Path.Combine(directory.Path, "Posts", "OOC");
        Directory.CreateDirectory(postsDirectory);
        Directory.CreateDirectory(asideDirectory);
        Directory.CreateDirectory(outOfCharacterDirectory);

        File.WriteAllText(
            Path.Combine(postsDirectory, "chapter.html"),
            CreateRpolSourceHtml(
                (1, RpolThreadPostUtility.DungeonMasterAuthor, "Mon 1 Jan 2026", "01:00", "The party arrives."),
                (2, RpolThreadPostUtility.NuandaAuthor, "Mon 1 Jan 2026", "01:05", "Nuanda answers."),
                (3, "Jelb Garrick", "Mon 1 Jan 2026", "01:10", "Jelb listens.")));
        File.WriteAllText(
            Path.Combine(asideDirectory, "aside.html"),
            CreateRpolSourceHtml(
                (4, RpolThreadPostUtility.NuandaNemereAuthor, "Mon 1 Jan 2026", "01:15", "Nemere answers."),
                (5, RpolThreadPostUtility.BillworthTurgenAuthor, "Mon 1 Jan 2026", "01:20", "Billworth watches.")));
        File.WriteAllText(
            Path.Combine(outOfCharacterDirectory, "ooc.html"),
            CreateRpolSourceHtml(
                (6, RpolThreadPostUtility.ThurganNewlAuthor, "Mon 1 Jan 2026", "01:25", "Thurgan comments."),
                (7, RpolThreadPostUtility.TheArchonAuthor, "Mon 1 Jan 2026", "01:30", "The Archon comments."),
                (8, "Kelpie Lawfuller", "Mon 1 Jan 2026", "01:35", "Kelpie comments.")));

        var counts = RpolThreadPostUtility.GetAdjustedPostTalliesFromSavedHtmlDirectories(
            postsDirectory,
            asideDirectory,
            outOfCharacterDirectory);

        AssertEqual(8, counts.Count, "expected adjusted author count");
        AssertEqual(6, counts[RpolThreadPostUtility.DungeonMasterAuthor], "unexpected Dungeon Master count");
        AssertEqual(1, counts[RpolThreadPostUtility.BillworthTurgenAuthor], "unexpected Billworth count");
        AssertEqual(1, counts["Jelb Garrick"], "unexpected Jelb count");
        AssertEqual(1, counts["Kelpie Lawfuller"], "unexpected Kelpie count");
        AssertEqual(1, counts[RpolThreadPostUtility.NuandaAuthor], "unexpected Nuanda count");
        AssertEqual(2, counts[RpolThreadPostUtility.NuandaNemereAuthor], "unexpected Nuanda Nemere count");
        AssertEqual(1, counts[RpolThreadPostUtility.TheArchonAuthor], "unexpected The-Archon count");
        AssertEqual(1, counts[RpolThreadPostUtility.ThurganNewlAuthor], "unexpected Thurgan count");
    }

    internal static void KeywordSearchFallsBackToThePrefixedTerm()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {
                    "The": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/the",
                          "count": 1,
                          "last_indexed": "2026-06-28T00:00:00.0000000+00:00"
                        }
                      ]
                    },
                    "The Coal": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/the-coal",
                          "count": 1,
                          "last_indexed": "2026-06-28T00:00:00.0000000+00:00"
                        }
                      ]
                    },
                    "The Hills": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/the-hills",
                          "count": 1,
                          "last_indexed": "2026-06-28T00:00:00.0000000+00:00"
                        }
                      ]
                    }
                  }
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                    txtSearch.Text = "The Coal Hills";
                    InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                    AssertEqual(3, results.Length, "expected three search results from the exact and fallback lookups");
                    AssertContains(string.Join("\n", results), "https://example.test/the");
                    AssertContains(string.Join("\n", results), "https://example.test/the-coal");
                    AssertContains(string.Join("\n", results), "https://example.test/the-hills");
                });
        });
    }

    internal static void KeywordSearchKeepsQuotedPhrasesTogether()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {
                    "one": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/one",
                          "count": 1,
                          "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                        }
                      ]
                    },
                    "two": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/two",
                          "count": 1,
                          "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                        }
                      ]
                    },
                    "one two": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/one-two",
                          "count": 1,
                          "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                        }
                      ]
                    },
                    "three": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/three",
                          "count": 1,
                          "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                        }
                      ]
                    }
                  }
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                    txtSearch.Text = "\"one two\" three";
                    InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                    AssertEqual(2, results.Length, "expected one quoted-phrase result plus one standalone result");
                    AssertContains(string.Join("\n", results), "https://example.test/one-two");
                    AssertContains(string.Join("\n", results), "https://example.test/three");
                    AssertFalse(results.Contains("https://example.test/one", StringComparer.Ordinal), "quoted phrase should not be split into a standalone 'one' lookup");
                    AssertFalse(results.Contains("https://example.test/two", StringComparer.Ordinal), "quoted phrase should not be split into a standalone 'two' lookup");
                });
        });
    }

    internal static void KeywordSearchAcceptsUrlSourceMetadata()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "urls": {
                    "https://example.test/rpol-entry": {
                      "source": "RPOL"
                    },
                    "https://example.test/obsidian-entry": {
                      "source": "Obsidian wiki"
                    }
                  },
                  "words": {
                    "entry": {
                      "total_occurrences": 2,
                      "matches": [
                        {
                          "url": "https://example.test/rpol-entry",
                          "count": 1,
                          "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                        },
                        {
                          "url": "https://example.test/obsidian-entry",
                          "count": 1,
                          "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                        }
                      ]
                    }
                  }
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                    txtSearch.Text = "entry";
                    InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                    AssertEqual(2, results.Length, "expected both matches to be returned when url source metadata is present");
                    AssertContains(string.Join("\n", results), "https://example.test/rpol-entry");
                    AssertContains(string.Join("\n", results), "https://example.test/obsidian-entry");
                });
        });
    }

    internal static void KeywordSearchFiltersRpolHeroMetadataOnlyHits()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {
                    "Kelpie Lawfuller": {
                      "total_occurrences": 3,
                      "matches": [
                        {
                          "url": "https://rpol.net/display.cgi?gi=80170&ti=11",
                          "count": 1,
                          "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                        },
                        {
                          "url": "https://rpol.net/display.cgi?gi=80170&ti=12",
                          "count": 1,
                          "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                        },
                        {
                          "url": "https://publish.obsidian.md/scarlethorizons/PCs/Kelpie+Lawfuller",
                          "count": 1,
                          "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                        }
                      ]
                    }
                  }
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");
                    var bodyCheckCount = 0;

                    SetPrivateField(
                        form,
                        "_playerCharacterListingMarkdown",
                        """
                        | Name | Character | Notes | Hero |
                        | --- | --- | --- | --- |
                        | Kelpie Lawfuller | [[Kelpie Lawfuller]] | active | ![[kelpie-token.webp]] |
                        """);
                    SetPrivateField(
                        form,
                        "_rpolHeroNameBodyMatchProvider",
                        (Func<string, string, CancellationToken, Task<bool>>)((url, term, _) =>
                        {
                            bodyCheckCount++;
                            AssertEqual("Kelpie Lawfuller", term, "unexpected hero term passed to RPOL body filter");
                            return Task.FromResult(url.Contains("ti=12", StringComparison.Ordinal));
                        }));

                    txtSearch.Text = "\"Kelpie Lawfuller\"";
                    InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                    AssertEqual(2, results.Length, "expected one RPOL body hit and one Obsidian hit");
                    AssertEqual(2, bodyCheckCount, "expected both RPOL matches to be checked against post bodies");
                    AssertContains(string.Join("\n", results), "https://rpol.net/display.cgi?gi=80170&ti=12&msgpage=&show=all");
                    AssertContains(string.Join("\n", results), "https://publish.obsidian.md/scarlethorizons/PCs/Kelpie+Lawfuller");
                    AssertFalse(
                        results.Contains("https://rpol.net/display.cgi?gi=80170&ti=11&msgpage=&show=all", StringComparer.Ordinal),
                        "metadata-only RPOL hit should be excluded for hero-name searches");
                });
        });
    }

    internal static void ShowMenuContainsXpItem()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
                ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
            var xpMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "xpToolStripMenuItem")
                ?? throw new InvalidOperationException("xpToolStripMenuItem was null."));

            AssertEqual("XP", xpMenuItem.Text ?? string.Empty, "unexpected XP menu item text");
            AssertTrue(
                showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(xpMenuItem),
                "Show menu should contain the XP item");
        });
    }

    internal static void ShowMenuContainsPartyItem()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
                ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
            var partyMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "partyToolStripMenuItem")
                ?? throw new InvalidOperationException("partyToolStripMenuItem was null."));

            AssertEqual("Party", partyMenuItem.Text ?? string.Empty, "unexpected Party menu item text");
            AssertTrue(
                showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(partyMenuItem),
                "Show menu should contain the Party item");
        });
    }

    internal static void ShowMenuContainsMyHeroBriefingItem()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
                ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
            var myHeroBriefingMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "myHeroBriefingToolStripMenuItem")
                ?? throw new InvalidOperationException("myHeroBriefingToolStripMenuItem was null."));

            AssertEqual("My Hero Briefing", myHeroBriefingMenuItem.Text ?? string.Empty, "unexpected My Hero Briefing menu item text");
            AssertTrue(
                showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(myHeroBriefingMenuItem),
                "Show menu should contain the My Hero Briefing item");
            AssertTrue(
                showMenuItem.DropDownItems.IndexOf(myHeroBriefingMenuItem) > showMenuItem.DropDownItems.IndexOf((ToolStripItem)(GetPrivateField(form, "partyToolStripMenuItem")
                    ?? throw new InvalidOperationException("partyToolStripMenuItem was null."))),
                "My Hero Briefing should appear after Party");
        });
    }

    internal static void ShowMenuContainsAdventureOutlineItem()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
                ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
            var adventureOutlineMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "adventureOutlineToolStripMenuItem")
                ?? throw new InvalidOperationException("adventureOutlineToolStripMenuItem was null."));

            AssertEqual("Adventure Outline", adventureOutlineMenuItem.Text ?? string.Empty, "unexpected Adventure Outline menu item text");
            AssertTrue(
                showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(adventureOutlineMenuItem),
                "Show menu should contain the Adventure Outline item");
        });
    }

    internal static void SearchEnterTriggersClickWhenEnabled()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {
                    "entry": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://publish.obsidian.md/scarlethorizons/entry",
                          "count": 1,
                          "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                        }
                      ]
                    }
                  }
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    using var buttonHost = new Form();

                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var btnSearch = GetControl<Button>(form, "btnSearch");
                    buttonHost.Controls.Add(btnSearch);
                    buttonHost.Show();
                    Application.DoEvents();

                    var clickCount = 0;
                    btnSearch.Click += (_, _) => clickCount++;

                    txtSearch.Text = "entry";
                    AssertTrue(btnSearch.Enabled, "expected search button to be enabled for a valid search term");

                    InvokePrivateMethod(
                        form,
                        "TxtSearch_EnterPressed",
                        txtSearch,
                        EventArgs.Empty);

                    AssertEqual(1, clickCount, "expected Enter to trigger the existing search click path");
                    var completionTimeout = Stopwatch.StartNew();
                    while (!btnSearch.Enabled && completionTimeout.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }

                    AssertTrue(btnSearch.Enabled, "expected the Enter-triggered search to complete");
                });
        });
    }

    internal static void KeywordSearchUppercasesEncryptedIndexResultsWithoutChangingLaunchUrl()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {
                    "nimba": {
                      "total_occurrences": 2,
                      "matches": [
                        {
                          "url": "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                          "count": 1,
                          "last_indexed": "2026-07-07T00:00:00.0000000+00:00"
                        },
                        {
                          "url": "https://publish.obsidian.md/scarlethorizons/NPCs/Nuanda+Armstrong",
                          "count": 1,
                          "last_indexed": "2026-07-07T00:00:00.0000000+00:00"
                        }
                      ]
                    }
                  }
                }
                """,
                () => WithTemporaryEncryptedTextIndex(
                    """
                    [
                      {
                        "url": "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                        "encrypted_sections": 2,
                        "frontmatter_tags": ["npc", "spy"]
                      }
                    ]
                    """,
                    () =>
                    {
                        using var form = new Form1(suppressHeroImagesForThisRun: true);
                        var txtSearch = GetControl<TextBox>(form, "txtSearch");
                        var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                        txtSearch.Text = "nimba";
                        InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                        var results = lstSearchResults.Items.Cast<object>().ToArray();
                        AssertEqual(2, results.Length, "expected both keyword-index matches to be returned");
                        AssertEqual(
                            "HTTPS://PUBLISH.OBSIDIAN.MD/SCARLETHORIZONS/NPCS/NIMBA+ARMSTRONG",
                            results[0]?.ToString() ?? string.Empty,
                            "encrypted index result should display in uppercase");
                        AssertEqual(
                            "https://publish.obsidian.md/scarlethorizons/NPCs/Nuanda+Armstrong",
                            results[1]?.ToString() ?? string.Empty,
                            "non-encrypted index result should display normally");

                        var launchUrl = InvokePrivateMethod(form, "GetSearchResultLaunchUrl", results[0]);
                        AssertEqual(
                            "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                            launchUrl?.ToString() ?? string.Empty,
                            "uppercase display item should retain the original launch URL");
                    }));
        });
    }

    internal static void KeywordSearchUppercasesOnlineObsidianFallbackResults()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {}
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                    SetPrivateField(
                        form,
                        "_showLocalIndexMissPrompt",
                        (Func<string[], DialogResult>)(_ => DialogResult.Yes));
                    SetPrivateField(
                        form,
                        "_showOnlineSearchCompletedMessage",
                        (Action<string[], int>)((_, _) => { }));
                    SetPrivateField(
                        form,
                        "_onlineSearchProvider",
                        (Func<string[], CancellationToken, Task<string[]>>)((_, _) => Task.FromResult(new[]
                        {
                            "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                            "https://rpol.net/display.cgi?gi=80170&ti=12&msgpage=&show=all"
                        })));

                    txtSearch.Text = "not indexed locally";
                    InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().ToArray();
                    AssertEqual(2, results.Length, "expected online fallback to populate both provider results");
                    AssertEqual(
                        "HTTPS://PUBLISH.OBSIDIAN.MD/SCARLETHORIZONS/NPCS/NIMBA+ARMSTRONG",
                        results[0]?.ToString() ?? string.Empty,
                        "online Obsidian fallback result should display in uppercase");
                    AssertEqual(
                        "https://rpol.net/display.cgi?gi=80170&ti=12&msgpage=&show=all",
                        results[1]?.ToString() ?? string.Empty,
                        "non-Obsidian online fallback result should display normally");

                    var launchUrl = InvokePrivateMethod(form, "GetSearchResultLaunchUrl", results[0]);
                    AssertEqual(
                        "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                        launchUrl?.ToString() ?? string.Empty,
                        "uppercase online Obsidian item should retain the original launch URL");
                });
        });
    }

    internal static void KeywordSearchOffersOnlineFallbackOnLocalMiss()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {}
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");
                    var promptCallCount = 0;
                    var onlineSearchCallCount = 0;
                    var onlineSearchCompletedCallCount = 0;

                    SetPrivateField(
                        form,
                        "_showLocalIndexMissPrompt",
                        (Func<string[], DialogResult>)(terms =>
                        {
                            promptCallCount++;
                            AssertEqual("not indexed locally", terms[0], "unexpected prompt term");
                            return DialogResult.Yes;
                        }));
                    SetPrivateField(
                        form,
                        "_onlineSearchProvider",
                        (Func<string[], CancellationToken, Task<string[]>>)((terms, _) =>
                        {
                            onlineSearchCallCount++;
                            AssertEqual("not indexed locally", terms[0], "unexpected online search term");
                            return Task.FromResult(new[]
                            {
                                "https://example.test/online-result"
                            });
                        }));
                    SetPrivateField(
                        form,
                        "_showOnlineSearchCompletedMessage",
                        (Action<string[], int>)((terms, resultCount) =>
                        {
                            onlineSearchCompletedCallCount++;
                            AssertEqual("not indexed locally", terms[0], "unexpected completed-message term");
                            AssertEqual(1, resultCount, "unexpected completed-message result count");
                        }));

                    txtSearch.Text = "\"not indexed locally\"";
                    InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                    AssertEqual(1, promptCallCount, "expected the local-index miss prompt to be shown once");
                    AssertEqual(1, onlineSearchCallCount, "expected online search to run once");
                    AssertEqual(1, onlineSearchCompletedCallCount, "expected the online-search completion message to be shown once");
                    AssertEqual(1, results.Length, "expected online fallback to populate one result");
                    AssertContains(string.Join("\n", results), "https://example.test/online-result");
                });
        });
    }

    internal static void KeywordSearchCancelsPreviousOnlineFallback()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {}
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var btnSearch = GetControl<Button>(form, "btnSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");
                    var firstSearchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var secondSearchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    CancellationToken firstSearchToken = default;
                    var onlineSearchCallCount = 0;

                    SetPrivateField(
                        form,
                        "_showLocalIndexMissPrompt",
                        (Func<string[], DialogResult>)(_ => DialogResult.Yes));
                    SetPrivateField(
                        form,
                        "_showOnlineSearchCompletedMessage",
                        (Action<string[], int>)((_, _) => { }));
                    SetPrivateField(
                        form,
                        "_onlineSearchProvider",
                        (Func<string[], CancellationToken, Task<string[]>>)(async (terms, cancellationToken) =>
                        {
                            onlineSearchCallCount++;
                            if (onlineSearchCallCount == 1)
                            {
                                firstSearchToken = cancellationToken;
                                firstSearchStarted.SetResult();
                                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                            }

                            secondSearchStarted.SetResult();
                            cancellationToken.ThrowIfCancellationRequested();
                            return ["https://example.test/current-search"];
                        }));

                    txtSearch.Text = "\"first missing\"";
                    _ = InvokePrivateAsync(form, "PerformSearchAsync");
                    AssertTrue(firstSearchStarted.Task.Wait(TimeSpan.FromSeconds(2)), "first search did not reach online fallback");

                    txtSearch.Text = "\"second missing\"";
                    var secondSearch = InvokePrivateAsync(form, "PerformSearchAsync");
                    secondSearch.GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                    AssertTrue(firstSearchToken.IsCancellationRequested, "starting a second search should cancel the first search token");
                    AssertTrue(secondSearchStarted.Task.IsCompleted, "second search did not reach online fallback");
                    AssertEqual(2, onlineSearchCallCount, "expected both online search attempts to start");
                    AssertTrue(btnSearch.Enabled, "search button should be re-enabled after current search completes");
                    AssertEqual(1, results.Length, "only current search results should remain");
                    AssertContains(string.Join("\n", results), "https://example.test/current-search");
                });
        });
    }

    internal static void KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheart()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var txtSearch = GetControl<TextBox>(form, "txtSearch");
            var rdoRPOL = GetControl<RadioButton>(form, "rdoRPOL");
            var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

            rdoRPOL.Checked = true;
            txtSearch.Text = "whiteheart";
            InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

            var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
            AssertEqual(0, results.Length, "expected RPOL-only search to exclude the Obsidian-only whiteheart entry");
        });
    }

    internal static void KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheartStiffwhiskers()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var txtSearch = GetControl<TextBox>(form, "txtSearch");
            var rdoRPOL = GetControl<RadioButton>(form, "rdoRPOL");
            var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

            rdoRPOL.Checked = true;
            txtSearch.Text = "whiteheart stiffwhiskers";
            InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

            var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
            AssertEqual(0, results.Length, "expected RPOL-only search to exclude the Obsidian-only whiteheart stiffwhiskers entry");
        });
    }

    internal static void KeywordSearchExpandsHeroFirstAndFullNames()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            SetPrivateField(
                form,
                "_playerCharacterListingMarkdown",
                """
                | Name | Character | Notes | Hero |
                | ---- | --------- | ----- | ---- |
                | [[Kelpie Lawfuller]] | Fighter | active | ![[kelpie-token.webp]] |
                | [[Jelb Garrick]] | Illusionist | active | ![[jelb-token.webp]] |
                """);

            var kelpieAliases = ((string[]?)InvokePrivateMethod(form, "GetHeroSearchTermAliases", "Kelpie")
                ?? []).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            var jelbAliases = ((string[]?)InvokePrivateMethod(form, "GetHeroSearchTermAliases", "Jelb Garrick")
                ?? []).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

            AssertEqual(2, kelpieAliases.Length, "Kelpie first-name search should produce first and full-name aliases");
            AssertEqual("Kelpie", kelpieAliases[0], "unexpected Kelpie first-name alias");
            AssertEqual("Kelpie Lawfuller", kelpieAliases[1], "unexpected Kelpie full-name alias");
            AssertEqual(2, jelbAliases.Length, "Jelb full-name search should produce first and full-name aliases");
            AssertEqual("Jelb", jelbAliases[0], "unexpected Jelb first-name alias");
            AssertEqual("Jelb Garrick", jelbAliases[1], "unexpected Jelb full-name alias");
        });
    }

    internal static void PartyHeroSheetParserReadsSummaryAndHidesXpLines()
    {
        var hero = PartyHeroUtility.ParseHeroSheet(
            """
            ---
            dg-publish: true
            ---
            ![[jelb-token.webp]]

            Class: Illusionist
            HP: 4
            Level: 1
            XP: 0
            Intelligence 16 Language Native+2 Literacy Literate XP Bonus: 10%
            Attained level 03 Illusionist after XP was awarded.

            Name: Jelb Garrick
            """,
            "Jelb");

        AssertEqual("Jelb Garrick", hero.Name, "unexpected parsed hero name");
        AssertEqual("Illusionist", hero.CharacterClass, "unexpected parsed class");
        AssertEqual("3", hero.Level, "unexpected parsed level");
        AssertEqual("4", hero.HitPoints, "unexpected parsed hit points");
        AssertFalse(hero.CharacterSheetText.Contains("XP: 0", StringComparison.Ordinal), "XP total lines should be hidden from party sheet text");
        AssertContains(hero.CharacterSheetText, "XP Bonus: 10%");
    }

    internal static void MyHeroBriefingBuildsSelectedHeroSummaryBoundary()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Kelpie Lawfuller", "kelpie-token.webp", "3", "Fighter", "12", "Kelpie sheet", CanonicalId: "kelpie"),
            new("Jelb Garrick", "jelb-token.webp", "3", "Illusionist", "8", "Jelb sheet", CanonicalId: "jelb")
        };
        var request = new MyHeroBriefingRequest(
            heroes,
            SelectedHeroCanonicalId: "kelpie",
            XpTotals: [new PcXpTotal("Jelb Garrick", 8575, "jelb")],
            ThreadPosts:
            [
                new MyHeroBriefingThreadPosts(
                    "Chapter 1",
                    "https://rpol.net/display.cgi?gi=80170&ti=7",
                    [CreateRpolThreadPost(1, "Jelb", "I inspect the corridor.")])
            ],
            EncryptedTextIndex:
            [
                new EncryptedTextIndexEntry(
                    "https://publish.obsidian.md/scarlethorizons/Secrets",
                    1,
                    ["illusionist"])
            ],
            QuickLinks:
            [
                new MyHeroBriefingQuickLink("Party", "app://show/party")
            ],
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("jelb", "Jelb Garrick", ["Jelb"], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player)));

        var briefing = MyHeroBriefingUtility.Build(request);

        AssertFalse(briefing.NeedsHeroSelection, "selected hero should not require a picker");
        AssertTrue(briefing.Hero is not null, "selected hero should build a hero summary");
        AssertEqual("Jelb Garrick", briefing.Hero!.Name, "unexpected briefing hero");
        AssertEqual("Illusionist", briefing.Hero.CharacterClass, "unexpected briefing class");
        AssertEqual("3", briefing.Hero.Level, "unexpected briefing level");
        AssertEqual("8", briefing.Hero.HitPoints, "unexpected briefing hit points");
        AssertEqual(8575, briefing.Hero.XpTotal ?? -1, "XP should match canonical identity");
        AssertEqual("jelb-token.webp", briefing.Hero.TokenImagePath ?? string.Empty, "unexpected token path");
        AssertEqual("Jelb Garrick", briefing.Hero.AccessContext.CharacterName ?? string.Empty, "unexpected access context character");
        AssertTrue(briefing.HeroCard is not null, "selected hero should build a current hero card");
        AssertEqual("Jelb Garrick", briefing.HeroCard!.Name, "unexpected card hero");
        AssertEqual("Illusionist", briefing.HeroCard.CharacterClass, "unexpected card class");
        AssertEqual("3", briefing.HeroCard.Level, "unexpected card level");
        AssertEqual("8", briefing.HeroCard.HitPoints, "unexpected card hit points");
        AssertEqual("XP Total: 8,575", briefing.HeroCard.XpTotalLabel, "unexpected card XP label");
        AssertEqual("jelb-token.webp", briefing.HeroCard.TokenImagePath ?? string.Empty, "unexpected card token path");
        AssertEqual("Jelb sheet", briefing.HeroCard.CharacterSheetText, "unexpected card sheet text");
        AssertEqual(2, briefing.HeroChoices.Count, "unexpected hero choice count");
        AssertEqual(MyHeroBriefingHeroIdentitySource.AuthenticatedHero, briefing.HeroIdentitySource, "authenticated identity should win before selected hero");
        AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Full Sheet" && link.Target == "app://show/party"), "briefing should include a full-sheet quick link");
        AssertTrue(briefing.QuickLinks.Any(link => link.Label == "XP" && link.Target == "app://show/xp"), "briefing should include an XP quick link");
        AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Party" && link.Target == "app://show/party"), "briefing should include a Party quick link");
        AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Adventure Outline" && link.Target == "app://show/adventure-outline"), "briefing should include an Adventure Outline quick link");
        AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Chapter 1" && link.Target == "https://rpol.net/display.cgi?gi=80170&ti=7"), "briefing should include RPOL thread quick links");
        AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Party" && link.Target == "app://show/party"), "provided quick links should be retained");
        AssertEqual(briefing.QuickLinks.Count, briefing.HeroCard.QuickLinks.Count, "card quick links should mirror briefing quick links");
        AssertEqual(1, briefing.RecentActivity.Count, "explicit identity alias should surface matching activity");
        AssertEqual(0, briefing.LikelyResponseItems.Count, "response items should be left for the later backlog step");
        AssertEqual(1, briefing.UnlockedNotes.Count, "encrypted index input should surface unlocked notes");
        AssertEqual("Secrets", briefing.UnlockedNotes[0].Title, "unexpected unlocked note title");
    }

    internal static void MyHeroBriefingPrefersAuthenticatedHeroIdentity()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet", CanonicalId: "kelpie"),
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet", CanonicalId: "jelb")
        };

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            SelectedHeroCanonicalId: "kelpie",
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("jelb", "Jelb Garrick", ["Jelb"], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player))));

        AssertTrue(briefing.Hero is not null, "authenticated hero should resolve a briefing hero");
        AssertEqual("Jelb Garrick", briefing.Hero!.Name, "authenticated canonical identity should select Jelb");
        AssertEqual(MyHeroBriefingHeroIdentitySource.AuthenticatedHero, briefing.HeroIdentitySource, "unexpected identity source");
        AssertFalse(briefing.NeedsHeroSelection, "resolved authenticated hero should not need a picker");
    }

    internal static void MyHeroBriefingRequiresExplicitDungeonMasterHeroSelection()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet", CanonicalId: "kelpie"),
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet", CanonicalId: "jelb")
        };
        var dungeonMasterIdentity = XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("dm", "Dungeon Master", [], true ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player));
        var unresolved = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            AuthenticatedIdentity: dungeonMasterIdentity));
        var selected = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            SelectedHeroCanonicalId: "kelpie",
            AuthenticatedIdentity: dungeonMasterIdentity));

        AssertTrue(unresolved.Hero is null, "DM briefing should not infer a hero from Dungeon Master identity");
        AssertTrue(unresolved.NeedsHeroSelection, "DM briefing should request explicit hero selection");
        AssertEqual(MyHeroBriefingHeroIdentitySource.None, unresolved.HeroIdentitySource, "unexpected unresolved DM identity source");
        AssertEqual("Choose a hero to build My Hero Briefing for Dungeon Master view.", unresolved.StatusMessage, "unexpected DM picker status");
        AssertTrue(selected.Hero is not null, "explicit DM selection should resolve a hero");
        AssertEqual("Kelpie Lawfuller", selected.Hero!.Name, "unexpected selected DM hero");
        AssertEqual(MyHeroBriefingHeroIdentitySource.SelectedHero, selected.HeroIdentitySource, "unexpected selected DM identity source");
    }

    internal static void MyHeroBriefingLeavesAmbiguousFirstNameUnresolved()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Max North", null, "1", "Fighter", "5", "Max North sheet", CanonicalId: "max-north"),
            new("Max Stone", null, "2", "Thief", "7", "Max Stone sheet", CanonicalId: "max-stone")
        };

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            AuthenticatedHeroName: "Max"));

        AssertTrue(briefing.Hero is null, "ambiguous first-name identity should remain unresolved");
        AssertFalse(briefing.NeedsHeroSelection, "unauthenticated identity should not offer protected hero selection");
        AssertEqual(MyHeroBriefingHeroIdentitySource.None, briefing.HeroIdentitySource, "unexpected ambiguous identity source");
        AssertEqual("No authenticated hero is available for My Hero Briefing.", briefing.StatusMessage, "unexpected fail-closed status");
    }

    internal static void MyHeroBriefingRejectsUnauthenticatedSelectedHero()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet", CanonicalId: "kelpie"),
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet", CanonicalId: "jelb")
        };

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            SelectedHeroCanonicalId: "kelpie",
            XpTotals: [new PcXpTotal("Kelpie Lawfuller", 7062, "kelpie")]));

        AssertTrue(briefing.HeroCard is null, "unauthenticated selected hero produced a protected hero card");
        AssertEqual(MyHeroBriefingHeroIdentitySource.None, briefing.HeroIdentitySource, "unauthenticated selection resolved an identity");
    }

    internal static void MyHeroBriefingPickerReturnsCanonicalId()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var selected = (string?)InvokePrivateMethod(
                form,
                "PromptForMyHeroBriefingHeroSelection",
                (object)new MyHeroBriefingHeroChoice[]
                {
                    new("hero-stable-001", "Shared Display Name")
                });

            AssertEqual("hero-stable-001", selected ?? string.Empty, "briefing picker returned a display name instead of the canonical ID");
        });
    }

    internal static void MyHeroBriefingBuildsRecentHeroActivity()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet", CanonicalId: "jelb")
        };
        var matchingPosts = Enumerable.Range(1, 12)
            .Select(index => new RpolThreadPost(
                index,
                index % 2 == 0 ? "Dungeon Master" : "Kelpie",
                string.Empty,
                "Mon 1 Jan 2026",
                $"{index:00}:00",
                $"{index:000}.html",
                "<div></div>",
                "<p></p>",
                index == 12
                    ? "Jelb Garrick considers the long corridor. " + new string('x', 220)
                    : $"Jelb studies clue {index}."))
            .Concat(
            [
                new RpolThreadPost(
                    13,
                    "Dungeon Master",
                    string.Empty,
                    "Mon 1 Jan 2026",
                    "13:00",
                    "013.html",
                    "<div></div>",
                    "<p></p>",
                    "A jelbian carving is unrelated."),
                new RpolThreadPost(
                    14,
                    "Dungeon Master",
                    string.Empty,
                    "Mon 1 Jan 2026",
                    "14:00",
                    "014.html",
                    "<div></div>",
                    "<p></p>",
                    "Kelpie studies the same clue."),
                new RpolThreadPost(
                    15,
                    "Jelb",
                    string.Empty,
                    "Mon 1 Jan 2026",
                    "15:00",
                    "015.html",
                    "<div></div>",
                    "<p></p>",
                    "I check the stonework for hidden catches.")
            ])
            .ToArray();
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            ThreadPosts:
            [
                new MyHeroBriefingThreadPosts(
                    "Chapter 1",
                    "https://rpol.net/display.cgi?gi=80170&ti=7",
                    matchingPosts)
            ],
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("jelb", "Jelb Garrick", ["Jelb"], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player))));

        AssertEqual(10, briefing.RecentActivity.Count, "recent activity should be capped at ten matching posts");
        AssertEqual(15, briefing.RecentActivity[0].MessageNumber, "latest hero-authored post should appear first");
        AssertEqual(4, briefing.RecentActivity[^1].MessageNumber, "oldest retained matching post should be message 4");
        AssertTrue(
            briefing.RecentActivity.All(item => item.ThreadTitle == "Chapter 1"
                && item.ThreadUrl == "https://rpol.net/display.cgi?gi=80170&ti=7"),
            "activity items should retain thread context");
        AssertTrue(
            briefing.RecentActivity.All(item => item.MessageNumber != 13 && item.MessageNumber != 14),
            "activity should exclude substring matches and unrelated hero posts");
        AssertTrue(briefing.RecentActivity.Any(item => item.MessageNumber == 15), "hero-authored posts should count as recent activity");
        AssertTrue(briefing.RecentActivity[1].Excerpt.EndsWith("...", StringComparison.Ordinal), "long excerpts should be shortened");
        AssertTrue(briefing.RecentActivity[1].Excerpt.Length <= 183, "shortened excerpts should stay bounded");
    }

    internal static void MyHeroBriefingBuildsLikelyOpenResponseItems()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet", CanonicalId: "jelb")
        };
        var chapterPosts = new RpolThreadPost[]
        {
            CreateRpolThreadPost(1, "Dungeon Master", "Before Jelb posts."),
            CreateRpolThreadPost(2, "Jelb", "Jelb watches the door."),
            CreateRpolThreadPost(3, "Kelpie", "Should we open it?"),
            CreateRpolThreadPost(4, "Dungeon Master", "Jelb hears a faint click."),
            CreateRpolThreadPost(5, "Nuanda", "The corridor stays quiet."),
            CreateRpolThreadPost(6, "Jelb", "Jelb studies the lock."),
            CreateRpolThreadPost(7, "Dungeon Master", "The lock gives way."),
            CreateRpolThreadPost(8, "Kelpie", "Jelb, do you want the lantern?")
        };
        var noHeroPostThread = new RpolThreadPost[]
        {
            CreateRpolThreadPost(1, "Kelpie", "Jelb might know this."),
            CreateRpolThreadPost(2, "Dungeon Master", "What happens next?")
        };

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            ThreadPosts:
            [
                new MyHeroBriefingThreadPosts(
                    "Chapter 1",
                    "https://rpol.net/display.cgi?gi=80170&ti=7",
                    chapterPosts),
                new MyHeroBriefingThreadPosts(
                    "Chapter 2",
                    "https://rpol.net/display.cgi?gi=80170&ti=8",
                    noHeroPostThread)
            ],
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("jelb", "Jelb Garrick", ["Jelb"], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player))));

        AssertEqual(2, briefing.LikelyResponseItems.Count, "only posts after the hero's latest post should be response candidates");
        AssertEqual(8, briefing.LikelyResponseItems[0].MessageNumber, "direct mention should rank first");
        AssertEqual("Direct mention after your last post", briefing.LikelyResponseItems[0].Reason, "unexpected direct-mention reason");
        AssertEqual(7, briefing.LikelyResponseItems[1].MessageNumber, "neutral follow-up should remain after direct mentions and questions");
        AssertEqual("Recent post after your last post", briefing.LikelyResponseItems[1].Reason, "weak evidence should stay neutral");
        AssertTrue(
            briefing.LikelyResponseItems.All(item => item.ThreadTitle == "Chapter 1"
                && item.ThreadUrl == "https://rpol.net/display.cgi?gi=80170&ti=7"),
            "response items should be grouped by retaining thread context and ignore threads without a hero post");
    }

    private static RpolThreadPost CreateRpolThreadPost(int messageNumber, string author, string bodyText)
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

    internal static void MyHeroBriefingSurfacesRelevantUnlockedNotes()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet", CanonicalId: "jelb")
        };
        var encryptedIndex = new EncryptedTextIndexEntry[]
        {
            new(
                "https://publish.obsidian.md/scarlethorizons/Secrets/Illusionist+Clue",
                2,
                ["Class Illusionist"]),
            new(
                "https://publish.obsidian.md/scarlethorizons/Secrets/Jelb+Only",
                1,
                ["Hero Jelb"]),
            new(
                "https://publish.obsidian.md/scarlethorizons/Secrets/High+Level",
                1,
                ["Level 4"]),
            new(
                "https://publish.obsidian.md/scarlethorizons/Secrets/Fighter+Only",
                1,
                ["Class Fighter"]),
            new(
                "https://publish.obsidian.md/scarlethorizons/Secrets/Public",
                0,
                ["Class Illusionist"])
        };

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            EncryptedTextIndex: encryptedIndex,
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("jelb", "Jelb Garrick", ["Jelb"], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player))));

        AssertEqual(2, briefing.UnlockedNotes.Count, "only notes unlocked by hero tags should be surfaced");
        AssertTrue(
            briefing.UnlockedNotes.Any(note =>
                note.Title == "Illusionist Clue"
                && note.Url == "https://publish.obsidian.md/scarlethorizons/Secrets/Illusionist+Clue"
                && note.Excerpt == "2 unlocked encrypted sections may be relevant."),
            "class-matched encrypted note should be included");
        AssertTrue(
            briefing.UnlockedNotes.Any(note =>
                note.Title == "Jelb Only"
                && note.Excerpt == "1 unlocked encrypted section may be relevant."),
            "hero-name matched encrypted note should be included");
        AssertFalse(
            briefing.UnlockedNotes.Any(note => note.Title is "High Level" or "Fighter Only" or "Public"),
            "locked notes and entries without encrypted sections should remain hidden");
    }

    internal static void MyHeroBriefingRequestsHeroSelectionWhenNoHeroSelected()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet", CanonicalId: "kelpie"),
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet", CanonicalId: "jelb")
        };

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("dm", "Dungeon Master", [], true ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player))));

        AssertTrue(briefing.Hero is null, "briefing should not choose a hero before identity resolution exists");
        AssertTrue(briefing.NeedsHeroSelection, "briefing should request a hero selection");
        AssertEqual(2, briefing.HeroChoices.Count, "unexpected hero choice count");
        AssertEqual(MyHeroBriefingHeroIdentitySource.None, briefing.HeroIdentitySource, "unexpected unresolved identity source");
        AssertEqual("Choose a hero to build My Hero Briefing for Dungeon Master view.", briefing.StatusMessage, "unexpected picker status");
    }

    internal static void MyHeroBriefingDisplayTextIncludesFocusedSections()
    {
        var briefing = CreateMyHeroBriefingDisplayFixture();
        var text = (string)(InvokeStaticMethod(typeof(Form1), "FormatMyHeroBriefingForDisplay", briefing)
            ?? throw new InvalidOperationException("briefing display text was null."));

        AssertContains(text, "My Hero Briefing");
        AssertContains(text, "Current Hero");
        AssertContains(text, "Jelb Garrick");
        AssertContains(text, "Class: Illusionist");
        AssertContains(text, "Level: 3");
        AssertContains(text, "HP: 8");
        AssertContains(text, "XP: 1,234 XP");
        AssertContains(text, "Likely Open Response Items");
        AssertContains(text, "*First, the app finds the hero's latest authored post in each thread.*");
        AssertContains(text, "*Then it looks at later posts in that same thread by other authors.*");
        AssertContains(text, "*Those later posts are ranked as:*");
        AssertContains(text, "*- Direct mention after your last post when the post mentions the hero by canonical name or explicit alias.*");
        AssertContains(text, "*- Question-like post after your last post when the post contains a ?.*");
        AssertContains(text, "*- Recent post after your last post when it is simply a later post in that thread.*");
        AssertContains(text, "Direct mention after your last post");
        AssertContains(text, "Recent Hero Activity");
        AssertContains(text, "Relevant Unlocked Notes");
        AssertContains(text, "Jelb Only");
        AssertContains(text, "Quick Links");
    }

    internal static void MyHeroBriefingStylesLikelyResponseKey()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            InvokePrivateMethod(form, "ShowMyHeroBriefing", CreateMyHeroBriefingDisplayFixture());
            var textBox = (RichTextBox)(GetPrivateField(form, "_myHeroBriefingTextBox")
                ?? throw new InvalidOperationException("my hero briefing text box was null."));
            const string keyLine = "*First, the app finds the hero's latest authored post in each thread.*";
            var start = textBox.Text.IndexOf(keyLine, StringComparison.Ordinal);

            AssertTrue(start >= 0, "expected likely response key line to be present");
            textBox.Select(start, 1);
            AssertEqual(Color.FromArgb(246, 241, 222), textBox.SelectionBackColor, "unexpected likely response key background color");
        });
    }

    private static MyHeroBriefing CreateMyHeroBriefingDisplayFixture()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Jelb Garrick", "jelb-token.webp", "3", "Illusionist", "8", "Jelb sheet", CanonicalId: "jelb")
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
            ThreadPosts: posts,
            XpTotals: [new PcXpTotal("Jelb Garrick", 1234, "jelb")],
            EncryptedTextIndex: encryptedIndex,
            AuthenticatedIdentity: XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("jelb", "Jelb Garrick", ["Jelb"], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player))));
    }

    internal static void MyHeroBriefingEncryptedIndexLoaderToleratesMalformedJson()
    {
        WithTemporaryEncryptedTextIndex(
            "{ not-json",
            () =>
            {
                var entries = (IReadOnlyList<EncryptedTextIndexEntry>)(InvokeStaticMethod(typeof(Form1), "LoadMyHeroBriefingEncryptedTextIndex")
                    ?? throw new InvalidOperationException("encrypted index entries were null."));

                AssertEqual(0, entries.Count, "malformed encrypted index should be ignored");
            });
    }

    private static string CreateRpolSourceHtml(params (int MessageNumber, string Author, string Date, string Time, string BodyText)[] posts)
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

    internal static void PartyHeroListingSummaryOverridesStaleCachedSheet()
    {
        using var directory = TemporaryDirectory.Create();
        var activeDirectory = Path.Combine(directory.Path, "active");
        Directory.CreateDirectory(activeDirectory);
        File.WriteAllText(
            PlayerCharacterAssetUtility.GetPlayerCharactersListingMarkdownCachePath(directory.Path),
            """
            | Name | Class | Level | Token | HP | Race | AC |
            | ---- | ----- | ----- | ----- | -- | ---- | -- |
            | [[Jelb Garrick]] | Illusionist | 3 | ![[jelb-token.webp\|70]] | 8 | Human | 7[12] |
            """);
        File.WriteAllText(
            Path.Combine(activeDirectory, "jelb.md"),
            """
            Class: Illusionist
            HP: 4
            Level: 1

            Name: Jelb Garrick
            """);

        var heroes = PartyHeroUtility.LoadActiveParty(directory.Path);

        AssertEqual(1, heroes.Count, "unexpected active party count");
        AssertEqual("Jelb Garrick", heroes[0].Name, "sheet name should remain the displayed party name");
        AssertEqual("Illusionist", heroes[0].CharacterClass, "listing class should be used");
        AssertEqual("3", heroes[0].Level, "listing level should override stale sheet level");
        AssertEqual("8", heroes[0].HitPoints, "listing HP should override stale sheet HP");
        AssertContains(heroes[0].CharacterSheetText, "HP: 4");
    }

    internal static void PartyHeroLegacyFileRejectsDifferentFullName()
    {
        using var directory = TemporaryDirectory.Create();
        var activeDirectory = Path.Combine(directory.Path, "active");
        Directory.CreateDirectory(activeDirectory);
        File.WriteAllText(
            PlayerCharacterAssetUtility.GetPlayerCharactersListingMarkdownCachePath(directory.Path),
            """
            | Name | Canonical ID | Class | Level | Token | HP |
            | ---- | ------------ | ----- | ----- | ----- | -- |
            | [[Ari Stoneward]] | ari-stoneward | Ranger | 4 | | 31 |
            """);
        const string rivalSheet = """
            Class: Bard
            HP: 48
            Level: 7

            Name: Ari Valesong
            """;
        File.WriteAllText(Path.Combine(activeDirectory, "ari-stoneward.md"), rivalSheet);
        File.WriteAllText(Path.Combine(activeDirectory, "ari.md"), rivalSheet);

        var hero = PartyHeroUtility.LoadActiveParty(directory.Path).Single();

        AssertEqual("Ari Stoneward", hero.Name, "legacy first-name file supplied another hero's display identity");
        AssertEqual(
            "Character sheet markdown is not available.",
            hero.CharacterSheetText,
            "legacy first-name file supplied another hero's protected sheet");
    }

    internal static void PartyHeroXpVisibilityFollowsAuthenticatedCharacter()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet", CanonicalId: "kelpie"),
            new("Jelb Garrick", null, "1", "Illusionist", "4", "Jelb sheet", CanonicalId: "jelb")
        };
        var xpTotals = new PcXpTotal[]
        {
            new("Kelpie Lawfuller", 7062, "kelpie"),
            new("Jelb Garrick", 8575, "jelb")
        };

        var kelpieView = PartyHeroUtility.WithVisibleXpTotals(
            heroes,
            xpTotals,
            XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("kelpie", "Kelpie Lawfuller", [], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player)));
        var dmView = PartyHeroUtility.WithVisibleXpTotals(
            heroes,
            xpTotals,
            XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("dm", "Dungeon Master", [], true ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player)));

        AssertEqual(7062, kelpieView[0].XpTotal ?? -1, "authenticated hero should see their own XP");
        AssertTrue(kelpieView[1].XpTotal is null, "authenticated hero should not see another hero's XP");
        AssertEqual(7062, dmView[0].XpTotal ?? -1, "DM should see Kelpie XP");
        AssertEqual(8575, dmView[1].XpTotal ?? -1, "DM should see Jelb XP");
    }

    internal static void PartyHeroXpVisibilityRequiresUniqueCanonicalIdentity()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Ari Stoneward", null, "4", "Ranger", "31", "Ari sheet", CanonicalId: "ari-stoneward"),
            new("Ari Valesong", null, "7", "Bard", "48", "Ari sheet", CanonicalId: "ari-valesong")
        };
        var totals = new PcXpTotal[]
        {
            new("Ari Stoneward", 1125, "ari-stoneward"),
            new("Ari Valesong", 2375, "ari-valesong")
        };

        var player = PartyHeroUtility.WithVisibleXpTotals(
            heroes,
            totals,
            XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("ari-valesong", "Ari Valesong", [], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player)));
        AssertTrue(player[0].XpTotal is null, "a player must not receive another same-first-name hero's XP");
        AssertEqual(2375, player[1].XpTotal ?? -1, "player XP should resolve by canonical ID");

        var duplicateHeroIds = PartyHeroUtility.WithVisibleXpTotals(
            [heroes[0], heroes[0] with { Name = "Ari Valesong" }],
            totals,
            XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("ari-stoneward", "Ari Stoneward", [], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player)));
        AssertTrue(duplicateHeroIds.All(hero => hero.XpTotal is null), "duplicate roster identities must fail closed");

        var missingHeroIdentity = PartyHeroUtility.WithVisibleXpTotals(
            [heroes[0] with { CanonicalId = null }],
            totals,
            XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("ari-stoneward", "Ari Stoneward", [], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player)));
        AssertTrue(missingHeroIdentity[0].XpTotal is null, "a hero without canonical identity must not receive protected XP");
    }

    internal static void TaggedNoteCipherDecryptsForMatchingLevelTag()
    {
        var hero = new HeroAccessContext(
            Level: 8,
            CharacterClass: "Paladin",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Wis"] = 12
            });

        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Level 8}The shrine door opens at moonrise.{Level 8}",
            TaggedNoteCipherMode.Encrypt);
        var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: hero);

        AssertEqual("{Level 8}The shrine door opens at moonrise.{Level 8}", decrypted, "matching level tag should decrypt note text");
        AssertTrue(encrypted.StartsWith("{Level 8}", StringComparison.Ordinal), "encrypted note should preserve opening tags as plaintext");
        AssertTrue(encrypted.EndsWith("{Level 8}", StringComparison.Ordinal), "encrypted note should preserve closing tags as plaintext");
        AssertFalse(encrypted.Contains("The shrine door opens", StringComparison.Ordinal), "encrypted note should hide wrapped plaintext");
    }

    internal static void TaggedNoteCipherDecryptsForMatchingCharacterTag()
    {
        var jelbHero = HeroAccessContext.FromPartyHeroSheet(new PartyHeroSheet(
            Name: "Jelb Stonehand",
            TokenImagePath: null,
            Level: "3",
            CharacterClass: "Fighter",
            HitPoints: "20",
            CharacterSheetText: "Name: Jelb Stonehand"),
            characterAliases: ["Jelb"]);
        var otherHero = HeroAccessContext.FromPartyHeroSheet(new PartyHeroSheet(
            Name: "Kelpie Lawfuller",
            TokenImagePath: null,
            Level: "8",
            CharacterClass: "Paladin",
            HitPoints: "42",
            CharacterSheetText: "Name: Kelpie Lawfuller"));
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Character Jelb}sample text{Character Jelb}",
            TaggedNoteCipherMode.Encrypt);

        var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: jelbHero);

        AssertEqual("{Character Jelb}sample text{Character Jelb}", decrypted, "matching character tag should decrypt note text");
        AssertThrows<UnauthorizedAccessException>(
            () => TaggedNoteCipherUtility.TransformTaggedText(encrypted, TaggedNoteCipherMode.Decrypt, hero: otherHero));
    }

    internal static void TaggedNoteCipherRejectsInferredFirstNameAlias()
    {
        var hero = HeroAccessContext.FromPartyHeroSheet(new PartyHeroSheet(
            Name: "Ari Stoneward",
            TokenImagePath: null,
            Level: "8",
            CharacterClass: "Fighter",
            HitPoints: "40",
            CharacterSheetText: "Name: Ari Stoneward"));
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Character Ari}The rival's ward answers Ari alone.{Character Ari}",
            TaggedNoteCipherMode.Encrypt);

        AssertThrows<UnauthorizedAccessException>(
            () => TaggedNoteCipherUtility.TransformTaggedText(
                encrypted,
                TaggedNoteCipherMode.Decrypt,
                hero: hero));
    }

    internal static void TaggedNoteCipherRejectsUnmetClassTag()
    {
        var hero = new HeroAccessContext(
            Level: 12,
            CharacterClass: "Illusionist",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Class paladin}Only paladins may read this vow.{Class paladin}",
            TaggedNoteCipherMode.Encrypt);

        AssertThrows<UnauthorizedAccessException>(
            () => TaggedNoteCipherUtility.TransformTaggedText(encrypted, TaggedNoteCipherMode.Decrypt, hero: hero));
    }

    internal static void TaggedNoteCipherAcceptsEitherOrAbilityTag()
    {
        var hero = new HeroAccessContext(
            Level: 4,
            CharacterClass: "Cleric",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Wisdom"] = 15
            });
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Level 6|Wis 15}The omen points east.{Level 6|Wis 15}",
            TaggedNoteCipherMode.Encrypt);
        var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: hero);

        AssertEqual("{Level 6|Wis 15}The omen points east.{Level 6|Wis 15}", decrypted, "either-or wisdom tag should decrypt note text");
    }

    internal static void TaggedNoteCipherAcceptsBareClassAlternative()
    {
        var hero = new HeroAccessContext(
            Level: 1,
            CharacterClass: "Wizard",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Wizard|Level 5}The sigil means danger.{Wizard|Level 5}",
            TaggedNoteCipherMode.Encrypt);
        var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: hero);

        AssertTrue(encrypted.StartsWith("{Wizard|Level 5}", StringComparison.Ordinal), "encrypted note should preserve bare class opening tag");
        AssertTrue(encrypted.EndsWith("{Wizard|Level 5}", StringComparison.Ordinal), "encrypted note should preserve bare class closing tag");
        AssertEqual("{Wizard|Level 5}The sigil means danger.{Wizard|Level 5}", decrypted, "bare class alternative should decrypt note text");
    }

    internal static void TaggedNoteCipherAcceptsClassLevelShorthandAndFactionTag()
    {
        var spyHero = new HeroAccessContext(
            Level: 4,
            CharacterClass: "Spy",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var factionHero = new HeroAccessContext(
            Level: 1,
            CharacterClass: "Fighter",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Faction"] = "Scyntarn"
            });
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Spy 4|Faction Scyntarn}Nimba is actually a witch, like her sister Nuanda.{Spy 4|Faction Scyntarn}",
            TaggedNoteCipherMode.Encrypt);

        var spyDecrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: spyHero);
        var factionDecrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: factionHero);

        AssertEqual(
            "{Spy 4|Faction Scyntarn}Nimba is actually a witch, like her sister Nuanda.{Spy 4|Faction Scyntarn}",
            spyDecrypted,
            "class level shorthand should decrypt note text");
        AssertEqual(spyDecrypted, factionDecrypted, "faction tag alternative should decrypt the same note text");
    }

    internal static void TaggedNoteCipherAcceptsGroupedAndExpressionTag()
    {
        const string taggedPlaintext = "{(Level 6 && Spy 3)|Scyntarn 9}The sealed paragraph opens.{(Level 6 && Spy 3)|Scyntarn 9}";
        var spyHero = new HeroAccessContext(
            Level: 6,
            CharacterClass: "Spy",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var scyntarnHero = new HeroAccessContext(
            Level: 1,
            CharacterClass: "Fighter",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            RankedMemberships: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Scyntarn"] = 9
            });
        var deniedHero = new HeroAccessContext(
            Level: 6,
            CharacterClass: "Fighter",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            RankedMemberships: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Scyntarn"] = 8
            });
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            taggedPlaintext,
            TaggedNoteCipherMode.Encrypt);

        var spyDecrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: spyHero);
        var scyntarnDecrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: scyntarnHero);

        AssertEqual(taggedPlaintext, spyDecrypted, "level and spy class-level branch should decrypt note text");
        AssertEqual(taggedPlaintext, scyntarnDecrypted, "ranked Scyntarn branch should decrypt note text");
        AssertThrows<UnauthorizedAccessException>(
            () => TaggedNoteCipherUtility.TransformTaggedText(encrypted, TaggedNoteCipherMode.Decrypt, hero: deniedHero));
    }

    internal static void TaggedNoteCipherReportsMismatchedDecryptTags()
    {
        var hero = new HeroAccessContext(
            Level: 8,
            CharacterClass: "Spy",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Level 8}The ward is real.{Level 8}",
            TaggedNoteCipherMode.Encrypt);
        var mismatched = encrypted[..^"{Level 8}".Length] + "{Level 9}";
        var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
            mismatched,
            TaggedNoteCipherMode.Decrypt,
            hero: hero);

        AssertEqual(
            "unable to decrypt due to non-matching opening and closing tags",
            decrypted,
            "mismatched opening and closing tags should return the player-safe decrypt failure text");
    }

    internal static void TaggedNoteCipherAuthenticatesVisibleTags()
    {
        var originalHero = new HeroAccessContext(
            Level: 8,
            CharacterClass: "Fighter",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var lowerLevelHero = originalHero with { Level = 7 };
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Level 8}The ward is real.{Level 8}",
            TaggedNoteCipherMode.Encrypt);
        var tampered = encrypted.Replace("{Level 8}", "{Level 7}", StringComparison.Ordinal);

        AssertThrows<InvalidOperationException>(
            () => TaggedNoteCipherUtility.TransformTaggedText(tampered, TaggedNoteCipherMode.Decrypt, hero: lowerLevelHero));
    }

    internal static void XpIdentityLookupRejectsDuplicateCanonicalNames()
    {
        var identity = XpAuthenticatedIdentity.Create(new XpCanonicalIdentityRecord("ari-stoneward", "Ari Stoneward", [], false ? XpIdentityRole.DungeonMaster : XpIdentityRole.Player));
        var duplicateCanonicalIds = new PcXpTotal[]
        {
            new("Ari Stoneward", 1200, "ari-stoneward"),
            new("Ari Valesong", 2400, "ari-stoneward")
        };
        var duplicateCanonicalNames = new PcXpTotal[]
        {
            new("Ari Stoneward", 1200),
            new("Ari Stoneward", 2400)
        };

        AssertTrue(
            XpTrackingUtility.FindXpTotalForIdentity(duplicateCanonicalIds, identity) is null,
            "duplicate canonical XP IDs must fail closed");
        AssertTrue(
            XpTrackingUtility.FindXpTotalForIdentity(duplicateCanonicalNames, identity) is null,
            "duplicate canonical XP names must fail closed during migration fallback");
    }

    internal static void XpDisplayStoresMultipleTotalsForDungeonMaster()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var totals = new PcXpTotal[]
            {
                new("Kelpie", 7062),
                new("Jelb", 8575)
            };

            InvokePrivateMethod(form, "ShowXpTotals", "As of 7.04.2026", totals);

            var storedTotals = (IReadOnlyList<PcXpTotal>)(GetPrivateField(form, "_xpTotals")
                ?? throw new InvalidOperationException("_xpTotals was null."));
            AssertEqual(2, storedTotals.Count, "Dungeon Master XP display should retain all requested totals");
            AssertEqual(new PcXpTotal("Kelpie", 7062), storedTotals[0], "unexpected first stored XP total");
            AssertTrue((bool)(GetPrivateField(form, "_showXpTotal") ?? false), "XP display should be active");
        });
    }

    internal static void XpTrackingParserReadsLatestTableTotals()
    {
        const string markdown =
            """
            ---
            status: XP
            ---
            As of 7.04.2026

            | Name     | Canonical ID | Class       | Level | XP Total |
            | -------- | ------------ | ----------- | ----- | -------- |
            | Kelpie   | kelpie       | Fighter     | 3     | 7,062    |
            | Jelb     | jelb         | Illusionist | 2     | 8,575    |
            | Max      | maximilian   | Theurge     | 1     | 3,175    |
            | Geoffroy | geoffroy     | Cleric      | 2     | 2,950    |

            As of 7.01.2026

            | Name     | Canonical ID | Class       | Level | XP Total |
            | -------- | ------------ | ----------- | ----- | -------- |
            | Kelpie   | kelpie       | Fighter     | 3     | 6,562    |
            | Jelb     | jelb         | Illusionist | 2     | 8,075    |
            """;

        var totals = XpTrackingUtility.ParseCurrentXpTotals(markdown).ToArray();

        AssertEqual(4, totals.Length, "expected latest XP table to contain four current PCs");
        AssertEqual(new PcXpTotal("Kelpie", 7062, "kelpie"), totals[0], "unexpected Kelpie XP total");
        AssertEqual(new PcXpTotal("Jelb", 8575, "jelb"), totals[1], "unexpected Jelb XP total");
        AssertEqual(new PcXpTotal("Max", 3175, "maximilian"), totals[2], "unexpected Max XP total");
        AssertEqual(new PcXpTotal("Geoffroy", 2950, "geoffroy"), totals[3], "unexpected Geoffroy XP total");
    }

    internal static void XpTrackingParserRejectsMissingLatestTable()
    {
        var exception = AssertThrows<InvalidOperationException>(() =>
            XpTrackingUtility.ParseCurrentXpTotals(
                """
                As of 7.04.2026

                No table today.
                """));

        AssertContains(exception.Message, "latest XP tracking date does not have a markdown table");
    }

    internal static void XpTrackingParserRejectsMissingOrDuplicateCanonicalIds()
    {
        var missing = AssertThrows<InvalidOperationException>(() =>
            XpTrackingUtility.ParseCurrentXpTotals(
                """
                As of 8.17.2026

                | Name | Class | XP Total |
                | ---- | ----- | -------- |
                | Ari Stoneward | Ranger | 2,000 |
                """));
        AssertContains(missing.Message, "latest XP tracking date does not have a markdown table");

        foreach (var invalidCanonicalId in new[] { "", "Ari-Stoneward", "ari stoneward" })
        {
            var invalid = AssertThrows<InvalidOperationException>(() =>
                XpTrackingUtility.ParseCurrentXpTotals(
                    $"""
                    As of 8.17.2026

                    | Name | Canonical ID | Class | XP Total |
                    | ---- | ------------ | ----- | -------- |
                    | Ari Stoneward | {invalidCanonicalId} | Ranger | 2,000 |
                    """));
            AssertContains(invalid.Message, "blank, invalid, or duplicate Canonical ID");
        }

        var duplicate = AssertThrows<InvalidOperationException>(() =>
            XpTrackingUtility.ParseCurrentXpTotals(
                """
                As of 8.17.2026

                | Name | Canonical ID | Class | XP Total |
                | ---- | ------------ | ----- | -------- |
                | Ari Stoneward | ari-stoneward | Ranger | 2,000 |
                | Ari Valesong | ari-stoneward | Bard | 4,000 |
                """));
        AssertContains(duplicate.Message, "blank, invalid, or duplicate Canonical ID");
    }

    internal static void XpTrackingFailureMessageHidesUrlAndDirectsPlayersToDm()
    {
        const string trackingUrl = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking";
        var message = XpTrackingUtility.FormatUserFacingFailureMessage(
            new InvalidOperationException($"XP tracking markdown could not be fetched from {trackingUrl}."));

        AssertContains(message, "XP totals could not be loaded from the XP Tracking page.");
        AssertContains(message, "Please contact the DM");
        AssertContains(message, "Technical detail:");
        AssertFalse(message.Contains(trackingUrl, StringComparison.Ordinal), "XP failure dialog should not expose the unlisted tracking URL");
        AssertFalse(message.Contains("https://", StringComparison.OrdinalIgnoreCase), "XP failure dialog should not expose URL-shaped text");
    }

    internal static void XpTrackingMissingPcMessageDirectsPlayersToDm()
    {
        var message = XpTrackingUtility.FormatMissingPcFailureMessage("Kelpie");

        AssertContains(message, "No XP total was found for 'Kelpie'.");
        AssertContains(message, "Please contact the DM");
        AssertFalse(message.Contains("https://", StringComparison.OrdinalIgnoreCase), "missing-PC message should not expose URL-shaped text");
    }

    internal static void IllusionistProgressionDataExposesXpThresholds()
    {
        var path = Path.Combine(GetRepositoryRoot(), "class-progression.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        AssertEqual(1, root.GetProperty("schema_version").GetInt32(), "unexpected class progression schema");

        var illusionist = root
            .GetProperty("classes")
            .GetProperty("illusionist");
        AssertEqual("Illusionist", illusionist.GetProperty("name").GetString() ?? string.Empty, "unexpected class name");
        AssertEqual(36, illusionist.GetProperty("maximum_level").GetInt32(), "unexpected Illusionist maximum level");
        AssertEqual(14, illusionist.GetProperty("published_maximum_level").GetInt32(), "unexpected published Illusionist maximum level");

        var extension = illusionist.GetProperty("extended_progression");
        AssertEqual(14, extension.GetProperty("starts_after_level").GetInt32(), "unexpected Illusionist extension starting level");
        AssertEqual(150000, extension.GetProperty("xp_per_additional_level").GetInt32(), "unexpected Illusionist extended XP increment");
        AssertFalse(extension.GetProperty("mechanical_statistics_available").GetBoolean(), "extended Illusionist mechanics should not be presented as published");

        var progression = illusionist.GetProperty("level_progression").EnumerateArray().ToArray();
        AssertEqual(36, progression.Length, "expected all Illusionist levels");
        var expectedThresholds = new[]
        {
            0, 2500, 5000, 10000, 20000, 40000, 80000,
            150000, 300000, 450000, 600000, 750000, 900000, 1050000,
            1200000, 1350000, 1500000, 1650000, 1800000, 1950000, 2100000,
            2250000, 2400000, 2550000, 2700000, 2850000, 3000000, 3150000,
            3300000, 3450000, 3600000, 3750000, 3900000, 4050000, 4200000,
            4350000
        };
        for (var index = 0; index < progression.Length; index++)
        {
            AssertEqual(index + 1, progression[index].GetProperty("level").GetInt32(), "unexpected Illusionist level");
            AssertEqual(
                expectedThresholds[index],
                progression[index].GetProperty("minimum_xp").GetInt32(),
                $"unexpected XP threshold for Illusionist level {index + 1}");
            if (index < 14)
            {
                AssertEqual(
                    6,
                    progression[index].GetProperty("spell_slots").GetArrayLength(),
                    $"unexpected spell-slot columns for Illusionist level {index + 1}");
            }
            else
            {
                AssertTrue(
                    progression[index].GetProperty("extrapolated").GetBoolean(),
                    $"Illusionist level {index + 1} should be marked as extrapolated");
                AssertFalse(
                    progression[index].TryGetProperty("spell_slots", out _),
                    $"Illusionist level {index + 1} should not invent unpublished spell slots");
                AssertFalse(
                    progression[index].TryGetProperty("hit_dice", out _),
                    $"Illusionist level {index + 1} should not invent unpublished hit dice");
                AssertFalse(
                    progression[index].TryGetProperty("thac0", out _),
                    $"Illusionist level {index + 1} should not invent unpublished THAC0");
                AssertFalse(
                    progression[index].TryGetProperty("saving_throws", out _),
                    $"Illusionist level {index + 1} should not invent unpublished saving throws");
            }
        }
    }

    private static void ResetRpolAuthFailureCache()
    {
        SetStaticField(typeof(RpolAuthUtility), "_cachedFatalAuthFailure", null);
        SetStaticField(typeof(RpolAuthUtility), "_cachedFatalAuthFailureLogged", false);
    }

    private static void WriteRpolStorageState(string storageStatePath, string contents, DateTimeOffset lastWriteUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storageStatePath)!);
        File.WriteAllText(storageStatePath, contents);
        File.SetLastWriteTimeUtc(storageStatePath, lastWriteUtc.UtcDateTime);
    }

    private static string CreateSampleRpolThreadHtml()
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

    internal static void RpolSnapshotSignsAndVerifiesCanonicalPayload()
    {
        var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var payload = RpolSnapshotUtility.CreatePayload(
            new Uri("https://rpol.net/game.php?gi=80170"),
            "<html>campaign</html>",
            "text/html; charset=utf-8",
            DateTimeOffset.Parse("2026-07-16T12:00:00Z"),
            signingKey);

        AssertTrue(RpolSnapshotUtility.VerifySignature(payload, signingKey), "snapshot signature should verify");
        AssertEqual(
            "<html>campaign</html>",
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload.ContentBase64)),
            "transport-wrapped snapshot content should round-trip");
        AssertFalse(
            System.Text.RegularExpressions.Regex.IsMatch(payload.ContentBase64, "[A-Za-z0-9+/]{4}"),
            "snapshot transport should not expose four-character base64 phrases to request filtering");
        AssertFalse(
            RpolSnapshotUtility.VerifySignature(payload with { ContentSha256 = new string('0', 64) }, signingKey),
            "tampered snapshot metadata should fail signature verification");
    }

    internal static void AdventureOutlineFillsEveryChapterThroughLatestIcChapter()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-1.html"),
            """
            <html><body>
            <h1>Ch 1 - Kirkilston.</h1>
            <span class="messageauthor">Dungeon Master</span>
            <div class="messagebody">The party leaves town.</div>
            </body></html>
            """);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-3.html"),
            """
            <html><body>
            <h1>Ch 3 - The Road.</h1>
            <span class="messageauthor">Kelpie</span>
            <div class="messagebody">Kelpie keeps watch while the party travels.</div>
            </body></html>
            """);

        var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
            .GetAwaiter()
            .GetResult();

        AssertContains(outline, "## Ch 1 - Kirkilston");
        AssertContains(outline, "## Ch 2");
        AssertContains(outline, "- The in-character chapter source is not available yet.");
        AssertContains(outline, "## Ch 3 - The Road");
        AssertTrue(
            outline.IndexOf("## Ch 1", StringComparison.Ordinal) < outline.IndexOf("## Ch 2", StringComparison.Ordinal)
                && outline.IndexOf("## Ch 2", StringComparison.Ordinal) < outline.IndexOf("## Ch 3", StringComparison.Ordinal),
            "adventure outline chapters should form a contiguous numeric range");
    }

    internal static void RpolSnapshotRejectsAnotherGame()
    {
        var exception = AssertThrows<InvalidOperationException>(() =>
            RpolSnapshotUtility.ValidateSourceUri(new Uri("https://rpol.net/game.php?gi=12345")));
        AssertContains(exception.Message, "80170");
    }

    internal static void RpolSnapshotAcceptsSanitizedCampaignContent()
    {
        var html = "<html><title>RPoL: World of Issenda - Scarlet Horizons</title><body>"
            + "<div class='threadstate'>Authenticated campaign thread list</div>"
            + "<a href='/game.php?gi=80170'>Campaign</a>"
            + new string('x', 1200)
            + "</body></html>";
        AssertTrue(RpolSnapshotUtility.IsUsableSnapshotHtml(html), "campaign HTML should be accepted after sanitization");
    }

    internal static void RpolSnapshotRejectsLoginOnlyContent()
    {
        var html = "<html><title>RPoL Login</title><body>" + new string('x', 1200)
            + "<form action='/login.cgi'><input name='username'><input name='password'></form></body></html>";
        AssertFalse(RpolSnapshotUtility.IsUsableSnapshotHtml(html), "login-only HTML should not be published");
    }

    internal static void RpolChallengeDetectionIgnoresPassiveCloudflareReferences()
    {
        AssertFalse(
            RpolAuthUtility.LooksLikeCloudflareChallengePage("<html><body>Protected by Cloudflare</body></html>"),
            "a passive Cloudflare reference should not be treated as a browser challenge");
        AssertTrue(
            RpolAuthUtility.LooksLikeCloudflareChallengePage("<title>Just a moment...</title>"),
            "a concrete Cloudflare challenge marker should still be detected");
    }

    internal static void RpolVerificationRecognizesAuthenticatedBrowserTitle()
    {
        AssertTrue(
            RpolAuthUtility.IsVerifiedRpolBrowserWindowTitle("RPoL: World of Issenda - Scarlet Horizons - Google Chrome"),
            "an authenticated RPOL window title should complete manual verification");
        AssertFalse(
            RpolAuthUtility.IsVerifiedRpolBrowserWindowTitle("Just a moment... - Google Chrome"),
            "a challenge window title should remain open");
    }

    internal static void RpolVerificationBrowserStartsWithCdpEnabled()
    {
        var arguments = RpolAuthUtility.CreateExternalBrowserVerificationArguments(
            54808,
            "C:/temp/rpol-profile",
            "C:/temp/rpol-notice.html");

        AssertTrue(
            arguments.Contains("--remote-debugging-port=54808", StringComparer.Ordinal),
            "the manually verified browser should expose CDP before verification starts");
        AssertTrue(
            arguments.Contains("--user-data-dir=C:/temp/rpol-profile", StringComparer.Ordinal),
            "the verified profile should remain isolated");
        AssertTrue(
            arguments.Contains(new Uri("C:/temp/rpol-notice.html").AbsoluteUri, StringComparer.Ordinal),
            "the verification browser should retain its local instructions page");
        AssertEqual(
            RpolProtectedResourceUtility.ProtectedDiceRollerUri.AbsoluteUri,
            arguments[^1],
            "the exact protected Dice Roller page should be the active tab used to detect completed verification");
    }

    internal static void RpolCredentialEntryUriRequiresExactTrustedOriginAndPath()
    {
        AssertTrue(
            NetworkUrlAllowlistUtility.IsTrustedRpolCredentialSubmissionUri(
                new Uri("https://rpol.net/game.php?gi=80170")),
            "the exact RPOL HTTPS game path should allow credential entry");
        AssertFalse(
            NetworkUrlAllowlistUtility.IsTrustedRpolCredentialSubmissionUri(
                new Uri("http://rpol.net/game.php?gi=80170")),
            "HTTP RPOL pages must not allow credential entry");
        AssertFalse(
            NetworkUrlAllowlistUtility.IsTrustedRpolCredentialSubmissionUri(
                new Uri("https://evil.example/game.php?next=rpol.net")),
            "lookalike hosts must not allow credential entry");
        AssertFalse(
            NetworkUrlAllowlistUtility.IsTrustedRpolCredentialSubmissionUri(
                new Uri("https://rpol.net/login.cgi?gi=80170")),
            "login action paths must not become credential-entry pages");
        AssertFalse(
            NetworkUrlAllowlistUtility.IsTrustedRpolCredentialSubmissionUri(
                new Uri("https://rpol.net/game.php/evil")),
            "unexpected RPOL paths must not allow credential entry");
    }

    internal static void RpolVerificationNavigationRequiresTrustedHttpsRpolPath()
    {
        AssertTrue(
            NetworkUrlAllowlistUtility.IsTrustedRpolNavigationUri(
                new Uri("https://rpol.net/game.php?gi=80170")),
            "the RPOL game page should be a trusted verification navigation");
        AssertTrue(
            NetworkUrlAllowlistUtility.IsTrustedRpolNavigationUri(
                new Uri("https://rpol.net/login.cgi")),
            "the RPOL login action should remain a trusted navigation target");
        AssertFalse(
            NetworkUrlAllowlistUtility.IsTrustedRpolNavigationUri(
                new Uri("http://rpol.net/game.php?gi=80170")),
            "HTTP verification navigation must be rejected");
        AssertFalse(
            NetworkUrlAllowlistUtility.IsTrustedRpolNavigationUri(
                new Uri("https://evil.example/?next=rpol.net/game.php")),
            "lookalike verification navigation must be rejected");
    }

    internal static void RpolWebViewCredentialFlowGuardsNavigationAndSubmission()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "RpolWebViewVerificationDialog.cs"))
            + Environment.NewLine
            + File.ReadAllText(Path.Combine(GetRepositoryRoot(), "RpolCredentialSubmissionScript.cs"));
        var navigationHandlerIndex = source.IndexOf("NavigationStarting +=", StringComparison.Ordinal);
        var navigationPolicyIndex = source.IndexOf("IsTrustedRpolNavigationUri", navigationHandlerIndex, StringComparison.Ordinal);
        var credentialPolicyIndex = source.IndexOf("TryValidateCredentialPage", navigationHandlerIndex, StringComparison.Ordinal);
        var scriptExecutionIndex = source.IndexOf("ExecuteScriptAsync", StringComparison.Ordinal);

        AssertTrue(navigationHandlerIndex >= 0, "RPOL WebView must handle navigation before page content is trusted");
        AssertTrue(
            navigationPolicyIndex > navigationHandlerIndex,
            "RPOL WebView navigation must use the exact trusted navigation policy");
        AssertTrue(
            credentialPolicyIndex > navigationHandlerIndex && credentialPolicyIndex < scriptExecutionIndex,
            "RPOL WebView must validate the live credential-entry URI before executing autofill script");
        AssertTrue(
            source.Contains("action.pathname === '/login.cgi'", StringComparison.Ordinal),
            "RPOL WebView autofill must validate the exact form action path before submission");
    }

    internal static void RpolVerificationConnectsOverCdpBeforeInspectingPageState()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "RpolAuthUtility.cs"));
        var connectIndex = source.IndexOf("browser = await ConnectToExternalBrowserAsync(", StringComparison.Ordinal);
        var inspectIndex = source.IndexOf("await WaitForExternalBrowserAuthenticationAsync(", StringComparison.Ordinal);
        var probeIndex = source.IndexOf("await VerifyAuthenticatedContextAsync(context, cancellationToken, page);", inspectIndex, StringComparison.Ordinal);
        var storageSaveIndex = source.IndexOf("await SaveStorageStateSecretAsync(", inspectIndex, StringComparison.Ordinal);

        AssertTrue(connectIndex >= 0, "external RPOL verification must connect over CDP");
        AssertTrue(inspectIndex > connectIndex,
            "external RPOL verification must connect over CDP before inspecting the RPOL page state");
        AssertTrue(probeIndex > inspectIndex,
            "external verification must prove protected access after CDP page inspection");
        AssertTrue(storageSaveIndex > inspectIndex,
            "RPOL storage state must not be captured before the external protected probe completes");
        AssertFalse(source.Contains("SubmitExternalBrowserLoginAsync(", StringComparison.Ordinal),
            "the headed external-browser path must not submit credentials against a dynamic login DOM");
        AssertTrue(source.Contains("IPage? suppliedPage = null", StringComparison.Ordinal),
            "the protected probe must be able to reuse the visible manual-verification page");
        AssertTrue(source.Contains("var ownsPage = suppliedPage is null", StringComparison.Ordinal),
            "owned protected-probe pages must still be cleaned up independently");
        AssertTrue(source.Contains("IsRetryableExternalAuthenticationFailure", StringComparison.Ordinal),
            "manual verification must retry only classified transient protected-probe failures");
        AssertTrue(source.Contains("Path.GetFileName(browserPath)", StringComparison.Ordinal),
            "external-browser cleanup must target the executable that was actually launched");

        var webViewSource = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "RpolWebViewVerificationDialog.cs"));
        AssertTrue(webViewSource.Contains("_webView.CoreWebView2.Source", StringComparison.Ordinal),
            "WebView verification must read the live CoreWebView2 source");
        AssertFalse(webViewSource.Contains("_webView.Source", StringComparison.Ordinal),
            "WebView verification must not use the stale WinForms wrapper source property");
    }

    internal static void RpolWebViewStateReplaysInHeadedBrowser()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "RpolAuthUtility.cs"));

        AssertTrue(
            source.Contains("_clearCloudflareChallengeWithHeadedBrowser = true", StringComparison.Ordinal),
            "RPOL authentication failures must schedule a headed browser retry for Cloudflare-compatible state");
        AssertTrue(
            source.Contains("CreateAuthenticatedSessionAsync(cancellationToken, clearCloudflareChallenge, lockOwner)", StringComparison.Ordinal)
                && source.Contains("LaunchRpolBrowserAsync(", StringComparison.Ordinal)
                && source.Contains("clearCloudflareChallenge", StringComparison.Ordinal),
            "the retry flag must select headed browser launch options without restarting WebView verification");
    }

    internal static void RpolSnapshotUploadJsonUsesBrokerCanonicalEscaping()
    {
        var payload = RpolSnapshotUtility.CreatePayload(
            new Uri("https://rpol.net/display.cgi?gi=80170&ti=18&msgpage=&show=all"),
            "<html><title>Scarlet Horizons</title><body>test</body></html>",
            "text/html; charset=utf-8",
            DateTimeOffset.Parse("2026-08-08T12:00:00Z"),
            Convert.ToBase64String(new byte[32]));

        var json = RpolSnapshotUtility.SerializePayloadForUpload(payload);

        AssertContains(json, "?gi=80170&ti=18&msgpage=&show=all");
        AssertFalse(
            json.Contains("\\u0026", StringComparison.OrdinalIgnoreCase),
            "snapshot JSON must use the same ampersand escaping as the broker's canonical body");
    }

    internal static void RpolDiceRollerNavigationUsesGamePageReferrer()
    {
        AssertTrue(
            string.Equals(
                AppSettingsUtility.GameForumUrl,
                RpolAuthUtility.GetNavigationReferer(
                    new Uri("https://rpol.net/usermodules/diceroller.cgi?gi=80170")),
                StringComparison.Ordinal),
            "Dice Roller navigation should carry the configured game page as its referrer");
        AssertTrue(
            RpolAuthUtility.GetNavigationReferer(
                new Uri("https://rpol.net/display.cgi?gi=80170&ti=7")) is null,
            "ordinary RPOL navigation should not receive a synthetic referrer");
    }

    internal static void SnapshotPublisherStateAdvancesOneTargetAndWraps()
    {
        var root = new Uri("https://rpol.net/game.php?gi=80170");
        var cast = new Uri("https://rpol.net/gameinfo.php?action=cast&gi=80170");
        var state = RpolSnapshotUtility.CreatePublisherState([root, cast]);

        AssertEqual(root, RpolSnapshotUtility.GetNextSourceUri(state), "the root should be the initial publisher target");
        state = RpolSnapshotUtility.AdvancePublisherState(state);
        AssertEqual(cast, RpolSnapshotUtility.GetNextSourceUri(state), "one success should advance exactly one target");
        state = RpolSnapshotUtility.AdvancePublisherState(state);
        AssertEqual(root, RpolSnapshotUtility.GetNextSourceUri(state), "the publisher queue should wrap after the last target");
    }

    internal static void RpolSnapshotFreshnessDetectsPossiblyStalePayloads()
    {
        var now = DateTimeOffset.Parse("2026-08-09T12:00:00Z");
        var current = new RpolSnapshotPayload(
            1,
            "80170",
            "https://rpol.net/game.php?gi=80170",
            now.AddMinutes(-59).ToString("O"),
            "text/html; charset=utf-8",
            new string('a', 64),
            "YQ==",
            "HMAC-SHA256",
            new string('b', 64));

        AssertFalse(
            RpolSnapshotUtility.IsPossiblyStale(current, now),
            "a snapshot newer than the startup freshness interval should remain current");
        AssertTrue(
            RpolSnapshotUtility.IsPossiblyStale(current with { FetchedAt = now.AddHours(-1).ToString("O") }, now),
            "a snapshot at the startup freshness boundary should be refreshed");
        AssertTrue(
            RpolSnapshotUtility.IsPossiblyStale(current with { FetchedAt = "not-a-timestamp" }, now),
            "an invalid snapshot timestamp should fail closed as possibly stale");
    }

    internal static void RpolSnapshotPreparationRejectsLoginPageBeforeSanitizing()
    {
        var loginHtml = "<html><title>RPoL Login</title><body>"
            + new string('x', 1200)
            + "<form action='/login.cgi'><input name='username'><input name='password'></form></body></html>";

        var exception = AssertThrows<InvalidOperationException>(() =>
            RpolSnapshotUtility.PrepareSnapshotHtml(loginHtml));

        AssertContains(exception.Message, "usable Scarlet Horizons");
    }

    internal static void RpolSnapshotPreparationAcceptsCampaignContentWithEmbeddedLoginForm()
    {
        var campaignHtml = "<html><title>View RPoL: World of Issenda - Scarlet Horizons - Chapter</title><body>"
            + "<article class='message'>Authenticated Scarlet Horizons campaign content.</article>"
            + "<a href='/display.cgi?gi=80170&amp;ti=23'>Campaign thread</a>"
            + new string('x', 1200)
            + "<form action='/login.cgi'><input name='username'><input name='password'></form></body></html>";

        var prepared = RpolSnapshotUtility.PrepareSnapshotHtml(campaignHtml);

        AssertTrue(RpolSnapshotUtility.IsUsableSnapshotHtml(prepared),
            "authenticated campaign pages should remain usable when RPOL includes its normal embedded login form");
        AssertContains(prepared, "Authenticated Scarlet Horizons campaign content.");
    }

    internal static void RpolSnapshotPreparationRejectsDisguisedLoginPage()
    {
        var disguisedLoginHtml = "<html><title>View RPoL: World of Issenda - Scarlet Horizons - Chapter</title><body>"
            + "<a href='/display.cgi?gi=80170&amp;ti=23'>Campaign thread</a>"
            + new string('x', 1200)
            + "<form action='/login.cgi'><input name='username'><input name='password'></form></body></html>";

        var exception = AssertThrows<InvalidOperationException>(() =>
            RpolSnapshotUtility.PrepareSnapshotHtml(disguisedLoginHtml));

        AssertContains(exception.Message, "usable Scarlet Horizons");
    }

    internal static void RpolSnapshotPreparationAcceptsCampaignPostsQuotingChallengeText()
    {
        var campaignHtml = "<html><title>View RPoL: World of Issenda - Scarlet Horizons - Chapter</title><body>"
            + new string('x', 1200)
            + "<a href='/display.cgi?gi=80170&amp;ti=23'>Campaign thread</a>"
            + "<article class='message'>A character quoted: Just a moment; Verify you are human; An Error Has Occurred; "
            + "You have encountered an error.</article></body></html>";

        var prepared = RpolSnapshotUtility.PrepareSnapshotHtml(campaignHtml);

        AssertContains(prepared, "Just a moment");
        AssertContains(prepared, "An Error Has Occurred");
    }

    internal static void RpolSnapshotProbeFailureRequiresRefresh()
    {
        var shouldRefresh = RpolSnapshotUtility.IsSnapshotRefreshRequiredAsync(
            _ => Task.FromException<RpolSnapshotPayload?>(
                new InvalidOperationException("invalid signed broker payload")),
            DateTimeOffset.Parse("2026-08-09T12:00:00Z"),
            CancellationToken.None).GetAwaiter().GetResult();

        AssertTrue(shouldRefresh, "a poisoned queued snapshot should be refreshed instead of blocking the cursor");
    }

    internal static void RpolSnapshotPublisherPreservesCallerCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        AssertFalse(
            RpolSnapshotUtility.ShouldHandlePublisherFailure(
                new OperationCanceledException(cancellationSource.Token),
                cancellationSource.Token),
            "caller cancellation must escape the publisher instead of becoming a failed report");
        AssertTrue(
            RpolSnapshotUtility.ShouldHandlePublisherFailure(
                new InvalidOperationException("refresh failed"),
                cancellationSource.Token),
            "ordinary publisher failures should still produce a failed report");
    }

    internal static void SnapshotDiscoveryMergePrioritizesNewTargetAndPreservesNormalizedQueue()
    {
        var legacy = new RpolSnapshotPublisherState(
            1,
            [
                "https://rpol.net/game.php?gi=80170",
                "https://rpol.net/display.cgi?gi=80170&ti=7&date=1779581880&msgpage=2",
                "https://rpol.net/usermodules/diceroller.cgi?gi=80170"
            ],
            1);
        var chapterSeven = new Uri("https://rpol.net/display.cgi?gi=80170&ti=23&msgpage=&show=all");

        var merged = RpolSnapshotUtility.MergeDiscoveredSourceUris(
            legacy,
            [new Uri("https://rpol.net/game.php?gi=80170"), chapterSeven]);

        AssertEqual(chapterSeven, RpolSnapshotUtility.GetNextSourceUri(merged), "a newly discovered target should be refreshed first");
        AssertTrue(
            merged.SourceUrls.Contains("https://rpol.net/display.cgi?gi=80170&ti=7&msgpage=&show=all"),
            "the existing cursor target should remain normalized in the merged queue");
    }

    internal static void SnapshotDiscoveryAllowsEmbeddedLoginFormOnCampaignRoot()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "RpolSnapshotUtility.cs"));
        var discoveryStart = source.IndexOf(
            "internal static async Task<RpolSnapshotDiscovery> DiscoverSourceUrisAsync",
            StringComparison.Ordinal);
        var discoveryEnd = source.IndexOf("private static", discoveryStart + 1, StringComparison.Ordinal);
        var discoverySource = source[discoveryStart..discoveryEnd];

        AssertContains(discoverySource, "RpolAuthUtility.GetSnapshotResponseAsync(rootUri");
        AssertFalse(discoverySource.Contains("RpolAuthUtility.GetHtmlFromUrlAsync(rootUri", StringComparison.Ordinal),
            "snapshot discovery must not reject the authenticated campaign root merely because RPOL embeds its login form");
    }

    internal static void SnapshotBrokerUnavailableFallsBackToDirectRpol()
    {
        AssertTrue(
            HtmlUtility.ShouldFallbackToDirectRpol(new InvalidOperationException("The RPOL snapshot broker returned HTTP 410 for 'https://rpol.net/game.php?gi=80170'.")),
            "an unavailable snapshot broker response should permit direct RPOL refresh");
        AssertFalse(
            HtmlUtility.ShouldFallbackToDirectRpol(new InvalidOperationException("The RPOL snapshot broker rejected the signed payload.")),
            "a broker integrity failure must not silently fall back");
    }

    internal static void GameForumStartupChecksSnapshotsBeforeDownloads()
    {
        var phases = new List<string>();
        var shouldStartCrawler = Form1.RunGameForumStartupAsync(
            _ =>
            {
                phases.Add("snapshot check");
                return Task.CompletedTask;
            },
            _ =>
            {
                phases.Add("post downloads");
                return Task.FromResult(true);
            },
            CancellationToken.None).GetAwaiter().GetResult();

        AssertEqual("snapshot check,post downloads", string.Join(',', phases), "startup should check snapshots before downloading posts");
        AssertTrue(shouldStartCrawler, "successful post downloads should still start keyword indexing");
    }

    internal static void SnapshotDiscoveryApprovesGameLinksAndDiceRoller()
    {
        var gameLinksApproved = (bool)(InvokeStaticMethod(
            typeof(RpolSnapshotUtility),
            "IsApprovedLinkLabel",
            "Game Links") ?? false);
        var diceRollerApproved = (bool)(InvokeStaticMethod(
            typeof(RpolSnapshotUtility),
            "IsApprovedLinkLabel",
            "Die Roller") ?? false);
        var unrelated = (bool)(InvokeStaticMethod(
            typeof(RpolSnapshotUtility),
            "IsApprovedLinkLabel",
            "Edit Game") ?? true);

        AssertTrue(gameLinksApproved, "Game Links should be included in snapshot discovery");
        AssertTrue(diceRollerApproved, "Die Roller should be included in snapshot discovery");
        AssertFalse(unrelated, "unrelated game administration links should remain excluded");
    }

    internal static void SnapshotPublisherNormalizesThreadTargetsToShowAll()
    {
        var state = RpolSnapshotUtility.CreatePublisherState(
        [
            new Uri("https://rpol.net/game.php?gi=80170"),
            new Uri("https://rpol.net/display.cgi?gi=80170&ti=7&date=1779581880&msgpage=2")
        ]);

        AssertEqual(
            "https://rpol.net/display.cgi?gi=80170&ti=7&msgpage=&show=all",
            state.SourceUrls[1],
            "new publisher state should normalize thread targets");

        var legacyState = new RpolSnapshotPublisherState(
            1,
            [
                "https://rpol.net/game.php?gi=80170",
                "https://rpol.net/display.cgi?gi=80170&ti=7&date=1779581880&msgpage=2",
                "https://rpol.net/usermodules/diceroller.cgi?gi=80170"
            ],
            1);
        var normalized = RpolSnapshotUtility.EnsureRequiredSourceUris(legacyState);

        AssertEqual(
            "https://rpol.net/display.cgi?gi=80170&ti=7&msgpage=&show=all",
            normalized.SourceUrls[normalized.NextIndex],
            "legacy publisher state should normalize its current thread without losing the cursor");
    }

    internal static void SnapshotPublisherStateInjectsRequiredDiceRollerTarget()
    {
        var root = new Uri("https://rpol.net/game.php?gi=80170");
        var cast = new Uri("https://rpol.net/gameinfo.php?action=cast&gi=80170");
        var state = RpolSnapshotUtility.AdvancePublisherState(
            RpolSnapshotUtility.CreatePublisherState([root, cast]));

        var updated = RpolSnapshotUtility.EnsureRequiredSourceUris(state);

        AssertEqual(3, updated.SourceUrls.Count, "the required Dice Roller target should be inserted once");
        AssertEqual(
            "https://rpol.net/usermodules/diceroller.cgi?gi=80170",
            updated.SourceUrls[updated.NextIndex],
            "the newly required target should be the next publisher item");
        AssertEqual(cast.AbsoluteUri, updated.SourceUrls[updated.NextIndex + 1], "the previous next target should be preserved");
        AssertTrue(
            ReferenceEquals(updated, RpolSnapshotUtility.EnsureRequiredSourceUris(updated)),
            "a current publisher queue should not be rewritten");
    }

    internal static void SnapshotPublisherStatePersistsItsCursor()
    {
        using var directory = TemporaryDirectory.Create();
        var statePath = Path.Combine(directory.Path, "publisher-state.json");
        var state = RpolSnapshotUtility.AdvancePublisherState(
            RpolSnapshotUtility.CreatePublisherState(
            [
                new Uri("https://rpol.net/game.php?gi=80170"),
                new Uri("https://rpol.net/gameinfo.php?action=cast&gi=80170")
            ]));

        RpolSnapshotUtility.SavePublisherStateAsync(statePath, state).GetAwaiter().GetResult();
        var loaded = RpolSnapshotUtility.LoadPublisherState(statePath)
            ?? throw new InvalidOperationException("expected persisted publisher state");

        AssertEqual(1, loaded.NextIndex, "the persisted publisher cursor should be retained");
        AssertEqual(state.SourceUrls[1], loaded.SourceUrls[1], "the persisted publisher queue should be retained");
    }

    internal static void SnapshotPublisherStateRejectsInvalidCursor()
    {
        var state = new RpolSnapshotPublisherState(
            1,
            ["https://rpol.net/game.php?gi=80170"],
            1);

        var exception = AssertThrows<InvalidOperationException>(() => RpolSnapshotUtility.GetNextSourceUri(state));
        AssertContains(exception.Message, "cursor");
    }

    internal static void SnapshotPublisherArgumentIsRecognized()
    {
        AssertTrue(PlayerAssistant.Program.IsPublishRpolSnapshotsArgument("--publish-rpol-snapshots"), "long snapshot argument should be recognized");
        AssertTrue(PlayerAssistant.Program.IsPublishRpolSnapshotsArgument("/publish-rpol-snapshots"), "slash snapshot argument should be recognized");
    }

}
