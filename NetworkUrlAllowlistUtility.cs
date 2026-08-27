namespace PlayerAssistant
{
    internal enum NetworkUrlPurpose
    {
        Generic,
        Rpol,
        ObsidianPublish,
        PlayerAssistantUpdate,
        PlayerAssistantHostedSettings,
        PlayerAssistantBroker
    }

    internal sealed record NetworkUrlAllowlistValidation(
        bool IsAllowed,
        Uri? Uri,
        string? RejectionReason)
    {
        public static NetworkUrlAllowlistValidation Allowed(Uri uri)
        {
            return new NetworkUrlAllowlistValidation(true, uri, null);
        }

        public static NetworkUrlAllowlistValidation Rejected(string reason)
        {
            return new NetworkUrlAllowlistValidation(false, null, reason);
        }
    }

    internal static partial class NetworkUrlAllowlistUtility
    {
        private const string HostedLocalSettingsOverrideEnvironmentVariable = "PLAYER_ASSISTANT_HOSTED_LOCAL_SETTINGS_URL_OVERRIDE";
        private const string UpdateBaseUrlOverrideEnvironmentVariable = "PLAYER_ASSISTANT_UPDATE_BASE_URL";
        private static Func<Uri, NetworkUrlPurpose, NetworkUrlAllowlistValidation?>? ValidationOverrideForTests;
        private static readonly NetworkUrlPolicyRule[] Rules =
        [
            new(
                NetworkUrlPurpose.Rpol,
                "RPOL thread and game page URLs must use rpol.net with '/game.php' or '/gameinfo.php'.",
                uri => IsHost(uri, "rpol.net") && (PathEquals(uri, "/game.php") || PathEquals(uri, "/gameinfo.php"))),
            new(
                NetworkUrlPurpose.Rpol,
                "RPOL thread display URLs must use rpol.net with the '/display.cgi' path.",
                uri => IsHost(uri, "rpol.net") && PathEquals(uri, "/display.cgi")),
            new(
                NetworkUrlPurpose.Rpol,
                "RPOL Dice Roller URLs must use rpol.net with the exact '/usermodules/diceroller.cgi' path.",
                uri => IsHost(uri, "rpol.net") && PathEquals(uri, "/usermodules/diceroller.cgi")),
            new(
                NetworkUrlPurpose.Rpol,
                "RPOL hosted image URLs must use rpol.net with the '/c-webp/' path.",
                uri => IsHost(uri, "rpol.net") && PathStartsWith(uri, "/c-webp/")),
            new(
                NetworkUrlPurpose.ObsidianPublish,
                "Obsidian Publish page and note URLs must use publish.obsidian.md or an Obsidian Publish content host.",
                uri => IsObsidianPublishHost(uri) && HasNonEmptyPath(uri)),
            new(
                NetworkUrlPurpose.ObsidianPublish,
                "Obsidian Publish cache URLs must use an Obsidian Publish content host with the '/cache/' path.",
                uri => IsObsidianPublishHost(uri) && PathStartsWith(uri, "/cache/")),
            new(
                NetworkUrlPurpose.ObsidianPublish,
                "Obsidian Publish asset access URLs must use an Obsidian Publish content host with the '/access/' path.",
                uri => IsObsidianPublishHost(uri) && PathStartsWith(uri, "/access/")),
            new(
                NetworkUrlPurpose.ObsidianPublish,
                "Obsidian Publish markdown URLs must use an Obsidian Publish content host and end with '.md'.",
                uri => IsObsidianPublishHost(uri)
                    && uri.AbsolutePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)),
            new(
                NetworkUrlPurpose.PlayerAssistantUpdate,
                "Player Assistant update listing must use bryanmiller.us with the '/scarlethorizons/' path.",
                uri => IsHost(uri, "bryanmiller.us", allowSubdomains: true) && PathEquals(uri, "/scarlethorizons/")),
            new(
                NetworkUrlPurpose.PlayerAssistantUpdate,
                "Player Assistant update manifests must use bryanmiller.us under '/scarlethorizons/'.",
                uri => IsHost(uri, "bryanmiller.us", allowSubdomains: true)
                    && (PathEquals(uri, "/scarlethorizons/p-assist-updates.json")
                        || PathEquals(uri, "/scarlethorizons/p-assist-updates.json.sig"))),
            new(
                NetworkUrlPurpose.PlayerAssistantUpdate,
                "Player Assistant update packages must use bryanmiller.us under '/scarlethorizons/' with approved archive names.",
                uri => IsHost(uri, "bryanmiller.us", allowSubdomains: true)
                    && PathStartsWith(uri, "/scarlethorizons/")
                    && UpdateArtifactFileNameRegex().IsMatch(Path.GetFileName(uri.AbsolutePath))),
            new(
                NetworkUrlPurpose.Generic,
                "Player Assistant regional map image must use bryanmiller.us at '/scarlethorizons/maps/northernreaches.png'.",
                uri => IsHost(uri, "bryanmiller.us", allowSubdomains: true)
                    && PathEquals(uri, "/scarlethorizons/maps/northernreaches.png")),
            new(
                NetworkUrlPurpose.PlayerAssistantHostedSettings,
                "Hosted Player Assistant settings must use bryanmiller.us at '/scarlethorizons/settings.local.json'.",
                uri => IsHost(uri, "bryanmiller.us", allowSubdomains: true)
                    && PathEquals(uri, "/scarlethorizons/settings.local.json")),
            new(
                NetworkUrlPurpose.PlayerAssistantBroker,
                "Player Assistant broker requests must use bryanmiller.us under '/scarlethorizons/api/v1/'.",
                uri => IsHost(uri, "bryanmiller.us", allowSubdomains: true)
                    && PathStartsWith(uri, "/scarlethorizons/api/v1/"))
        ];

        public static NetworkUrlAllowlistValidation Validate(
            string? value,
            NetworkUrlPurpose purpose = NetworkUrlPurpose.Generic)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return NetworkUrlAllowlistValidation.Rejected("URL is missing or empty.");
            }

            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            {
                return NetworkUrlAllowlistValidation.Rejected("URL must be absolute.");
            }

            return Validate(uri, purpose);
        }

        public static NetworkUrlAllowlistValidation Validate(
            Uri? uri,
            NetworkUrlPurpose purpose = NetworkUrlPurpose.Generic)
        {
            if (uri is null || !uri.IsAbsoluteUri)
            {
                return NetworkUrlAllowlistValidation.Rejected("URL must be absolute.");
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return NetworkUrlAllowlistValidation.Rejected("Only HTTP and HTTPS URLs are allowed.");
            }

            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                return NetworkUrlAllowlistValidation.Rejected("URLs with embedded credentials are not allowed.");
            }

            if (string.IsNullOrWhiteSpace(uri.Host))
            {
                return NetworkUrlAllowlistValidation.Rejected("URL must include a host.");
            }

            if (uri.Host.Contains('%', StringComparison.Ordinal))
            {
                return NetworkUrlAllowlistValidation.Rejected("URL hosts may not contain escaped characters.");
            }

            var overrideValidation = ValidationOverrideForTests?.Invoke(uri, purpose);
            if (overrideValidation is not null)
            {
                return overrideValidation;
            }

            var environmentOverrideValidation = ValidateEnvironmentOverride(uri, purpose);
            if (environmentOverrideValidation is not null)
            {
                return environmentOverrideValidation;
            }

            if (purpose == NetworkUrlPurpose.Generic)
            {
                return Rules.Any(rule => rule.IsMatch(uri))
                    ? NetworkUrlAllowlistValidation.Allowed(uri)
                    : NetworkUrlAllowlistValidation.Rejected("URL host/path is not on the Player Assistant network allowlist.");
            }

            var matchingRules = Rules
                .Where(rule => rule.Purpose == purpose)
                .ToArray();
            if (matchingRules.Any(rule => rule.IsMatch(uri)))
            {
                return NetworkUrlAllowlistValidation.Allowed(uri);
            }

            var rejectionReason = matchingRules.Length == 0
                ? $"No network allowlist rules are configured for purpose '{purpose}'."
                : string.Join(" ", matchingRules.Select(rule => rule.RejectionMessage).Distinct(StringComparer.Ordinal));
            return NetworkUrlAllowlistValidation.Rejected(rejectionReason);
        }

        public static void EnsureAllowed(
            Uri uri,
            NetworkUrlPurpose purpose = NetworkUrlPurpose.Generic)
        {
            var validation = Validate(uri, purpose);
            if (!validation.IsAllowed)
            {
                throw new InvalidOperationException(
                    $"Network URL is not allowed: {uri}. {validation.RejectionReason}");
            }
        }

        public static bool IsRpolHost(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            return IsHost(uri, "rpol.net");
        }

        internal static bool IsRpolCredentialEntryUri(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            return IsHttpsRpolUri(uri)
                && PathEquals(uri, "/game.php");
        }

        internal static bool IsRpolVerificationNavigationUri(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            if (!IsHttpsRpolUri(uri))
            {
                return false;
            }

            return PathEquals(uri, "/game.php")
                || PathEquals(uri, "/gameinfo.php")
                || PathEquals(uri, "/login.cgi")
                || PathEquals(uri, "/display.cgi")
                || PathEquals(uri, "/usermodules/diceroller.cgi")
                || PathStartsWith(uri, "/c-webp/");
        }

        public static bool IsObsidianPublishHost(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            return string.Equals(uri.Host, "publish.obsidian.md", StringComparison.OrdinalIgnoreCase)
                || (uri.Host.StartsWith("publish-", StringComparison.OrdinalIgnoreCase)
                    && uri.Host.EndsWith(".obsidian.md", StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsPlayerAssistantUpdateHost(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            return IsHost(uri, "bryanmiller.us", allowSubdomains: true);
        }

        private static NetworkUrlAllowlistValidation? ValidateEnvironmentOverride(Uri uri, NetworkUrlPurpose purpose)
        {
            if (purpose == NetworkUrlPurpose.PlayerAssistantHostedSettings
                && TryGetAbsoluteEnvironmentUri(HostedLocalSettingsOverrideEnvironmentVariable) is { } hostedSettingsOverrideUri)
            {
                return UriEquals(uri, hostedSettingsOverrideUri)
                    ? NetworkUrlAllowlistValidation.Allowed(uri)
                    : null;
            }

            if (purpose == NetworkUrlPurpose.PlayerAssistantUpdate
                && TryGetAbsoluteEnvironmentUri(UpdateBaseUrlOverrideEnvironmentVariable) is { } updateBaseOverrideUri)
            {
                return IsEnvironmentOverrideUpdateUri(uri, updateBaseOverrideUri)
                    ? NetworkUrlAllowlistValidation.Allowed(uri)
                    : null;
            }

            return null;
        }

        private static Uri? TryGetAbsoluteEnvironmentUri(string environmentVariableName)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariableName);
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsAbsoluteUri
                ? uri
                : null;
        }

        private static bool IsEnvironmentOverrideUpdateUri(Uri candidateUri, Uri baseOverrideUri)
        {
            if (!string.Equals(candidateUri.Scheme, baseOverrideUri.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(candidateUri.Host, baseOverrideUri.Host, StringComparison.OrdinalIgnoreCase)
                || candidateUri.Port != baseOverrideUri.Port)
            {
                return false;
            }

            var basePath = baseOverrideUri.AbsolutePath;
            if (!basePath.EndsWith("/", StringComparison.Ordinal))
            {
                basePath += "/";
            }

            if (!candidateUri.AbsolutePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var relativePath = candidateUri.AbsolutePath[basePath.Length..];
            if (relativePath.Length == 0)
            {
                return true;
            }

            return string.Equals(relativePath, "p-assist-updates.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(relativePath, "p-assist-updates.json.sig", StringComparison.OrdinalIgnoreCase)
                || UpdateArtifactFileNameRegex().IsMatch(Path.GetFileName(candidateUri.AbsolutePath));
        }

        private static bool UriEquals(Uri left, Uri right)
        {
            return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
                && left.Port == right.Port
                && string.Equals(left.AbsolutePath, right.AbsolutePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Query, right.Query, StringComparison.Ordinal);
        }

        internal static IDisposable UseValidationOverrideForTests(
            Func<Uri, NetworkUrlPurpose, NetworkUrlAllowlistValidation?> validator)
        {
            ArgumentNullException.ThrowIfNull(validator);

            var previousValidator = ValidationOverrideForTests;
            ValidationOverrideForTests = validator;
            return new DelegateDisposable(() => ValidationOverrideForTests = previousValidator);
        }

        private static bool IsHost(Uri uri, string host, bool allowSubdomains = true)
        {
            ArgumentNullException.ThrowIfNull(uri);
            ArgumentException.ThrowIfNullOrWhiteSpace(host);

            return string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)
                || (allowSubdomains && uri.Host.EndsWith($".{host}", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsHttpsRpolUri(Uri uri)
        {
            return uri.IsAbsoluteUri
                && uri.Scheme == Uri.UriSchemeHttps
                && uri.IsDefaultPort
                && string.IsNullOrWhiteSpace(uri.UserInfo)
                && string.Equals(uri.Host, "rpol.net", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasNonEmptyPath(Uri uri)
        {
            return !string.IsNullOrWhiteSpace(uri.AbsolutePath);
        }

        private static bool PathEquals(Uri uri, string expectedPath)
        {
            return string.Equals(uri.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathStartsWith(Uri uri, string expectedPrefix)
        {
            return uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
        }

        [System.Text.RegularExpressions.GeneratedRegex(@"^p-assist-\d+\.\d+\.\d+(?:\.\d+)?\.(zip|exe)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
        private static partial System.Text.RegularExpressions.Regex UpdateArtifactFileNameRegex();

        private sealed class DelegateDisposable(Action onDispose) : IDisposable
        {
            private Action? _onDispose = onDispose;

            public void Dispose()
            {
                Interlocked.Exchange(ref _onDispose, null)?.Invoke();
            }
        }

        private sealed record NetworkUrlPolicyRule(
            NetworkUrlPurpose Purpose,
            string RejectionMessage,
            Func<Uri, bool> IsMatch);
    }
}
