using System.Diagnostics;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
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

        internal static Func<RpolWebViewVerificationRequest, CancellationToken, Task<string?>>? WebViewVerificationHandler { get; set; }

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
            CancellationToken cancellationToken = default)
        {
            return await GetResponseAsync(uri, allowEmbeddedLoginForm: true, cancellationToken);
        }

        private static async Task<RpolResponse> GetResponseAsync(
            Uri uri,
            bool allowEmbeddedLoginForm,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NetworkUrlAllowlistUtility.EnsureAllowed(uri, NetworkUrlPurpose.Rpol);
            ThrowIfCachedFatalAuthFailure();

            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var context = await GetAuthenticatedContextAsync(cancellationToken);
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

        private static async Task<IBrowserContext> GetAuthenticatedContextAsync(CancellationToken cancellationToken)
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
                    _session = await CreateAuthenticatedSessionAsync(cancellationToken, clearCloudflareChallenge);
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
            bool clearCloudflareChallenge = false)
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

                        RuntimeSecretStoreUtility.SaveRpolStorageState(storageStateJson);
                        playwright.Dispose();
                        return await CreateAuthenticatedSessionAsync(cancellationToken);
                    }

                    return await RefreshStorageStateWithExternalBrowserAsync(
                        playwright,
                        userName,
                        password,
                        cancellationToken);
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
                await SaveStorageStateSecretAsync(context, cancellationToken);
                if (clearCloudflareChallenge)
                {
                    await context.CloseAsync();
                    await browser.CloseAsync();
                    playwright.Dispose();
                    return await CreateAuthenticatedSessionAsync(cancellationToken);
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
            CancellationToken cancellationToken)
        {
            var browserPath = FindExternalBrowserExecutable()
                ?? throw new RpolAuthException(
                    RpolAuthFailureKind.PlaywrightUnavailable,
                    "RPOL browser verification requires installed Chrome or Microsoft Edge, but neither browser executable was found.");
            var tempDirectory = RuntimePathUtility.GetUserDataPath("temp");
            Directory.CreateDirectory(tempDirectory);
            var profileDirectory = RuntimePathUtility.CombineUnderBase(
                tempDirectory,
                $"{ExternalBrowserVerificationDirectoryName}-{Guid.NewGuid():N}");
            var noticePath = RuntimePathUtility.CombineUnderBase(
                tempDirectory,
                $"rpol-browser-verification-{Guid.NewGuid():N}.html");
            Directory.CreateDirectory(profileDirectory);
            File.WriteAllText(noticePath, CreateExternalBrowserNoticeHtml(), Encoding.UTF8);
            var remoteDebuggingPort = GetAvailableLoopbackPort();
            var verificationProcess = StartExternalBrowserForManualVerification(
                browserPath,
                remoteDebuggingPort,
                profileDirectory,
                noticePath);

            IBrowser? browser = null;
            try
            {
                browser = await ConnectToExternalBrowserAsync(
                    playwright,
                    remoteDebuggingPort,
                    cancellationToken);
                var context = browser.Contexts.FirstOrDefault()
                    ?? throw new RpolAuthException(
                        RpolAuthFailureKind.PlaywrightUnavailable,
                        "RPOL browser verification could not access the external browser context.");

                await CompleteExternalBrowserVerificationAsync(
                    context,
                    userName,
                    password,
                    cancellationToken);
                await SaveStorageStateSecretAsync(context, cancellationToken);
                DeleteTemporaryStorageStateFile(noticePath);
                return new RpolBrowserSession(
                    playwright,
                    browser,
                    context,
                    verificationProcess,
                    profileDirectory);
            }
            catch
            {
                if (browser is not null)
                {
                    try
                    {
                        await browser.CloseAsync();
                    }
                    catch
                    {
                    }
                }

                if (!verificationProcess.HasExited)
                {
                    try
                    {
                        verificationProcess.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }
                }

                DeleteTemporaryStorageStateFile(noticePath);
                DeleteTemporaryDirectory(profileDirectory);
                verificationProcess.Dispose();
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
                    if (ShouldTreatExternalPageAsLogin(html))
                    {
                        if (ShouldRejectPersistentExternalLoginPage(loginSubmissionCount))
                        {
                            throw new RpolAuthException(
                                RpolAuthFailureKind.LoginRejected,
                                "RPOL rejected the configured credentials in the temporary browser.");
                        }

                        await SubmitExternalBrowserLoginAsync(page, userName, password, cancellationToken);
                        loginSubmissionCount++;
                        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                        continue;
                    }

                    if (!LooksLikeCloudflareChallengePage(html))
                    {
                        if (!page.Url.StartsWith(gameForumUri.ToString(), StringComparison.OrdinalIgnoreCase))
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

                await Task.Delay(CloudflareClearancePollInterval, cancellationToken);
            }

            throw new RpolAuthException(
                RpolAuthFailureKind.CloudflareChallenge,
                $"RPOL browser verification did not complete within {CloudflareClearanceMaxWait.TotalMinutes:0} minutes. Complete the checkbox in the temporary Chrome or Edge window, keep that window open, and let Player Assistant finish saving the RPOL browser state.");
        }

        private static async Task<IPage> GetExternalRpolPageAsync(
            IBrowserContext context,
            Uri gameForumUri,
            CancellationToken cancellationToken)
        {
            var page = context.Pages.FirstOrDefault(page =>
                !page.IsClosed
                && page.Url.Contains("rpol.net", StringComparison.OrdinalIgnoreCase));
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
            await WaitForPlaywrightAsync(
                page.Locator("input[name='username']").FillAsync(userName),
                "filling the RPOL user name in the external browser",
                cancellationToken);
            await WaitForPlaywrightAsync(
                page.Locator("input[name='password']").FillAsync(password),
                "filling the RPOL password in the external browser",
                cancellationToken);

            var rememberMeInput = page.Locator("input[name='perm']");
            if (await WaitForPlaywrightAsync(
                    rememberMeInput.CountAsync(),
                    "checking the RPOL remember-me option in the external browser",
                    cancellationToken) > 0
                && !await WaitForPlaywrightAsync(
                    rememberMeInput.IsCheckedAsync(),
                    "reading the RPOL remember-me option in the external browser",
                    cancellationToken))
            {
                await WaitForPlaywrightAsync(
                    rememberMeInput.CheckAsync(),
                    "checking the RPOL remember-me option in the external browser",
                    cancellationToken);
            }

            await WaitForPlaywrightAsync(
                page.Locator("input[name='specialaction'][value='Login']").ClickAsync(),
                "submitting the RPOL login form in the external browser",
                cancellationToken);
            await WaitForPlaywrightAsync(
                page.WaitForLoadStateAsync(LoadState.DOMContentLoaded),
                "waiting for the RPOL login response in the external browser",
                cancellationToken);
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

        private static void DeleteTemporaryDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
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

                var userNameInput = page.Locator("input[name='username']");
                var passwordInput = page.Locator("input[name='password']");
                var rememberMeInput = page.Locator("input[name='perm']");
                var submitButton = page.Locator("input[name='specialaction'][value='Login']");

                await WaitForPlaywrightAsync(userNameInput.FillAsync(userName), "filling the RPOL user name", cancellationToken);
                await WaitForPlaywrightAsync(passwordInput.FillAsync(password), "filling the RPOL password", cancellationToken);
                if (await WaitForPlaywrightAsync(rememberMeInput.CountAsync(), "checking the RPOL remember-me option", cancellationToken) > 0
                    && !await WaitForPlaywrightAsync(rememberMeInput.IsCheckedAsync(), "reading the RPOL remember-me option", cancellationToken))
                {
                    await WaitForPlaywrightAsync(rememberMeInput.CheckAsync(), "checking the RPOL remember-me option", cancellationToken);
                }

                await WaitForPlaywrightAsync(submitButton.ClickAsync(), "submitting the RPOL login form", cancellationToken);
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
            return html.Contains("action='/login.cgi'", StringComparison.OrdinalIgnoreCase)
                && html.Contains("name='username'", StringComparison.OrdinalIgnoreCase)
                && html.Contains("name='password'", StringComparison.OrdinalIgnoreCase);
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
                var tempPath = RuntimePathUtility.CombineUnderBase(
                    tempDirectory,
                    $"rpol-storage-state-{Guid.NewGuid():N}.json");
                File.WriteAllText(tempPath, storageStateJson);
                File.SetLastWriteTimeUtc(tempPath, lastWritten.UtcDateTime);

                if (!TryPrepareStorageStateFile(tempPath, DateTimeOffset.Now))
                {
                    DeleteTemporaryStorageStateFile(tempPath);
                    RuntimeSecretStoreUtility.DeleteRpolStorageState();
                    return null;
                }

                return tempPath;
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or JsonException
                or InvalidOperationException)
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
            CancellationToken cancellationToken)
        {
            var tempDirectory = RuntimePathUtility.GetUserDataPath("temp");
            Directory.CreateDirectory(tempDirectory);
            var tempPath = RuntimePathUtility.CombineUnderBase(
                tempDirectory,
                $"rpol-storage-state-save-{Guid.NewGuid():N}.json");

            try
            {
                await WaitForPlaywrightAsync(
                    context.StorageStateAsync(new BrowserContextStorageStateOptions
                    {
                        Path = tempPath
                    }),
                    "saving the RPOL browser storage state",
                    cancellationToken);
                RuntimeSecretStoreUtility.SaveRpolStorageState(File.ReadAllText(tempPath));
            }
            finally
            {
                DeleteTemporaryStorageStateFile(tempPath);
            }
        }

        private static void DeleteTemporaryStorageStateFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
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
                StartupLoggingUtility.Append(
                    "RPOL storage state validation",
                    new InvalidOperationException(
                        $"RPOL browser auth state '{storageStatePath}' is invalid but could not be deleted.",
                        deleteException));
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
            return string.Equals(
                uri.AbsolutePath,
                "/usermodules/diceroller.cgi",
                StringComparison.OrdinalIgnoreCase)
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

        internal static bool ShouldRejectPersistentExternalLoginPage(int loginSubmissionCount)
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
                or RpolAuthFailureKind.RpolBlocked;
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
                await session.DisposeAsync();
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

            public RpolBrowserSession(
                IPlaywright playwright,
                IBrowser browser,
                IBrowserContext context,
                Process? externalBrowserProcess = null,
                string? profileDirectory = null)
            {
                Playwright = playwright;
                Browser = browser;
                Context = context;
                _externalBrowserProcess = externalBrowserProcess;
                _profileDirectory = profileDirectory;
            }

            public IPlaywright Playwright { get; }

            public IBrowser Browser { get; }

            public IBrowserContext Context { get; }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await Context.CloseAsync();
                    await Browser.CloseAsync();
                }
                finally
                {
                    if (_externalBrowserProcess is not null)
                    {
                        if (!_externalBrowserProcess.HasExited)
                        {
                            _externalBrowserProcess.Kill(entireProcessTree: true);
                        }

                        _externalBrowserProcess.Dispose();
                    }

                    if (!string.IsNullOrWhiteSpace(_profileDirectory))
                    {
                        DeleteTemporaryDirectory(_profileDirectory);
                    }

                    Playwright.Dispose();
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
                throw new TimeoutException(
                    $"Timed out after {PlaywrightOperationTimeout.TotalSeconds:0} seconds while {operationDescription}.",
                    ex);
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
                throw new TimeoutException(
                    $"Timed out after {PlaywrightOperationTimeout.TotalSeconds:0} seconds while {operationDescription}.",
                    ex);
            }
        }
    }

    internal sealed record RpolWebViewVerificationRequest(
        string GameForumUrl,
        string UserName,
        string Password,
        TimeSpan MaxWait);
}
