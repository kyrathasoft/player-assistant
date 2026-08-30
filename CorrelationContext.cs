using System.Text.RegularExpressions;

namespace PlayerAssistant;

internal sealed record CorrelationContext(string CorrelationId)
{
    internal static CorrelationContext Create(string? value = null)
    {
        if (value is not null && Guid.TryParse(value, out var parsed)) return new(parsed.ToString("D"));
        return new(Guid.NewGuid().ToString("D"));
    }

    internal string HeaderValue => CorrelationId;
    internal string Redact(string text) => SensitiveTextRedactionUtility.Redact(text).Replace(CorrelationId, "[CORRELATION_ID]", StringComparison.Ordinal);
    internal static bool IsSafeHeader(string? value) => value is not null && Guid.TryParse(value, out _);
}

internal static class CorrelationRedaction
{
    private static readonly Regex Secret = new("(?i)(password|token|cookie|authorization|set-cookie)\\s*[:=]\\s*[^\\s,;]+", RegexOptions.Compiled);
    internal static string ForLog(string value) => Secret.Replace(SensitiveTextRedactionUtility.Redact(value), "$1=[REDACTED]");
}
