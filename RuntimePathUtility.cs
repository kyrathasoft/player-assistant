namespace PlayerAssistant
{
    internal static class RuntimePathUtility
    {
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
    }
}
