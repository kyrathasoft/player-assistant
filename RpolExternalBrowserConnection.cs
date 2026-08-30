using System.ComponentModel;
using System.Diagnostics;
using System.Net;

namespace PlayerAssistant;

/// <summary>Owns the external browser CDP rendezvous. The browser chooses its port;
/// no released port is ever reused by the verifier.</summary>
internal static class RpolExternalBrowserConnection
{
    internal static string[] CreateLaunchArguments(string profileDirectory, string noticePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(noticePath);
        return [
            "--remote-debugging-port=0",
            "--remote-debugging-address=127.0.0.1",
            $"--user-data-dir={profileDirectory}",
            "--no-first-run",
            "--new-window",
            new Uri(noticePath).AbsoluteUri,
            RpolProtectedResourceUtility.CanonicalDiceRollerProbe.Uri.AbsoluteUri];
    }

    internal static Uri ReadEndpoint(string profileDirectory)
    {
        var path = Path.Combine(profileDirectory, "DevToolsActivePort");
        if (!File.Exists(path)) throw new InvalidDataException("The external browser did not publish its CDP rendezvous file.");
        var lines = File.ReadAllLines(path);
        if (lines.Length < 1 || !int.TryParse(lines[0], out var port) || port is < 1 or > 65535)
            throw new InvalidDataException("The external browser CDP rendezvous port is invalid.");
        return ValidateEndpoint(new Uri($"http://127.0.0.1:{port}/"));
    }

    internal static Uri ValidateEndpoint(Uri endpoint)
    {
        if (!IsLoopbackEndpoint(endpoint) || endpoint.AbsolutePath != "/")
            throw new InvalidDataException("The RPOL CDP endpoint must be an HTTP loopback root endpoint.");
        return endpoint;
    }

    internal static bool IsLoopbackEndpoint(Uri endpoint)
        => endpoint.Scheme == Uri.UriSchemeHttp
            && endpoint.Port is > 0 and <= 65535
            && (IPAddress.TryParse(endpoint.Host, out var address) && IPAddress.IsLoopback(address));

    internal static void EnsureAuthorizedProcess(Process process, string expectedExecutablePath, string profileDirectory)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        if (process.HasExited) throw new InvalidOperationException("The external RPOL browser exited before authorization.");
        string actualPath;
        try { actualPath = process.MainModule?.FileName ?? string.Empty; }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        { throw new InvalidOperationException("The external RPOL browser process identity could not be verified.", ex); }
        if (!string.Equals(Path.GetFullPath(actualPath), Path.GetFullPath(expectedExecutablePath), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The RPOL CDP process identity did not match the launched browser executable.");
        if (!Path.GetFullPath(profileDirectory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The RPOL CDP profile must be under the user temporary directory.");
    }
}
