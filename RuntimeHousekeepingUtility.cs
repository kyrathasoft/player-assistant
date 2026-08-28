namespace PlayerAssistant
{
    internal static class RuntimeHousekeepingUtility
    {
        private const string TempDirectoryName = "temp";
        private const string StartupLogArchiveFileName = "startup-errors.log.1";
        private const int AtomicGuidLength = 32;

        public static RuntimeHousekeepingReport Clean(
            string runtimeDirectory,
            DateTimeOffset? now = null,
            RuntimeHousekeepingOptions? options = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);

            var resolvedOptions = options ?? RuntimeHousekeepingOptions.Default;
            var resolvedNow = now ?? DateTimeOffset.Now;
            var rootDirectory = Path.GetFullPath(runtimeDirectory);
            var report = new RuntimeHousekeepingReport();

            CleanTempDirectory(rootDirectory, resolvedNow, resolvedOptions, report);
            CleanOrphanedAtomicFiles(rootDirectory, resolvedNow, resolvedOptions, report);
            CleanQuarantinedJsonFiles(rootDirectory, resolvedNow, resolvedOptions, report);
            CleanRuntimeBackupFiles(rootDirectory, resolvedNow, resolvedOptions, report);
            RotateStartupLog(rootDirectory, resolvedNow, resolvedOptions, report);

            return report;
        }

        public static void CleanCurrentRuntimeAndLog()
        {
            var report = Clean(RuntimePathUtility.WritableRuntimeDirectory);
            if (report.HasActivity)
            {
                StartupLoggingUtility.Append("runtime housekeeping", report.ToLogMessage());
            }
        }

        private static void CleanTempDirectory(
            string rootDirectory,
            DateTimeOffset now,
            RuntimeHousekeepingOptions options,
            RuntimeHousekeepingReport report)
        {
            var tempDirectory = Path.Combine(rootDirectory, TempDirectoryName);
            if (!Directory.Exists(tempDirectory))
            {
                return;
            }

            foreach (var path in EnumerateFilesSafely(tempDirectory, "*"))
            {
                DeleteIfStale(path, now, options.StaleTempFileAge, report);
            }

            RemoveEmptyDirectories(tempDirectory);
        }

        private static void CleanOrphanedAtomicFiles(
            string rootDirectory,
            DateTimeOffset now,
            RuntimeHousekeepingOptions options,
            RuntimeHousekeepingReport report)
        {
            foreach (var path in EnumerateFilesSafely(rootDirectory, "*"))
            {
                if (IsAtomicTempFile(path))
                {
                    DeleteIfStale(path, now, options.OrphanedAtomicFileAge, report);
                }
            }
        }

        private static void CleanQuarantinedJsonFiles(
            string rootDirectory,
            DateTimeOffset now,
            RuntimeHousekeepingOptions options,
            RuntimeHousekeepingReport report)
        {
            foreach (var path in EnumerateFilesSafely(rootDirectory, "*.json"))
            {
                var fileName = Path.GetFileName(path);
                if (fileName.Contains(".bad-", StringComparison.OrdinalIgnoreCase))
                {
                    DeleteIfStale(path, now, options.QuarantinedJsonRetention, report);
                }
            }
        }

        private static void CleanRuntimeBackupFiles(
            string rootDirectory,
            DateTimeOffset now,
            RuntimeHousekeepingOptions options,
            RuntimeHousekeepingReport report)
        {
            foreach (var path in EnumerateFilesSafely(rootDirectory, "*"))
            {
                if (RuntimeBackupUtility.IsBackupFile(path))
                {
                    DeleteIfStale(path, now, options.RuntimeBackupRetention, report);
                }
            }
        }

        private static void RotateStartupLog(
            string rootDirectory,
            DateTimeOffset now,
            RuntimeHousekeepingOptions options,
            RuntimeHousekeepingReport report)
        {
            var logPath = Path.Combine(rootDirectory, StartupLoggingUtility.LogFileName);
            if (!File.Exists(logPath))
            {
                return;
            }

            try
            {
                var logInfo = new FileInfo(logPath);
                if (logInfo.Length <= options.MaxStartupLogBytes)
                {
                    return;
                }

                var archivePath = Path.Combine(rootDirectory, StartupLogArchiveFileName);
                File.Move(logPath, archivePath, overwrite: true);
                File.WriteAllText(
                    logPath,
                    StartupLoggingUtility.FormatLogEntry(
                        "runtime housekeeping",
                        $"Startup log exceeded {options.MaxStartupLogBytes} bytes and was rotated to {StartupLogArchiveFileName}."));

                report.StartupLogRotated = true;
                report.StartupLogBytesArchived = logInfo.Length;
            }
            catch (Exception ex) when (IsRecoverableFileException(ex))
            {
                report.SkippedFileCount++;
            }
        }

        private static IEnumerable<string> EnumerateFilesSafely(string directoryPath, string searchPattern)
        {
            try
            {
                return Directory.EnumerateFiles(directoryPath, searchPattern, SearchOption.AllDirectories).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                return [];
            }
        }

        private static void DeleteIfStale(
            string path,
            DateTimeOffset now,
            TimeSpan minimumAge,
            RuntimeHousekeepingReport report)
        {
            try
            {
                var fileInfo = new FileInfo(path);
                if (!fileInfo.Exists)
                {
                    return;
                }

                var lastWriteTime = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);
                if (now - lastWriteTime < minimumAge)
                {
                    return;
                }

                var length = fileInfo.Length;
                File.Delete(path);
                report.RemovedFileCount++;
                report.ReclaimedBytes += length;
            }
            catch (Exception ex) when (IsRecoverableFileException(ex))
            {
                report.SkippedFileCount++;
            }
        }

        private static bool IsAtomicTempFile(string path)
        {
            var extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".tmp", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".source", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
            var lastDotIndex = fileNameWithoutExtension.LastIndexOf('.');
            if (lastDotIndex < 0 || lastDotIndex == fileNameWithoutExtension.Length - 1)
            {
                return false;
            }

            var candidate = fileNameWithoutExtension[(lastDotIndex + 1)..];
            return candidate.Length == AtomicGuidLength
                && candidate.All(Uri.IsHexDigit);
        }

        private static void RemoveEmptyDirectories(string rootDirectory)
        {
            try
            {
                var directories = Directory
                    .EnumerateDirectories(rootDirectory, "*", SearchOption.AllDirectories)
                    .OrderByDescending(path => path.Length);

                foreach (var directory in directories)
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        {
                            Directory.Delete(directory);
                        }
                    }
                    catch (Exception ex) when (IsRecoverableFileException(ex) || ex is DirectoryNotFoundException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
            }
        }

        private static bool IsRecoverableFileException(Exception ex)
        {
            return ex is IOException
                or UnauthorizedAccessException
                or DirectoryNotFoundException;
        }
    }

    internal sealed class RuntimeHousekeepingOptions
    {
        public static RuntimeHousekeepingOptions Default { get; } = new();

        public TimeSpan StaleTempFileAge { get; init; } = TimeSpan.FromDays(1);

        public TimeSpan OrphanedAtomicFileAge { get; init; } = TimeSpan.FromDays(1);

        public TimeSpan QuarantinedJsonRetention { get; init; } = TimeSpan.FromDays(14);

        public TimeSpan RuntimeBackupRetention { get; init; } = TimeSpan.FromDays(30);

        public long MaxStartupLogBytes { get; init; } = 1_048_576;
    }

    internal sealed class RuntimeHousekeepingReport
    {
        public int RemovedFileCount { get; set; }

        public long ReclaimedBytes { get; set; }

        public int SkippedFileCount { get; set; }

        public bool StartupLogRotated { get; set; }

        public long StartupLogBytesArchived { get; set; }

        public bool HasActivity =>
            RemovedFileCount > 0
            || SkippedFileCount > 0
            || StartupLogRotated;

        public string ToLogMessage()
        {
            var rotationMessage = StartupLogRotated
                ? $"; rotated startup log ({StartupLogBytesArchived} bytes archived)"
                : string.Empty;

            return $"Removed {RemovedFileCount} stale runtime file(s), reclaimed {ReclaimedBytes} byte(s), skipped {SkippedFileCount} locked/inaccessible file(s){rotationMessage}.";
        }
    }
}
