namespace PlayerAssistant;

internal sealed class RpolCredentialSubmissionGuard : IDisposable
{
    private readonly TaskCompletionSource<bool> _requestCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _armed = 1;

    internal bool IsArmed => Volatile.Read(ref _armed) != 0;

    internal bool Complete(bool valid)
    {
        if (Interlocked.Exchange(ref _armed, 0) == 0)
        {
            return false;
        }

        _requestCompletion.TrySetResult(valid);
        return true;
    }

    internal Task<bool> WaitForRequestAsync(TimeSpan bound, CancellationToken cancellationToken)
    {
        if (bound <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(bound));
        return _requestCompletion.Task.WaitAsync(bound, cancellationToken);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _armed, 0);
        _requestCompletion.TrySetResult(false);
    }
}