using System.Diagnostics;

namespace PlayerAssistant
{
    internal static class ExternalUrlLaunchUtility
    {
        public static ExternalUrlLaunchValidation Validate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return ExternalUrlLaunchValidation.Rejected("No URL was selected.");
            }

            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            {
                return ExternalUrlLaunchValidation.Rejected("The selected item is not an absolute URL.");
            }

            var allowlistValidation = NetworkUrlAllowlistUtility.Validate(uri);
            if (!allowlistValidation.IsAllowed)
            {
                return ExternalUrlLaunchValidation.Rejected(allowlistValidation.RejectionReason ?? "The selected URL is not allowed.");
            }

            return ExternalUrlLaunchValidation.Allowed(uri.AbsoluteUri, uri.IdnHost);
        }

        public static ProcessStartInfo CreateStartInfo(ExternalUrlLaunchValidation validation)
        {
            ArgumentNullException.ThrowIfNull(validation);
            if (!validation.IsAllowed || string.IsNullOrWhiteSpace(validation.Url))
            {
                throw new ArgumentException("A valid external URL is required.", nameof(validation));
            }

            return new ProcessStartInfo(validation.Url)
            {
                UseShellExecute = true
            };
        }
    }

    internal sealed record ExternalUrlLaunchValidation(
        bool IsAllowed,
        string? Url,
        string? Host,
        string? RejectionReason)
    {
        public static ExternalUrlLaunchValidation Allowed(string url, string host)
        {
            return new ExternalUrlLaunchValidation(true, url, host, null);
        }

        public static ExternalUrlLaunchValidation Rejected(string reason)
        {
            return new ExternalUrlLaunchValidation(false, null, null, reason);
        }
    }
}
