using System.Diagnostics;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace PlayerAssistant
{
    internal sealed record RpolResponse(byte[] Body, string? ContentType);

    internal enum RpolAuthFailureKind
    {
        MissingCredentials,
        PlaywrightUnavailable,
        TransportSecurityFailure,
        LoginRejected,
        AuthSessionExpired,
        CloudflareChallenge,
        RpolBlocked,
        UntrustedNavigation,
        UnexpectedProtectedContent,
        RemoteUnavailable
    }

    internal sealed class RpolAuthException : InvalidOperationException
    {
        public RpolAuthException(RpolAuthFailureKind kind, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Kind = kind;
        }

        public RpolAuthFailureKind Kind { get; }
    }

    internal static class RpolAuthUtility
    {
        private const string DesktopChromeUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36";
        private static readonly TimeSpan StorageStateMaxAge = TimeSpan.FromDays(30);
        private static readonly TimeSpan PlaywrightOperationTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan RpolNavigationAttemptInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan CloudflareClearancePollInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan CloudflareClearanceMaxWait = TimeSpan.FromMinutes(5);
        private const string ExternalBrowserVerificationDirectoryName = "rpol-browser-verification";
        private static readonly SemaphoreSlim SessionSemaphore = new(1, 1);
        private static readonly SemaphoreSlim NavigationSemaphore = new(1, 1);
        private static RpolBrowserSession? _session;
        private static RpolAuthException? _cachedFatalAuthFailure;
        private static bool _cachedFatalAuthFailureLogged;
        private static bool _processExitRegistered;
        private static bool _clearCloudflareChallengeWithHeadedBrowser;
        private static DateTimeOffset? _lastNavigationAttemptUtc;
        private static readonly ConcurrentDictionary<Task, byte> LatePlaywrightCleanupTasks = new();

        internal static Func<RpolWebViewVerificationRequest, CancellationToken, Task<string?>>? WebViewVerificationHandler { get; set; }
        internal static Func<Uri, CancellationToken, RpolCrossProcessLock, Task<RpolResponse>>? SnapshotResponseOverrideForTests { get; set; }
        internal static Func<string, CancellationToken, RpolCrossProcessLock, Task>? PersistVerifiedStorageStateJsonOverrideForTests { get; set; }

        internal static Uri ProtectedDiceRollerUri => RpolProtectedResourceUtility.ProtectedDiceRollerUri;

        internal static RpolProtectedResourceClassification ClassifyProtectedResource(
            Uri requestedUri,
            Uri? finalUri,
            int? statusCode,
            string? contentType,
            string? html,
            string? challengeMarkers = null)
        {
            return RpolProtectedResourceUtility.Classify(
                requestedUri,
                finalUri,
                statusCode,
                contentType,
                html,
                challengeMarkers);
        }

        internal static RpolProtectedResourceClassification ClassifyProtectedResource(
            Uri requestedUri,
            Uri? responseUri,
            Uri? settledUri,
            int? statusCode,
            string? contentType,
            string? html,
            string? challengeMarkers = null)
        {
            return RpolProtectedResourceUtility.Classify(
                requestedUri,
                responseUri,
                settledUri,
                statusCode,
                contentType,
                html,
                challengeMarkers);
        }

        public static bool IsRpolUri(Uri uri)
        {
            return NetworkUrlAllowlistUtility.IsRpolHost(uri);
        }

        public static async Task<string> GetHtmlFromUrlAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NetworkUrlAllowlistUtility.EnsureAllowed(uri, NetworkUrlPurpose.Rpol);
            ThrowIfCachedFatalAuthFailure();

            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var context = await GetAuthenticatedContextAsync(cancellationToken);
                    var html = await GetPageHtmlAsync(context, uri, cancellationToken);
                    if (LooksLikeLoginPage(html))
                    {
                        await ResetSessionAsync(cancellationToken);
                        if (attempt == 0)
                        {
                            _clearCloudflareChallengeWithHeadedBrowser = true;
                        }

                        continue;
                    }

                    return html;
                }
                catch (RpolAuthException ex) when (ShouldRetryWithHeadedBrowser(ex, attempt))
                {
                    await ResetSessionAsync(cancellationToken);
                    _clearCloudflareChallengeWithHeadedBrowser = true;
                    continue;
                }
                catch (Exception ex) when (TryCacheFatalAuthFailure(ex, out var cachedException))
                {
                    throw cachedException;
                }
            }

            throw CacheFatalAuthFailure(new RpolAuthException(
                RpolAuthFailureKind.AuthSessionExpired,
                $"RPoL returned a login page after authenticated navigation to '{uri}'. The stored auth state may have expired or the account may no longer have access."));
        }

        public static async Task<RpolResponse> GetResponseAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            return await GetResponseAsync(uri, allowEmbeddedLoginForm: false, cancellationToken);
        }

        internal static async Task<RpolResponse> GetSnapshotResponseAsync(
            Uri uri,
            CancellationToken cancellationToken,
            RpolCrossProcessLock lockOwner)
        {
            ArgumentNullException.ThrowIfNull(lockOwner);
            if (SnapshotResponseOverrideForTests is not null)
            {
                return await SnapshotResponseOverrideForTests(uri, cancellationToken, lockOwner);
            }
            return await GetResponseAsync(uri, allowEmbeddedLoginForm: true, cancellationToken, lockOwner);
        }

        private static async Task<RpolResponse> GetResponseAsync(
            Uri uri,
            bool allowEmbeddedLoginForm,
            CancellationToken cancellationToken,
            RpolCrossProcessLock? lockOwner = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NetworkUrlAllowlistUtility.EnsureAllowed(uri, NetworkUrlPurpose.Rpol);
            ThrowIfCachedFatalAuthFailure();

            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var context = await GetAuthenticatedContextAsync(cancellationToken, lockOwner);
                    var response = await GetPageResponseAsync(context, uri, cancellationToken);

                    if (ShouldTreatResponseAsLogin(
                        response.ContentType,
                        response.Body,
                        allowEmbeddedLoginForm))
                    {
                        await ResetSessionAsync(cancellationToken);
                        if (attempt == 0)
                        {
                            _clearCloudflareChallengeWithHeadedBrowser = true;
                        }

                        continue;
                    }

                    return response;
                }
                catch (RpolAuthException ex) when (ShouldRetryWithHeadedBrowser(ex, attempt))
                {
                    await ResetSessionAsync(cancellationToken);
                    _clearCloudflareChallengeWithHeadedBrowser = true;
                    continue;
                }
                catch (Exception ex) when (TryCacheFatalAuthFailure(ex, out var cachedException))
                {
                    throw cachedException;
                }
            }

            throw CacheFatalAuthFailure(new RpolAuthException(
                RpolAuthFailureKind.AuthSessionExpired,
                $"RPoL returned a login page after authenticated navigation to '{uri}'. The stored auth state may have expired or the account may no longer have access."));
        }

        private static async Task<IBrowserContext> GetAuthenticatedContextAsync(
            CancellationToken cancellationToken,
            RpolCrossProcessLock? lockOwner = null)
        {
            await SessionSemaphore.WaitAsync(cancellationToken);
            try
            {
                ThrowIfCachedFatalAuthFailure();
                RegisterProcessExitHandler();

                if (_session is null)
                {
                    var clearCloudflareChallenge = _clearCloudflareChallengeWithHeadedBrowser;
                    _clearCloudflareChallengeWithHeadedBrowser = false;
                    _session = await CreateAuthenticatedSessionAsync(cancellationToken, clearCloudflareChallenge, lockOwner);
                }

                return _session.Context;
            }
            finally
            {
                SessionSemaphore.Release();
            }
        }

        private static async Task<RpolBrowserSession> CreateAuthenticatedSessionAsync(
            CancellationToken cancellationToken,
            bool clearCloudflareChallenge = false,
            RpolCrossProcessLock? lockOwner = null)
        {
            var (userName, password) = GetCredentials();
            Environment.SetEnvironmentVariable("NODE_OPTIONS", "--use-system-ca");
            IPlaywright playwright;
            IBrowser browser;
            bool useDefaultUserAgent;
            try
            {
                playwright = await WaitForPlaywrightAsync(
                    Playwright.CreateAsync(),
                    "starting the RPOL browser session",
                    cancellationToken);

                if (clearCloudflareChallenge)
                {
                    if (WebViewVerificationHandler is not null)
                    {
                        var storageStateJson = await WebViewVerificationHandler(
                            new RpolWebViewVerificationRequest(
                                AppSettingsUtility.GameForumUrl,
                                userName,
                                password,
                                CloudflareClearanceMaxWait),
                            cancellationToken);
                        if (string.IsNullOrWhiteSpace(storageStateJson))
                        {
                            throw new RpolAuthException(
                                RpolAuthFailureKind.CloudflareChallenge,
                                "RPOL browser verification did not provide a usable browser state.");
                        }

                        await PersistStorageStateAsync(storageStateJson, cancellationToken, lockOwner, null);
                        playwright.Dispose();
                        return await CreateAuthenticatedSessionAsync(cancellationToken, lockOwner: lockOwner);
                    }

                    return await RefreshStorageStateWithExternalBrowserAsync(
                        playwright,
                        userName,
                        password,
                        cancellationToken,
                        lockOwner);
                }

                var browserLaunch = await LaunchRpolBrowserAsync(
                    playwright,
                    clearCloudflareChallenge,
                    cancellationToken);
                browser = browserLaunch.Browser;
                useDefaultUserAgent = browserLaunch.UseDefaultUserAgent;
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or IOException)
            {
                throw new RpolAuthException(
                    RpolAuthFailureKind.PlaywrightUnavailable,
                    $"RPOL browser authentication is unavailable while starting Playwright: {ex.Message}",
                    ex);
            }

            try
            {
                var preparedStorageStatePath = TryCreatePreparedStorageStateFile();
                IBrowserContext context;
                try
                {
                    var contextOptions = CreateBrowserContextOptions(
                        preparedStorageStatePath,
                        useDefaultUserAgent);
                    context = await WaitForPlaywrightAsync(
                        browser.NewContextAsync(contextOptions),
                        "creating the RPOL browser context",
                        cancellationToken);
                }
                finally
                {
                    DeleteTemporaryStorageStateFile(preparedStorageStatePath);
                }

                await WaitForPlaywrightAsync(
                    context.AddInitScriptAsync("""
                    Object.defineProperty(navigator, 'webdriver', {
                        get: () => undefined
                    });
                    """),
                    "configuring the RPOL browser context",
                    cancellationToken);
                if (clearCloudflareChallenge)
                {
                    await AddCloudflareClearanceNoticeScriptAsync(context, cancellationToken);
                }

                await EnsureLoggedInAsync(context, userName, password, clearCloudflareChallenge, cancellationToken);
                await VerifyAuthenticatedContextAsync(context, cancellationToken);
                await SaveStorageStateSecretAsync(context, cancellationToken, lockOwner);
                if (clearCloudflareChallenge)
                {
                    await context.CloseAsync();
                    await browser.CloseAsync();
                    playwright.Dispose();
                    return await CreateAuthenticatedSessionAsync(cancellationToken, lockOwner: lockOwner);
                }

                return new RpolBrowserSession(playwright, browser, context);
            }
            catch
            {
                await browser.CloseAsync();
                playwright.Dispose();
                throw;
            }
        }

        private static async Task<RpolBrowserLaunch> LaunchRpolBrowserAsync(
            IPlaywright playwright,
            bool clearCloudflareChallenge,
            CancellationToken cancellationToken)
        {
            foreach (var launchOptions in CreateRpolBrowserLaunchOptions(clearCloudflareChallenge))
            {
                try
                {
                    var browser = await WaitForPlaywrightAsync(
                        playwright.Chromium.LaunchAsync(launchOptions),
                        GetBrowserLaunchDescription(launchOptions),
                        cancellationToken);
                    return new RpolBrowserLaunch(browser, UseDefaultUserAgent: !string.IsNullOrWhiteSpace(launchOptions.Channel));
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException or IOException)
                {
                }
            }

            throw new RpolAuthException(
                RpolAuthFailureKind.PlaywrightUnavailable,
                "RPOL browser authentication could not launch installed Microsoft Edge, installed Google Chrome, or Playwright Chromium.");
        }

        private static BrowserTypeLaunchOptions[] CreateRpolBrowserLaunchOptions(bool clearCloudflareChallenge)
        {
            var headless = !clearCloudflareChallenge;
            return
            [
                CreateRpolBrowserLaunchOption("msedge", headless),
                CreateRpolBrowserLaunchOption("chrome", headless),
                CreateRpolBrowserLaunchOption(channel: null, headless)
            ];
        }

        private static BrowserTypeLaunchOptions CreateRpolBrowserLaunchOption(string? channel, bool headless)
        {
            return new BrowserTypeLaunchOptions
            {
                Channel = channel,
                Headless = headless,
                Args = ["--disable-blink-features=AutomationControlled"]
            };
        }

        private static string GetBrowserLaunchDescription(BrowserTypeLaunchOptions launchOptions)
        {
            return string.IsNullOrWhiteSpace(launchOptions.Channel)
                ? "launching the RPOL browser"
                : $"launching the RPOL browser ({launchOptions.Channel})";
        }

        private static BrowserNewContextOptions CreateBrowserContextOptions(
            string? storageStatePath,
            bool useDefaultUserAgent)
        {
            var contextOptions = new BrowserNewContextOptions
            {
                Locale = "en-US",
                TimezoneId = "America/Chicago",
                ViewportSize = new ViewportSize
                {
                    Width = 1365,
                    Height = 768
                },
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    ["Accept-Language"] = "en-US,en;q=0.9"
                }
            };

            if (storageStatePath is not null)
            {
                contextOptions.StorageStatePath = storageStatePath;
            }

            if (!useDefaultUserAgent)
            {
                contextOptions.UserAgent = DesktopChromeUserAgent;
            }

            return contextOptions;
        }

        private static async Task<RpolBrowserSession> RefreshStorageStateWithExternalBrowserAsync(
            IPlaywright playwright,
            string userName,
            string password,
            CancellationToken cancellationToken,
            RpolCrossProcessLock? lockOwner = null)
        {
            var browserPath = FindExternalBrowserExecutable()
                ?? throw new RpolAuthException(
                    RpolAuthFailureKind.PlaywrightUnavailable,
                    "RPOL browser verification requires installed Chrome or Microsoft Edge, but neither browser executable was found.");
            var tempDirectory = RuntimePathUtility.GetUserDataPath("temp");
            Directory.CreateDirectory(tempDirectory);
            var staleProfileCleanupErrors = RpolExternalProfileCleanup.ScavengeStaleProfiles(
                tempDirectory,
                DateTimeOffset.UtcNow.AddHours(-1));
            if (staleProfileCleanupErrors.Count > 0)
            {
                throw new AggregateException(
                    "RPOL stale external verification profile cleanup was incomplete.",
                    staleProfileCleanupErrors);
            }
            var profileDirectory = RuntimePathUtility.CombineUnderBase(
                tempDirectory,
                $"{ExternalBrowserVerificationDirectoryName}-{Guid.NewGuid():N}");
            var noticePath = RuntimePathUtility.CombineUnderBase(
                tempDirectory,
                $"rpol-browser-verification-{Guid.NewGuid():N}.html");
            Directory.CreateDirectory(profileDirectory);
            var remoteDebuggingPort = GetAvailableLoopbackPort();

            IBrowser? browser = null;
            Process? verificationProcess = null;
            RpolExternalProfileLease? profileLease = null;
            try
            {
                profileLease = RpolExternalProfileCleanup.Acquire(profileDirectory);
                File.WriteAllText(noticePath, CreateExternalBrowserNoticeHtml(), Encoding.UTF8);
                verificationProcess = StartExternalBrowserForManualVerification(
                    browserPath,
                    remoteDebuggingPort,
                    profileDirectory,
                    noticePath);
                StartupLoggingUtility.Append("RPOL authentication stage", "stage=external_browser_started");
                browser = await ConnectToExternalBrowserAsync(
                    playwright,
                    remoteDebuggingPort,
                    cancellationToken);
                StartupLoggingUtility.Append("RPOL authentication stage", "stage=cdp_connected");
                var context = browser.Contexts.FirstOrDefault()
                    ?? throw new RpolAuthException(
                        RpolAuthFailureKind.PlaywrightUnavailable,
                        "RPOL browser verification could not access the external browser context.");

                await CompleteExternalBrowserVerificationAsync(
                    context,
                    userName,
                    password,
                    cancellationToken);
                await SaveStorageStateSecretAsync(
                    context,
                    cancellationToken,
                    lockOwner,
                    $"http://127.0.0.1:{remoteDebuggingPort}");
                StartupLoggingUtility.Append("RPOL authentication stage", "stage=state_persisted");
                DeleteTemporaryStorageStateFile(noticePath);
                return new RpolBrowserSession(
                    playwright,
                    browser,
                    context,
                    verificationProcess,
                    profileDirectory,
                    profileLease);
            }
            catch (Exception ex)
            {
                var cleanupErrors = new List<Exception>();
                if (browser is not null)
                {
                    cleanupErrors.AddRange(await RpolCleanupUtility.DisposeAsyncIndependently(
                        cancellationToken,
                        ("external browser", () => browser.CloseAsync())));
                }
                if (verificationProcess is not null)
                {
                    cleanupErrors.AddRange(RpolCleanupUtility.DisposeIndependently(
                        ("external browser process", () =>
                        {
                            if (!verificationProcess.HasExited)
                            {
                                verificationProcess.Kill(entireProcessTree: true);
                                if (!verificationProcess.WaitForExit(5000))
                                {
                                    throw new TimeoutException("The external RPOL browser did not exit within the cleanup bound.");
                                }
                            }
                        }
                    ),
                        ("external browser process handle", verificationProcess.Dispose)));
                }
                cleanupErrors.AddRange(RpolCleanupUtility.DisposeIndependently(
                    ("external browser notice", () => DeleteTemporaryStorageStateFile(noticePath)),
                    ("external browser process tree", () => TerminateExternalBrowserProcessesForProfile(profileDirectory)),
                    ("external browser profile", profileLease is not null
                        ? profileLease.Dispose
                        : () => RpolExternalProfileCleanup.CleanupProfile(profileDirectory))));
                if (cleanupErrors.Count > 0)
                {
                    throw new AggregateException(
                        "RPOL external-browser authentication failed and cleanup was incomplete.",
                        new[] { ex }.Concat(cleanupErrors));
                }
                ExceptionDispatchInfo.Capture(ex).Throw();
                throw;
            }
        }

        internal static bool IsVerifiedRpolBrowserWindowTitle(string? title)
        {
            return !string.IsNullOrWhiteSpace(title)
                && title.StartsWith("RPoL:", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<IBrowser> ConnectToExternalBrowserAsync(
            IPlaywright playwright,
            int remoteDebuggingPort,
            CancellationToken cancellationToken)
        {
            var endpoint = $"http://127.0.0.1:{remoteDebuggingPort}";
            var startedAt = DateTimeOffset.UtcNow;
            Exception? lastException = null;
            while (DateTimeOffset.UtcNow - startedAt < PlaywrightOperationTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await WaitForPlaywrightAsync(
                        playwright.Chromium.ConnectOverCDPAsync(endpoint),
                        "connecting to the external RPOL browser",
                        cancellationToken);
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException or IOException)
                {
                    lastException = ex;
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                }
            }

            throw new RpolAuthException(
                RpolAuthFailureKind.PlaywrightUnavailable,
                $"RPOL browser verification could not connect to the external browser: {lastException?.Message ?? "connection timed out"}.",
                lastException);
        }

        private static async Task CompleteExternalBrowserVerificationAsync(
            IBrowserContext context,
            string userName,
            string password,
            CancellationToken cancellationToken)
        {
            var gameForumUri = new Uri(AppSettingsUtility.GameForumUrl);
            var page = await GetExternalRpolPageAsync(context, gameForumUri, cancellationToken);
            var startedAt = DateTimeOffset.UtcNow;
            var loginSubmissionCount = 0;

            while (DateTimeOffset.UtcNow - startedAt < CloudflareClearanceMaxWait)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (page.IsClosed)
                    {
                        page = await GetExternalRpolPageAsync(context, gameForumUri, cancellationToken);
                    }

                    var html = await WaitForPlaywrightAsync(
                        page.ContentAsync(),
                        "checking the external RPOL browser page",
                        cancellationToken);
                    if (await HasLoginFormAsync(page, cancellationToken))
                    {
                        if (ShouldAwaitManualExternalLogin(loginSubmissionCount))
                        {
                            await Task.Delay(CloudflareClearancePollInterval, cancellationToken);
                            continue;
                        }

                        await SubmitExternalBrowserLoginAsync(page, userName, password, cancellationToken);
                        loginSubmissionCount++;
                        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                        continue;
                    }

                    if (LooksLikeCloudflareChallengePage(html))
                    {
                        await Task.Delay(CloudflareClearancePollInterval, cancellationToken);
                        continue;
                    }

                    if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var currentUri)
                        || !NetworkUrlAllowlistUtility.IsTrustedRpolNavigationUri(currentUri))
                    {
                        await WaitForPlaywrightAsync(
                            page.GotoAsync(gameForumUri.ToString(), new PageGotoOptions
                            {
                                WaitUntil = WaitUntilState.DOMContentLoaded,
                                Timeout = (float)PlaywrightOperationTimeout.TotalMilliseconds
                            }),
                            $"loading '{gameForumUri}' in the external RPOL browser",
                            cancellationToken);
                        continue;
                    }

                    try
                    {
                        await VerifyAuthenticatedContextAsync(context, cancellationToken);
                        return;
                    }
                    catch (RpolAuthException ex) when (ex.Kind == RpolAuthFailureKind.LoginRejected
                        || ex.Kind == RpolAuthFailureKind.AuthSessionExpired)
                    {
                        if (ShouldAwaitManualExternalLogin(loginSubmissionCount))
                        {
                            await Task.Delay(CloudflareClearancePollInterval, cancellationToken);
                            continue;
                        }

                        page = await GetExternalRpolPageAsync(context, gameForumUri, cancellationToken);
                        await SubmitExternalBrowserLoginAsync(page, userName, password, cancellationToken);
                        loginSubmissionCount++;
                        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                    }
                    catch (RpolAuthException ex) when (ex.Kind == RpolAuthFailureKind.CloudflareChallenge)
                    {
                        await Task.Delay(CloudflareClearancePollInterval, cancellationToken);
                    }
                }
                catch (PlaywrightException ex) when (IsBrowserClosedException(ex))
                {
                    throw new RpolAuthException(
                        RpolAuthFailureKind.CloudflareChallenge,
                        "RPOL browser verification was cancelled because the temporary browser window was closed before verification completed.",
                        ex);
                }

                await Task.Delay(CloudflareClearancePollInterval, cancellationToken);
            }

            throw new RpolAuthException(
                RpolAuthFailureKind.CloudflareChallenge,
                $"RPOL browser verification did not complete within {CloudflareClearanceMaxWait.TotalMinutes:0} minutes. Complete the checkbox in the temporary Chrome or Edge window, keep that window open, and let Player Assistant finish saving the RPOL browser state.");
        }

        private static async Task<RpolProtectedResourceClassification> VerifyAuthenticatedContextAsync(
            IBrowserContext context,
            CancellationToken cancellationToken)
        {
            var startedAt = DateTimeOffset.UtcNow;
            RpolProtectedResourceKind? classificationKind = null;
            var page = await WaitForPlaywrightAsync(
                context.NewPageAsync(),
                "opening the RPOL protected authentication probe",
                cancellationToken);
            string? mainFrameReferer = null;
            void ObserveMainFrameRequest(object? _, IRequest request)
            {
                if (request.IsNavigationRequest
                    && request.Frame == page.MainFrame
                    && Uri.TryCreate(request.Url, UriKind.Absolute, out var requestUri)
                    && RpolProtectedResourceUtility.IsExactProtectedUri(requestUri))
                {
                    request.Headers.TryGetValue("referer", out mainFrameReferer);
                }
            }

            page.Request += ObserveMainFrameRequest;
            try
            {
                var response = await WaitForPlaywrightAsync(
                    page.GotoAsync(ProtectedDiceRollerUri.AbsoluteUri, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = (float)PlaywrightOperationTimeout.TotalMilliseconds,
                        Referer = AppSettingsUtility.GameForumUrl
                    }),
                    "loading the exact RPOL Dice Roller authentication probe",
                    cancellationToken);
                var contentType = response?.Headers.TryGetValue("content-type", out var headerValue) == true
                    ? headerValue
                    : null;
                var stableNavigation = await RpolNavigationStability.WaitForStableAsync(
                    async token =>
                    {
                        try
                        {
                            var domJson = await WaitForPlaywrightAsync(
                                page.EvaluateAsync<string>("""
                                    () => {
                                        const root = document.documentElement;
                                        if (!root) return JSON.stringify({ Identity: 'missing', Html: '' });
                                        if (!root.dataset.playerAssistantRpolIdentity) {
                                            root.dataset.playerAssistantRpolIdentity =
                                                (globalThis.crypto && crypto.randomUUID) ? crypto.randomUUID() : String(Date.now());
                                        }
                                        return JSON.stringify({ Identity: root.dataset.playerAssistantRpolIdentity, Html: root.outerHTML });
                                    }
                                    """),
                                "reading the RPOL protected authentication DOM identity",
                                token);
                            var dom = JsonSerializer.Deserialize<RpolNavigationDomSnapshot>(domJson)
                                ?? throw new RpolAuthException(
                                    RpolAuthFailureKind.UnexpectedProtectedContent,
                                    "The RPOL protected authentication DOM evidence could not be decoded.");
                            NetworkRequestUtility.EnsureByteCountWithinLimit(
                                Encoding.UTF8.GetByteCount(dom.Html),
                                NetworkResponseContentLimit.Html);
                            return new RpolNavigationSnapshot(
                                Uri.TryCreate(page.Url, UriKind.Absolute, out var currentUri) ? currentUri : null,
                                dom.Identity,
                                dom.Html);
                        }
                        catch (PlaywrightException ex) when (IsTransientProtectedProbeObservationFailure(ex.Message))
                        {
                            return new RpolNavigationSnapshot(
                                Uri.TryCreate(page.Url, UriKind.Absolute, out var navigatingUri) ? navigatingUri : null,
                                $"navigating-{Guid.NewGuid():N}",
                                string.Empty);
                        }
                    },
                    quietPeriod: TimeSpan.FromSeconds(1),
                    maximumWait: TimeSpan.FromSeconds(20),
                    pollInterval: TimeSpan.FromMilliseconds(100),
                    cancellationToken);
                var secondSettledUri = stableNavigation.Url;
                var secondHtml = stableNavigation.Html;

                var challengeHeader = response?.Headers.Keys.Any(header =>
                    header.StartsWith("cf-", StringComparison.OrdinalIgnoreCase)) == true
                    ? "Cloudflare"
                    : null;
                var classification = RpolProtectedResourceUtility.ClassifyEvidence(
                    new RpolProtectedProbeEvidence(
                        ProtectedDiceRollerUri,
                        response is not null && Uri.TryCreate(response.Url, UriKind.Absolute, out var responseUri)
                            ? responseUri
                            : null,
                        secondSettledUri,
                        response?.Status,
                        contentType,
                        secondHtml,
                        mainFrameReferer,
                        SettledAfterStabilization: true));
                classificationKind = classification.Kind;
                if (classification.Kind != RpolProtectedResourceKind.AuthenticatedProtectedContent)
                {
                    throw CreateProtectedProbeException(classification);
                }

                return classification;
            }
            finally
            {
                page.Request -= ObserveMainFrameRequest;
                try
                {
                    await page.CloseAsync().WaitAsync(PlaywrightOperationTimeout, cancellationToken);
                }
                catch (Exception cleanupException)
                {
                    StartupLoggingUtility.Append(
                        "RPOL protected probe",
                        $"stage=protected_probe_cleanup category=cleanup_failure error={cleanupException.GetType().Name}");
                    throw new RpolCleanupFailureException(
                        "RPOL protected probe page cleanup failed; authentication evidence is not accepted.",
                        cleanupException);
                }

                StartupLoggingUtility.Append(
                    "RPOL protected probe",
                    $"stage=protected_probe category={classificationKind?.ToString() ?? "error"} duration_ms={(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds:0}");
            }
        }

        private static RpolAuthException CreateProtectedProbeException(
            RpolProtectedResourceClassification classification)
        {
            var failureKind = classification.Kind switch
            {
                RpolProtectedResourceKind.LoginRequired => RpolAuthFailureKind.AuthSessionExpired,
                RpolProtectedResourceKind.CloudflareChallenge => RpolAuthFailureKind.CloudflareChallenge,
                RpolProtectedResourceKind.UntrustedNavigation => RpolAuthFailureKind.UntrustedNavigation,
                RpolProtectedResourceKind.UnexpectedContent => RpolAuthFailureKind.UnexpectedProtectedContent,
                _ => RpolAuthFailureKind.RemoteUnavailable
            };
            return new RpolAuthException(failureKind, classification.Reason);
        }

        private static async Task<IPage> GetExternalRpolPageAsync(
            IBrowserContext context,
            Uri gameForumUri,
            CancellationToken cancellationToken)
        {
            var page = context.Pages.FirstOrDefault(page =>
                !page.IsClosed
                && Uri.TryCreate(page.Url, UriKind.Absolute, out var pageUri)
                && RpolCredentialSubmissionPolicy.TryValidateCredentialPage(pageUri, out _));
            if (page is not null)
            {
                return page;
            }

            page = await WaitForPlaywrightAsync(
                context.NewPageAsync(),
                "opening an RPOL page in the external browser",
                cancellationToken);
            await WaitForPlaywrightAsync(
                page.GotoAsync(gameForumUri.ToString(), new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = (float)PlaywrightOperationTimeout.TotalMilliseconds
                }),
                $"loading '{gameForumUri}' in the external browser",
                cancellationToken);
            return page;
        }

        private static async Task SubmitExternalBrowserLoginAsync(
            IPage page,
            string userName,
            string password,
            CancellationToken cancellationToken)
        {
            await SubmitValidatedCredentialFormAsync(
                page,
                userName,
                password,
                "submitting RPOL credentials in the external browser",
                cancellationToken);
            await WaitForPlaywrightAsync(
                page.WaitForLoadStateAsync(LoadState.DOMContentLoaded),
                "waiting for the RPOL login response in the external browser",
                cancellationToken);
        }

        private static async Task ValidateCredentialFormAsync(
            IPage page,
            string operationDescription,
            CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var topFrameUri))
            {
                throw new RpolAuthException(
                    RpolAuthFailureKind.TransportSecurityFailure,
                    $"RPOL credentials were not submitted because the live page URI was invalid while {operationDescription}.");
            }

            var forms = page.Locator("form");
            var formCount = await WaitForPlaywrightAsync(
                forms.CountAsync(),
                $"checking the RPOL login form count while {operationDescription}",
                cancellationToken);
            if (formCount != 1)
            {
                throw new RpolAuthException(
                    RpolAuthFailureKind.TransportSecurityFailure,
                    $"RPOL credentials were not submitted because the login form was ambiguous while {operationDescription}.");
            }

            var snapshotJson = await WaitForPlaywrightAsync(
                forms.First.EvaluateAsync<string>("form => JSON.stringify({ Action: form.action, Method: form.method, Target: form.target, SameFrame: window.top === window.self })"),
                $"reading the RPOL login form while {operationDescription}",
                cancellationToken);
            var snapshot = JsonSerializer.Deserialize<RpolLoginFormSnapshot>(snapshotJson)
                ?? throw new RpolAuthException(
                    RpolAuthFailureKind.TransportSecurityFailure,
                    "RPOL login form details could not be read safely.");
            var reason = snapshot.SameFrame ? string.Empty : "The login form is not in the top frame.";
            var validForm = snapshot.SameFrame
                && RpolCredentialSubmissionPolicy.TryValidateLoginForm(
                    topFrameUri,
                    snapshot.Action,
                    snapshot.Method,
                    snapshot.Target,
                    out reason);
            if (!validForm)
            {
                throw new RpolAuthException(
                    RpolAuthFailureKind.TransportSecurityFailure,
                    $"RPOL credentials were not submitted because the login form changed: {reason}");
            }
        }

        private static async Task SubmitValidatedCredentialFormAsync(
            IPage page,
            string userName,
            string password,
            string operationDescription,
            CancellationToken cancellationToken)
        {
            var popupObserved = false;
            var frameReplacementObserved = false;
            var submitEvaluationStarted = false;
            var credentialRequestObserved = false;
            string? requestGuardFailure = null;
            Task? popupCloseTask = null;
            using var credentialGuard = new RpolCredentialSubmissionGuard();
            void OnPopup(object? _, IPage popup)
            {
                popupObserved = true;
                popupCloseTask = popup.CloseAsync();
            }
            void OnFrameNavigated(object? _, IFrame frame)
            {
                if (frame != page.MainFrame || !submitEvaluationStarted)
                {
                    frameReplacementObserved = true;
                }
            }
            async Task OnRoute(IRoute route)
            {
                var request = route.Request;
                if (!submitEvaluationStarted)
                {
                    await route.ContinueAsync();
                    return;
                }

                if (!request.IsNavigationRequest)
                {
                    await route.ContinueAsync();
                    return;
                }

                if (!credentialGuard.IsArmed)
                {
                    requestGuardFailure = "The credential transmission guard was no longer armed.";
                    await route.AbortAsync();
                    return;
                }

                var topFrameUri = Uri.TryCreate(page.Url, UriKind.Absolute, out var currentUri)
                    ? currentUri
                    : null;
                var isMainFrame = request.Frame == page.MainFrame;
                string? reason = null;
                var validRequest = topFrameUri is not null
                    && Uri.TryCreate(request.Url, UriKind.Absolute, out var requestUri)
                    && RpolCredentialSubmissionPolicy.TryValidateCredentialRequest(
                        topFrameUri,
                        requestUri,
                        request.Method,
                        isMainFrame,
                        out reason);
                if (!validRequest)
                {
                    requestGuardFailure = reason ?? "The credential-bearing request could not be validated.";
                    await route.AbortAsync();
                    submitEvaluationStarted = false;
                    credentialGuard.Complete(false);
                    return;
                }

                credentialRequestObserved = true;
                try
                {
                    await route.ContinueAsync();
                    submitEvaluationStarted = false;
                    credentialGuard.Complete(true);
                }
                catch
                {
                    submitEvaluationStarted = false;
                    credentialGuard.Complete(false);
                    throw;
                }
            }

            page.Popup += OnPopup;
            page.FrameNavigated += OnFrameNavigated;
            var routeInstalled = false;
            try
            {
                await page.Context.RouteAsync("**/*", OnRoute);
                routeInstalled = true;
                await ValidateCredentialFormAsync(page, $"before {operationDescription}", cancellationToken);
                var form = page.Locator("form").First;
                submitEvaluationStarted = true;
                var submitted = await WaitForPlaywrightAsync(
                    form.EvaluateAsync<bool>(
                        RpolCredentialSubmissionScript.Source,
                        new { userName, password }),
                    operationDescription,
                    cancellationToken);
                if (submitted)
                {
                    var requestValidated = await credentialGuard.WaitForRequestAsync(
                        PlaywrightOperationTimeout,
                        cancellationToken);
                    if (!requestValidated)
                    {
                        throw new RpolAuthException(
                            RpolAuthFailureKind.TransportSecurityFailure,
                            "RPOL credential submission did not produce a validated main-frame request before the guard deadline.");
                    }
                }
                if (popupCloseTask is not null)
                {
                    try
                    {
                        await popupCloseTask.WaitAsync(PlaywrightOperationTimeout, cancellationToken);
                    }
                    catch
                    {
                        if (!popupCloseTask.IsCompleted) RpolCleanupUtility.TrackLateTask(popupCloseTask);
                        throw;
                    }
                }
                if (!submitted
                    || !credentialRequestObserved
                    || requestGuardFailure is not null
                    || popupObserved
                    || frameReplacementObserved)
                {
                    throw new RpolAuthException(
                        RpolAuthFailureKind.TransportSecurityFailure,
                        $"RPOL credentials were not submitted because the validated form changed during {operationDescription}: {requestGuardFailure ?? "navigation or popup mutation observed"}.");
                }
            }
            finally
            {
                submitEvaluationStarted = false;
                if (popupCloseTask is not null && !popupCloseTask.IsCompleted) RpolCleanupUtility.TrackLateTask(popupCloseTask);
                if (routeInstalled) await page.Context.UnrouteAsync("**/*", OnRoute);
                page.Popup -= OnPopup;
                page.FrameNavigated -= OnFrameNavigated;
            }
        }

        private static Process StartExternalBrowserForManualVerification(
            string browserPath,
            int remoteDebuggingPort,
            string profileDirectory,
            string noticePath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = browserPath,
                UseShellExecute = false
            };
            foreach (var argument in CreateExternalBrowserVerificationArguments(
                remoteDebuggingPort,
                profileDirectory,
                noticePath))
            {
                startInfo.ArgumentList.Add(argument);
            }

            return StartExternalBrowserProcess(startInfo);
        }

        internal static string[] CreateExternalBrowserVerificationArguments(
            int remoteDebuggingPort,
            string profileDirectory,
            string noticePath)
        {
            return
            [
                $"--remote-debugging-port={remoteDebuggingPort}",
                $"--user-data-dir={profileDirectory}",
                "--no-first-run",
                "--new-window",
                new Uri(noticePath).AbsoluteUri,
                AppSettingsUtility.GameForumUrl
            ];
        }

        private static Process StartExternalBrowserProcess(ProcessStartInfo startInfo)
        {
            try
            {
                return Process.Start(startInfo)
                    ?? throw new RpolAuthException(
                        RpolAuthFailureKind.PlaywrightUnavailable,
                        "RPOL browser verification could not start the external browser process.");
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
            {
                throw new RpolAuthException(
                    RpolAuthFailureKind.PlaywrightUnavailable,
                    $"RPOL browser verification could not start the external browser process: {ex.Message}",
                    ex);
            }
        }

        private static void TerminateExternalBrowserProcessesForProfile(string profileDirectory)
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(profileDirectory))
            {
                return;
            }

            var escapedProfile = profileDirectory.Replace("'", "''", StringComparison.Ordinal);
            var script = $"$profile = '{escapedProfile}'; "
                + "$items = Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'chrome.exe' -and $_.CommandLine -like ('*' + $profile + '*') }; "
                + "foreach ($item in $items) { & taskkill.exe /PID $item.ProcessId /T /F | Out-Null };";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    script
                }
            }) ?? throw new InvalidOperationException("RPOL browser cleanup could not start the process cleanup helper.");
            if (!process.WaitForExit(5000))
            {
                throw new TimeoutException("RPOL browser process cleanup exceeded its bound.");
            }
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("RPOL browser process cleanup returned a failure status.");
            }
        }

        private static string CreateExternalBrowserNoticeHtml()
        {
            var encodedUrl = WebUtility.HtmlEncode(AppSettingsUtility.GameForumUrl);
            return $$"""
                <!doctype html>
                <html lang="en">
                <head>
                    <meta charset="utf-8">
                    <title>Player Assistant RPOL Verification</title>
                    <style>
                        body {
                            margin: 0;
                            min-height: 100vh;
                            display: grid;
                            place-items: center;
                            font-family: Arial, sans-serif;
                            background: #f8fafc;
                            color: #111827;
                        }

                        main {
                            max-width: 760px;
                            padding: 40px;
                            border: 4px solid #7f1d1d;
                            background: #ffffff;
                            box-shadow: 0 24px 80px rgba(15, 23, 42, 0.18);
                        }

                        h1 {
                            margin: 0 0 18px;
                            font-size: 34px;
                            line-height: 1.15;
                        }

                        p {
                            font-size: 21px;
                            line-height: 1.45;
                        }

                        strong {
                            color: #7f1d1d;
                        }
                    </style>
                </head>
                <body>
                    <main>
                        <h1>Player Assistant is verifying RPOL access</h1>
                        <p><strong>Please wait patiently.</strong> In the RPOL tab, complete any "verify you are human" checkbox or browser prompt that appears.</p>
                        <p>When RPOL no longer shows a browser verification page, leave this temporary browser window open. Player Assistant will capture the verified browser state over CDP and close the temporary window when finished.</p>
                        <p>RPOL target: {{encodedUrl}}</p>
                    </main>
                </body>
                </html>
                """;
        }

        private static string? FindExternalBrowserExecutable()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var candidates = new[]
            {
                Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static int GetAvailableLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static void DeleteTemporaryDirectory(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        private static async Task EnsureLoggedInAsync(
            IBrowserContext context,
            string userName,
            string password,
            bool clearCloudflareChallenge,
            CancellationToken cancellationToken)
        {
            var page = await WaitForPlaywrightAsync(
                context.NewPageAsync(),
                "opening the RPOL login page",
                cancellationToken);
            try
            {
                var gameForumUri = new Uri(AppSettingsUtility.GameForumUrl);
                await NavigateWithRateLimitAsync(page, gameForumUri, cancellationToken);
                if (clearCloudflareChallenge)
                {
                    await ShowCloudflareClearanceNoticeAsync(page, cancellationToken);
                }

                if (!await HasLoginFormAsync(page, cancellationToken))
                {
                    if (clearCloudflareChallenge)
                    {
                        await WaitForCloudflareChallengeClearanceAsync(page, gameForumUri, cancellationToken);
                    }

                    return;
                }

                await SubmitValidatedCredentialFormAsync(
                    page,
                    userName,
                    password,
                    "submitting RPOL credentials",
                    cancellationToken);
                await WaitForPlaywrightAsync(
                    page.WaitForLoadStateAsync(LoadState.DOMContentLoaded),
                    "waiting for the RPOL login response",
                    cancellationToken);
                await Task.Delay(
                    clearCloudflareChallenge
                        ? TimeSpan.FromSeconds(6)
                        : TimeSpan.FromMilliseconds(500),
                    cancellationToken);

                if (await HasLoginFormAsync(page, cancellationToken))
                {
                    throw new RpolAuthException(
                        RpolAuthFailureKind.LoginRejected,
                        "RPoL login was rejected. Check the configured credentials.");
                }

                if (clearCloudflareChallenge)
                {
                    await ShowCloudflareClearanceNoticeAsync(page, cancellationToken);
                    await WaitForCloudflareChallengeClearanceAsync(page, gameForumUri, cancellationToken);
                }
            }
            finally
            {
                await page.CloseAsync();
            }
        }

        private static async Task<bool> HasLoginFormAsync(IPage page, CancellationToken cancellationToken)
        {
            var userNameInput = page.Locator("input[name='username']");
            return await WaitForPlaywrightAsync(
                userNameInput.CountAsync(),
                "checking for the RPOL login form",
                cancellationToken) > 0;
        }

        private static async Task WaitForCloudflareChallengeClearanceAsync(
            IPage page,
            Uri uri,
            CancellationToken cancellationToken)
        {
            var startedAt = DateTimeOffset.UtcNow;
            while (DateTimeOffset.UtcNow - startedAt < CloudflareClearanceMaxWait)
            {
                try
                {
                    await Task.Delay(CloudflareClearancePollInterval, cancellationToken);
                    await ShowCloudflareClearanceNoticeAsync(page, cancellationToken);

                    var currentHtml = await WaitForPlaywrightAsync(
                        page.ContentAsync(),
                        "checking the RPOL browser verification page",
                        cancellationToken);
                    if (!LooksLikeCloudflareChallengePage(currentHtml)
                        && !await HasLoginFormAsync(page, cancellationToken))
                    {
                        return;
                    }
                }
                catch (PlaywrightException ex) when (IsBrowserClosedException(ex))
                {
                    throw new RpolAuthException(
                        RpolAuthFailureKind.CloudflareChallenge,
                        "RPOL browser verification was cancelled because the temporary browser window was closed before verification completed.",
                        ex);
                }
            }

            throw new RpolAuthException(
                RpolAuthFailureKind.CloudflareChallenge,
                $"RPOL browser verification did not complete within {CloudflareClearanceMaxWait.TotalMinutes:0} minutes. Cloudflare did not present a solvable challenge in the temporary browser, so authenticated RPOL downloads remain unavailable.");
        }

        private static bool IsBrowserClosedException(PlaywrightException exception)
        {
            return exception.Message.Contains("Target page, context or browser has been closed", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("Browser has been closed", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("Target closed", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsTransientProtectedProbeObservationFailure(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.Contains("page is navigating and changing the content", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsTransportSecurityFailureMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.Contains("net::ERR_CERT_", StringComparison.OrdinalIgnoreCase)
                || message.Contains("net::ERR_SSL_", StringComparison.OrdinalIgnoreCase)
                || message.Contains("net::ERR_TLS_", StringComparison.OrdinalIgnoreCase)
                || message.Contains("certificate verify failed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("unable to verify the first certificate", StringComparison.OrdinalIgnoreCase);
        }

        private static RpolAuthException CreateTransportSecurityException(PlaywrightException exception)
        {
            return new RpolAuthException(
                RpolAuthFailureKind.TransportSecurityFailure,
                "Player Assistant could not establish a trusted TLS connection to RPOL. Authentication and downloads were stopped. Verify the Windows date, time, and trusted root certificates; do not bypass certificate warnings.",
                exception);
        }

        internal static bool LooksLikeCloudflareChallengePage(string html)
        {
            return html.Contains("cf-challenge", StringComparison.OrdinalIgnoreCase)
                || html.Contains("cf_clearance", StringComparison.OrdinalIgnoreCase)
                || html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
                || html.Contains("Verify you are human", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task AddCloudflareClearanceNoticeScriptAsync(
            IBrowserContext context,
            CancellationToken cancellationToken)
        {
            await WaitForPlaywrightAsync(
                context.AddInitScriptAsync("""
                (() => {
                    const showPlayerAssistantNotice = () => {
                        const existing = document.getElementById('player-assistant-rpol-wait-notice');
                        if (existing) {
                            return;
                        }

                        const notice = document.createElement('div');
                        notice.id = 'player-assistant-rpol-wait-notice';
                        notice.setAttribute('role', 'status');
                        notice.setAttribute('aria-live', 'polite');
                        notice.textContent = 'Player Assistant is clearing RPOL browser verification. Please wait patiently. If this page asks you to verify you are human, complete that prompt and do not close this temporary browser window.';
                        Object.assign(notice.style, {
                            position: 'fixed',
                            top: '0',
                            left: '0',
                            right: '0',
                            zIndex: '2147483647',
                            padding: '18px 28px',
                            background: '#7f1d1d',
                            color: '#ffffff',
                            font: '700 22px/1.35 Arial, sans-serif',
                            textAlign: 'center',
                            boxShadow: '0 4px 18px rgba(0, 0, 0, 0.35)',
                            pointerEvents: 'none'
                        });
                        document.documentElement.appendChild(notice);
                    };

                    if (document.readyState === 'loading') {
                        document.addEventListener('DOMContentLoaded', showPlayerAssistantNotice, { once: true });
                    } else {
                        showPlayerAssistantNotice();
                    }
                })();
                """),
                "installing the RPOL browser verification notice",
                cancellationToken);
        }

        private static async Task ShowCloudflareClearanceNoticeAsync(
            IPage page,
            CancellationToken cancellationToken)
        {
            await WaitForPlaywrightAsync(
                page.EvaluateAsync("""
                (() => {
                    const existing = document.getElementById('player-assistant-rpol-wait-notice');
                    if (existing) {
                        return;
                    }

                    const notice = document.createElement('div');
                    notice.id = 'player-assistant-rpol-wait-notice';
                    notice.setAttribute('role', 'status');
                    notice.setAttribute('aria-live', 'polite');
                    notice.textContent = 'Player Assistant is clearing RPOL browser verification. Please wait patiently. If this page asks you to verify you are human, complete that prompt and do not close this temporary browser window.';
                    Object.assign(notice.style, {
                        position: 'fixed',
                        top: '0',
                        left: '0',
                        right: '0',
                        zIndex: '2147483647',
                        padding: '18px 28px',
                        background: '#7f1d1d',
                        color: '#ffffff',
                        font: '700 22px/1.35 Arial, sans-serif',
                        textAlign: 'center',
                        boxShadow: '0 4px 18px rgba(0, 0, 0, 0.35)',
                        pointerEvents: 'none'
                    });
                    document.documentElement.appendChild(notice);
                })()
                """),
                "showing the RPOL browser verification notice",
                cancellationToken);
        }

        private static (string UserName, string Password) GetCredentials()
        {
            if (!AppSettingsUtility.TryGetRpolCredentials(out var userName, out var password)
                || string.IsNullOrWhiteSpace(userName)
                || string.IsNullOrWhiteSpace(password))
            {
                throw new RpolAuthException(
                    RpolAuthFailureKind.MissingCredentials,
                    "Missing RPoL credentials. Confirm the installed encrypted local settings include both RPOL credential values.");
            }

            return (userName, password);
        }

        internal static bool LooksLikeLoginResponse(string? contentType, byte[] body)
        {
            if (contentType is null ||
                !contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return LooksLikeLoginPage(DecodeHtmlBody(body, contentType));
        }

        internal static bool ShouldTreatResponseAsLogin(
            string? contentType,
            byte[] body,
            bool allowEmbeddedLoginForm)
        {
            if (!LooksLikeLoginResponse(contentType, body))
            {
                return false;
            }

            return !allowEmbeddedLoginForm
                || ShouldTreatExternalPageAsLogin(DecodeHtmlBody(body, contentType));
        }

        internal static string DecodeHtmlBody(byte[] body, string? contentType)
        {
            ArgumentNullException.ThrowIfNull(body);
            if (System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
                && !string.IsNullOrWhiteSpace(mediaType.CharSet))
            {
                try
                {
                    return Encoding.GetEncoding(mediaType.CharSet.Trim('"')).GetString(body);
                }
                catch (ArgumentException)
                {
                }
            }

            return Encoding.UTF8.GetString(body);
        }

        internal static bool LooksLikeLoginPage(string html)
        {
            ArgumentNullException.ThrowIfNull(html);
            return RpolProtectedResourceUtility.LooksLikeLoginForm(html);
        }

        public static void ResetAuthenticationState()
        {
            ResetAuthenticationStateAsync().GetAwaiter().GetResult();
        }

        private static async Task ResetAuthenticationStateAsync(CancellationToken cancellationToken = default)
        {
            _cachedFatalAuthFailure = null;
            _cachedFatalAuthFailureLogged = false;
            RuntimeSecretStoreUtility.DeleteRpolStorageState();
            await ResetSessionAsync(cancellationToken);
        }

        private static string? TryCreatePreparedStorageStateFile()
        {
            try
            {
                if (!RuntimeSecretStoreUtility.TryGetRpolStorageState(out var storageStateJson, out var lastWritten)
                    || string.IsNullOrWhiteSpace(storageStateJson))
                {
                    return null;
                }

                var tempDirectory = RuntimePathUtility.GetUserDataPath("temp");
                Directory.CreateDirectory(tempDirectory);
                RpolSecureStorageStateFile.Scavenge(tempDirectory, DateTimeOffset.UtcNow.AddHours(-1));
                var tempPath = RuntimePathUtility.CombineUnderBase(
                    tempDirectory,
                    $"rpol-storage-state-{Guid.NewGuid():N}.json");
                WriteUserOnlyStorageStateFile(tempPath, storageStateJson);
                File.SetLastWriteTimeUtc(tempPath, lastWritten.UtcDateTime);

                if (!TryPrepareStorageStateFile(tempPath, DateTimeOffset.Now))
                {
                    try
                    {
                        DeleteTemporaryStorageStateFile(tempPath);
                    }
                    catch (Exception cleanupException)
                    {
                        throw new RpolCleanupFailureException(
                            "Malformed RPOL browser auth state could not be deleted; authentication is stopped.",
                            cleanupException);
                    }
                    RuntimeSecretStoreUtility.DeleteRpolStorageState();
                    return null;
                }

                return tempPath;
            }
            catch (Exception ex) when (ex is not RpolCleanupFailureException
                && (ex is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or JsonException
                    or InvalidOperationException))
            {
                RuntimeSecretStoreUtility.DeleteRpolStorageState();
                StartupLoggingUtility.Append(
                    "RPOL storage state validation",
                    new InvalidOperationException(
                        "Deleted malformed RPOL browser auth state from Windows Credential Manager. A fresh login will be attempted.",
                        ex));
                return null;
            }
        }

        private static async Task SaveStorageStateSecretAsync(
            IBrowserContext context,
            CancellationToken cancellationToken,
            RpolCrossProcessLock? lockOwner = null,
            string? cdpEndpoint = null)
        {
            var tempDirectory = RuntimePathUtility.GetUserDataPath("temp");
            Directory.CreateDirectory(tempDirectory);
            RpolSecureStorageStateFile.Scavenge(tempDirectory, DateTimeOffset.UtcNow.AddHours(-1));
            var tempPath = RuntimePathUtility.CombineUnderBase(
                tempDirectory,
                $"rpol-storage-state-save-{Guid.NewGuid():N}.json");

            try
            {
                var storageStateJson = await WaitForPlaywrightAsync(
                    context.StorageStateAsync(),
                    "saving the RPOL browser storage state",
                    cancellationToken);
                WriteUserOnlyStorageStateFile(tempPath, storageStateJson);
                StartupLoggingUtility.Append("RPOL authentication stage", "stage=storage_captured");
                if (!TryPrepareStorageStateFile(tempPath, DateTimeOffset.UtcNow))
                {
                    throw new RpolAuthException(
                        RpolAuthFailureKind.UnexpectedProtectedContent,
                        "RPOL browser storage state failed structural validation before persistence.");
                }

                await PersistStorageStateAsync(storageStateJson, cancellationToken, lockOwner, cdpEndpoint);
            }
            finally
            {
                DeleteTemporaryStorageStateFile(tempPath);
            }
        }

        private static async Task PersistStorageStateAsync(
            string storageStateJson,
            CancellationToken cancellationToken,
            RpolCrossProcessLock? lockOwner,
            string? cdpEndpoint)
        {
            if (lockOwner is not null)
            {
                await PersistVerifiedStorageStateJsonAsync(storageStateJson, cancellationToken, lockOwner, cdpEndpoint);
                return;
            }

            using var operationLock = await RpolCrossProcessLock.AcquireAsync(
                RpolCrossProcessLock.AuthAndPublisherName,
                TimeSpan.FromSeconds(10),
                cancellationToken);
            await PersistVerifiedStorageStateJsonAsync(storageStateJson, cancellationToken, operationLock, cdpEndpoint);
        }

        internal static async Task PersistVerifiedStorageStateJsonAsync(
            string storageStateJson,
            CancellationToken cancellationToken,
            RpolCrossProcessLock lockOwner,
            string? cdpEndpoint = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storageStateJson);
            ArgumentNullException.ThrowIfNull(lockOwner);
            if (PersistVerifiedStorageStateJsonOverrideForTests is not null)
            {
                await PersistVerifiedStorageStateJsonOverrideForTests(storageStateJson, cancellationToken, lockOwner);
                return;
            }
            using var operationLock = lockOwner.AcquireReentrant(TimeSpan.FromSeconds(10), cancellationToken);
            var previousPointer = RuntimeSecretStoreUtility.CaptureRpolActiveStatePointer();
            var hasPreviousState = RuntimeSecretStoreUtility.TryGetRpolStorageState(out var previousState, out _);
            previousState = hasPreviousState ? previousState : null;
            var tempDirectory = RuntimePathUtility.GetUserDataPath("temp");
            Directory.CreateDirectory(tempDirectory);
            var tempPath = RuntimePathUtility.CombineUnderBase(
                tempDirectory,
                $"rpol-storage-state-candidate-{Guid.NewGuid():N}.json");
            var promotionStarted = false;
            try
            {
                WriteUserOnlyStorageStateFile(tempPath, storageStateJson);
                if (!TryPrepareStorageStateFile(tempPath, DateTimeOffset.UtcNow))
                {
                    throw new RpolAuthException(
                        RpolAuthFailureKind.UnexpectedProtectedContent,
                        "RPOL candidate browser storage state failed structural validation.");
                }

                RuntimeSecretStoreUtility.SaveRpolStorageStateCandidate(storageStateJson);
                StartupLoggingUtility.Append("RPOL authentication stage", "stage=candidate_state_written");
                if (!RuntimeSecretStoreUtility.PromoteRpolStorageStateCandidate(out var promotionError))
                {
                    throw new RpolAuthException(
                        RpolAuthFailureKind.UnexpectedProtectedContent,
                        promotionError ?? "RPOL candidate state promotion failed.");
                }
                promotionStarted = true;

                if (RuntimeSecretStoreUtility.TryGetRpolStorageState(out var loadedState, out _)
                    && string.Equals(loadedState, storageStateJson, StringComparison.Ordinal))
                {
                    throw new RpolAuthException(
                        RpolAuthFailureKind.UnexpectedProtectedContent,
                        "The promoted RPOL state unexpectedly became visible through the normal active-state loader before proof.");
                }
                await RpolPublisherEquivalentProcessProof.ProveCandidateAsync(
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(45),
                    cdpEndpoint);
                RuntimeSecretStoreUtility.MarkRpolStorageStateVerified();
                RuntimeSecretStoreUtility.DeleteRpolStorageStateCandidate();
                StartupLoggingUtility.Append("RPOL authentication stage", "stage=state_persisted");
            }
            catch (Exception ex)
            {
                var cleanupErrors = new List<Exception>();
                if (promotionStarted)
                {
                    try
                    {
                        RuntimeSecretStoreUtility.RestoreRpolActiveStatePointer(previousPointer);
                        if (!RuntimeSecretStoreUtility.VerifyRpolActiveStateRestored(previousPointer, previousState, out var rollbackReason))
                        {
                            throw new InvalidOperationException(rollbackReason);
                        }
                    }
                    catch (Exception rollbackException) { cleanupErrors.Add(new InvalidOperationException("RPOL active-state rollback failed.", rollbackException)); }
                }
                try { RuntimeSecretStoreUtility.DeleteRpolStorageStateCandidate(); }
                catch (Exception candidateCleanupException) { cleanupErrors.Add(new InvalidOperationException("RPOL candidate cleanup failed.", candidateCleanupException)); }
                if (cleanupErrors.Count > 0)
                {
                    throw new AggregateException("RPOL state promotion failed and cleanup was incomplete.", new[] { ex }.Concat(cleanupErrors));
                }
                throw;
            }
            finally
            {
                DeleteTemporaryStorageStateFile(tempPath);
            }
        }

        private static void WriteUserOnlyStorageStateFile(string path, string contents)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            RpolSecureStorageStateFile.ApplyUserOnlyAcl(path);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            writer.Write(contents);
            writer.Flush();
            stream.Flush(true);
        }

        internal static async Task VerifyCandidateStorageStateInPublisherProcessAsync(
            CancellationToken cancellationToken,
            string? cdpEndpoint = null)
        {
            if (!RuntimeSecretStoreUtility.TryGetRpolStorageStateCandidate(out var storageStateJson)
                || string.IsNullOrWhiteSpace(storageStateJson))
            {
                throw new RpolAuthException(
                    RpolAuthFailureKind.UnexpectedProtectedContent,
                    "The RPOL candidate state was not available to the publisher-equivalent proof.");
            }

            if (!string.IsNullOrWhiteSpace(cdpEndpoint))
            {
                await VerifyCandidateThroughCdpAsync(cdpEndpoint, cancellationToken);
                return;
            }

            var tempDirectory = RuntimePathUtility.GetUserDataPath("temp");
            Directory.CreateDirectory(tempDirectory);
            RpolSecureStorageStateFile.Scavenge(tempDirectory, DateTimeOffset.UtcNow.AddHours(-1));
            var tempPath = RuntimePathUtility.CombineUnderBase(
                tempDirectory,
                $"rpol-storage-state-proof-{Guid.NewGuid():N}.json");
            IPlaywright? playwright = null;
            IBrowser? browser = null;
            IBrowserContext? context = null;
            Exception? primaryFailure = null;
            try
            {
                WriteUserOnlyStorageStateFile(tempPath, storageStateJson);
                playwright = await WaitForPlaywrightAsync(
                    Playwright.CreateAsync(),
                    "starting the separate RPOL publisher-equivalent proof browser",
                    cancellationToken);
                var launch = await LaunchRpolBrowserAsync(playwright, clearCloudflareChallenge: false, cancellationToken);
                browser = launch.Browser;
                context = await WaitForPlaywrightAsync(
                    browser.NewContextAsync(CreateBrowserContextOptions(tempPath, launch.UseDefaultUserAgent)),
                    "creating the publisher-equivalent RPOL proof context",
                    cancellationToken);
                await WaitForPlaywrightAsync(
                    context.AddInitScriptAsync("""
                    Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                    """),
                    "configuring the publisher-equivalent RPOL proof context",
                    cancellationToken);
                await VerifyAuthenticatedContextAsync(context, cancellationToken);
            }
            catch (Exception ex)
            {
                primaryFailure = ex;
            }
            finally
            {
                var cleanupErrors = new List<Exception>();
                if (context is not null)
                {
                    cleanupErrors.AddRange(await RpolCleanupUtility.DisposeAsyncIndependently(
                        cancellationToken,
                        ("publisher proof context", () => context.CloseAsync())));
                }

                if (browser is not null)
                {
                    cleanupErrors.AddRange(await RpolCleanupUtility.DisposeAsyncIndependently(
                        cancellationToken,
                        ("publisher proof browser", () => browser.CloseAsync())));
                }

                cleanupErrors.AddRange(RpolCleanupUtility.DisposeIndependently(
                    ("publisher proof Playwright", () => playwright?.Dispose()),
                    ("publisher proof storage state", () => DeleteTemporaryStorageStateFile(tempPath))));
                if (cleanupErrors.Count > 0)
                {
                    var cleanupFailure = new AggregateException("Publisher-equivalent RPOL proof cleanup failed.", cleanupErrors);
                    if (primaryFailure is not null) throw new AggregateException(cleanupFailure, primaryFailure);
                    throw cleanupFailure;
                }
            }
            if (primaryFailure is not null) ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        private static async Task VerifyCandidateThroughCdpAsync(
            string cdpEndpoint,
            CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(cdpEndpoint, UriKind.Absolute, out var endpoint)
                || !string.Equals(endpoint.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                || (endpoint.Host is not ("127.0.0.1" or "localhost"))
                || endpoint.AbsolutePath != "/")
            {
                throw new RpolAuthException(
                    RpolAuthFailureKind.UntrustedNavigation,
                    "The RPOL publisher proof CDP endpoint was not a loopback HTTP endpoint.");
            }

            IPlaywright? playwright = null;
            try
            {
                playwright = await WaitForPlaywrightAsync(
                    Playwright.CreateAsync(),
                    "starting the RPOL publisher-equivalent CDP proof",
                    cancellationToken);
                var browser = await WaitForPlaywrightAsync(
                    playwright.Chromium.ConnectOverCDPAsync(cdpEndpoint),
                    "connecting the RPOL publisher-equivalent proof to the authenticated browser",
                    cancellationToken);
                var context = browser.Contexts.FirstOrDefault()
                    ?? throw new RpolAuthException(
                        RpolAuthFailureKind.PlaywrightUnavailable,
                        "The authenticated RPOL browser did not expose a context to the publisher proof.");
                await VerifyAuthenticatedContextAsync(context, cancellationToken);
            }
            finally
            {
                playwright?.Dispose();
            }
        }

        internal static async Task DisposeCurrentSessionAsync(CancellationToken cancellationToken = default)
        {
            await ResetSessionAsync(cancellationToken);
            await AwaitLatePlaywrightCleanupAsync(cancellationToken);
        }

        private static void DeleteTemporaryStorageStateFile(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        // Playwright storage-state JSON has no authenticated-resource proof. Only a live protected probe can establish it.
        internal static bool IsStorageStateSemanticProof(string storageStateJson)
        {
            ArgumentNullException.ThrowIfNull(storageStateJson);
            return false;
        }

        internal static bool TryPrepareStorageStateFile(
            string storageStatePath,
            DateTimeOffset? now = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storageStatePath);

            if (!File.Exists(storageStatePath))
            {
                return false;
            }

            try
            {
                ValidateStorageStateFile(storageStatePath, now ?? DateTimeOffset.Now);
                return true;
            }
            catch (Exception ex) when (ex is JsonException
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
            {
                DeleteInvalidStorageStateAndLog(storageStatePath, ex);
                return false;
            }
        }

        private static void ValidateStorageStateFile(string storageStatePath, DateTimeOffset now)
        {
            var fileInfo = new FileInfo(storageStatePath);
            if (fileInfo.Length <= 0)
            {
                throw new InvalidOperationException("RPOL browser auth state is empty.");
            }

            var lastWriteUtc = new DateTimeOffset(fileInfo.LastWriteTimeUtc);
            var age = now.ToUniversalTime() - lastWriteUtc;
            if (age > StorageStateMaxAge)
            {
                throw new InvalidOperationException(
                    $"RPOL browser auth state is older than {StorageStateMaxAge.TotalDays:0} days.");
            }

            using var stream = new FileStream(
                storageStatePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("RPOL browser auth state root must be a JSON object.");
            }

            if (!root.TryGetProperty("cookies", out var cookies)
                || cookies.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("RPOL browser auth state must contain a cookies array.");
            }

            var hasRpolCookie = false;
            foreach (var cookie in cookies.EnumerateArray())
            {
                if (cookie.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException("RPOL browser auth state cookies must be JSON objects.");
                }

                var name = GetRequiredCookieString(cookie, "name");
                _ = GetRequiredCookieString(cookie, "value");
                var domain = GetRequiredCookieString(cookie, "domain");
                _ = GetRequiredCookieString(cookie, "path");

                if (name.Length > 0
                    && (string.Equals(domain, "rpol.net", StringComparison.OrdinalIgnoreCase)
                        || domain.EndsWith(".rpol.net", StringComparison.OrdinalIgnoreCase)))
                {
                    hasRpolCookie = true;
                }
            }

            if (!hasRpolCookie)
            {
                throw new InvalidOperationException("RPOL browser auth state does not contain an rpol.net cookie.");
            }
        }

        private static string GetRequiredCookieString(JsonElement cookie, string propertyName)
        {
            if (!cookie.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(property.GetString()))
            {
                throw new InvalidOperationException(
                    $"RPOL browser auth state cookie is missing required '{propertyName}' text.");
            }

            return property.GetString()!;
        }

        private static void DeleteInvalidStorageStateAndLog(string storageStatePath, Exception exception)
        {
            try
            {
                File.Delete(storageStatePath);
                StartupLoggingUtility.Append(
                    "RPOL storage state validation",
                    new InvalidOperationException(
                        $"Deleted invalid RPOL browser auth state '{storageStatePath}'. A fresh login will be attempted.",
                        exception));
            }
            catch (Exception deleteException) when (deleteException is IOException or UnauthorizedAccessException)
            {
                var cleanupFailure = new InvalidOperationException(
                    $"RPOL browser auth state '{storageStatePath}' is invalid and could not be deleted.",
                    deleteException);
                StartupLoggingUtility.Append("RPOL storage state validation", cleanupFailure);
                throw cleanupFailure;
            }
        }

        private static async Task<string> GetPageHtmlAsync(
            IBrowserContext context,
            Uri uri,
            CancellationToken cancellationToken)
        {
            var page = await WaitForPlaywrightAsync(
                context.NewPageAsync(),
                $"opening '{uri}' in the RPOL browser",
                cancellationToken);
            try
            {
                var response = await NavigateWithRateLimitAsync(page, uri, cancellationToken);
                var html = await WaitForPlaywrightAsync(
                    page.ContentAsync(),
                    $"reading HTML from '{uri}'",
                    cancellationToken);
                EnsureSuccessfulResponse(uri, response, html);
                NetworkRequestUtility.EnsureByteCountWithinLimit(
                    Encoding.UTF8.GetByteCount(html),
                    NetworkResponseContentLimit.Html);
                return html;
            }
            finally
            {
                await page.CloseAsync();
            }
        }

        private static async Task<RpolResponse> GetPageResponseAsync(
            IBrowserContext context,
            Uri uri,
            CancellationToken cancellationToken)
        {
            var page = await WaitForPlaywrightAsync(
                context.NewPageAsync(),
                $"opening '{uri}' in the RPOL browser",
                cancellationToken);
            try
            {
                var response = await NavigateWithRateLimitAsync(page, uri, cancellationToken);
                var contentType = response!.Headers.TryGetValue("content-type", out var headerValue)
                    ? headerValue
                    : null;

                var body = await WaitForPlaywrightAsync(
                    response.BodyAsync(),
                    $"reading the response body from '{uri}'",
                    cancellationToken);
                EnsureSuccessfulResponse(
                    uri,
                    response,
                    contentType is not null && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase)
                        ? DecodeHtmlBody(body, contentType)
                        : null);
                var limit = contentType is not null && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    ? NetworkResponseContentLimit.Image
                    : NetworkResponseContentLimit.Html;
                NetworkRequestUtility.EnsureByteCountWithinLimit(body.Length, limit);

                return new RpolResponse(
                    body,
                    contentType);
            }
            finally
            {
                await page.CloseAsync();
            }
        }

        private static async Task<IResponse?> NavigateWithRateLimitAsync(
            IPage page,
            Uri uri,
            CancellationToken cancellationToken)
        {
            await NavigationSemaphore.WaitAsync(cancellationToken);
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (_lastNavigationAttemptUtc is { } lastAttempt)
                {
                    var nextAttempt = lastAttempt + RpolNavigationAttemptInterval;
                    var delay = nextAttempt - now;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                }

                _lastNavigationAttemptUtc = DateTimeOffset.UtcNow;
                return await WaitForPlaywrightAsync(
                    page.GotoAsync(uri.ToString(), new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = (float)PlaywrightOperationTimeout.TotalMilliseconds,
                        Referer = GetNavigationReferer(uri)
                    }),
                    $"loading '{uri}'",
                    cancellationToken);
            }
            finally
            {
                NavigationSemaphore.Release();
            }
        }

        internal static string? GetNavigationReferer(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            return RpolProtectedResourceUtility.IsExactProtectedUri(uri)
                ? AppSettingsUtility.GameForumUrl
                : null;
        }

        private static void EnsureSuccessfulResponse(Uri uri, IResponse? response, string? responseBody = null)
        {
            if (response is null)
            {
                throw new RpolAuthException(
                    RpolAuthFailureKind.RemoteUnavailable,
                    $"RPoL navigation to '{uri}' did not return a response.");
            }

            if (!response.Ok)
            {
                throw CreateUnsuccessfulResponseException(
                    uri,
                    response.Status,
                    response.StatusText,
                    response.Url,
                    response.Headers,
                    responseBody);
            }
        }

        internal static RpolAuthException CreateUnsuccessfulResponseException(
            Uri uri,
            int statusCode,
            string? statusText)
        {
            return CreateUnsuccessfulResponseException(uri, statusCode, statusText, responseUrl: null);
        }

        internal static RpolAuthException CreateUnsuccessfulResponseException(
            Uri uri,
            int statusCode,
            string? statusText,
            string? responseUrl)
        {
            return CreateUnsuccessfulResponseException(
                uri,
                statusCode,
                statusText,
                responseUrl,
                responseHeaders: null,
                responseBody: null);
        }

        private static RpolAuthException CreateUnsuccessfulResponseException(
            Uri uri,
            int statusCode,
            string? statusText,
            string? responseUrl,
            IReadOnlyDictionary<string, string>? responseHeaders,
            string? responseBody)
        {
            ArgumentNullException.ThrowIfNull(uri);

            var statusDescription = string.IsNullOrWhiteSpace(statusText)
                ? statusCode.ToString()
                : $"{statusCode} {statusText}";
            if (IsCloudflareChallengeResponse(statusCode, responseUrl, responseHeaders, responseBody))
            {
                return new RpolAuthException(
                    RpolAuthFailureKind.CloudflareChallenge,
                    $"RPoL presented a Cloudflare browser challenge while loading '{uri}' with status {statusDescription}. A visible browser session will be used once to refresh the authenticated browser state.");
            }

            if (statusCode is 401 or 403 or 429)
            {
                return new RpolAuthException(
                    RpolAuthFailureKind.RpolBlocked,
                    $"RPoL blocked authenticated access to '{uri}' with status {statusDescription}. Check credentials, account access, rate limits, or site-side blocking.");
            }

            return new RpolAuthException(
                RpolAuthFailureKind.RemoteUnavailable,
                $"RPoL request to '{uri}' failed with status {statusDescription}.");
        }

        private static bool IsCloudflareChallengeResponse(
            int statusCode,
            string? responseUrl,
            IReadOnlyDictionary<string, string>? responseHeaders,
            string? responseBody)
        {
            return statusCode == 403
                && ((!string.IsNullOrWhiteSpace(responseUrl)
                        && responseUrl.Contains("__cf_chl", StringComparison.OrdinalIgnoreCase))
                    || (responseHeaders is not null
                        && responseHeaders.Keys.Any(header => header.StartsWith("cf-", StringComparison.OrdinalIgnoreCase)))
                    || (!string.IsNullOrWhiteSpace(responseBody)
                        && (responseBody.Contains("cf-challenge", StringComparison.OrdinalIgnoreCase)
                            || responseBody.Contains("cf_clearance", StringComparison.OrdinalIgnoreCase)
                            || responseBody.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
                            || responseBody.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase))));
        }

        private static bool TryCacheFatalAuthFailure(Exception ex, out RpolAuthException cachedException)
        {
            if (ex is RpolAuthException authException && IsFatalAuthFailure(authException))
            {
                cachedException = CacheFatalAuthFailure(authException);
                return true;
            }

            cachedException = null!;
            return false;
        }

        internal static bool ShouldRetryWithHeadedBrowser(RpolAuthException exception, int attempt)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return attempt == 0
                && exception.Kind is RpolAuthFailureKind.CloudflareChallenge
                    or RpolAuthFailureKind.LoginRejected;
        }

        internal static bool ShouldAwaitManualExternalLogin(int loginSubmissionCount)
        {
            return loginSubmissionCount > 0;
        }

        internal static bool ShouldTreatExternalPageAsLogin(string html)
        {
            ArgumentNullException.ThrowIfNull(html);
            return LooksLikeLoginPage(html)
                && !RpolSnapshotUtility.IsUsableCampaignSnapshotHtml(RpolSnapshotUtility.SanitizeHtml(html));
        }

        internal static bool IsFatalAuthFailure(RpolAuthException exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            return exception.Kind is RpolAuthFailureKind.MissingCredentials
                or RpolAuthFailureKind.PlaywrightUnavailable
                or RpolAuthFailureKind.TransportSecurityFailure
                or RpolAuthFailureKind.LoginRejected
                or RpolAuthFailureKind.AuthSessionExpired
                or RpolAuthFailureKind.RpolBlocked
                or RpolAuthFailureKind.UntrustedNavigation
                or RpolAuthFailureKind.UnexpectedProtectedContent;
        }

        private static RpolAuthException CacheFatalAuthFailure(RpolAuthException exception)
        {
            if (_cachedFatalAuthFailure is not null)
            {
                return _cachedFatalAuthFailure;
            }

            _cachedFatalAuthFailure = exception;
            if (!_cachedFatalAuthFailureLogged)
            {
                StartupLoggingUtility.Append("RPOL authentication", exception);
                _cachedFatalAuthFailureLogged = true;
            }

            return _cachedFatalAuthFailure;
        }

        private static void ThrowIfCachedFatalAuthFailure()
        {
            if (_cachedFatalAuthFailure is not null)
            {
                throw _cachedFatalAuthFailure;
            }
        }

        private static async Task ResetSessionAsync(CancellationToken cancellationToken = default)
        {
            await SessionSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_session is null)
                {
                    return;
                }

                var session = _session;
                _session = null;
                await session.DisposeAsync(cancellationToken);
            }
            finally
            {
                SessionSemaphore.Release();
            }
        }

        private static void RegisterProcessExitHandler()
        {
            if (_processExitRegistered)
            {
                return;
            }

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                ResetSessionAsync().GetAwaiter().GetResult();
            };
            _processExitRegistered = true;
        }

        private sealed class RpolBrowserSession : IAsyncDisposable
        {
            private readonly Process? _externalBrowserProcess;
            private readonly string? _profileDirectory;
            private readonly RpolExternalProfileLease? _profileLease;

            public RpolBrowserSession(
                IPlaywright playwright,
                IBrowser browser,
                IBrowserContext context,
                Process? externalBrowserProcess = null,
                string? profileDirectory = null,
                RpolExternalProfileLease? profileLease = null)
            {
                Playwright = playwright;
                Browser = browser;
                Context = context;
                _externalBrowserProcess = externalBrowserProcess;
                _profileDirectory = profileDirectory;
                _profileLease = profileLease;
            }

            public IPlaywright Playwright { get; }

            public IBrowser Browser { get; }

            public IBrowserContext Context { get; }

            public async ValueTask DisposeAsync()
            {
                await DisposeAsync(CancellationToken.None);
            }

            public async ValueTask DisposeAsync(CancellationToken cancellationToken)
            {
                var cleanupErrors = new List<Exception>();
                try
                {
                    await Context.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (Exception ex)
                {
                    cleanupErrors.Add(new InvalidOperationException("RPOL browser context cleanup failed.", ex));
                }

                try
                {
                    await Browser.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (Exception ex)
                {
                    cleanupErrors.Add(new InvalidOperationException("RPOL browser cleanup failed.", ex));
                }

                if (_externalBrowserProcess is not null)
                {
                    try
                    {
                        if (!_externalBrowserProcess.HasExited)
                        {
                            _externalBrowserProcess.Kill(entireProcessTree: true);
                            if (!_externalBrowserProcess.WaitForExit(5000))
                            {
                                cleanupErrors.Add(new TimeoutException("RPOL external browser cleanup exceeded its bound."));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        cleanupErrors.Add(new InvalidOperationException("RPOL external browser cleanup failed.", ex));
                    }
                    finally
                    {
                        _externalBrowserProcess.Dispose();
                    }
                }

                if (!string.IsNullOrWhiteSpace(_profileDirectory))
                {
                    try
                    {
                        TerminateExternalBrowserProcessesForProfile(_profileDirectory);
                        if (_profileLease is not null)
                        {
                            _profileLease.Dispose();
                        }
                        else
                        {
                            RpolExternalProfileCleanup.CleanupProfile(_profileDirectory, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        cleanupErrors.Add(new InvalidOperationException("RPOL temporary browser profile cleanup failed.", ex));
                    }
                }

                try
                {
                    Playwright.Dispose();
                }
                catch (Exception ex)
                {
                    cleanupErrors.Add(new InvalidOperationException("RPOL Playwright cleanup failed.", ex));
                }
                if (cleanupErrors.Count > 0)
                {
                    throw new AggregateException("RPOL browser session cleanup did not complete.", cleanupErrors);
                }
            }
        }

        private sealed record RpolBrowserLaunch(IBrowser Browser, bool UseDefaultUserAgent);

        private static async Task WaitForPlaywrightAsync(
            Task task,
            string operationDescription,
            CancellationToken cancellationToken)
        {
            try
            {
                await task.WaitAsync(PlaywrightOperationTimeout, cancellationToken);
            }
            catch (PlaywrightException ex) when (IsTransportSecurityFailureMessage(ex.Message))
            {
                throw CreateTransportSecurityException(ex);
            }
            catch (TimeoutException ex)
            {
                ObserveLatePlaywrightTask(task, operationDescription);
                throw new TimeoutException(
                    $"Timed out after {PlaywrightOperationTimeout.TotalSeconds:0} seconds while {operationDescription}.",
                    ex);
            }
            catch (OperationCanceledException)
            {
                ObserveLatePlaywrightTask(task, operationDescription);
                throw;
            }
        }

        private static async Task<T> WaitForPlaywrightAsync<T>(
            Task<T> task,
            string operationDescription,
            CancellationToken cancellationToken)
        {
            try
            {
                return await task.WaitAsync(PlaywrightOperationTimeout, cancellationToken);
            }
            catch (PlaywrightException ex) when (IsTransportSecurityFailureMessage(ex.Message))
            {
                throw CreateTransportSecurityException(ex);
            }
            catch (TimeoutException ex)
            {
                ObserveLatePlaywrightResource(task, operationDescription);
                throw new TimeoutException(
                    $"Timed out after {PlaywrightOperationTimeout.TotalSeconds:0} seconds while {operationDescription}.",
                    ex);
            }
            catch (OperationCanceledException)
            {
                ObserveLatePlaywrightResource(task, operationDescription);
                throw;
            }
        }

        private static void ObserveLatePlaywrightTask(Task task, string operationDescription)
        {
            TrackLatePlaywrightCleanupTask(task.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                    {
                        _ = completed.Exception;
                        return;
                    }

                    if (!completed.IsCanceled)
                    {
                        StartupLoggingUtility.Append(
                            "RPOL late Playwright operation completed after cancellation",
                            operationDescription);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default));
        }

        private static void ObserveLatePlaywrightResource<T>(Task<T> task, string operationDescription)
        {
            TrackLatePlaywrightCleanupTask(task.ContinueWith(
                completed =>
                {
                    if (completed.IsCanceled)
                    {
                        return Task.CompletedTask;
                    }

                    if (completed.IsFaulted)
                    {
                        _ = completed.Exception;
                        return Task.CompletedTask;
                    }

                    return DisposeLatePlaywrightResourceAsync(completed.Result, operationDescription);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default).Unwrap());
        }

        private static void TrackLatePlaywrightCleanupTask(Task task)
        {
            LatePlaywrightCleanupTasks.TryAdd(task, 0);
        }

        private static async Task AwaitLatePlaywrightCleanupAsync(CancellationToken cancellationToken)
        {
            var cleanupErrors = new List<Exception>();
            while (LatePlaywrightCleanupTasks.Count > 0)
            {
                var stoppedForCancellation = false;
                foreach (var task in LatePlaywrightCleanupTasks.Keys.ToArray())
                {
                    try
                    {
                        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
                    {
                        cleanupErrors.Add(new InvalidOperationException(
                            "Late RPOL Playwright cleanup exceeded the shared cleanup deadline.", ex));
                        stoppedForCancellation = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        cleanupErrors.Add(new InvalidOperationException(
                            "A late RPOL Playwright operation did not complete cleanly.", ex));
                    }
                    finally
                    {
                        if (task.IsCompleted)
                        {
                            LatePlaywrightCleanupTasks.TryRemove(task, out _);
                        }
                    }
                }

                if (stoppedForCancellation)
                {
                    break;
                }
            }

            if (cleanupErrors.Count > 0)
            {
                throw new AggregateException("Late RPOL Playwright cleanup failed.", cleanupErrors);
            }
        }

        private static async Task DisposeLatePlaywrightResourceAsync(object? resource, string operationDescription)
        {
            try
            {
                switch (resource)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync();
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
            catch (Exception ex)
            {
                StartupLoggingUtility.Append(
                    "RPOL late Playwright resource cleanup failed",
                    $"{operationDescription}: {SensitiveTextRedactionUtility.Redact(ex.Message)}");
                throw;
            }
        }
    }

    internal sealed record RpolWebViewVerificationRequest(
        string GameForumUrl,
        string UserName,
        string Password,
        TimeSpan MaxWait);

    internal sealed record RpolLoginFormSnapshot(
        string? Action,
        string? Method,
        string? Target,
        bool SameFrame);

    internal sealed record RpolNavigationDomSnapshot(
        string Identity,
        string Html);
}
