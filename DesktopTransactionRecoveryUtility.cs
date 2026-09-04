using System.Security.Cryptography;
using System.Text.Json;

namespace PlayerAssistant;

internal static class DesktopTransactionRecoveryUtility
{
    internal const string JournalFileName = "release-update-generation.journal.json";
    private const int CurrentSchemaVersion = 2;
    internal enum RecoveryStatus { NothingFound, Recovered, Ambiguous }

    internal static RecoveryStatus RecoverDiscovered(string runtimeDirectory)
    {
        var statuses = new[] { runtimeDirectory, Path.Combine(runtimeDirectory, "installer") }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Recover)
            .ToArray();
        return statuses.Contains(RecoveryStatus.Ambiguous) ? RecoveryStatus.Ambiguous
            : statuses.Contains(RecoveryStatus.Recovered) ? RecoveryStatus.Recovered
            : RecoveryStatus.NothingFound;
    }

    internal static RecoveryStatus Recover(string runtimeDirectory)
    {
        var root = Path.GetFullPath(runtimeDirectory);
        var journalPath = Path.Combine(root, JournalFileName);
        if (!File.Exists(journalPath)) return RecoveryStatus.NothingFound;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(journalPath));
            var journal = ReadJournal(document.RootElement, root);
            var states = journal.Artifacts.Select(a => ReadState(a, journal)).ToArray();
            if (states.Any(x => x == ArtifactState.Invalid || x == ArtifactState.Unknown)) return PreserveAmbiguous(journalPath);
            if (states.All(x => x == ArtifactState.Generation))
            {
                Complete(journalPath, journal);
                return RecoveryStatus.Recovered;
            }
            Rollback(journalPath, journal);
            return RecoveryStatus.Recovered;
        }
        catch { return PreserveAmbiguous(journalPath); }
    }

    private static Journal ReadJournal(JsonElement root, string runtimeDirectory)
    {
        if (root.GetProperty("schema_version").GetInt32() != CurrentSchemaVersion
            || !string.Equals(root.GetProperty("state").GetString(), "promoting", StringComparison.Ordinal)) throw new InvalidDataException();
        var finalDirectory = SafePath(runtimeDirectory, root.GetProperty("final_directory").GetString());
        var generation = SafePath(finalDirectory, root.GetProperty("generation").GetString());
        var backup = SafePath(finalDirectory, root.GetProperty("backup").GetString());
        var artifacts = root.GetProperty("artifacts").EnumerateArray().Select(x => new Artifact(
            SafeName(x.GetProperty("name").GetString()), Hash(x.GetProperty("generation_sha256").GetString()),
            x.GetProperty("prior_sha256").ValueKind == JsonValueKind.Null ? null : Hash(x.GetProperty("prior_sha256").GetString()),
            x.GetProperty("had_prior").GetBoolean())).ToArray();
        if (artifacts.Length == 0 || artifacts.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != artifacts.Length) throw new InvalidDataException();
        return new Journal(finalDirectory, generation, backup, artifacts);
    }

    private static ArtifactState ReadState(Artifact artifact, Journal journal)
    {
        var target = Path.Combine(journal.FinalDirectory, artifact.Name);
        var generated = Path.Combine(journal.Generation, artifact.Name);
        var old = Path.Combine(journal.Backup, artifact.Name);
        if (!File.Exists(generated) || !HashMatches(generated, artifact.GenerationHash)) return ArtifactState.Invalid;
        if (artifact.HadPrior && (!File.Exists(old) || !HashMatches(old, artifact.PriorHash!))) return ArtifactState.Invalid;
        if (!File.Exists(target)) return artifact.HadPrior ? ArtifactState.Unknown : ArtifactState.Prior;
        var hash = ComputeHash(target);
        if (hash.Equals(artifact.GenerationHash, StringComparison.OrdinalIgnoreCase)) return ArtifactState.Generation;
        if (artifact.PriorHash is not null && hash.Equals(artifact.PriorHash, StringComparison.OrdinalIgnoreCase)) return ArtifactState.Prior;
        return ArtifactState.Unknown;
    }

    private static void Complete(string journalPath, Journal journal)
    {
        DeleteDirectory(journal.Backup); DeleteDirectory(journal.Generation); File.Delete(journalPath);
    }

    private static void Rollback(string journalPath, Journal journal)
    {
        foreach (var artifact in journal.Artifacts)
        {
            var target = Path.Combine(journal.FinalDirectory, artifact.Name);
            var old = Path.Combine(journal.Backup, artifact.Name);
            if (artifact.HadPrior) File.Copy(old, target, true);
            else if (File.Exists(target)) File.Delete(target);
            if (artifact.HadPrior && !HashMatches(target, artifact.PriorHash!)) throw new IOException();
        }
        Complete(journalPath, journal);
    }

    private static RecoveryStatus PreserveAmbiguous(string journalPath)
    {
        try { File.Copy(journalPath, journalPath + ".ambiguous-" + DateTime.UtcNow.Ticks + ".json"); } catch { }
        return RecoveryStatus.Ambiguous;
    }

    private static string SafeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.GetFileName(value) != value || value is "." or "..") throw new InvalidDataException();
        return value;
    }
    private static string SafePath(string baseDirectory, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException();
        return RuntimePathUtility.EnsurePathUnderBase(baseDirectory, value);
    }
    private static string Hash(string? value) => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit) ? value : throw new InvalidDataException();
    private static bool HashMatches(string path, string expected) => File.Exists(path) && ComputeHash(path).Equals(expected, StringComparison.OrdinalIgnoreCase);
    private static string ComputeHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void DeleteDirectory(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }
    private sealed record Journal(string FinalDirectory, string Generation, string Backup, Artifact[] Artifacts);
    private sealed record Artifact(string Name, string GenerationHash, string? PriorHash, bool HadPrior);
    private enum ArtifactState { Prior, Generation, Unknown, Invalid }
}
