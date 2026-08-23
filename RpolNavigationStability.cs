using System.Diagnostics;

namespace PlayerAssistant;

internal sealed record RpolNavigationSnapshot(
    Uri? Url,
    string DomIdentity,
    string Html);

internal static class RpolNavigationStability
{
    internal static async Task<RpolNavigationSnapshot> WaitForStableAsync(
        Func<CancellationToken, Task<RpolNavigationSnapshot>> observeAsync,
        TimeSpan quietPeriod,
        TimeSpan maximumWait,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observeAsync);
        if (quietPeriod <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(quietPeriod));
        if (maximumWait < quietPeriod) throw new ArgumentOutOfRangeException(nameof(maximumWait));
        if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));

        var stopwatch = Stopwatch.StartNew();
        RpolNavigationSnapshot? previous = null;
        var stableSince = TimeSpan.Zero;
        while (stopwatch.Elapsed < maximumWait)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await observeAsync(cancellationToken).ConfigureAwait(false);
            if (previous is null || !AreEquivalent(previous, current))
            {
                previous = current;
                stableSince = stopwatch.Elapsed;
            }
            var remaining = maximumWait - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            await Task.Delay(remaining < pollInterval ? remaining : pollInterval, cancellationToken).ConfigureAwait(false);
        }

        if (previous is not null && stopwatch.Elapsed - stableSince >= quietPeriod)
        {
            return previous;
        }

        throw new TimeoutException($"RPOL navigation did not remain stable for {quietPeriod.TotalMilliseconds:0} ms within the {maximumWait.TotalSeconds:0.0} second bound.");
    }

    internal static bool AreEquivalent(RpolNavigationSnapshot left, RpolNavigationSnapshot right)
    {
        return Equals(left.Url, right.Url)
            && string.Equals(left.DomIdentity, right.DomIdentity, StringComparison.Ordinal)
            && string.Equals(left.Html, right.Html, StringComparison.Ordinal);
    }
}
