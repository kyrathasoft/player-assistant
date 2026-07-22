param(
    [string] $OrcishLexiconPath = (Join-Path $PSScriptRoot '..\web-translator\orcish-lexicon.json'),
    [string] $ElvenDictionaryPath = (Join-Path $PSScriptRoot '..\web-translator\elven-translations.json'),
    [string] $FirstIterationPath = (Join-Path $PSScriptRoot '..\web-translator\elven-first-iteration.json'),
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\web-translator\elven-second-iteration.json'),
    [string] $ReportPath = (Join-Path $PSScriptRoot '..\web-translator\elven-second-iteration-report.json'),
    [int] $TargetCount = 5000
)

$ErrorActionPreference = 'Stop'

function Test-SimpleForm([string] $Value) { return $Value -match '^\p{L}+$' }
function Test-Vowel([char] $Character) {
    $normalized = $Character.ToString().Normalize([Text.NormalizationForm]::FormD)
    return $normalized.Length -gt 0 -and 'aeiouy'.Contains($normalized[0].ToString().ToLowerInvariant())
}

function Get-SoftMutation([string] $Root) {
    $replacements = @{ p='b'; t='d'; c='g'; b='v'; d='dh'; g=''; m='v'; s='h' }
    $initial = $Root.Substring(0, 1).ToLowerInvariant()
    if ($replacements.ContainsKey($initial)) { return $replacements[$initial] + $Root.Substring(1) }
    return $Root
}

function Get-DerivedForm([string] $Language, [string] $Root, [string] $Rule) {
    if ($Rule -eq 'semantic-extension') {
        if ($Root -match "^[\p{L} '\-’]+$") { return $Root }
        return $null
    }
    if (-not (Test-SimpleForm $Root)) { return $null }

    switch ($Rule) {
        'possessive' {
            if ($Language -eq 'Sindarin') { return $Root }
            if ($Language -eq 'Quenya') { return $Root + $(if (Test-Vowel $Root[$Root.Length - 1]) { 'va' } else { 'wa' }) }
        }
        'gerund' {
            if ($Language -eq 'Sindarin') { return $(if ($Root.EndsWith('a')) { $Root.Substring(0, $Root.Length - 1) + 'ad' } else { $Root + 'ed' }) }
            if ($Language -eq 'Quenya') {
                if ($Root.EndsWith('a')) { return $Root.Substring(0, $Root.Length - 1) + 'ie' }
                if ($Root.EndsWith('u')) { return $Root + 'ye' }
                return $Root + 'ie'
            }
        }
        'passive-participle' {
            if ($Language -eq 'Sindarin' -and $Root.EndsWith('a')) { return $Root.Substring(0, $Root.Length - 1) + 'annen' }
            if ($Language -eq 'Quenya') {
                if ($Root.EndsWith('a')) { return $Root.Substring(0, $Root.Length - 1) + 'aina' }
                if ($Root.EndsWith('u')) { return $Root + 'nwa' }
                return $Root + 'ina'
            }
        }
        'adverb' { if ($Language -eq 'Sindarin') { return $Root + 'ra' }; if ($Language -eq 'Quenya') { return $Root + 've' } }
        'abstract-noun' { if ($Language -eq 'Sindarin') { return $Root + 'th' }; if ($Language -eq 'Quenya') { return $Root + 'lë' } }
        'agent-noun' {
            $stem = if ($Root.EndsWith('a')) { $Root.Substring(0, $Root.Length - 1) } else { $Root }
            if ($Language -eq 'Sindarin') { return $stem + 'ron' }
            if ($Language -eq 'Quenya') { return $Root + 'mo' }
        }
        'comparative' {
            if ($Language -eq 'Quenya') { return 'an' + $Root }
            if ($Language -eq 'Sindarin') {
                if ($Root.StartsWith('t')) { return 'ath' + $Root.Substring(1) }
                if ($Root.StartsWith('p')) { return 'aff' + $Root.Substring(1) }
                if ($Root.StartsWith('b')) { return 'amm' + $Root.Substring(1) }
            }
        }
        'superlative' {
            if ($Language -eq 'Quenya') { return 'ari' + $Root }
            if ($Language -eq 'Sindarin') { return 'ro' + (Get-SoftMutation $Root) }
        }
        'able-adjective' { if ($Language -eq 'Sindarin') { return $Root + 'ui' }; if ($Language -eq 'Quenya') { return $Root + 'ima' } }
    }
    return $null
}

