param(
    [string] $EldamoRoot = (Join-Path $PSScriptRoot '..\ToElvish'),
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\web-translator\elven-lexicon.json'),
    [string] $DictionaryOutputPath = (Join-Path $PSScriptRoot '..\web-translator\elven-translations.json'),
    [string] $AuditOutputPath = (Join-Path $PSScriptRoot '..\web-translator\elven-candidate-audit.json')
)

$ErrorActionPreference = 'Stop'

function Get-CuratedPageIds {
    param([Parameter(Mandatory)][string] $Path)

    $html = [System.IO.File]::ReadAllText($Path)
    $ids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($html, 'class="primary "><a href="\.\./words/word-(\d+)\.html"')) {
        [void] $ids.Add($match.Groups[1].Value)
    }

    return ,$ids
}

function Get-EnglishTerms {
    param([Parameter(Mandatory)][string] $Gloss)

    $decoded = [System.Net.WebUtility]::HtmlDecode($Gloss)
    $decoded = [regex]::Replace($decoded, '<[^>]+>', ' ')
    $terms = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($piece in [regex]::Split($decoded, '\s*(?:,|;|/|\bor\b)\s*')) {
        $term = $piece.Trim()
        $term = [regex]::Replace($term, '^\s*[*†#!?^‽]+\s*', '')
        $term = [regex]::Replace($term, '\[[^\]]*\]', ' ')
        $term = [regex]::Replace($term, '\([^)]*\)', ' ')
        $term = [regex]::Replace($term, '^to\s+', '', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        $term = [regex]::Replace($term, '\s+', ' ').Trim(' ', '"', "'", '.', ':')
        if ($term.Length -eq 0 -or $term.Length -gt 80) {
            continue
        }

        if (($term -split '\s+').Count -gt 8 -or $term -notmatch "^[\p{L}\p{M}][\p{L}\p{M} '\-]*$") {
            continue
        }

        [void] $terms.Add($term.ToLowerInvariant())
    }

    return @($terms | Sort-Object)
}

function Get-CanonicalElvishForm {
    param(
        [Parameter(Mandatory)][string] $Form,
        [AllowNull()][string] $Gloss
    )

    $canonical = $Form.Trim()
    $flags = [System.Collections.Generic.List[string]]::new()
    if ($canonical.Contains('/')) {
        $canonical = $canonical.Split('/', 2)[0].Trim()
        $flags.Add('first-listed-variant')
    }

    if ($canonical.Contains('(') -or $canonical.Contains(')')) {
        $canonical = [regex]::Replace($canonical, '\(([^()]*)\)', '$1')
        $flags.Add('expanded-parenthetical-letters')
    }

    if ($canonical.Contains('.') -and $Gloss.Contains('abbreviation of ')) {
        $expansion = $Gloss.Split([string[]]@('abbreviation of '), 2, [System.StringSplitOptions]::None)[1]
        $expansion = $expansion.Trim(' ', [char]0x2018, [char]0x2019, [char]0x201C, [char]0x201D, '"', "'", '.')
        if (-not [string]::IsNullOrWhiteSpace($expansion)) {
            $canonical = $expansion
            $flags.Add('expanded-attested-abbreviation')
        }
    }

    $canonical = [regex]::Replace($canonical, '\s+', ' ').Trim()
    $isValid = $canonical.Length -gt 0 -and
        $canonical -match "^[\p{L}\p{M} '\-’]+$" -and
        -not $canonical.StartsWith('-') -and
        -not $canonical.EndsWith('-') -and
        -not $canonical.Contains('--')
    if (-not $isValid) {
        $flags.Add('invalid-canonical-form')
    }

    return [pscustomobject]@{
        Form = $canonical
        IsValid = $isValid
        Flags = @($flags)
    }
}

function Get-ElvenCandidateScore {
    param(
        [Parameter(Mandatory)][string] $SourceLanguage,
        [AllowNull()][string] $Mark,
        [Parameter(Mandatory)][int] $NormalizationPenalty
    )

    $sourceScores = @{ s = 0; q = 0; n = 20; mq = 20; ns = 40; nq = 40 }
    $markScores = @{ '' = 0; '#' = 5; '*' = 10; '?' = 15; '†' = 18; '^' = 25; '!' = 50 }
    $sourceScore = if ($sourceScores.ContainsKey($SourceLanguage)) { $sourceScores[$SourceLanguage] } else { 60 }
    $markKey = if ([string]::IsNullOrWhiteSpace($Mark)) { '' } else { $Mark }
    $markScore = if ($markScores.ContainsKey($markKey)) { $markScores[$markKey] } else { 30 }
    return $sourceScore + $markScore + $NormalizationPenalty
}

function Get-ReliabilityFlags {
    param([AllowNull()][string] $Mark)

    $flags = switch ($Mark) {
        '!' { @('pure-neologism') }
        '^' { @('adapted-or-reformulated') }
        '†' { @('archaic-or-poetic') }
        '#' { @('compound-or-inflected-attestation') }
        '*' { @('reconstructed') }
        '?' { @('speculative') }
        default { @() }
    }
    return @($flags)
}

$xmlPath = Join-Path $EldamoRoot 'content\data-model\eldamo-data.xml'
$sindarinVocabularyPath = Join-Path $EldamoRoot 'content\vocabulary-indexes\vocabulary-words-ns.html'
$quenyaVocabularyPath = Join-Path $EldamoRoot 'content\vocabulary-indexes\vocabulary-words-nq.html'

$sindarinIds = Get-CuratedPageIds -Path $sindarinVocabularyPath
$quenyaIds = Get-CuratedPageIds -Path $quenyaVocabularyPath
$allIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$allIds.UnionWith($sindarinIds)
$allIds.UnionWith($quenyaIds)

$settings = [System.Xml.XmlReaderSettings]::new()
$settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
$settings.IgnoreComments = $true
$settings.IgnoreWhitespace = $true
$reader = [System.Xml.XmlReader]::Create($xmlPath, $settings)
$entries = [System.Collections.Generic.List[object]]::new()
$sourceVersion = ''
try {
    while ($reader.Read()) {
        if ($reader.NodeType -ne [System.Xml.XmlNodeType]::Element) {
            continue
        }

        if ($reader.Name -eq 'word-data') {
            $sourceVersion = $reader.GetAttribute('version')
            continue
        }

        if ($reader.Name -ne 'word') {
            continue
        }

        $pageId = $reader.GetAttribute('page-id')
        if ([string]::IsNullOrEmpty($pageId) -or -not $allIds.Contains($pageId)) {
            continue
        }

        $form = $reader.GetAttribute('v')
        $gloss = $reader.GetAttribute('gloss')
        $sourceLanguage = $reader.GetAttribute('l')
        if ([string]::IsNullOrWhiteSpace($form) -or [string]::IsNullOrWhiteSpace($gloss)) {
            continue
        }

        # Leading hyphens are bound suffixes. Trailing hyphens identify usable verb/root lemmas.
        if ($form.StartsWith('-', [System.StringComparison]::Ordinal)) {
            continue
        }

        $form = [regex]::Replace($form, '[¹²³⁴⁵⁶⁷⁸⁹⁰]+$', '').TrimEnd('-').Trim()
        $englishTerms = @(Get-EnglishTerms -Gloss $gloss)
        if ($form.Length -eq 0 -or $englishTerms.Count -eq 0) {
            continue
        }

        $language = if ($sindarinIds.Contains($pageId)) { 'Sindarin' } elseif ($quenyaIds.Contains($pageId)) { 'Quenya' } else { continue }
        $entries.Add([ordered]@{
            english = $englishTerms
            elvish = $form
            language = $language
            sourceLanguage = $sourceLanguage
            partOfSpeech = $reader.GetAttribute('speech')
            mark = $reader.GetAttribute('mark')
            gloss = $gloss
            pageId = $pageId
        })
    }
}
finally {
    $reader.Dispose()
}

$sourceRank = @{ s = 0; n = 1; ns = 2; q = 0; mq = 1; nq = 2 }
$orderedEntries = @($entries | Sort-Object `
    @{ Expression = { if ($_.language -eq 'Sindarin') { 0 } else { 1 } } }, `
    @{ Expression = { $sourceRank[$_.sourceLanguage] } }, `
    @{ Expression = { if ([string]::IsNullOrWhiteSpace($_.mark)) { 0 } elseif ($_.mark -eq '#') { 1 } elseif ($_.mark -eq '^') { 2 } elseif ($_.mark -eq '!') { 3 } else { 4 } } }, `
    @{ Expression = { $_.elvish } })

$sindarinByTerm = @{}
$quenyaByTerm = @{}
foreach ($entry in $orderedEntries) {
    $target = if ($entry.language -eq 'Sindarin') { $sindarinByTerm } else { $quenyaByTerm }
    foreach ($term in $entry.english) {
        if (-not $target.ContainsKey($term)) {
            $target[$term] = [System.Collections.Generic.List[object]]::new()
        }

        $target[$term].Add(@(
            $entry.elvish,
            $entry.language,
            $entry.sourceLanguage,
            $entry.partOfSpeech,
            $entry.mark,
            $entry.gloss,
            $entry.pageId
        ))
    }
}

$allTerms = @($sindarinByTerm.Keys + $quenyaByTerm.Keys | Sort-Object -Unique)
$terms = [ordered]@{}
$entryCount = 0
$sindarinPreferredTermCount = 0
$quenyaFallbackTermCount = 0
foreach ($term in $allTerms) {
    if ($sindarinByTerm.ContainsKey($term)) {
        $candidates = @($sindarinByTerm[$term])
        $sindarinPreferredTermCount++
    }
    else {
        $candidates = @($quenyaByTerm[$term])
        $quenyaFallbackTermCount++
    }

    $terms[$term] = $candidates
    $entryCount += $candidates.Count
}

$snapshot = [ordered]@{
    schemaVersion = 1
    source = 'Eldamo'
    sourceVersion = $sourceVersion
    sourceLicense = 'CC BY 4.0'
    sourceUrl = 'https://eldamo.org/'
    policy = 'Prefer curated Sindarin; use curated Quenya only when an English term has no Sindarin equivalent.'
    uniqueEnglishTerms = $terms.Keys.Count
    entryCount = $entryCount
    sindarinPreferredTerms = $sindarinPreferredTermCount
    quenyaFallbackTerms = $quenyaFallbackTermCount
    terms = $terms
}

$outputDirectory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$json = $snapshot | ConvertTo-Json -Depth 6 -Compress
[System.IO.File]::WriteAllText($OutputPath, $json, [System.Text.UTF8Encoding]::new($false))

$translations = [ordered]@{}
$candidateReviews = [System.Collections.Generic.List[object]]::new()
$selectedSindarin = 0
$selectedQuenya = 0
$selectedPureNeologisms = 0
$selectedAdaptedForms = 0
$selectedArchaicForms = 0
$normalizedSelectedForms = 0
foreach ($term in $terms.Keys) {
    $reviewsForTerm = [System.Collections.Generic.List[object]]::new()
    foreach ($candidate in $terms[$term]) {
        $canonical = Get-CanonicalElvishForm -Form $candidate[0] -Gloss $candidate[5]
        $normalizationPenalty = if ($canonical.Flags -contains 'first-listed-variant') { 4 } elseif ($canonical.Flags.Count -gt 0) { 2 } else { 0 }
        $score = Get-ElvenCandidateScore -SourceLanguage $candidate[2] -Mark $candidate[4] -NormalizationPenalty $normalizationPenalty
        $flags = [System.Collections.Generic.List[string]]::new()
        $flags.AddRange([string[]]@(Get-ReliabilityFlags -Mark $candidate[4]))
        $flags.AddRange([string[]]@($canonical.Flags))
        $review = [ordered]@{
            english = $term
            originalForm = $candidate[0]
            canonicalForm = $canonical.Form
            language = $candidate[1]
            sourceLanguage = $candidate[2]
            partOfSpeech = $candidate[3]
            mark = $candidate[4]
            gloss = $candidate[5]
            pageId = $candidate[6]
            score = $score
            eligible = $canonical.IsValid
            selected = $false
            flags = @($flags)
        }
        $reviewsForTerm.Add($review)
    }

    $selected = $reviewsForTerm |
        Where-Object eligible |
        Sort-Object score, canonicalForm, pageId |
        Select-Object -First 1
    if ($null -eq $selected) {
        throw "No linguistically usable Elven candidate remains for English term '$term'."
    }

    $selected.selected = $true
    $translations[$term] = @(
        $selected.canonicalForm,
        $selected.language,
        $selected.sourceLanguage,
        $selected.partOfSpeech,
        $selected.mark,
        $selected.gloss,
        $selected.pageId
    )
    if ($selected.language -eq 'Sindarin') { $selectedSindarin++ } else { $selectedQuenya++ }
    if ($selected.mark -eq '!') { $selectedPureNeologisms++ }
    if ($selected.mark -eq '^') { $selectedAdaptedForms++ }
    if ($selected.mark -eq '†') { $selectedArchaicForms++ }
    if ($selected.originalForm -ne $selected.canonicalForm) { $normalizedSelectedForms++ }
    foreach ($review in $reviewsForTerm) {
        $candidateReviews.Add($review)
    }
}

$dictionary = [ordered]@{
    schemaVersion = 1
    source = 'Eldamo'
    sourceVersion = $sourceVersion
    sourceLicense = 'CC BY 4.0'
    sourceUrl = 'https://eldamo.org/'
    policy = 'One deterministic translation per English term; prefer Sindarin, use Quenya only when Sindarin is unavailable, then rank attested period and reliability.'
    candidateCountReviewed = $candidateReviews.Count
    translationCount = $translations.Keys.Count
    selectedSindarin = $selectedSindarin
    selectedQuenya = $selectedQuenya
    selectedPureNeologisms = $selectedPureNeologisms
    selectedAdaptedForms = $selectedAdaptedForms
    selectedArchaicForms = $selectedArchaicForms
    normalizedSelectedForms = $normalizedSelectedForms
    translations = $translations
}

$audit = [ordered]@{
    schemaVersion = 1
    source = 'Eldamo'
    sourceVersion = $sourceVersion
    candidateCountReviewed = $candidateReviews.Count
    translationCount = $translations.Keys.Count
    scoring = [ordered]@{
        sourcePeriods = 'Late Sindarin/Quenya 0; Noldorin/Middle Quenya 20; Neo-Sindarin/Neo-Quenya 40; unknown 60.'
        reliability = 'Unmarked 0; compound/inflected # 5; reconstructed * 10; speculative ? 15; archaic † 18; adapted ^ 25; pure neologism ! 50; other 30.'
        normalization = 'Expanded parenthetical letters +2; first-listed slash variant +4; malformed canonical forms are ineligible.'
    }
    candidates = @($candidateReviews)
}

foreach ($artifact in @(
    @{ Path = $DictionaryOutputPath; Value = $dictionary },
    @{ Path = $AuditOutputPath; Value = $audit }
)) {
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $artifact.Path)) | Out-Null
    $artifactJson = $artifact.Value | ConvertTo-Json -Depth 8 -Compress
    [System.IO.File]::WriteAllText($artifact.Path, $artifactJson, [System.Text.UTF8Encoding]::new($false))
}

[pscustomobject]@{
    OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    SindarinCuratedIds = $sindarinIds.Count
    QuenyaCuratedIds = $quenyaIds.Count
    SnapshotEntries = $entryCount
    EnglishTerms = $terms.Keys.Count
    FinalTranslations = $translations.Keys.Count
    CandidateReviews = $candidateReviews.Count
}
