using Microsoft.Playwright;
using System.Text.Json;

namespace PlayerAssistant.Tests;

internal static partial class TestCases
{
    internal static void RpolNavigationStabilityObservesDelayedRedirectAndDomReplacement()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var observations = 0;
        var final = RpolNavigationStability.WaitForStableAsync(
            _ =>
            {
                observations++;
                var elapsed = stopwatch.Elapsed;
                if (elapsed < TimeSpan.FromMilliseconds(60))
                {
                    return Task.FromResult(new RpolNavigationSnapshot(
                        new Uri("https://rpol.net/game.php"), "root-a", "<body>a</body>"));
                }

                if (elapsed < TimeSpan.FromMilliseconds(140))
                {
                    return Task.FromResult(new RpolNavigationSnapshot(
                        new Uri("https://rpol.net/game.php"), "root-b", "<body>delayed-dom</body>"));
                }

                if (elapsed < TimeSpan.FromMilliseconds(220))
                {
                    return Task.FromResult(new RpolNavigationSnapshot(
                        new Uri("https://rpol.net/game.php"), "root-b2", "<body>second-delayed-dom</body>"));
                }

                return Task.FromResult(new RpolNavigationSnapshot(
                    new Uri("https://rpol.net/login.cgi"), "root-c", "<body>redirected</body>"));
            },
            quietPeriod: TimeSpan.FromMilliseconds(80),
            maximumWait: TimeSpan.FromMilliseconds(700),
            pollInterval: TimeSpan.FromMilliseconds(20),
            CancellationToken.None).GetAwaiter().GetResult();

        AssertTrue(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(300), "stability must wait through the delayed client changes");
        AssertTrue(final.Url is not null && final.Url == new Uri("https://rpol.net/login.cgi"), "a delayed redirect must be visible in the settled result");
        AssertEqual("root-c", final.DomIdentity, "a delayed DOM replacement must be visible in the settled result");

        var continuouslyChanging = 0;
        var timedOut = false;
        try
        {
            _ = RpolNavigationStability.WaitForStableAsync(
                _ => Task.FromResult(new RpolNavigationSnapshot(
                    new Uri("https://rpol.net/game.php"),
                    $"root-{Interlocked.Increment(ref continuouslyChanging)}",
                    "<body>changing</body>")),
                quietPeriod: TimeSpan.FromMilliseconds(40),
                maximumWait: TimeSpan.FromMilliseconds(180),
                pollInterval: TimeSpan.FromMilliseconds(15),
                CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            timedOut = true;
        }

        AssertTrue(timedOut, "a client that keeps changing must fail the bounded stability contract");
    }

    internal static void RpolCredentialSubmissionIsBehaviorallyAtomicAgainstEventMutation()
    {
        RunCredentialFixtureScenario(null, expectSubmission: true);
        RunCredentialFixtureScenario(
            "document.forms[0].addEventListener('input', event => { if (event.target.name === 'username') document.forms[0].action = '/evil.cgi'; });",
            expectSubmission: false);
        RunCredentialFixtureScenario(
            "document.forms[0].addEventListener('change', event => { if (event.target.name === 'password') document.forms[0].method = 'GET'; });",
            expectSubmission: false);
        RunCredentialFixtureScenario(
            "document.forms[0].addEventListener('click', event => { if (event.target.name === 'perm') document.forms[0].target = '_blank'; });",
            expectSubmission: false);
        RunCredentialFixtureScenario(
            "document.forms[0].addEventListener('submit', () => { document.forms[0].action = '/evil.cgi'; document.forms[0].method = 'GET'; document.forms[0].target = '_blank'; });",
            expectSubmission: false);
    }

    internal static void RpolCredentialGuardRemainsArmedUntilRequestOrBoundedFailure()
    {
        using var guard = new RpolCredentialSubmissionGuard();
        var wait = guard.WaitForRequestAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        Thread.Sleep(50);
        AssertTrue(guard.IsArmed, "the transmission guard must remain armed while the request is still pending");
        AssertFalse(wait.IsCompleted, "guard teardown must not complete before the request boundary");
        AssertTrue(guard.Complete(true), "the first validated request must complete the guard");
        AssertTrue(wait.GetAwaiter().GetResult(), "the guard must report the validated request");
        AssertFalse(guard.IsArmed, "the guard must disarm only after the request boundary");
        AssertFalse(guard.Complete(true), "a second request cannot reuse a completed transmission guard");

        using var cancelledGuard = new RpolCredentialSubmissionGuard();
        var cancelledWait = cancelledGuard.WaitForRequestAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        cancelledGuard.Dispose();
        AssertFalse(cancelledWait.GetAwaiter().GetResult(), "bounded guard teardown must fail closed without a request");
    }

    internal static void RpolProtectedProbeFixtureObservesRefererResponseAndDelayedDom()
    {
        RunProtectedProbeFixtureAsync().GetAwaiter().GetResult();
    }

    private static void RunCredentialFixtureScenario(string? mutationScript, bool expectSubmission)
    {
        RunCredentialFixtureScenarioAsync(mutationScript, expectSubmission).GetAwaiter().GetResult();
    }

