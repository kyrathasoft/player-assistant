using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PlayerAssistant;

internal static class RpolExternalProfileCleanup
{
    internal const string ProfilePrefix = "rpol-browser-verification-";
    private const string LockFileName = ".rpol-profile.lock";
    private static readonly TimeSpan CleanupBound = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    internal static RpolExternalProfileLease Acquire(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        ApplyUserOnlyDirectoryAcl(directory);
        var lockPath = Path.Combine(directory, LockFileName);
        var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            ApplyUserOnlyFileAcl(lockPath);
            return new RpolExternalProfileLease(directory, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal static void CleanupProfile(string directory, CancellationToken callerToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                DeleteWithIndependentBound(directory);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
            {
                lastFailure = ex;
                if (attempt < 2)
                {
                    Thread.Sleep(RetryDelay);
                }
            }
        }

        ScheduleRetry(directory, lastFailure!);
        throw new RpolCleanupFailureException(
            "The RPOL external verification profile could not be deleted within its independent cleanup bound; a retry was scheduled.",
            lastFailure!);
    }

    internal static IReadOnlyList<Exception> ScavengeStaleProfiles(
        string rootDirectory,
        DateTimeOffset olderThan)
    {
        if (!Directory.Exists(rootDirectory)) return [];
        var errors = new List<Exception>();
        foreach (var directory in Directory.EnumerateDirectories(rootDirectory, ProfilePrefix + "*"))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) >= olderThan.UtcDateTime || IsActiveOrLocked(directory))
                {
                    continue;
                }

                DeleteWithIndependentBound(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
            {
                ScheduleRetry(directory, ex);
                errors.Add(new InvalidOperationException(
                    $"RPOL stale external verification profile cleanup failed for '{directory}'; a retry was scheduled.",
                    ex));
            }
        }

        return errors;
    }

    internal static bool IsActiveOrLocked(string directory)
    {
        foreach (var fileName in new[] { LockFileName, "SingletonLock", "SingletonCookie", "SingletonSocket" })
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) continue;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    private static void DeleteWithIndependentBound(string directory)
    {
        using var cleanupCancellation = new CancellationTokenSource(CleanupBound);
        RpolCleanupUtility.DeleteDirectoryBounded(directory, CleanupBound, cleanupCancellation.Token);
    }

    private static void ScheduleRetry(string directory, Exception firstFailure)
    {
        var retryTask = Task.Run(async () =>
        {
            Exception lastFailure = firstFailure;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    DeleteWithIndependentBound(directory);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
                {
                    lastFailure = ex;
                    await Task.Delay(RetryDelay).ConfigureAwait(false);
                }
            }

            throw new RpolCleanupFailureException(
                $"The RPOL external verification profile cleanup retry owner exhausted its bounded attempts for '{directory}'.",
                lastFailure);
        });
        RpolCleanupUtility.TrackLateTask(retryTask);
    }

    private static void ApplyUserOnlyDirectoryAcl(string directory)
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

    private static void ApplyUserOnlyFileAcl(string path)
    {
        var sid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            sid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }
}

internal sealed class RpolExternalProfileLease : IDisposable
{
    private readonly string _directory;
    private FileStream? _lockStream;
    private int _disposed;

    internal RpolExternalProfileLease(string directory, FileStream lockStream)
    {
        _directory = directory;
        _lockStream = lockStream;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lockStream?.Dispose();
        _lockStream = null;
        RpolExternalProfileCleanup.CleanupProfile(_directory);
    }
}