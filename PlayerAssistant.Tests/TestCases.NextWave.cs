using PlayerAssistant;

namespace PlayerAssistant.Tests;

internal static partial class TestCases
{
    internal static void ReleaseStateCompatibilityRejectsRollbackAndFutureSchema()
    {
        var now = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var previous = new ReleaseState(1, 4, ReleaseTransactionStage.Verified, new string('a', 64), now.AddMinutes(-2), now.AddMinutes(-1), Guid.NewGuid().ToString("D"));
        var rollback = previous with { Stage = ReleaseTransactionStage.Staged, UpdatedAtUtc = now };
        AssertThrows<InvalidOperationException>(() => ReleaseStateCompatibilityVerifier.Verify(rollback, previous, now));
        var future = previous with { SchemaVersion = 2 };
        AssertThrows<InvalidOperationException>(() => ReleaseStateCompatibilityVerifier.Verify(future, null, now));
    }

    internal static void CorrelationContextRejectsSecretsAndAcceptsSafeIds()
    {
        var context = CorrelationContext.Create();
        AssertTrue(CorrelationContext.IsSafeHeader(context.HeaderValue), "correlation ID must be a GUID header");
        var redacted = CorrelationRedaction.ForLog($"{context.CorrelationId} password=hunter2 Cookie=session-secret");
        AssertFalse(redacted.Contains("hunter2", StringComparison.Ordinal), "password must not be logged");
        AssertFalse(redacted.Contains("session-secret", StringComparison.Ordinal), "cookie must not be logged");
    }

    internal static void DateBoundaryRejectsAmbiguousAndNonUtcValues()
    {
        var central = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
        AssertThrows<ArgumentException>(() => DateBoundary.RequireUtc(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-6)), "created_at"));
        AssertThrows<ArgumentException>(() => DateBoundary.ToUtc(new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Unspecified), central, "schedule"));
        var utc = DateBoundary.ToUtc(new DateTime(2026, 2, 1, 1, 30, 0, DateTimeKind.Unspecified), central, "schedule");
        AssertEqual(TimeSpan.Zero, utc.Offset, "date boundary must produce UTC");
    }

    internal static void StartupRecoveryRejectsAmbiguousVerifiedJournal()
    {
        using var directory = TemporaryDirectory.Create();
        var target = Path.Combine(directory.Path, "release.bin");
        File.WriteAllText(target, "old");
        var journalPath = Path.Combine(directory.Path, "one.transaction.json");
        var journal = new StartupTransactionJournal(1, Guid.NewGuid().ToString("D"), "verified", Path.GetFullPath(target), new string('0', 64), new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        File.WriteAllText(journalPath, System.Text.Json.JsonSerializer.Serialize(journal));
        AssertThrows<InvalidOperationException>(() => StartupTransactionRecovery.Recover(directory.Path, _ => { }, _ => { }));
    }
}
