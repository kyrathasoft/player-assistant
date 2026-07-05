namespace PlayerAssistant
{
    internal enum NetworkUrlPurpose
    {
        Generic,
        Rpol,
        ObsidianPublish,
        PlayerAssistantUpdate,
        PlayerAssistantHostedSettings
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
        private static Func<Uri, NetworkUrlPurpose, NetworkUrlAllowlistValidation?>? ValidationOverrideForTests;
        private static readonly NetworkUrlPolicyRule[] Rules =
        [
            new(
                NetworkUrlPurpose.Rpol,
                "RPOL thread and game page URLs must use rpol.net with '/game.php' or '/gameinfo.php'.",
                uri => IsHost(uri, "rpol.net") && (PathEquals(uri, "/game.php") || PathEquals(uri, "/gameinfo.php"))),
            new(
                NetworkUrlPurpose.Rpol,
                "RPOL hosted image URLs must use rpol.net with the '/c-webp/' path.",
                uri => IsHost(uri, "rpol.net") && PathStartsWith(uri, "/c-webp/")),
            new(
                NetworkUrlPurpose.ObsidianPublish,
                "Obsidian Publish page and note URLs must use publish.obsidian.md or an obsidian.md subdomain.",
                uri => IsHost(uri, "publish.obsidian.md", allowSubdomains: true) && HasNonEmptyPath(uri)),
            new(
                NetworkUrlPurpose.ObsidianPublish,
                "Obsidian Publish cache URLs must use an obsidian.md host with the '/cache/' path.",
                uri => IsHost(uri, "publish.obsidian.md", allowSubdomains: true) && PathStartsWith(uri, "/cache/")),
            new(
                NetworkUrlPurpose.ObsidianPublish,
                "Obsidian Publish asset access URLs must use an obsidian.md host with the '/access/' path.",
                uri => IsHost(uri, "publish.obsidian.md", allowSubdomains: true) && PathStartsWith(uri, "/access/")),
            new(
                NetworkUrlPurpose.ObsidianPublish,
                "Obsidian Publish markdown URLs must use an obsidian.md host and end with '.md'.",
                uri => IsHost(uri, "publish.obsidian.md", allowSubdomains: true)
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
                NetworkUrlPurpose.PlayerAssistantHostedSettings,
                "Hosted Player Assistant settings must use bryanmiller.us at '/scarlethorizons/settings.local.json'.",
                uri => IsHost(uri, "bryanmiller.us", allowSubdomains: true)
                    && PathEquals(uri, "/scarlethorizons/settings.local.json"))
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

        public static bool IsObsidianPublishHost(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            return IsHost(uri, "publish.obsidian.md", allowSubdomains: true);
        }

        public static bool IsPlayerAssistantUpdateHost(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            return IsHost(uri, "bryanmiller.us", allowSubdomains: true);
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
