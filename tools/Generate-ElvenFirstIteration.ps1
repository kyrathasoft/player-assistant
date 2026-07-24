param(
    [string] $OrcishLexiconPath = (Join-Path $PSScriptRoot '..\web-translator\orcish-lexicon.json'),
    [string] $ElvenDictionaryPath = (Join-Path $PSScriptRoot '..\web-translator\elven-translations.json'),
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\web-translator\elven-first-iteration.json'),
    [string] $ReportPath = (Join-Path $PSScriptRoot '..\web-translator\elven-first-iteration-report.json')
)

$ErrorActionPreference = 'Stop'

function Test-Vowel([char] $Character) {
    $normalized = $Character.ToString().Normalize([Text.NormalizationForm]::FormD)
    return $normalized.Length -gt 0 -and 'aeiouy'.Contains($normalized[0].ToString().ToLowerInvariant())
}

function Get-VowelNuclei([string] $Value) {
    $result = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $Value.Length; $index++) {
        if (-not (Test-Vowel $Value[$index])) { continue }
        $length = if ($index + 1 -lt $Value.Length -and (Test-Vowel $Value[$index + 1])) { 2 } else { 1 }
        $result.Add([pscustomobject]@{
            Index = $index
            Length = $length
            Text = $Value.Substring($index, $length).ToLowerInvariant()
        })
        $index += $length - 1
    }
    return $result.ToArray()
}

function Test-IIntrusion([string] $Tail) {
    $normalized = $Tail.ToLowerInvariant()
    if ($normalized -in @('m', 'ng')) { return $false }
    if ($normalized -in @('ss', 'll', 'nn', 'ph', 'th', 'ch', 'dh')) { return $true }
    return $normalized.Length -eq 1
}

function Get-SindarinFinalPluralVowel([string] $Vowel, [string] $Tail, [bool] $Monosyllabic) {
    switch ($Vowel) {
        'a' { if (Test-IIntrusion $Tail) { return 'ai' }; return 'e' }
        'â' { return 'ai' }
        'e' { return 'i' }
        'ê' { return 'î' }
        'o' { return 'y' }
        'u' { return 'y' }
        'ô' { if ($Monosyllabic) { return 'ui' }; return $null }
        'û' { if ($Monosyllabic) { return 'ui' }; return $null }
        'au' { return 'oe' }
        'oe' { return 'ui' }
        { $_ -in @('i', 'î', 'y', 'ŷ', 'ae', 'ai', 'ei', 'ui') } { return $Vowel }
        default { return $null }
    }
}

function Get-SindarinInternalPluralVowel([string] $Vowel) {
    switch ($Vowel) {
        'a' { return 'e' }
        'o' { return 'e' }
        'u' { return 'y' }
        { $_ -in @('e', 'i', 'y', 'â', 'ê', 'î', 'ô', 'û', 'ŷ', 'ae', 'ai', 'au', 'ei', 'oe', 'ui') } { return $Vowel }
        default { return $null }
    }
}