    private static async Task RunCredentialFixtureScenarioAsync(string? mutationScript, bool expectSubmission)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync();
        var loginRequests = 0;
        string? submittedMethod = null;
        await context.RouteAsync("https://rpol.net/**", async route =>
        {
            if (route.Request.Url.Contains("/login.cgi", StringComparison.Ordinal))
            {
                loginRequests++;
                submittedMethod = route.Request.Method;
            }

            var body = route.Request.Url.Contains("/game.php", StringComparison.Ordinal)
                ? "<html><body><form action='/login.cgi' method='post' target='_self'><input name='username'><input name='password' type='password'><input name='perm' type='checkbox'><input name='specialaction' value='Login' type='submit'></form></body></html>"
                : "<html><body>fixture response</body></html>";
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "text/html",
                Body = body
            });
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync("https://rpol.net/game.php?gi=80170", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (mutationScript is not null)
        {
            await page.EvaluateAsync($"() => {{ {mutationScript} }}");
        }

        var result = await page.Locator("form").EvaluateAsync<bool>(
            RpolCredentialSubmissionScript.Source,
            new { userName = "fixture-user", password = "fixture-password" });
        await page.WaitForTimeoutAsync(75);

        AssertEqual(expectSubmission, result, expectSubmission
            ? "the exact fixture form should submit"
            : "a form mutation during an input/change/click handler must abort submission");
        AssertEqual(expectSubmission ? 1 : 0, loginRequests, "the fixture must observe exactly the permitted login request count");
        if (expectSubmission)
        {
            AssertTrue(string.Equals("POST", submittedMethod, StringComparison.Ordinal), "the permitted fixture submission must remain POST");
        }
    }

    internal static void RpolProtectedProbeAcceptsDiceRollerShellWithoutRollHistory()
    {
        var html = "<html><head><title>Dice Roller - World of Issenda - Scarlet Horizons - RPoL</title></head><body><form action='/login.cgi'><input name='username'><input name='password' type='password'></form><div>Step 1: Choose the Dice</div><div>Roll the Dice</div></body></html>";
        var classification = RpolProtectedResourceUtility.Classify(
            RpolAuthUtility.ProtectedDiceRollerUri,
            RpolAuthUtility.ProtectedDiceRollerUri,
            200,
            "text/html; charset=utf-8",
            html);
        AssertEqual(RpolProtectedResourceKind.AuthenticatedProtectedContent, classification.Kind, "the protected Dice Roller shell must prove authentication without roll history");
    }

    private static async Task RunProtectedProbeFixtureAsync()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync();
        string? observedReferer = null;
        IResponse? observedResponse = null;
        await context.RouteAsync("https://rpol.net/**", async route =>
        {
            if (route.Request.Url.Equals(RpolAuthUtility.ProtectedDiceRollerUri.AbsoluteUri, StringComparison.Ordinal))
            {
                observedReferer = route.Request.Headers.TryGetValue("referer", out var referer) ? referer : null;
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "text/html; charset=utf-8",
                    Headers = new Dictionary<string, string> { ["x-fixture-proof"] = "observed" },
                    Body = "<html><head><title>Dice Roller - World of Issenda - Scarlet Horizons - RPoL</title></head><body><form action='/search.cgi'><input name='q'></form><form action='/login.cgi' method='post'><input name='username'><input name='password' type='password'></form><div>Step 1: Choose the Dice</div><div>Roll the Dice</div><div id='roll-log'>Kelpie rolled 1d20 using d20. [roll=1.2.3]</div><script>setTimeout(() => { document.querySelector('#roll-log').textContent = 'Kelpie rolled 1d20 using d20. [roll=after-one-second]'; }, 1250);</script></body></html>"
                });
                return;
            }

            await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "text/html", Body = "<html></html>" });
        });

        var page = await context.NewPageAsync();
        observedResponse = await page.GotoAsync(
            RpolAuthUtility.ProtectedDiceRollerUri.AbsoluteUri,
            new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Referer = AppSettingsUtility.GameForumUrl
            });
        AssertTrue(observedResponse is not null, "the fixture must return an actual response object");
        AssertEqual(200, observedResponse!.Status, "the fixture response status must be observed");
        AssertTrue(observedResponse.Headers.ContainsKey("x-fixture-proof"), "the fixture response header must be observed");
        AssertTrue(string.Equals(AppSettingsUtility.GameForumUrl, observedReferer, StringComparison.Ordinal), "the exact game-page Referer must be observed on the protected request");

        var stable = await RpolNavigationStability.WaitForStableAsync(
            async token =>
            {
                var dom = await page.EvaluateAsync<JsonElement>("""
                    () => {
                        const root = document.documentElement;
                        if (!root) return { identity: 'missing', html: '' };
                        if (!root.dataset.fixtureIdentity) root.dataset.fixtureIdentity = 'fixture-root';
                        return { identity: root.dataset.fixtureIdentity, html: root.outerHTML };
                    }
                    """);
                return new RpolNavigationSnapshot(
                    new Uri(page.Url),
                    dom.GetProperty("identity").GetString() ?? "missing",
                    dom.GetProperty("html").GetString() ?? "");
            },
            quietPeriod: TimeSpan.FromMilliseconds(100),
            maximumWait: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(25),
            CancellationToken.None);

        AssertTrue(stable.Html.Contains("after-one-second", StringComparison.Ordinal), "the fixture stability contract must include a mutation after the former one-second quiet period");
        var classification = RpolProtectedResourceUtility.ClassifyEvidence(new RpolProtectedProbeEvidence(
            RpolAuthUtility.ProtectedDiceRollerUri,
            new Uri(observedResponse.Url),
            new Uri(page.Url),
            observedResponse.Status,
            observedResponse.Headers.TryGetValue("content-type", out var contentType) ? contentType : null,
            stable.Html,
            observedReferer,
            SettledAfterStabilization: true));
        AssertEqual(RpolProtectedResourceKind.AuthenticatedProtectedContent, classification.Kind, "the real intercepted fixture must satisfy the protected-content contract");
    }
}
