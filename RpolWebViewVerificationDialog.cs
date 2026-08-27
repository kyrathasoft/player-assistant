using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PlayerAssistant
{
    internal sealed class RpolWebViewVerificationDialog : Form
    {
        private const string WebViewUserDataDirectoryName = "rpol-webview2";
        private readonly RpolWebViewVerificationRequest _request;
        private readonly CancellationToken _cancellationToken;
        private readonly RpolWebViewLifetime _lifetime;
        private readonly WebView2 _webView;
        private readonly WebView2 _probeWebView;
        private readonly Label _statusLabel;
        private readonly Button _saveButton;
        private readonly Button _cancelButton;
        private bool _protectedProbeInProgress;
        private RpolWebViewProfileLease? _profileLease;
        private CancellationTokenRegistration _cancellationRegistration;
        private bool _resourcesClosed;
        private bool _credentialSubmissionArmed;
        private RpolCredentialSubmissionGuard? _credentialSubmissionGuard;
        private readonly object _eventTaskGate = new();
        private readonly HashSet<Task> _eventTasks = [];

        public RpolWebViewVerificationDialog(
            RpolWebViewVerificationRequest request,
            CancellationToken cancellationToken)
        {
            _request = request;
            _cancellationToken = cancellationToken;
            _lifetime = RpolWebViewLifetime.Create(request.MaxWait, cancellationToken);

            Text = "RPOL Browser Verification";
            Width = 1180;
            Height = 820;
            MinimumSize = new Size(900, 650);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 96,
                Padding = new Padding(16, 12, 16, 8),
                BackColor = Color.FromArgb(127, 29, 29)
            };
            var headerLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font(Font.FontFamily, 13.5f, FontStyle.Bold),
                Text = "Complete any RPOL browser verification in this window. If RPOL asks you to verify you are human, complete the prompt here. When RPOL is loaded or logged in, click Save RPOL State.",
                TextAlign = ContentAlignment.MiddleLeft
            };
            headerPanel.Controls.Add(headerLabel);

            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.White
            };
            _probeWebView = new WebView2
            {
                Visible = false,
                Width = 1,
                Height = 1
            };

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 64,
                Padding = new Padding(12),
                BackColor = SystemColors.Control
            };
            _statusLabel = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Text = "Starting RPOL verification browser...",
                TextAlign = ContentAlignment.MiddleLeft
            };
            _saveButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 150,
                Text = "Save RPOL State",
                Enabled = false
            };
            _cancelButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 96,
                Text = "Cancel"
            };
            footerPanel.Controls.Add(_statusLabel);
            footerPanel.Controls.Add(_saveButton);
            footerPanel.Controls.Add(_cancelButton);

            Controls.Add(_webView);
            Controls.Add(_probeWebView);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            _saveButton.Click += async (_, _) => await SaveBrowserStateAsync();
            _cancelButton.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Shown += async (_, _) => await InitializeWebViewAsync();
            FormClosed += (_, _) => CloseWebViewResources();
        }

        public string? StorageStateJson { get; private set; }

        internal Exception? CleanupFailure { get; private set; }

        internal static bool ShouldCancelNavigation(string? value)
        {
            return !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || !NetworkUrlAllowlistUtility.IsTrustedRpolNavigationUri(uri);
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                _cancellationRegistration = _lifetime.Token.Register(() =>
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke(() =>
                        {
                            DialogResult = DialogResult.Cancel;
                            Close();
                        });
                    }
                });

                _lifetime.ThrowIfNotAlive();
                var userDataFolder = RuntimePathUtility.GetUserDataPath(WebViewUserDataDirectoryName);
                _profileLease = RpolWebViewProfileLease.Acquire(userDataFolder);
                var environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolder);
                await _webView.EnsureCoreWebView2Async(environment);
                await _probeWebView.EnsureCoreWebView2Async(environment);
                _lifetime.ThrowIfNotAlive();

                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
                _webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Document);
                _webView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
                _webView.CoreWebView2.Navigate(_request.GameForumUrl);
                _statusLabel.Text = "Loading RPOL. Complete any verification prompt in this window.";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Unable to start WebView2 verification: {ex.Message}";
                _saveButton.Enabled = false;
            }
        }

        private void OnNavigationStarting(object? _, CoreWebView2NavigationStartingEventArgs args)
        {
            if (ShouldCancelNavigation(args.Uri))
            {
                args.Cancel = true;
                _statusLabel.Text = "Navigation was blocked because the destination is not an approved HTTPS RPOL page.";
            }
        }

        private void OnNavigationCompleted(object? _, CoreWebView2NavigationCompletedEventArgs __)
        {
            TrackEventTask(HandleNavigationCompletedAsync());
        }

        private async Task HandleNavigationCompletedAsync()
        {
            if (!_lifetime.IsAlive || IsDisposed) return;
            if (_webView.CoreWebView2.Source is { } uri
                && Uri.TryCreate(uri, UriKind.Absolute, out var currentUri)
                && NetworkUrlAllowlistUtility.IsTrustedRpolNavigationUri(currentUri))
            {
                var submitted = await TryAutoSubmitLoginAsync();
                if (!submitted)
                {
                    var classification = await TryVerifyProtectedResourceAsync();
                    _saveButton.Enabled = classification?.Kind == RpolProtectedResourceKind.AuthenticatedProtectedContent;
                }
            }
            else
            {
                _saveButton.Enabled = false;
            }
            _statusLabel.Text = BuildStatusText();
        }

        private void OnNewWindowRequested(object? _, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            _statusLabel.Text = "RPOL opened a new window, so the navigation was blocked.";
        }

        private void OnWebResourceRequested(object? _, CoreWebView2WebResourceRequestedEventArgs args)
        {
            var core = _webView.CoreWebView2;
            if (!_credentialSubmissionArmed || core is null) return;
            var topFrameUri = Uri.TryCreate(core.Source, UriKind.Absolute, out var currentUri)
                ? currentUri
                : null;
            var requestUri = Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var parsedRequestUri)
                ? parsedRequestUri
                : null;
            var referer = args.Request.Headers.GetHeader("Referer");
            var isMainFrame = args.ResourceContext == CoreWebView2WebResourceContext.Document
                && string.Equals(referer, _request.GameForumUrl, StringComparison.Ordinal);
            string? reason = null;
            var validRequest = topFrameUri is not null
                && requestUri is not null
                && RpolCredentialSubmissionPolicy.TryValidateCredentialRequest(
                    topFrameUri,
                    requestUri,
                    args.Request.Method,
                    isMainFrame,
                    out reason);
            if (!validRequest)
            {
                _credentialSubmissionArmed = false;
                _credentialSubmissionGuard?.Complete(false);
                args.Response = core.Environment.CreateWebResourceResponse(
                    null,
                    403,
                    "Blocked",
                    "Content-Type: text/plain\r\n");
                _statusLabel.Text = $"RPOL credential transmission was blocked: {reason ?? "invalid request"}";
                return;
            }

            _credentialSubmissionArmed = false;
            _credentialSubmissionGuard?.Complete(true);
        }

        private void TrackEventTask(Task task)
        {
            lock (_eventTaskGate) _eventTasks.Add(task);
            _ = task.ContinueWith(
                completed =>
                {
                    lock (_eventTaskGate) _eventTasks.Remove(completed);
                    if (completed.IsFaulted) _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void JoinEventTasks(CancellationToken cleanupToken)
        {
            Task[] tasks;
            lock (_eventTaskGate) tasks = _eventTasks.ToArray();
            if (tasks.Length == 0) return;
            try
            {
                Task.WhenAll(tasks).WaitAsync(cleanupToken).GetAwaiter().GetResult();
            }
            catch
            {
                foreach (var task in tasks.Where(task => !task.IsCompleted)) RpolCleanupUtility.TrackLateTask(task);
                throw;
            }
        }

        private void CloseWebViewResources()
        {
            if (_resourcesClosed)
            {
                return;
            }

            var errors = new List<Exception>();
            using var cleanupCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (_webView.CoreWebView2 is not null)
            {
                _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
                _webView.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
            }
            _credentialSubmissionArmed = false;
            errors.AddRange(RpolCleanupUtility.JoinLateTasksAsync(cleanupCancellation.Token).GetAwaiter().GetResult());
            try { JoinEventTasks(cleanupCancellation.Token); }
            catch (Exception ex) { errors.Add(new InvalidOperationException("WebView event handlers did not join before disposal.", ex)); }
            errors.AddRange(RpolCleanupUtility.DisposeIndependently(
                ("WebView cancellation registration", _cancellationRegistration.Dispose),
                ("hidden probe WebView", _probeWebView.Dispose),
                ("visible WebView", _webView.Dispose),
                ("WebView profile", () => _profileLease?.Dispose(cleanupCancellation.Token)),
                ("WebView lifetime", _lifetime.Dispose)));
            _resourcesClosed = errors.Count == 0;
            if (errors.Count > 0)
            {
                StorageStateJson = null;
                CleanupFailure = new AggregateException("RPOL WebView cleanup did not complete.", errors);
                StartupLoggingUtility.Append("RPOL WebView cleanup", CleanupFailure);
            }
        }

        private async Task<bool> TryAutoSubmitLoginAsync()
        {
            if (_webView.CoreWebView2 is null)
            {
                return false;
            }

            if (_webView.CoreWebView2.Source is not { } currentUriText
                            || !Uri.TryCreate(currentUriText, UriKind.Absolute, out var currentUri)
                            || !RpolCredentialSubmissionPolicy.TryValidateCredentialPage(currentUri, out _))
            {
                return false;
            }

            var userNameJson = JsonSerializer.Serialize(_request.UserName);
            var passwordJson = JsonSerializer.Serialize(_request.Password);
            var script = $$"""
                (() => {
                    const submit = {{RpolCredentialSubmissionScript.Source}};
                    return submit(
                        document.querySelector('form'),
                        { userName: {{userNameJson}}, password: {{passwordJson}} });
                })();
                """;

            using var credentialGuard = new RpolCredentialSubmissionGuard();
            _credentialSubmissionGuard = credentialGuard;
            _credentialSubmissionArmed = true;
            try
            {
                var resultJson = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                if (!string.Equals(resultJson, "true", StringComparison.OrdinalIgnoreCase))
                {
                    credentialGuard.Complete(false);
                    return false;
                }

                var requestValidated = await credentialGuard.WaitForRequestAsync(
                    TimeSpan.FromSeconds(30),
                    _lifetime.Token);
                if (!requestValidated)
                {
                    return false;
                }

                _statusLabel.Text = "RPOL login form was filled and submitted. Complete any verification prompt while protected access is checked.";
                return true;
            }
            finally
            {
                _credentialSubmissionArmed = false;
                if (ReferenceEquals(_credentialSubmissionGuard, credentialGuard))
                {
                    _credentialSubmissionGuard = null;
                }
            }
        }

        private async Task<RpolProtectedResourceClassification?> TryVerifyProtectedResourceAsync()
        {
            var probe = _probeWebView.CoreWebView2;
            if (probe is null || _protectedProbeInProgress || !_lifetime.IsAlive || IsDisposed)
            {
                return null;
            }

            _protectedProbeInProgress = true;
            _saveButton.Enabled = false;
            string? observedReferer = null;
            Uri? responseUri = null;
            int? responseStatus = null;
            string? responseContentType = null;
            var navigationCompletion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnRequest(object? _, CoreWebView2WebResourceRequestedEventArgs args)
            {
                if (args.ResourceContext == CoreWebView2WebResourceContext.Document
                    && Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var requestUri)
                    && RpolProtectedResourceUtility.IsExactProtectedUri(requestUri))
                {
                    observedReferer = args.Request.Headers.GetHeader("Referer");
                }
            }
            void OnResponse(object? _, CoreWebView2WebResourceResponseReceivedEventArgs args)
            {
                if (Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var requestUri)
                    && RpolProtectedResourceUtility.IsExactProtectedUri(requestUri))
                {
                    responseUri = requestUri;
                    responseStatus = args.Response.StatusCode;
                    responseContentType = args.Response.Headers.GetHeader("Content-Type");
                }
            }
            void OnNavigationCompleted(object? _, CoreWebView2NavigationCompletedEventArgs args)
                => navigationCompletion.TrySetResult(args);

            try
            {
                probe.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Document);
                probe.WebResourceRequested += OnRequest;
                probe.WebResourceResponseReceived += OnResponse;
                probe.NavigationCompleted += OnNavigationCompleted;
                var request = probe.Environment.CreateWebResourceRequest(
                    RpolAuthUtility.ProtectedDiceRollerUri.AbsoluteUri,
                    "GET",
                    null,
                    $"Referer: {_request.GameForumUrl}\r\n");
                probe.NavigateWithWebResourceRequest(request);
                await navigationCompletion.Task.WaitAsync(TimeSpan.FromSeconds(30), _lifetime.Token);
                var stableNavigation = await RpolNavigationStability.WaitForStableAsync(
                    async token =>
                    {
                        var dom = JsonSerializer.Deserialize<RpolNavigationDomSnapshot>(
                            await probe.ExecuteScriptAsync("""
                                (() => {
                                    const root = document.documentElement;
                                    if (!root) return { identity: 'missing', html: '' };
                                    if (!root.dataset.playerAssistantRpolIdentity) {
                                        root.dataset.playerAssistantRpolIdentity =
                                            (globalThis.crypto && crypto.randomUUID) ? crypto.randomUUID() : String(Date.now());
                                    }
                                    return { identity: root.dataset.playerAssistantRpolIdentity, html: root.outerHTML };
                                })()
                                """)) ?? new RpolNavigationDomSnapshot("missing", string.Empty);
                        return new RpolNavigationSnapshot(
                            Uri.TryCreate(probe.Source, UriKind.Absolute, out var currentUri) ? currentUri : null,
                            dom.Identity,
                            dom.Html);
                    },
                    quietPeriod: TimeSpan.FromSeconds(1),
                    maximumWait: TimeSpan.FromSeconds(20),
                    pollInterval: TimeSpan.FromMilliseconds(100),
                    cancellationToken: _lifetime.Token);
                var settledUri = stableNavigation.Url;
                var settledHtml = stableNavigation.Html;
                var classification = RpolProtectedResourceUtility.ClassifyEvidence(
                    new RpolProtectedProbeEvidence(
                        RpolAuthUtility.ProtectedDiceRollerUri,
                        responseUri,
                        settledUri,
                        responseStatus,
                        responseContentType,
                        settledHtml,
                        observedReferer,
                        SettledAfterStabilization: true));
                if (classification.Kind != RpolProtectedResourceKind.AuthenticatedProtectedContent)
                {
                    _statusLabel.Text = $"Protected RPOL access was not proven ({classification.Kind}). Finish RPOL verification, then try again.";
                    return classification;
                }

                _statusLabel.Text = "Protected RPOL Dice Roller access was verified in a separate same-profile probe. State can now be saved.";
                return classification;
            }
            catch (OperationCanceledException) when (_lifetime.Token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Protected RPOL access could not be verified: {ex.Message}";
                return null;
            }
            finally
            {
                probe.NavigationCompleted -= OnNavigationCompleted;
                probe.WebResourceRequested -= OnRequest;
                probe.WebResourceResponseReceived -= OnResponse;
                _protectedProbeInProgress = false;
            }
        }

        private async Task SaveBrowserStateAsync()
        {
            if (_webView.CoreWebView2 is null)
            {
                _statusLabel.Text = "WebView2 is not ready yet.";
                return;
            }

            _saveButton.Enabled = false;
            try
            {
                await TryAutoSubmitLoginAsync();
                await Task.Delay(TimeSpan.FromSeconds(2), _lifetime.Token);

                if (_webView.CoreWebView2.Source is not { } currentUriText
                                    || !Uri.TryCreate(currentUriText, UriKind.Absolute, out var currentUri)
                                    || !NetworkUrlAllowlistUtility.IsTrustedRpolNavigationUri(currentUri))
                {
                    _statusLabel.Text = "RPOL state was not saved because the page is not an approved HTTPS RPOL page.";
                    return;
                }

                var protectedClassification = await TryVerifyProtectedResourceAsync();
                if (protectedClassification?.Kind != RpolProtectedResourceKind.AuthenticatedProtectedContent)
                {
                    return;
                }

                var cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync("https://rpol.net/");
                var rpolCookies = cookies
                    .Where(cookie => IsRpolCookieDomain(cookie.Domain))
                    .Select(ToPlaywrightCookie)
                    .ToArray();

                if (rpolCookies.Length == 0)
                {
                    _statusLabel.Text = "No RPOL cookies were available yet. Finish RPOL verification in the page, then try again.";
                    return;
                }

                StorageStateJson = JsonSerializer.Serialize(
                    new PlaywrightStorageState(rpolCookies, []),
                    new JsonSerializerOptions
                    {
                        WriteIndented = false,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    });
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException) when (_lifetime.Token.IsCancellationRequested)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Could not save RPOL browser state: {ex.Message}";
            }
            finally
            {
                if (!IsDisposed)
                {
                    _saveButton.Enabled = true;
                }
            }
        }

        private static bool IsRpolCookieDomain(string domain)
        {
            return string.Equals(domain.TrimStart('.'), "rpol.net", StringComparison.OrdinalIgnoreCase)
                || domain.EndsWith(".rpol.net", StringComparison.OrdinalIgnoreCase);
        }

        private static PlaywrightCookie ToPlaywrightCookie(CoreWebView2Cookie cookie)
        {
            return new PlaywrightCookie(
                cookie.Name,
                cookie.Value,
                NormalizeCookieDomain(cookie.Domain),
                string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
                GetPlaywrightCookieExpires(cookie),
                cookie.IsHttpOnly,
                cookie.IsSecure,
                NormalizeSameSite(cookie.SameSite));
        }

        private static string NormalizeCookieDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                return "rpol.net";
            }

            return domain.StartsWith(".", StringComparison.Ordinal)
                ? domain
                : $".{domain}";
        }

        private static double GetPlaywrightCookieExpires(CoreWebView2Cookie cookie)
        {
            if (cookie.Expires <= DateTime.UnixEpoch)
            {
                return -1;
            }

            return new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds();
        }

        private static string NormalizeSameSite(CoreWebView2CookieSameSiteKind sameSite)
        {
            return sameSite switch
            {
                CoreWebView2CookieSameSiteKind.Lax => "Lax",
                CoreWebView2CookieSameSiteKind.Strict => "Strict",
                CoreWebView2CookieSameSiteKind.None => "None",
                _ => "Lax"
            };
        }

        private string BuildStatusText()
        {
            var uri = _webView.CoreWebView2?.Source;
            return string.IsNullOrWhiteSpace(uri)
                ? "Complete RPOL verification, then click Save RPOL State."
                : $"Loaded {uri}. Complete RPOL verification, then click Save RPOL State.";
        }

        private sealed record PlaywrightStorageState(
            [property: JsonPropertyName("cookies")] PlaywrightCookie[] Cookies,
            [property: JsonPropertyName("origins")] object[] Origins);

        private sealed record PlaywrightCookie(
            [property: JsonPropertyName("name")] string Name,
            [property: JsonPropertyName("value")] string Value,
            [property: JsonPropertyName("domain")] string Domain,
            [property: JsonPropertyName("path")] string Path,
            [property: JsonPropertyName("expires")] double Expires,
            [property: JsonPropertyName("httpOnly")] bool HttpOnly,
            [property: JsonPropertyName("secure")] bool Secure,
            [property: JsonPropertyName("sameSite")] string SameSite);
    }
}
