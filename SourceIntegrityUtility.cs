using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal sealed record SourceIntegrityShape(
        [property: JsonPropertyName("primary_metric")]
        string PrimaryMetric,

        [property: JsonPropertyName("metrics")]
        IReadOnlyDictionary<string, long> Metrics);

    internal sealed record SourceIntegrityRecord(
        [property: JsonPropertyName("schema_version")]
        int SchemaVersion,

        [property: JsonPropertyName("artifact_kind")]
        string ArtifactKind,

        [property: JsonPropertyName("source")]
        string Source,

        [property: JsonPropertyName("hash_algorithm")]
        string HashAlgorithm,

        [property: JsonPropertyName("sha256")]
        string Sha256,

        [property: JsonPropertyName("byte_length")]
        long ByteLength,

        [property: JsonPropertyName("recorded_at")]
        string RecordedAt,

        [property: JsonPropertyName("shape")]
        SourceIntegrityShape Shape);

    internal static class SourceIntegrityUtility
    {
        public const string SidecarFileName = "source-integrity.json";
        private const int SchemaVersion = 1;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static readonly Regex MarkdownLinkRegex = new(@"(\[[^\]]+\]\([^)]+\)|\[\[[^\]]+\]\])", RegexOptions.Compiled);

        public static async Task ValidateAndWriteTextFileAsync(
            string destinationPath,
            string source,
            string artifactKind,
            string content,
            SourceIntegrityShape shape,
            CancellationToken cancellationToken = default)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var record = ValidateContent(destinationPath, source, artifactKind, bytes, shape);
            await AtomicFileUtility.WriteAllTextAsync(destinationPath, content, cancellationToken).ConfigureAwait(false);
            await WriteRecordAsync(destinationPath, record, cancellationToken).ConfigureAwait(false);
        }

        public static async Task ValidateAndWriteBytesFileAsync(
            string destinationPath,
            string source,
            string artifactKind,
            byte[] content,
            SourceIntegrityShape shape,
            CancellationToken cancellationToken = default)
        {
            var record = ValidateContent(destinationPath, source, artifactKind, content, shape);
            await AtomicFileUtility.WriteAllBytesAsync(destinationPath, content, cancellationToken).ConfigureAwait(false);
            await WriteRecordAsync(destinationPath, record, cancellationToken).ConfigureAwait(false);
        }

        public static SourceIntegrityRecord ValidateTextContent(
            string artifactPath,
            string source,
            string artifactKind,
            string content,
            SourceIntegrityShape shape)
        {
            return ValidateContent(artifactPath, source, artifactKind, Encoding.UTF8.GetBytes(content), shape);
        }

        public static SourceIntegrityRecord ValidateContent(
            string artifactPath,
            string source,
            string artifactKind,
            byte[] content,
            SourceIntegrityShape shape)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(artifactKind);
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(shape);

            var record = new SourceIntegrityRecord(
                SchemaVersion,
                artifactKind,
                source,
                "SHA256",
                Convert.ToHexString(SHA256.HashData(content)),
                content.LongLength,
                DateTimeOffset.UtcNow.ToString("O"),
                shape);

            var previous = TryReadRecord(artifactPath);
            if (previous is null)
            {
                return record;
            }

            ValidateContinuity(previous, record, artifactPath);
            return record;
        }

        public static Task WriteRecordAsync(
            string artifactPath,
            SourceIntegrityRecord record,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
            ArgumentNullException.ThrowIfNull(record);

            return AtomicFileUtility.WriteAllTextAsync(
                GetSidecarPath(artifactPath),
                JsonSerializer.Serialize(record, JsonOptions),
                cancellationToken);
        }

        public static SourceIntegrityRecord? TryReadRecord(string artifactPath)
        {
            var sidecarPath = GetSidecarPath(artifactPath);
            if (!File.Exists(sidecarPath))
            {
                return null;
            }

            try
            {
                var record = JsonSerializer.Deserialize<SourceIntegrityRecord>(
                    File.ReadAllText(sidecarPath),
                    JsonOptions);
                return record is { SchemaVersion: SchemaVersion, HashAlgorithm: "SHA256" }
                    ? record
                    : null;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                StartupLoggingUtility.Append("source integrity load", ex);
                return null;
            }
        }

        public static string GetSidecarPath(string artifactPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

            if (Directory.Exists(artifactPath) || string.IsNullOrWhiteSpace(Path.GetExtension(artifactPath)))
            {
                return Path.Combine(artifactPath, SidecarFileName);
            }

            return artifactPath + "." + SidecarFileName;
        }

        public static SourceIntegrityShape CreateSitemapShape(string sitemapXml)
        {
            var document = System.Xml.Linq.XDocument.Parse(sitemapXml);
            return CreateShape(
                "url_count",
                new Dictionary<string, long>
                {
                    ["url_count"] = document.Descendants().LongCount(element => element.Name.LocalName == "loc")
                });
        }

        public static SourceIntegrityShape CreateMarkdownShape(string markdown)
        {
            var lines = markdown.Split('\n');
            return CreateShape(
                "line_count",
                new Dictionary<string, long>
                {
                    ["line_count"] = lines.LongLength,
                    ["heading_count"] = lines.LongCount(line => line.TrimStart().StartsWith("#", StringComparison.Ordinal)),
                    ["table_row_count"] = lines.LongCount(line => line.Trim().StartsWith("|", StringComparison.Ordinal)),
                    ["link_count"] = MarkdownLinkRegex.Matches(markdown).Count
                });
        }

        public static SourceIntegrityShape CreateKeywordIndexShape(
            long urlCount,
            long wordCount,
            long totalOccurrences)
        {
            return CreateShape(
                "url_count",
                new Dictionary<string, long>
                {
                    ["url_count"] = urlCount,
                    ["word_count"] = wordCount,
                    ["total_occurrences"] = totalOccurrences
                });
        }

        public static SourceIntegrityShape CreateRpolThreadShape(
            long postCount,
            long authorCount,
            long bodyCharacterCount)
        {
            return CreateShape(
                "post_count",
                new Dictionary<string, long>
                {
                    ["post_count"] = postCount,
                    ["author_count"] = authorCount,
                    ["body_character_count"] = bodyCharacterCount
                });
        }

        private static SourceIntegrityShape CreateShape(
            string primaryMetric,
            Dictionary<string, long> metrics)
        {
            return new SourceIntegrityShape(
                primaryMetric,
                metrics.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        }

        private static void ValidateContinuity(
            SourceIntegrityRecord previous,
            SourceIntegrityRecord current,
            string artifactPath)
        {
            if (!string.Equals(previous.ArtifactKind, current.ArtifactKind, StringComparison.Ordinal))
            {
                throw CreateTamperException(artifactPath, "artifact kind changed");
            }

            if (!string.Equals(previous.Source, current.Source, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateTamperException(artifactPath, "source URL changed");
            }

            if (string.Equals(previous.Sha256, current.Sha256, StringComparison.Ordinal))
            {
                return;
            }

            if (current.ByteLength < previous.ByteLength / 4)
            {
                throw CreateTamperException(artifactPath, "downloaded content shrank unexpectedly");
            }

            var primaryMetric = previous.Shape.PrimaryMetric;
            if (!string.Equals(primaryMetric, current.Shape.PrimaryMetric, StringComparison.Ordinal)
                || !previous.Shape.Metrics.TryGetValue(primaryMetric, out var previousPrimary)
                || !current.Shape.Metrics.TryGetValue(primaryMetric, out var currentPrimary))
            {
                throw CreateTamperException(artifactPath, "structural fingerprint is incomplete");
            }

            if (previousPrimary > 0 && currentPrimary == 0)
            {
                throw CreateTamperException(artifactPath, $"{primaryMetric} dropped to zero");
            }

            if (string.Equals(primaryMetric, "post_count", StringComparison.Ordinal)
                && currentPrimary < previousPrimary)
            {
                throw CreateTamperException(artifactPath, $"{primaryMetric} dropped from {previousPrimary} to {currentPrimary}");
            }

            if (previousPrimary >= 4 && currentPrimary < Math.Max(1, previousPrimary / 2))
            {
                throw CreateTamperException(artifactPath, $"{primaryMetric} dropped from {previousPrimary} to {currentPrimary}");
            }
        }

        private static InvalidOperationException CreateTamperException(string artifactPath, string reason)
        {
            return new InvalidOperationException(
                $"Authenticated source tamper detection rejected fetched content for '{artifactPath}' because {reason}. The previous last-known-good content was preserved. Contact the DM or release maintainer if the source intentionally changed.");
        }
    }
}