function Test-RegularPluralOrPresent([string] $Base, [string] $Term) {
    if ($Term -eq $Base + 's') { return $true }
    if ($Base -match '(?i)(s|x|z|ch|sh|o)$' -and $Term -eq $Base + 'es') { return $true }
    return $Base -match '(?i)[^aeiou]y$' -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'ies'
}

function Test-RegularPast([string] $Base, [string] $Term) {
    if ($Term -eq $Base + 'ed') { return $true }
    if ($Base.EndsWith('e') -and $Term -eq $Base + 'd') { return $true }
    return $Base -match '(?i)[^aeiou]y$' -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'ied'
}

function Test-RegularIng([string] $Base, [string] $Term) {
    if ($Term -eq $Base + 'ing') { return $true }
    if ($Base.EndsWith('ie') -and $Term -eq $Base.Substring(0, $Base.Length - 2) + 'ying') { return $true }
    if ($Base.EndsWith('e') -and -not $Base.EndsWith('ee') -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'ing') { return $true }
    return $Base -match '(?i)[^aeiou][aeiou][^aeiouwxy]$' -and $Term -eq $Base + $Base[$Base.Length - 1] + 'ing'
}

function Get-Rule([string] $Base, [string] $Term, [string] $PartOfSpeech) {
    if ($Term -eq $Base + "'s" -or ($Base.EndsWith('s') -and $Term -eq $Base + "'")) { return 'possessive' }
    if (Test-RegularPast $Base $Term) { return 'passive-participle' }
    if (Test-RegularIng $Base $Term) { return 'gerund' }
    if ($Term -eq $Base + 'ly' -or ($Base.EndsWith('y') -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'ily')) { return 'adverb' }
    if ($Term -eq $Base + 'ness' -or ($Base.EndsWith('y') -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'iness')) { return 'abstract-noun' }
    if ($Term -eq $Base + 'est' -or ($Base.EndsWith('e') -and $Term -eq $Base + 'st') -or ($Base.EndsWith('y') -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'iest')) { return 'superlative' }
    if ($Term -eq $Base + 'er' -or ($Base.EndsWith('e') -and $Term -eq $Base + 'r') -or ($Base.EndsWith('y') -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'ier')) {
        return $(if ($PartOfSpeech -eq 'adj') { 'comparative' } else { 'agent-noun' })
    }
    if ($Term -eq $Base + 'ers' -or ($Base.EndsWith('e') -and $Term -eq $Base + 'rs')) { return 'agent-noun' }
    if ($Term -eq $Base + 'able' -or ($Base.EndsWith('e') -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'able')) { return 'able-adjective' }
    if (Test-RegularPluralOrPresent $Base $Term) { return 'semantic-extension' }
    if ($Term -eq $Base + 'ment' -or $Term -eq $Base + 'ments') { return 'gerund' }
    return 'semantic-extension'
}

function Get-EnglishDerivedForms([string] $Base, [string] $PartOfSpeech) {
    $forms = [Collections.Generic.List[object]]::new()
    $forms.Add([pscustomobject]@{Term=$Base + "'s"; Rule='possessive'; Priority=1})
    if ($PartOfSpeech -eq 'n' -or $PartOfSpeech -eq 'adj') {
        $plural = if ($Base -match '(?i)[^aeiou]y$') { $Base.Substring(0, $Base.Length - 1) + 'ies' } elseif ($Base -match '(?i)(s|x|z|ch|sh|o)$') { $Base + 'es' } else { $Base + 's' }
        $forms.Add([pscustomobject]@{Term=$plural; Rule='semantic-extension'; Priority=3})
    }
    if ($PartOfSpeech -eq 'vb') {
        $past = if ($Base -match '(?i)[^aeiou]y$') { $Base.Substring(0, $Base.Length - 1) + 'ied' } elseif ($Base.EndsWith('e')) { $Base + 'd' } else { $Base + 'ed' }
        $ing = if ($Base.EndsWith('ie')) { $Base.Substring(0, $Base.Length - 2) + 'ying' } elseif ($Base.EndsWith('e') -and -not $Base.EndsWith('ee')) { $Base.Substring(0, $Base.Length - 1) + 'ing' } else { $Base + 'ing' }
        $forms.Add([pscustomobject]@{Term=$past; Rule='passive-participle'; Priority=2})
        $forms.Add([pscustomobject]@{Term=$ing; Rule='gerund'; Priority=2})
        $forms.Add([pscustomobject]@{Term=$Base + 'er'; Rule='agent-noun'; Priority=4})
        $forms.Add([pscustomobject]@{Term=$Base + 'able'; Rule='able-adjective'; Priority=5})
    }
    if ($PartOfSpeech -eq 'adj') {
        $forms.Add([pscustomobject]@{Term=$(if ($Base.EndsWith('y')) { $Base.Substring(0, $Base.Length - 1) + 'ily' } else { $Base + 'ly' }); Rule='adverb'; Priority=2})
        $forms.Add([pscustomobject]@{Term=$(if ($Base.EndsWith('y')) { $Base.Substring(0, $Base.Length - 1) + 'iness' } else { $Base + 'ness' }); Rule='abstract-noun'; Priority=3})
        $forms.Add([pscustomobject]@{Term=$(if ($Base.EndsWith('y')) { $Base.Substring(0, $Base.Length - 1) + 'ier' } elseif ($Base.EndsWith('e')) { $Base + 'r' } else { $Base + 'er' }); Rule='comparative'; Priority=4})
        $forms.Add([pscustomobject]@{Term=$(if ($Base.EndsWith('y')) { $Base.Substring(0, $Base.Length - 1) + 'iest' } elseif ($Base.EndsWith('e')) { $Base + 'st' } else { $Base + 'est' }); Rule='superlative'; Priority=4})
    }
    return $forms.ToArray()
}

