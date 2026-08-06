namespace PlayerAssistant;

internal enum TranslatorTargetLanguage
{
    Orcish,
    Elven,
    Ghukliak
}

internal interface ITranslatorService
{
    bool IsReady(TranslatorTargetLanguage targetLanguage);

    Task<int> WarmUpAsync(TranslatorTargetLanguage targetLanguage, CancellationToken cancellationToken);

    Task<string> TranslateAsync(
        string input,
        TranslatorTargetLanguage targetLanguage,
        bool targetToEnglish,
        CancellationToken cancellationToken);
}

internal sealed class TranslatorService(Func<Func<string, bool, string>?> translationOverrideProvider) : ITranslatorService
{
    public bool IsReady(TranslatorTargetLanguage targetLanguage) =>
        translationOverrideProvider() is not null || targetLanguage switch
        {
            TranslatorTargetLanguage.Orcish => OrcishTranslatorWarmupUtility.IsReady,
            TranslatorTargetLanguage.Elven => ElvenTranslatorWarmupUtility.IsReady,
            _ => GhukliakTranslatorWarmupUtility.IsReady
        };

    public async Task<int> WarmUpAsync(
        TranslatorTargetLanguage targetLanguage,
        CancellationToken cancellationToken)
    {
        if (translationOverrideProvider() is not null)
        {
            return 0;
        }

        return targetLanguage switch
        {
            TranslatorTargetLanguage.Orcish =>
                (await OrcishTranslatorWarmupUtility.WaitUntilReadyAsync(cancellationToken)).EnglishTermCount,
            TranslatorTargetLanguage.Elven =>
                (await ElvenTranslatorWarmupUtility.WaitUntilReadyAsync(cancellationToken)).EnglishTermCount,
            _ => (await GhukliakTranslatorWarmupUtility.WaitUntilReadyAsync(cancellationToken)).EnglishTermCount
        };
    }

    public Task<string> TranslateAsync(
        string input,
        TranslatorTargetLanguage targetLanguage,
        bool targetToEnglish,
        CancellationToken cancellationToken)
    {
        var translatorOverride = translationOverrideProvider();
        return Task.Run(
            () => translatorOverride is not null
                ? translatorOverride(input, targetToEnglish)
                : TranslateText(input, targetLanguage, targetToEnglish),
            cancellationToken);
    }

    private static string TranslateText(
        string input,
        TranslatorTargetLanguage targetLanguage,
        bool targetToEnglish) =>
        targetLanguage switch
        {
            TranslatorTargetLanguage.Orcish when targetToEnglish =>
                OrcishTranslatorUtility.TranslateOrcishTextToEnglish(input),
            TranslatorTargetLanguage.Orcish =>
                OrcishTranslatorUtility.TranslateEnglishTextToOrcish(input),
            TranslatorTargetLanguage.Elven when targetToEnglish =>
                ElvenTranslatorUtility.TranslateElvenTextToEnglish(input),
            TranslatorTargetLanguage.Elven =>
                ElvenTranslatorUtility.TranslateEnglishTextToElven(input),
            TranslatorTargetLanguage.Ghukliak when targetToEnglish =>
                GhukliakTranslatorUtility.TranslateGhukliakTextToEnglish(input),
            _ => GhukliakTranslatorUtility.TranslateEnglishTextToGhukliak(input)
        };
}

internal sealed class TranslatorController : IDisposable
{
    private static readonly TimeSpan InputDebounceDelay = TimeSpan.FromMilliseconds(125);
    private static readonly TimeSpan BusyIndicatorDelay = TimeSpan.FromMilliseconds(250);

    private readonly ITranslatorService _translatorService;
    private readonly Action<string> _setStatus;
    private readonly Action<bool> _setWaitCursor;
    private readonly Func<string, Exception, Task> _reportFailureAsync;
    private readonly Action<string> _showTranslation;
    private CancellationTokenSource? _translationCancellationSource;
    private int _translationGeneration;
    private bool _active;
    private bool _waitCursorActive;

    public TranslatorController(
        ITranslatorService translatorService,
        Action<string> setStatus,
        Action<bool> setWaitCursor,
        Func<string, Exception, Task> reportFailureAsync,
        Action<string> showTranslation)
    {
        _translatorService = translatorService;
        _setStatus = setStatus;
        _setWaitCursor = setWaitCursor;
        _reportFailureAsync = reportFailureAsync;
        _showTranslation = showTranslation;
    }

