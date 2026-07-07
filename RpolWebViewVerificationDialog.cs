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
        private readonly WebView2 _webView;
        private readonly Label _statusLabel;
        private readonly Button _saveButton;
        private readonly Button _cancelButton;

        public RpolWebViewVerificationDialog(
            RpolWebViewVerificationRequest request,
            CancellationToken cancellationToken)
        {
            _request = request;
            _cancellationToken = cancellationToken;

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
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            _saveButton.Click += async (_, _) => await SaveBrowserStateAsync();
            _cancelButton.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Shown += async (_, _) => await InitializeWebViewAsync();
            FormClosed += (_, _) => _webView.Dispose();
        }

        public string? StorageStateJson { get; private set; }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                _cancellationToken.Register(() =>
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

                var userDataFolder = RuntimePathUtility.GetUserDataPath(WebViewUserDataDirectoryName);
                Directory.CreateDirectory(userDataFolder);
                var environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolder);
                await _webView.EnsureCoreWebView2Async(environment);

                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.NavigationCompleted += async (_, _) =>
                {
                    await TryAutoSubmitLoginAsync();
                    _saveButton.Enabled = true;
                    _statusLabel.Text = BuildStatusText();
                };
                _webView.CoreWebView2.Navigate(_request.GameForumUrl);
                _statusLabel.Text = "Loading RPOL. Complete any verification prompt in this window.";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Unable to start WebView2 verification: {ex.Message}";
                _saveButton.Enabled = false;
            }
        }

        private async Task TryAutoSubmitLoginAsync()
        {
            if (_webView.CoreWebView2 is null)
            {
                return;
            }

            var userNameJson = JsonSerializer.Serialize(_request.UserName);
            var passwordJson = JsonSerializer.Serialize(_request.Password);
            var script = $$"""
                (() => {
                    const userName = {{userNameJson}};
                    const password = {{passwordJson}};
                    const userInput = document.querySelector("input[name='username']");
                    const passwordInput = document.querySelector("input[name='password']");
                    if (!userInput || !passwordInput) {
                        return false;
                    }

                    userInput.value = userName;
                    passwordInput.value = password;
                    userInput.dispatchEvent(new Event('input', { bubbles: true }));
                    passwordInput.dispatchEvent(new Event('input', { bubbles: true }));

                    const rememberInput = document.querySelector("input[name='perm']");
                    if (rememberInput && !rememberInput.checked) {
                        rememberInput.click();
                    }

                    const submitButton = document.querySelector("input[name='specialaction'][value='Login']");
                    if (submitButton) {
                        submitButton.click();
                    }

                    return true;
                })();
                """;

            var resultJson = await _webView.CoreWebView2.ExecuteScriptAsync(script);
            if (string.Equals(resultJson, "true", StringComparison.OrdinalIgnoreCase))
            {
                _statusLabel.Text = "RPOL login form was filled and submitted. Complete any verification prompt, then click Save RPOL State.";
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
                await Task.Delay(TimeSpan.FromSeconds(2), _cancellationToken);

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
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
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
