using System.ComponentModel;
using System.Diagnostics;

namespace PlayerAssistant;

internal sealed record RpolProcessSupervisionResult(
    int ExitCode,
    bool TimedOut,
    bool Cancelled,
    bool ProcessTreeTerminated,
    IReadOnlyList<string> CleanupErrors);

internal static class RpolProcessSupervisor
{
    internal static async Task<RpolProcessSupervisionResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The RPOL child process could not be started.");
        var startedAt = DateTimeOffset.UtcNow;
        while (!process.HasExited)
        {
            if (cancellationToken.IsCancellationRequested || DateTimeOffset.UtcNow - startedAt >= timeout)
            {
                var cancelled = cancellationToken.IsCancellationRequested;
                var cleanupErrors = new List<string>();
                var terminated = false;
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    terminated = process.WaitForExit(5000);
                    if (!terminated)
                    {
                        cleanupErrors.Add("Timed-out RPOL child process did not exit within the cleanup bound.");
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    cleanupErrors.Add("RPOL child process cleanup failed: " + ex.Message);
                }

                return new RpolProcessSupervisionResult(
                    ExitCode: process.HasExited ? process.ExitCode : -1,
                    TimedOut: !cancelled,
                    Cancelled: cancelled,
                    ProcessTreeTerminated: terminated,
                    CleanupErrors: cleanupErrors);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The next loop iteration performs the bounded process-tree termination and wait.
            }
        }

        return new RpolProcessSupervisionResult(process.ExitCode, false, false, true, []);
    }
}
