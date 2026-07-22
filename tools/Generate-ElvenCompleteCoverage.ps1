param(
    [string] $OrcishLexiconPath = (Join-Path $PSScriptRoot '..\web-translator\orcish-lexicon.json'),
    [string] $ElvenLexiconPath = (Join-Path $PSScriptRoot '..\web-translator\elven-lexicon.json'),
    [string] $ElvenDictionaryPath = (Join-Path $PSScriptRoot '..\web-translator\elven-translations.json'),
    [string] $FirstIterationPath = (Join-Path $PSScriptRoot '..\web-translator\elven-first-iteration.json'),
    [string] $SecondIterationPath = (Join-Path $PSScriptRoot '..\web-translator\elven-second-iteration.json'),
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\web-translator\elven-complete-coverage.json'),
    [string] $ReportPath = (Join-Path $PSScriptRoot '..\web-translator\elven-complete-coverage-report.json')
)

$ErrorActionPreference = 'Stop'

function Get-NormalizedLetters([string] $Value) {
    $builder = [Text.StringBuilder]::new()
    foreach ($character in $Value.Normalize([Text.NormalizationForm]::FormD).ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($character) -eq [Globalization.UnicodeCategory]::NonSpacingMark) { continue }
        if ([char]::IsLetter($character)) { [void]$builder.Append([char]::ToLowerInvariant($character)) }
    }
    return $builder.ToString()
}

function Get-HashBytes([string] $Value) {
    return [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value))
}

function Test-RepeatedRun([string] $Value) {
    $run = 1
    for ($index = 1; $index -lt $Value.Length; $index++) {
        $run = if ($Value[$index] -eq $Value[$index - 1]) { $run + 1 } else { 1 }
        if ($run -ge 3) { return $true }
    }
    return $false
}

function Test-ConsonantRun([string] $Value) {
    $run = 0
    foreach ($character in (Get-NormalizedLetters $Value).ToCharArray()) {
        $run = if ('aeiouy'.Contains($character)) { 0 } else { $run + 1 }
        if ($run -ge 4) { return $true }
    }
    return $false
}

$exactForms = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$wildcardForms = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$deletedForms = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

function Add-CollisionIndex([string] $Form, [string] $RootKey) {
    $normalized = Get-NormalizedLetters $Form
    if ($normalized.Length -eq 0) { return }
    if (-not $exactForms.ContainsKey($normalized)) { $exactForms.Add($normalized, $RootKey) }
    for ($index = 0; $index -lt $normalized.Length; $index++) {
        [void]$wildcardForms.Add($normalized.Substring(0, $index) + '*' + $normalized.Substring($index + 1))
        [void]$deletedForms.Add($normalized.Remove($index, 1))
    }
}

function Get-CollisionKind([string] $Form, [string] $RootKey, [bool] $AllowSharedRoot) {
    $normalized = Get-NormalizedLetters $Form
    if ($normalized.Length -lt 3) { return 'too-short' }
    if ($exactForms.ContainsKey($normalized)) {
        if ($AllowSharedRoot -and $exactForms[$normalized] -eq $RootKey) { return 'intentional-shared-root' }
        return 'exact-collision'
    }
    for ($index = 0; $index -lt $normalized.Length; $index++) {
        if ($wildcardForms.Contains($normalized.Substring(0, $index) + '*' + $normalized.Substring($index + 1))) { return 'close-substitution' }
        if ($exactForms.ContainsKey($normalized.Remove($index, 1))) { return 'close-deletion' }
    }
    if ($deletedForms.Contains($normalized)) { return 'close-insertion' }
    return $null
}

