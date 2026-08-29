namespace PlayerAssistant;

internal sealed class RpolWebViewDispatchRegistration : IDisposable
{
    private readonly Action _cancel;
    private readonly Action _dispose;
    private int _disposed;

    internal RpolWebViewDispatchRegistration(Action cancel, Action dispose)
    {
        _cancel = cancel;
        _dispose = dispose;
    }

    internal void Cancel() => _cancel();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _dispose();
    }
}

internal static class RpolWebViewDispatchLifetime
{
    internal static RpolWebViewDispatchRegistration Register(
        Action cancellation,
        Func<Action, bool> enqueue,
        Action dispose)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        ArgumentNullException.ThrowIfNull(enqueue);
        ArgumentNullException.ThrowIfNull(dispose);
        var completed = 0;
        void Cancel()
        {
            if (Interlocked.Exchange(ref completed, 1) != 0) return;
            try
            {
                if (enqueue(cancellation)) return;
            }
            catch { }
            cancellation();
        }
        return new RpolWebViewDispatchRegistration(Cancel, dispose);
    }
}
