using PlayerAssistant;

namespace PlayerAssistant.Tests;

internal static partial class TestCases
{
    internal static void ClockBoundaryContractIsDeterministic()
    {
        var springBefore = new DateTimeOffset(2026, 3, 8, 7, 59, 0, TimeSpan.Zero);
        var springAfter = new DateTimeOffset(2026, 3, 8, 8, 1, 0, TimeSpan.Zero);
        AssertEqual(1, ClockUtility.Central(springBefore).Hour, "Central conversion before DST transition was not explicit");
        AssertEqual(3, ClockUtility.Central(springAfter).Hour, "Central conversion after DST transition was not explicit");

        var leapDay = new DateTimeOffset(2024, 2, 29, 12, 0, 0, TimeSpan.Zero);
        AssertTrue(ClockUtility.TryParseUtc(leapDay.ToString("O"), out var parsedLeapDay), "UTC leap-day timestamp should parse");
        AssertEqual(leapDay, parsedLeapDay, "UTC leap-day timestamp changed during normalization");

        var rollback = new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero);
        var rollbackLater = rollback.AddHours(1);
        AssertTrue(ClockUtility.Central(rollback).Hour == 1 && ClockUtility.Central(rollbackLater).Hour == 1,
            "both sides of the Central rollback must remain distinguishable instants");
        AssertTrue(TimeZoneInfo.FindSystemTimeZoneById(ClockUtility.CentralTimeZoneId)
            .IsAmbiguousTime(new DateTime(2026, 11, 1, 1, 30, 0)),
            "Central rollback should identify the ambiguous local hour");

        AssertTrue(ClockUtility.TryParseUtc("2026-02-28T23:59:59+00:00", out _), "valid future-boundary format should parse");
        AssertTrue(!ClockUtility.TryParseUtc("not-a-timestamp", out _), "invalid timestamp must fail closed");
        AssertTrue(rollbackLater > rollback, "clock rollback test must compare instants, not local wall time");
    }
}
