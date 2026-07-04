using System.Text.Json;

namespace PlayerAssistant
{
    internal static class RuntimeArtifactUtility
    {
        private const string BadArtifactTimestampFormat = "yyyyMMdd-HHmmss-fff";

        public static bool TryReadText(
            string path,
            string phase,
            out string contents)
        {
            contents = string.Empty;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                using var stream = OpenSharedRead(path);
                using var reader = new StreamReader(stream);
                contents = reader.ReadToEnd();
                return true;
            }
            catch (Exception ex) when (IsRecoverableArtifactException(ex))
            {
                QuarantineAndLog(path, phase, ex);
                return false;
            }
        }

        public static bool TryLoadJson<T>(
            string path,
            string phase,
            out T? value,
            JsonSerializerOptions? options = null)
        {
            value = default;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                using var stream = OpenSharedRead(path);
                value = JsonSerializer.Deserialize<T>(stream, options);
                return value is not null;
            }
            catch (Exception ex) when (IsRecoverableArtifactException(ex))
            {
                if (TryRestoreJsonBackup<T>(path, phase, options, ex, out value))
                {
                    return true;
                }

                QuarantineAndLog(path, phase, ex);
                return false;
            }
        }

        public static async Task<T?> LoadJsonOrDefaultAsync<T>(
            string path,
            string phase,
            CancellationToken cancellationToken = default,
            JsonSerializerOptions? options = null)
        {
            if (!File.Exists(path))
            {
                return default;
            }

            try
            {
                await using var stream = OpenSharedRead(path);
                return await JsonSerializer.DeserializeAsync<T>(
                    stream,
                    options,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsRecoverableArtifactException(ex))
            {
                if (TryRestoreJsonBackup<T>(path, phase, options, ex, out var restoredValue))
                {
                    return restoredValue;
                }

                QuarantineAndLog(path, phase, ex);
                return default;
            }
        }

        public static string QuarantineAndLog(string path, string phase, Exception ex)
        {
            var badPath = Quarantine(path);
            StartupLoggingUtility.Append(
                phase,
                new InvalidOperationException(
                    $"Runtime artifact '{path}' could not be loaded and was moved to '{badPath}'.",
                    ex));
            return badPath;
        }

        private static FileStream OpenSharedRead(string path)
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
        }

        private static bool TryRestoreJsonBackup<T>(
            string path,
            string phase,
            JsonSerializerOptions? options,
            Exception originalException,
            out T? value)
        {
            value = default;
            if (!RuntimeBackupUtility.TryRestoreLatestValidBackup(
                    path,
                    candidatePath => CanDeserializeJson<T>(candidatePath, options),
                    phase,
                    originalException,
                    out _))
            {
                return false;
            }

            try
            {
                using var stream = OpenSharedRead(path);
                value = JsonSerializer.Deserialize<T>(stream, options);
                return value is not null;
            }
            catch (Exception ex) when (IsRecoverableArtifactException(ex))
            {
                StartupLoggingUtility.Append("runtime backup restore verification", ex);
                return false;
            }
        }

        private static bool CanDeserializeJson<T>(string path, JsonSerializerOptions? options)
        {
            try
            {
                using var stream = OpenSharedRead(path);
                return JsonSerializer.Deserialize<T>(stream, options) is not null;
            }
            catch (Exception ex) when (IsRecoverableArtifactException(ex))
            {
                return false;
            }
        }

        private static bool IsRecoverableArtifactException(Exception ex)
        {
            return ex is JsonException
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException;
        }

        private static string Quarantine(string path)
        {
            var directory = Path.GetDirectoryName(path);
            var fileName = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            var timestamp = DateTimeOffset.Now.ToString(BadArtifactTimestampFormat);
            var targetDirectory = string.IsNullOrWhiteSpace(directory) ? "." : directory;
            var badPath = Path.Combine(targetDirectory, $"{fileName}.bad-{timestamp}{extension}");
            for (var suffix = 1; File.Exists(badPath); suffix++)
            {
                badPath = Path.Combine(targetDirectory, $"{fileName}.bad-{timestamp}-{suffix}{extension}");
            }

            try
            {
                File.Move(path, badPath);
                return badPath;
            }
            catch (Exception moveException) when (moveException is IOException or UnauthorizedAccessException)
            {
                StartupLoggingUtility.Append(
                    "runtime artifact quarantine",
                    moveException);
                return path;
            }
        }
    }
}