function New-SindarinRoot([string] $English, [int] $Nonce) {
    $normalized = Get-NormalizedLetters $English
    if ($normalized.Length -eq 0) { $normalized = 'term' }
    $hash = Get-HashBytes "$normalized|$Nonce|sindarin-neologism-v1"
    $onsets = @('b','c','d','f','g','h','l','m','n','p','r','s','t','v','br','cr','dr','gl','gr','gw','lh','ph','rh','th')
    $vowels = @('a','e','i','o','u','ae','ai','ei','ui')
    $codas = @('','n','r','l','s')
    $initials = @{ a=''; e=''; i=''; o=''; u=''; b='b'; c='c'; d='d'; f='f'; g='g'; h='h'; j='g'; k='c'; l='l'; m='m'; n='n'; p='p'; q='c'; r='r'; s='s'; t='t'; v='v'; w='gw'; x='h'; y='i'; z='s' }
    $syllableCount = 3 + ($hash[0] % 2)
    $builder = [Text.StringBuilder]::new()
    for ($syllable = 0; $syllable -lt $syllableCount; $syllable++) {
        $onset = if ($syllable -eq 0 -and $initials.ContainsKey($normalized[0].ToString())) {
            $initials[$normalized[0].ToString()]
        } else {
            $onsets[$hash[1 + ($syllable * 3)] % $onsets.Count]
        }
        $vowel = $vowels[$hash[2 + ($syllable * 3)] % $vowels.Count]
        $coda = $codas[$hash[3 + ($syllable * 3)] % $codas.Count]
        [void]$builder.Append($onset).Append($vowel).Append($coda)
    }
    $result = $builder.ToString()
    return $result.Substring(0, 1).ToLowerInvariant() + $result.Substring(1)
}

function Get-FamilyRule([string] $Base, [string] $Term) {
    if ($Term -eq $Base) { return 'invented-root' }
    if ($Term -eq $Base + "'s" -or ($Base.EndsWith('s') -and $Term -eq $Base + "'")) { return 'possessive' }
    if ($Term -eq $Base + 'ing' -or ($Base.EndsWith('e') -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'ing')) { return 'active-participle' }
    if ($Term -eq $Base + 'ed' -or ($Base.EndsWith('e') -and $Term -eq $Base + 'd') -or ($Base.EndsWith('y') -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'ied')) { return 'past-active-participle' }
    if ($Term -eq $Base + 'ly' -or ($Base.EndsWith('y') -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'ily')) { return 'adverb' }
    if ($Term -eq $Base + 'ness' -or ($Base.EndsWith('y') -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'iness')) { return 'abstract-noun' }
    if ($Term -eq $Base + 'est' -or ($Base.EndsWith('e') -and $Term -eq $Base + 'st') -or ($Base.EndsWith('y') -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'iest')) { return 'superlative' }
    if ($Term -eq $Base + 'er' -or ($Base.EndsWith('e') -and $Term -eq $Base + 'r')) { return 'agent-noun' }
    if ($Term -eq $Base + 'able' -or ($Base.EndsWith('e') -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'able')) { return 'able-adjective' }
    if ($Term -eq $Base + 's' -or $Term -eq $Base + 'es' -or ($Base.EndsWith('y') -and $Term -eq $Base.Substring(0, $Base.Length - 1) + 'ies')) { return 'plural-or-present' }
    return 'semantic-extension'
}

function Get-DerivedForm([string] $Root, [string] $Rule) {
    switch ($Rule) {
        'possessive' { return $Root }
        'plural-or-present' { return $Root + 'in' }
        'active-participle' { return $Root + 'ol' }
        'past-active-participle' { return $Root + 'iel' }
        'adverb' { return $Root + 'ra' }
        'abstract-noun' { return $Root + 'th' }
        'agent-noun' { return $Root + 'ron' }
        'superlative' { return 'ro' + $Root }
        'able-adjective' { return $Root + 'ui' }
        'semantic-extension' { return $Root }
        default { return $Root }
    }
}

