namespace PlayerAssistant;

internal sealed class RpolOperationDeadline : IDisposable
{
    private readonly CancellationTokenSource _operationCancellation;
    private readonly CancellationTokenSource _cleanupCancellation;
    internal DateTimeOffset OperationDeadlineUtc { get; }
    internal DateTimeOffset CleanupDeadlineUtc { get; }

    private RpolOperationDeadline(TimeSpan operationDuration, TimeSpan cleanupMargin)
    {
        if (operationDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(operationDuration));
        if (cleanupMargin < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(cleanupMargin));
        OperationDeadlineUtc = DateTimeOffset.UtcNow + operationDuration;
        CleanupDeadlineUtc = OperationDeadlineUtc + cleanupMargin;
        _operationCancellation = new CancellationTokenSource(operationDuration);
        _cleanupCancellation = new CancellationTokenSource(operationDuration + cleanupMargin);
    }

    internal static RpolOperationDeadline Create(TimeSpan operationDuration, TimeSpan cleanupMargin)
        => new(operationDuration, cleanupMargin);

    internal TimeSpan RemainingOperation => OperationDeadlineUtc - DateTimeOffset.UtcNow;
    internal void ThrowIfExpired(DateTimeOffset now)
    {
        if (now >= OperationDeadlineUtc) throw new TimeoutException("The RPOL operation exceeded its end-to-end deadline.");
    }

    internal CancellationToken OperationToken => _operationCancellation.Token;
    internal CancellationToken CleanupToken => _cleanupCancellation.Token;

    public void Dispose()
    {
        _operationCancellation.Dispose();
        _cleanupCancellation.Dispose();
    }
}
