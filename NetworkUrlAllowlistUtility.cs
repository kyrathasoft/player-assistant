namespace PlayerAssistant
{
    internal enum NetworkUrlPurpose
    {
        Generic,
        Rpol,
        ObsidianPublish,
        PlayerAssistantUpdate
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

    internal static class NetworkUrlAllowlistUtility
    {
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

            return purpose switch
            {
                NetworkUrlPurpose.Rpol => IsRpolHost(uri)
                    ? NetworkUrlAllowlistValidation.Allowed(uri)
                    : NetworkUrlAllowlistValidation.Rejected("RPOL URLs must use rpol.net or a subdomain of rpol.net."),
                NetworkUrlPurpose.ObsidianPublish => IsObsidianPublishHost(uri)
                    ? NetworkUrlAllowlistValidation.Allowed(uri)
                    : NetworkUrlAllowlistValidation.Rejected("Obsidian Publish URLs must use publish.obsidian.md or an obsidian.md subdomain."),
                NetworkUrlPurpose.PlayerAssistantUpdate => IsPlayerAssistantUpdateHost(uri)
                    ? NetworkUrlAllowlistValidation.Allowed(uri)
                    : NetworkUrlAllowlistValidation.Rejected("Player Assistant update URLs must use bryanmiller.us."),
                _ => IsRpolHost(uri) || IsObsidianPublishHost(uri) || IsPlayerAssistantUpdateHost(uri)
                    ? NetworkUrlAllowlistValidation.Allowed(uri)
                    : NetworkUrlAllowlistValidation.Rejected("URL host is not on the Player Assistant network allowlist.")
            };
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

            return string.Equals(uri.Host, "rpol.net", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".rpol.net", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsObsidianPublishHost(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            return string.Equals(uri.Host, "publish.obsidian.md", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".obsidian.md", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPlayerAssistantUpdateHost(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            return string.Equals(uri.Host, "bryanmiller.us", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".bryanmiller.us", StringComparison.OrdinalIgnoreCase);
        }
    }
}
