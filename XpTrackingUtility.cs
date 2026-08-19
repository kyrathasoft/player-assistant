namespace PlayerAssistant
{
    internal sealed record PcXpTotal(string Name, int XpTotal, string? CanonicalId = null);

    internal sealed record XpTrackingSnapshot(string DateLabel, IReadOnlyList<PcXpTotal> Totals);

    internal static class XpTrackingUtility
    {
        private const string TrackingPageLabel = "XP Tracking page";

        public static async Task<IReadOnlyList<PcXpTotal>> GetCurrentXpTotalsAsync(
            CancellationToken cancellationToken = default)
        {
            var snapshot = await GetCurrentXpSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return snapshot.Totals;
        }

        public static async Task<XpTrackingSnapshot> GetCurrentXpSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            var markdown = await MarkdownUtility.GetMarkdownFromUrlAsync(
                AppSettingsUtility.XpTrackingUrl,
                cancellationToken).ConfigureAwait(false);

            if (markdown.StartsWith(MarkdownUtility.InvalidUrlMessage, StringComparison.Ordinal)
                || markdown.StartsWith(MarkdownUtility.UnresolvedUrlMessage, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"XP tracking markdown could not be fetched from {AppSettingsUtility.XpTrackingUrl}.");
            }

            return ParseCurrentXpSnapshot(markdown);
        }

        internal static IReadOnlyList<PcXpTotal> ParseCurrentXpTotals(string markdown)
        {
            return ParseCurrentXpSnapshot(markdown).Totals;
        }

        internal static PcXpTotal? FindXpTotalForIdentity(
            IReadOnlyList<PcXpTotal> totals,
            XpAuthenticatedIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(totals);
            ArgumentNullException.ThrowIfNull(identity);

            var matches = totals.Where(row =>
                string.Equals(row.CanonicalId, identity.CanonicalId, StringComparison.Ordinal)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        internal static XpTrackingSnapshot ParseCurrentXpSnapshot(string markdown)
        {
            ArgumentNullException.ThrowIfNull(markdown);

            var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');

            for (var index = 0; index < lines.Length; index++)
            {
                if (!TryGetDateLabel(lines[index], out var dateLabel))
                {
                    continue;
                }

                var tableStart = FindTableHeader(lines, index + 1);
                if (tableStart < 0)
                {
                    throw new InvalidOperationException("The latest XP tracking date does not have a markdown table.");
                }

                return new XpTrackingSnapshot(dateLabel, ParseXpTable(lines, tableStart));
            }

            throw new InvalidOperationException("XP tracking markdown does not contain an 'As of' date section.");
        }

        internal static string FormatUserFacingFailureMessage(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            return string.Join(
                Environment.NewLine + Environment.NewLine,
                $"XP totals could not be loaded from the {TrackingPageLabel}.",
                "The page may be unavailable, changed, or in an unexpected format. Please contact the DM so they can confirm the XP Tracking page and app configuration.",
                $"Technical detail: {SanitizeUserFacingDetail(exception.Message)}");
        }

        internal static string FormatMissingPcFailureMessage(string characterName)
        {
            var displayName = string.IsNullOrWhiteSpace(characterName) ? "the requested character" : $"'{characterName.Trim()}'";
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                $"No XP total was found for {displayName}.",
                "Please contact the DM so they can confirm the XP Tracking page contains your current PC row.");
        }

        private static string SanitizeUserFacingDetail(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return "No additional detail was available.";
            }

            return System.Text.RegularExpressions.Regex.Replace(
                detail,
                @"https?://\S+",
                $"the {TrackingPageLabel}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static bool TryGetDateLabel(string line, out string dateLabel)
        {
            dateLabel = line.Trim().TrimStart('#').Trim();
            return dateLabel.StartsWith("As of ", StringComparison.OrdinalIgnoreCase);
        }

        private static int FindTableHeader(string[] lines, int startIndex)
        {
            for (var index = startIndex; index < lines.Length; index++)
            {
                var cells = SplitTableRow(lines[index]);
                if (cells.Length == 0)
                {
                    continue;
                }

                if (cells.Any(cell => string.Equals(cell, "Name", StringComparison.OrdinalIgnoreCase))
                    && cells.Any(cell => string.Equals(cell, "Canonical ID", StringComparison.OrdinalIgnoreCase))
                    && cells.Any(cell => string.Equals(cell, "XP Total", StringComparison.OrdinalIgnoreCase)))
                {
                    return index;
                }

                if (TryGetDateLabel(lines[index], out _))
                {
                    return -1;
                }
            }

            return -1;
        }

        private static IReadOnlyList<PcXpTotal> ParseXpTable(string[] lines, int headerIndex)
        {
            var headerCells = SplitTableRow(lines[headerIndex]);
            var nameIndex = Array.FindIndex(
                headerCells,
                cell => string.Equals(cell, "Name", StringComparison.OrdinalIgnoreCase));
            var canonicalIdIndex = Array.FindIndex(
                headerCells,
                cell => string.Equals(cell, "Canonical ID", StringComparison.OrdinalIgnoreCase));
            var xpIndex = Array.FindIndex(
                headerCells,
                cell => string.Equals(cell, "XP Total", StringComparison.OrdinalIgnoreCase));
            if (nameIndex < 0 || canonicalIdIndex < 0 || xpIndex < 0)
            {
                throw new InvalidOperationException(
                    "The latest XP tracking table must contain Name, Canonical ID, and XP Total columns.");
            }

            var results = new List<PcXpTotal>();
            var canonicalIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = headerIndex + 1; index < lines.Length; index++)
            {
                var line = lines[index];
                if (!line.TrimStart().StartsWith('|'))
                {
                    break;
                }

                var cells = SplitTableRow(line);
                if (cells.Length == 0 || IsSeparatorRow(cells))
                {
                    continue;
                }

                if (cells.Length <= Math.Max(Math.Max(nameIndex, canonicalIdIndex), xpIndex))
                {
                    break;
                }

                var name = CleanMarkdownCell(cells[nameIndex]);
                var canonicalId = CleanMarkdownCell(cells[canonicalIdIndex]);
                var xpTotal = ParseXpTotal(cells[xpIndex]);
                if (name.Length > 0 && IsValidCanonicalId(canonicalId) && canonicalIds.Add(canonicalId))
                {
                    results.Add(new PcXpTotal(name, xpTotal, canonicalId));
                }
                else
                {
                    throw new InvalidOperationException(
                        "The latest XP tracking table contains a blank, invalid, or duplicate Canonical ID.");
                }
            }

            if (results.Count == 0)
            {
                throw new InvalidOperationException("The latest XP tracking table did not contain any PC XP totals.");
            }

            return results;
        }

        private static string[] SplitTableRow(string line)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|'))
            {
                return [];
            }

            return trimmed.Trim('|')
                .Split('|')
                .Select(cell => cell.Trim())
                .ToArray();
        }

        private static bool IsSeparatorRow(string[] cells)
        {
            return cells.All(cell =>
                cell.Length > 0
                && cell.All(character => character is '-' or ':' or ' '));
        }

        private static string CleanMarkdownCell(string value)
        {
            var cleaned = value.Trim();
            if (cleaned.StartsWith("[[", StringComparison.Ordinal)
                && cleaned.EndsWith("]]", StringComparison.Ordinal))
            {
                cleaned = cleaned[2..^2];
                var aliasIndex = cleaned.LastIndexOf('|');
                if (aliasIndex >= 0)
                {
                    cleaned = cleaned[(aliasIndex + 1)..];
                }
            }

            return cleaned.Trim();
        }

        private static int ParseXpTotal(string value)
        {
            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (digits.Length == 0 || !int.TryParse(digits, out var xpTotal))
            {
                throw new InvalidOperationException($"XP total '{value}' is not a number.");
            }

            return xpTotal;
        }

        private static bool IsValidCanonicalId(string value)
        {
            return value.Length is >= 3 and <= 100
                && value[0] is >= 'a' and <= 'z' or >= '0' and <= '9'
                && value.All(character => character is >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '-'
                    or '_'
                    or '.'
                    or ':');
        }
    }
}
