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
            redacted = GenericSecretAssignmentPattern().Replace(redacted, $"$1{RedactedValue}");
            redacted = AdminKeyPattern().Replace(redacted, $"$1{RedactedValue}");
            redacted = PrivatePathPattern().Replace(redacted, $"$1{RedactedValue}");
            redacted = ResponseBodyPattern().Replace(redacted, $"$1{RedactedValue}");
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

        [GeneratedRegex("(\"(?:payload|cookie|token|authorization|password|secret|credential|admin_key|admin-key|private_path|private-path|response_body|response-body|storage state|storage_state)\"\\s*:\\s*)\"[^\"]*\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex JsonSensitiveValuePattern();

        [GeneratedRegex("(RPOL (?:password|user name)\\s*[:=]\\s*)\\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex RpolCredentialPattern();

        [GeneratedRegex("((?:api[_-]?key|access[_-]?token|client[_-]?secret|password|secret|token|cookie|storage(?:[_-]|\\s+)?state)\\s*[:=]\\s*)[^\\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex GenericSecretAssignmentPattern();

        [GeneratedRegex("((?:admin(?:istrator)?[_-]?key|x-admin-key|admin[_-]?token)\\s*[:=]\\s*)[^\\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex AdminKeyPattern();

        [GeneratedRegex("((?:path|file|directory|profile|private[_-]?path)\\s*[:=]\\s*)(?:[A-Za-z]:\\\\|\\\\\\|/)[^\\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex PrivatePathPattern();

        [GeneratedRegex("((?:response[_-]?body|body|response)\\s*[:=]\\s*)[^\\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ResponseBodyPattern();
    }
}
