using System.Diagnostics;

namespace PlayerAssistant.Tests;

internal static partial class TestCases
{
    internal static void RepositoryRootDiscoveryHandlesCiOutputLayout()
    {
        var expectedRoot = RepositoryRootDiscovery.Find(AppContext.BaseDirectory);
        var ciLikeOutputDirectory = Path.Combine(
            expectedRoot,
            "PlayerAssistant.Tests",
            "bin",
            "Release",
            "net10.0-windows");
        var repositoryRoot = RepositoryRootDiscovery.Find(ciLikeOutputDirectory);
        AssertEqual(
            expectedRoot,
            repositoryRoot,
            "Repository root discovery must use repository markers rather than a fixed output depth.");
    }

    internal static void LoginThrottlingRejectsMalformedSourceAtLoginBoundary()
    {
        var repositoryRoot = RepositoryRootDiscovery.Find(AppContext.BaseDirectory);
        var script = Path.Combine(repositoryRoot, "web-deploy", "tests", "login-hardening-tests.php");
        var startInfo = new ProcessStartInfo("php")
        {
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(script);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the PHP login boundary regression suite.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        AssertEqual(0, process.ExitCode, $"PHP login boundary suite failed: {output}{error}");
    }
}
