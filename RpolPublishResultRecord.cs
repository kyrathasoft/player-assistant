using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant;

internal sealed record RpolTargetOutcome(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("error")] string? Error);

internal sealed record RpolPublishResultRecord(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("started_at")] string StartedAt,
    [property: JsonPropertyName("ended_at")] string EndedAt,
    [property: JsonPropertyName("terminal_status")] string TerminalStatus,
    [property: JsonPropertyName("terminal_stage")] string TerminalStage,
    [property: JsonPropertyName("timeout_category")] string? TimeoutCategory,
    [property: JsonPropertyName("discovered")] int Discovered,
    [property: JsonPropertyName("attempted")] int Attempted,
    [property: JsonPropertyName("published")] int Published,
    [property: JsonPropertyName("failed")] int Failed,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors,
    [property: JsonPropertyName("target_outcomes")] IReadOnlyList<RpolTargetOutcome> TargetOutcomes,
    [property: JsonPropertyName("cleanup_errors")] IReadOnlyList<string> CleanupErrors,
    [property: JsonPropertyName("upload_completed")] bool UploadCompleted = false,
    [property: JsonPropertyName("cursor_persisted")] bool CursorPersisted = false,
    [property: JsonPropertyName("recovery_stage")] string? RecoveryStage = null)
{
    internal const int CurrentSchemaVersion = 1;

    internal static RpolPublishResultRecord Create(string runId, DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return new RpolPublishResultRecord(
            CurrentSchemaVersion,
            runId,
            startedAt.ToUniversalTime().ToString("O"),
            string.Empty,
            "running",
            "starting",
            null,
            -1,
            0,
            0,
            0,
            [],
            [],
            [],
            false,
            false,
            null);
    }

    internal static RpolPublishResultRecord FromReport(
        RpolSnapshotPublishReport report,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        string terminalStage,
        string? timeoutCategory = null,
        IReadOnlyList<string>? cleanupErrors = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new RpolPublishResultRecord(
            CurrentSchemaVersion,
            runId,
            startedAt.ToUniversalTime().ToString("O"),
            endedAt.ToUniversalTime().ToString("O"),
            report.Discovered < 0
                ? timeoutCategory is null ? "crash" : "timeout"
                : report.Failed == 0 && report.Published == 1 && (cleanupErrors is null || cleanupErrors.Count == 0) && report.UploadCompleted && report.CursorPersisted && string.IsNullOrWhiteSpace(report.RecoveryStage)
     ? "success"
     : "failure",
            terminalStage,
            timeoutCategory,
            report.Discovered,
            report.Attempted == 0 ? report.Published + report.Failed : report.Attempted,
            report.Published,
            report.Failed,
            report.Errors,
            report.TargetOutcomes ?? [],
            cleanupErrors ?? [],
            report.UploadCompleted,
            report.CursorPersisted,
            report.RecoveryStage);
    }
}

internal static class RpolPublishResultValidator
{
    internal static bool Validate(
        RpolPublishResultRecord record,
        string expectedRunId,
        DateTimeOffset notBefore,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.SchemaVersion != RpolPublishResultRecord.CurrentSchemaVersion)
        {
            reason = "Unsupported result schema.";
            return false;
        }

        if (!string.Equals(record.RunId, expectedRunId, StringComparison.Ordinal))
        {
            reason = "Result run ID does not match the current invocation.";
            return false;
        }

        if (!DateTimeOffset.TryParse(record.StartedAt, out var startedAt)
            || startedAt.ToUniversalTime() < notBefore.ToUniversalTime())
        {
            reason = "Result start time is stale or invalid.";
            return false;
        }

        if (!DateTimeOffset.TryParse(record.EndedAt, out var endedAt)
            || endedAt < startedAt)
        {
            reason = "Result end time is invalid.";
            return false;
        }

        if (record.TerminalStatus is "timeout" or "crash")
        {
            if (record.Discovered >= 0
                || record.Attempted != 0
                || record.Published != 0
                || record.Failed != 1
                || record.TargetOutcomes.Count != 0
                || record.UploadCompleted
                || record.CursorPersisted)
            {
                reason = "A timeout or crash result must describe unknown discovery with no attempted target or completed pre-stage work.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        if (record.TerminalStatus == "success"
            && (!record.UploadCompleted || !record.CursorPersisted || !string.IsNullOrWhiteSpace(record.RecoveryStage)))
        {
            reason = "A successful result must prove both publication and cursor persistence with no recovery stage.";
            return false;
        }

        if (record.Discovered <= 0
            || record.Attempted < 0
            || record.Published < 0
            || record.Failed < 0
            || record.Published + record.Failed != record.Attempted
            || record.TargetOutcomes.Count != record.Attempted)
        {
            reason = "Result count invariants are invalid or discovery is unknown.";
            return false;
        }

        if (record.TargetOutcomes.Any(outcome =>
                string.IsNullOrWhiteSpace(outcome.Target)
                || outcome.Status is not ("published" or "published-cursor-pending" or "published-cursor-recovered" or "failed")
                || (outcome.Status is "failed" or "published-cursor-pending" && string.IsNullOrWhiteSpace(outcome.Error))
                || (outcome.Status is "published" or "published-cursor-recovered" && !string.IsNullOrWhiteSpace(outcome.Error))))
        {
            reason = "Per-target outcome details are invalid.";
            return false;
        }

        if (record.TerminalStatus == "success"
            && (record.Attempted != 1 || record.Published != 1 || record.Failed != 0 || record.Errors.Count != 0 || record.CleanupErrors.Count != 0))
        {
            reason = "A successful one-target result does not describe exactly one published target.";
            return false;
        }

        if (record.TerminalStatus is not ("success" or "failure" or "timeout" or "crash"))
        {
            reason = "Result terminal status is invalid.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    internal static RpolPublishResultRecord ReadAndValidate(
        string path,
        string expectedRunId,
        DateTimeOffset notBefore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new InvalidDataException("The RPOL publisher result file is missing.");
        }

        var record = JsonSerializer.Deserialize<RpolPublishResultRecord>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The RPOL publisher result file is empty.");
        if (!Validate(record, expectedRunId, notBefore, out var reason))
        {
            throw new InvalidDataException(reason);
        }

        return record;
    }

    internal static void WriteAtomic(string path, RpolPublishResultRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(record);
        AtomicFileUtility.WriteAllText(
            path,
            JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
    }
}
