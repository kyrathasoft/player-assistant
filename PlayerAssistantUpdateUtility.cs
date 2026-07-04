using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal sealed record PlayerAssistantUpdateInfo(
        Version Version,
        string VersionText,
        Uri DownloadUri)
    {
        public bool IsNewerThan(Version currentVersion)
        {
            ArgumentNullException.ThrowIfNull(currentVersion);
            return Version.CompareTo(currentVersion) > 0;
        }
    }

    internal static partial class PlayerAssistantUpdateUtility
    {
        public static Uri UpdateListingUri { get; } = new("https://bryanmiller.us/scarlethorizons/");

        public static async Task<PlayerAssistantUpdateInfo?> CheckForLatestUpdateAsync(
            HttpClient httpClient,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(httpClient);

            using var response = await NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, UpdateListingUri),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var listing = await NetworkRequestUtility.ReadStringAsync(
                response.Content,
                NetworkResponseContentLimit.Html,
                cancellationToken).ConfigureAwait(false);
            return FindLatestUpdate(listing, UpdateListingUri);
        }

        public static PlayerAssistantUpdateInfo? FindLatestUpdate(string? listingContent, Uri listingUri)
        {
            ArgumentNullException.ThrowIfNull(listingUri);
            if (string.IsNullOrWhiteSpace(listingContent))
            {
                return null;
            }

            return EnumerateArchiveReferences(listingContent)
                .Select(reference => TryCreateUpdateInfo(reference, listingUri))
                .Where(update => update is not null)
                .Select(update => update!)
                .GroupBy(update => update.DownloadUri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(update => update.Version)
                .FirstOrDefault();
        }

        public static Version GetCurrentAppVersion()
        {
            var informationalVersion = typeof(PlayerAssistantUpdateUtility).Assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), inherit: false)
                .OfType<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()
                ?.InformationalVersion;

            return TryParseVersionPrefix(informationalVersion, out var version)
                ? version
                : typeof(PlayerAssistantUpdateUtility).Assembly.GetName().Version ?? new Version(0, 0, 0);
        }

        public static bool TryParseVersionPrefix(string? value, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var match = VersionPrefixRegex().Match(value.Trim());
            if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var parsed))
            {
                return false;
            }

            version = parsed;
            return true;
        }

        private static IEnumerable<string> EnumerateArchiveReferences(string listingContent)
        {
            foreach (Match match in HrefRegex().Matches(listingContent))
            {
                yield return WebUtility.HtmlDecode(match.Groups["url"].Value);
            }

            foreach (Match match in ArchiveReferenceRegex().Matches(listingContent))
            {
                yield return WebUtility.HtmlDecode(match.Groups["url"].Value);
            }
        }

        private static PlayerAssistantUpdateInfo? TryCreateUpdateInfo(string reference, Uri listingUri)
        {
            if (string.IsNullOrWhiteSpace(reference) ||
                !Uri.TryCreate(listingUri, reference.Trim(), out var uri))
            {
                return null;
            }

            var allowlistValidation = NetworkUrlAllowlistUtility.Validate(uri, NetworkUrlPurpose.PlayerAssistantUpdate);
            if (!allowlistValidation.IsAllowed)
            {
                return null;
            }

            var fileName = Path.GetFileName(uri.LocalPath);
            var match = ArchiveFileNameRegex().Match(fileName);
            if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var version))
            {
                return null;
            }

            return new PlayerAssistantUpdateInfo(version, match.Groups["version"].Value, uri);
        }

        [GeneratedRegex(@"^\s*(?<version>\d+\.\d+\.\d+)", RegexOptions.CultureInvariant)]
        private static partial Regex VersionPrefixRegex();

        [GeneratedRegex(@"href\s*=\s*[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex HrefRegex();

        [GeneratedRegex(@"(?<url>(?:https?://[^\s""'<>]+/)?p-assist-[^\s""'<>]+\.zip)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ArchiveReferenceRegex();

        [GeneratedRegex(@"^p-assist-(?<version>\d+\.\d+\.\d+)[^/\\]*\.zip$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ArchiveFileNameRegex();
    }
}
