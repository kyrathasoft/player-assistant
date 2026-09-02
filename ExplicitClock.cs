namespace PlayerAssistant;

internal interface IClock { DateTimeOffset UtcNow { get; } }
internal sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
internal sealed class FixedClock : IClock { public FixedClock(DateTimeOffset utcNow) { UtcNow = utcNow.ToUniversalTime(); } public DateTimeOffset UtcNow { get; set; } }

internal static class DateBoundary
{
    internal static DateTimeOffset RequireUtc(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero) throw new ArgumentException($"{name} must be UTC.", name);
        return value;
    }
    internal static DateTimeOffset ToUtc(DateTime value, TimeZoneInfo zone, string name)
    {
        if (value.Kind != DateTimeKind.Unspecified) throw new ArgumentException($"{name} must be an unspecified wall-clock time.", name);
        if (zone.IsInvalidTime(value)) throw new ArgumentException($"{name} falls in a daylight-saving gap.", name);
        if (zone.IsAmbiguousTime(value)) throw new ArgumentException($"{name} is ambiguous in its timezone.", name);
        return new DateTimeOffset(value, zone.GetUtcOffset(value)).ToUniversalTime();
    }

    internal static DateTimeOffset ParseUtc(string value, string name)
    {
        if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            throw new ArgumentException($"{name} is not an ISO timestamp.", name);
        return RequireUtc(parsed, name);
    }
}
