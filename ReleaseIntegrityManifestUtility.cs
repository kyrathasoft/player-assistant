using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant
{
    internal static class ReleaseIntegrityManifestUtility
    {
        public const string FileName = "release-manifest.json";

        public static IReadOnlyList<string> ValidateIfPresent(string runtimeDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);

            var manifestPath = RuntimePathUtility.CombineUnderBase(runtimeDirectory, FileName);
            if (!File.Exists(manifestPath))
            {
                return [];
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<ReleaseIntegrityManifest>(
                    File.ReadAllText(manifestPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (manifest is null)
                {
                    return [$"{FileName} could not be parsed."];
                }

                return Validate(runtimeDirectory, manifest);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return [$"{FileName} could not be validated: {ex.Message}"];
            }
        }

        private static IReadOnlyList<string> Validate(
            string runtimeDirectory,
            ReleaseIntegrityManifest manifest)
        {
            var issues = new List<string>();
            if (manifest.SchemaVersion != 1)
            {
                issues.Add($"{FileName} schema_version '{manifest.SchemaVersion}' is not supported.");
            }

            if (!string.Equals(manifest.HashAlgorithm, "SHA256", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"{FileName} must use SHA256 hashes.");
            }

            if (manifest.Files is null || manifest.Files.Length == 0)
            {
                issues.Add($"{FileName} does not list any files.");
                return issues;
            }

            foreach (var entry in manifest.Files)
            {
                ValidateEntry(runtimeDirectory, entry, issues);
            }

            return issues;
        }

        private static void ValidateEntry(
            string runtimeDirectory,
            ReleaseIntegrityManifestFile? entry,
            List<string> issues)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.RelativePath))
            {
                issues.Add($"{FileName} contains an empty file entry.");
                return;
            }

            string path;
            try
            {
                path = RuntimePathUtility.CombineUnderBase(runtimeDirectory, entry.RelativePath);
            }
            catch (InvalidOperationException ex)
            {
                issues.Add($"{FileName} contains an unsafe path '{entry.RelativePath}': {ex.Message}");
                return;
            }

            if (!File.Exists(path))
            {
                issues.Add($"{FileName} missing manifested file '{entry.RelativePath}'.");
                return;
            }

            var fileInfo = new FileInfo(path);
            if (entry.Length != fileInfo.Length)
            {
                issues.Add($"{FileName} length mismatch for '{entry.RelativePath}'.");
            }

            var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"{FileName} SHA256 mismatch for '{entry.RelativePath}'.");
            }
        }

        private sealed record ReleaseIntegrityManifest(
            [property: JsonPropertyName("schema_version")]
            int SchemaVersion,
            [property: JsonPropertyName("hash_algorithm")]
            string? HashAlgorithm,
            [property: JsonPropertyName("files")]
            ReleaseIntegrityManifestFile[]? Files);

        private sealed record ReleaseIntegrityManifestFile(
            [property: JsonPropertyName("relative_path")]
            string? RelativePath,
            [property: JsonPropertyName("length")]
            long Length,
            [property: JsonPropertyName("sha256")]
            string? Sha256);
    }
}
