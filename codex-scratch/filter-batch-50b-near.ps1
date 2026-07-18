$ErrorActionPreference = 'Stop'

$data = Get-Content -Raw -LiteralPath 'codex-scratch\batch-50b-near-raw.json' | ConvertFrom-Json
$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path 'Release\player-assistant.dll'))
$type = $assembly.GetType('PlayerAssistant.OrcishTranslatorUtility')
$method = $type.GetMethod('GetLexiconEntries', [System.Reflection.BindingFlags]'Public,Static')
$entries = $method.Invoke($null, @())
$existing = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) { [void]$existing.Add($entry.English) }
$source = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($word in $data.sources) { [void]$source.Add($word) }

$bad = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    'minion','minions','lateraled','lateraling',"waters's",'cr',"cr's","combs's","demographics's",
    'forwardest','missive','pigment','piped','piper','pipers','piping','ripen','ripens','riper','ripest',
    'somewhats',"weeks's",'wined','wining','doz','dozed','dozes','dozing','corn','corned','corning','corns',
    'cursive','butch',"butch's",'butches','hamburg',"hamburg's",'hamburgs','grippe',"grippe's",
    "hostilities's","odds's",'patientest','precised','precises','precising','restive','rustication','saucer',
    'saucers','skew',"skew's",'skewed','skewing','skews','stealth','tinged','twined','twiner','twiners',
    'twining',"wits's","less's",'julies','lemming','lemmings','latiner','mister','misters','fib',"fib's",
    'fiber','fibs','cubed','cuber','cubers','cubing','flashest','rockies','taped','taper','tapers','taping',
    'dimer','dined','diner','diners','dining','laded','lading','ladings','luster','meter','meters','moped',
    'moper','mopers','moping','out',"out's",'outed','outing','outings','outs','pee',"pee's",'peed','pees','seamen'
) | ForEach-Object { [void]$bad.Add($_) }

$candidateToSource = [ordered]@{}
foreach ($property in $data.families.PSObject.Properties) {
    $sourceEnglish = $property.Name
    foreach ($english in $property.Value) {
        if ($existing.Contains($english) -or $source.Contains($english) -or $bad.Contains($english)) { continue }
        if (-not $candidateToSource.Contains($english)) { $candidateToSource[$english] = $sourceEnglish }
    }
}

$candidateLines = @($candidateToSource.Keys | Sort-Object)
$familyLines = @($candidateToSource.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name)|$($_.Value)" })
Set-Content -LiteralPath 'codex-scratch\batch-50b-near-candidates.txt' -Value $candidateLines
Set-Content -LiteralPath 'codex-scratch\batch-50b-near-families.txt' -Value $familyLines
[pscustomobject]@{ SourceCount = $source.Count; NearCount = $candidateLines.Count; LexiconCount = $entries.Count } | ConvertTo-Json -Compress
