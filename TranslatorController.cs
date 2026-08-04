namespace PlayerAssistant
{
    internal enum TranslatorTargetLanguage
    {
        Orcish,
        Elven,
        Ghukliak
    }

    internal interface ITranslatorBackend
    {
        bool IsReady(TranslatorTargetLanguage targetLanguage);

        Task<OrcishTranslatorWarmupResult> WaitUntilReadyAsync(
            TranslatorTargetLanguage targetLanguage,
            CancellationToken cancellationToken);

        Task<int> StartPreloadingAsync(TranslatorTargetLanguage targetLanguage);

        string Translate(
            string input,
            TranslatorTargetLanguage targetLanguage,
            bool targetToEnglish);
    }

    internal sealed class TranslatorController
    {
        private readonly ITranslatorBackend _backend;

        public TranslatorController(ITranslatorBackend backend)
        {
            ArgumentNullException.ThrowIfNull(backend);
            _backend = backend;
        }

        public TranslatorTargetLanguage TargetLanguage { get; private set; } = TranslatorTargetLanguage.Orcish;

        public string TargetName => GetTargetName(TargetLanguage);

        public string ExportLanguageToken => GetExportLanguageToken(TargetLanguage);

        public bool IsReady => _backend.IsReady(TargetLanguage);

        public void SelectTarget(TranslatorTargetLanguage targetLanguage)
        {
            TargetLanguage = targetLanguage;
        }

        public Task<OrcishTranslatorWarmupResult> WaitUntilReadyAsync(CancellationToken cancellationToken)
        {
            return WaitUntilReadyAsync(TargetLanguage, cancellationToken);
        }

        public Task<OrcishTranslatorWarmupResult> WaitUntilReadyAsync(
            TranslatorTargetLanguage targetLanguage,
            CancellationToken cancellationToken)
        {
            return _backend.WaitUntilReadyAsync(targetLanguage, cancellationToken);
        }

        public Task<int> StartPreloadingAsync()
        {
            return StartPreloadingAsync(TargetLanguage);
        }

        public Task<int> StartPreloadingAsync(TranslatorTargetLanguage targetLanguage)
        {
            return _backend.StartPreloadingAsync(targetLanguage);
        }

        public string Translate(string input, bool targetToEnglish)
        {
            return Translate(input, TargetLanguage, targetToEnglish);
        }

        public string Translate(
            string input,
            TranslatorTargetLanguage targetLanguage,
            bool targetToEnglish)
        {
            return _backend.Translate(input, targetLanguage, targetToEnglish);
        }

        public bool IsReadyFor(TranslatorTargetLanguage targetLanguage)
        {
            return _backend.IsReady(targetLanguage);
        }

        public static string GetTargetName(TranslatorTargetLanguage targetLanguage) =>
            targetLanguage switch
            {
                TranslatorTargetLanguage.Orcish => "Orcish",
                TranslatorTargetLanguage.Elven => "Elven",
                _ => "Goblin (Ghukliak)"
            };

        public static string GetExportLanguageToken(TranslatorTargetLanguage targetLanguage) =>
            targetLanguage switch
            {
                TranslatorTargetLanguage.Orcish => "orcish",
                TranslatorTargetLanguage.Elven => "elvish",
                _ => "ghukliak"
            };
    }

    internal sealed class TranslatorBackend : ITranslatorBackend
    {
        public bool IsReady(TranslatorTargetLanguage targetLanguage) =>
            targetLanguage switch
            {
                TranslatorTargetLanguage.Orcish => OrcishTranslatorWarmupUtility.IsReady,
                TranslatorTargetLanguage.Elven => ElvenTranslatorWarmupUtility.IsReady,
                _ => GhukliakTranslatorWarmupUtility.IsReady
            };

        public async Task<OrcishTranslatorWarmupResult> WaitUntilReadyAsync(
            TranslatorTargetLanguage targetLanguage,
            CancellationToken cancellationToken)
        {
            switch (targetLanguage)
            {
                case TranslatorTargetLanguage.Orcish:
                    return await OrcishTranslatorWarmupUtility.WaitUntilReadyAsync(cancellationToken);
                case TranslatorTargetLanguage.Elven:
                    {
                        var result = await ElvenTranslatorWarmupUtility.WaitUntilReadyAsync(cancellationToken);
                        return new OrcishTranslatorWarmupResult(result.EnglishTermCount, result.Duration);
                    }
                default:
                    {
                        var result = await GhukliakTranslatorWarmupUtility.WaitUntilReadyAsync(cancellationToken);
                        return new OrcishTranslatorWarmupResult(result.EnglishTermCount, result.Duration);
                    }
            }
        }

        public async Task<int> StartPreloadingAsync(TranslatorTargetLanguage targetLanguage)
        {
            return targetLanguage switch
            {
                TranslatorTargetLanguage.Orcish => (await OrcishTranslatorWarmupUtility.StartPreloading()).EnglishTermCount,
                TranslatorTargetLanguage.Elven => (await ElvenTranslatorWarmupUtility.StartPreloading()).EnglishTermCount,
                _ => (await GhukliakTranslatorWarmupUtility.StartPreloading()).EnglishTermCount
            };
        }

        public string Translate(
            string input,
            TranslatorTargetLanguage targetLanguage,
            bool targetToEnglish)
        {
            return targetLanguage switch
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
    }
}
