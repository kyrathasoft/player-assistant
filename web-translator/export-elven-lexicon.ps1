param()

$ErrorActionPreference = 'Stop'

$webRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$terms = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$canonicalTerms = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$maxEnglishPhraseWords = 1

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required Elven source file not found: $Path"
    }

    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}

function Add-ElvenTerm {
    param(
        [Parameter(Mandatory = $true)][string]$English,
        [Parameter(Mandatory = $true)][string]$Elvish
    )

    $englishTerm = $English.Trim()
    $elvishTerm = $Elvish.Trim()
    if ($englishTerm.Length -eq 0 -or $elvishTerm.Length -eq 0 -or $terms.ContainsKey($englishTerm)) {
        return
    }

    $terms.Add($englishTerm, $elvishTerm)
    $canonicalTerms.Add($englishTerm, $englishTerm)
    $phraseWords = @($englishTerm -split '\s+' | Where-Object { $_.Length -gt 0 }).Count
    $script:maxEnglishPhraseWords = [Math]::Max($script:maxEnglishPhraseWords, $phraseWords)
}

$base = Read-JsonFile (Join-Path $webRoot 'elven-translations.json')
foreach ($property in $base.translations.PSObject.Properties) {
    Add-ElvenTerm -English $property.Name -Elvish ([string]$property.Value[0])
}

$firstIteration = Read-JsonFile (Join-Path $webRoot 'elven-first-iteration.json')
foreach ($entry in $firstIteration.entries) {
    Add-ElvenTerm -English ([string]$entry.english) -Elvish ([string]$entry.elvish)
}

$secondIteration = Read-JsonFile (Join-Path $webRoot 'elven-second-iteration.json')
foreach ($entry in $secondIteration.entries) {
    Add-ElvenTerm -English ([string]$entry.english) -Elvish ([string]$entry.elvish)
}

$completeCoverage = Read-JsonFile (Join-Path $webRoot 'elven-complete-coverage.json')
foreach ($entry in $completeCoverage.entries) {
    Add-ElvenTerm -English ([string]$entry[0]) -Elvish ([string]$entry[1])
}

$sortedKeys = [string[]]::new($terms.get_Count())
$terms.get_Keys().CopyTo($sortedKeys, 0)
[Array]::Sort($sortedKeys, [StringComparer]::OrdinalIgnoreCase)
$sortedTerms = [ordered]@{}
foreach ($key in $sortedKeys) {
    $sortedTerms[$canonicalTerms[$key]] = $terms[$key]
}

$document = [ordered]@{
    schemaVersion = 1
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    language = 'Elvish'
    policy = 'Sindarin preferred; Quenya fallback; validator-reviewed generated forms and neologisms complete remaining coverage.'
    source = 'Eldamo 0.8.13, CC BY 4.0, plus project-generated reviewed forms.'
    entryCount = $terms.get_Count()
    maxEnglishPhraseWords = $maxEnglishPhraseWords
    terms = $sortedTerms
}

$options = [System.Text.Json.JsonSerializerOptions]::new()
$options.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
$json = [System.Text.Json.JsonSerializer]::Serialize($document, $options)
$outputPath = Join-Path $webRoot 'elvish-lexicon.json'
[IO.File]::WriteAllText($outputPath, $json, [Text.UTF8Encoding]::new($false))

Write-Output "Exported $($terms.get_Count()) English-to-Elvish terms to $outputPath"
