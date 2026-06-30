using System.Text;
using Microsoft.Playwright;

namespace PlayerAssistant
{
    internal sealed record RpolResponse(byte[] Body, string? ContentType);

    internal static class RpolAuthUtility
    {
        private const string SettingsLocalFileName = "settings.local.json";
        private const string StorageStateFileName = "rpol-storage-state.json";
        private const string DesktopChromeUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36";
        private static readonly TimeSpan PlaywrightOperationTimeout = TimeSpan.FromSeconds(30);
        private static readonly SemaphoreSlim SessionSemaphore = new(1, 1);
        private static RpolBrowserSession? _session;
        private static bool _processExitRegistered;

        public static bool IsRpolUri(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            return string.Equals(uri.Host, "rpol.net", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".rpol.net", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<string> GetHtmlFromUrlAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var attempt = 0; attempt < 2; attempt++)
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

            throw new InvalidOperationException(
                $"Unable to authenticate against rpol.net. Add credentials to '{SettingsLocalFileName}' and retry.");
        }

        public static async Task<RpolResponse> GetResponseAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var attempt = 0; attempt < 2; attempt++)
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

            throw new InvalidOperationException(
                $"Unable to authenticate against rpol.net. Add credentials to '{SettingsLocalFileName}' and retry.");
        }

        private static async Task<IBrowserContext> GetAuthenticatedContextAsync(CancellationToken cancellationToken)
        {
            await SessionSemaphore.WaitAsync(cancellationToken);
            try
            {
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
            var storageStatePath = GetStorageStatePath();
            Environment.SetEnvironmentVariable("NODE_OPTIONS", "--use-system-ca");
            var playwright = await WaitForPlaywrightAsync(
                Playwright.CreateAsync(),
                "starting the RPOL browser session",
                cancellationToken);
            var browser = await WaitForPlaywrightAsync(
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
 
            try
            {
                var contextOptions = File.Exists(storageStatePath)
                    ? new BrowserNewContextOptions
                    {
                        IgnoreHTTPSErrors = true,
                        StorageStatePath = storageStatePath,
                        UserAgent = DesktopChromeUserAgent
                    }
                    : new BrowserNewContextOptions
                    {
                        IgnoreHTTPSErrors = true,
                        UserAgent = DesktopChromeUserAgent
                    };
                var context = await WaitForPlaywrightAsync(
                    browser.NewContextAsync(contextOptions),
                    "creating the RPOL browser context",
                    cancellationToken);
                await WaitForPlaywrightAsync(
                    context.AddInitScriptAsync("""
                    Object.defineProperty(navigator, 'webdriver', {
                        get: () => undefined
                    });
                    """),
                    "configuring the RPOL browser context",
                    cancellationToken);

                await EnsureLoggedInAsync(context, userName, password, cancellationToken);
                await WaitForPlaywrightAsync(
                    context.StorageStateAsync(new BrowserContextStorageStateOptions
                    {
                        Path = storageStatePath
                    }),
                    "saving the RPOL browser storage state",
                    cancellationToken);

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
                    throw new InvalidOperationException("RPoL login was rejected. Check the configured credentials.");
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
            var userName = AppSettingsUtility.RpolUserName;
            var password = AppSettingsUtility.RpolPassword;

            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    $"Missing RPoL credentials. Add 'RPOL user name' and 'RPOL password' to '{SettingsLocalFileName}'.");
            }

            return (userName, password);
        }

        private static bool LooksLikeLoginResponse(string? contentType, byte[] body)
        {
            if (contentType is null ||
                !contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return LooksLikeLoginPage(Encoding.UTF8.GetString(body));
        }

        private static bool LooksLikeLoginPage(string html)
        {
            return html.Contains("action='/login.cgi'", StringComparison.OrdinalIgnoreCase)
                && html.Contains("name='username'", StringComparison.OrdinalIgnoreCase)
                && html.Contains("name='password'", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetStorageStatePath()
        {
            return Path.Combine(AppContext.BaseDirectory, StorageStateFileName);
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
                return await WaitForPlaywrightAsync(
                    page.ContentAsync(),
                    $"reading HTML from '{uri}'",
                    cancellationToken);
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

                return new RpolResponse(
                    await WaitForPlaywrightAsync(
                        response.BodyAsync(),
                        $"reading the response body from '{uri}'",
                        cancellationToken),
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
                throw new InvalidOperationException($"RPoL navigation to '{uri}' did not return a response.");
            }

            if (!response.Ok)
            {
                throw new HttpRequestException(
                    $"RPoL request to '{uri}' failed with status {response.Status} {response.StatusText}.");
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
