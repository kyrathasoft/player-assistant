using System;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal static class StructuredCorrelationUtility
    {
        private static readonly Regex CorrelationIdPattern = new("^[a-f0-9]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string CreateCorrelationId(string? candidate = null)
        {
            return IsValid(candidate) ? candidate! : Guid.NewGuid().ToString("N");
        }

        public static bool IsValid(string? value)
        {
            return value is not null && CorrelationIdPattern.IsMatch(value);
        }
    }
}