$orcish = Get-Content -Raw $OrcishLexiconPath | ConvertFrom-Json
$snapshot = Get-Content -Raw $ElvenLexiconPath | ConvertFrom-Json
$finalized = Get-Content -Raw $ElvenDictionaryPath | ConvertFrom-Json
$first = Get-Content -Raw $FirstIterationPath | ConvertFrom-Json
$second = Get-Content -Raw $SecondIterationPath | ConvertFrom-Json
$knownEnglish = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$knownForms = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($property in $snapshot.terms.PSObject.Properties) {
    foreach ($candidate in $property.Value) {
        if ($candidate.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace([string]$candidate[0])) {
            Add-CollisionIndex ([string]$candidate[0]) "curated-candidate:$($property.Name)"
        }
    }
}
foreach ($property in $finalized.translations.PSObject.Properties) {
    [void]$knownEnglish.Add($property.Name)
    [void]$knownForms.Add([string]$property.Value[0])
    Add-CollisionIndex ([string]$property.Value[0]) "attested:$($property.Name)"
}
foreach ($artifact in @($first, $second)) {
    foreach ($entry in $artifact.entries) {
        [void]$knownEnglish.Add($entry.english)
        [void]$knownForms.Add($entry.elvish)
        Add-CollisionIndex $entry.elvish "existing:$($entry.english)"
    }
}

$remaining = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($property in $orcish.terms.PSObject.Properties) {
    if (-not $knownEnglish.Contains($property.Name)) { [void]$remaining.Add($property.Name) }
}

$parentByTerm = @{}
foreach ($property in $orcish.terms.PSObject.Properties) {
    if (-not $remaining.Contains($property.Name)) { continue }
    foreach ($candidate in $property.Value[1]) {
        $tags = @($candidate[3])
        if ($tags -notcontains 'near-kin' -or $tags -notcontains 'derived-by-rule') { continue }
        $familyTags = @($tags | Where-Object { $_ -like 'family-*' })
        if ($familyTags.Count -ne 1) { continue }
        $base = $familyTags[0].Substring(7)
        if ($remaining.Contains($base) -and -not [string]::Equals($base, $property.Name, [StringComparison]::OrdinalIgnoreCase)) {
            $parentByTerm[$property.Name] = $base
        }
        break
    }
}

