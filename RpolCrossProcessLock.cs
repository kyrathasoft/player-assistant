using System.Threading;

namespace PlayerAssistant;

internal sealed class RpolCrossProcessLock : IDisposable
{
    internal const string AuthAndPublisherName = "PlayerAssistant.Rpol.AuthAndPublisher";
    private readonly LockState _state;
    private int _disposed;

    private RpolCrossProcessLock(LockState state)
    {
        _state = state;
    }

    internal string Name => _state.Name;

    // Reentrancy is explicit: a nested acquire must be given the lease that owns the lock.
    internal RpolCrossProcessLock AcquireReentrant(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _state.AddReference();
        return new RpolCrossProcessLock(_state);
    }

    internal static RpolCrossProcessLock? TryAcquire(string name)
    {
        return TryAcquire(name, CancellationToken.None);
    }

    internal static RpolCrossProcessLock? TryAcquire(
        string name,
        CancellationToken cancellationToken)
    {
        return AcquireCore(name, TimeSpan.Zero, cancellationToken, throwOnTimeout: false);
    }

    internal static RpolCrossProcessLock Acquire(
        string name,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return AcquireCore(name, timeout, cancellationToken, throwOnTimeout: true)!;
    }

    internal static Task<RpolCrossProcessLock> AcquireAsync(
        string name,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Acquire(name, timeout, cancellationToken), cancellationToken);
    }

    private static RpolCrossProcessLock? AcquireCore(
        string name,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool throwOnTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var normalizedName = name.StartsWith("Local\\", StringComparison.Ordinal)
            ? name
            : $"Local\\{name}";
        var semaphore = new Semaphore(1, 1, normalizedName);
        try
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = deadline - DateTimeOffset.UtcNow;
                var wait = timeout == TimeSpan.Zero
                    ? TimeSpan.Zero
                    : remaining > TimeSpan.FromMilliseconds(100)
                        ? TimeSpan.FromMilliseconds(100)
                        : remaining;
                if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
                if (semaphore.WaitOne(wait))
                {
                    return new RpolCrossProcessLock(new LockState(semaphore, normalizedName));
                }
                if (timeout == TimeSpan.Zero || DateTimeOffset.UtcNow >= deadline)
                {
                    if (throwOnTimeout) throw new TimeoutException("Another RPOL verifier or publisher is already running.");
                    return null;
                }
            }
        }
        catch
        {
            semaphore.Dispose();
            throw;
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(RpolCrossProcessLock));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _state.ReleaseReference();
    }

    private sealed class LockState
    {
        private readonly Semaphore _semaphore;
        private int _references = 1;
        private int _released;

        internal LockState(Semaphore semaphore, string name)
        {
            _semaphore = semaphore;
            Name = name;
        }

        internal string Name { get; }

        internal void AddReference()
        {
            while (true)
            {
                var current = Volatile.Read(ref _references);
                if (current <= 0 || Volatile.Read(ref _released) != 0)
                {
                    throw new ObjectDisposedException(nameof(RpolCrossProcessLock));
                }
                if (Interlocked.CompareExchange(ref _references, current + 1, current) == current) return;
            }
        }

        internal void ReleaseReference()
        {
            if (Interlocked.Decrement(ref _references) != 0) return;
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            try
            {
                _semaphore.Release();
            }
            finally
            {
                _semaphore.Dispose();
            }
        }
    }
}
