param([string]$Round = 'round1', [string]$Corpus = 'gutenberg', [string[]]$ExcludeExistingTag = @())
$ErrorActionPreference = 'Stop'
$prefix = "codex-scratch\$Corpus-$Round"
$data = Get-Content -Raw -LiteralPath "$prefix-near-raw.json" | ConvertFrom-Json
$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path 'Release\player-assistant.dll'))
$type = $assembly.GetType('PlayerAssistant.OrcishTranslatorUtility')
$entries = $type.GetMethod('GetLexiconEntries', [System.Reflection.BindingFlags]'Public,Static').Invoke($null, @())
$existing = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) {
    $excluded = $false
    foreach ($tag in $ExcludeExistingTag) {
        if ($entry.Tags -contains $tag) { $excluded = $true; break }
    }
    if (-not $excluded) { [void]$existing.Add($entry.English) }
}
$sources = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($word in $data.sources) { [void]$sources.Add($word) }
$rejected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    # Reviewed false stems and dictionary homographs.
    'miser','miser''s','miserly','misers','nob','nobs','shill','shill''s','shilled','shilling','shills',
    'crater','craters','desert''s','exceptionable','executable','executive','glassed','glassing','plumed','pluming','raged','rimed','riming','snicker','snickers',
    # Awkward or malformed dictionary products seen in earlier batches.
    'somewhats','patientest','forwardest','acridest','balmiest','brashest','coyest','falser','falsest','flashest','gravest','grislier','grisliest','saltest','sheerer','sheerest','slipperier','slipperiest','vivider','vividest'
) | ForEach-Object { [void]$rejected.Add($_) }

$candidateToSource = [ordered]@{}
foreach ($family in $data.families.PSObject.Properties) {
    foreach ($candidate in $family.Value) {
        if ($candidate.Length -lt 3 -or $candidate -match "s's$" -or $existing.Contains($candidate) -or $sources.Contains($candidate) -or $rejected.Contains($candidate)) { continue }
        if (-not $candidateToSource.Contains($candidate)) { $candidateToSource[$candidate] = $family.Name }
    }
}
$manualFamilies = [ordered]@{
    'allotted' = @('allot','allots','allotting')
    'begged' = @('beg','begging','begs')
    'boughs' = @('bough')
    'clung' = @('cling','clinging','clings')
    'countrymen' = @('countryman')
    'expelled' = @('expel','expelling','expels')
    'fitted' = @('fit','fits','fitting')
    'flew' = @('flies','fly','flying','flown')
    'forbade' = @('forbid','forbidden','forbidding','forbids')
    'gentlemen' = @('gentleman')
    'knelt' = @('kneel','kneeling','kneels')
    'knives' = @('knife')
    'manned' = @('man','manning','mans')
    'mice' = @('mouse')
    'rubbed' = @('rub','rubbing','rubs')
    'shrank' = @('shrink','shrinking','shrinks','shrunk')
    'smote' = @('smite','smites','smiting','smitten')
    'snapped' = @('snap','snapping','snaps')
    'sobbed' = @('sob','sobbing','sobs')
    'spake' = @('speak','speaking','speaks','spoken')
    'sped' = @('speed','speeding','speeds')
    'strewn' = @('strew','strewed','strewing','strews')
    'stripped' = @('strip','stripping','strips')
    'strode' = @('stride','stridden','strides','striding')
    'strove' = @('strive','striven','strives','striving')
    'swam' = @('swim','swimming','swims','swum')
    'swung' = @('swing','swinging','swings')
    'tore' = @('tear','tearing','tears','torn')
    'trod' = @('tread','treading','treads','trodden')
    'trot' = @('trotted','trotting')
    'undertaken' = @('undertake','undertakes','undertaking')
    'wept' = @('weep','weeping','weeps')
    'workmen' = @('workman')
    'youths' = @('youth')
}
foreach ($family in $manualFamilies.GetEnumerator()) {
    foreach ($candidate in $family.Value) {
        if (-not $existing.Contains($candidate) -and -not $sources.Contains($candidate) -and -not $rejected.Contains($candidate)) { $candidateToSource[$candidate] = $family.Name }
    }
}
$preAnachronismNear = @($candidateToSource.Keys | Sort-Object)
$preAnachronismNear | Set-Content -LiteralPath "$prefix-near-pre-anachronism.txt" -Encoding utf8
python 'codex-scratch\filter-anachronistic-candidates.py' "$prefix-near-pre-anachronism.txt" "$prefix-near-filtered.txt" "$prefix-near-anachronism-rejected.txt"
$near = @(Get-Content -LiteralPath "$prefix-near-filtered.txt")
$acceptedNear = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($word in $near) { [void]$acceptedNear.Add($word) }
$families = @($candidateToSource.GetEnumerator() | Where-Object { $acceptedNear.Contains($_.Name) } | Sort-Object Name | ForEach-Object { "$($_.Name)|$($_.Value)" })
$combined = @($sources + $near | Sort-Object -Unique)
$near | Set-Content -LiteralPath "$prefix-near-candidates.txt" -Encoding utf8
$families | Set-Content -LiteralPath "$prefix-near-families.txt" -Encoding utf8
$combined | Set-Content -LiteralPath 'codex-scratch\candidates.txt' -Encoding utf8
$nearAnachronismRejected = @(Get-Content -LiteralPath "$prefix-near-anachronism-rejected.txt")
[pscustomobject]@{generatedAt=(Get-Date).ToUniversalTime().ToString('o');sourceCount=$sources.Count;nearKinCount=$near.Count;combinedCount=$combined.Count;nearKinAnachronismRejectedCount=$nearAnachronismRejected.Count;lexiconEntryCount=$entries.Count} | ConvertTo-Json | Set-Content -LiteralPath "$prefix-near-manifest.json" -Encoding utf8
[pscustomobject]@{source=$sources.Count;near=$near.Count;combined=$combined.Count;nearAnachronismRejected=$nearAnachronismRejected.Count}|ConvertTo-Json
