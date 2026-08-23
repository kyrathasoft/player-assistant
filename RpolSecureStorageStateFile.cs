using System.Runtime.ExceptionServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace PlayerAssistant;

internal static class RpolSecureStorageStateFile
{
    internal static void WriteAndRun(string path, string content, Action<string> operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(operation);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Exception? operationFailure = null;
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                ApplyUserOnlyAcl(path);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }

            operation(path);
        }
        catch (Exception ex)
        {
            operationFailure = ex;
        }

        try
        {
            DeleteRequired(path);
        }
        catch (Exception cleanupFailure)
        {
            if (operationFailure is not null)
            {
                throw new AggregateException("RPOL temporary storage-state operation and cleanup both failed.", operationFailure, cleanupFailure);
            }

            throw;
        }

        if (operationFailure is not null)
        {
            ExceptionDispatchInfo.Capture(operationFailure).Throw();
        }
    }

    internal static void Scavenge(string directory, DateTimeOffset olderThan)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var cleanupErrors = new List<Exception>();
        foreach (var path in Directory.EnumerateFiles(directory, "rpol-storage-state-*.json"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < olderThan.UtcDateTime)
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                cleanupErrors.Add(new InvalidOperationException($"RPOL temporary storage-state cleanup failed for '{path}'.", ex));
            }
        }

        if (cleanupErrors.Count > 0)
        {
            throw new AggregateException("RPOL temporary storage-state scavenging failed.", cleanupErrors);
        }
    }

    internal static void ApplyUserOnlyAcl(string path)
    {
        var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            sid,
            FileSystemRights.FullControl,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    private static void DeleteRequired(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