function Get-ElvenDerivedForm([string] $Language, [string] $Root, [string] $Inflection) {
    if ($Root -notmatch '^\p{L}+$') { return $null }

    if ($Language -eq 'Quenya') {
        switch ($Inflection) {
            'plural' {
                if ($Root -match '(?i)(ië|ie|lë|le)$') { return $Root + 'r' }
                if ($Root -match '(?i)[eë]$') { return $Root.Substring(0, $Root.Length - 1) + 'i' }
                if (Test-Vowel $Root[$Root.Length - 1]) { return $Root + 'r' }
                return $Root + 'i'
            }
            'present-active' {
                if (Test-Vowel $Root[$Root.Length - 1]) { return $Root }
                return $Root + 'ë'
            }
            'active-participle' { return $Root + 'ila' }
        }
    }

    if ($Language -ne 'Sindarin') { return $null }
    switch ($Inflection) {
        'present-active' {
            if ($Root.EndsWith('a', [StringComparison]::OrdinalIgnoreCase)) { return $Root }
            $nuclei = @(Get-VowelNuclei $Root)
            if ($nuclei.Count -ne 1 -or $nuclei[0].Length -ne 1) { return $null }
            $lengthened = @{ a='â'; e='ê'; i='î'; o='ô'; u='û'; y='ŷ' }[$nuclei[0].Text]
            if ($null -eq $lengthened) { return $null }
            return $Root.Substring(0, $nuclei[0].Index) + $lengthened + $Root.Substring($nuclei[0].Index + 1)
        }
        'active-participle' {
            if ($Root.EndsWith('a', [StringComparison]::OrdinalIgnoreCase)) {
                return $Root.Substring(0, $Root.Length - 1) + 'ol'
            }
            return $Root + 'ol'
        }
        'plural' {
            $nuclei = @(Get-VowelNuclei $Root)
            if ($nuclei.Count -eq 0) { return $null }
            $final = $nuclei[-1]
            $tail = $Root.Substring($final.Index + $final.Length)
            if ($tail.Length -eq 0 -or $tail -notmatch '^\p{L}+$') { return $null }
            $finalReplacement = Get-SindarinFinalPluralVowel $final.Text $tail ($nuclei.Count -eq 1)
            if ($null -eq $finalReplacement) { return $null }
            $result = $Root
            for ($index = $nuclei.Count - 1; $index -ge 0; $index--) {
                $nucleus = $nuclei[$index]
                $replacement = if ($index -eq $nuclei.Count - 1) {
                    $finalReplacement
                } else {
                    Get-SindarinInternalPluralVowel $nucleus.Text
                }
                if ($null -eq $replacement) { return $null }
                $result = $result.Substring(0, $nucleus.Index) + $replacement + $result.Substring($nucleus.Index + $nucleus.Length)
            }
            return $result
        }
    }
    return $null
}

function Get-EnglishInflection([string] $Base, [string] $Term, [string] $PartOfSpeech) {
    if ($PartOfSpeech -eq 'n') {
        $forms = @($Base + 's')
        if ($Base -match '(?i)(s|x|z|ch|sh|o)$') { $forms += $Base + 'es' }
        if ($Base -match '(?i)[^aeiou]y$') { $forms += $Base.Substring(0, $Base.Length - 1) + 'ies' }
        if ($forms -contains $Term) { return 'plural' }
        return $null
    }

    if ($PartOfSpeech -ne 'vb') { return $null }
    $presentForms = @($Base + 's')
    if ($Base -match '(?i)(s|x|z|ch|sh|o)$') { $presentForms += $Base + 'es' }
    if ($Base -match '(?i)[^aeiou]y$') { $presentForms += $Base.Substring(0, $Base.Length - 1) + 'ies' }
    if ($presentForms -contains $Term) { return 'present-active' }

    $progressiveForms = @($Base + 'ing')
    if ($Base -match '(?i)ie$') {
        $progressiveForms += $Base.Substring(0, $Base.Length - 2) + 'ying'
    } elseif ($Base -match '(?i)e$' -and $Base -notmatch '(?i)ee$') {
        $progressiveForms += $Base.Substring(0, $Base.Length - 1) + 'ing'
    }
    if ($Base -match '(?i)[^aeiou][aeiou][^aeiouwxy]$') {
        $progressiveForms += $Base + $Base[$Base.Length - 1] + 'ing'
    }
    if ($progressiveForms -contains $Term) { return 'active-participle' }
    return $null
}

$orcish = Get-Content -Raw $OrcishLexiconPath | ConvertFrom-Json
$elven = Get-Content -Raw $ElvenDictionaryPath | ConvertFrom-Json
$existingEnglish = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$existingElvish = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$rootsByEnglish = @{}
foreach ($property in $elven.translations.PSObject.Properties) {
    [void]$existingEnglish.Add($property.Name)
    [void]$existingElvish.Add([string]$property.Value[0])
    $rootsByEnglish[$property.Name] = $property.Value
}

