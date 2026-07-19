param(
    [string]$Round = 'round1',
    [string]$Corpus = 'gutenberg',
    [int]$ExpectedSource = 893,
    [int]$ExpectedNear = 1588,
    [string]$OutputPath = 'OrcishTranslatorUtility.GutenbergCorpusBatch.cs',
    [string]$MemberPrefix = 'GutenbergCorpus',
    [string]$RootPrefix = 'guthraz',
    [string]$SourceTag = 'gutenberg-corpus-candidate-batch',
    [string]$NearTag = 'gutenberg-corpus-near-kin'
)
$ErrorActionPreference = 'Stop'
$prefix = "codex-scratch\$Corpus-$Round"
$sources = @(Get-Content -LiteralPath "$prefix-source-candidates.txt" | Where-Object { $_.Trim() })
$nearKin = @(Get-Content -LiteralPath "$prefix-near-families.txt" | Where-Object { $_.Trim() })
if ($sources.Count -ne $ExpectedSource) { throw "Expected $ExpectedSource source candidates, found $($sources.Count)." }
if ($nearKin.Count -ne $ExpectedNear) { throw "Expected $ExpectedNear near-kin candidates, found $($nearKin.Count)." }
if (@($sources | Sort-Object -Unique).Count -ne $sources.Count) { throw 'Source candidates are not unique.' }
if (@($nearKin | ForEach-Object { ($_ -split '\|', 2)[0] } | Sort-Object -Unique).Count -ne $nearKin.Count) { throw 'Near-kin candidates are not unique.' }
$sourceData = $sources -join "`r`n"
$nearKinData = $nearKin -join "`r`n"
$content = @"
namespace PlayerAssistant
{
    internal static partial class OrcishTranslatorUtility
    {
        private const string ${MemberPrefix}SourceCandidateData = """
$sourceData
""";

        private const string ${MemberPrefix}NearKinCandidateData = """
$nearKinData
""";

        private static IEnumerable<OrcishLexiconEntry> Build${MemberPrefix}CandidateEntries(IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var sourceRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sourceOrdinal = 0;
            foreach (var english in ${MemberPrefix}SourceCandidateData.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var root = `$"$RootPrefix-{EncodeTwentyPageOrdinal(sourceOrdinal++)}";
                sourceRoots.Add(english, root);
                var candidate = new OrcishLexiconEntry(english, root, Tags: ["gutenberg", "$SourceTag", "generated", "review-promoted", "close-form-reviewed", `$"family-{english}"]);
                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
            var nearKinOrdinal = 0;
            foreach (var line in ${MemberPrefix}NearKinCandidateData.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|', 2, StringSplitOptions.TrimEntries);
                var english = fields[0];
                var sourceEnglish = fields[1];
                var candidate = new OrcishLexiconEntry(english, CreateThirtyPageNearKinForm(sourceRoots[sourceEnglish], english, nearKinOrdinal++), Tags: ["gutenberg", "$NearTag", "near-kin", "derived-by-rule", "review-promoted", "close-form-reviewed", `$"family-{sourceEnglish}"]);
                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
        }
    }
}
"@
Set-Content -LiteralPath $OutputPath -Value $content -Encoding utf8
[pscustomobject]@{Source=$sources.Count;Near=$nearKin.Count;Total=$sources.Count+$nearKin.Count;Output=$OutputPath}|ConvertTo-Json
