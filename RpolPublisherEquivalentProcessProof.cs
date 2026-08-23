using System.Diagnostics;

namespace PlayerAssistant;

internal sealed record RpolPublisherEquivalentProcessProofMetadata(
    string Purpose,
    string ProcessBoundary,
    bool UsesPublisherContextPath);

internal static class RpolPublisherEquivalentProcessProof
{
    internal static string[] CreateChildArguments(string? cdpEndpoint = null)
    {
        var arguments = new List<string> { "--rpol-state-proof" };
        if (!string.IsNullOrWhiteSpace(cdpEndpoint))
        {
            arguments.Add("--rpol-cdp-endpoint");
            arguments.Add(cdpEndpoint);
        }
        return arguments.ToArray();
    }

    internal static bool IsSeparateProcessProof(RpolPublisherEquivalentProcessProofMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return string.Equals(metadata.ProcessBoundary, "separate-process", StringComparison.Ordinal)
            && metadata.UsesPublisherContextPath;
    }

    internal static async Task ProveCandidateAsync(
        CancellationToken cancellationToken,
        TimeSpan timeout,
        string? cdpEndpoint = null)
    {
        var executablePath = Path.Combine(AppContext.BaseDirectory, "player-assistant.exe");
        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException("The publisher-equivalent RPOL proof executable is unavailable.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in CreateChildArguments(cdpEndpoint))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var result = await RpolProcessSupervisor.RunAsync(startInfo, timeout, cancellationToken);
        if (result.TimedOut)
        {
            throw new TimeoutException("The separate RPOL publisher-equivalent proof process timed out.");
        }

        if (result.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("The separate RPOL publisher-equivalent proof process rejected the candidate state.");
        }

        if (!result.ProcessTreeTerminated || result.CleanupErrors.Count > 0)
        {
            throw new InvalidOperationException("The separate RPOL publisher-equivalent proof process did not clean up completely.");
        }
    }
}
