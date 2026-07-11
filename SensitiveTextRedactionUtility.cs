using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal static partial class SensitiveTextRedactionUtility
    {
        public const string RedactedValue = "[REDACTED]";

        public static string Redact(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var redacted = CredentialedUrlPattern().Replace(text, $"$1{RedactedValue}:{RedactedValue}@");
            redacted = SecretQueryPattern().Replace(redacted, $"$1{RedactedValue}");
            redacted = BearerTokenPattern().Replace(redacted, $"$1{RedactedValue}");
            redacted = CookieHeaderPattern().Replace(redacted, $"$1{RedactedValue}");
            redacted = JsonSensitiveValuePattern().Replace(redacted, $"$1\"{RedactedValue}\"");
            redacted = RpolCredentialPattern().Replace(redacted, $"$1{RedactedValue}");
            return redacted;
        }

        [GeneratedRegex("(https?://)([^/\\s:@]+):([^/\\s@]+)@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex CredentialedUrlPattern();

        [GeneratedRegex("([?&](?:password|token|secret)=)[^&\\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex SecretQueryPattern();

        [GeneratedRegex("(Authorization\\s*:\\s*Bearer\\s+)\\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex BearerTokenPattern();

        [GeneratedRegex("(Cookie\\s*:\\s*).+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex CookieHeaderPattern();

        [GeneratedRegex("(\"(?:payload|cookie|token|authorization|password|secret|credential|storage state|storage_state)\"\\s*:\\s*)\"[^\"]*\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex JsonSensitiveValuePattern();

        [GeneratedRegex("(RPOL (?:password|user name)\\s*[:=]\\s*)\\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RpolCredentialPattern();
    }
}