$orcish = Get-Content -Raw $OrcishLexiconPath | ConvertFrom-Json
$finalized = Get-Content -Raw $ElvenDictionaryPath | ConvertFrom-Json
$first = Get-Content -Raw $FirstIterationPath | ConvertFrom-Json
$roots = @{}
$knownEnglish = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$existingForms = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($property in $finalized.translations.PSObject.Properties) {
    $roots[$property.Name] = [pscustomobject]@{Form=[string]$property.Value[0]; Language=[string]$property.Value[1]; PartOfSpeech=[string]$property.Value[3]}
    [void]$knownEnglish.Add($property.Name)
    [void]$existingForms.Add([string]$property.Value[0])
}
foreach ($entry in $first.entries) {
    $roots[$entry.english] = [pscustomobject]@{Form=$entry.elvish; Language=$entry.language; PartOfSpeech=$(if ($entry.partOfSpeech -eq 'noun') { 'n' } else { 'vb' })}
    [void]$knownEnglish.Add($entry.english)
    [void]$existingForms.Add($entry.elvish)
}

$orcishTerms = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($property in $orcish.terms.PSObject.Properties) { [void]$orcishTerms.Add($property.Name) }
$familyTerms = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$proposals = [Collections.Generic.List[object]]::new()
foreach ($property in $orcish.terms.PSObject.Properties) {
    $english = $property.Name
    if ($knownEnglish.Contains($english) -or $english -notmatch "^[a-z]+(?:'s|s')?$") { continue }
    foreach ($candidate in $property.Value[1]) {
        $tags = @($candidate[3])
        if ($tags -notcontains 'near-kin' -or $tags -notcontains 'derived-by-rule') { continue }
        $familyTags = @($tags | Where-Object { $_ -like 'family-*' })
        if ($familyTags.Count -ne 1) { continue }
        $baseEnglish = $familyTags[0].Substring(7)
        if (-not $roots.ContainsKey($baseEnglish)) { continue }
        [void]$familyTerms.Add($english)
        $root = $roots[$baseEnglish]
        $rule = Get-Rule $baseEnglish $english $root.PartOfSpeech
        $proposals.Add([pscustomobject]@{English=$english; BaseEnglish=$baseEnglish; Root=$root; Rule=$rule; Source='family-linked'; Priority=0})
        break
    }
}

$generic = [Collections.Generic.List[object]]::new()
foreach ($baseEnglish in @($roots.Keys | Sort-Object)) {
    if ($baseEnglish -notmatch '^[a-z]+$') { continue }
    $root = $roots[$baseEnglish]
    foreach ($form in Get-EnglishDerivedForms $baseEnglish $root.PartOfSpeech) {
        if (-not $orcishTerms.Contains($form.Term) -or $knownEnglish.Contains($form.Term) -or $familyTerms.Contains($form.Term)) { continue }
        $generic.Add([pscustomobject]@{English=$form.Term; BaseEnglish=$baseEnglish; Root=$root; Rule=$form.Rule; Source='spelling-derived'; Priority=$form.Priority})
    }
}

$needed = $TargetCount - $proposals.Count
if ($needed -lt 0) { throw "Family-linked proposals exceed target count $TargetCount." }
$seenGeneric = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($proposal in $generic | Sort-Object Priority, English, BaseEnglish) {
    if ($needed -eq 0) { break }
    if (-not $seenGeneric.Add($proposal.English)) { continue }
    $proposals.Add($proposal)
    $needed--
}
if ($needed -ne 0) { throw "Only $($TargetCount - $needed) safe proposals were found; $needed more are required." }

