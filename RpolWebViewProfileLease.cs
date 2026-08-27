using System.Security.AccessControl;
using System.Security.Principal;

namespace PlayerAssistant;

internal sealed class RpolWebViewProfileLease : IDisposable
{
    private readonly string _directory;
    private readonly FileStream _lockStream;
    private bool _disposed;

    private RpolWebViewProfileLease(string directory, FileStream lockStream)
    {
        _directory = directory;
        _lockStream = lockStream;
    }

    internal static RpolWebViewProfileLease Acquire(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        ApplyUserOnlyDirectoryAcl(directory);
        var lockPath = Path.Combine(directory, ".profile.lock");
        var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            RpolSecureStorageStateFile.ApplyUserOnlyAcl(lockPath);
            ScavengeCrashLeftoverContents(directory, lockPath);
            return new RpolWebViewProfileLease(directory, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal static void ApplyUserOnlyDirectoryAcl(string directory)
    {
        var sid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            sid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(security);
    }

    private static void ScavengeCrashLeftoverContents(string directory, string lockPath)
    {
        var errors = new List<Exception>();
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(lockPath), StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (Directory.Exists(path)) RpolCleanupUtility.DeleteDirectoryBounded(path, TimeSpan.FromSeconds(10), CancellationToken.None);
                else File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(new InvalidOperationException($"RPOL WebView crash-leftover cleanup failed for '{path}'.", ex));
            }
        }
        if (errors.Count > 0) throw new AggregateException("RPOL WebView profile scavenging failed.", errors);
    }

    public void Dispose()
    {
        Dispose(CancellationToken.None);
    }

    internal void Dispose(CancellationToken cleanupToken)
    {
        if (_disposed) return;
        Exception? streamError = null;
        Exception? directoryError = null;
        try { _lockStream.Dispose(); }
        catch (Exception ex) { streamError = ex; }
        try
        {
            RpolCleanupUtility.DeleteDirectoryBounded(_directory, TimeSpan.FromSeconds(10), cleanupToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or TimeoutException)
        {
            directoryError = ex;
        }
        var errors = new[] { streamError, directoryError }.OfType<Exception>().ToArray();
        if (errors.Length > 0)
        {
            throw new AggregateException("The RPOL WebView authenticated profile could not be reset and cleaned up.", errors);
        }

        _disposed = true;
    }
}

internal sealed class RpolWebViewLifetime : IDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private bool _disposed;

    private RpolWebViewLifetime(CancellationTokenSource cancellation) => _cancellation = cancellation;

    internal static RpolWebViewLifetime Create(TimeSpan maxWait, CancellationToken callerToken)
    {
        if (maxWait <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxWait));
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cancellation.CancelAfter(maxWait);
        return new RpolWebViewLifetime(cancellation);
    }

    internal CancellationToken Token => _cancellation.Token;
    internal bool IsAlive => !_disposed && !_cancellation.IsCancellationRequested;

    internal void ThrowIfNotAlive()
    {
        if (!IsAlive) throw new OperationCanceledException(_cancellation.Token);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
