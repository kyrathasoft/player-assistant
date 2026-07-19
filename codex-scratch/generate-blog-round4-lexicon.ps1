$ErrorActionPreference = 'Stop'

$sources = @(Get-Content -LiteralPath 'codex-scratch\blog-round4-source-candidates.txt' | Where-Object { $_.Trim() })
$nearKin = @(Get-Content -LiteralPath 'codex-scratch\blog-round4-near-families.txt' | Where-Object { $_.Trim() })
$outputPath = 'OrcishTranslatorUtility.BlogMixedHighYieldBatch.cs'

if ($sources.Count -ne 374) { throw "Expected 374 source candidates, found $($sources.Count)." }
if ($nearKin.Count -ne 413) { throw "Expected 413 near-kin candidates, found $($nearKin.Count)." }
if (@($sources | Sort-Object -Unique).Count -ne $sources.Count) { throw 'Source candidates are not unique.' }
if (@($nearKin | ForEach-Object { ($_ -split '\|', 2)[0] } | Sort-Object -Unique).Count -ne $nearKin.Count) { throw 'Near-kin candidates are not unique.' }

$sourceData = $sources -join "`r`n"
$nearKinData = $nearKin -join "`r`n"
$content = @"
namespace PlayerAssistant
{
    internal static partial class OrcishTranslatorUtility
    {
        private const string BlogMixedHighYieldSourceCandidateData = """
$sourceData
""";

        private const string BlogMixedHighYieldNearKinCandidateData = """
$nearKinData
""";

        private static IEnumerable<OrcishLexiconEntry> BuildBlogMixedHighYieldCandidateEntries(
            IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var sourceRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sourceOrdinal = 0;

            foreach (var english in BlogMixedHighYieldSourceCandidateData.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var root = `$"skraz-{EncodeTwentyPageOrdinal(sourceOrdinal++)}";
                sourceRoots.Add(english, root);
                var candidate = new OrcishLexiconEntry(
                    english,
                    root,
                    Tags:
                    [
                        "blog",
                        "blog-mixed-high-yield-candidate-batch",
                        "generated",
                        "review-promoted",
                        "close-form-reviewed",
                        `$"family-{english}"
                    ]);

                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }

            var nearKinOrdinal = 0;
            foreach (var line in BlogMixedHighYieldNearKinCandidateData.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|', 2, StringSplitOptions.TrimEntries);
                var english = fields[0];
                var sourceEnglish = fields[1];
                var root = sourceRoots[sourceEnglish];
                var candidate = new OrcishLexiconEntry(
                    english,
                    CreateThirtyPageNearKinForm(root, english, nearKinOrdinal++),
                    Tags:
                    [
                        "blog",
                        "blog-mixed-high-yield-near-kin",
                        "near-kin",
                        "derived-by-rule",
                        "review-promoted",
                        "close-form-reviewed",
                        `$"family-{sourceEnglish}"
                    ]);

                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
        }
    }
}
"@

Set-Content -LiteralPath $outputPath -Value $content -Encoding utf8
[pscustomobject]@{ SourceCount = $sources.Count; NearKinCount = $nearKin.Count; TotalCount = $sources.Count + $nearKin.Count; OutputPath = $outputPath } | ConvertTo-Json
