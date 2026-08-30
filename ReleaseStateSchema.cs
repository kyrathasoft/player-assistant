using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant;

internal enum ReleaseTransactionStage { Prepared, Staged, Verified, Promoted, Finalized, RolledBack }

internal sealed record ReleaseState(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("generation")] long Generation,
    [property: JsonPropertyName("stage")] ReleaseTransactionStage Stage,
    [property: JsonPropertyName("artifact_hash")] string ArtifactHash,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("correlation_id")] string CorrelationId);

internal static class ReleaseStateCompatibilityVerifier
{
    internal const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    internal static ReleaseState ParseAndVerify(string json, ReleaseState? previous = null, DateTimeOffset? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("Release state is empty.");
        ReleaseState state;
        try { state = JsonSerializer.Deserialize<ReleaseState>(json, Options) ?? throw new InvalidOperationException("Release state is null."); }
        catch (JsonException ex) { throw new InvalidOperationException("Release state is invalid JSON.", ex); }
        Verify(state, previous, nowUtc ?? DateTimeOffset.UtcNow);
        return state;
    }

    internal static void Verify(ReleaseState state, ReleaseState? previous, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != CurrentSchemaVersion) throw new InvalidOperationException($"Unsupported release-state schema version {state.SchemaVersion}.");
        if (state.Generation <= 0) throw new InvalidOperationException("Release state generation must be positive.");
        if (string.IsNullOrWhiteSpace(state.ArtifactHash) || state.ArtifactHash.Length != 64 || !state.ArtifactHash.All(Uri.IsHexDigit)) throw new InvalidOperationException("Release state artifact hash is invalid.");
        if (!Guid.TryParse(state.CorrelationId, out _)) throw new InvalidOperationException("Release state correlation ID is invalid.");
        if (state.CreatedAtUtc.Offset != TimeSpan.Zero || state.UpdatedAtUtc.Offset != TimeSpan.Zero) throw new InvalidOperationException("Release state timestamps must be UTC.");
        if (state.UpdatedAtUtc < state.CreatedAtUtc || state.UpdatedAtUtc > nowUtc) throw new InvalidOperationException("Release state timestamps are not valid.");
        if (previous is null) return;
        if (state.Generation < previous.Generation) throw new InvalidOperationException("Release state generation rollback is not allowed.");
        if (state.Generation == previous.Generation && StageRank(state.Stage) < StageRank(previous.Stage)) throw new InvalidOperationException("Release state transition rollback is not allowed.");
        if (state.Generation > previous.Generation && state.Stage != ReleaseTransactionStage.Prepared) throw new InvalidOperationException("A new generation must begin in Prepared stage.");
    }

    private static int StageRank(ReleaseTransactionStage stage) => stage switch
    {
        ReleaseTransactionStage.Prepared => 1, ReleaseTransactionStage.Staged => 2, ReleaseTransactionStage.Verified => 3,
        ReleaseTransactionStage.Promoted => 4, ReleaseTransactionStage.Finalized => 5, ReleaseTransactionStage.RolledBack => 6,
        _ => throw new InvalidOperationException("Unknown release-state stage.")
    };
}
