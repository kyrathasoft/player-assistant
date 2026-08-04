using System.Text;

namespace PlayerAssistant
{
    internal sealed class TranslatorPresenter : IDisposable
    {
        private readonly Form _owner;
        private readonly MenuStrip _menuStrip;
        private readonly StatusStrip _statusStrip;
        private readonly ToolStripMenuItem _translatorMenuItem;
        private readonly ToolStripMenuItem _orcishMenuItem;
        private readonly ToolStripMenuItem _elvenMenuItem;
        private readonly ToolStripMenuItem _ghukliakMenuItem;
        private readonly TranslatorController _controller;
        private readonly Action<string> _setStatus;
        private readonly Func<string, string, string, Exception, bool, Task> _reportFailure;
        private readonly Func<bool> _menusAllowed;
        private readonly Func<Func<string, bool, string>?> _translationOverrideProvider;
        private readonly Func<Func<string?>?> _exportPathOverrideProvider;
        private CancellationTokenSource? _translationCancellationSource;
        private int _translationGeneration;
        private int _previousInputLength;
        private bool _waitCursorActive;

        public TranslatorPresenter(
            Form owner,
            MenuStrip menuStrip,
            StatusStrip statusStrip,
            ToolStripMenuItem translatorMenuItem,
            ToolStripMenuItem orcishMenuItem,
            ToolStripMenuItem elvenMenuItem,
            ToolStripMenuItem ghukliakMenuItem,
            TranslatorController controller,
            Action<string> setStatus,
            Func<string, string, string, Exception, bool, Task> reportFailure,
            Func<bool> menusAllowed,
            Func<Func<string, bool, string>?> translationOverrideProvider,
            Func<Func<string?>?> exportPathOverrideProvider)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(menuStrip);
            ArgumentNullException.ThrowIfNull(statusStrip);
            ArgumentNullException.ThrowIfNull(translatorMenuItem);
            ArgumentNullException.ThrowIfNull(orcishMenuItem);
            ArgumentNullException.ThrowIfNull(elvenMenuItem);
            ArgumentNullException.ThrowIfNull(ghukliakMenuItem);
            ArgumentNullException.ThrowIfNull(controller);
            ArgumentNullException.ThrowIfNull(setStatus);
            ArgumentNullException.ThrowIfNull(reportFailure);
            ArgumentNullException.ThrowIfNull(menusAllowed);
            ArgumentNullException.ThrowIfNull(translationOverrideProvider);
            ArgumentNullException.ThrowIfNull(exportPathOverrideProvider);

            _owner = owner;
            _menuStrip = menuStrip;
            _statusStrip = statusStrip;
            _translatorMenuItem = translatorMenuItem;
            _orcishMenuItem = orcishMenuItem;
            _elvenMenuItem = elvenMenuItem;
            _ghukliakMenuItem = ghukliakMenuItem;
            _controller = controller;
            _setStatus = setStatus;
            _reportFailure = reportFailure;
            _menusAllowed = menusAllowed;
            _translationOverrideProvider = translationOverrideProvider;
            _exportPathOverrideProvider = exportPathOverrideProvider;
        }

        public Panel? Panel { get; private set; }

        public Label? HeadingLabel { get; private set; }

        public CheckBox? DirectionCheckBox { get; private set; }

        public Label? InputLabel { get; private set; }

        public TextBox? InputTextBox { get; private set; }

        public Label? OutputLabel { get; private set; }

        public TextBox? OutputTextBox { get; private set; }

        public Button? ExportButton { get; private set; }

        public bool IsVisible => Panel is not null;

        public TranslatorTargetLanguage TargetLanguage => _controller.TargetLanguage;

