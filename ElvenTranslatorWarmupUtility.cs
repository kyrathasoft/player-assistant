namespace PlayerAssistant
{
    internal sealed record ElvenTranslatorWarmupResult(int EnglishTermCount, TimeSpan Duration);

    internal static class ElvenTranslatorWarmupUtility
    {
        private static readonly object SyncRoot = new();
        private static Task<ElvenTranslatorWarmupResult>? _warmupTask;

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

        public static Task<ElvenTranslatorWarmupResult> StartPreloading()
        {
            lock (SyncRoot)
            {
                if (_warmupTask is not null)
                {
                    return _warmupTask;
                }

                _warmupTask = Task.Run(() =>
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var count = ElvenTranslatorUtility.WarmUpIndexes();
                    stopwatch.Stop();
                    return new ElvenTranslatorWarmupResult(count, stopwatch.Elapsed);
                });
                _ = _warmupTask.ContinueWith(
                    static failedTask => StartupLoggingUtility.Append(
                        "Elven translator preload",
                        failedTask.Exception?.GetBaseException()
                            ?? new InvalidOperationException("Elven translator preload failed.")),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return _warmupTask;
            }
        }

        public static Task<ElvenTranslatorWarmupResult> WaitUntilReadyAsync(
            CancellationToken cancellationToken = default) =>
            StartPreloading().WaitAsync(cancellationToken);
    }
}