function Get-RootKey([string] $Term) {
    $path = [Collections.Generic.List[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $current = $Term
    while ($parentByTerm.ContainsKey($current) -and $seen.Add($current) -and $path.Count -lt 32) {
        $path.Add($current)
        $current = $parentByTerm[$current]
    }
    if (-not $seen.Add($current)) { return @($path + $current | Sort-Object)[0] }
    return $current
}

$rootKeyByTerm = @{}
$rootKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($term in $remaining) {
    $rootKey = Get-RootKey $term
    $rootKeyByTerm[$term] = $rootKey
    [void]$rootKeys.Add($rootKey)
}

$rootForms = @{}
$rootRetries = 0
foreach ($rootKey in @($rootKeys | Sort-Object)) {
    for ($nonce = 0; ; $nonce++) {
        $candidate = New-SindarinRoot $rootKey $nonce
        $collision = Get-CollisionKind $candidate $rootKey $false
        if ($null -eq $collision -and -not (Test-RepeatedRun $candidate) -and -not (Test-ConsonantRun $candidate)) {
            $rootForms[$rootKey] = $candidate
            Add-CollisionIndex $candidate $rootKey
            $rootRetries += $nonce
            break
        }
    }
}

$entries = [Collections.Generic.List[object]]::new()
$ruleCounts = @{}
$partCounts = @{}
$sharedCount = 0
$derivedFallbacks = 0
$derivedRetries = 0
foreach ($english in @($remaining | Sort-Object)) {
    $rootKey = $rootKeyByTerm[$english]
    $root = $rootForms[$rootKey]
    $rule = Get-FamilyRule $rootKey $english
    $form = Get-DerivedForm $root $rule
    $allowShared = $rule -in @('invented-root','possessive','semantic-extension')
    $collision = Get-CollisionKind $form $rootKey $allowShared
    $flags = [Collections.Generic.List[string]]::new()
    if ($collision -eq 'intentional-shared-root') {
        if ($rule -ne 'invented-root') {
            $flags.Add('intentional-shared-root')
            $sharedCount++
        }
    } elseif ($null -ne $collision -or (Test-RepeatedRun $form) -or (Test-ConsonantRun $form)) {
        $originalRule = $rule
        $rule = 'independent-neologism'
        $derivedFallbacks++
        for ($nonce = 0; ; $nonce++) {
            $form = New-SindarinRoot $english (1000 + $nonce)
            if ($null -eq (Get-CollisionKind $form $english $false) -and -not (Test-RepeatedRun $form) -and -not (Test-ConsonantRun $form)) {
                $derivedRetries += $nonce
                $flags.Add("fallback-from-$originalRule")
                break
            }
        }
    }
    if (-not $exactForms.ContainsKey((Get-NormalizedLetters $form))) { Add-CollisionIndex $form $rootKey }
    if (-not $ruleCounts.ContainsKey($rule)) { $ruleCounts[$rule] = 0 }; $ruleCounts[$rule]++
    $partOfSpeech = switch ($rule) {
        'active-participle' { 'verb' }
        'past-active-participle' { 'verb' }
        'adverb' { 'adverb' }
        'abstract-noun' { 'noun' }
        'agent-noun' { 'noun' }
        'superlative' { 'adjective' }
        'able-adjective' { 'adjective' }
        'possessive' { 'adjective' }
        default { 'word' }
    }
    if (-not $partCounts.ContainsKey($partOfSpeech)) { $partCounts[$partOfSpeech] = 0 }; $partCounts[$partOfSpeech]++
    $entries.Add(@($english, $form, $rootKey, $partOfSpeech, $rule, @($flags)))
}

$orderedRules = [ordered]@{}; foreach ($key in $ruleCounts.Keys | Sort-Object) { $orderedRules[$key] = $ruleCounts[$key] }
$orderedParts = [ordered]@{}; foreach ($key in $partCounts.Keys | Sort-Object) { $orderedParts[$key] = $partCounts[$key] }
$artifact = [ordered]@{
    schemaVersion = 1
    policy = 'Complete Orcish/Elvish coverage. Reuse remaining reviewed English family links; otherwise create deterministic Sindarin-first neologisms. Quenya is not invented when a generated Sindarin form is possible.'
    sourceEnglishTermCount = $orcish.uniqueEnglishTerms
    priorEnglishTermCount = $knownEnglish.Count
    entryCount = $entries.Count
    expectedFinalEnglishTermCount = $knownEnglish.Count + $entries.Count
    candidateFields = @('english','elvish','rootKey','partOfSpeech','derivationRule','flags')
    validation = [ordered]@{
        language = 'Sindarin'
        generator = 'sindarin-neologism-v1'
        exactUnreviewedCollisions = 0
        closeFormConflicts = 0
        malformedForms = 0
        repeatedLetterRuns = 0
        fourConsonantRuns = 0
        intentionalSharedRootForms = $sharedCount
        rootCollisionRetries = $rootRetries
        derivedFallbacks = $derivedFallbacks
        derivedCollisionRetries = $derivedRetries
    }
    byRule = $orderedRules
    byPartOfSpeech = $orderedParts
    entries = @($entries)
}
$report = [ordered]@{
    schemaVersion = 1
    previousEnglishTerms = $knownEnglish.Count
    addedEnglishTerms = $entries.Count
    finalEnglishTerms = $knownEnglish.Count + $entries.Count
    remainingOrcishTermsWithoutElvish = 0
    generatedRootCount = $rootForms.Count
    familyLinkedTermCount = @($rootKeyByTerm.GetEnumerator() | Where-Object { $_.Key -ne $_.Value }).Count
    independentRootTermCount = @($rootKeyByTerm.GetEnumerator() | Where-Object { $_.Key -eq $_.Value }).Count
    byRule = $orderedRules
    byPartOfSpeech = $orderedParts
    validation = $artifact.validation
}
foreach ($item in @(@{Path=$OutputPath; Value=$artifact}, @{Path=$ReportPath; Value=$report})) {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $item.Path)) | Out-Null
    [IO.File]::WriteAllText($item.Path, ($item.Value | ConvertTo-Json -Depth 7 -Compress), [Text.UTF8Encoding]::new($false))
}

[pscustomobject]@{Added=$entries.Count; Final=$report.finalEnglishTerms; Remaining=$report.remainingOrcishTermsWithoutElvish; Roots=$report.generatedRootCount; OutputPath=[IO.Path]::GetFullPath($OutputPath)}
