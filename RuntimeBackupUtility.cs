namespace PlayerAssistant
{
    internal static class RuntimeBackupUtility
    {
        private const string BackupTimestampFormat = "yyyyMMdd-HHmmss-fff";
        private const int DefaultMaxBackupsPerFile = 5;

        public static string? CreateBackupBeforeWrite(string destinationPath, int maxBackupsPerFile = DefaultMaxBackupsPerFile)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

            if (!File.Exists(destinationPath))
            {
                return null;
            }

            var backupPath = CreateUniqueBackupPath(destinationPath);
            File.Copy(destinationPath, backupPath, overwrite: false);
            File.SetAttributes(backupPath, File.GetAttributes(backupPath) & ~FileAttributes.ReadOnly);
            PruneBackups(destinationPath, maxBackupsPerFile);
            return backupPath;
        }

        public static bool TryRestoreLatestValidBackup(
            string destinationPath,
            Func<string, bool> isValidBackup,
            string phase,
            Exception originalException,
            out string restoredBackupPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
            ArgumentNullException.ThrowIfNull(isValidBackup);

            restoredBackupPath = string.Empty;
            foreach (var backupPath in EnumerateBackups(destinationPath))
            {
                try
                {
                    if (!isValidBackup(backupPath))
                    {
                        continue;
                    }

                    var destinationDirectory = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    File.Copy(backupPath, destinationPath, overwrite: true);
                    restoredBackupPath = backupPath;
                    StartupLoggingUtility.Append(
                        phase,
                        $"Restored runtime artifact '{destinationPath}' from backup '{backupPath}' after load failure: {originalException.GetType().Name}: {originalException.Message}");
                    return true;
                }
                catch (Exception ex) when (IsRecoverableFileException(ex))
                {
                    StartupLoggingUtility.Append("runtime backup restore", ex);
                }
            }

            return false;
        }

        public static void PruneBackups(string destinationPath, int maxBackupsPerFile = DefaultMaxBackupsPerFile)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

            if (maxBackupsPerFile < 1)
            {
                maxBackupsPerFile = 1;
            }

            foreach (var staleBackup in EnumerateBackups(destinationPath).Skip(maxBackupsPerFile))
            {
                try
                {
                    File.Delete(staleBackup);
                }
                catch (Exception ex) when (IsRecoverableFileException(ex))
                {
                    StartupLoggingUtility.Append("runtime backup retention", ex);
                }
            }
        }

        public static bool IsBackupFile(string path)
        {
            var fileName = Path.GetFileName(path);
            return fileName.Contains(".bak-", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateBackups(string destinationPath)
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return [];
            }

            var fileName = Path.GetFileNameWithoutExtension(destinationPath);
            var extension = Path.GetExtension(destinationPath);
            var searchPattern = $"{fileName}.bak-*{extension}";
            return Directory
                .EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenByDescending(Path.GetFileName)
                .ToArray();
        }

        private static string CreateUniqueBackupPath(string destinationPath)
        {
            var directory = Path.GetDirectoryName(destinationPath);
            var fileName = Path.GetFileNameWithoutExtension(destinationPath);
            var extension = Path.GetExtension(destinationPath);
            var targetDirectory = string.IsNullOrWhiteSpace(directory) ? "." : directory;
            var timestamp = DateTimeOffset.Now.ToString(BackupTimestampFormat);
            var backupPath = Path.Combine(targetDirectory, $"{fileName}.bak-{timestamp}{extension}");
            for (var suffix = 1; File.Exists(backupPath); suffix++)
            {
                backupPath = Path.Combine(targetDirectory, $"{fileName}.bak-{timestamp}-{suffix}{extension}");
            }

            return backupPath;
        }

        private static bool IsRecoverableFileException(Exception ex)
        {
            return ex is IOException
                or UnauthorizedAccessException
                or DirectoryNotFoundException;
        }
    }
}
