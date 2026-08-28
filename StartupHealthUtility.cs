using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant
{
    internal static class StartupHealthUtility
    {
        public const string HealthFileName = "startup-health.json";
        public const int CurrentSchemaVersion = 1;
        private static readonly object SyncRoot = new();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };
        private static readonly List<StartupHealthPhase> Phases = [];
        private static DateTimeOffset _startedAt = DateTimeOffset.Now;
        private static DateTimeOffset _updatedAt = _startedAt;

        public static void Reset()
        {
            lock (SyncRoot)
            {
                Phases.Clear();
                _startedAt = DateTimeOffset.Now;
                _updatedAt = _startedAt;
                WriteSnapshotLocked();
            }
        }

        public static void RecordPhase(
            string phase,
            string status,
            TimeSpan elapsed,
            Exception? exception = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(phase);
            ArgumentException.ThrowIfNullOrWhiteSpace(status);

            lock (SyncRoot)
            {
                _updatedAt = DateTimeOffset.Now;
                Phases.Add(new StartupHealthPhase(
                    SensitiveTextRedactionUtility.Redact(phase),
                    status,
                    Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds)),
                    FileDownloadCounters.CompletedDownloadCount,
                    null,
                    exception is null ? 0 : 1,
                    exception is null ? null : StartupHealthException.From(exception)));
                WriteSnapshotLocked();
            }
        }

        private static void WriteSnapshotLocked()
        {
            try
            {
                var snapshot = new StartupHealthSnapshot(
                    CurrentSchemaVersion,
                    _startedAt,
                    _updatedAt,
                    Phases.ToArray());
                AtomicFileUtility.WriteAllText(
                    RuntimePathUtility.GetWritableRuntimePath(HealthFileName),
                    JsonSerializer.Serialize(snapshot, JsonOptions));
            }
            catch
            {
            }
        }
    }

    internal sealed record StartupHealthSnapshot(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("phases")] IReadOnlyList<StartupHealthPhase> Phases);

    internal sealed record StartupHealthPhase(
        [property: JsonPropertyName("phase")] string Phase,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("elapsed_milliseconds")] long ElapsedMilliseconds,
        [property: JsonPropertyName("refreshed_count")] int RefreshedCount,
        [property: JsonPropertyName("current_count")] int? CurrentCount,
        [property: JsonPropertyName("failed_count")] int FailedCount,
        [property: JsonPropertyName("last_exception")] StartupHealthException? LastException);

    internal sealed record StartupHealthException(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("message")] string Message)
    {
        public static StartupHealthException From(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            return new StartupHealthException(
                exception.GetType().Name,
                SensitiveTextRedactionUtility.Redact(exception.Message));
        }
    }
}
