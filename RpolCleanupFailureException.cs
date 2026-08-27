namespace PlayerAssistant;

internal sealed class RpolCleanupFailureException : InvalidOperationException
{
    internal RpolCleanupFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
