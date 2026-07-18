$ErrorActionPreference = 'Stop'
$data = Get-Content -Raw -LiteralPath 'codex-scratch\blog-round6-near-raw.json' | ConvertFrom-Json
$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path 'Release\player-assistant.dll'))
$type = $assembly.GetType('PlayerAssistant.OrcishTranslatorUtility')
$entries = $type.GetMethod('GetLexiconEntries', [System.Reflection.BindingFlags]'Public,Static').Invoke($null, @())
$existing = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) { [void]$existing.Add($entry.English) }
$sources = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($word in $data.sources) { [void]$sources.Add($word) }
$candidateToSource = [ordered]@{}
foreach ($family in $data.families.PSObject.Properties) {
    foreach ($candidate in $family.Value) {
        if ($candidate.Length -lt 3 -or $candidate -match "s's$" -or $existing.Contains($candidate) -or $sources.Contains($candidate)) { continue }
        if (-not $candidateToSource.Contains($candidate)) { $candidateToSource[$candidate] = $family.Name }
    }
}
$manualFamilies = [ordered]@{
    'boar-men' = @('boar-man')
    'dispatched' = @('dispatch','dispatches','dispatching')
    'favoured' = @('favour','favouring','favours')
    'goat-men' = @('goat-man')
    'inhabits' = @('inhabit','inhabited','inhabiting')
    'lowest-ranking' = @('low-ranking')
    'neighbouring' = @('neighbour','neighboured','neighbours')
}
foreach ($family in $manualFamilies.GetEnumerator()) {
    foreach ($candidate in $family.Value) {
        if (-not $existing.Contains($candidate) -and -not $sources.Contains($candidate)) { $candidateToSource[$candidate] = $family.Name }
    }
}
$near = @($candidateToSource.Keys | Sort-Object)
$families = @($candidateToSource.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name)|$($_.Value)" })
$combined = @($sources + $near | Sort-Object -Unique)
$near | Set-Content -LiteralPath 'codex-scratch\blog-round6-near-candidates.txt' -Encoding utf8
$families | Set-Content -LiteralPath 'codex-scratch\blog-round6-near-families.txt' -Encoding utf8
$combined | Set-Content -LiteralPath 'codex-scratch\candidates.txt' -Encoding utf8
[pscustomobject]@{generatedAt=(Get-Date).ToUniversalTime().ToString('o');sourceCount=$sources.Count;nearKinCount=$near.Count;combinedCount=$combined.Count;lexiconEntryCount=$entries.Count} | ConvertTo-Json | Set-Content -LiteralPath 'codex-scratch\blog-round6-near-manifest.json' -Encoding utf8
[pscustomobject]@{source=$sources.Count;near=$near.Count;combined=$combined.Count}|ConvertTo-Json
