using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant
{
    internal static class OutboundNetworkDiagnosticsUtility
    {
        public const string DiagnosticsFileName = "outbound-network-diagnostics.json";
        public const int CurrentSchemaVersion = 1;

        private static readonly object SyncRoot = new();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };
        private static readonly Dictionary<string, EndpointDiagnosticEntry> Entries = new(StringComparer.OrdinalIgnoreCase);
        private static DateTimeOffset _startedAt = DateTimeOffset.Now;
        private static DateTimeOffset _updatedAt = _startedAt;

        public static void Reset()
        {
            lock (SyncRoot)
            {
                Entries.Clear();
                _startedAt = DateTimeOffset.Now;
                _updatedAt = _startedAt;
                WriteSnapshotLocked();
            }
        }

        public static void RecordSuccess(
            HttpRequestMessage request,
            NetworkUrlPurpose purpose,
            HttpStatusCode statusCode)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.RequestUri is null)
            {
                return;
            }

            lock (SyncRoot)
            {
                var entry = GetOrCreateEntryLocked(request, request.RequestUri, purpose);
                entry.TotalRequests++;
                entry.SuccessCount++;
                entry.LastOutcome = "success";
                entry.LastStatusCode = (int)statusCode;
                entry.LastFailureKind = null;
                entry.LastObservedAt = DateTimeOffset.Now;
                _updatedAt = entry.LastObservedAt;
                WriteSnapshotLocked();
            }
        }

        public static void RecordFailure(
            HttpRequestMessage request,
            NetworkUrlPurpose purpose,
            NetworkFailureKind? failureKind,
            HttpStatusCode? statusCode,
            string? failureSummary = null)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.RequestUri is null)
            {
                return;
            }

            lock (SyncRoot)
            {
                var entry = GetOrCreateEntryLocked(request, request.RequestUri, purpose);
                entry.TotalRequests++;
                entry.FailureCount++;
                entry.LastOutcome = "failure";
                entry.LastStatusCode = statusCode is null ? null : (int)statusCode.Value;
                entry.LastFailureKind = failureKind?.ToString();
                entry.LastFailureSummary = string.IsNullOrWhiteSpace(failureSummary)
                    ? null
                    : SensitiveTextRedactionUtility.Redact(failureSummary);
                entry.LastObservedAt = DateTimeOffset.Now;
                _updatedAt = entry.LastObservedAt;
                WriteSnapshotLocked();
            }
        }

        public static void RecordRetry(
            HttpRequestMessage request,
            NetworkUrlPurpose purpose)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.RequestUri is null)
            {
                return;
            }

            lock (SyncRoot)
            {
                var entry = GetOrCreateEntryLocked(request, request.RequestUri, purpose);
                entry.RetryCount++;
                entry.LastObservedAt = DateTimeOffset.Now;
                _updatedAt = entry.LastObservedAt;
                WriteSnapshotLocked();
            }
        }

        private static EndpointDiagnosticEntry GetOrCreateEntryLocked(
            HttpRequestMessage request,
            Uri uri,
            NetworkUrlPurpose purpose)
        {
            var endpoint = CreateEndpointSummary(uri);
            var key = $"{request.Method.Method}|{purpose}|{endpoint.Scheme}|{endpoint.Host}|{endpoint.Path}";
            if (!Entries.TryGetValue(key, out var entry))
            {
                entry = new EndpointDiagnosticEntry
                {
                    Method = request.Method.Method,
                    Purpose = purpose.ToString(),
                    Scheme = endpoint.Scheme,
                    Host = endpoint.Host,
                    Path = endpoint.Path,
                    QueryPresent = endpoint.QueryPresent,
                    FirstObservedAt = DateTimeOffset.Now,
                    LastObservedAt = DateTimeOffset.Now
                };
                Entries[key] = entry;
            }

            return entry;
        }

        private static EndpointSummary CreateEndpointSummary(Uri uri)
        {
            var path = uri.AbsolutePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = "/";
            }

            return new EndpointSummary(
                SensitiveTextRedactionUtility.Redact(uri.Scheme),
                SensitiveTextRedactionUtility.Redact(uri.IdnHost),
                SensitiveTextRedactionUtility.Redact(path),
                !string.IsNullOrWhiteSpace(uri.Query));
        }

        private static void WriteSnapshotLocked()
        {
            try
            {
                var snapshot = new OutboundNetworkDiagnosticsSnapshot(
                    CurrentSchemaVersion,
                    _startedAt,
                    _updatedAt,
                    Entries.Values
                        .OrderBy(entry => entry.Purpose, StringComparer.Ordinal)
                        .ThenBy(entry => entry.Host, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(entry => entry.Path, StringComparer.Ordinal)
                        .ToArray());
                AtomicFileUtility.WriteAllText(
                    RuntimePathUtility.GetWritableRuntimePath(DiagnosticsFileName),
                    JsonSerializer.Serialize(snapshot, JsonOptions));
            }
            catch
            {
            }
        }

        private sealed record EndpointSummary(
            string Scheme,
            string Host,
            string Path,
            bool QueryPresent);
    }

    internal sealed record OutboundNetworkDiagnosticsSnapshot(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("endpoints")] IReadOnlyList<EndpointDiagnosticEntry> Endpoints);

    internal sealed class EndpointDiagnosticEntry
    {
        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        [JsonPropertyName("purpose")]
        public string Purpose { get; set; } = string.Empty;

        [JsonPropertyName("scheme")]
        public string Scheme { get; set; } = string.Empty;

        [JsonPropertyName("host")]
        public string Host { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("query_present")]
        public bool QueryPresent { get; set; }

        [JsonPropertyName("total_requests")]
        public int TotalRequests { get; set; }

        [JsonPropertyName("success_count")]
        public int SuccessCount { get; set; }

        [JsonPropertyName("failure_count")]
        public int FailureCount { get; set; }

        [JsonPropertyName("retry_count")]
        public int RetryCount { get; set; }

        [JsonPropertyName("last_outcome")]
        public string? LastOutcome { get; set; }

        [JsonPropertyName("last_status_code")]
        public int? LastStatusCode { get; set; }

        [JsonPropertyName("last_failure_kind")]
        public string? LastFailureKind { get; set; }

        [JsonPropertyName("last_failure_summary")]
        public string? LastFailureSummary { get; set; }

        [JsonPropertyName("first_observed_at")]
        public DateTimeOffset FirstObservedAt { get; set; }

        [JsonPropertyName("last_observed_at")]
        public DateTimeOffset LastObservedAt { get; set; }
    }
}
