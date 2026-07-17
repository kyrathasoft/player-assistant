namespace PlayerAssistant
{
    internal sealed record FormerPcSummary(
        string Name,
        string CharacterClass,
        string? TokenImagePath);

    internal static class FormerPcUtility
    {
        public static IReadOnlyList<FormerPcSummary> Load(string pcsDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pcsDirectory);

            var listingPath = PlayerCharacterAssetUtility.GetFormerPlayerCharactersListingMarkdownCachePath(pcsDirectory);
            if (!File.Exists(listingPath))
            {
                return [];
            }

            var inactiveDirectory = Path.Combine(pcsDirectory, "inactive");
            return PlayerCharacterAssetUtility
                .GetHeroRows(File.ReadAllText(listingPath))
                .Select(hero => new FormerPcSummary(
                    hero.Name,
                    hero.CharacterClass,
                    GetTokenImagePath(inactiveDirectory, hero.TokenFileName)))
                .ToArray();
        }

        private static string? GetTokenImagePath(string inactiveDirectory, string? tokenFileName)
        {
            if (string.IsNullOrWhiteSpace(tokenFileName))
            {
                return null;
            }

            if (Path.IsPathRooted(tokenFileName)
                || !string.Equals(Path.GetFileName(tokenFileName), tokenFileName, StringComparison.Ordinal))
            {
                return null;
            }

            var tokenPath = RuntimePathUtility.CombineUnderBase(inactiveDirectory, tokenFileName);
            return File.Exists(tokenPath) ? tokenPath : null;
        }
    }
}
