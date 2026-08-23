using System.Security.Cryptography;
using System.Text;

namespace PlayerAssistant;

internal sealed record RpolActiveStatePointer(
    int Version,
    string ActiveSlot,
    string? PreviousVerifiedSlot,
    bool Verified,
    string ContentHash);

internal static class RpolVersionedStateTransaction
{
    internal static bool TryPromote(
        string candidate,
        Func<RpolActiveStatePointer?> readPointer,
        Func<string, string?> readSlot,
        Action<string, string> writeSlot,
        Action<RpolActiveStatePointer> writePointer,
        out RpolActiveStatePointer? previousPointer,
        out string? error,
        Action? clearPointer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate);
        ArgumentNullException.ThrowIfNull(readPointer);
        ArgumentNullException.ThrowIfNull(readSlot);
        ArgumentNullException.ThrowIfNull(writeSlot);
        ArgumentNullException.ThrowIfNull(writePointer);

        previousPointer = readPointer();
        var previousVerifiedSlot = previousPointer?.Verified == true
            ? previousPointer.ActiveSlot
            : previousPointer?.PreviousVerifiedSlot;
        var nextSlot = string.Equals(previousVerifiedSlot, "A", StringComparison.Ordinal)
            ? "B"
            : "A";
        var nextVersion = Math.Max(previousPointer?.Version ?? 0, 0) + 1;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(candidate)));
        var pendingPointer = new RpolActiveStatePointer(
            nextVersion,
            nextSlot,
            previousVerifiedSlot,
            Verified: false,
            hash);

        try
        {
            writeSlot(nextSlot, candidate);
            if (!string.Equals(readSlot(nextSlot), candidate, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("RPOL versioned candidate readback did not match.");
            }

            writePointer(pendingPointer);
            if (readPointer() is not { } readBack
                || readBack.Version != pendingPointer.Version
                || !string.Equals(readBack.ActiveSlot, pendingPointer.ActiveSlot, StringComparison.Ordinal)
                || readBack.Verified)
            {
                throw new InvalidOperationException("RPOL pending active pointer readback did not match.");
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            try
            {
                if (previousPointer is not null)
                {
                    writePointer(previousPointer);
                }
                else if (clearPointer is not null)
                {
                    clearPointer();
                }
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                error = $"{ex.Message}; active pointer rollback failed: {rollbackException.Message}";
                return false;
            }

            error = ex.Message;
            return false;
        }
    }

    internal static RpolActiveStatePointer MarkVerified(
        RpolActiveStatePointer pending,
        Func<RpolActiveStatePointer?> readPointer,
        Action<RpolActiveStatePointer> writePointer)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(readPointer);
        ArgumentNullException.ThrowIfNull(writePointer);
        if (pending.Verified || readPointer() is not { } current
            || current.Version != pending.Version
            || !string.Equals(current.ActiveSlot, pending.ActiveSlot, StringComparison.Ordinal)
            || current.Verified)
        {
            throw new InvalidOperationException("The pending RPOL active pointer is not the expected version.");
        }

        var verified = pending with { Verified = true };
        writePointer(verified);
        if (readPointer() is not { } readBack || !readBack.Verified || readBack.Version != verified.Version)
        {
            throw new InvalidOperationException("RPOL verified active pointer readback did not match.");
        }

        return verified;
    }

    internal static bool TryReadActiveState(
        RpolActiveStatePointer pointer,
        Func<string, string?> readSlot,
        bool allowPending,
        out string? state)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        ArgumentNullException.ThrowIfNull(readSlot);
        state = null;
        var slot = pointer.Verified || allowPending ? pointer.ActiveSlot : pointer.PreviousVerifiedSlot;
        if (slot is null || readSlot(slot) is not { } candidate) return false;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(candidate)));
        if (pointer.Verified && !string.Equals(hash, pointer.ContentHash, StringComparison.OrdinalIgnoreCase)) return false;
        state = candidate;
        return true;
    }

    internal static bool TryReadVerifiedState(
        RpolActiveStatePointer pointer,
        Func<string, string?> readSlot,
        out string? state)
    {
        return TryReadActiveState(pointer, readSlot, allowPending: false, out state);
    }

    // The normal publisher loader must fail closed at the pending-pointer crash
    // boundary.  Candidate proof uses the explicit candidate slot API instead.
    internal static bool TryReadNormalActiveState(
        RpolActiveStatePointer pointer,
        Func<string, string?> readSlot,
        out string? state)
    {
        return TryReadActiveState(pointer, readSlot, allowPending: false, out state);
    }
}
