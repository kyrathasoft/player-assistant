using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant;

internal sealed record StartupTransactionJournal(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("transaction_id")] string TransactionId,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("target_path")] string TargetPath,
    [property: JsonPropertyName("expected_hash")] string ExpectedHash,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc);

internal static class StartupTransactionRecovery
{
    internal const int CurrentSchemaVersion = 1;
    internal static IEnumerable<string> Discover(string directory) => Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.transaction.json", SearchOption.TopDirectoryOnly) : [];
    internal static StartupTransactionJournal Read(string path)
    {
        var journal = JsonSerializer.Deserialize<StartupTransactionJournal>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Transaction journal is empty.");
        if (journal.SchemaVersion != CurrentSchemaVersion || !Guid.TryParse(journal.TransactionId, out _) || journal.CreatedAtUtc.Offset != TimeSpan.Zero) throw new InvalidOperationException("Transaction journal schema or timestamp is invalid.");
        if (Path.GetFullPath(journal.TargetPath) != journal.TargetPath || string.IsNullOrWhiteSpace(journal.ExpectedHash) || journal.ExpectedHash.Length != 64 || !journal.ExpectedHash.All(Uri.IsHexDigit)) throw new InvalidOperationException("Transaction journal path or hash is invalid.");
        return journal;
    }
    internal static bool TargetMatches(StartupTransactionJournal journal)
    {
        if (!File.Exists(journal.TargetPath)) return false;
        return string.Equals(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(journal.TargetPath))), journal.ExpectedHash, StringComparison.OrdinalIgnoreCase);
    }
    internal static void Recover(string directory, Action<StartupTransactionJournal> resume, Action<StartupTransactionJournal> rollback, IClock? clock = null)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var now = (clock ?? new SystemClock()).UtcNow;
        DateBoundary.RequireUtc(now, "clock");
        foreach (var path in Discover(directory))
        {
            var journal = Read(path);
            if (!Path.GetFullPath(journal.TargetPath).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Transaction journal target is outside the transaction directory; journal evidence was retained.");
            if (journal.CreatedAtUtc > now)
                throw new InvalidOperationException("Transaction journal timestamp is in the future; journal evidence was retained.");
            if (journal.Stage is "prepared" or "staged")
            {
                if (!TargetMatches(journal)) throw new InvalidOperationException("Ambiguous startup transaction cannot be recovered; journal evidence was retained.");
                resume(journal);
                File.Delete(path);
                continue;
            }
            if (journal.Stage is "verified" or "promoted" or "finalized")
            {
                if (!TargetMatches(journal)) throw new InvalidOperationException("Ambiguous startup transaction cannot be recovered; journal evidence was retained.");
                File.Delete(path);
                continue;
            }
            if (journal.Stage == "rolled_back") { rollback(journal); File.Delete(path); continue; }
            throw new InvalidOperationException("Unknown startup transaction stage.");
        }
    }
}
