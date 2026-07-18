$ErrorActionPreference = 'Stop'
$sources = @(Get-Content -LiteralPath 'codex-scratch\blog-round5-source-candidates.txt' | Where-Object { $_.Trim() })
$nearKin = @(Get-Content -LiteralPath 'codex-scratch\blog-round5-near-families.txt' | Where-Object { $_.Trim() })
$outputPath = 'OrcishTranslatorUtility.BlogRandomHundredBatch.cs'
if ($sources.Count -ne 142) { throw "Expected 142 source candidates, found $($sources.Count)." }
if ($nearKin.Count -ne 168) { throw "Expected 168 near-kin candidates, found $($nearKin.Count)." }
if (@($sources | Sort-Object -Unique).Count -ne $sources.Count) { throw 'Source candidates are not unique.' }
if (@($nearKin | ForEach-Object { ($_ -split '\|', 2)[0] } | Sort-Object -Unique).Count -ne $nearKin.Count) { throw 'Near-kin candidates are not unique.' }

$sourceData = $sources -join "`r`n"
$nearKinData = $nearKin -join "`r`n"
$content = @"
namespace PlayerAssistant
{
    internal static partial class OrcishTranslatorUtility
    {
        private const string BlogRandomHundredSourceCandidateData = """
$sourceData
""";

        private const string BlogRandomHundredNearKinCandidateData = """
$nearKinData
""";

        private static IEnumerable<OrcishLexiconEntry> BuildBlogRandomHundredCandidateEntries(
            IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var sourceRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sourceOrdinal = 0;
            foreach (var english in BlogRandomHundredSourceCandidateData.Split(
                         ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var root = `$"zugraz-{EncodeTwentyPageOrdinal(sourceOrdinal++)}";
                sourceRoots.Add(english, root);
                var candidate = new OrcishLexiconEntry(
                    english, root,
                    Tags: ["blog", "blog-random-hundred-candidate-batch", "generated", "review-promoted", "close-form-reviewed", `$"family-{english}"]);
                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }

            var nearKinOrdinal = 0;
            foreach (var line in BlogRandomHundredNearKinCandidateData.Split(
                         ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|', 2, StringSplitOptions.TrimEntries);
                var english = fields[0];
                var sourceEnglish = fields[1];
                var root = sourceRoots[sourceEnglish];
                var candidate = new OrcishLexiconEntry(
                    english, CreateThirtyPageNearKinForm(root, english, nearKinOrdinal++),
                    Tags: ["blog", "blog-random-hundred-near-kin", "near-kin", "derived-by-rule", "review-promoted", "close-form-reviewed", `$"family-{sourceEnglish}"]);
                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
        }
    }
}
"@
Set-Content -LiteralPath $outputPath -Value $content -Encoding utf8
[pscustomobject]@{ Source=$sources.Count; Near=$nearKin.Count; Total=$sources.Count+$nearKin.Count; Output=$outputPath } | ConvertTo-Json
