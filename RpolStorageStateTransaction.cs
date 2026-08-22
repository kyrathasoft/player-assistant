namespace PlayerAssistant;

internal static class RpolStorageStateTransaction
{
    internal static bool TryPromote(
        string candidate,
        Func<string?> readActive,
        Action<string> writeActive,
        out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate);
        ArgumentNullException.ThrowIfNull(readActive);
        ArgumentNullException.ThrowIfNull(writeActive);

        var previousActive = readActive();
        try
        {
            writeActive(candidate);
            var readBack = readActive();
            if (!string.Equals(readBack, candidate, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("RPOL candidate state readback did not match the candidate.");
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            try
            {
                if (previousActive is null)
                {
                    return FailWithoutActiveRestore(ex, out error);
                }

                writeActive(previousActive);
                if (!string.Equals(readActive(), previousActive, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("RPOL active state rollback readback did not match the prior state.");
                }
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                error = $"{ex.Message}; rollback failed: {rollbackException.Message}";
                return false;
            }

            error = ex.Message;
            return false;
        }
    }

    private static bool FailWithoutActiveRestore(Exception exception, out string? error)
    {
        error = exception.Message;
        return false;
    }
}
