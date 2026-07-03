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

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return ExternalUrlLaunchValidation.Rejected("Only HTTP and HTTPS URLs can be opened.");
            }

            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                return ExternalUrlLaunchValidation.Rejected("URLs with embedded credentials cannot be opened.");
            }

            if (string.IsNullOrWhiteSpace(uri.Host))
            {
                return ExternalUrlLaunchValidation.Rejected("The selected URL does not include a host.");
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
