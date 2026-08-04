namespace PlayerAssistant
{
    internal sealed record GhukliakTranslatorWarmupResult(int EnglishTermCount, TimeSpan Duration);

    internal static class GhukliakTranslatorWarmupUtility
    {
        private static readonly object SyncRoot = new();
        private static Task<GhukliakTranslatorWarmupResult>? _warmupTask;

        public static bool IsReady
        {
            get
            {
                lock (SyncRoot)
                {
                    return _warmupTask?.IsCompletedSuccessfully == true;
                }
            }
        }

        public static Task<GhukliakTranslatorWarmupResult> StartPreloading()
        {
            lock (SyncRoot)
            {
                return _warmupTask ??= Task.Run(() =>
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var count = GhukliakTranslatorUtility.WarmUpIndexes();
                    stopwatch.Stop();
                    return new GhukliakTranslatorWarmupResult(count, stopwatch.Elapsed);
                });
            }
        }

        public static Task<GhukliakTranslatorWarmupResult> WaitUntilReadyAsync(
            CancellationToken cancellationToken = default) =>
            StartPreloading().WaitAsync(cancellationToken);
    }
}
