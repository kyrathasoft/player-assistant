namespace PlayerAssistant
{
    internal static class KeywordTermsFileUtility
    {
        public const string FileName = "game-posts-key-terms.md";

        private static readonly HashSet<string> IgnoredSearchDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            "bin",
            "obj",
            "graphify-out"
        };

        public static string GetReleasePath()
        {
            var baseDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            return Path.Combine(baseDirectory, FileName);
        }

        public static string? TryResolvePath()
        {
            var releasePath = GetReleasePath();
            if (File.Exists(releasePath))
            {
                return releasePath;
            }

            var repoRoot = Directory.GetParent(Path.GetDirectoryName(releasePath) ?? releasePath)?.FullName;
            if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
            {
                return null;
            }

            return EnumerateKeywordTermsFiles(repoRoot).FirstOrDefault();
        }

        public static void EnsureReleaseCopy()
        {
            var releasePath = GetReleasePath();
            var repoRoot = Directory.GetParent(Path.GetDirectoryName(releasePath) ?? releasePath)?.FullName
                ?? Path.GetDirectoryName(releasePath)
                ?? releasePath;
            var termPaths = EnumerateKeywordTermsFiles(repoRoot).ToList();

            if (!termPaths.Any(path => path.Equals(releasePath, StringComparison.OrdinalIgnoreCase)))
            {
                var sourcePath = termPaths
                    .OrderByDescending(path => new FileInfo(path).Length)
                    .ThenByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(sourcePath))
                {
                    File.Copy(sourcePath, releasePath, overwrite: true);
                    termPaths.Add(releasePath);
                }
            }

            if (termPaths.Count <= 1)
            {
                return;
            }

            foreach (var duplicatePath in termPaths.Where(path => !path.Equals(releasePath, StringComparison.OrdinalIgnoreCase)))
            {
                File.Delete(duplicatePath);
            }
        }

        private static IEnumerable<string> EnumerateKeywordTermsFiles(string rootDirectory)
        {
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(rootDirectory);

            while (pendingDirectories.Count > 0)
            {
                var currentDirectory = pendingDirectories.Pop();

                IEnumerable<string> files = [];
                try
                {
                    files = Directory.EnumerateFiles(currentDirectory, FileName, SearchOption.TopDirectoryOnly);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                foreach (var file in files)
                {
                    yield return Path.GetFullPath(file);
                }

                IEnumerable<string> directories = [];
                try
                {
                    directories = Directory.EnumerateDirectories(currentDirectory);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                foreach (var directory in directories)
                {
                    var directoryName = Path.GetFileName(directory);
                    if (IgnoredSearchDirectories.Contains(directoryName))
                    {
                        continue;
                    }

                    pendingDirectories.Push(directory);
                }
            }
        }
    }
}
