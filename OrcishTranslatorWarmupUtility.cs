namespace PlayerAssistant
{
    internal sealed record OrcishTranslatorWarmupResult(
        int EnglishTermCount,
        TimeSpan Duration);

    internal static class OrcishTranslatorWarmupUtility
    {
        private static readonly object SyncRoot = new();
        private static Task<OrcishTranslatorWarmupResult>? _warmupTask;

        internal static Func<int>? WarmupOverrideForTests { get; set; }

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

        public static Task<OrcishTranslatorWarmupResult> StartPreloading()
        {
            lock (SyncRoot)
            {
                return _warmupTask ??= CreateWarmupTask();
            }
        }

        public static Task<OrcishTranslatorWarmupResult> WaitUntilReadyAsync(
            CancellationToken cancellationToken = default)
        {
            return StartPreloading().WaitAsync(cancellationToken);
        }

        internal static void ResetForTests()
        {
            lock (SyncRoot)
            {
                if (_warmupTask is { IsCompleted: false })
                {
                    throw new InvalidOperationException("Cannot reset Orcish translator warmup while it is running.");
                }

                _warmupTask = null;
                WarmupOverrideForTests = null;
            }
        }

        private static Task<OrcishTranslatorWarmupResult> CreateWarmupTask()
        {
            var task = Task.Run(() =>
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var termCount = WarmupOverrideForTests?.Invoke()
                    ?? OrcishTranslatorUtility.WarmUpIndexes();
                stopwatch.Stop();
                return new OrcishTranslatorWarmupResult(termCount, stopwatch.Elapsed);
            });

            _ = task.ContinueWith(
                static failedTask => StartupLoggingUtility.Append(
                    "Orcish translator preload",
                    failedTask.Exception?.GetBaseException()
                        ?? new InvalidOperationException("Orcish translator preload failed.")),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }
    }
}
