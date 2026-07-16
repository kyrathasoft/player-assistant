using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace PlayerAssistant
{
    public partial class Form1 : Form
    {
        private enum LoginInfoDisplayMode
        {
            LoginInfo,
            PostTotals
        }

        private enum LocalIndexSearchOutcome
        {
            FoundMatches,
            NotFound,
            IndexUnavailable
        }

        private enum AdventureOutlineLineStyle
        {
            Body,
            Title,
            Chapter,
            Bullet
        }

        private static readonly string[] MyHeroBriefingLikelyResponseKeyLines =
        [
            "*First, the app finds the hero's latest authored post in each thread.*",
            "*Then it looks at later posts in that same thread by other authors.*",
            "*Those later posts are ranked as:*",
            "*- Direct mention after your last post when the post mentions the hero by name or first name.*",
            "*- Question-like post after your last post when the post contains a ?.*",
            "*- Recent post after your last post when it is simply a later post in that thread.*"
        ];

        private static string PlayerCharactersListingUrl => $"{AppSettingsUtility.ObsidianGameVaultUrl}/PCs/Player+Characters+Listing";
        private static string SitemapUrl => $"{AppSettingsUtility.ObsidianGameVaultUrl}/sitemap.xml";
        private const string PlayerCharactersDirectoryName = "PCs";
        private const string PostsDirectoryName = "Posts";
        private const string InCharacterPostsDirectoryName = "IC";
        private const string OutOfCharacterPostsDirectoryName = "OOC";
        private const string AsidePostsDirectoryName = "Aside";
        private const string ImagesDirectoryName = "Images";
        private const string MapsDirectoryName = "Maps";
        private const string ActivePlayerCharactersDirectoryName = "active";
        private const string InactivePlayerCharactersDirectoryName = "inactive";
        private const string ActiveHeroImageDownloadMarkerFileName = ".active-hero-images-downloaded";
        private static readonly TimeSpan ActiveHeroImageDownloadInterval = TimeSpan.FromHours(3);
        private const string ImageUriMessageBoxShownFileName = ".player-character-image-uris-shown";
        private const string HtmlImageUriMessageBoxShownFileName = ".player-character-html-image-uris-shown";
        private const string IndexImagePathMessageBoxShownFileName = ".player-character-index-image-paths-shown";
        private const string SitemapFileName = "sitemap.xml";
        private const string SitemapKeywordUrlsFileName = "sitemap-keyword-urls.json";
        private const string TempDirectoryName = "temp";
        private const string GameForumChapterPrefixesFileName = "game-forum-chapter-prefixes.txt";
        private const string GameForumChapterDownloadsFileName = "game-forum-chapter-downloads.txt";
        private const string GameForumAsideDownloadsFileName = "game-forum-aside-downloads.txt";
        private const string GameForumOutOfCharacterDownloadsFileName = "game-forum-ooc-downloads.txt";
        private const string TheCastLoginInfoFileName = "login-info.json";
        private const string DiceRollsHtmlFileName = "dice-rolls.html";
        private const string RegionalMapFileName = "northernreaches.png";
        private const string KeywordIndexFileName = "keyword-index.json";
        private const string DungeonMasterXpAccessName = "Dungeon Master";
        private static readonly TimeSpan HeroImageShowcaseStartDelay = TimeSpan.FromMilliseconds(2500);
        private static readonly TimeSpan HeroImageIntroDuration = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan HeroImageFadeInDuration = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan HeroImageDisplayDuration = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan HeroImageFadeOutDuration = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan HeroImageInterImageDelayDuration = TimeSpan.FromMilliseconds(600);
        private static readonly string[] HeroImageExtensions =
        [
            ".avif",
            ".bmp",
            ".gif",
            ".ico",
            ".jpeg",
            ".jpg",
            ".png",
            ".svg",
            ".tif",
            ".tiff",
            ".webp"
        ];

        private string[] _playerCharacterImageUris = [];
        private string[] _playerCharacterHtmlImageUris = [];
        private string[] _playerCharacterImageFileNames = [];
        private string[] _playerCharacterResolvedImagePaths = [];
        private string[] _activePlayerCharacterImagePaths = [];
        private string _playerCharacterListingMarkdown = string.Empty;
        private string _playerCharacterListingHtml = string.Empty;
        private TheCastLoginInfo[] _loginInfoRows = [];
        private PostTotalsSummary? _postTotalsSummary;
        private IReadOnlyList<PcXpTotal> _xpTotals = [];
        private string _xpDateLabel = string.Empty;
        private IReadOnlyList<PartyHeroSheet> _partyHeroes = [];
        private string _adventureOutlineMarkdown = string.Empty;
        private HashSet<string>? _encryptedTextIndexUrls;
        private bool _showLoginInfo;
        private bool _showPostTotals;
        private bool _showXpTotal;
        private bool _showParty;
        private bool _showMyHeroBriefing;
        private bool _showWelcomeText = true;
        private bool _showHeroIntroText;
        private bool _showAttributionText;
        private System.Windows.Forms.Timer? _attributionTimer;
        private System.Windows.Forms.Timer? _welcomeTimer;
        private System.Windows.Forms.Timer? _heroImageShowcaseTimer;
        private System.Windows.Forms.Timer? _keywordIndexStatusTimer;
        private System.Windows.Forms.Timer? _statusActivityTimer;
        private readonly Random _random = new();
        private readonly List<string> _heroImageShowcasePaths = [];
        private Image? _currentHeroImage;
        private Rectangle _currentHeroImageBounds;
        private readonly Stopwatch _currentHeroImageStopwatch = new();
        private float _currentHeroImageOpacity;
        private bool _heroImageShowcaseStarted;
        private int _heroImageShowcaseTotal;
        private int _heroImageShowcaseIndex;
        private bool _currentHeroImageWasVisible;
        private bool _playerCharacterListingUpdateStarted;
        private bool _heroImageShowcaseCompleted;
        private bool _heroImageIntroStarted;
        private bool _regionalMapActive;
        private bool _regionalMapTransitionPending;
        private int _heroImageShowcaseSkipped;
        private string _lastHeroImageSkipReason = string.Empty;
        private PictureBox? _heroImagePictureBox;
        private Panel? _regionalMapPanel;
        private ListBox? _diceRollsListBox;
        private RichTextBox? _adventureOutlineTextBox;
        private RichTextBox? _myHeroBriefingTextBox;
        private Panel? _partyPanel;
        private Image? _regionalMapImage;
        private Image? _regionalMapImageCache;
        private string? _regionalMapImageCachePath;
        private DateTime _regionalMapImageCacheLastWriteUtc;
        private Task? _regionalMapImagePreloadTask;
        private bool _loginInfoRefreshStarted;
        private LoginInfoDisplayMode _loginInfoRefreshTarget = LoginInfoDisplayMode.LoginInfo;
        private readonly BackgroundTaskSupervisor _backgroundTasks = new();
        private readonly bool _suppressHeroImagesForThisRun;
        private bool _searchResultsRequested;
        private readonly string _baseTitleText;
        internal static string? MyHeroBriefingPostsDirectoryOverride { get; set; }
        private DateTimeOffset _keywordIndexStatusLastChangedUtc;
        private DateTimeOffset _keywordIndexStatusLockedUntilUtc;
        private string _keywordIndexStatusMessage = string.Empty;
        private string _keywordIndexPinnedStatusMessage = string.Empty;
        private static readonly TimeSpan MinimumStatusBarMessageDuration = TimeSpan.FromMilliseconds(1000);
        private DateTimeOffset _statusBarMessageLockedUntilUtc;
        private string? _pendingStatusBarMessage;
        private TimeSpan _pendingStatusBarDuration;
        private string? _pendingKeywordIndexPinnedStatusMessage;
        private TimeSpan _pendingKeywordIndexPinnedStatusDuration;
        private KeywordIndexProgress? _latestKeywordIndexProgress;
        private KeywordIndexProgress? _pendingKeywordIndexProgress;
        private bool _keywordIndexingInProgress;
        private int _activeAsyncOperationCount;
        private int _statusActivityFrameIndex;
        private CancellationTokenSource? _searchOperationCancellation;
        private static readonly string[] StatusActivityFrames = ["-", "\\", "|", "/"];
        private Func<string[], DialogResult> _showLocalIndexMissPrompt = _ => DialogResult.No;
        private Action<string[], int> _showOnlineSearchCompletedMessage = static (_, _) => { };
        private Action<string, string> _showWarningDialog = static (_, _) => { };
        private Func<string[], CancellationToken, Task<string[]>> _onlineSearchProvider = static (_, _) => Task.FromResult(Array.Empty<string>());
        private Func<string, string, CancellationToken, Task<bool>> _rpolHeroNameBodyMatchProvider = static (_, _, _) => Task.FromResult(false);

        public Form1(bool suppressHeroImagesForThisRun = false)
        {
            _suppressHeroImagesForThisRun = suppressHeroImagesForThisRun;
            _showLocalIndexMissPrompt = ShowLocalIndexMissPrompt;
            _showOnlineSearchCompletedMessage = ShowOnlineSearchCompletedMessage;
            _showWarningDialog = ShowWarningDialog;
            _onlineSearchProvider = SearchOnlineForTermsAsync;
            _rpolHeroNameBodyMatchProvider = DoesRpolPostBodyContainSearchTermAsync;
            RpolAuthUtility.WebViewVerificationHandler = ShowRpolWebViewVerificationAsync;
            InitializeComponent();
            statusActivityToolStripStatusLabel.Available = false;
            statusActivityToolStripStatusLabel.Text = string.Empty;
            _baseTitleText = Text;
            InitializeRegionalMapPanel();
            InitializeHeroImagePictureBox();
            DoubleBuffered = true;
            Icon = LoadApplicationIcon();
            skipHeroImageParadeAtStartupToolStripMenuItem.Checked = UserPreferencesUtility.SkipHeroImageParadeAtStartup;
            whiteMarbleBackgroundTilingToolStripMenuItem.Checked = UserPreferencesUtility.WhiteMarbleBackgroundTilingEnabled;
            UpdateRegionalMapMenuItem();
            ApplyWhiteMarbleBackgroundTiling();

            _attributionTimer = new System.Windows.Forms.Timer
            {
                Interval = 2000
            };
            _attributionTimer.Tick += (_, _) =>
            {
                _showAttributionText = true;
                _attributionTimer.Stop();
                _attributionTimer.Dispose();
                _attributionTimer = null;
                Invalidate();
            };
            _attributionTimer.Start();

            _welcomeTimer = new System.Windows.Forms.Timer
            {
                Interval = 5000
            };
            _welcomeTimer.Tick += (_, _) =>
            {
                _showWelcomeText = false;
                _showAttributionText = false;
                whiteMarbleBackgroundTilingToolStripMenuItem.Checked = false;
                SetBackgroundImage(LoadDragonBackgroundImage(), ImageLayout.Center);
                _welcomeTimer.Stop();
                _welcomeTimer.Dispose();
                _welcomeTimer = null;
                if (!_suppressHeroImagesForThisRun)
                {
                    StartBackgroundTask("hero image showcase startup", StartHeroImageShowcaseAfterDelayAsync);
                }
                StartBackgroundTask(
                    "player character refresh",
                    cancellationToken => StartPlayerCharacterListingUpdateAsync(
                        showFailureDialog: false,
                        cancellationToken));
                Invalidate();
            };
            _welcomeTimer.Start();

            _keywordIndexStatusTimer = new System.Windows.Forms.Timer
            {
                Interval = 250
            };
            _keywordIndexStatusTimer.Tick += (_, _) => UpdateKeywordIndexStatusTimer();
            _keywordIndexStatusTimer.Start();

            _statusActivityTimer = new System.Windows.Forms.Timer
            {
                Interval = 150
            };
            _statusActivityTimer.Tick += (_, _) => AdvanceStatusActivityIndicator();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopHeroImageShowcase();
            _attributionTimer?.Dispose();
            _welcomeTimer?.Dispose();
            _keywordIndexStatusTimer?.Dispose();
            _statusActivityTimer?.Dispose();
            _searchOperationCancellation?.Cancel();
            _backgroundTasks.Dispose();
            if (RpolAuthUtility.WebViewVerificationHandler == ShowRpolWebViewVerificationAsync)
            {
                RpolAuthUtility.WebViewVerificationHandler = null;
            }

            _regionalMapPanel?.Dispose();
            _heroImagePictureBox?.Image?.Dispose();
            _heroImagePictureBox?.Dispose();
            _diceRollsListBox?.Dispose();
            _adventureOutlineTextBox?.Dispose();
            _regionalMapImage?.Dispose();
            _regionalMapImageCache?.Dispose();
            BackgroundImage?.Dispose();

            base.OnFormClosed(e);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FillCurrentScreenWorkingArea();
            UpdateRegionalMapPanelBounds();
            UpdateSearchPanelBounds();
            ShowStartupConfigurationWarning();
            InitializeCachedActiveHeroImages();
            StartBackgroundTask("regional map preload", PreloadRegionalMapImageAsync);
            await Task.Yield();
            StartBackgroundTask(
                "game forum startup",
                async cancellationToken =>
                {
                    if (await LoadGameForumChapterPrefixesAsync(cancellationToken))
                    {
                        StartKeywordIndexCrawler();
                    }

                    StartBackgroundTask(
                        "player character refresh",
                        playerCharacterCancellationToken => StartPlayerCharacterListingUpdateAsync(
                            showFailureDialog: false,
                            playerCharacterCancellationToken));
                });
        }

        private void ShowStartupConfigurationWarning()
        {
            var message = AppConfigurationValidationUtility.LatestReport.FirstUserMessage;
            if (!string.IsNullOrWhiteSpace(message))
            {
                SetStatusBarMessage(message, TimeSpan.FromSeconds(8));
            }
        }

        private Task<string?> ShowRpolWebViewVerificationAsync(
            RpolWebViewVerificationRequest request,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void ShowDialogOnUiThread()
            {
                try
                {
                    if (IsDisposed)
                    {
                        completion.TrySetCanceled(cancellationToken);
                        return;
                    }

                    using var dialog = new RpolWebViewVerificationDialog(request, cancellationToken);
                    var result = dialog.ShowDialog(this);
                    completion.TrySetResult(result == DialogResult.OK
                        ? dialog.StorageStateJson
                        : null);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }

            if (InvokeRequired)
            {
                BeginInvoke(ShowDialogOnUiThread);
            }
            else
            {
                ShowDialogOnUiThread();
            }

            return completion.Task;
        }

        private void StartKeywordIndexCrawler()
        {
            if (_keywordIndexingInProgress)
            {
                return;
            }

            _keywordIndexingInProgress = true;
            var started = StartBackgroundTask("keyword crawler", async cancellationToken =>
            {
                var progress = new Progress<KeywordIndexProgress>(UpdateKeywordIndexStatus);
                await Task.Run(
                    () => KeywordIndexCrawler.BuildIndexAsync(progress, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            });

            if (!started)
            {
                _keywordIndexingInProgress = false;
            }
        }

        private bool StartBackgroundTask(string phase, Func<CancellationToken, Task> action)
        {
            var activity = BeginStatusBarActivity();
            var started = _backgroundTasks.TryStart(phase, async cancellationToken =>
            {
                try
                {
                    await action(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (string.Equals(phase, "keyword crawler", StringComparison.OrdinalIgnoreCase)
                        && !IsDisposed
                        && IsHandleCreated)
                    {
                        BeginInvoke(() =>
                        {
                            _keywordIndexingInProgress = false;
                        });
                    }

                    activity.Dispose();
                }
            });
            if (!started)
            {
                activity.Dispose();
            }

            return started;
        }

        private IDisposable BeginStatusBarActivity()
        {
            ChangeStatusBarActivityCount(1);
            return new StatusActivityScope(this);
        }

        private void EndStatusBarActivity()
        {
            ChangeStatusBarActivityCount(-1);
        }

        private void ChangeStatusBarActivityCount(int delta)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(() => ChangeStatusBarActivityCount(delta));
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            _activeAsyncOperationCount = Math.Max(0, _activeAsyncOperationCount + delta);
            UpdateStatusActivityIndicatorState();
        }

        private void UpdateStatusActivityIndicatorState()
        {
            if (_activeAsyncOperationCount > 0)
            {
                if (!statusActivityToolStripStatusLabel.Available)
                {
                    _statusActivityFrameIndex = 0;
                    statusActivityToolStripStatusLabel.Text = StatusActivityFrames[_statusActivityFrameIndex];
                    statusActivityToolStripStatusLabel.Available = true;
                }

                _statusActivityTimer?.Start();
                return;
            }

            _statusActivityTimer?.Stop();
            statusActivityToolStripStatusLabel.Available = false;
            statusActivityToolStripStatusLabel.Text = string.Empty;
        }

        private void AdvanceStatusActivityIndicator()
        {
            if (_activeAsyncOperationCount <= 0)
            {
                UpdateStatusActivityIndicatorState();
                return;
            }

            _statusActivityFrameIndex = (_statusActivityFrameIndex + 1) % StatusActivityFrames.Length;
            statusActivityToolStripStatusLabel.Text = StatusActivityFrames[_statusActivityFrameIndex];
        }

        private sealed class StatusActivityScope : IDisposable
        {
            private Form1? _owner;

            public StatusActivityScope(Form1 owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _owner, null)?.EndStatusBarActivity();
            }
        }

        private void UpdateKeywordIndexStatus(KeywordIndexProgress progress)
        {
            UpdateKeywordIndexTitle(progress);

            if (progress.IsCompleted)
            {
                _keywordIndexingInProgress = false;
                _latestKeywordIndexProgress = null;
                PinKeywordIndexStatusMessage("indexing of keywords has completed", TimeSpan.FromSeconds(3));
                return;
            }

            if (progress.IsTermsLoaded)
            {
                SetKeywordIndexStatusMessage(
                    $"Keyword index: loaded {progress.TotalKeywordCount} unique term{(progress.TotalKeywordCount == 1 ? string.Empty : "s")} to be indexed.");
                return;
            }

            if (progress.IsIndexFileCreated)
            {
                PinKeywordIndexStatusMessage("Keyword index: created keyword-index.json.", TimeSpan.FromSeconds(3));
                return;
            }

            if (progress.Keyword is null && progress.TotalUrlCount > 0)
            {
                SetKeywordIndexStatusMessage(
                    $"Keyword index: scanned {progress.ProcessedUrlCount} of {progress.TotalUrlCount} URLs ({progress.TotalObsidianUrlCount} Obsidian + {progress.TotalRpolUrlCount} RPOL); examining {progress.CurrentUrl ?? "(unknown URL)"}.");
                return;
            }

            if (progress.Keyword is null)
            {
                return;
            }

            _latestKeywordIndexProgress = progress;
            if (DateTimeOffset.UtcNow < _keywordIndexStatusLockedUntilUtc)
            {
                _pendingKeywordIndexProgress = progress;
                return;
            }

            ApplyKeywordIndexProgress(progress);
        }

        private void ApplyKeywordIndexProgress(KeywordIndexProgress progress)
        {
            var currentTermSuffix = FormatKeywordIndexCurrentTermSuffix(progress);
            SetKeywordIndexStatusMessage(progress.IsNewKeyword
                ? $"Keyword index: adding '{progress.Keyword}' ({FormatKeywordIndexCount(progress.UrlCount, "URL")} found; total count {progress.TotalOccurrences}); examining {progress.CurrentUrl ?? "(unknown URL)"}.{currentTermSuffix}"
                : $"Keyword index: updating '{progress.Keyword}' ({FormatKeywordIndexCount(progress.UrlCount, "URL")} found; total count {progress.TotalOccurrences}); examining {progress.CurrentUrl ?? "(unknown URL)"}.{currentTermSuffix}");
        }

        private static string FormatKeywordIndexCount(int value, string noun)
        {
            return value == 1
                ? $"1 {noun}"
                : $"{value} {noun}s";
        }

        private static string FormatKeywordIndexCurrentTermSuffix(KeywordIndexProgress progress)
        {
            if (progress.CurrentKeywordNumber <= 0 || progress.TotalKeywordCount <= 0)
            {
                return string.Empty;
            }

            return $" ({progress.CurrentKeywordNumber} of {progress.TotalKeywordCount} terms is now being indexed)";
        }

        private void UpdateKeywordIndexTitle(KeywordIndexProgress progress)
        {
            if (progress.TotalKeywordCount <= 0 || progress.IsTermsLoaded || progress.IsIndexFileCreated)
            {
                Text = _baseTitleText;
                return;
            }

            var rawPercentage = progress.TotalUrlCount > 0
                ? progress.ProcessedUrlCount * 100d / progress.TotalUrlCount
                : progress.CompletedKeywordCount * 100d / Math.Max(progress.TotalKeywordCount, 1);
            var percentage = (int)Math.Floor(rawPercentage);
            if (rawPercentage > 0d && percentage == 0)
            {
                percentage = 1;
            }

            if (!progress.IsCompleted && percentage >= 100)
            {
                percentage = 99;
            }

            percentage = Math.Clamp(percentage, 0, 100);
            Text = $"{_baseTitleText} - {percentage}% of keyword indexing complete";
        }

        private void UpdateKeywordIndexStatusTimer()
        {
            var now = DateTimeOffset.UtcNow;
            if (_pendingStatusBarMessage is not null && now >= _statusBarMessageLockedUntilUtc)
            {
                var pendingMessage = _pendingStatusBarMessage;
                var pendingDuration = _pendingStatusBarDuration;
                _pendingStatusBarMessage = null;
                _pendingStatusBarDuration = TimeSpan.Zero;
                ApplyStatusBarMessageNow(pendingMessage, pendingDuration);
                return;
            }

            if (now < _keywordIndexStatusLockedUntilUtc)
            {
                if (_keywordIndexPinnedStatusMessage.Length > 0
                    && !string.Equals(statusToolStripStatusLabel.Text, _keywordIndexPinnedStatusMessage, StringComparison.Ordinal))
                {
                    statusToolStripStatusLabel.Text = _keywordIndexPinnedStatusMessage;
                }

                return;
            }

            _keywordIndexPinnedStatusMessage = string.Empty;
            if (now >= _keywordIndexStatusLockedUntilUtc
                && _pendingKeywordIndexProgress is { } pendingProgress)
            {
                _pendingKeywordIndexProgress = null;
                UpdateKeywordIndexStatus(pendingProgress);
                return;
            }

            if (_keywordIndexingInProgress
                && _latestKeywordIndexProgress is { } latestProgress
                && !(statusToolStripStatusLabel.Text ?? string.Empty).StartsWith("Keyword index:", StringComparison.Ordinal))
            {
                ApplyKeywordIndexProgress(latestProgress);
                return;
            }

            if (!(statusToolStripStatusLabel.Text ?? string.Empty).StartsWith("Keyword index: adding", StringComparison.Ordinal)
                || now - _keywordIndexStatusLastChangedUtc < TimeSpan.FromSeconds(5))
            {
                return;
            }

            var fileSizeMessage = BuildKeywordTermsFileSizeStatusMessage();
            PinKeywordIndexStatusMessage(fileSizeMessage, TimeSpan.FromSeconds(3));
        }

        private void SetKeywordIndexStatusMessage(string message)
        {
            if (SetStatusBarMessage(message))
            {
                _keywordIndexStatusLastChangedUtc = DateTimeOffset.UtcNow;
            }

            _keywordIndexStatusMessage = message;
        }

        private void PinKeywordIndexStatusMessage(string message, TimeSpan duration)
        {
            if (SetStatusBarMessage(message, duration))
            {
                _keywordIndexStatusLastChangedUtc = DateTimeOffset.UtcNow;
                _keywordIndexPinnedStatusMessage = message;
                _keywordIndexStatusLockedUntilUtc = DateTimeOffset.UtcNow.Add(duration);
                _pendingKeywordIndexPinnedStatusMessage = null;
                _pendingKeywordIndexPinnedStatusDuration = TimeSpan.Zero;
                return;
            }

            _pendingKeywordIndexPinnedStatusMessage = message;
            _pendingKeywordIndexPinnedStatusDuration = duration;
        }

        private bool SetStatusBarMessage(
            string message,
            TimeSpan? minimumDuration = null)
        {
            var effectiveDuration = minimumDuration.GetValueOrDefault(MinimumStatusBarMessageDuration);
            if (effectiveDuration < MinimumStatusBarMessageDuration)
            {
                effectiveDuration = MinimumStatusBarMessageDuration;
            }

            var now = DateTimeOffset.UtcNow;
            var currentMessage = statusToolStripStatusLabel.Text ?? string.Empty;
            if (now < _statusBarMessageLockedUntilUtc
                && !string.Equals(currentMessage, message, StringComparison.Ordinal))
            {
                _pendingStatusBarMessage = message;
                _pendingStatusBarDuration = effectiveDuration;
                return false;
            }

            _pendingStatusBarMessage = null;
            _pendingStatusBarDuration = TimeSpan.Zero;
            ApplyStatusBarMessageNow(message, effectiveDuration);
            return true;
        }

        private void ApplyStatusBarMessageNow(string message, TimeSpan duration)
        {
            statusToolStripStatusLabel.Text = message;
            var now = DateTimeOffset.UtcNow;
            _statusBarMessageLockedUntilUtc = now.Add(duration);

            if (string.Equals(_pendingKeywordIndexPinnedStatusMessage, message, StringComparison.Ordinal))
            {
                _keywordIndexPinnedStatusMessage = message;
                _keywordIndexStatusLockedUntilUtc = now.Add(_pendingKeywordIndexPinnedStatusDuration);
                _keywordIndexStatusLastChangedUtc = now;
                _pendingKeywordIndexPinnedStatusMessage = null;
                _pendingKeywordIndexPinnedStatusDuration = TimeSpan.Zero;
            }
        }

        private static string BuildKeywordTermsFileSizeStatusMessage()
        {
            var markdownPath = KeywordTermsFileUtility.TryResolvePath();
            if (!string.IsNullOrWhiteSpace(markdownPath)
                && File.Exists(markdownPath))
            {
                return $"Keyword index: game-posts-key-terms.md size {FormatFileSize(new FileInfo(markdownPath).Length)}.";
            }

            return "Keyword index: game-posts-key-terms.md unavailable.";
        }

        private static string FormatFileSize(long bytes)
        {
            const long kilobyte = 1024;
            const long megabyte = kilobyte * 1024;

            return bytes >= megabyte
                ? $"{bytes / (double)megabyte:0.##} MB"
                : bytes >= kilobyte
                    ? $"{bytes / (double)kilobyte:0.##} KB"
                    : $"{bytes} bytes";
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegionalMapPanelBounds();
            UpdateDiceRollsListBoxBounds();
            UpdateAdventureOutlineTextBoxBounds();
            UpdateMyHeroBriefingTextBoxBounds();
            UpdatePartyPanelBounds();
            UpdateSearchPanelBounds();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _searchOperationCancellation?.Cancel();

            if (_backgroundTasks.IsRunning("keyword crawler"))
            {
                var result = MessageBox.Show(
                    this,
                    "Keyword indexing is still in progress. Are you sure you want to close the app?",
                    "Player Assistant",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            base.OnFormClosing(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_regionalMapActive)
            {
                DrawRegionalMap(e.Graphics, GetHeroImageDisplayBounds());
                return;
            }

            if (_showLoginInfo)
            {
                DrawLoginInfo(e.Graphics);
                return;
            }

            if (_showPostTotals)
            {
                DrawPostTotals(e.Graphics);
                return;
            }

            if (_showXpTotal)
            {
                DrawXpTotal(e.Graphics);
                return;
            }

            if (_showWelcomeText || _showHeroIntroText)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var fontFamily = new FontFamily("Segoe UI");

                DrawOutlinedText(
                    e.Graphics,
                    _showHeroIntroText ? "Let's Meet Our Heroes..." : "Welcome to Player Assistant!",
                    fontFamily,
                    40,
                    new Rectangle(ClientRectangle.X, ClientRectangle.Y - 100, ClientRectangle.Width, ClientRectangle.Height),
                    Color.LightGray);

                if (_showWelcomeText && _showAttributionText)
                {
                    DrawOutlinedText(
                        e.Graphics,
                        "For players in the Scarlet Horizons campaign",
                        fontFamily,
                        30,
                        ClientRectangle,
                        Color.LightGray);
                }
            }
        }

        private void ExitToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            EnableLoginInfoMenuItem();
            EnableShowPostTotalsMenuItem();
            EnableXpMenuItem();
            EnablePartyMenuItem();
            EnableMyHeroBriefingMenuItem();
            EnableAdventureOutlineMenuItem();
            Close();
        }

        private void FileToolStripMenuItem_DropDownOpening(object? sender, EventArgs e)
        {
            UpdateShowMenuItemsForHeroImageShowcase();
        }

        private void NonSearchToolStripMenuItem_DropDownOpening(object? sender, EventArgs e)
        {
            whiteMarbleBackgroundTilingToolStripMenuItem.Checked = UserPreferencesUtility.WhiteMarbleBackgroundTilingEnabled;
        }

        private void SearchToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            EnableLoginInfoMenuItem();
            EnableShowPostTotalsMenuItem();
            EnableShowDiceRollsMenuItem();
            EnableXpMenuItem();
            EnablePartyMenuItem();
            EnableMyHeroBriefingMenuItem();
            EnableAdventureOutlineMenuItem();

            if (_regionalMapActive || _showLoginInfo || _showPostTotals || _showXpTotal || _showParty || _showMyHeroBriefing || _diceRollsListBox is not null || _adventureOutlineTextBox is not null || _myHeroBriefingTextBox is not null)
            {
                ClearDisplaySurfaceForRegionalMap();
                Refresh();
            }

            ShowSearchPanel();
        }

        private async void LoginInfoToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            ClearDiceRollsDisplayIfVisible();
            HideSearchPanel();
            EnableShowPostTotalsMenuItem();
            EnableShowDiceRollsMenuItem();
            EnableXpMenuItem();
            EnablePartyMenuItem();
            EnableMyHeroBriefingMenuItem();
            EnableAdventureOutlineMenuItem();
            loginInfoToolStripMenuItem.Enabled = false;
            _loginInfoRefreshTarget = LoginInfoDisplayMode.LoginInfo;

            try
            {
                var loginInfoPath = GetLoginInfoPath();
                if (File.Exists(loginInfoPath))
                {
                    ShowLoginInfoRows(LoadLoginInfoJson(loginInfoPath), "cached");
                    StartBackgroundTask("login info refresh", _ => RefreshLoginInfoInBackgroundAsync());
                    return;
                }

                SetStatusBarMessage("Loading login info...");
                StartBackgroundTask("login info refresh", _ => RefreshLoginInfoInBackgroundAsync());
            }
            catch (Exception ex)
            {
                loginInfoToolStripMenuItem.Enabled = true;
                await ReportOperationFailureAsync(
                    "login info display",
                    "Login info unavailable",
                    "Login Info Error",
                    ex,
                    showDialog: true);
            }
        }

        private async void ShowPostTotalsToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            ClearDiceRollsDisplayIfVisible();
            HideSearchPanel();
            EnableLoginInfoMenuItem();
            EnableShowDiceRollsMenuItem();
            EnableXpMenuItem();
            EnablePartyMenuItem();
            EnableMyHeroBriefingMenuItem();
            EnableAdventureOutlineMenuItem();
            showPostTotalsToolStripMenuItem.Enabled = false;
            _loginInfoRefreshTarget = LoginInfoDisplayMode.PostTotals;

            try
            {
                var localTheCastPath = GetTheCastHtmlPath();
                if (File.Exists(localTheCastPath))
                {
                    ShowPostTotalsRows(LoadTheCastLoginInfoFromHtml(localTheCastPath), "cached");
                    StartBackgroundTask("login info refresh", _ => RefreshLoginInfoInBackgroundAsync());
                    return;
                }

                var loginInfoPath = GetLoginInfoPath();
                if (File.Exists(loginInfoPath))
                {
                    ShowPostTotalsRows(LoadLoginInfoJson(loginInfoPath), "cached");
                    StartBackgroundTask("login info refresh", _ => RefreshLoginInfoInBackgroundAsync());
                    return;
                }

                SetStatusBarMessage("Loading post totals...");
                StartBackgroundTask("login info refresh", _ => RefreshLoginInfoInBackgroundAsync());
            }
            catch (Exception ex)
            {
                showPostTotalsToolStripMenuItem.Enabled = true;
                await ReportOperationFailureAsync(
                    "post totals display",
                    "Post totals unavailable",
                    "Post Totals Error",
                    ex,
                    showDialog: true);
            }
        }

        private async void ShowDiceRollsToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            ClearDiceRollsDisplayIfVisible();
            HideSearchPanel();
            EnableLoginInfoMenuItem();
            EnableShowPostTotalsMenuItem();
            EnableXpMenuItem();
            EnablePartyMenuItem();
            EnableMyHeroBriefingMenuItem();
            EnableAdventureOutlineMenuItem();

            var diceRollsPath = GetDiceRollsHtmlPath();
            if (!TryLoadDiceRollEntries(diceRollsPath, out var entries) || entries.Length == 0)
            {
                showDiceRollsToolStripMenuItem.Enabled = false;
                SetStatusBarMessage($"Dice rolls unavailable: {diceRollsPath}");
                return;
            }

            try
            {
                ClearDisplaySurfaceForRegionalMap();
                _postTotalsSummary = null;
                ShowDiceRollEntries(entries);
                showDiceRollsToolStripMenuItem.Enabled = false;
            }
            catch (Exception ex)
            {
                await ReportOperationFailureAsync(
                    "dice rolls display",
                    "Dice rolls unavailable",
                    "Dice Rolls Error",
                    ex,
                showDialog: true);
            }
        }

        private async void XpToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            ClearDiceRollsDisplayIfVisible();
            HideSearchPanel();
            EnableLoginInfoMenuItem();
            EnableShowPostTotalsMenuItem();
            EnableShowDiceRollsMenuItem();
            EnablePartyMenuItem();
            EnableMyHeroBriefingMenuItem();
            EnableAdventureOutlineMenuItem();

            if (!TryPromptForXpCredentials(out var characterName, out var password))
            {
                return;
            }

            if (!XpPasswordStoreUtility.ValidatePassword(characterName, password, AppContext.BaseDirectory))
            {
                MessageBox.Show(
                    this,
                    "The character name and XP password did not match.",
                    "XP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                SetStatusBarMessage("XP access denied.");
                return;
            }

            xpToolStripMenuItem.Enabled = false;
            try
            {
                SetStatusBarMessage("Loading XP total...");
                using var activity = BeginStatusBarActivity();
                var snapshot = await XpTrackingUtility.GetCurrentXpSnapshotAsync();
                if (IsDungeonMasterXpAccess(characterName))
                {
                    ShowXpTotals(snapshot.DateLabel, snapshot.Totals);
                    return;
                }

                var total = FindXpTotalForCharacter(snapshot.Totals, characterName);
                if (total is null)
                {
                    xpToolStripMenuItem.Enabled = true;
                    MessageBox.Show(
                        this,
                        XpTrackingUtility.FormatMissingPcFailureMessage(characterName),
                        "XP",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    SetStatusBarMessage($"XP total unavailable for {characterName}. Contact the DM.");
                    return;
                }

                ShowXpTotals(snapshot.DateLabel, [total]);
            }
            catch (Exception ex)
            {
                xpToolStripMenuItem.Enabled = true;
                await AppendStartupErrorLogAsync("XP display", ex);
                SetStatusBarMessage("XP total unavailable. Contact the DM.");
                ShowWarningDialog("XP Error", XpTrackingUtility.FormatUserFacingFailureMessage(ex));
            }
        }

        private async void PartyToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            ClearDiceRollsDisplayIfVisible();
            HideSearchPanel();
            EnableLoginInfoMenuItem();
            EnableShowPostTotalsMenuItem();
            EnableShowDiceRollsMenuItem();
            EnableXpMenuItem();
            EnableMyHeroBriefingMenuItem();
            EnableAdventureOutlineMenuItem();

            partyToolStripMenuItem.Enabled = false;
            try
            {
                var partyHeroes = PartyHeroUtility.LoadActiveParty(EnsurePlayerCharacterDirectories());
                if (TryPromptForXpCredentials(out var characterName, out var password))
                {
                    var passwordValidation = await ValidateOptionalXpPasswordAsync(
                        characterName,
                        password,
                        "party XP authentication");
                    if (passwordValidation == OptionalXpPasswordValidation.Valid)
                    {
                        try
                        {
                            SetStatusBarMessage("Loading party and XP totals...");
                            using var activity = BeginStatusBarActivity();
                            var snapshot = await XpTrackingUtility.GetCurrentXpSnapshotAsync();
                            partyHeroes = PartyHeroUtility.WithVisibleXpTotals(
                                partyHeroes,
                                snapshot.Totals,
                                characterName,
                                IsDungeonMasterXpAccess(characterName));
                        }
                        catch (Exception ex)
                        {
                            await AppendStartupErrorLogAsync("party XP display", ex);
                            SetStatusBarMessage("Party loaded without XP totals. Contact the DM if XP should be visible.");
                        }
                    }
                    else if (passwordValidation == OptionalXpPasswordValidation.Invalid)
                    {
                        MessageBox.Show(
                            this,
                            "The character name and XP password did not match. Party details will be shown without XP totals.",
                            "Party",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        SetStatusBarMessage("Party XP access denied. Showing party without XP totals.");
                    }
                    else
                    {
                        ShowMissingXpPasswordStoreWarning("Party");
                        SetStatusBarMessage("Party loaded without XP totals because the XP password file is missing.");
                    }
                }

                ShowPartyHeroes(partyHeroes);
            }
            catch (Exception ex)
            {
                partyToolStripMenuItem.Enabled = true;
                await ReportOperationFailureAsync(
                    "party display",
                    "Party unavailable",
                    "Party Error",
                    ex,
                showDialog: true);
            }
        }

        private async void MyHeroBriefingToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            ClearDiceRollsDisplayIfVisible();
            HideSearchPanel();
            EnableLoginInfoMenuItem();
            EnableShowPostTotalsMenuItem();
            EnableShowDiceRollsMenuItem();
            EnableXpMenuItem();
            EnablePartyMenuItem();
            EnableMyHeroBriefingMenuItem();
            EnableAdventureOutlineMenuItem();

            myHeroBriefingToolStripMenuItem.Enabled = false;
            try
            {
                SetStatusBarMessage("Loading My Hero Briefing...");
                using var activity = BeginStatusBarActivity();
                var partyHeroes = PartyHeroUtility.LoadActiveParty(EnsurePlayerCharacterDirectories());
                string? authenticatedHeroName = null;
                var isDungeonMaster = false;
                IReadOnlyList<PcXpTotal> xpTotals = [];

                if (TryPromptForXpCredentials(out var characterName, out var password))
                {
                    var passwordValidation = await ValidateOptionalXpPasswordAsync(
                        characterName,
                        password,
                        "my hero briefing XP authentication");
                    if (passwordValidation == OptionalXpPasswordValidation.Valid)
                    {
                        authenticatedHeroName = characterName;
                        isDungeonMaster = IsDungeonMasterXpAccess(characterName);
                        try
                        {
                            var snapshot = await XpTrackingUtility.GetCurrentXpSnapshotAsync();
                            xpTotals = snapshot.Totals;
                        }
                        catch (Exception ex)
                        {
                            await AppendStartupErrorLogAsync("my hero briefing XP display", ex);
                            SetStatusBarMessage("My Hero Briefing loaded without XP totals. Contact the DM if XP should be visible.");
                        }
                    }
                    else if (passwordValidation == OptionalXpPasswordValidation.Invalid)
                    {
                        MessageBox.Show(
                            this,
                            "The character name and XP password did not match. My Hero Briefing will be shown without XP totals.",
                            "My Hero Briefing",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        SetStatusBarMessage("My Hero Briefing XP access denied. Showing briefing without XP totals.");
                    }
                    else
                    {
                        ShowMissingXpPasswordStoreWarning("My Hero Briefing");
                        SetStatusBarMessage("My Hero Briefing loaded without XP totals because the XP password file is missing.");
                    }
                }

                var threadPosts = LoadMyHeroBriefingThreadPosts();
                var encryptedTextIndex = LoadMyHeroBriefingEncryptedTextIndex();
                var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
                    partyHeroes,
                    AuthenticatedHeroName: authenticatedHeroName,
                    IsDungeonMaster: isDungeonMaster,
                    ThreadPosts: threadPosts,
                    XpTotals: xpTotals,
                    EncryptedTextIndex: encryptedTextIndex));

                if (briefing.NeedsHeroSelection)
                {
                    var selectedHeroName = PromptForMyHeroBriefingHeroSelection(briefing.HeroChoices);
                    if (string.IsNullOrWhiteSpace(selectedHeroName))
                    {
                        myHeroBriefingToolStripMenuItem.Enabled = true;
                        SetStatusBarMessage("My Hero Briefing canceled.");
                        return;
                    }

                    briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
                        partyHeroes,
                        SelectedHeroName: selectedHeroName,
                        AuthenticatedHeroName: authenticatedHeroName,
                        IsDungeonMaster: isDungeonMaster,
                        ThreadPosts: threadPosts,
                        XpTotals: xpTotals,
                        EncryptedTextIndex: encryptedTextIndex));
                }

                ShowMyHeroBriefing(briefing);
            }
            catch (Exception ex)
            {
                myHeroBriefingToolStripMenuItem.Enabled = true;
                await ReportOperationFailureAsync(
                    "my hero briefing display",
                    "My Hero Briefing unavailable",
                    "My Hero Briefing Error",
                    ex,
                    showDialog: true);
            }
        }

        private async Task<OptionalXpPasswordValidation> ValidateOptionalXpPasswordAsync(
            string characterName,
            string password,
            string logPhase)
        {
            try
            {
                return XpPasswordStoreUtility.ValidatePassword(characterName, password, AppContext.BaseDirectory)
                    ? OptionalXpPasswordValidation.Valid
                    : OptionalXpPasswordValidation.Invalid;
            }
            catch (FileNotFoundException ex) when (string.Equals(
                Path.GetFileName(ex.FileName),
                XpPasswordStoreUtility.FileName,
                StringComparison.OrdinalIgnoreCase))
            {
                await AppendStartupErrorLogAsync(logPhase, ex);
                return OptionalXpPasswordValidation.StoreUnavailable;
            }
        }

        private void ShowMissingXpPasswordStoreWarning(string featureName)
        {
            MessageBox.Show(
                this,
                $"The XP password hash file '{XpPasswordStoreUtility.FileName}' was not found. {featureName} will be shown without XP totals. Restore the file to the Release folder to enable protected XP access.",
                featureName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private enum OptionalXpPasswordValidation
        {
            Valid,
            Invalid,
            StoreUnavailable
        }

        private async void AdventureOutlineToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            ClearDiceRollsDisplayIfVisible();
            HideSearchPanel();
            EnableLoginInfoMenuItem();
            EnableShowPostTotalsMenuItem();
            EnableShowDiceRollsMenuItem();
            EnableXpMenuItem();
            EnablePartyMenuItem();
            EnableMyHeroBriefingMenuItem();

            adventureOutlineToolStripMenuItem.Enabled = false;
            try
            {
                SetStatusBarMessage("Loading adventure outline...");
                using var activity = BeginStatusBarActivity();
                var outlinePath = GetAdventureOutlinePath();
                var icPostsDirectory = Path.Combine(
                    GetReleaseDirectory(),
                    PostsDirectoryName,
                    InCharacterPostsDirectoryName);

                await AdventureOutlineUtility.UpdateAdventureOutlineAsync(
                    icPostsDirectory,
                    outlinePath).ConfigureAwait(true);

                var outline = File.Exists(outlinePath)
                    ? await File.ReadAllTextAsync(outlinePath).ConfigureAwait(true)
                    : await AdventureOutlineUtility.BuildAdventureOutlineAsync(icPostsDirectory).ConfigureAwait(true);

                ShowAdventureOutline(outline);
            }
            catch (Exception ex)
            {
                adventureOutlineToolStripMenuItem.Enabled = true;
                await ReportOperationFailureAsync(
                    "adventure outline display",
                    "Adventure outline unavailable",
                    "Adventure Outline Error",
                    ex,
                    showDialog: true);
            }
        }

        private static PcXpTotal? FindXpTotalForCharacter(
            IReadOnlyList<PcXpTotal> totals,
            string characterName)
        {
            var trimmedName = characterName.Trim();
            var exactMatch = totals.FirstOrDefault(row =>
                string.Equals(row.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch is not null)
            {
                return exactMatch;
            }

            var firstName = GetFirstName(trimmedName);
            var firstNameMatches = totals
                .Where(row => string.Equals(GetFirstName(row.Name), firstName, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            return firstNameMatches.Length == 1
                ? firstNameMatches[0]
                : null;
        }

        private bool TryPromptForXpCredentials(out string characterName, out string password)
        {
            using var dialog = new Form
            {
                Text = "XP",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(360, 158)
            };

            using var characterLabel = new Label
            {
                AutoSize = true,
                Location = new Point(18, 22),
                Text = "Character name:"
            };
            using var characterTextBox = new TextBox
            {
                Location = new Point(138, 18),
                Size = new Size(198, 23)
            };
            using var passwordLabel = new Label
            {
                AutoSize = true,
                Location = new Point(18, 62),
                Text = "XP password:"
            };
            using var passwordTextBox = new TextBox
            {
                Location = new Point(138, 58),
                PasswordChar = '*',
                Size = new Size(198, 23)
            };
            using var okButton = new Button
            {
                DialogResult = DialogResult.OK,
                Location = new Point(180, 108),
                Size = new Size(75, 26),
                Text = "OK"
            };
            using var cancelButton = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(261, 108),
                Size = new Size(75, 26),
                Text = "Cancel"
            };

            dialog.Controls.AddRange(
                [
                    characterLabel,
                    characterTextBox,
                    passwordLabel,
                    passwordTextBox,
                    okButton,
                    cancelButton
                ]);
            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;

            characterName = string.Empty;
            password = string.Empty;

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return false;
            }

            characterName = characterTextBox.Text.Trim();
            password = passwordTextBox.Text;
            if (!string.IsNullOrWhiteSpace(characterName) && password.Length > 0)
            {
                return true;
            }

            MessageBox.Show(
                this,
                "Enter both character name and XP password.",
                "XP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        private string? PromptForMyHeroBriefingHeroSelection(IReadOnlyList<string> heroChoices)
        {
            var choices = heroChoices
                .Where(choice => !string.IsNullOrWhiteSpace(choice))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(choice => choice, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (choices.Length == 0)
            {
                MessageBox.Show(
                    this,
                    "No active heroes are available for My Hero Briefing.",
                    "My Hero Briefing",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return null;
            }

            if (choices.Length == 1)
            {
                return choices[0];
            }

            using var dialog = new Form
            {
                Text = "My Hero Briefing",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(380, 132)
            };

            using var label = new Label
            {
                AutoSize = true,
                Location = new Point(18, 22),
                Text = "Hero:"
            };
            using var comboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(82, 18),
                Size = new Size(274, 23)
            };
            comboBox.Items.AddRange(choices.Cast<object>().ToArray());
            comboBox.SelectedIndex = 0;

            using var okButton = new Button
            {
                DialogResult = DialogResult.OK,
                Location = new Point(200, 82),
                Size = new Size(75, 26),
                Text = "OK"
            };
            using var cancelButton = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(281, 82),
                Size = new Size(75, 26),
                Text = "Cancel"
            };

            dialog.Controls.AddRange([label, comboBox, okButton, cancelButton]);
            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;

            return dialog.ShowDialog(this) == DialogResult.OK
                ? comboBox.SelectedItem?.ToString()
                : null;
        }

        private static IReadOnlyList<MyHeroBriefingThreadPosts> LoadMyHeroBriefingThreadPosts()
        {
            var icPostsDirectory = MyHeroBriefingPostsDirectoryOverride
                ?? Path.Combine(GetReleaseDirectory(), PostsDirectoryName, InCharacterPostsDirectoryName);
            if (!Directory.Exists(icPostsDirectory))
            {
                return [];
            }

            var threadPosts = new List<MyHeroBriefingThreadPosts>();
            LoadMyHeroBriefingSourceExportThreads(icPostsDirectory, threadPosts);
            if (threadPosts.Count == 0)
            {
                LoadMyHeroBriefingFlatCacheThreads(icPostsDirectory, threadPosts);
            }

            return threadPosts
                .OrderBy(thread => thread.ThreadTitle, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void LoadMyHeroBriefingSourceExportThreads(
            string icPostsDirectory,
            List<MyHeroBriefingThreadPosts> threadPosts)
        {
            foreach (var sourcePath in Directory.EnumerateFiles(icPostsDirectory, "_source-show-all.html", SearchOption.AllDirectories))
            {
                try
                {
                    var html = File.ReadAllText(sourcePath);
                    var posts = RpolThreadPostUtility.GetThreadPostsFromHtml(html);
                    if (posts.Length == 0)
                    {
                        continue;
                    }

                    var manifest = TryLoadRpolThreadManifest(Path.Combine(Path.GetDirectoryName(sourcePath) ?? icPostsDirectory, "manifest.json"));
                    var threadTitle = !string.IsNullOrWhiteSpace(manifest?.ThreadTitle)
                        ? manifest.ThreadTitle
                        : Path.GetFileName(Path.GetDirectoryName(sourcePath) ?? icPostsDirectory);
                    var threadUrl = !string.IsNullOrWhiteSpace(manifest?.SourceUrl)
                        ? manifest.SourceUrl
                        : sourcePath;

                    threadPosts.Add(new MyHeroBriefingThreadPosts(threadTitle, threadUrl, posts));
                }
                catch
                {
                    continue;
                }
            }
        }

        private static void LoadMyHeroBriefingFlatCacheThreads(
            string icPostsDirectory,
            List<MyHeroBriefingThreadPosts> threadPosts)
        {
            var flatHtmlFiles = Directory
                .EnumerateFiles(icPostsDirectory, "*.html", SearchOption.TopDirectoryOnly)
                .Concat(EnumerateMyHeroBriefingAsideHtmlFiles(icPostsDirectory))
                .Where(IsMyHeroBriefingCurrentFlatThreadFile);

            foreach (var sourcePath in flatHtmlFiles)
            {
                try
                {
                    var posts = RpolThreadPostUtility.GetThreadPostsFromHtml(File.ReadAllText(sourcePath));
                    if (posts.Length == 0)
                    {
                        continue;
                    }

                    threadPosts.Add(new MyHeroBriefingThreadPosts(
                        Path.GetFileNameWithoutExtension(sourcePath),
                        sourcePath,
                        posts));
                }
                catch
                {
                    continue;
                }
            }
        }

        private static IEnumerable<string> EnumerateMyHeroBriefingAsideHtmlFiles(string icPostsDirectory)
        {
            var asideDirectory = Path.Combine(icPostsDirectory, AsidePostsDirectoryName);
            return Directory.Exists(asideDirectory)
                ? Directory.EnumerateFiles(asideDirectory, "*.html", SearchOption.TopDirectoryOnly)
                : [];
        }

        private static bool IsMyHeroBriefingCurrentFlatThreadFile(string path)
        {
            var fileName = Path.GetFileName(path);
            return !fileName.Equals("_source-show-all.html", StringComparison.OrdinalIgnoreCase)
                && !fileName.Equals("index.html", StringComparison.OrdinalIgnoreCase)
                && !fileName.Contains(".bak-", StringComparison.OrdinalIgnoreCase);
        }

        private static RpolThreadSplitResult? TryLoadRpolThreadManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<RpolThreadSplitResult>(File.ReadAllText(manifestPath));
            }
            catch
            {
                return null;
            }
        }

        private static IReadOnlyList<EncryptedTextIndexEntry> LoadMyHeroBriefingEncryptedTextIndex()
        {
            var indexPath = GetEncryptedTextIndexPath();
            if (!File.Exists(indexPath))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<EncryptedTextIndexEntry[]>(File.ReadAllText(indexPath)) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private async void RegionalMapToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            ClearDiceRollsDisplayIfVisible();
            EnableLoginInfoMenuItem();
            EnableShowPostTotalsMenuItem();
            EnableShowDiceRollsMenuItem();
            EnableXpMenuItem();
            EnablePartyMenuItem();
            EnableAdventureOutlineMenuItem();
            var searchPanelWasHidden = HideSearchPanel();
            var shouldRefreshBeforeShowingMap = searchPanelWasHidden
                || _showLoginInfo
                || _showXpTotal
                || _showParty
                || _showMyHeroBriefing
                || _adventureOutlineTextBox is not null
                || _myHeroBriefingTextBox is not null
                || _showWelcomeText
                || _showHeroIntroText
                || _showAttributionText;

            var regionalMapPath = GetRegionalMapPath();
            UpdateRegionalMapMenuItem();

            if (!regionalMapToolStripMenuItem.Enabled)
            {
                SetStatusBarMessage($"Regional map unavailable: {regionalMapPath}");
                return;
            }

            try
            {
                _regionalMapTransitionPending = true;
                ClearDisplaySurfaceForRegionalMap();
                _regionalMapActive = true;
                if (shouldRefreshBeforeShowingMap)
                {
                    Refresh();
                }

                var regionalMapImage = TryCreateRegionalMapDisplayImage(regionalMapPath);
                if (regionalMapImage is null)
                {
                    regionalMapToolStripMenuItem.Enabled = false;
                    SetStatusBarMessage("Loading regional map...");
                    using var activity = BeginStatusBarActivity();
                    await PreloadRegionalMapImageAsync();
                    regionalMapImage = TryCreateRegionalMapDisplayImage(regionalMapPath)
                        ?? LoadImageCopy(regionalMapPath);
                }

                _regionalMapImage?.Dispose();
                _regionalMapImage = regionalMapImage;
                ShowRegionalMapPanel();
                _regionalMapTransitionPending = false;
                loginInfoToolStripMenuItem.Enabled = true;
                showPostTotalsToolStripMenuItem.Enabled = true;
                EnableXpMenuItem();
                EnablePartyMenuItem();
                EnableMyHeroBriefingMenuItem();
                EnableAdventureOutlineMenuItem();
                UpdateRegionalMapMenuItem();
                SetStatusBarMessage($"Regional map loaded: {RegionalMapFileName}.");
                Invalidate();
            }
            catch (Exception ex)
            {
                _regionalMapActive = false;
                _regionalMapTransitionPending = false;
                _regionalMapImage?.Dispose();
                _regionalMapImage = null;
                UpdateRegionalMapMenuItem();
                await ReportOperationFailureAsync(
                    "regional map display",
                    "Regional map unavailable",
                    "Regional Map Error",
                    ex,
                    showDialog: true);
            }
        }

        private void WhiteMarbleBackgroundTilingToolStripMenuItem_CheckedChanged(object? sender, EventArgs e)
        {
            ClearDiceRollsDisplayIfVisible();
            EnableLoginInfoMenuItem();
            EnableShowPostTotalsMenuItem();
            UserPreferencesUtility.WhiteMarbleBackgroundTilingEnabled = whiteMarbleBackgroundTilingToolStripMenuItem.Checked;
            UserPreferencesUtility.Save();
            ApplyWhiteMarbleBackgroundTiling();
        }

        private void WhiteMarbleBackgroundTilingToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            ClearDiceRollsDisplayIfVisible();
            EnableLoginInfoMenuItem();
            EnableShowPostTotalsMenuItem();
            HideSearchPanel();
        }

        private void SkipHeroImageParadeAtStartupToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            UserPreferencesUtility.SkipHeroImageParadeAtStartup = skipHeroImageParadeAtStartupToolStripMenuItem.Checked;
            UserPreferencesUtility.Save();
            SetStatusBarMessage(
                UserPreferencesUtility.SkipHeroImageParadeAtStartup
                    ? "Hero images will be skipped on the next startup."
                    : "Hero images will play on the next startup.");
        }

        private void AuthorToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            MessageBox.Show(
                this,
                GetAuthorInfoText(),
                "Author",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private async void CheckForUpdateToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            checkForUpdateToolStripMenuItem.Enabled = false;
            try
            {
                SetStatusBarMessage("Checking for updates...");
                using var activity = BeginStatusBarActivity();
                using var httpClient = NetworkRequestUtility.CreateHttpClient();
                var update = await PlayerAssistantUpdateUtility.CheckForLatestUpdateAsync(httpClient);
                var currentVersion = PlayerAssistantUpdateUtility.GetCurrentAppVersion();
                if (update is null)
                {
                    MessageBox.Show(
                        this,
                        "No Player Assistant update archive was found.",
                        "Check for Update",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    SetStatusBarMessage("No update archive found.");
                    return;
                }

                if (!update.IsNewerThan(currentVersion))
                {
                    MessageBox.Show(
                        this,
                        GetLatestVersionMessage(),
                        "Check for Update",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    SetStatusBarMessage($"Player Assistant is up to date ({currentVersion}).");
                    return;
                }

                var result = MessageBox.Show(
                    this,
                    $"Player Assistant {update.VersionText} is available.{Environment.NewLine}{Environment.NewLine}Current version: {currentVersion}{Environment.NewLine}{Environment.NewLine}Download the update now?",
                    "Update Available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (result != DialogResult.Yes)
                {
                    SetStatusBarMessage($"Update available: {update.VersionText}.");
                    return;
                }

                SetStatusBarMessage($"Downloading verified installer: {update.VersionText}...");
                var installer = await VerifiedInstallerUpdateUtility.DownloadVerifiedInstallerAsync(httpClient, update);
                var installerLaunchTicket = VerifiedInstallerLaunchUtility.CreateLaunchTicket(installer);
                var launchInstallerResult = MessageBox.Show(
                    this,
                    $"Player Assistant {update.VersionText} was downloaded and verified.{Environment.NewLine}{Environment.NewLine}Installer: {installer.InstallerPath}{Environment.NewLine}{Environment.NewLine}Run the installer now?",
                    "Verified Update Ready",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (launchInstallerResult != DialogResult.Yes)
                {
                    SetStatusBarMessage($"Verified installer ready: {update.VersionText}.");
                    return;
                }

                Process.Start(VerifiedInstallerLaunchUtility.CreateStartInfo(installerLaunchTicket));
                SetStatusBarMessage($"Launching verified installer: {update.VersionText}.");
            }
            catch (Exception ex)
            {
                await ReportOperationFailureAsync(
                    "update check",
                    "Update check unavailable",
                    "Check for Update",
                    ex,
                    showDialog: true);
            }
            finally
            {
                checkForUpdateToolStripMenuItem.Enabled = true;
            }
        }

        private void VersionToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            MessageBox.Show(
                this,
                GetAppVersionText(),
                "Version",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static string GetAuthorInfoText()
        {
            return string.Join(
                Environment.NewLine,
                "Bryan Miller",
                "kyrathasoft@gmail.com",
                "bryanmiller.us");
        }

        private static string GetLatestVersionMessage()
        {
            return "You are using the latest version of this software.";
        }

        private static string GetAppVersionText()
        {
            return $"RPOL Scarlet Horizon Campaign Assistant {PlayerAssistantUpdateUtility.GetCurrentAppVersion()}";
        }

        private void ShowSearchPanel()
        {
            UpdateSearchPanelBounds();
            pnlSearch.Visible = true;
            _searchResultsRequested = false;
            UpdateSearchButtonEnabledState();
            UpdateSearchResultsVisibility([]);
            pnlSearch.BringToFront();
            menuStrip.BringToFront();
            statusStrip.BringToFront();
            searchToolStripMenuItem.Enabled = false;
            txtSearch.Focus();
        }

        private bool HideSearchPanel()
        {
            if (!pnlSearch.Visible)
            {
                return false;
            }

            pnlSearch.Visible = false;
            _searchResultsRequested = false;
            UpdateSearchResultsVisibility([]);
            searchToolStripMenuItem.Enabled = true;
            ClearPaintedFormSurface();
            return true;
        }

        private void ClearPaintedFormSurface()
        {
            DisposeDiceRollsListBox();
            DisposeAdventureOutlineTextBox();
            _showLoginInfo = false;
            _showWelcomeText = false;
            _showHeroIntroText = false;
            _showAttributionText = false;
            _adventureOutlineMarkdown = string.Empty;
            Invalidate();
            Update();
        }

        private void UpdateSearchPanelBounds()
        {
            pnlSearch.Bounds = new Rectangle(
                10,
                35,
                Math.Max(0, ClientSize.Width - 30),
                Math.Max(0, ClientSize.Height - 70));

            CenterSearchControls();
        }

        private void CenterSearchControls()
        {
            const int controlSpacing = 16;
            const int searchScopePanelWidthPadding = 10;
            const int searchResultsPanelTopMargin = 8;
            var totalWidth = lblSearchPrompt.Width + controlSpacing + txtSearch.Width + controlSpacing + btnSearch.Width;
            var startX = Math.Max(0, (pnlSearch.ClientSize.Width - totalWidth) / 2);
            var centerY = 40;
            const int searchCharacterCountTopMargin = 8;
            const int searchScopePanelTopMargin = 8;

            lblSearchPrompt.Location = new Point(startX, centerY + (txtSearch.Height - lblSearchPrompt.Height) / 2);
            txtSearch.Location = new Point(lblSearchPrompt.Right + controlSpacing, centerY);
            btnSearch.Location = new Point(txtSearch.Right + controlSpacing, centerY - 1);
            lblSearchCharacterCnt.Location = new Point(txtSearch.Left, txtSearch.Bottom + searchCharacterCountTopMargin);
            pnlSearchScope.Location = new Point(txtSearch.Left, lblSearchCharacterCnt.Bottom + searchScopePanelTopMargin);
            pnlSearchScope.Width = txtSearch.Width + searchScopePanelWidthPadding;
            UpdateSearchScopePanelLayout();

            var searchResultsTop = pnlSearchScope.Bottom + searchResultsPanelTopMargin;
            pnlSearchResults.Width = (pnlSearch.ClientSize.Width * 3) / 4;
            pnlSearchResults.Height = Math.Max(0, pnlSearch.ClientSize.Height - searchResultsTop - 12);
            pnlSearchResults.Location = new Point(
                Math.Max(0, (pnlSearch.ClientSize.Width - pnlSearchResults.Width) / 2),
                searchResultsTop);
            lstSearchResults.ColumnWidth = Math.Max(120, pnlSearchResults.ClientSize.Width / 3);
        }

        private void TxtSearch_TextChanged(object? sender, EventArgs e)
        {
            _searchResultsRequested = false;
            UpdateSearchButtonEnabledState();
            UpdateSearchResultsVisibility(GetSearchTerms(txtSearch.Text));
        }

        private async void BtnSearch_Click(object? sender, EventArgs e)
        {
            await PerformSearchAsync();
        }

        private async Task PerformSearchAsync()
        {
            using var activity = BeginStatusBarActivity();
            using var searchOperation = StartSearchOperation();
            await PerformSearchAsync(searchOperation.Token, searchOperation);
        }

        private async Task PerformSearchAsync(
            CancellationToken cancellationToken,
            CancellationTokenSource? searchOperation = null)
        {
            var searchTerms = GetSearchTerms(txtSearch.Text);
            lstSearchResults.Items.Clear();
            _searchResultsRequested = true;
            UpdateSearchResultsVisibility(searchTerms);
            btnSearch.Enabled = false;

            SetStatusBarMessage(
                searchTerms.Length.ToString() + $" search term(s) entered by the user: {string.Join(", ", searchTerms)}");

            try
            {
                var localIndexUnavailable = false;
                var localMatchesFound = false;

                for (var i = 0; i < searchTerms.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var searchOutcome = await SearchIndexFileForTermAsync(searchTerms[i], i + 1, searchTerms.Length, cancellationToken);
                    localMatchesFound |= searchOutcome == LocalIndexSearchOutcome.FoundMatches;
                    localIndexUnavailable |= searchOutcome == LocalIndexSearchOutcome.IndexUnavailable;
                }

                if (!localMatchesFound
                    && !localIndexUnavailable
                    && searchTerms.Length > 0
                    && _showLocalIndexMissPrompt(searchTerms) == DialogResult.Yes)
                {
                    try
                    {
                        SetStatusBarMessage("Searching online for matching URLs.");
                        var onlineResults = await _onlineSearchProvider(searchTerms, cancellationToken);
                        foreach (var url in onlineResults)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            AddSearchResultUrl(url, displayAsUppercase: IsObsidianWikiSearchResultUrl(url));
                        }

                        await BackfillKeywordIndexWithOnlineResultsAsync(searchTerms, onlineResults, cancellationToken);
                        _showOnlineSearchCompletedMessage(searchTerms, onlineResults.Length);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        await ReportOperationFailureAsync(
                            "online search",
                            "Online search unavailable",
                            "Search Error",
                            ex,
                            showDialog: false);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                lblSearchCharacterCnt.Visible = true;
                lblSearchCharacterCnt.Text = $"Results found: {lstSearchResults.Items.Count}";
                SetStatusBarMessage(
                    $"Search results: {lstSearchResults.Items.Count} URL{(lstSearchResults.Items.Count == 1 ? string.Empty : "s")} found.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (IsCurrentSearchOperation(searchOperation))
                {
                    SetStatusBarMessage("Search canceled.");
                }
            }
            finally
            {
                if (IsCurrentSearchOperation(searchOperation))
                {
                    _searchOperationCancellation = null;
                    UpdateSearchButtonEnabledState();
                }
            }
        }

        private CancellationTokenSource StartSearchOperation()
        {
            _searchOperationCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _searchOperationCancellation = cancellation;
            return cancellation;
        }

        private bool IsCurrentSearchOperation(CancellationTokenSource? searchOperation)
        {
            return searchOperation is null
                || ReferenceEquals(_searchOperationCancellation, searchOperation);
        }

        private async Task<LocalIndexSearchOutcome> SearchIndexFileForTermAsync(
            string term,
            int searchTermNumber,
            int totalSearchTerms,
            CancellationToken cancellationToken)
        {
            var indexPath = GetKeywordIndexPath();
            if (!File.Exists(indexPath))
            {
                SetStatusBarMessage($"Keyword index unavailable: {indexPath}");
                return LocalIndexSearchOutcome.IndexUnavailable;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetStatusBarMessage(
                    $"searching against {KeywordIndexFileName} for search term {searchTermNumber} of {totalSearchTerms}: '{term}'");
                using var document = JsonDocument.Parse(ReadTextFileShared(indexPath));
                if (!document.RootElement.TryGetProperty("words", out var wordsElement)
                    || wordsElement.ValueKind != JsonValueKind.Object)
                {
                    return LocalIndexSearchOutcome.NotFound;
                }

                var keywordEntry = FindKeywordEntry(wordsElement, term);

                if (!keywordEntry.HasValue
                    || !keywordEntry.Value.TryGetProperty("matches", out var matchesElement)
                    || matchesElement.ValueKind != JsonValueKind.Array)
                {
                    return LocalIndexSearchOutcome.NotFound;
                }

                var foundMatch = false;
                var isHeroNameSearchTerm = IsHeroNameSearchTerm(term);

                foreach (var matchElement in matchesElement.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!matchElement.TryGetProperty("url", out var urlElement))
                    {
                        continue;
                    }

                    var url = urlElement.GetString();
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    var normalizedUrl = NormalizeSearchResultUrl(url);
                    if (isHeroNameSearchTerm
                        && IsRpolSearchResultUrl(normalizedUrl)
                        && !await _rpolHeroNameBodyMatchProvider(normalizedUrl, term, cancellationToken))
                    {
                        continue;
                    }

                    foundMatch = true;
                    AddSearchResultUrl(normalizedUrl);
                }

                return foundMatch
                    ? LocalIndexSearchOutcome.FoundMatches
                    : LocalIndexSearchOutcome.NotFound;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetStatusBarMessage($"Keyword index unavailable: {ex.Message}");
                return LocalIndexSearchOutcome.IndexUnavailable;
            }
        }

        private static JsonElement? FindKeywordEntry(JsonElement wordsElement, string term)
        {
            if (TryFindKeywordEntry(wordsElement, term, out var keywordEntry))
            {
                return keywordEntry;
            }

            var prefixedTerm = "The " + term;
            return TryFindKeywordEntry(wordsElement, prefixedTerm, out keywordEntry)
                ? keywordEntry
                : null;
        }

        private static bool TryFindKeywordEntry(JsonElement wordsElement, string term, out JsonElement keywordEntry)
        {
            foreach (var wordProperty in wordsElement.EnumerateObject())
            {
                if (!string.Equals(wordProperty.Name, term, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                keywordEntry = wordProperty.Value;
                return true;
            }

            keywordEntry = default;
            return false;
        }

        private static string ReadTextFileShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        internal static async Task BackfillKeywordIndexWithOnlineResultsAsync(
            string[] searchTerms,
            string[] onlineResults,
            CancellationToken cancellationToken)
        {
            if (searchTerms.Length == 0 || onlineResults.Length == 0)
            {
                return;
            }

            var indexPath = GetKeywordIndexPath();
            if (!File.Exists(indexPath))
            {
                return;
            }

            try
            {
                var root = JsonNode.Parse(ReadTextFileShared(indexPath)) as JsonObject;
                if (root?["words"] is not JsonObject words)
                {
                    return;
                }

                var changed = false;
                foreach (var term in searchTerms
                    .Select(term => term.Trim())
                    .Where(term => term.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (KeywordIndexContainsTerm(words, term))
                    {
                        continue;
                    }

                    var matchingUrls = GetOnlineResultUrlsForKeywordBackfill(term, searchTerms.Length, onlineResults);
                    if (matchingUrls.Length == 0)
                    {
                        continue;
                    }

                    words[term] = CreateKeywordIndexWordNode(matchingUrls);
                    changed = true;
                }

                if (!changed)
                {
                    return;
                }

                UpdateKeywordIndexMetadata(root, words.Count);
                var updatedJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                KeywordIndexCrawler.ValidateKeywordIndexJson(updatedJson);
                await AtomicFileUtility.WriteAllTextAsync(indexPath, updatedJson, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
            {
                StartupLoggingUtility.Append("keyword index online backfill", ex);
            }
        }

        private static bool KeywordIndexContainsTerm(JsonObject words, string term)
        {
            return words.Any(pair => string.Equals(pair.Key, term, StringComparison.OrdinalIgnoreCase));
        }

        private static string[] GetOnlineResultUrlsForKeywordBackfill(
            string term,
            int searchTermCount,
            string[] onlineResults)
        {
            var allowedResults = onlineResults
                .Select(NormalizeSearchResultUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Where(url => NetworkUrlAllowlistUtility.Validate(url).IsAllowed)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var matchingResults = allowedResults
                .Where(url => SearchTextMatches(url, term))
                .ToArray();
            if (matchingResults.Length > 0 || searchTermCount > 1)
            {
                return matchingResults;
            }

            return allowedResults;
        }

        private static JsonObject CreateKeywordIndexWordNode(string[] urls)
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            var matches = new JsonArray();
            foreach (var url in urls.OrderBy(url => url, StringComparer.OrdinalIgnoreCase))
            {
                matches.Add(new JsonObject
                {
                    ["url"] = url,
                    ["count"] = 1,
                    ["last_indexed"] = now
                });
            }

            return new JsonObject
            {
                ["total_occurrences"] = urls.Length,
                ["matches"] = matches
            };
        }

        private static void UpdateKeywordIndexMetadata(JsonObject root, int totalWordsIndexed)
        {
            if (root["index_metadata"] is not JsonObject metadata)
            {
                metadata = [];
                root["index_metadata"] = metadata;
            }

            metadata["generated_at"] = DateTimeOffset.UtcNow.ToString("O");
            metadata["total_words_indexed"] = totalWordsIndexed;
        }

        private DialogResult ShowLocalIndexMissPrompt(string[] searchTerms)
        {
            var promptPrefix = searchTerms.Length == 1
                ? $"The term '{searchTerms[0]}' was not found in the local index."
                : $"These terms were not found in the local index: {string.Join(", ", searchTerms)}.";

            return MessageBox.Show(
                this,
                $"{promptPrefix}{Environment.NewLine}{Environment.NewLine}Would you like to search online instead?",
                "Search Online",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
        }

        private void ShowOnlineSearchCompletedMessage(string[] searchTerms, int resultCount)
        {
            var subject = searchTerms.Length == 1
                ? $"Online search for '{searchTerms[0]}' has completed."
                : $"Online search for {searchTerms.Length} terms has completed.";

            MessageBox.Show(
                this,
                $"{subject}{Environment.NewLine}{Environment.NewLine}Results found: {resultCount}",
                "Online Search Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);
        }

        private async Task<string[]> SearchOnlineForTermsAsync(string[] searchTerms, CancellationToken cancellationToken)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (ShouldSearchRpolOnline())
            {
                foreach (var url in await SearchRpolOnlineAsync(searchTerms, cancellationToken))
                {
                    results.Add(url);
                }
            }

            if (ShouldSearchObsidianOnline())
            {
                foreach (var url in await SearchObsidianOnlineAsync(searchTerms, cancellationToken))
                {
                    results.Add(url);
                }
            }

            return results.ToArray();
        }

        private async Task<string[]> SearchRpolOnlineAsync(string[] searchTerms, CancellationToken cancellationToken)
        {
            var hyperlinks = await HtmlUtility.GetRpolGameHyperlinksAsync(cancellationToken);
            var normalizedHeroSearchTerms = searchTerms
                .SelectMany(GetHeroSearchTermAliases)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var matchingUrls = hyperlinks
                .Where(hyperlink => searchTerms.Any(term => SearchTextMatches(hyperlink.Text, term) || SearchTextMatches(hyperlink.Url, term)))
                .Select(hyperlink => NormalizeSearchResultUrl(hyperlink.Url))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalizedHeroSearchTerms.Length == 0)
            {
                return matchingUrls;
            }

            var filteredUrls = matchingUrls
                .Where(url => !IsRpolSearchResultUrl(url))
                .ToList();

            var candidateRpolPostUrls = hyperlinks
                .Select(hyperlink => NormalizeSearchResultUrl(hyperlink.Url))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Where(IsRpolSearchResultUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var url in candidateRpolPostUrls)
            {
                foreach (var heroTerm in normalizedHeroSearchTerms)
                {
                    if (await _rpolHeroNameBodyMatchProvider(url, heroTerm, cancellationToken))
                    {
                        filteredUrls.Add(url);
                        break;
                    }
                }
            }

            return filteredUrls
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task<string[]> SearchObsidianOnlineAsync(string[] searchTerms, CancellationToken cancellationToken)
        {
            var tempSitemapPath = Path.Combine(Path.GetTempPath(), $"player-assistant-sitemap-{Guid.NewGuid():N}.xml");

            try
            {
                await SitemapUtility.DownloadSitemapAsync(SitemapUrl, tempSitemapPath, cancellationToken);
                var urls = await SitemapUtility.ReadUrlsFromSitemapAsync(tempSitemapPath, cancellationToken);

                return urls
                    .Where(url => searchTerms.Any(term => SearchTextMatches(url, term)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            finally
            {
                if (File.Exists(tempSitemapPath))
                {
                    File.Delete(tempSitemapPath);
                }
            }
        }

        private static bool SearchTextMatches(string candidate, string term)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(term))
            {
                return false;
            }

            var normalizedCandidate = Uri.UnescapeDataString(candidate)
                .Replace('+', ' ')
                .Replace('-', ' ');
            var normalizedTerm = term.Trim();

            return normalizedCandidate.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase);
        }

        private string[] GetHeroSearchTermAliases(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return [];
            }

            var trimmedTerm = term.Trim();
            var heroNames = GetHeroNamesForSearch().ToArray();
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var heroName in heroNames)
            {
                var firstName = GetFirstName(heroName);
                if (string.Equals(heroName, trimmedTerm, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(firstName, trimmedTerm, StringComparison.OrdinalIgnoreCase))
                {
                    aliases.Add(heroName);
                    aliases.Add(firstName);
                }
            }

            return aliases.ToArray();
        }

        private static string NormalizeSearchResultUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return url;
            }

            var isRpolThreadUrl = uri.AbsolutePath.EndsWith("/display.cgi", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.EndsWith("display.cgi", StringComparison.OrdinalIgnoreCase);

            return isRpolThreadUrl
                ? RpolThreadPostUtility.GetShowAllThreadUrl(uri.ToString())
                : uri.ToString();
        }

        private bool IsHeroNameSearchTerm(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return false;
            }

            return GetHeroNamesForSearch().Contains(term.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        private IEnumerable<string> GetHeroNamesForSearch()
        {
            var listingMarkdown = GetPlayerCharacterListingMarkdownForSearch();
            if (string.IsNullOrWhiteSpace(listingMarkdown))
            {
                return [];
            }

            return PlayerCharacterAssetUtility.GetHeroRows(listingMarkdown)
                .Select(row => row.Name.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private string GetPlayerCharacterListingMarkdownForSearch()
        {
            if (!string.IsNullOrWhiteSpace(_playerCharacterListingMarkdown))
            {
                return _playerCharacterListingMarkdown;
            }

            var cachedListingMarkdownPath = Path.Combine(
                GetReleaseDirectory(),
                PlayerCharactersDirectoryName,
                "player-characters-listing.md");

            return File.Exists(cachedListingMarkdownPath)
                ? ReadTextFileShared(cachedListingMarkdownPath)
                : string.Empty;
        }

        private static async Task<bool> DoesRpolPostBodyContainSearchTermAsync(
            string url,
            string term,
            CancellationToken cancellationToken)
        {
            var html = await GameForumUtility.GetRpolHtmlWithRateLimitAsync(url, cancellationToken);
            return RpolThreadPostUtility.GetThreadPostsFromHtml(html)
                .Any(post => SearchTextMatches(post.BodyText, term));
        }

        private static bool IsRpolSearchResultUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && RpolAuthUtility.IsRpolUri(uri);
        }

        private static string GetKeywordIndexPath()
        {
            return Path.Combine(GetApplicationExecutableDirectory(), KeywordIndexFileName);
        }

        private static string GetEncryptedTextIndexPath()
        {
            return Path.Combine(GetApplicationExecutableDirectory(), TaggedNoteCipherUtility.EncryptedTextIndexFileName);
        }

        private static string GetApplicationExecutableDirectory()
        {
#pragma warning disable IL3000
            var assemblyLocation = typeof(Form1).Assembly.Location;
#pragma warning restore IL3000
            if (!string.IsNullOrWhiteSpace(assemblyLocation))
            {
                var assemblyDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyLocation));
                if (!string.IsNullOrWhiteSpace(assemblyDirectory))
                {
                    return assemblyDirectory;
                }
            }

            return GetReleaseDirectory();
        }

        private void AddSearchResultUrl(string url, bool displayAsUppercase = false)
        {
            if (lstSearchResults.Items.Cast<object>()
                .Any(item => string.Equals(GetSearchResultLaunchUrl(item), url, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            lstSearchResults.Items.Add(CreateSearchResultItem(url, displayAsUppercase));
            SetStatusBarMessage($"Search results: {lstSearchResults.Items.Count} URL{(lstSearchResults.Items.Count == 1 ? string.Empty : "s")} found.");
        }

        private object CreateSearchResultItem(string url, bool displayAsUppercase)
        {
            return displayAsUppercase || IsEncryptedTextIndexUrl(url)
                ? new SearchResultItem(url, url.ToUpperInvariant())
                : url;
        }

        private static bool IsObsidianWikiSearchResultUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && NetworkUrlAllowlistUtility.IsObsidianPublishHost(uri);
        }

        private string? GetSearchResultLaunchUrl(object? selectedItem)
        {
            return selectedItem switch
            {
                SearchResultItem result => result.Url,
                null => null,
                _ => selectedItem.ToString()
            };
        }

        private bool IsEncryptedTextIndexUrl(string url)
        {
            return GetEncryptedTextIndexUrls().Contains(url);
        }

        private HashSet<string> GetEncryptedTextIndexUrls()
        {
            if (_encryptedTextIndexUrls is not null)
            {
                return _encryptedTextIndexUrls;
            }

            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var indexPath = GetEncryptedTextIndexPath();
            if (!File.Exists(indexPath))
            {
                _encryptedTextIndexUrls = urls;
                return _encryptedTextIndexUrls;
            }

            try
            {
                using var document = JsonDocument.Parse(ReadTextFileShared(indexPath));
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    _encryptedTextIndexUrls = urls;
                    return _encryptedTextIndexUrls;
                }

                foreach (var entry in document.RootElement.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object
                        && entry.TryGetProperty("url", out var urlElement)
                        && urlElement.ValueKind == JsonValueKind.String)
                    {
                        var entryUrl = urlElement.GetString();
                        if (!string.IsNullOrWhiteSpace(entryUrl))
                        {
                            urls.Add(entryUrl);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                StartupLoggingUtility.Append("encrypted text index load", ex);
            }

            _encryptedTextIndexUrls = urls;
            return _encryptedTextIndexUrls;
        }

        private bool ShouldSearchRpolOnline()
        {
            return rdoSearchDefault.Checked || rdoRPOL.Checked;
        }

        private bool ShouldSearchObsidianOnline()
        {
            return rdoSearchDefault.Checked || rdoObsidian.Checked;
        }

        private void UpdateSearchResultsVisibility(string[] searchResults)
        {
            if (pnlSearchResults.Parent != pnlSearch)
            {
                pnlSearch.Controls.Add(pnlSearchResults);
            }

            var showSearchResults = _searchResultsRequested && searchResults.Length > 0;
            pnlSearchResults.Visible = showSearchResults;
            lstSearchResults.Visible = showSearchResults;

            if (!showSearchResults)
            {
                return;
            }

            pnlSearchResults.BringToFront();
            lstSearchResults.BringToFront();
        }

        private void LstSearchResults_MouseClick(object? sender, MouseEventArgs e)
        {
            var selectedItem = GetSearchResultLaunchUrl(lstSearchResults.SelectedItem);
            if (string.IsNullOrWhiteSpace(selectedItem))
            {
                return;
            }

            var launchValidation = ExternalUrlLaunchUtility.Validate(selectedItem);
            if (!launchValidation.IsAllowed)
            {
                var reason = launchValidation.RejectionReason ?? "The selected URL cannot be opened.";
                SetStatusBarMessage(reason);
                StartupLoggingUtility.Append(
                    "external URL launch rejected",
                    reason);
                return;
            }

            var result = MessageBox.Show(
                this,
                $"Would you like to open this URL in a browser tab?{Environment.NewLine}{launchValidation.Url}{Environment.NewLine}{Environment.NewLine}Host: {launchValidation.Host}",
                "Open Search Result",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                return;
            }

            try
            {
                Process.Start(ExternalUrlLaunchUtility.CreateStartInfo(launchValidation));
            }
            catch (Exception ex)
            {
                StartupLoggingUtility.Append("external URL launch", ex);
                SetStatusBarMessage($"Unable to open URL: {ex.Message}");
            }
        }

        private sealed record SearchResultItem(string Url, string DisplayText)
        {
            public override string ToString()
            {
                return DisplayText;
            }
        }

        private void TxtSearch_EnterPressed(object? sender, EventArgs e)
        {
            if (!btnSearch.Enabled)
            {
                return;
            }

            btnSearch.PerformClick();
        }

        private void TxtSearch_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (e.KeyChar != ' ')
            {
                return;
            }

            var selectionStart = txtSearch.SelectionStart;
            var selectionLength = txtSearch.SelectionLength;
            var searchText = txtSearch.Text;
            var precedingCharacterIsSpace = selectionStart > 0 && searchText[selectionStart - 1] == ' ';
            var followingCharacterIndex = selectionStart + selectionLength;
            var followingCharacterIsSpace = followingCharacterIndex < searchText.Length && searchText[followingCharacterIndex] == ' ';

            if (precedingCharacterIsSpace || followingCharacterIsSpace)
            {
                e.Handled = true;
            }
        }

        private void TxtSearch_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void UpdateSearchButtonEnabledState()
        {
            var searchText = txtSearch.Text;
            var hasContiguousNonSpacePair = HasContiguousNonSpacePair(searchText);
            var hasAlphanumericCharacter = searchText.Any(char.IsLetterOrDigit);

            btnSearch.Enabled = hasContiguousNonSpacePair && hasAlphanumericCharacter;
            lblSearchCharacterCnt.Visible = hasContiguousNonSpacePair;
            lblSearchCharacterCnt.Text = $"Characters entered: {searchText.Length}";
            pnlSearchScope.Visible = btnSearch.Enabled;
        }

        private static bool HasContiguousNonSpacePair(string searchText)
        {
            for (var i = 1; i < searchText.Length; i++)
            {
                if (searchText[i - 1] != ' ' && searchText[i] != ' ')
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] GetSearchTerms(string searchText)
        {
            var searchTerms = new List<string>();
            var currentTerm = new List<char>();
            var insideQuotes = false;

            foreach (var character in searchText)
            {
                if (character == '"')
                {
                    insideQuotes = !insideQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(character) && !insideQuotes)
                {
                    AddSearchTerm(currentTerm, searchTerms);
                    continue;
                }

                currentTerm.Add(character);
            }

            AddSearchTerm(currentTerm, searchTerms);

            return searchTerms
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void AddSearchTerm(List<char> currentTerm, List<string> searchTerms)
        {
            if (currentTerm.Count == 0)
            {
                return;
            }

            var term = new string(currentTerm.ToArray()).Trim();
            currentTerm.Clear();

            if (term.Length == 0)
            {
                return;
            }

            searchTerms.Add(term);
        }

        private void PnlSearch_Paint(object? sender, PaintEventArgs e)
        {
            using var pen = new Pen(Color.LightGray);
            var borderBounds = pnlSearch.ClientRectangle;
            borderBounds.Width = Math.Max(0, borderBounds.Width - 1);
            borderBounds.Height = Math.Max(0, borderBounds.Height - 1);
            e.Graphics.DrawRectangle(pen, borderBounds);
        }

        private void PnlSearchScope_Paint(object? sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var panelBounds = pnlSearchScope.ClientRectangle;
            panelBounds.Width = Math.Max(0, panelBounds.Width - 1);
            panelBounds.Height = Math.Max(0, panelBounds.Height - 1);

            using var backgroundBrush = new SolidBrush(pnlSearchScope.BackColor);
            using var borderPen = new Pen(Color.Silver);
            using var roundedPath = CreateRoundedRectanglePath(panelBounds, 12);
            e.Graphics.FillPath(backgroundBrush, roundedPath);
            e.Graphics.DrawPath(borderPen, roundedPath);
        }

        private void PnlSearchScope_Resize(object? sender, EventArgs e)
        {
            UpdateSearchScopePanelLayout();
        }

        private void UpdateSearchScopePanelLayout()
        {
            const int radioButtonSpacing = 18;
            const int panelCornerRadius = 12;
            var radioButtons = new[] { rdoSearchDefault, rdoRPOL, rdoObsidian };
            var totalWidth = radioButtons.Sum(radioButton => radioButton.PreferredSize.Width)
                + (radioButtonSpacing * (radioButtons.Length - 1));
            var startX = Math.Max(12, (pnlSearchScope.ClientSize.Width - totalWidth) / 2);
            var maxHeight = radioButtons.Max(radioButton => radioButton.PreferredSize.Height);
            var startY = Math.Max(0, (pnlSearchScope.ClientSize.Height - maxHeight) / 2);
            var currentX = startX;

            foreach (var radioButton in radioButtons)
            {
                radioButton.Location = new Point(currentX, startY);
                currentX += radioButton.PreferredSize.Width + radioButtonSpacing;
            }

            pnlSearchScope.Region?.Dispose();
            using var roundedPath = CreateRoundedRectanglePath(pnlSearchScope.ClientRectangle, panelCornerRadius);
            pnlSearchScope.Region = new Region(roundedPath);
            pnlSearchScope.Invalidate();
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int cornerRadius)
        {
            var path = new GraphicsPath();

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return path;
            }

            var diameter = Math.Min(cornerRadius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void ApplyWhiteMarbleBackgroundTiling()
        {
            if (!whiteMarbleBackgroundTilingToolStripMenuItem.Checked)
            {
                SetBackgroundImage(LoadDragonBackgroundImage(), ImageLayout.Center);
                return;
            }

            SetBackgroundImage(LoadBackgroundImage(), ImageLayout.Tile);
        }

        private void SetBackgroundImage(Image? image, ImageLayout layout)
        {
            var previousImage = BackgroundImage;

            BackgroundImage = image;
            BackgroundImageLayout = layout;
            previousImage?.Dispose();
            Invalidate();
        }

        private async Task<string> RefreshLoginInfoJsonAsync(CancellationToken cancellationToken = default)
        {
            var tempDirectory = Path.Combine(GetReleaseDirectory(), TempDirectoryName);
            var loginInfoPath = GetLoginInfoPath();
            var theCastHtmlPath = GetTheCastHtmlPath();
            var tempLoginInfoPath = Path.Combine(tempDirectory, TheCastLoginInfoFileName);
            var oocPostsDirectory = Path.GetDirectoryName(loginInfoPath)
                ?? Path.Combine(GetReleaseDirectory(), PostsDirectoryName, OutOfCharacterPostsDirectoryName);

            Directory.CreateDirectory(oocPostsDirectory);
            Directory.CreateDirectory(tempDirectory);

            var tempCastDownload = await GameForumUtility.DownloadTheCastHtmlAsync(
                AppSettingsUtility.TheCastUrl,
                tempDirectory,
                forceDownload: true,
                cancellationToken);
            await GameForumUtility.WriteTheCastLoginInfoJsonAsync(
                tempCastDownload.FilePath,
                tempLoginInfoPath,
                cancellationToken);

            await PromoteFileIfChangedAsync(tempCastDownload.FilePath, theCastHtmlPath, cancellationToken);
            await PromoteFileIfChangedAsync(tempLoginInfoPath, loginInfoPath, cancellationToken);

            return loginInfoPath;
        }

        private async Task RefreshLoginInfoInBackgroundAsync()
        {
            if (_loginInfoRefreshStarted)
            {
                return;
            }

            _loginInfoRefreshStarted = true;
            SetStatusBarMessage(_loginInfoRefreshTarget == LoginInfoDisplayMode.PostTotals
                ? _showPostTotals
                    ? $"Post totals: {_postTotalsSummary?.Rows.Count ?? 0} cached rows loaded; refreshing..."
                    : "Refreshing post totals..."
                : _showLoginInfo
                    ? $"Login info: {_loginInfoRows.Length} cached rows loaded; refreshing..."
                    : "Refreshing login info...");

            try
            {
                var loginInfoPath = await RefreshLoginInfoJsonAsync();
                var loginInfoRows = await LoadLoginInfoJsonAsync(loginInfoPath);
                if (_regionalMapActive || _regionalMapTransitionPending)
                {
                    _loginInfoRows = loginInfoRows;
                    return;
                }

                if (_loginInfoRefreshTarget == LoginInfoDisplayMode.PostTotals || _showPostTotals)
                {
                    var theCastHtmlPath = GetTheCastHtmlPath();
                    ShowPostTotalsRows(
                        File.Exists(theCastHtmlPath)
                            ? LoadTheCastLoginInfoFromHtml(theCastHtmlPath)
                            : loginInfoRows,
                        "refreshed");
                    return;
                }

                ShowLoginInfoRows(loginInfoRows, "refreshed");
            }
            catch (Exception ex)
            {
                loginInfoToolStripMenuItem.Enabled = true;
                showPostTotalsToolStripMenuItem.Enabled = true;
                await ReportOperationFailureAsync(
                    _loginInfoRefreshTarget == LoginInfoDisplayMode.PostTotals
                        ? "post totals refresh"
                        : "login info refresh",
                    _loginInfoRefreshTarget == LoginInfoDisplayMode.PostTotals
                        ? "Post totals unavailable"
                        : "Login info unavailable",
                    _loginInfoRefreshTarget == LoginInfoDisplayMode.PostTotals
                        ? "Post Totals Error"
                        : "Login Info Error",
                    ex,
                    showDialog: true);
            }
            finally
            {
                _loginInfoRefreshStarted = false;
            }
        }

        private void ShowLoginInfoRows(TheCastLoginInfo[] loginInfoRows, string source)
        {
            _loginInfoRows = loginInfoRows;
            ClearDisplaySurfaceForLoginInfo();
            _showLoginInfo = true;
            _showPostTotals = false;
            _showXpTotal = false;
            _showParty = false;
            _showMyHeroBriefing = false;
            _xpTotals = [];
            _xpDateLabel = string.Empty;
            _partyHeroes = [];
            _postTotalsSummary = null;
            SetStatusBarMessage($"Login info: {_loginInfoRows.Length} {source} rows loaded.");
            Invalidate();
        }

        private void ShowPostTotalsRows(TheCastLoginInfo[] loginInfoRows, string source)
        {
            _loginInfoRows = loginInfoRows;
            _postTotalsSummary = PostTotalsUtility.BuildSummary(loginInfoRows, GetReleaseDirectory());
            ClearDisplaySurfaceForLoginInfo();
            _showLoginInfo = false;
            _showPostTotals = true;
            _showXpTotal = false;
            _showParty = false;
            _showMyHeroBriefing = false;
            _xpTotals = [];
            _xpDateLabel = string.Empty;
            _partyHeroes = [];
            SetStatusBarMessage($"Post totals: {_postTotalsSummary.Rows.Count} {source} rows loaded.");
            Invalidate();
        }

        private void ShowXpTotals(string dateLabel, IReadOnlyList<PcXpTotal> totals)
        {
            ClearDisplaySurfaceForLoginInfo();
            _showLoginInfo = false;
            _showPostTotals = false;
            _showParty = false;
            _showMyHeroBriefing = false;
            _partyHeroes = [];
            _showXpTotal = true;
            _postTotalsSummary = null;
            _xpDateLabel = dateLabel;
            _xpTotals = totals.ToArray();
            xpToolStripMenuItem.Enabled = false;
            SetStatusBarMessage(_xpTotals.Count == 1
                ? $"XP total: {_xpTotals[0].Name} has {_xpTotals[0].XpTotal:N0} XP."
                : $"XP totals: {_xpTotals.Count} PCs loaded.");
            Invalidate();
        }

        private void ShowPartyHeroes(IReadOnlyList<PartyHeroSheet> heroes)
        {
            ClearDisplaySurfaceForLoginInfo();
            _showLoginInfo = false;
            _showPostTotals = false;
            _showXpTotal = false;
            _showParty = true;
            _showMyHeroBriefing = false;
            _postTotalsSummary = null;
            _xpTotals = [];
            _xpDateLabel = string.Empty;
            _partyHeroes = heroes.ToArray();
            partyToolStripMenuItem.Enabled = false;
            BuildPartyPanel(_partyHeroes);
            SetStatusBarMessage(_partyHeroes.Count == 0
                ? "Party unavailable: no active heroes were found."
                : $"Party: {_partyHeroes.Count} active hero{(_partyHeroes.Count == 1 ? string.Empty : "es")} loaded.");
            Invalidate();
        }

        private void ShowMyHeroBriefing(MyHeroBriefing briefing)
        {
            ClearDisplaySurfaceForLoginInfo();
            _showLoginInfo = false;
            _showPostTotals = false;
            _showXpTotal = false;
            _showParty = false;
            _showMyHeroBriefing = true;
            _postTotalsSummary = null;
            _xpTotals = [];
            _xpDateLabel = string.Empty;
            _partyHeroes = [];
            myHeroBriefingToolStripMenuItem.Enabled = false;
            BuildMyHeroBriefingTextBox(FormatMyHeroBriefingForDisplay(briefing));
            SetStatusBarMessage(briefing.Hero is null
                ? "My Hero Briefing needs a hero selection."
                : $"My Hero Briefing: {briefing.Hero.Name} loaded.");
            Invalidate();
        }

        private void ShowAdventureOutline(string outlineMarkdown)
        {
            ClearDisplaySurfaceForLoginInfo();
            _showLoginInfo = false;
            _showPostTotals = false;
            _showXpTotal = false;
            _showParty = false;
            _showMyHeroBriefing = false;
            _postTotalsSummary = null;
            _xpTotals = [];
            _xpDateLabel = string.Empty;
            _partyHeroes = [];
            _adventureOutlineMarkdown = string.IsNullOrWhiteSpace(outlineMarkdown)
                ? "# Adventure Outline\r\n\r\nNo adventure outline entries are available yet."
                : outlineMarkdown.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
            adventureOutlineToolStripMenuItem.Enabled = false;
            BuildAdventureOutlineTextBox(_adventureOutlineMarkdown);
            SetStatusBarMessage("Adventure outline loaded.");
            Invalidate();
        }

        private void BuildAdventureOutlineTextBox(string outlineMarkdown)
        {
            DisposeAdventureOutlineTextBox();
            var displayLines = ParseAdventureOutlineDisplayLines(outlineMarkdown);

            var textBox = new RichTextBox
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                DetectUrls = true,
                Font = new Font("Segoe UI", 12),
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true
            };

            AppendAdventureOutlineText(textBox, displayLines);
            _adventureOutlineTextBox = textBox;
            Controls.Add(textBox);
            UpdateAdventureOutlineTextBoxBounds();
            textBox.BringToFront();
            menuStrip.BringToFront();
            statusStrip.BringToFront();
        }

        private void BuildMyHeroBriefingTextBox(string briefingText)
        {
            DisposeMyHeroBriefingTextBox();
            var textBox = new RichTextBox
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                DetectUrls = true,
                Font = new Font("Segoe UI", 11),
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Text = briefingText,
                WordWrap = true
            };

            _myHeroBriefingTextBox = textBox;
            Controls.Add(textBox);
            _ = textBox.Handle;
            StyleMyHeroBriefingText(textBox);
            UpdateMyHeroBriefingTextBoxBounds();
            textBox.BringToFront();
            menuStrip.BringToFront();
            statusStrip.BringToFront();
        }

        private static void StyleMyHeroBriefingText(RichTextBox textBox)
        {
            var keyBackColor = Color.FromArgb(246, 241, 222);
            foreach (var keyLine in MyHeroBriefingLikelyResponseKeyLines)
            {
                var start = textBox.Text.IndexOf(keyLine, StringComparison.Ordinal);
                if (start < 0)
                {
                    continue;
                }

                textBox.Select(start, keyLine.Length);
                textBox.SelectionBackColor = keyBackColor;
                textBox.SelectionFont = new Font(textBox.Font, FontStyle.Italic);
            }

            textBox.Select(0, 0);
        }

        private static string FormatMyHeroBriefingForDisplay(MyHeroBriefing briefing)
        {
            ArgumentNullException.ThrowIfNull(briefing);

            var builder = new StringBuilder();
            builder.AppendLine("My Hero Briefing");
            builder.AppendLine();
            builder.AppendLine(briefing.StatusMessage);
            builder.AppendLine();

            if (briefing.HeroCard is not null)
            {
                builder.AppendLine("Current Hero");
                builder.AppendLine($"{briefing.HeroCard.Name}");
                builder.AppendLine($"Class: {ValueOrUnavailable(briefing.HeroCard.CharacterClass)}");
                builder.AppendLine($"Level: {ValueOrUnavailable(briefing.HeroCard.Level)}");
                builder.AppendLine($"HP: {ValueOrUnavailable(briefing.HeroCard.HitPoints)}");
                builder.AppendLine($"XP: {FormatBriefingXpTotal(briefing.HeroCard)}");
                if (!string.IsNullOrWhiteSpace(briefing.HeroCard.TokenImagePath))
                {
                    builder.AppendLine($"Token: {briefing.HeroCard.TokenImagePath}");
                }

                builder.AppendLine();
            }

            AppendResponseItems(builder, briefing.LikelyResponseItems);
            AppendActivityItems(builder, briefing.RecentActivity);
            AppendUnlockedNotes(builder, briefing.UnlockedNotes);
            AppendQuickLinks(builder, briefing.QuickLinks);
            return builder.ToString().TrimEnd();
        }

        private static void AppendResponseItems(StringBuilder builder, IReadOnlyList<MyHeroBriefingResponseItem> items)
        {
            builder.AppendLine("Likely Open Response Items");
            AppendLikelyResponseKey(builder);
            if (items.Count == 0)
            {
                builder.AppendLine("No likely open response items were found.");
                builder.AppendLine();
                return;
            }

            foreach (var item in items)
            {
                builder.AppendLine($"- {item.ThreadTitle} #{item.MessageNumber} by {item.Author}: {item.Reason}");
                builder.AppendLine($"  {item.Excerpt}");
            }

            builder.AppendLine();
        }

        private static void AppendLikelyResponseKey(StringBuilder builder)
        {
            foreach (var line in MyHeroBriefingLikelyResponseKeyLines)
            {
                builder.AppendLine(line);
            }

            builder.AppendLine();
        }

        private static void AppendActivityItems(StringBuilder builder, IReadOnlyList<MyHeroBriefingActivityItem> items)
        {
            builder.AppendLine("Recent Hero Activity");
            if (items.Count == 0)
            {
                builder.AppendLine("No recent hero activity was found.");
                builder.AppendLine();
                return;
            }

            foreach (var item in items)
            {
                builder.AppendLine($"- {item.ThreadTitle} #{item.MessageNumber} by {item.Author} {FormatPostedAt(item.PostedDate, item.PostedTime)}");
                builder.AppendLine($"  {item.Excerpt}");
            }

            builder.AppendLine();
        }

        private static void AppendUnlockedNotes(StringBuilder builder, IReadOnlyList<MyHeroBriefingUnlockedNoteItem> notes)
        {
            builder.AppendLine("Relevant Unlocked Notes");
            if (notes.Count == 0)
            {
                builder.AppendLine("No relevant unlocked notes were found.");
                builder.AppendLine();
                return;
            }

            foreach (var note in notes)
            {
                builder.AppendLine($"- {note.Title}");
                builder.AppendLine($"  {note.Excerpt}");
                builder.AppendLine($"  {note.Url}");
            }

            builder.AppendLine();
        }

        private static void AppendQuickLinks(StringBuilder builder, IReadOnlyList<MyHeroBriefingQuickLink> quickLinks)
        {
            builder.AppendLine("Quick Links");
            if (quickLinks.Count == 0)
            {
                builder.AppendLine("No quick links are available.");
                return;
            }

            foreach (var link in quickLinks)
            {
                builder.AppendLine($"- {link.Label}: {link.Target}");
            }
        }

        private static string ValueOrUnavailable(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Unavailable" : value.Trim();
        }

        private static string FormatBriefingXpTotal(MyHeroBriefingHeroCard heroCard)
        {
            return heroCard.XpTotal.HasValue
                ? $"{heroCard.XpTotal.Value:N0} XP"
                : heroCard.XpTotalLabel;
        }

        private static string FormatPostedAt(string postedDate, string postedTime)
        {
            var value = $"{postedDate} {postedTime}".Trim();
            return value.Length == 0 ? string.Empty : $"({value})";
        }

        private static IReadOnlyList<(string Text, AdventureOutlineLineStyle Style)> ParseAdventureOutlineDisplayLines(string outlineMarkdown)
        {
            var displayLines = new List<(string Text, AdventureOutlineLineStyle Style)>();
            var skippingSourceFiles = false;
            var canStartYamlFrontmatter = true;
            var skippingYamlFrontmatter = false;

            foreach (var line in outlineMarkdown
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n'))
            {
                if (canStartYamlFrontmatter)
                {
                    canStartYamlFrontmatter = false;
                    if (line.Trim().Equals("---", StringComparison.Ordinal))
                    {
                        skippingYamlFrontmatter = true;
                        continue;
                    }
                }

                if (skippingYamlFrontmatter)
                {
                    if (line.Trim().Equals("---", StringComparison.Ordinal))
                    {
                        skippingYamlFrontmatter = false;
                    }

                    continue;
                }

                if (line.Equals("- Source files inspected:", StringComparison.OrdinalIgnoreCase))
                {
                    skippingSourceFiles = true;
                    continue;
                }

                if (skippingSourceFiles)
                {
                    if (line.StartsWith("  - ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    skippingSourceFiles = false;
                }

                if (line.StartsWith("# ", StringComparison.Ordinal))
                {
                    var titleText = line[2..].Trim();
                    if (titleText.Length > 0)
                    {
                        displayLines.Add((titleText, AdventureOutlineLineStyle.Title));
                    }

                    continue;
                }

                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    var chapterText = line[3..].Trim();
                    if (chapterText.Length > 0)
                    {
                        displayLines.Add((chapterText, AdventureOutlineLineStyle.Chapter));
                    }

                    continue;
                }

                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    var bulletText = line[2..].Trim();
                    if (bulletText.Length == 0)
                    {
                        continue;
                    }

                    displayLines.Add((bulletText, AdventureOutlineLineStyle.Bullet));
                    continue;
                }

                if (line.Trim().Equals("-", StringComparison.Ordinal)
                    || line.Trim().Equals("•", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.Length == 0
                    && displayLines.Count > 0
                    && displayLines[^1].Text.Length == 0)
                {
                    continue;
                }

                displayLines.Add((line.TrimEnd(), AdventureOutlineLineStyle.Body));
            }

            return displayLines;
        }

        private static void AppendAdventureOutlineText(
            RichTextBox textBox,
            IReadOnlyList<(string Text, AdventureOutlineLineStyle Style)> displayLines)
        {
            for (var index = 0; index < displayLines.Count; index++)
            {
                var line = displayLines[index];
                var start = textBox.TextLength;
                if (index > 0)
                {
                    textBox.AppendText(Environment.NewLine);
                    start = textBox.TextLength;
                }

                textBox.AppendText(line.Text);
                textBox.Select(start, line.Text.Length);
                textBox.SelectionBullet = false;
                textBox.SelectionIndent = 0;
                textBox.SelectionHangingIndent = 0;
                textBox.SelectionFont = new Font("Segoe UI", 12, FontStyle.Regular);
                textBox.SelectionColor = Color.FromArgb(32, 32, 32);

                if (line.Style == AdventureOutlineLineStyle.Title)
                {
                    textBox.SelectionFont = new Font("Segoe UI", 22, FontStyle.Bold);
                    textBox.SelectionColor = Color.FromArgb(40, 55, 75);
                }
                else if (line.Style == AdventureOutlineLineStyle.Chapter)
                {
                    textBox.SelectionFont = new Font("Segoe UI", 16, FontStyle.Bold);
                    textBox.SelectionColor = Color.FromArgb(55, 72, 95);
                }
                else if (line.Style == AdventureOutlineLineStyle.Bullet)
                {
                    textBox.SelectionBullet = true;
                    textBox.SelectionIndent = 18;
                    textBox.SelectionHangingIndent = 8;
                }

                textBox.Select(textBox.TextLength, 0);
                textBox.SelectionBullet = false;
                textBox.SelectionIndent = 0;
                textBox.SelectionHangingIndent = 0;
            }

            textBox.Select(0, 0);
        }

        private void BuildPartyPanel(IReadOnlyList<PartyHeroSheet> heroes)
        {
            DisposePartyPanel();

            var panel = new Panel
            {
                AutoScroll = true,
                BackColor = Color.White
            };
            _partyPanel = panel;
            Controls.Add(panel);
            UpdatePartyPanelBounds();

            var titleLabel = new Label
            {
                AutoSize = false,
                Font = new Font("Segoe UI", 22, FontStyle.Bold),
                ForeColor = Color.FromArgb(35, 35, 35),
                Location = new Point(24, 18),
                Size = new Size(Math.Max(320, panel.ClientSize.Width - 48), 42),
                Text = "Party"
            };
            panel.Controls.Add(titleLabel);

            var top = 76;
            if (heroes.Count == 0)
            {
                panel.Controls.Add(new Label
                {
                    AutoSize = false,
                    Font = new Font("Segoe UI", 12, FontStyle.Regular),
                    ForeColor = Color.FromArgb(75, 75, 75),
                    Location = new Point(24, top),
                    Size = new Size(Math.Max(320, panel.ClientSize.Width - 48), 36),
                    Text = "No active hero sheets were found."
                });
            }

            foreach (var hero in heroes)
            {
                var heroPanel = CreatePartyHeroPanel(hero, Math.Max(520, panel.ClientSize.Width - 64));
                heroPanel.Location = new Point(24, top);
                panel.Controls.Add(heroPanel);
                top += heroPanel.Height + 18;
            }

            panel.BringToFront();
            menuStrip.BringToFront();
            statusStrip.BringToFront();
        }

        private Panel CreatePartyHeroPanel(PartyHeroSheet hero, int width)
        {
            var heroPanel = new Panel
            {
                BackColor = Color.FromArgb(248, 249, 251),
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(width, 292)
            };

            var imageBox = new PictureBox
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(16, 16),
                Size = new Size(128, 128),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            if (!string.IsNullOrWhiteSpace(hero.TokenImagePath) && File.Exists(hero.TokenImagePath))
            {
                try
                {
                    imageBox.Image = LoadImageCopy(hero.TokenImagePath);
                }
                catch
                {
                }
            }

            var nameLabel = new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(35, 35, 35),
                Location = new Point(160, 16),
                Size = new Size(width - 184, 34),
                Text = hero.Name
            };
            var summaryLabel = new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(75, 75, 75),
                Location = new Point(160, 54),
                Size = new Size(width - 184, 28),
                Text = FormatPartyHeroSummary(hero)
            };
            var xpLabel = new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(55, 80, 120),
                Location = new Point(160, 84),
                Size = new Size(width - 184, 28),
                Text = hero.XpTotal is null
                    ? "XP Total: hidden"
                    : $"XP Total: {hero.XpTotal.Value:N0}"
            };
            var sheetTextBox = new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.5f, FontStyle.Regular),
                Location = new Point(160, 116),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Size = new Size(width - 184, 154),
                Text = hero.CharacterSheetText
            };

            heroPanel.Controls.Add(imageBox);
            heroPanel.Controls.Add(nameLabel);
            heroPanel.Controls.Add(summaryLabel);
            heroPanel.Controls.Add(xpLabel);
            heroPanel.Controls.Add(sheetTextBox);
            return heroPanel;
        }

        private static string FormatPartyHeroSummary(PartyHeroSheet hero)
        {
            var values = new[]
            {
                string.IsNullOrWhiteSpace(hero.Level) ? null : $"Level {hero.Level}",
                string.IsNullOrWhiteSpace(hero.CharacterClass) ? null : hero.CharacterClass,
                string.IsNullOrWhiteSpace(hero.HitPoints) ? null : $"HP {hero.HitPoints}"
            };
            var summary = string.Join("   ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
            return summary.Length == 0 ? "Level, class, and hit points unavailable" : summary;
        }

        private static string GetLoginInfoPath()
        {
            return Path.Combine(
                GetReleaseDirectory(),
                PostsDirectoryName,
                OutOfCharacterPostsDirectoryName,
                TheCastLoginInfoFileName);
        }

        private static string GetTheCastHtmlPath()
        {
            return Path.Combine(
                GetReleaseDirectory(),
                PostsDirectoryName,
                OutOfCharacterPostsDirectoryName,
                "the-cast.html");
        }

        private static string GetDiceRollsHtmlPath()
        {
            return Path.Combine(
                GetReleaseDirectory(),
                PostsDirectoryName,
                OutOfCharacterPostsDirectoryName,
                DiceRollsHtmlFileName);
        }

        private static bool HasDiceRollEntries(string diceRollsPath)
        {
            return TryLoadDiceRollEntries(diceRollsPath, out var entries) && entries.Length > 0;
        }

        private static bool TryLoadDiceRollEntries(string diceRollsPath, out DieRollEntry[] entries)
        {
            entries = [];
            if (!RuntimeArtifactUtility.TryReadText(
                    diceRollsPath,
                    "dice rolls cache load",
                    out var html))
            {
                return false;
            }

            try
            {
                entries = GameForumUtility.ExtractDieRollEntries(html);
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                RuntimeArtifactUtility.QuarantineAndLog(diceRollsPath, "dice rolls cache parse", ex);
                return false;
            }
        }

        private static TheCastLoginInfo[] LoadLoginInfoJson(string loginInfoPath)
        {
            return RuntimeArtifactUtility.TryLoadJson<TheCastLoginInfo[]>(
                loginInfoPath,
                "login info cache load",
                out var loginInfoRows)
                    ? loginInfoRows ?? []
                    : [];
        }

        private static TheCastLoginInfo[] LoadTheCastLoginInfoFromHtml(string theCastHtmlPath)
        {
            if (!RuntimeArtifactUtility.TryReadText(
                    theCastHtmlPath,
                    "the cast cache load",
                    out var html))
            {
                return [];
            }

            try
            {
                return GameForumUtility.GetTheCastLoginInfoFromHtml(html);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                RuntimeArtifactUtility.QuarantineAndLog(theCastHtmlPath, "the cast cache parse", ex);
                return [];
            }
        }

        private static async Task<TheCastLoginInfo[]> LoadLoginInfoJsonAsync(
            string loginInfoPath,
            CancellationToken cancellationToken = default)
        {
            return await RuntimeArtifactUtility.LoadJsonOrDefaultAsync<TheCastLoginInfo[]>(
                loginInfoPath,
                "login info cache load",
                cancellationToken).ConfigureAwait(false)
                    ?? [];
        }

        private void ClearDisplaySurfaceForLoginInfo()
        {
            ClearMainDisplaySurface();
            _regionalMapActive = false;
            _regionalMapImage?.Dispose();
            _regionalMapImage = null;
            HideRegionalMapPanel();
            UpdateRegionalMapMenuItem();
        }

        private void ClearDisplaySurfaceForRegionalMap()
        {
            ClearMainDisplaySurface();
            _showLoginInfo = false;
            _showPostTotals = false;
            _showXpTotal = false;
            _showParty = false;
            _showMyHeroBriefing = false;
            _xpTotals = [];
            _xpDateLabel = string.Empty;
            _partyHeroes = [];
            _adventureOutlineMarkdown = string.Empty;
        }

        private void ClearMainDisplaySurface()
        {
            StopHeroImageShowcase();
            ClearHeroImagePictureBox();
            DisposeDiceRollsListBox();
            DisposeAdventureOutlineTextBox();
            DisposeMyHeroBriefingTextBox();
            DisposePartyPanel();
            _showWelcomeText = false;
            _showHeroIntroText = false;
            _showAttributionText = false;
            _showXpTotal = false;
            _showParty = false;
            _showMyHeroBriefing = false;
            _xpTotals = [];
            _xpDateLabel = string.Empty;
            _partyHeroes = [];
            _adventureOutlineMarkdown = string.Empty;
            _regionalMapActive = false;
            _regionalMapImage?.Dispose();
            _regionalMapImage = null;
            HideRegionalMapPanel();

            _welcomeTimer?.Stop();
            _welcomeTimer?.Dispose();
            _welcomeTimer = null;
            _attributionTimer?.Stop();
            _attributionTimer?.Dispose();
            _attributionTimer = null;

            SetBackgroundImage(null, ImageLayout.None);
            BackColor = Color.White;
            menuStrip.BringToFront();
            statusStrip.BringToFront();
        }

        private void ClearDiceRollsDisplayIfVisible()
        {
            if (_diceRollsListBox is null)
            {
                return;
            }

            ClearDisplaySurfaceForRegionalMap();
            _postTotalsSummary = null;
            Invalidate();
            Update();
        }

        private void ShowDiceRollEntries(IReadOnlyCollection<DieRollEntry> entries)
        {
            DisposeDiceRollsListBox();

            var listBox = new ListBox
            {
                HorizontalScrollbar = true,
                IntegralHeight = false
            };
            listBox.Items.AddRange(entries.Select(entry => entry.Line).Cast<object>().ToArray());

            _diceRollsListBox = listBox;
            Controls.Add(listBox);
            UpdateDiceRollsListBoxBounds();
            listBox.BringToFront();
            menuStrip.BringToFront();
            statusStrip.BringToFront();
            SetStatusBarMessage($"Dice rolls: {listBox.Items.Count} loaded.");
        }

        private void UpdateDiceRollsListBoxBounds()
        {
            if (_diceRollsListBox is null)
            {
                return;
            }

            _diceRollsListBox.Bounds = new Rectangle(
                10,
                menuStrip.Bottom + 10,
                Math.Max(0, ClientSize.Width - 20),
                Math.Max(0, statusStrip.Top - menuStrip.Bottom - 20));
        }

        private void UpdateAdventureOutlineTextBoxBounds()
        {
            if (_adventureOutlineTextBox is null)
            {
                return;
            }

            _adventureOutlineTextBox.Bounds = new Rectangle(
                24,
                menuStrip.Bottom + 18,
                Math.Max(0, ClientSize.Width - 48),
                Math.Max(0, statusStrip.Top - menuStrip.Bottom - 36));
        }

        private void UpdateMyHeroBriefingTextBoxBounds()
        {
            if (_myHeroBriefingTextBox is null)
            {
                return;
            }

            _myHeroBriefingTextBox.Bounds = new Rectangle(
                24,
                menuStrip.Bottom + 18,
                Math.Max(0, ClientSize.Width - 48),
                Math.Max(0, statusStrip.Top - menuStrip.Bottom - 36));
        }

        private void UpdatePartyPanelBounds()
        {
            if (_partyPanel is null)
            {
                return;
            }

            _partyPanel.Bounds = new Rectangle(
                10,
                menuStrip.Bottom + 10,
                Math.Max(0, ClientSize.Width - 20),
                Math.Max(0, statusStrip.Top - menuStrip.Bottom - 20));
        }

        private void DisposeDiceRollsListBox()
        {
            if (_diceRollsListBox is null)
            {
                return;
            }

            Controls.Remove(_diceRollsListBox);
            _diceRollsListBox.Dispose();
            _diceRollsListBox = null;
        }

        private void DisposeAdventureOutlineTextBox()
        {
            if (_adventureOutlineTextBox is null)
            {
                return;
            }

            Controls.Remove(_adventureOutlineTextBox);
            _adventureOutlineTextBox.Dispose();
            _adventureOutlineTextBox = null;
        }

        private void DisposeMyHeroBriefingTextBox()
        {
            if (_myHeroBriefingTextBox is null)
            {
                return;
            }

            Controls.Remove(_myHeroBriefingTextBox);
            _myHeroBriefingTextBox.Dispose();
            _myHeroBriefingTextBox = null;
        }

        private void DisposePartyPanel()
        {
            if (_partyPanel is null)
            {
                return;
            }

            foreach (var pictureBox in _partyPanel.Controls
                .OfType<Control>()
                .SelectMany(GetSelfAndDescendants)
                .OfType<PictureBox>())
            {
                pictureBox.Image?.Dispose();
                pictureBox.Image = null;
            }

            Controls.Remove(_partyPanel);
            _partyPanel.Dispose();
            _partyPanel = null;
        }

        private static IEnumerable<Control> GetSelfAndDescendants(Control control)
        {
            yield return control;
            foreach (Control child in control.Controls)
            {
                foreach (var descendant in GetSelfAndDescendants(child))
                {
                    yield return descendant;
                }
            }
        }

        private async Task<bool> LoadGameForumChapterPrefixesAsync(CancellationToken cancellationToken = default)
        {
            GameForumChapterDownload[] chapterDownloads = [];
            GameForumPostDownload[] asideDownloads = [];
            GameForumPostDownload[] allOutOfCharacterDownloads = [];

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetStatusBarMessage("Reading game forum links...");

                var hyperlinks = await HtmlUtility.GetRpolGameHyperlinksAsync(cancellationToken);
                var icPostsDirectory = Path.Combine(GetReleaseDirectory(), PostsDirectoryName, InCharacterPostsDirectoryName);
                chapterDownloads = await TryDownloadChaptersAsync(hyperlinks, icPostsDirectory, cancellationToken);
                var asidePostsDirectory = Path.Combine(icPostsDirectory, AsidePostsDirectoryName);
                asideDownloads = await TryDownloadAsidesAsync(hyperlinks, asidePostsDirectory, cancellationToken);

                var oocPostsDirectory = Path.Combine(GetReleaseDirectory(), PostsDirectoryName, OutOfCharacterPostsDirectoryName);
                allOutOfCharacterDownloads = await TryDownloadOutOfCharacterAsync(hyperlinks, oocPostsDirectory, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await ReportOperationFailureAsync(
                    "game forum startup",
                    "Game forum links unavailable",
                    "Game Forum Error",
                    ex,
                    showDialog: false);
                return false;
            }

            SetStatusBarMessage(
                $"Game forum links: {FormatGameForumDownloadStatus("IC", chapterDownloads)}; {FormatGameForumDownloadStatus("Aside", asideDownloads)}; {FormatGameForumDownloadStatus("OOC", allOutOfCharacterDownloads)}.");
            UpdateShowMenuItemsForHeroImageShowcase();
            return true;
        }

        private static string FormatGameForumDownloadStatus<TDownload>(string label, IReadOnlyCollection<TDownload> downloads)
            where TDownload : notnull
        {
            var refreshedCount = downloads.Count(download => GetGameForumDownloadStatus(download).Downloaded);
            var failedCount = downloads.Count(download => !string.IsNullOrWhiteSpace(GetGameForumDownloadStatus(download).ErrorMessage));
            var currentCount = downloads.Count - refreshedCount - failedCount;

            return $"{label}: {downloads.Count} links, {refreshedCount} refreshed, {currentCount} already current, {failedCount} failed";
        }

        private static (bool Downloaded, string? ErrorMessage) GetGameForumDownloadStatus<TDownload>(TDownload download)
        {
            return download switch
            {
                GameForumChapterDownload chapterDownload => (chapterDownload.Downloaded, chapterDownload.ErrorMessage),
                GameForumPostDownload postDownload => (postDownload.Downloaded, postDownload.ErrorMessage),
                _ => throw new ArgumentException("Unsupported game forum download result.", nameof(download))
            };
        }

        private async Task<GameForumChapterDownload[]> TryDownloadChaptersAsync(
            Hyperlink[] hyperlinks,
            string icPostsDirectory,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var chapterDownloads = await GameForumUtility.DownloadChapterHtmlAsync(
                    hyperlinks,
                    icPostsDirectory,
                    cancellationToken);
                var chapterPrefixesPath = Path.Combine(AppContext.BaseDirectory, GameForumChapterPrefixesFileName);
                await AtomicFileUtility.WriteAllLinesAsync(
                    chapterPrefixesPath,
                    chapterDownloads.Select(download => download.Prefix),
                    cancellationToken);

                var chapterDownloadsPath = Path.Combine(AppContext.BaseDirectory, GameForumChapterDownloadsFileName);
                await WriteDownloadManifestAsync(
                    chapterDownloadsPath,
                    chapterDownloads.Select(download =>
                        $"{download.Prefix}\t{GetManifestStatus(download.Downloaded, download.ErrorMessage)}\t{download.FilePath}\t{download.ErrorMessage}"),
                    cancellationToken);

                await TryBuildAdventureOutlineAsync(icPostsDirectory, cancellationToken);

                return chapterDownloads;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await AppendStartupErrorLogAsync("chapter downloads", ex);
                return [];
            }
        }

        private static async Task TryBuildAdventureOutlineAsync(
            string icPostsDirectory,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var outlinePath = Path.Combine(GetReleaseDirectory(), AdventureOutlineUtility.FileName);
                await AdventureOutlineUtility.UpdateAdventureOutlineAsync(
                    icPostsDirectory,
                    outlinePath,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await AppendStartupErrorLogAsync("adventure outline", ex);
            }
        }

        private async Task<GameForumPostDownload[]> TryDownloadAsidesAsync(
            Hyperlink[] hyperlinks,
            string asidePostsDirectory,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var asideDownloads = await GameForumUtility.DownloadAsideHtmlAsync(
                    hyperlinks,
                    asidePostsDirectory,
                    cancellationToken);
                var asideDownloadsPath = Path.Combine(AppContext.BaseDirectory, GameForumAsideDownloadsFileName);
                await WriteDownloadManifestAsync(
                    asideDownloadsPath,
                    asideDownloads.Select(download =>
                        $"{download.LinkText}\t{GetManifestStatus(download.Downloaded, download.ErrorMessage)}\t{download.FilePath}\t{download.ErrorMessage}"),
                    cancellationToken);

                return asideDownloads;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await AppendStartupErrorLogAsync("aside downloads", ex);
                return [];
            }
        }

        private async Task<GameForumPostDownload[]> TryDownloadOutOfCharacterAsync(
            Hyperlink[] hyperlinks,
            string oocPostsDirectory,
            CancellationToken cancellationToken = default)
        {
            var manifestPath = Path.Combine(AppContext.BaseDirectory, GameForumOutOfCharacterDownloadsFileName);
            var allDownloads = new List<GameForumPostDownload>();

            try
            {
                var outOfCharacterDownloads = await GameForumUtility.DownloadOutOfCharacterHtmlAsync(
                    hyperlinks,
                    oocPostsDirectory,
                    cancellationToken);
                allDownloads.AddRange(outOfCharacterDownloads);
                await WriteOutOfCharacterDownloadsManifestAsync(manifestPath, allDownloads, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await AppendStartupErrorLogAsync("ooc thread downloads", ex);
            }

            try
            {
                var houseRulesDownload = await GameForumUtility.DownloadHouseRulesHtmlAsync(
                    hyperlinks,
                    oocPostsDirectory,
                    cancellationToken);
                if (houseRulesDownload is not null)
                {
                    allDownloads.Add(houseRulesDownload);
                    await WriteOutOfCharacterDownloadsManifestAsync(manifestPath, allDownloads, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await AppendStartupErrorLogAsync("house rules download", ex);
            }

            try
            {
                var gameIntroDownload = await GameForumUtility.DownloadGameIntroHtmlAsync(
                    AppSettingsUtility.GameIntroUrl,
                    oocPostsDirectory,
                    cancellationToken);
                allDownloads.Add(gameIntroDownload);
                await WriteOutOfCharacterDownloadsManifestAsync(manifestPath, allDownloads, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await AppendStartupErrorLogAsync("game intro download", ex);
            }

            GameForumPostDownload? theCastDownload = null;
            try
            {
                theCastDownload = await GameForumUtility.DownloadTheCastHtmlAsync(
                    AppSettingsUtility.TheCastUrl,
                    oocPostsDirectory,
                    cancellationToken);
                allDownloads.Add(theCastDownload);
                await WriteOutOfCharacterDownloadsManifestAsync(manifestPath, allDownloads, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await AppendStartupErrorLogAsync("the cast download", ex);
            }

            try
            {
                var dieRollsDownload = await GameForumUtility.DownloadDieRollsHtmlAsync(
                    hyperlinks,
                    oocPostsDirectory,
                    cancellationToken);
                allDownloads.Add(dieRollsDownload);
                await WriteOutOfCharacterDownloadsManifestAsync(manifestPath, allDownloads, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await AppendStartupErrorLogAsync("die rolls download", ex);
            }

            if (theCastDownload is not null &&
                string.IsNullOrWhiteSpace(theCastDownload.ErrorMessage) &&
                File.Exists(theCastDownload.FilePath))
            {
                try
                {
                    var theCastLoginInfoPath = Path.Combine(oocPostsDirectory, TheCastLoginInfoFileName);
                    await GameForumUtility.WriteTheCastLoginInfoJsonAsync(
                        theCastDownload.FilePath,
                        theCastLoginInfoPath,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await AppendStartupErrorLogAsync("the cast login info export", ex);
                }
            }

            await WriteOutOfCharacterDownloadsManifestAsync(manifestPath, allDownloads, cancellationToken);
            return allDownloads.ToArray();
        }

        private static async Task WriteOutOfCharacterDownloadsManifestAsync(
            string manifestPath,
            IReadOnlyCollection<GameForumPostDownload> downloads,
            CancellationToken cancellationToken = default)
        {
            await WriteDownloadManifestAsync(
                manifestPath,
                downloads.Select(download =>
                    $"{download.LinkText}\t{GetManifestStatus(download.Downloaded, download.ErrorMessage)}\t{download.FilePath}\t{download.ErrorMessage}"),
                cancellationToken);
        }

        private static Task WriteDownloadManifestAsync(
            string outputPath,
            IEnumerable<string> lines,
            CancellationToken cancellationToken = default)
        {
            return AtomicFileUtility.WriteAllLinesAsync(outputPath, lines, cancellationToken);
        }

        internal static string GetManifestStatus(bool downloaded, string? errorMessage)
        {
            return downloaded
                ? "downloaded"
                : string.IsNullOrWhiteSpace(errorMessage)
                    ? "skipped"
                    : "failed";
        }

        internal static string FormatStartupErrorLogEntry(string phase, Exception ex)
        {
            return StartupLoggingUtility.FormatLogEntry(phase, ex);
        }

        private static Task AppendStartupErrorLogAsync(string phase, Exception ex)
        {
            return StartupLoggingUtility.AppendAsync(phase, ex);
        }

        private Task ReportOperationFailureAsync(
            string phase,
            string statusPrefix,
            string dialogTitle,
            Exception ex,
            bool showDialog)
        {
            return UiOperationFailureReporter.ReportAsync(
                new UiOperationFailure(phase, statusPrefix, dialogTitle, ex, showDialog),
                message => SetStatusBarMessage(message),
                _showWarningDialog);
        }

        private void ShowWarningDialog(string title, string message)
        {
            MessageBox.Show(
                this,
                message,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private async Task UpdatePlayerCharacterListingAsync(
            bool showFailureDialog,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pcsDirectory = EnsurePlayerCharacterDirectories();
                var cachedListingMarkdownPath = PlayerCharacterAssetUtility.GetPlayerCharactersListingMarkdownCachePath(pcsDirectory);

                _playerCharacterListingHtml = await HtmlUtility.GetHtmlFromUrlAsync(
                    PlayerCharactersListingUrl,
                    cancellationToken);
                _playerCharacterListingMarkdown = await MarkdownUtility.GetMarkdownFromUrlAsync(
                    PlayerCharactersListingUrl,
                    cancellationToken);

                if (IsMarkdownFetchFailure(_playerCharacterListingMarkdown))
                {
                    throw new InvalidOperationException($"Markdown could not be fetched from {PlayerCharactersListingUrl}.");
                }

                await AtomicFileUtility.WriteAllTextAsync(
                    cachedListingMarkdownPath,
                    _playerCharacterListingMarkdown,
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                _playerCharacterImageUris = MarkdownUtility.GetImageUrisFromMarkdown(_playerCharacterListingMarkdown, PlayerCharactersListingUrl);
                _playerCharacterImageFileNames = MarkdownUtility.GetImageFileNamesFromMarkdown(_playerCharacterListingMarkdown);
                _playerCharacterHtmlImageUris = HtmlUtility.GetImageUrisFromHtml(_playerCharacterListingHtml, PlayerCharactersListingUrl);
                var imagePathsByFileName = await ObsidianPublishUtility.GetPublishedAssetUrlsByFileNameAsync(
                    PlayerCharactersListingUrl,
                    cancellationToken);
                _playerCharacterResolvedImagePaths = _playerCharacterImageFileNames
                    .Select(fileName => imagePathsByFileName.TryGetValue(fileName, out var imagePath)
                        ? $"{fileName}: {imagePath}"
                        : $"{fileName}: (not found)")
                    .ToArray();

                var downloadMarkerPath = Path.Combine(pcsDirectory, ActiveHeroImageDownloadMarkerFileName);

                if (ShouldDownloadActiveHeroImages(downloadMarkerPath))
                {
                    await PlayerCharacterAssetUtility.DownloadActiveHeroImagesAsync(
                        PlayerCharactersListingUrl,
                        pcsDirectory,
                        imagePathsByFileName,
                        cancellationToken);
                    await AtomicFileUtility.WriteAllTextAsync(
                        downloadMarkerPath,
                        DateTimeOffset.Now.ToString("O"),
                        cancellationToken);
                    _activePlayerCharacterImagePaths = PlayerCharacterAssetUtility.GetListedActiveHeroImagePaths(
                        _playerCharacterListingMarkdown,
                        pcsDirectory);
                    UpdateActiveHeroImageStatus(_activePlayerCharacterImagePaths, refreshed: true);
                }
                else
                {
                    _activePlayerCharacterImagePaths = PlayerCharacterAssetUtility.GetListedActiveHeroImagePaths(
                        _playerCharacterListingMarkdown,
                        pcsDirectory);
                    UpdateActiveHeroImageStatus(_activePlayerCharacterImagePaths, refreshed: false);
                }

                await PlayerCharacterAssetUtility.DownloadActiveHeroMarkdownAsync(
                    _playerCharacterListingMarkdown,
                    PlayerCharactersListingUrl,
                    pcsDirectory,
                    cancellationToken);
                await DownloadSitemapAsync(cancellationToken);
                await DownloadRegionalMapAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                StartHeroImageShowcaseIfReady();

            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await ReportOperationFailureAsync(
                    "player character refresh",
                    "Player character refresh unavailable",
                    "Player Character Image URI Error",
                    ex,
                    showFailureDialog);
            }
        }

        private async Task DownloadSitemapAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sitemapPath = Path.Combine(AppContext.BaseDirectory, SitemapFileName);
                var tempDirectory = Path.Combine(AppContext.BaseDirectory, TempDirectoryName);
                var tempSitemapPath = Path.Combine(tempDirectory, SitemapFileName);
                var keywordUrlsPath = Path.Combine(AppContext.BaseDirectory, SitemapKeywordUrlsFileName);

                Directory.CreateDirectory(tempDirectory);
                await SitemapUtility.DownloadSitemapAsync(SitemapUrl, tempSitemapPath, cancellationToken);

                var updated = await PromoteFileIfChangedAsync(tempSitemapPath, sitemapPath, cancellationToken);

                var sitemapIndex = await SitemapUtility.WriteKeywordUrlDictionaryAsync(
                    sitemapPath,
                    keywordUrlsPath,
                    cancellationToken);
                var sitemapStatus = updated
                    ? $"updated {SitemapFileName}; {sitemapIndex.NodeCount} nodes"
                    : $"Using cached {SitemapFileName}";

                SetStatusBarMessage(
                    $"{sitemapStatus}; indexed {sitemapIndex.KeywordCount} sitemap URLs.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetStatusBarMessage($"Sitemap unavailable: {ex.Message}");
            }
        }

        private async Task DownloadRegionalMapAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mapsDirectory = EnsureMapsDirectory();
            var download = await GameForumUtility.DownloadRegionalMapAsync(
                AppSettingsUtility.GameForumUrl,
                mapsDirectory,
                cancellationToken);
            await PreloadRegionalMapImageAsync(cancellationToken);

            var downloadSummary = GetStartupDownloadSummary();
            SetStatusBarMessage(download.ErrorMessage is null
                ? downloadSummary
                : $"{downloadSummary}; regional map unavailable: {download.ErrorMessage}");
            UpdateRegionalMapMenuItem();
        }

        private static Task<bool> PromoteFileIfChangedAsync(
            string tempFilePath,
            string destinationFilePath,
            CancellationToken cancellationToken = default)
        {
            return AtomicFileUtility.PromoteTempFileIfChangedAsync(
                tempFilePath,
                destinationFilePath,
                cancellationToken);
        }

        private static string EnsureMapsDirectory()
        {
            var imagesDirectory = Path.Combine(GetReleaseDirectory(), ImagesDirectoryName);
            var mapsDirectory = Path.Combine(imagesDirectory, MapsDirectoryName);

            Directory.CreateDirectory(imagesDirectory);
            Directory.CreateDirectory(mapsDirectory);

            return mapsDirectory;
        }

        private static string GetRegionalMapPath()
        {
            return Path.Combine(GetReleaseDirectory(), ImagesDirectoryName, MapsDirectoryName, RegionalMapFileName);
        }

        private static string GetAdventureOutlinePath()
        {
            return Path.Combine(GetReleaseDirectory(), AdventureOutlineUtility.FileName);
        }

        private void UpdateRegionalMapMenuItem()
        {
            regionalMapToolStripMenuItem.Enabled = !_heroImageIntroStarted
                && !_heroImageShowcaseStarted
                && !_regionalMapActive
                && File.Exists(GetRegionalMapPath());
        }

        private void UpdateShowMenuItemsForHeroImageShowcase()
        {
            var showMenuItemsEnabled = !_heroImageIntroStarted && !_heroImageShowcaseStarted;
            loginInfoToolStripMenuItem.Enabled = showMenuItemsEnabled && !_showLoginInfo;
            showPostTotalsToolStripMenuItem.Enabled = showMenuItemsEnabled && !_showPostTotals;
            showDiceRollsToolStripMenuItem.Enabled = showMenuItemsEnabled
                && _diceRollsListBox is null
                && HasDiceRollEntries(GetDiceRollsHtmlPath());
            xpToolStripMenuItem.Enabled = showMenuItemsEnabled && !_showXpTotal;
            partyToolStripMenuItem.Enabled = showMenuItemsEnabled && !_showParty;
            myHeroBriefingToolStripMenuItem.Enabled = showMenuItemsEnabled && !_showMyHeroBriefing;
            adventureOutlineToolStripMenuItem.Enabled = showMenuItemsEnabled && _adventureOutlineTextBox is null;
            UpdateRegionalMapMenuItem();
        }

        private void EnableLoginInfoMenuItem()
        {
            if (_heroImageIntroStarted || _heroImageShowcaseStarted)
            {
                return;
            }

            loginInfoToolStripMenuItem.Enabled = true;
        }

        private void EnableShowPostTotalsMenuItem()
        {
            if (_heroImageIntroStarted || _heroImageShowcaseStarted)
            {
                return;
            }

            showPostTotalsToolStripMenuItem.Enabled = true;
        }

        private void EnableShowDiceRollsMenuItem()
        {
            if (_heroImageIntroStarted || _heroImageShowcaseStarted)
            {
                return;
            }

            showDiceRollsToolStripMenuItem.Enabled = _diceRollsListBox is null && HasDiceRollEntries(GetDiceRollsHtmlPath());
        }

        private void EnableXpMenuItem()
        {
            if (_heroImageIntroStarted || _heroImageShowcaseStarted)
            {
                return;
            }

            xpToolStripMenuItem.Enabled = true;
        }

        private void EnablePartyMenuItem()
        {
            if (_heroImageIntroStarted || _heroImageShowcaseStarted)
            {
                return;
            }

            partyToolStripMenuItem.Enabled = true;
        }

        private void EnableMyHeroBriefingMenuItem()
        {
            if (_heroImageIntroStarted || _heroImageShowcaseStarted)
            {
                return;
            }

            myHeroBriefingToolStripMenuItem.Enabled = !_showMyHeroBriefing;
        }

        private void EnableAdventureOutlineMenuItem()
        {
            if (_heroImageIntroStarted || _heroImageShowcaseStarted)
            {
                return;
            }

            adventureOutlineToolStripMenuItem.Enabled = _adventureOutlineTextBox is null;
        }

        private async Task PreloadRegionalMapImageAsync(CancellationToken cancellationToken = default)
        {
            var preloadTask = _regionalMapImagePreloadTask;
            if (preloadTask is not null && !preloadTask.IsCompleted)
            {
                await preloadTask;
                return;
            }

            _regionalMapImagePreloadTask = PreloadRegionalMapImageCoreAsync(cancellationToken);
            await _regionalMapImagePreloadTask;
        }

        private async Task PreloadRegionalMapImageCoreAsync(CancellationToken cancellationToken = default)
        {
            var regionalMapPath = GetRegionalMapPath();
            if (!File.Exists(regionalMapPath))
            {
                return;
            }

            var lastWriteUtc = File.GetLastWriteTimeUtc(regionalMapPath);
            if (_regionalMapImageCache is not null
                && string.Equals(_regionalMapImageCachePath, regionalMapPath, StringComparison.OrdinalIgnoreCase)
                && _regionalMapImageCacheLastWriteUtc == lastWriteUtc)
            {
                return;
            }

            var image = await Task.Run(() => LoadImageCopy(regionalMapPath), cancellationToken);
            var previousImage = _regionalMapImageCache;
            _regionalMapImageCache = image;
            _regionalMapImageCachePath = regionalMapPath;
            _regionalMapImageCacheLastWriteUtc = lastWriteUtc;
            previousImage?.Dispose();
            UpdateRegionalMapMenuItem();
        }

        private Image? TryCreateRegionalMapDisplayImage(string regionalMapPath)
        {
            if (_regionalMapImageCache is null
                || !string.Equals(_regionalMapImageCachePath, regionalMapPath, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(regionalMapPath)
                || _regionalMapImageCacheLastWriteUtc != File.GetLastWriteTimeUtc(regionalMapPath))
            {
                return null;
            }

            return new Bitmap(_regionalMapImageCache);
        }

        private static string EnsurePlayerCharacterDirectories()
        {
            var pcsDirectory = Path.Combine(GetReleaseDirectory(), PlayerCharactersDirectoryName);

            Directory.CreateDirectory(pcsDirectory);
            Directory.CreateDirectory(Path.Combine(pcsDirectory, ActivePlayerCharactersDirectoryName));
            Directory.CreateDirectory(Path.Combine(pcsDirectory, InactivePlayerCharactersDirectoryName));

            return pcsDirectory;
        }

        private static string GetReleaseDirectory()
        {
            return RuntimePathUtility.WritableRuntimeDirectory;
        }

        private void InitializeCachedActiveHeroImages()
        {
            var pcsDirectory = EnsurePlayerCharacterDirectories();
            var cachedListingMarkdownPath = PlayerCharacterAssetUtility.GetPlayerCharactersListingMarkdownCachePath(pcsDirectory);
            if (File.Exists(cachedListingMarkdownPath))
            {
                _playerCharacterListingMarkdown = File.ReadAllText(cachedListingMarkdownPath);
                _activePlayerCharacterImagePaths = PlayerCharacterAssetUtility.GetListedActiveHeroImagePaths(
                    _playerCharacterListingMarkdown,
                    pcsDirectory);
            }
            else
            {
                _activePlayerCharacterImagePaths = [];
            }

            if (_activePlayerCharacterImagePaths.Length > 0)
            {
                UpdateActiveHeroImageStatus(_activePlayerCharacterImagePaths, refreshed: false);
                StartHeroImageShowcaseIfReady();
            }
        }

        private static bool IsMarkdownFetchFailure(string markdown)
        {
            return markdown.StartsWith(MarkdownUtility.InvalidUrlMessage, StringComparison.Ordinal)
                || markdown.StartsWith(MarkdownUtility.UnresolvedUrlMessage, StringComparison.Ordinal);
        }

        private static bool ShouldDownloadActiveHeroImages(string markerPath)
        {
            return !File.Exists(markerPath)
                || DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(markerPath) >= ActiveHeroImageDownloadInterval;
        }

        private void UpdateActiveHeroImageStatus(string[] imagePaths, bool refreshed)
        {
            var totalBytes = imagePaths
                .Where(File.Exists)
                .Select(path => new FileInfo(path).Length)
                .Sum();

            var verb = refreshed ? "Downloaded" : "Using cached";
            SetStatusBarMessage($"{verb} {imagePaths.Length} hero images ({FormatByteSize(totalBytes)}).");
        }

        private void StartHeroImageShowcase(string[] imagePaths)
        {
            StopHeroImageShowcase();
            _heroImageShowcaseStarted = true;
            UpdateShowMenuItemsForHeroImageShowcase();

            _heroImageShowcasePaths.AddRange(imagePaths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(_ => _random.Next()));
            _heroImageShowcaseTotal = _heroImageShowcasePaths.Count;
            _heroImageShowcaseIndex = 0;
            _heroImageShowcaseSkipped = 0;
            _lastHeroImageSkipReason = string.Empty;

            if (_heroImageShowcasePaths.Count == 0)
            {
                _heroImageShowcaseStarted = false;
                UpdateShowMenuItemsForHeroImageShowcase();
                return;
            }

            _heroImageShowcaseTimer = new System.Windows.Forms.Timer
            {
                Interval = 16
            };
            _heroImageShowcaseTimer.Tick += (_, _) => UpdateHeroImageShowcase();

            ShowNextHeroImage();
        }

        private void StartHeroImageShowcaseIfReady()
        {
            if (_suppressHeroImagesForThisRun)
            {
                return;
            }

            if (_showWelcomeText
                || _showHeroIntroText
                || _regionalMapActive
                || _heroImageIntroStarted
                || _heroImageShowcaseStarted
                || _heroImageShowcaseCompleted
                || _activePlayerCharacterImagePaths.Length == 0)
            {
                return;
            }

            StartBackgroundTask("hero image showcase intro", StartHeroImageShowcaseWithIntroAsync);
        }

        private async Task StartHeroImageShowcaseAfterDelayAsync(CancellationToken cancellationToken = default)
        {
            if (_suppressHeroImagesForThisRun)
            {
                return;
            }

            if (_regionalMapActive
                || _activePlayerCharacterImagePaths.Length == 0
                || _heroImageShowcaseStarted
                || _heroImageShowcaseCompleted)
            {
                return;
            }

            var delayBeforeIntro = HeroImageShowcaseStartDelay - HeroImageIntroDuration;
            if (delayBeforeIntro > TimeSpan.Zero)
            {
                await Task.Delay(delayBeforeIntro, cancellationToken);
            }

            if (_regionalMapActive)
            {
                return;
            }

            await StartHeroImageShowcaseWithIntroAsync(cancellationToken);
        }

        private async Task StartHeroImageShowcaseWithIntroAsync(CancellationToken cancellationToken = default)
        {
            if (_suppressHeroImagesForThisRun)
            {
                return;
            }

            if (_showWelcomeText
                || _showHeroIntroText
                || _regionalMapActive
                || _heroImageIntroStarted
                || _heroImageShowcaseStarted
                || _heroImageShowcaseCompleted
                || _activePlayerCharacterImagePaths.Length == 0)
            {
                return;
            }

            _heroImageIntroStarted = true;
            _showHeroIntroText = true;
            UpdateShowMenuItemsForHeroImageShowcase();
            Invalidate();

            await Task.Delay(HeroImageIntroDuration, cancellationToken);

            _showHeroIntroText = false;
            _heroImageIntroStarted = false;
            UpdateShowMenuItemsForHeroImageShowcase();
            Invalidate();

            if (_showWelcomeText
                || _regionalMapActive
                || _heroImageShowcaseStarted
                || _heroImageShowcaseCompleted
                || _activePlayerCharacterImagePaths.Length == 0)
            {
                return;
            }

            StartHeroImageShowcase(_activePlayerCharacterImagePaths);
        }

        private void StopHeroImageShowcase()
        {
            _heroImageShowcaseTimer?.Stop();
            _heroImageShowcaseTimer?.Dispose();
            _heroImageShowcaseTimer = null;
            _heroImageShowcasePaths.Clear();
            _currentHeroImage?.Dispose();
            _currentHeroImage = null;
            ClearHeroImagePictureBox();
            _currentHeroImageOpacity = 0;
            _currentHeroImageStopwatch.Reset();
            _heroImageIntroStarted = false;
            _heroImageShowcaseStarted = false;
            UpdateShowMenuItemsForHeroImageShowcase();
        }

        private void ShowNextHeroImage()
        {
            _currentHeroImage?.Dispose();
            _currentHeroImage = null;

            while (_heroImageShowcasePaths.Count > 0)
            {
                var imagePath = _heroImageShowcasePaths[^1];
                _heroImageShowcasePaths.RemoveAt(_heroImageShowcasePaths.Count - 1);

                try
                {
                    _currentHeroImage = LoadImageCopy(imagePath);
                    _currentHeroImageBounds = GetCenteredHeroImageBounds(_currentHeroImage);
                    _heroImageShowcaseIndex++;
                    _currentHeroImageOpacity = 0;
                    _currentHeroImageWasVisible = false;
                    _currentHeroImageStopwatch.Restart();
                    UpdateHeroImagePictureBox();
                    _heroImageShowcaseTimer?.Start();
                    return;
                }
                catch (Exception ex)
                {
                    _heroImageShowcaseSkipped++;
                    _lastHeroImageSkipReason = ex.Message;
                    continue;
                }
            }

            StopHeroImageShowcase();
            _heroImageShowcaseCompleted = true;
            StartBackgroundTask(
                "player character refresh",
                cancellationToken => StartPlayerCharacterListingUpdateAsync(
                    showFailureDialog: false,
                    cancellationToken));
            Invalidate();
        }

        private async Task StartPlayerCharacterListingUpdateAsync(
            bool showFailureDialog = true,
            CancellationToken cancellationToken = default)
        {
            if (_playerCharacterListingUpdateStarted
                || _showWelcomeText
                || _showHeroIntroText
                || _regionalMapActive
                || _heroImageShowcaseStarted
                || ShouldDelayPlayerCharacterRefreshForHeroShowcase())
            {
                return;
            }

            _playerCharacterListingUpdateStarted = true;
            try
            {
                await UpdatePlayerCharacterListingAsync(showFailureDialog, cancellationToken);
            }
            finally
            {
                _playerCharacterListingUpdateStarted = false;
            }
        }

        private bool ShouldDelayPlayerCharacterRefreshForHeroShowcase()
        {
            return !_suppressHeroImagesForThisRun
                && _activePlayerCharacterImagePaths.Length > 0
                && !_heroImageShowcaseCompleted;
        }

        private void UpdateHeroImageShowcase()
        {
            var elapsed = _currentHeroImageStopwatch.Elapsed;
            var fadeInEnd = HeroImageFadeInDuration;
            var displayEnd = fadeInEnd + HeroImageDisplayDuration;
            var fadeOutEnd = displayEnd + HeroImageFadeOutDuration;
            var nextImageStart = fadeOutEnd + HeroImageInterImageDelayDuration;

            if (elapsed < fadeInEnd)
            {
                _currentHeroImageOpacity = GetProgress(elapsed, HeroImageFadeInDuration);
                UpdateHeroImagePictureBox();
                return;
            }

            if (elapsed < displayEnd)
            {
                _currentHeroImageOpacity = 1;
                UpdateHeroImagePictureBox();
                return;
            }

            if (elapsed < fadeOutEnd)
            {
                _currentHeroImageOpacity = 1 - GetProgress(elapsed - displayEnd, HeroImageFadeOutDuration);
                UpdateHeroImagePictureBox();
                return;
            }

            if (elapsed < nextImageStart)
            {
                ClearHeroImagePictureBox();
                return;
            }

            if (!_currentHeroImageWasVisible)
            {
                _currentHeroImageOpacity = 1;
                UpdateHeroImagePictureBox();
                return;
            }

            ShowNextHeroImage();
        }

        private Rectangle GetCenteredHeroImageBounds(Image image)
        {
            var displayBounds = GetHeroImageDisplayBounds();
            var targetHeight = displayBounds.Height * 0.45;
            var scale = Math.Min(
                targetHeight / image.Height,
                Math.Min(
                    (double)displayBounds.Width / image.Width,
                    (double)displayBounds.Height / image.Height));
            var width = Math.Max(1, (int)Math.Round(image.Width * scale));
            var height = Math.Max(1, (int)Math.Round(image.Height * scale));
            var x = displayBounds.Left + (displayBounds.Width - width) / 2;
            var y = displayBounds.Top + (displayBounds.Height - height) / 2;

            return new Rectangle(x, y, width, height);
        }

        private Rectangle GetHeroImageDisplayBounds()
        {
            var bounds = new Rectangle(
                ClientRectangle.Left,
                menuStrip.Bottom,
                ClientRectangle.Width,
                statusStrip.Top - menuStrip.Bottom);

            return bounds.Width > 0 && bounds.Height > 0
                ? bounds
                : ClientRectangle;
        }

        private void InitializeRegionalMapPanel()
        {
            _regionalMapPanel = new Panel
            {
                BackColor = Color.White,
                Visible = false
            };
            _regionalMapPanel.Paint += RegionalMapPanel_Paint;
            Controls.Add(_regionalMapPanel);
            UpdateRegionalMapPanelBounds();
            _regionalMapPanel.SendToBack();
            menuStrip.BringToFront();
            statusStrip.BringToFront();
        }

        private void ShowRegionalMapPanel()
        {
            menuStrip.BringToFront();
            statusStrip.BringToFront();
            Invalidate();
            Update();
        }

        private void HideRegionalMapPanel()
        {
            if (_regionalMapPanel is null)
            {
                return;
            }

            _regionalMapPanel.Visible = false;
        }

        private void UpdateRegionalMapPanelBounds()
        {
            if (_regionalMapPanel is null)
            {
                return;
            }

            _regionalMapPanel.Bounds = GetHeroImageDisplayBounds();
            if (_regionalMapPanel.Visible)
            {
                _regionalMapPanel.Invalidate();
            }
        }

        private void RegionalMapPanel_Paint(object? sender, PaintEventArgs e)
        {
            DrawRegionalMap(e.Graphics, _regionalMapPanel?.ClientRectangle ?? Rectangle.Empty);
        }

        private void InitializeHeroImagePictureBox()
        {
            _heroImagePictureBox = new PictureBox
            {
                BackColor = Color.Transparent,
                Enabled = false,
                SizeMode = PictureBoxSizeMode.StretchImage,
                Visible = false
            };
            Controls.Add(_heroImagePictureBox);
            _heroImagePictureBox.BringToFront();
            menuStrip.BringToFront();
            statusStrip.BringToFront();
        }

        private void UpdateHeroImagePictureBox()
        {
            if (_heroImagePictureBox is null || _currentHeroImage is null || _currentHeroImageOpacity <= 0)
            {
                ClearHeroImagePictureBox();
                return;
            }

            if (_currentHeroImageOpacity >= 1)
            {
                _currentHeroImageWasVisible = true;
            }

            var shadowPadding = 8;
            var imageBounds = new Rectangle(
                shadowPadding,
                shadowPadding,
                _currentHeroImageBounds.Width,
                _currentHeroImageBounds.Height);
            var frame = new Bitmap(
                _currentHeroImageBounds.Width + shadowPadding * 2,
                _currentHeroImageBounds.Height + shadowPadding * 2,
                PixelFormat.Format32bppArgb);

            using var graphics = Graphics.FromImage(frame);
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var imageAttributes = new ImageAttributes();
            var colorMatrix = new ColorMatrix
            {
                Matrix00 = 1,
                Matrix11 = 1,
                Matrix22 = 1,
                Matrix33 = Math.Clamp(_currentHeroImageOpacity, 0, 1),
                Matrix44 = 1
            };
            imageAttributes.SetColorMatrix(colorMatrix);
            graphics.DrawImage(
                _currentHeroImage,
                imageBounds,
                0,
                0,
                _currentHeroImage.Width,
                _currentHeroImage.Height,
                GraphicsUnit.Pixel,
                imageAttributes);

            _heroImagePictureBox.Image?.Dispose();
            _heroImagePictureBox.Image = frame;
            _heroImagePictureBox.Bounds = new Rectangle(
                _currentHeroImageBounds.Left - shadowPadding,
                _currentHeroImageBounds.Top - shadowPadding,
                frame.Width,
                frame.Height);
            _heroImagePictureBox.Visible = true;
            _heroImagePictureBox.BringToFront();
            menuStrip.BringToFront();
            statusStrip.BringToFront();
        }

        private void ClearHeroImagePictureBox()
        {
            if (_heroImagePictureBox is null)
            {
                return;
            }

            _heroImagePictureBox.Visible = false;
            _heroImagePictureBox.Image?.Dispose();
            _heroImagePictureBox.Image = null;
        }

        private static Image LoadImageCopy(string imagePath)
        {
            try
            {
                using var image = Image.FromFile(imagePath);
                return new Bitmap(image);
            }
            catch
            {
                try
                {
                    return LoadImageWithSkiaSharp(imagePath);
                }
                catch (Exception skiaException)
                {
                    throw new InvalidOperationException($"Skia fallback failed: {skiaException.Message}", skiaException);
                }
            }
        }

        private static Image LoadImageWithSkiaSharp(string imagePath)
        {
            using var skBitmap = SKBitmap.Decode(imagePath)
                ?? throw new ArgumentException($"Image '{imagePath}' could not be decoded.");
            using var convertedBitmap = new SKBitmap(new SKImageInfo(
                skBitmap.Width,
                skBitmap.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul));

            if (!skBitmap.CopyTo(convertedBitmap, SKColorType.Bgra8888))
            {
                throw new ArgumentException($"Image '{imagePath}' could not be converted.");
            }

            var bitmap = new Bitmap(skBitmap.Width, skBitmap.Height, PixelFormat.Format32bppPArgb);
            var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bitmapData = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, bitmap.PixelFormat);

            try
            {
                var bytes = convertedBitmap.ByteCount;
                var buffer = new byte[bytes];
                Marshal.Copy(convertedBitmap.GetPixels(), buffer, 0, bytes);
                Marshal.Copy(buffer, 0, bitmapData.Scan0, bytes);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }

        private static float GetProgress(TimeSpan elapsed, TimeSpan duration)
        {
            return duration <= TimeSpan.Zero
                ? 1
                : Math.Clamp((float)(elapsed.TotalMilliseconds / duration.TotalMilliseconds), 0, 1);
        }

        private static string FormatByteSize(long bytes)
        {
            string[] units = ["bytes", "KB", "MB", "GB"];
            var size = (double)bytes;
            var unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return unitIndex == 0
                ? $"{bytes} {units[unitIndex]}"
                : $"{size:0.##} {units[unitIndex]}";
        }

        internal static string GetStartupDownloadSummary()
        {
            var bytes = FileDownloadCounters.CompletedDownloadBytes;
            var kilobytes = bytes / 1024;
            var megabytes = kilobytes / 1024;

            return $"Startup downloads complete: {FileDownloadCounters.CompletedDownloadCount} files, {kilobytes:0.##} KB ({megabytes:0.##} MB).";
        }

        private void ShowPlayerCharacterResolvedImagePathsOnce()
        {
            var markerPath = Path.Combine(AppContext.BaseDirectory, IndexImagePathMessageBoxShownFileName);

            if (File.Exists(markerPath))
            {
                return;
            }

            File.WriteAllText(markerPath, DateTimeOffset.Now.ToString("O"));

            var message = _playerCharacterResolvedImagePaths.Length == 0
                ? "(no image file names found)"
                : string.Join(Environment.NewLine, _playerCharacterResolvedImagePaths);

            MessageBox.Show(
                this,
                message,
                "Resolved Player Character Image Paths",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ShowPlayerCharacterImageFileNames()
        {
            var message = _playerCharacterImageFileNames.Length == 0
                ? "(no image file names found)"
                : string.Join(Environment.NewLine, _playerCharacterImageFileNames);

            MessageBox.Show(
                this,
                message,
                "Player Character Image File Names",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ShowPlayerCharacterHtmlImageUrisOnce()
        {
            var markerPath = Path.Combine(AppContext.BaseDirectory, HtmlImageUriMessageBoxShownFileName);

            if (File.Exists(markerPath))
            {
                return;
            }

            File.WriteAllText(markerPath, DateTimeOffset.Now.ToString("O"));

            var message = _playerCharacterHtmlImageUris.Length == 0
                ? "(no scraped image links found)"
                : string.Join(Environment.NewLine, _playerCharacterHtmlImageUris);

            MessageBox.Show(
                this,
                message,
                "Scraped Player Character Image Links",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ShowPlayerCharacterImageUrisOnce()
        {
            var markerPath = Path.Combine(AppContext.BaseDirectory, ImageUriMessageBoxShownFileName);

            if (File.Exists(markerPath))
            {
                return;
            }

            var message = _playerCharacterImageUris.Length == 0
                ? "(no image URIs found)"
                : string.Join(Environment.NewLine, _playerCharacterImageUris);

            MessageBox.Show(
                this,
                message,
                "Player Character Image URIs",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            File.WriteAllText(markerPath, DateTimeOffset.Now.ToString("O"));
        }

        private void FillCurrentScreenWorkingArea()
        {
            var workingArea = Screen.FromControl(this).WorkingArea;

            WindowState = FormWindowState.Normal;
            Bounds = workingArea;
        }

        private void DrawRegionalMap(Graphics graphics, Rectangle contentBounds)
        {
            graphics.Clear(BackColor);

            if (_regionalMapImage is null || contentBounds.Width <= 0 || contentBounds.Height <= 0)
            {
                return;
            }

            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var scale = Math.Min(
                (double)contentBounds.Width / _regionalMapImage.Width,
                (double)contentBounds.Height / _regionalMapImage.Height);
            var width = Math.Max(1, (int)Math.Round(_regionalMapImage.Width * scale));
            var height = Math.Max(1, (int)Math.Round(_regionalMapImage.Height * scale));
            var bounds = new Rectangle(
                contentBounds.Left + (contentBounds.Width - width) / 2,
                contentBounds.Top + (contentBounds.Height - height) / 2,
                width,
                height);

            graphics.DrawImage(_regionalMapImage, bounds);
        }

        private void DrawLoginInfo(Graphics graphics)
        {
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var contentBounds = GetHeroImageDisplayBounds();
            var left = contentBounds.Left + 32;
            var top = contentBounds.Top + 32;
            var width = Math.Max(320, contentBounds.Width - 64);
            var rowHeight = 34;
            var columns = GetLoginInfoColumns(width);

            using var titleFont = new Font("Segoe UI", 22, FontStyle.Bold);
            using var headerFont = new Font("Segoe UI", 10, FontStyle.Bold);
            using var rowFont = new Font("Segoe UI", 10, FontStyle.Bold);
            using var titleBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
            using var textBrush = new SolidBrush(Color.FromArgb(35, 35, 35));
            using var headerTextBrush = new SolidBrush(Color.White);
            using var mutedBrush = new SolidBrush(Color.FromArgb(115, 115, 115));
            using var headerBackBrush = new SolidBrush(Color.Black);
            using var alternateBackBrush = new SolidBrush(Color.FromArgb(248, 249, 251));
            using var linePen = new Pen(Color.FromArgb(215, 218, 224));
            using var textFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            using var rightTextFormat = new StringFormat
            {
                Alignment = StringAlignment.Far,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            graphics.DrawString("Login Info", titleFont, titleBrush, new PointF(left, top));
            top += 48;

            DrawLoginInfoRow(
                graphics,
                columns,
                top,
                rowHeight,
                ["Character Name", "Posts", "Tag", "Last Visited", "Last Post"],
                headerFont,
                headerTextBrush,
                headerBackBrush,
                linePen,
                left,
                textFormat,
                rightTextFormat);
            top += rowHeight;

            for (var index = 0; index < _loginInfoRows.Length; index++)
            {
                var row = _loginInfoRows[index];
                var values = new[]
                {
                    row.CharacterName,
                    row.Posts?.ToString() ?? string.Empty,
                    row.Tag,
                    row.LastVisited ?? string.Empty,
                    row.LastPost
                };

                DrawLoginInfoRow(
                    graphics,
                    columns,
                    top,
                    rowHeight,
                    values,
                    rowFont,
                    row.LastVisited is null ? mutedBrush : textBrush,
                    index % 2 == 0 ? Brushes.White : alternateBackBrush,
                    linePen,
                    left,
                    textFormat,
                    rightTextFormat);
                top += rowHeight;

                if (top + rowHeight > contentBounds.Bottom - 16)
                {
                    break;
                }
            }
        }

        private void DrawPostTotals(Graphics graphics)
        {
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var summary = _postTotalsSummary;
            if (summary is null)
            {
                return;
            }

            var contentBounds = GetHeroImageDisplayBounds();
            var left = contentBounds.Left + 32;
            var top = contentBounds.Top + 32;
            var width = Math.Max(320, contentBounds.Width - 64);
            var rowHeight = 34;
            var columns = GetPostTotalsColumns(width);

            using var titleFont = new Font("Segoe UI", 22, FontStyle.Bold);
            using var noteFont = new Font("Segoe UI", 11, FontStyle.Bold);
            using var headerFont = new Font("Segoe UI", 11, FontStyle.Bold);
            using var rowFont = new Font("Segoe UI", 11, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.FromArgb(35, 35, 35));
            using var mutedBrush = new SolidBrush(Color.FromArgb(105, 105, 105));
            using var headerTextBrush = new SolidBrush(Color.White);
            using var headerBackBrush = new SolidBrush(Color.Black);
            using var alternateBackBrush = new SolidBrush(Color.FromArgb(248, 249, 251));
            using var linePen = new Pen(Color.FromArgb(215, 218, 224));
            using var textFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            using var rightTextFormat = new StringFormat
            {
                Alignment = StringAlignment.Far,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            graphics.DrawString("Post Totals", titleFont, textBrush, new PointF(left, top));
            top += 42;

            var note = "Local file copies only cover a subset of the posts stored on the RPOL game forum.";
            var noteBounds = new RectangleF(left, top, width, 42);
            graphics.DrawString(note, noteFont, mutedBrush, noteBounds);
            top += 54;

            DrawLoginInfoRow(
                graphics,
                columns,
                top,
                rowHeight,
                ["Posting Character", "the-cast.html", "Local HTML Files"],
                headerFont,
                headerTextBrush,
                headerBackBrush,
                linePen,
                left,
                textFormat,
                rightTextFormat,
                rightAlignedColumnIndexes: [1, 2]);
            top += rowHeight;

            for (var index = 0; index < summary.Rows.Count; index++)
            {
                var row = summary.Rows[index];
                var values = new[]
                {
                    row.CharacterName,
                    row.ForumPosts?.ToString() ?? string.Empty,
                    row.LocalPosts.ToString()
                };

                DrawLoginInfoRow(
                    graphics,
                    columns,
                    top,
                    rowHeight,
                    values,
                    rowFont,
                    textBrush,
                    index % 2 == 0 ? Brushes.White : alternateBackBrush,
                    linePen,
                    left,
                    textFormat,
                    rightTextFormat,
                    rightAlignedColumnIndexes: [1, 2]);
                top += rowHeight;

                if (top + rowHeight > contentBounds.Bottom - 16)
                {
                    break;
                }
            }
        }

        private void DrawXpTotal(Graphics graphics)
        {
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (_xpTotals.Count == 0)
            {
                return;
            }

            var contentBounds = GetHeroImageDisplayBounds();
            var left = contentBounds.Left + 32;
            var top = contentBounds.Top + 32;
            var width = Math.Max(320, contentBounds.Width - 64);
            var rowHeight = 38;
            var columns = GetXpTotalColumns(width);

            using var titleFont = new Font("Segoe UI", 22, FontStyle.Bold);
            using var noteFont = new Font("Segoe UI", 11, FontStyle.Bold);
            using var headerFont = new Font("Segoe UI", 11, FontStyle.Bold);
            using var rowFont = new Font("Segoe UI", 12, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.FromArgb(35, 35, 35));
            using var mutedBrush = new SolidBrush(Color.FromArgb(105, 105, 105));
            using var headerTextBrush = new SolidBrush(Color.White);
            using var headerBackBrush = new SolidBrush(Color.Black);
            using var alternateBackBrush = new SolidBrush(Color.FromArgb(248, 249, 251));
            using var linePen = new Pen(Color.FromArgb(215, 218, 224));
            using var textFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            using var rightTextFormat = new StringFormat
            {
                Alignment = StringAlignment.Far,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            graphics.DrawString("XP", titleFont, textBrush, new PointF(left, top));
            top += 42;
            graphics.DrawString(_xpDateLabel, noteFont, mutedBrush, new RectangleF(left, top, width, 32));
            top += 46;

            DrawLoginInfoRow(
                graphics,
                columns,
                top,
                rowHeight,
                ["Character", "XP Total"],
                headerFont,
                headerTextBrush,
                headerBackBrush,
                linePen,
                left,
                textFormat,
                rightTextFormat,
                rightAlignedColumnIndexes: [1]);
            top += rowHeight;

            for (var index = 0; index < _xpTotals.Count; index++)
            {
                var total = _xpTotals[index];
                DrawLoginInfoRow(
                    graphics,
                    columns,
                    top,
                    rowHeight,
                    [total.Name, total.XpTotal.ToString("N0", CultureInfo.InvariantCulture)],
                    rowFont,
                    textBrush,
                    index % 2 == 0 ? Brushes.White : alternateBackBrush,
                    linePen,
                    left,
                    textFormat,
                    rightTextFormat,
                    rightAlignedColumnIndexes: [1]);
                top += rowHeight;

                if (top + rowHeight > contentBounds.Bottom - 16)
                {
                    break;
                }
            }
        }

        private static bool IsDungeonMasterXpAccess(string characterName)
        {
            return string.Equals(characterName, DungeonMasterXpAccessName, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetFirstName(string value)
        {
            var trimmedValue = value.Trim();
            var spaceIndex = trimmedValue.IndexOf(' ');
            return spaceIndex < 0
                ? trimmedValue
                : trimmedValue[..spaceIndex];
        }

        private static RectangleF[] GetLoginInfoColumns(int width)
        {
            var characterWidth = width * 0.32f;
            var postsWidth = width * 0.10f;
            var tagWidth = width * 0.25f;
            var lastVisitedWidth = width * 0.16f;
            var lastPostWidth = width - characterWidth - postsWidth - tagWidth - lastVisitedWidth;
            var x = 0f;

            RectangleF Next(float columnWidth)
            {
                var rectangle = new RectangleF(x, 0, columnWidth, 0);
                x += columnWidth;
                return rectangle;
            }

            return
            [
                Next(characterWidth),
                Next(postsWidth),
                Next(tagWidth),
                Next(lastVisitedWidth),
                Next(lastPostWidth)
            ];
        }

        private static RectangleF[] GetPostTotalsColumns(int width)
        {
            var columnWidth = width / 3f;
            var x = 0f;

            RectangleF Next(float columnWidth)
            {
                var rectangle = new RectangleF(x, 0, columnWidth, 0);
                x += columnWidth;
                return rectangle;
            }

            return
            [
                Next(columnWidth),
                Next(columnWidth),
                Next(width - (columnWidth * 2))
            ];
        }

        private static RectangleF[] GetXpTotalColumns(int width)
        {
            var characterWidth = width * 0.68f;
            return
            [
                new RectangleF(0, 0, characterWidth, 0),
                new RectangleF(characterWidth, 0, width - characterWidth, 0)
            ];
        }

        private static void DrawLoginInfoRow(
            Graphics graphics,
            RectangleF[] columns,
            int top,
            int height,
            string[] values,
            Font font,
            Brush textBrush,
            Brush backBrush,
            Pen linePen,
            int left,
            StringFormat textFormat,
            StringFormat rightTextFormat,
            params int[] rightAlignedColumnIndexes)
        {
            var rowBounds = new RectangleF(left, top, columns.Sum(column => column.Width), height);
            graphics.FillRectangle(backBrush, rowBounds);
            graphics.DrawRectangle(linePen, rowBounds.X, rowBounds.Y, rowBounds.Width, rowBounds.Height);

            for (var index = 0; index < columns.Length; index++)
            {
                var column = columns[index];
                var cellBounds = new RectangleF(
                    rowBounds.Left + column.Left + 8,
                    top,
                    column.Width - 16,
                    height);

                if (index > 0)
                {
                    var separatorX = rowBounds.Left + column.Left;
                    graphics.DrawLine(linePen, separatorX, top, separatorX, top + height);
                }

                graphics.DrawString(
                    values[index],
                    font,
                    textBrush,
                    cellBounds,
                    rightAlignedColumnIndexes.Contains(index) ? rightTextFormat : textFormat);
            }
        }

        private static void DrawOutlinedText(
            Graphics graphics,
            string text,
            FontFamily fontFamily,
            float fontSize,
            Rectangle bounds,
            Color fillColor)
        {
            using var textPath = CreateCenteredTextPath(text, fontFamily, fontSize, bounds);
            using var shadowPath = (GraphicsPath)textPath.Clone();
            using var transform = new Matrix();
            transform.Translate(4, 4);
            shadowPath.Transform(transform);

            using var shadowBrush = new SolidBrush(Color.FromArgb(120, Color.Black));
            using var outlinePen = new Pen(Color.Black, 4) { LineJoin = LineJoin.Round };
            using var textBrush = new SolidBrush(fillColor);

            graphics.FillPath(shadowBrush, shadowPath);
            graphics.DrawPath(outlinePen, textPath);
            graphics.FillPath(textBrush, textPath);
        }

        private static Image LoadBackgroundImage()
        {
            using var image = Image.FromStream(OpenEmbeddedAsset("white-marble.png"));
            return new Bitmap(image);
        }

        private static Image LoadDragonBackgroundImage()
        {
            using var image = Image.FromStream(OpenEmbeddedAsset("dragon-dim.png"));
            return new Bitmap(image);
        }

        private static Icon LoadApplicationIcon()
        {
            using var iconStream = OpenEmbeddedAsset("dragon-icon.ico");
            return new Icon(iconStream);
        }

        private static Stream OpenEmbeddedAsset(string fileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"PlayerAssistant.Assets.{fileName}";

            return assembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' was not found.");
        }

        private static GraphicsPath CreateCenteredTextPath(
            string text,
            FontFamily fontFamily,
            float fontSize,
            Rectangle bounds)
        {
            var path = new GraphicsPath();
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            path.AddString(text, fontFamily, (int)FontStyle.Bold, fontSize, bounds, format);
            return path;
        }
    }
}
