namespace PlayerAssistant
{
    internal static class RuntimePathUtility
    {
        private const string CompanyDirectoryName = "KyrathaSoft";
        private const string AppDirectoryName = "player-assistant";

        public static string ApplicationDirectory => Path.GetFullPath(AppContext.BaseDirectory);

        public static string SharedDataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            CompanyDirectoryName,
            AppDirectoryName);

        public static string UserDataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            CompanyDirectoryName,
            AppDirectoryName);

        public static string ResolveApplicationFileForRead(params string[] relativeParts)
        {
            return ResolveFileForRead([ApplicationDirectory, SharedDataDirectory], relativeParts);
        }

        public static string ResolveSharedDataFileForRead(params string[] relativeParts)
        {
            return ResolveFileForRead([SharedDataDirectory, ApplicationDirectory], relativeParts);
        }

        public static string ResolveUserDataFileForRead(params string[] relativeParts)
        {
            return ResolveFileForRead([UserDataDirectory, ApplicationDirectory], relativeParts);
        }

        public static string GetSharedDataPath(params string[] relativeParts)
        {
            return CombineUnderBase(SharedDataDirectory, relativeParts);
        }

        public static string GetUserDataPath(params string[] relativeParts)
        {
            return CombineUnderBase(UserDataDirectory, relativeParts);
        }

        public static string GetApplicationPath(params string[] relativeParts)
        {
            return CombineUnderBase(ApplicationDirectory, relativeParts);
        }

        public static string CombineUnderBase(string baseDirectory, params string[] relativeParts)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
            ArgumentNullException.ThrowIfNull(relativeParts);

            var combinedPath = relativeParts.Aggregate(baseDirectory, Path.Combine);
            return EnsurePathUnderBase(baseDirectory, combinedPath);
        }

        public static string EnsurePathUnderBase(string baseDirectory, string candidatePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);

            var fullBase = Path.GetFullPath(baseDirectory);
            var fullCandidate = Path.GetFullPath(candidatePath);
            var normalizedBase = fullBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(fullCandidate, normalizedBase, StringComparison.OrdinalIgnoreCase)
                || fullCandidate.StartsWith(
                    normalizedBase + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || fullCandidate.StartsWith(
                    normalizedBase + Path.AltDirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return fullCandidate;
            }

            throw new InvalidOperationException($"Runtime path '{fullCandidate}' is outside expected base directory '{normalizedBase}'.");
        }

        private static string ResolveFileForRead(string[] baseDirectories, string[] relativeParts)
        {
            ArgumentNullException.ThrowIfNull(baseDirectories);
            ArgumentNullException.ThrowIfNull(relativeParts);

            string? fallbackPath = null;
            foreach (var baseDirectory in baseDirectories)
            {
                var path = CombineUnderBase(baseDirectory, relativeParts);
                fallbackPath ??= path;
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return fallbackPath ?? CombineUnderBase(ApplicationDirectory, relativeParts);
        }
    }
}
