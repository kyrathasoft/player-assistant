$ErrorActionPreference = 'Stop'

$data = Get-Content -Raw -LiteralPath 'codex-scratch\blog-round5-near-raw.json' | ConvertFrom-Json
$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path 'Release\player-assistant.dll'))
$type = $assembly.GetType('PlayerAssistant.OrcishTranslatorUtility')
$entries = $type.GetMethod('GetLexiconEntries', [System.Reflection.BindingFlags]'Public,Static').Invoke($null, @())
$existing = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) { [void]$existing.Add($entry.English) }
$sources = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($word in $data.sources) { [void]$sources.Add($word) }

$rejected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@('shiv','shiv''s','shivs','tidings') | ForEach-Object { [void]$rejected.Add($_) }

$candidateToSource = [ordered]@{}
foreach ($family in $data.families.PSObject.Properties) {
    foreach ($candidate in $family.Value) {
        if ($candidate.Length -lt 3 -or $candidate -match "s's$" -or $existing.Contains($candidate) -or $sources.Contains($candidate) -or $rejected.Contains($candidate)) { continue }
        if (-not $candidateToSource.Contains($candidate)) { $candidateToSource[$candidate] = $family.Name }
    }
}

$manualFamilies = [ordered]@{
    'advisors' = @('advisor')
    'aldermen' = @('alderman')
    'bear-men' = @('bear-man')
    'exsanguinate' = @('exsanguinated','exsanguinates','exsanguinating')
    'husbandmen' = @('husbandman')
    'knapping' = @('knap','knapped','knaps')
    'locksmiths' = @('locksmith')
    'overlaid' = @('overlay','overlaying','overlays')
    'pit-fighters' = @('pit-fighter')
    're-arranging' = @('re-arrange','re-arranged','re-arranges')
    'reciting' = @('recite','recited','recites')
    'reshuffle' = @('reshuffled','reshuffles','reshuffling')
    'seediest' = @('seedier','seedy')
    'snake-heads' = @('snake-head')
    'subvocalizes' = @('subvocalize','subvocalized','subvocalizing')
    'surface-worlders' = @('surface-worlder')
    'unwashed' = @('wash','washed','washes','washing')
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
$near | Set-Content -LiteralPath 'codex-scratch\blog-round5-near-candidates.txt' -Encoding utf8
$familyLines | Set-Content -LiteralPath 'codex-scratch\blog-round5-near-families.txt' -Encoding utf8
$combined | Set-Content -LiteralPath 'codex-scratch\candidates.txt' -Encoding utf8

[pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    sourceCount = $sources.Count
    nearKinCount = $near.Count
    combinedCount = $combined.Count
    lexiconEntryCount = $entries.Count
} | ConvertTo-Json | Set-Content -LiteralPath 'codex-scratch\blog-round5-near-manifest.json' -Encoding utf8
[pscustomobject]@{ source=$sources.Count; near=$near.Count; combined=$combined.Count } | ConvertTo-Json