$acceptedForms = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$entries = [Collections.Generic.List[object]]::new()
$rejections = [Collections.Generic.List[object]]::new()
foreach ($property in $orcish.terms.PSObject.Properties) {
    $english = $property.Name
    if ($existingEnglish.Contains($english) -or $english -notmatch '^[a-z]+$') { continue }

    foreach ($candidate in $property.Value[1]) {
        $tags = @($candidate[3])
        if ($tags -notcontains 'near-kin' -or $tags -notcontains 'derived-by-rule') { continue }
        $familyTags = @($tags | Where-Object { $_ -like 'family-*' })
        if ($familyTags.Count -ne 1) { continue }
        $baseEnglish = $familyTags[0].Substring(7)
        if (-not $rootsByEnglish.ContainsKey($baseEnglish)) { continue }

        $rootRecord = $rootsByEnglish[$baseEnglish]
        $partOfSpeech = [string]$rootRecord[3]
        $inflection = Get-EnglishInflection $baseEnglish $english $partOfSpeech
        if ($null -eq $inflection) { continue }

        $language = [string]$rootRecord[1]
        $rootForm = [string]$rootRecord[0]
        $derived = Get-ElvenDerivedForm $language $rootForm $inflection
        if ([string]::IsNullOrWhiteSpace($derived)) {
            $rejections.Add([ordered]@{ english=$english; baseEnglish=$baseEnglish; root=$rootForm; language=$language; inflection=$inflection; reason='unsupported-morphology' })
            break
        }
        if (($existingElvish.Contains($derived) -and $derived -ne $rootForm) -or -not $acceptedForms.Add($derived)) {
            $rejections.Add([ordered]@{ english=$english; baseEnglish=$baseEnglish; root=$rootForm; language=$language; inflection=$inflection; derived=$derived; reason='elvish-form-collision' })
            break
        }

        $pos = if ($inflection -eq 'plural') { 'noun' } else { 'verb' }
        $entries.Add([ordered]@{
            english = $english
            elvish = $derived
            language = $language
            partOfSpeech = $pos
            rootForms = @($rootForm)
            tags = @('derived-by-rule', 'first-iteration', $inflection, "base-$baseEnglish")
            derivation = "$language $inflection of '$rootForm' ($baseEnglish)"
        })
        break
    }
}

$orderedEntries = @($entries | Sort-Object { $_['english'] })
$byInflection = [ordered]@{}
foreach ($group in $orderedEntries | Group-Object { $_['tags'][2] }) { $byInflection[$group.Name] = $group.Count }
$byLanguage = [ordered]@{}
foreach ($group in $orderedEntries | Group-Object { $_['language'] }) { $byLanguage[$group.Name] = $group.Count }
$artifact = [ordered]@{
    schemaVersion = 1
    policy = 'Conservative first pass over missing single English words: regular noun plurals, simple-present active verbs, and present active participles derived from finalized Sindarin-first/Quenya-fallback roots.'
    entryCount = $orderedEntries.Count
    byLanguage = $byLanguage
    byInflection = $byInflection
    entries = $orderedEntries
}
$report = [ordered]@{
    schemaVersion = 1
    acceptedCount = $orderedEntries.Count
    rejectedCount = $rejections.Count
    byLanguage = $byLanguage
    byInflection = $byInflection
    rejections = @($rejections | Sort-Object english)
}

foreach ($item in @(@{Path=$OutputPath; Value=$artifact}, @{Path=$ReportPath; Value=$report})) {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $item.Path)) | Out-Null
    [IO.File]::WriteAllText($item.Path, ($item.Value | ConvertTo-Json -Depth 8 -Compress), [Text.UTF8Encoding]::new($false))
}

[pscustomobject]@{
    Accepted = $orderedEntries.Count
    Rejected = $rejections.Count
    OutputPath = [IO.Path]::GetFullPath($OutputPath)
}