    public TranslatorTargetLanguage TargetLanguage { get; private set; } = TranslatorTargetLanguage.Orcish;

    public void Activate(TranslatorTargetLanguage targetLanguage)
    {
        CancelPendingTranslation();
        TargetLanguage = targetLanguage;
        _active = true;
    }

    public void Deactivate()
    {
        _active = false;
        CancelPendingTranslation();
    }

    public async Task TranslateInputAsync(
        string input,
        bool targetToEnglish,
        int inputLengthChange)
    {
        CancelPendingTranslation();
        if (!_active || string.IsNullOrWhiteSpace(input))
        {
            _setStatus("Translator ready.");
            return;
        }

        var targetLanguage = TargetLanguage;
        var generation = _translationGeneration;
        using var cancellationSource = new CancellationTokenSource();
        _translationCancellationSource = cancellationSource;
        var cancellationToken = cancellationSource.Token;

        try
        {
            if (inputLengthChange <= 1)
            {
                await Task.Delay(InputDebounceDelay, cancellationToken);
            }

            var waitCursorDelay = Task.Delay(BusyIndicatorDelay, cancellationToken);
            if (!_translatorService.IsReady(targetLanguage))
            {
                var warmupTask = _translatorService.WarmUpAsync(targetLanguage, cancellationToken);
                if (await Task.WhenAny(warmupTask, waitCursorDelay) == waitCursorDelay && IsCurrent(generation, cancellationSource))
                {
                    SetWaitCursor(true);
                    _setStatus($"Preparing {GetTargetName(targetLanguage)} translator...");
                }

                await warmupTask;
            }

            var translationTask = _translatorService.TranslateAsync(input, targetLanguage, targetToEnglish, cancellationToken);
            if (!_waitCursorActive &&
                await Task.WhenAny(translationTask, waitCursorDelay) == waitCursorDelay &&
                IsCurrent(generation, cancellationSource))
            {
                SetWaitCursor(true);
                _setStatus("Translating...");
            }

            var translation = await translationTask;
            cancellationToken.ThrowIfCancellationRequested();
            if (IsCurrent(generation, cancellationSource))
            {
                _showTranslation(translation);
                _setStatus("Translation complete.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrent(generation, cancellationSource))
            {
                await _reportFailureAsync($"{GetTargetName(targetLanguage)} translation", ex);
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
        }
    }

    public async Task UpdateWarmupStatusAsync()
    {
        var targetLanguage = TargetLanguage;
        var generation = _translationGeneration;
        if (!_active)
        {
            return;
        }

        if (_translatorService.IsReady(targetLanguage))
        {
            _setStatus("Translator ready.");
            return;
        }

        _setStatus($"Preparing {GetTargetName(targetLanguage)} translator...");
        try
        {
            var termCount = await _translatorService.WarmUpAsync(targetLanguage, CancellationToken.None);
            if (_active && generation == _translationGeneration && TargetLanguage == targetLanguage)
            {
                _setStatus($"Translator ready: {termCount:N0} English terms loaded.");
            }
        }
        catch
        {
            if (_active && generation == _translationGeneration && TargetLanguage == targetLanguage)
            {
                _setStatus("Translator unavailable.");
            }
        }
    }

    public void CancelPendingTranslation()
    {
        _translationGeneration++;
        var cancellationSource = _translationCancellationSource;
        _translationCancellationSource = null;
        cancellationSource?.Cancel();
        SetWaitCursor(false);
    }

    public void Dispose()
    {
        Deactivate();
    }

    internal static string GetTargetName(TranslatorTargetLanguage targetLanguage) =>
        targetLanguage switch
        {
            TranslatorTargetLanguage.Orcish => "Orcish",
            TranslatorTargetLanguage.Elven => "Elven",
            _ => "Goblin (Ghukliak)"
        };

    private bool IsCurrent(int generation, CancellationTokenSource cancellationSource) =>
        _active &&
        generation == _translationGeneration &&
        ReferenceEquals(_translationCancellationSource, cancellationSource) &&
        !cancellationSource.IsCancellationRequested;

    private void SetWaitCursor(bool active)
    {
        if (_waitCursorActive == active)
        {
            return;
        }

        _waitCursorActive = active;
        _setWaitCursor(active);
    }
}
