$ErrorActionPreference = 'Stop'

$data = Get-Content -Raw -LiteralPath 'codex-scratch\blog-round3-near-raw.json' | ConvertFrom-Json
$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path 'Release\player-assistant.dll'))
$type = $assembly.GetType('PlayerAssistant.OrcishTranslatorUtility')
$entries = $type.GetMethod('GetLexiconEntries', [System.Reflection.BindingFlags]'Public,Static').Invoke($null, @())
$existing = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) { [void]$existing.Add($entry.English) }

$sources = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($word in $data.sources) { [void]$sources.Add($word) }

$rejected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    # Dictionary homographs and false stems found during review.
    'broad''s','broads','crater','craters','desert''s','exceptionable','executable','executive',
    'glassed','glassing','minored','minoring','plumed','pluming','raged','rimed','riming',
    'sing''s','singable','singer','singers','singly','snicker','snickers',
    # The source cull intentionally excluded conversational "course"; do not reintroduce its family.
    'course','course''s','coursed','courses','coursing',
    # Technically possible, but poor near-kin translation candidates.
    'questioner','questioners','questionings'
) | ForEach-Object { [void]$rejected.Add($_) }

$candidateToSource = [ordered]@{}
foreach ($family in $data.families.PSObject.Properties) {
    foreach ($candidate in $family.Value) {
        if ($candidate.Length -lt 3 -or $candidate -match "s's$" -or $existing.Contains($candidate) -or $sources.Contains($candidate) -or $rejected.Contains($candidate)) { continue }
        if (-not $candidateToSource.Contains($candidate)) { $candidateToSource[$candidate] = $family.Name }
    }
}

# Irregular forms and transparent compound families that Hunspell does not connect.
$manualFamilies = [ordered]@{
    'bagged' = @('bag','bags','bagging')
    'beast-kings' = @('beast-king')
    'bloodstones' = @('bloodstone')
    'cat-golems' = @('cat-golem')
    'cave-ins' = @('cave-in')
    'charred' = @('char','charring','chars')
    'crow-feathers' = @('crow-feather')
    'eye-stones' = @('eye-stone')
    'fire-sticks' = @('fire-stick')
    'khopeshes' = @('khopesh')
    'nocking' = @('nock','nocked')
    'pale-skins' = @('pale-skin')
    'pinning' = @('pin','pinned','pins')
    'pipe-crawlers' = @('pipe-crawler')
    'scholar-kings' = @('scholar-king')
    'shadow-stalkers' = @('shadow-stalker')
    'shadow-wardens' = @('shadow-warden')
    'singed' = @('singe','singeing','singes')
    'sling-stones' = @('sling-stone')
    'soul-chains' = @('soul-chain')
    'sound-snarers' = @('sound-snarer')
    'stabbed' = @('stab','stabbing','stabs')
    'stitched-ones' = @('stitched-one')
    'stirs' = @('stir','stirred','stirring')
    'stung' = @('sting','stinging','stings')
    'tax-men' = @('tax-man')
    'track-rails' = @('track-rail')
    'tying' = @('tie','tied','ties')
    'unfurled' = @('unfurl','unfurling','unfurls')
    'unlatch' = @('unlatched','unlatches','unlatching')
    'unsealing' = @('unseal','unsealed','unseals')
    'unsheathed' = @('unsheathe','unsheathes','unsheathing')
    'unslung' = @('unsling','unslinging','unslings')
    'untangling' = @('untangle','untangled','untangles')
    'untie' = @('untied','unties','untying')
    'unwind' = @('unwinding','unwinds','unwound')
    'whipped' = @('whip','whipping','whips')
}
foreach ($family in $manualFamilies.GetEnumerator()) {
    foreach ($candidate in $family.Value) {
        if (-not $existing.Contains($candidate) -and -not $sources.Contains($candidate) -and -not $rejected.Contains($candidate)) {
            $candidateToSource[$candidate] = $family.Name
        }
    }
}

$near = @($candidateToSource.Keys | Sort-Object)
$familyLines = @($candidateToSource.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name)|$($_.Value)" })
$combined = @($sources + $near | Sort-Object -Unique)

$sources | Sort-Object | Set-Content -LiteralPath 'codex-scratch\blog-round3-source-candidates.txt' -Encoding utf8
$near | Set-Content -LiteralPath 'codex-scratch\blog-round3-near-candidates.txt' -Encoding utf8
$familyLines | Set-Content -LiteralPath 'codex-scratch\blog-round3-near-families.txt' -Encoding utf8
$combined | Set-Content -LiteralPath 'codex-scratch\candidates.txt' -Encoding utf8

[pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    sourceCount = $sources.Count
    nearKinCount = $near.Count
    combinedCount = $combined.Count
    rejectedNearKinCount = $rejected.Count
    lexiconEntryCount = $entries.Count
} | ConvertTo-Json | Set-Content -LiteralPath 'codex-scratch\blog-round3-near-manifest.json' -Encoding utf8

[pscustomobject]@{ sourceCount = $sources.Count; nearKinCount = $near.Count; combinedCount = $combined.Count } | ConvertTo-Json