$generatedForms = @{}
$entries = [Collections.Generic.List[object]]::new()
$fallbacks = [Collections.Generic.List[object]]::new()
foreach ($proposal in $proposals | Sort-Object English) {
    $rule = $proposal.Rule
    $derived = Get-DerivedForm $proposal.Root.Language $proposal.Root.Form $rule
    $reason = $null
    $sharedForm = $false
    if ([string]::IsNullOrWhiteSpace($derived)) {
        $reason = 'unsupported-specific-rule'
    } elseif ($derived -ne $proposal.Root.Form -and $existingForms.Contains($derived)) {
        $reason = 'existing-form-collision'
    } elseif ($derived -ne $proposal.Root.Form -and $generatedForms.ContainsKey($derived)) {
        if ($generatedForms[$derived] -eq $proposal.Root.Form) {
            $sharedForm = $true
        } else {
            $reason = 'generated-form-collision'
        }
    }
    if ($null -ne $reason) {
        $fallbacks.Add([ordered]@{english=$proposal.English; baseEnglish=$proposal.BaseEnglish; attemptedRule=$rule; reason=$reason})
        $rule = 'semantic-extension'
        $derived = Get-DerivedForm $proposal.Root.Language $proposal.Root.Form $rule
    }
    if ([string]::IsNullOrWhiteSpace($derived)) { throw "No usable form could be produced for '$($proposal.English)'." }
    $generatedForms[$derived] = $proposal.Root.Form
    $partOfSpeech = switch ($rule) {
        'possessive' { 'adjective' }
        'gerund' { 'noun' }
        'passive-participle' { 'adjective' }
        'adverb' { 'adverb' }
        'abstract-noun' { 'noun' }
        'agent-noun' { 'noun' }
        'comparative' { 'adjective' }
        'superlative' { 'adjective' }
        'able-adjective' { 'adjective' }
        default { if ($proposal.Root.PartOfSpeech -eq 'vb') { 'verb' } elseif ($proposal.Root.PartOfSpeech -eq 'adj') { 'adjective' } else { 'noun' } }
    }
    $entryTags = [Collections.Generic.List[string]]::new()
    $entryTags.AddRange([string[]]@('derived-by-rule', 'second-iteration', $rule, "base-$($proposal.BaseEnglish)", $proposal.Source))
    if ($sharedForm) { $entryTags.Add('shared-form') }
    $entries.Add([ordered]@{
        english = $proposal.English
        elvish = $derived
        language = $proposal.Root.Language
        partOfSpeech = $partOfSpeech
        rootForms = @($proposal.Root.Form)
        tags = @($entryTags)
        derivation = "$($proposal.Root.Language) $rule of '$($proposal.Root.Form)' ($($proposal.BaseEnglish))"
    })
}

$byRule = [ordered]@{}
foreach ($group in $entries | Group-Object { $_['tags'][2] }) { $byRule[$group.Name] = $group.Count }
$byLanguage = [ordered]@{}
foreach ($group in $entries | Group-Object { $_['language'] }) { $byLanguage[$group.Name] = $group.Count }
$artifact = [ordered]@{
    schemaVersion = 1
    policy = 'Second deterministic morphology pass: 5,000 remaining Orcish-linked English terms derived from validated Elvish roots, with exact rule validation and guarded same-root fallback for unsafe sound changes or collisions.'
    entryCount = $entries.Count
    byLanguage = $byLanguage
    byRule = $byRule
    entries = @($entries)
}
$report = [ordered]@{
    schemaVersion = 1
    acceptedCount = $entries.Count
    familyLinkedCount = @($proposals | Where-Object Source -eq 'family-linked').Count
    spellingDerivedCount = @($proposals | Where-Object Source -eq 'spelling-derived').Count
    fallbackCount = $fallbacks.Count
    byLanguage = $byLanguage
    byRule = $byRule
    fallbacks = @($fallbacks)
}
foreach ($item in @(@{Path=$OutputPath; Value=$artifact}, @{Path=$ReportPath; Value=$report})) {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $item.Path)) | Out-Null
    [IO.File]::WriteAllText($item.Path, ($item.Value | ConvertTo-Json -Depth 8 -Compress), [Text.UTF8Encoding]::new($false))
}

[pscustomobject]@{Accepted=$entries.Count; FamilyLinked=$report.familyLinkedCount; SpellingDerived=$report.spellingDerivedCount; Fallbacks=$fallbacks.Count; OutputPath=[IO.Path]::GetFullPath($OutputPath)}
