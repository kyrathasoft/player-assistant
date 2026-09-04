using System.Globalization;

namespace PlayerAssistant;

internal interface IPlayerAssistantClock
{
    DateTimeOffset UtcNow { get; }
}

internal sealed class SystemPlayerAssistantClock : IPlayerAssistantClock
{
    internal static SystemPlayerAssistantClock Instance { get; } = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

internal static class ClockUtility
{
    internal const string CentralTimeZoneId = "Central Standard Time";

    internal static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();

    internal static DateTime Central(DateTimeOffset utcNow)
    {
        var normalized = Utc(utcNow);
        return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(normalized, CentralTimeZoneId).DateTime;
    }

    internal static bool TryParseUtc(string? value, out DateTimeOffset utc)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out utc);
    }
}
