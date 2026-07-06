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
        LoginRejected,
        AuthSessionExpired,
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
        private static readonly SemaphoreSlim SessionSemaphore = new(1, 1);
        private static RpolBrowserSession? _session;
        private static RpolAuthException? _cachedFatalAuthFailure;
        private static bool _cachedFatalAuthFailureLogged;
        private static bool _processExitRegistered;

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
                        continue;
                    }

                    return html;
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
            cancellationToken.ThrowIfCancellationRequested();
            NetworkUrlAllowlistUtility.EnsureAllowed(uri, NetworkUrlPurpose.Rpol);
            ThrowIfCachedFatalAuthFailure();

            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var context = await GetAuthenticatedContextAsync(cancellationToken);
                    var response = await GetPageResponseAsync(context, uri, cancellationToken);

                    if (LooksLikeLoginResponse(response.ContentType, response.Body))
                    {
                        await ResetSessionAsync(cancellationToken);
                        continue;
                    }

                    return response;
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
                    _session = await CreateAuthenticatedSessionAsync(cancellationToken);
                }

                return _session.Context;
            }
            finally
            {
                SessionSemaphore.Release();
            }
        }

        private static async Task<RpolBrowserSession> CreateAuthenticatedSessionAsync(CancellationToken cancellationToken)
        {
            var (userName, password) = GetCredentials();
            Environment.SetEnvironmentVariable("NODE_OPTIONS", "--use-system-ca");
            IPlaywright playwright;
            IBrowser browser;
            try
            {
                playwright = await WaitForPlaywrightAsync(
                    Playwright.CreateAsync(),
                    "starting the RPOL browser session",
                    cancellationToken);
                browser = await WaitForPlaywrightAsync(
                    playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = true,
                        Args =
                        [
                            "--disable-blink-features=AutomationControlled"
                        ]
                    }),
                    "launching the RPOL browser",
                    cancellationToken);
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
                    var contextOptions = preparedStorageStatePath is not null
                        ? new BrowserNewContextOptions
                        {
                            IgnoreHTTPSErrors = true,
                            StorageStatePath = preparedStorageStatePath,
                            UserAgent = DesktopChromeUserAgent
                        }
                        : new BrowserNewContextOptions
                        {
                            IgnoreHTTPSErrors = true,
                            UserAgent = DesktopChromeUserAgent
                        };
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

                await EnsureLoggedInAsync(context, userName, password, cancellationToken);
                await SaveStorageStateSecretAsync(context, cancellationToken);

                return new RpolBrowserSession(playwright, browser, context);
            }
            catch
            {
                await browser.CloseAsync();
                playwright.Dispose();
                throw;
            }
        }

        private static async Task EnsureLoggedInAsync(
            IBrowserContext context,
            string userName,
            string password,
            CancellationToken cancellationToken)
        {
            var page = await WaitForPlaywrightAsync(
                context.NewPageAsync(),
                "opening the RPOL login page",
                cancellationToken);
            try
            {
                await WaitForPlaywrightAsync(
                    page.GotoAsync(AppSettingsUtility.GameForumUrl, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = (float)PlaywrightOperationTimeout.TotalMilliseconds
                    }),
                    $"loading '{AppSettingsUtility.GameForumUrl}'",
                    cancellationToken);

                if (!await HasLoginFormAsync(page, cancellationToken))
                {
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
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

                if (await HasLoginFormAsync(page, cancellationToken))
                {
                    throw new RpolAuthException(
                        RpolAuthFailureKind.LoginRejected,
                        "RPoL login was rejected. Check the configured credentials.");
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

        private static (string UserName, string Password) GetCredentials()
        {
            if (!AppSettingsUtility.TryGetRpolCredentials(out var userName, out var password)
                || string.IsNullOrWhiteSpace(userName)
                || string.IsNullOrWhiteSpace(password))
            {
                throw new RpolAuthException(
                    RpolAuthFailureKind.MissingCredentials,
                    "Missing RPoL credentials. Open Settings > RPOL Credentials and store both values in Windows Credential Manager.");
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

            return LooksLikeLoginPage(Encoding.UTF8.GetString(body));
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
                var response = await WaitForPlaywrightAsync(
                    page.GotoAsync(uri.ToString(), new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = (float)PlaywrightOperationTimeout.TotalMilliseconds
                    }),
                    $"loading '{uri}'",
                    cancellationToken);
                EnsureSuccessfulResponse(uri, response);
                var html = await WaitForPlaywrightAsync(
                    page.ContentAsync(),
                    $"reading HTML from '{uri}'",
                    cancellationToken);
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
                var response = await WaitForPlaywrightAsync(
                    page.GotoAsync(uri.ToString(), new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = (float)PlaywrightOperationTimeout.TotalMilliseconds
                    }),
                    $"loading '{uri}'",
                    cancellationToken);
                EnsureSuccessfulResponse(uri, response);
                var contentType = response!.Headers.TryGetValue("content-type", out var headerValue)
                    ? headerValue
                    : null;

                var body = await WaitForPlaywrightAsync(
                    response.BodyAsync(),
                    $"reading the response body from '{uri}'",
                    cancellationToken);
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

        private static void EnsureSuccessfulResponse(Uri uri, IResponse? response)
        {
            if (response is null)
            {
                throw new RpolAuthException(
                    RpolAuthFailureKind.RemoteUnavailable,
                    $"RPoL navigation to '{uri}' did not return a response.");
            }

            if (!response.Ok)
            {
                throw CreateUnsuccessfulResponseException(uri, response.Status, response.StatusText);
            }
        }

        internal static RpolAuthException CreateUnsuccessfulResponseException(
            Uri uri,
            int statusCode,
            string? statusText)
        {
            ArgumentNullException.ThrowIfNull(uri);

            var statusDescription = string.IsNullOrWhiteSpace(statusText)
                ? statusCode.ToString()
                : $"{statusCode} {statusText}";
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

        internal static bool IsFatalAuthFailure(RpolAuthException exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            return exception.Kind is RpolAuthFailureKind.MissingCredentials
                or RpolAuthFailureKind.PlaywrightUnavailable
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
            public RpolBrowserSession(IPlaywright playwright, IBrowser browser, IBrowserContext context)
            {
                Playwright = playwright;
                Browser = browser;
                Context = context;
            }

            public IPlaywright Playwright { get; }

            public IBrowser Browser { get; }

            public IBrowserContext Context { get; }

            public async ValueTask DisposeAsync()
            {
                await Context.CloseAsync();
                await Browser.CloseAsync();
                Playwright.Dispose();
            }
        }

        private static async Task WaitForPlaywrightAsync(
            Task task,
            string operationDescription,
            CancellationToken cancellationToken)
        {
            try
            {
                await task.WaitAsync(PlaywrightOperationTimeout, cancellationToken);
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
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    $"Timed out after {PlaywrightOperationTimeout.TotalSeconds:0} seconds while {operationDescription}.",
                    ex);
            }
        }
    }
}
