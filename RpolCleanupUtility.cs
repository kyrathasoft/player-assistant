namespace PlayerAssistant;

internal static class RpolCleanupUtility
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Task, byte> LateTasks = new();

    internal static IReadOnlyList<Exception> DisposeIndependently(
        params (string Name, Action Dispose)[] resources)
    {
        var errors = new List<Exception>();
        foreach (var (name, dispose) in resources)
        {
            if (dispose is null) continue;
            try { dispose(); }
            catch (Exception ex) { errors.Add(new InvalidOperationException($"RPOL cleanup failed for {name}.", ex)); }
        }
        return errors;
    }

    internal static async Task<IReadOnlyList<Exception>> DisposeAsyncIndependently(
        CancellationToken cancellationToken,
        params (string Name, Func<Task> Dispose)[] resources)
    {
        var errors = new List<Exception>();
        foreach (var (name, dispose) in resources)
        {
            if (dispose is null) continue;
            try
            {
                var task = dispose();
                try
                {
                    await task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    TrackLateTask(task);
                    throw;
                }
            }
            catch (Exception ex) { errors.Add(new InvalidOperationException($"RPOL cleanup failed for {name}.", ex)); }
        }
        return errors;
    }

    internal static async Task<IReadOnlyList<Exception>> DisposeAsyncIndependently(
        params (string Name, Func<Task> Dispose)[] resources)
    {
        return await DisposeAsyncIndependently(CancellationToken.None, resources).ConfigureAwait(false);
    }

    internal static void TrackLateTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        LateTasks.TryAdd(task, 0);
    }

    internal static async Task<IReadOnlyList<Exception>> JoinLateTasksAsync(CancellationToken cancellationToken)
    {
        var errors = new List<Exception>();
        foreach (var task in LateTasks.Keys.ToArray())
        {
            try { await task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { errors.Add(ex); }
            finally
            {
                if (task.IsCompleted) LateTasks.TryRemove(task, out _);
            }
        }
        return errors;
    }

    internal static void DeleteDirectoryBounded(
        string directory,
        TimeSpan bound,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (bound <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(bound));
        var deadline = DateTimeOffset.UtcNow + bound;
        if (!Directory.Exists(directory)) return;

        var pending = new Stack<string>(Directory.EnumerateFileSystemEntries(directory));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("RPOL directory cleanup exceeded its bound.");
            var path = pending.Pop();
            if (Directory.Exists(path))
            {
                var children = Directory.EnumerateFileSystemEntries(path).ToArray();
                if (children.Length > 0)
                {
                    pending.Push(path);
                    foreach (var child in children) pending.Push(child);
                }
                else
                {
                    Directory.Delete(path);
                }
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(directory);
    }
}