        public void Show(TranslatorTargetLanguage targetLanguage)
        {
            DisposePanel();
            _controller.SelectTarget(targetLanguage);
            var targetName = _controller.TargetName;

            HeadingLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Name = "lblTranslatorHeading",
                Text = $"English to {targetName}"
            };
            DirectionCheckBox = new CheckBox
            {
                AutoSize = true,
                Name = "chkTranslatorTargetToEnglish",
                Text = $"{targetName} to English",
                UseVisualStyleBackColor = true
            };
            DirectionCheckBox.CheckedChanged += DirectionCheckBox_CheckedChanged;
            InputLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Name = "lblTranslatorInput",
                Text = "English text"
            };
            InputTextBox = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                Font = new Font("Segoe UI", 11F),
                Multiline = true,
                Name = "txtTranslatorInput",
                ScrollBars = ScrollBars.Vertical
            };
            InputTextBox.TextChanged += InputTextBox_TextChanged;
            OutputLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Name = "lblTranslatorOutput",
                Text = $"{targetName} translation"
            };
            OutputTextBox = new TextBox
            {
                AcceptsReturn = true,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 11F),
                Multiline = true,
                Name = "txtTranslatorOutput",
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };
            OutputTextBox.TextChanged += OutputTextBox_TextChanged;
            ExportButton = new Button
            {
                AutoSize = true,
                Enabled = false,
                Name = "btnExportTranslation",
                Text = "Export Translation",
                UseVisualStyleBackColor = true,
                Visible = false
            };
            ExportButton.Click += ExportButton_Click;
            Panel = new Panel
            {
                BackColor = Color.WhiteSmoke,
                Name = "pnlTranslator"
            };
            Panel.Controls.AddRange(
            [
                HeadingLabel,
                DirectionCheckBox,
                InputLabel,
                InputTextBox,
                OutputLabel,
                OutputTextBox,
                ExportButton
            ]);
            _owner.Controls.Add(Panel);
            UpdateBounds();
            Panel.BringToFront();
            _menuStrip.BringToFront();
            _statusStrip.BringToFront();
            _orcishMenuItem.Enabled = targetLanguage != TranslatorTargetLanguage.Orcish;
            _elvenMenuItem.Enabled = targetLanguage != TranslatorTargetLanguage.Elven;
            _ghukliakMenuItem.Enabled = targetLanguage != TranslatorTargetLanguage.Ghukliak;
            FocusInput();
            _ = UpdateWarmupStatusAsync();
        }

        public void UpdateBounds()
        {
            if (Panel is null ||
                HeadingLabel is null ||
                DirectionCheckBox is null ||
                InputLabel is null ||
                InputTextBox is null ||
                OutputLabel is null ||
                OutputTextBox is null ||
                ExportButton is null)
            {
                return;
            }

            Panel.Bounds = new Rectangle(
                10,
                35,
                Math.Max(0, _owner.ClientSize.Width - 30),
                Math.Max(0, _owner.ClientSize.Height - 70));

            const int maximumContentWidth = 880;
            const int sidePadding = 20;
            const int spacing = 8;
            var contentWidth = Math.Max(120, Math.Min(maximumContentWidth, Panel.ClientSize.Width - (sidePadding * 2)));
            var left = Math.Max(sidePadding, (Panel.ClientSize.Width - contentWidth) / 2);
            var top = 18;

            HeadingLabel.Location = new Point(
                Math.Max(0, (Panel.ClientSize.Width - HeadingLabel.Width) / 2),
                top);
            DirectionCheckBox.Location = new Point(
                Math.Max(0, (Panel.ClientSize.Width - DirectionCheckBox.Width) / 2),
                HeadingLabel.Bottom + 10);
            InputLabel.Location = new Point(left, DirectionCheckBox.Bottom + 14);

            const int reservedHeight = 140;
            var availableTextHeight = Math.Max(120, Panel.ClientSize.Height - reservedHeight);
            var textBoxHeight = Math.Max(60, availableTextHeight / 2);
            InputTextBox.Bounds = new Rectangle(left, InputLabel.Bottom + spacing, contentWidth, textBoxHeight);
            OutputLabel.Location = new Point(left, InputTextBox.Bottom + 14);
            var outputBottomInset = ExportButton.Visible
                ? ExportButton.Height + spacing + 14
                : 14;
            OutputTextBox.Bounds = new Rectangle(
                left,
                OutputLabel.Bottom + spacing,
                contentWidth,
                Math.Max(50, Panel.ClientSize.Height - OutputLabel.Bottom - spacing - outputBottomInset));
            ExportButton.Location = new Point(
                left + contentWidth - ExportButton.Width,
                OutputTextBox.Bottom + spacing);
        }

        public void Dispose()
        {
            DisposePanel();
        }

        private void OutputTextBox_TextChanged(object? sender, EventArgs e)
        {
            UpdateExportButtonState();
        }

        private void UpdateExportButtonState()
        {
            if (ExportButton is null || DirectionCheckBox is null || OutputTextBox is null)
            {
                return;
            }

            var shouldBeVisible =
                !DirectionCheckBox.Checked &&
                !string.IsNullOrWhiteSpace(OutputTextBox.Text);
            var layoutChanged = ExportButton.Enabled != shouldBeVisible;
            ExportButton.Visible = shouldBeVisible;
            ExportButton.Enabled = shouldBeVisible;
            if (layoutChanged)
            {
                UpdateBounds();
            }
        }

        private void ExportButton_Click(object? sender, EventArgs e)
        {
            ExportTranslation();
        }

        internal void ExportTranslation()
        {
            if (DirectionCheckBox?.Checked != false ||
                InputTextBox is null ||
                OutputTextBox is null ||
                string.IsNullOrWhiteSpace(OutputTextBox.Text))
            {
                return;
            }

            string? filePath;
            var pathOverride = _exportPathOverrideProvider();
            if (pathOverride is not null)
            {
                filePath = pathOverride();
            }
            else
            {
                var defaultFileName = Form1.BuildTranslatorExportDefaultFileName(
                    InputTextBox.Text,
                    OutputTextBox.Text,
                    _controller.ExportLanguageToken);
                using var saveDialog = new SaveFileDialog
                {
                    AddExtension = true,
                    DefaultExt = "txt",
                    FileName = defaultFileName,
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    OverwritePrompt = true,
                    RestoreDirectory = true,
                    Title = "Export Translation"
                };
                filePath = saveDialog.ShowDialog(_owner) == DialogResult.OK
                    ? saveDialog.FileName
                    : null;
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                File.WriteAllText(filePath, OutputTextBox.Text, new UTF8Encoding(false));
                _setStatus($"Translation exported to {filePath}.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    _owner,
                    $"The translation could not be saved.\r\n\r\n{ex.Message}",
                    "Export Translation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void DirectionCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            if (DirectionCheckBox is null ||
                HeadingLabel is null ||
                InputLabel is null ||
                InputTextBox is null ||
                OutputLabel is null ||
                OutputTextBox is null ||
                ExportButton is null)
            {
                return;
            }

            CancelPendingTranslation();
            var targetToEnglish = DirectionCheckBox.Checked;
            var targetName = _controller.TargetName;
            HeadingLabel.Text = targetToEnglish ? $"{targetName} to English" : $"English to {targetName}";
            InputLabel.Text = targetToEnglish ? $"{targetName} text" : "English text";
            OutputLabel.Text = targetToEnglish ? "English translation" : $"{targetName} translation";
            InputTextBox.Clear();
            OutputTextBox.Clear();
            _previousInputLength = 0;
            UpdateBounds();
            FocusInput();
            _ = UpdateWarmupStatusAsync();
        }

        private async void InputTextBox_TextChanged(object? sender, EventArgs e)
        {
            if (InputTextBox is null || OutputTextBox is null || DirectionCheckBox is null)
            {
                return;
            }

            var input = InputTextBox.Text;
            var inputLengthChange = Math.Abs(input.Length - _previousInputLength);
            _previousInputLength = input.Length;

            CancelPendingTranslation();
            if (string.IsNullOrWhiteSpace(input))
            {
                OutputTextBox.Clear();
                _setStatus("Translator ready.");
                return;
            }

            var targetToEnglish = DirectionCheckBox.Checked;
            var targetLanguage = _controller.TargetLanguage;
            var generation = _translationGeneration;
            var cancellationSource = new CancellationTokenSource();
            _translationCancellationSource = cancellationSource;
            try
            {
                if (inputLengthChange <= 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(125), cancellationSource.Token);
                }

                var translatorOverride = _translationOverrideProvider();
                var waitCursorDelay = Task.Delay(TimeSpan.FromMilliseconds(250), cancellationSource.Token);
                if (translatorOverride is null)
                {
                    var warmupTask = _controller.WaitUntilReadyAsync(targetLanguage, cancellationSource.Token);
                    if (await Task.WhenAny(warmupTask, waitCursorDelay) == waitCursorDelay &&
                        !cancellationSource.IsCancellationRequested &&
                        generation == _translationGeneration)
                    {
                        SetWaitCursor(true);
                        _setStatus($"Preparing {TranslatorController.GetTargetName(targetLanguage)} translator...");
                    }

                    await warmupTask;
                }

                var translationTask = Task.Run(
                    () => translatorOverride is not null
                        ? translatorOverride(input, targetToEnglish)
                        : _controller.Translate(input, targetLanguage, targetToEnglish),
                    cancellationSource.Token);
                if (!_waitCursorActive &&
                    await Task.WhenAny(translationTask, waitCursorDelay) == waitCursorDelay &&
                    !cancellationSource.IsCancellationRequested &&
                    generation == _translationGeneration)
                {
                    SetWaitCursor(true);
                    _setStatus("Translating...");
                }

                var translation = await translationTask;
                cancellationSource.Token.ThrowIfCancellationRequested();
                if (generation == _translationGeneration &&
                    OutputTextBox is not null &&
                    !OutputTextBox.IsDisposed)
                {
                    OutputTextBox.Text = translation;
                    _setStatus("Translation complete.");
                }
            }
            catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (generation == _translationGeneration)
                {
                    await _reportFailure(
                        $"{TranslatorController.GetTargetName(targetLanguage)} translation",
                        "Translation unavailable",
                        "Translator Error",
                        ex,
                        true);
                }
            }
            finally
            {
                if (ReferenceEquals(_translationCancellationSource, cancellationSource))
                {
                    _translationCancellationSource = null;
                }

                if (generation == _translationGeneration)
                {
                    SetWaitCursor(false);
                }

                cancellationSource.Dispose();
            }
        }

        private async Task UpdateWarmupStatusAsync()
        {
            var targetLanguage = _controller.TargetLanguage;
            if (_translationOverrideProvider() is not null)
            {
                _setStatus("Translator ready.");
                return;
            }

            if (_controller.IsReadyFor(targetLanguage))
            {
                _setStatus("Translator ready.");
                return;
            }

            _setStatus($"Preparing {TranslatorController.GetTargetName(targetLanguage)} translator...");
            try
            {
                var englishTermCount = await _controller.StartPreloadingAsync(targetLanguage);
                if (_controller.TargetLanguage == targetLanguage && Panel is not null && !Panel.IsDisposed)
                {
                    _setStatus($"Translator ready: {englishTermCount:N0} English terms loaded.");
                }
            }
            catch
            {
                if (_controller.TargetLanguage == targetLanguage && Panel is not null && !Panel.IsDisposed)
                {
                    _setStatus("Translator unavailable.");
                }
            }
        }

        private void CancelPendingTranslation()
        {
            _translationGeneration++;
            var cancellationSource = _translationCancellationSource;
            _translationCancellationSource = null;
            cancellationSource?.Cancel();
            SetWaitCursor(false);
        }

        private void SetWaitCursor(bool active)
        {
            if (_waitCursorActive == active)
            {
                return;
            }

            _waitCursorActive = active;
            _owner.UseWaitCursor = active;
        }

        private void FocusInput()
        {
            if (InputTextBox is null || InputTextBox.IsDisposed)
            {
                return;
            }

            _owner.ActiveControl = InputTextBox;
            InputTextBox.Focus();
            InputTextBox.Select(InputTextBox.TextLength, 0);
        }

        private void DisposePanel()
        {
            CancelPendingTranslation();
            if (Panel is null)
            {
                return;
            }

            _owner.Controls.Remove(Panel);
            Panel.Dispose();
            Panel = null;
            HeadingLabel = null;
            DirectionCheckBox = null;
            InputLabel = null;
            InputTextBox = null;
            OutputLabel = null;
            OutputTextBox = null;
            ExportButton = null;
            _previousInputLength = 0;
            var translatorMenuEnabled = _menusAllowed();
            _translatorMenuItem.Enabled = translatorMenuEnabled;
            _orcishMenuItem.Enabled = translatorMenuEnabled;
            _elvenMenuItem.Enabled = translatorMenuEnabled;
            _ghukliakMenuItem.Enabled = translatorMenuEnabled;
        }
    }
}
