namespace PlayerAssistant
{
    internal sealed class BackgroundTaskSupervisor : IDisposable
    {
        private readonly object _syncRoot = new();
        private readonly Dictionary<string, BackgroundTaskHandle> _runningTasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _shutdownCancellation = new();
        private bool _disposed;

        public bool TryStart(string phase, Func<CancellationToken, Task> action)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(phase);
            ArgumentNullException.ThrowIfNull(action);

            BackgroundTaskHandle handle;
            lock (_syncRoot)
            {
                if (_disposed || _runningTasks.ContainsKey(phase))
                {
                    return false;
                }

                handle = new BackgroundTaskHandle(
                    CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token));
                _runningTasks.Add(phase, handle);
            }

            var task = RunAsync(phase, action, handle);
            lock (_syncRoot)
            {
                handle.Task = task;
            }

            return true;
        }

        public bool Cancel(string phase)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(phase);
            lock (_syncRoot)
            {
                if (!_runningTasks.TryGetValue(phase, out var handle))
                {
                    return false;
                }

                handle.Cancellation.Cancel();
                return true;
            }
        }

        public bool IsRunning(string phase)
        {
            lock (_syncRoot)
            {
                return _runningTasks.ContainsKey(phase);
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _shutdownCancellation.Cancel();
                _runningTasks.Clear();
            }

            _shutdownCancellation.Dispose();
        }

        private async Task RunAsync(
            string phase,
            Func<CancellationToken, Task> action,
            BackgroundTaskHandle handle)
        {
            try
            {
                await action(handle.Cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (handle.Cancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                await StartupLoggingUtility.AppendAsync(phase, ex).ConfigureAwait(false);
            }
            finally
            {
                lock (_syncRoot)
                {
                    if (_runningTasks.TryGetValue(phase, out var currentHandle)
                        && ReferenceEquals(currentHandle, handle))
                    {
                        _runningTasks.Remove(phase);
                    }
                }

                handle.Dispose();
            }
        }

        private sealed class BackgroundTaskHandle : IDisposable
        {
            public BackgroundTaskHandle(CancellationTokenSource cancellation)
            {
                Cancellation = cancellation;
            }

            public CancellationTokenSource Cancellation { get; }

            public Task? Task { get; set; }

            public void Dispose()
            {
                Cancellation.Dispose();
            }
        }
    }
}
