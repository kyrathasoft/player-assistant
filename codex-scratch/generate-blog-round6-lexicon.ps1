$ErrorActionPreference = 'Stop'
$sources = @(Get-Content -LiteralPath 'codex-scratch\blog-round6-source-candidates.txt' | Where-Object { $_.Trim() })
$nearKin = @(Get-Content -LiteralPath 'codex-scratch\blog-round6-near-families.txt' | Where-Object { $_.Trim() })
$outputPath = 'OrcishTranslatorUtility.BlogFinalSitemapBatch.cs'
if ($sources.Count -ne 22) { throw "Expected 22 source candidates, found $($sources.Count)." }
if ($nearKin.Count -ne 23) { throw "Expected 23 near-kin candidates, found $($nearKin.Count)." }
$sourceData = $sources -join "`r`n"
$nearKinData = $nearKin -join "`r`n"
$content = @"
namespace PlayerAssistant
{
    internal static partial class OrcishTranslatorUtility
    {
        private const string BlogFinalSitemapSourceCandidateData = """
$sourceData
""";

        private const string BlogFinalSitemapNearKinCandidateData = """
$nearKinData
""";

        private static IEnumerable<OrcishLexiconEntry> BuildBlogFinalSitemapCandidateEntries(IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var sourceRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sourceOrdinal = 0;
            foreach (var english in BlogFinalSitemapSourceCandidateData.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var root = `$"ulgraz-{EncodeTwentyPageOrdinal(sourceOrdinal++)}";
                sourceRoots.Add(english, root);
                var candidate = new OrcishLexiconEntry(english, root, Tags: ["blog", "blog-final-sitemap-candidate-batch", "generated", "review-promoted", "close-form-reviewed", `$"family-{english}"]);
                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
            var nearKinOrdinal = 0;
            foreach (var line in BlogFinalSitemapNearKinCandidateData.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|', 2, StringSplitOptions.TrimEntries);
                var english = fields[0];
                var sourceEnglish = fields[1];
                var candidate = new OrcishLexiconEntry(english, CreateThirtyPageNearKinForm(sourceRoots[sourceEnglish], english, nearKinOrdinal++), Tags: ["blog", "blog-final-sitemap-near-kin", "near-kin", "derived-by-rule", "review-promoted", "close-form-reviewed", `$"family-{sourceEnglish}"]);
                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
        }
    }
}
"@
Set-Content -LiteralPath $outputPath -Value $content -Encoding utf8
[pscustomobject]@{Source=$sources.Count;Near=$nearKin.Count;Total=$sources.Count+$nearKin.Count;Output=$outputPath}|ConvertTo-Json
