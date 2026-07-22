param(
    [string] $EldamoRoot = (Join-Path $PSScriptRoot '..\ToElvish'),
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\web-translator\elven-lexicon.json')
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

[pscustomobject]@{
    OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    SindarinCuratedIds = $sindarinIds.Count
    QuenyaCuratedIds = $quenyaIds.Count
    SnapshotEntries = $entryCount
    EnglishTerms = $terms.Keys.Count
}
