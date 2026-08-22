namespace PlayerAssistant;

internal sealed class RpolOperationDeadline : IDisposable
{
    private readonly CancellationTokenSource _operationCancellation;
    private readonly CancellationTokenSource _cleanupCancellation;

    private RpolOperationDeadline(TimeSpan operationDuration, TimeSpan cleanupMargin)
    {
        if (operationDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(operationDuration));
        if (cleanupMargin < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(cleanupMargin));
        _operationCancellation = new CancellationTokenSource(operationDuration);
        _cleanupCancellation = new CancellationTokenSource(operationDuration + cleanupMargin);
    }

    internal static RpolOperationDeadline Create(TimeSpan operationDuration, TimeSpan cleanupMargin)
        => new(operationDuration, cleanupMargin);

    internal CancellationToken OperationToken => _operationCancellation.Token;
    internal CancellationToken CleanupToken => _cleanupCancellation.Token;

    public void Dispose()
    {
        _operationCancellation.Dispose();
        _cleanupCancellation.Dispose();
    }
}
