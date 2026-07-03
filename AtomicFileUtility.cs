namespace PlayerAssistant
{
    internal static class AtomicFileUtility
    {
        private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(250);
        private const int ReplaceRetryCount = 8;

        public static async Task WriteAllTextAsync(
            string destinationPath,
            string contents,
            CancellationToken cancellationToken = default)
        {
            var tempPath = CreateTempPath(destinationPath);
            try
            {
                await File.WriteAllTextAsync(tempPath, contents, cancellationToken);
                await PromoteTempFileAsync(tempPath, destinationPath, cancellationToken);
            }
            finally
            {
                DeleteTempFileIfPresent(tempPath);
            }
        }

        public static void WriteAllText(string destinationPath, string contents)
        {
            var tempPath = CreateTempPath(destinationPath);
            try
            {
                File.WriteAllText(tempPath, contents);
                PromoteTempFileAsync(tempPath, destinationPath).GetAwaiter().GetResult();
            }
            finally
            {
                DeleteTempFileIfPresent(tempPath);
            }
        }

        public static async Task WriteAllLinesAsync(
            string destinationPath,
            IEnumerable<string> lines,
            CancellationToken cancellationToken = default)
        {
            var tempPath = CreateTempPath(destinationPath);
            try
            {
                await File.WriteAllLinesAsync(tempPath, lines, cancellationToken);
                await PromoteTempFileAsync(tempPath, destinationPath, cancellationToken);
            }
            finally
            {
                DeleteTempFileIfPresent(tempPath);
            }
        }

        public static async Task WriteAllBytesAsync(
            string destinationPath,
            byte[] bytes,
            CancellationToken cancellationToken = default)
        {
            var tempPath = CreateTempPath(destinationPath);
            try
            {
                await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
                await PromoteTempFileAsync(tempPath, destinationPath, cancellationToken);
            }
            finally
            {
                DeleteTempFileIfPresent(tempPath);
            }
        }

        public static async Task WriteFileAsync(
            string destinationPath,
            Func<FileStream, Task> writeContentAsync,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(writeContentAsync);

            var tempPath = CreateTempPath(destinationPath);
            try
            {
                await using (var outputStream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true))
                {
                    await writeContentAsync(outputStream);
                }

                await PromoteTempFileAsync(tempPath, destinationPath, cancellationToken);
            }
            finally
            {
                DeleteTempFileIfPresent(tempPath);
            }
        }

        public static async Task<bool> PromoteTempFileIfChangedAsync(
            string tempPath,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tempPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

            if (!File.Exists(tempPath))
            {
                return false;
            }

            if (File.Exists(destinationPath) && FilesHaveSameContent(tempPath, destinationPath))
            {
                File.Delete(tempPath);
                return false;
            }

            await PromoteTempFileAsync(tempPath, destinationPath, cancellationToken);
            return true;
        }

        public static async Task PromoteTempFileAsync(
            string tempPath,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tempPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

            if (!File.Exists(tempPath))
            {
                throw new FileNotFoundException("Temporary file was not found.", tempPath);
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(destinationPath))
                    {
                        File.Replace(tempPath, destinationPath, null, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempPath, destinationPath);
                    }

                    return;
                }
                catch (UnauthorizedAccessException) when (attempt < ReplaceRetryCount)
                {
                }
                catch (IOException) when (attempt < ReplaceRetryCount)
                {
                }

                if (!File.Exists(tempPath))
                {
                    throw new IOException($"Temporary file '{tempPath}' disappeared before it could replace '{destinationPath}'.");
                }

                await Task.Delay(ReplaceRetryDelay, cancellationToken);
            }
        }

        public static string CreateTempPath(string destinationPath, string extension = ".tmp")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            var fileName = Path.GetFileName(destinationPath);
            var directory = string.IsNullOrWhiteSpace(destinationDirectory)
                ? "."
                : destinationDirectory;
            return Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}{extension}");
        }

        private static bool FilesHaveSameContent(string leftPath, string rightPath)
        {
            var leftInfo = new FileInfo(leftPath);
            var rightInfo = new FileInfo(rightPath);

            if (leftInfo.Length != rightInfo.Length)
            {
                return false;
            }

            const int bufferSize = 81920;
            var leftBuffer = new byte[bufferSize];
            var rightBuffer = new byte[bufferSize];

            using var leftStream = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var rightStream = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            while (true)
            {
                var leftRead = leftStream.Read(leftBuffer, 0, leftBuffer.Length);
                var rightRead = rightStream.Read(rightBuffer, 0, rightBuffer.Length);

                if (leftRead != rightRead)
                {
                    return false;
                }

                if (leftRead == 0)
                {
                    return true;
                }

                for (var index = 0; index < leftRead; index++)
                {
                    if (leftBuffer[index] != rightBuffer[index])
                    {
                        return false;
                    }
                }
            }
        }

        private static void DeleteTempFileIfPresent(string tempPath)
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }
        }
    }
}
