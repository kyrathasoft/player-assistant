$ErrorActionPreference = 'Stop'

$data = Get-Content -Raw -LiteralPath 'codex-scratch\blog-round4-near-raw.json' | ConvertFrom-Json
$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path 'Release\player-assistant.dll'))
$type = $assembly.GetType('PlayerAssistant.OrcishTranslatorUtility')
$entries = $type.GetMethod('GetLexiconEntries', [System.Reflection.BindingFlags]'Public,Static').Invoke($null, @())
$existing = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) { [void]$existing.Add($entry.English) }

$sources = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($word in $data.sources) { [void]$sources.Add($word) }

$rejected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    # Dictionary homographs, false stems, and terms deliberately removed during source curation.
    'blank','blank''s','blanked','blanker','blankest','blanking','blankness','blanks',
    'course','course''s','courses','hater','haters','levier','leviers','pluming','riming'
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
    'biomechanicals' = @('biomechanical')
    'bone-spears' = @('bone-spear')
    'colossi' = @('colossus')
    'crabwalks' = @('crabwalk','crabwalked','crabwalking')
    'delays' = @('delay','delayed','delaying')
    'dismisses' = @('dismiss','dismissed','dismissing')
    'frog-men' = @('frog-man')
    'infuses' = @('infuse','infused','infusing')
    'lock-picks' = @('lock-pick')
    'mega-dungeons' = @('mega-dungeon')
    'napped' = @('nap','napping','naps')
    'pack-mates' = @('pack-mate')
    'popped' = @('pop','popping','pops')
    'remarks' = @('remark','remarked','remarking')
    'scanned' = @('scan','scanning','scans')
    'soul-obols' = @('soul-obol')
    'spore-pods' = @('spore-pod')
    'trip-wires' = @('trip-wire')
    'uninjured' = @('injure','injured','injures','injuring')
    'unmoving' = @('move','moved','moves','moving')
    'unstrung' = @('unstring','unstringing','unstrings')
    'wraiths' = @('wraith')
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

$near | Set-Content -LiteralPath 'codex-scratch\blog-round4-near-candidates.txt' -Encoding utf8
$familyLines | Set-Content -LiteralPath 'codex-scratch\blog-round4-near-families.txt' -Encoding utf8
$combined | Set-Content -LiteralPath 'codex-scratch\candidates.txt' -Encoding utf8

[pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    sourceCount = $sources.Count
    nearKinCount = $near.Count
    combinedCount = $combined.Count
    lexiconEntryCount = $entries.Count
} | ConvertTo-Json | Set-Content -LiteralPath 'codex-scratch\blog-round4-near-manifest.json' -Encoding utf8

[pscustomobject]@{ sourceCount = $sources.Count; nearKinCount = $near.Count; combinedCount = $combined.Count } | ConvertTo-Json
