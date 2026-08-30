using PlayerAssistant;
using System.Threading;

namespace PlayerAssistant.Tests;

internal static partial class TestCases
{
    internal static void BackgroundTaskSupervisorCancelsOnePhase()
    {
        using var supervisor = new BackgroundTaskSupervisor();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AssertTrue(
            supervisor.TryStart("cancellable phase", async cancellationToken =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancelled.SetResult();
                }
            }),
            "expected cancellable phase to start");
        WaitForCondition(() => supervisor.IsRunning("cancellable phase"), "cancellable phase did not start");
        AssertTrue(supervisor.Cancel("cancellable phase"), "expected phase cancellation to be accepted");
        cancelled.Task.Wait(TimeSpan.FromSeconds(2));
        WaitForCondition(() => !supervisor.IsRunning("cancellable phase"), "cancellable phase did not stop");
        AssertFalse(supervisor.Cancel("cancellable phase"), "completed phase should not cancel twice");
    }
}
