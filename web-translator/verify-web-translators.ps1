param(
    [string]$WebRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$requiredFiles = @(
    'index.php',
    'orcish.php',
    'elven.php',
    'api.php',
    'elven-api.php',
    'OrcishTranslator.php',
    'ElvenTranslator.php',
    'translator.js',
    'styles.css',
    'orcish-lexicon.json',
    'elvish-lexicon.json'
)

foreach ($relativePath in $requiredFiles) {
    Assert-Condition -Condition (Test-Path -LiteralPath (Join-Path $WebRoot $relativePath) -PathType Leaf) -Message "Missing web translator file: $relativePath"
}

$landingPage = Get-Content -Raw -LiteralPath (Join-Path $WebRoot 'index.php')
Assert-Condition -Condition ($landingPage.Contains('href="orcish.php"')) -Message 'The landing page does not link to the Orcish translator.'
Assert-Condition -Condition ($landingPage.Contains('href="elven.php"')) -Message 'The landing page does not link to the Elven translator.'

$orcish = Get-Content -Raw -LiteralPath (Join-Path $WebRoot 'orcish-lexicon.json') | ConvertFrom-Json
$orcishCount = @($orcish.terms.PSObject.Properties).Count
Assert-Condition -Condition ($orcishCount -gt 0) -Message 'The Orcish web lexicon is empty.'
Assert-Condition -Condition ([int]$orcish.uniqueEnglishTerms -eq $orcishCount) -Message 'The Orcish web lexicon count is inconsistent.'

$elvish = Get-Content -Raw -LiteralPath (Join-Path $WebRoot 'elvish-lexicon.json') | ConvertFrom-Json
$elvishCount = @($elvish.terms.PSObject.Properties).Count
Assert-Condition -Condition ($elvishCount -gt 0) -Message 'The Elvish web lexicon is empty.'
Assert-Condition -Condition ([int]$elvish.entryCount -eq $elvishCount) -Message 'The Elvish web lexicon count is inconsistent.'
Assert-Condition -Condition ([int]$elvish.maxEnglishPhraseWords -gt 0) -Message 'The Elvish web lexicon phrase metadata is missing.'

$node = Get-Command node -ErrorAction SilentlyContinue
if ($node) {
    & $node.Source --check (Join-Path $WebRoot 'translator.js')
    Assert-Condition -Condition ($LASTEXITCODE -eq 0) -Message 'translator.js contains a syntax error.'
}

$php = Get-Command php -ErrorAction SilentlyContinue
$phpStatus = 'PHP lint skipped because PHP is not installed locally.'
if ($php) {
    foreach ($file in Get-ChildItem -LiteralPath $WebRoot -Filter '*.php' -File) {
        & $php.Source -l $file.FullName
        Assert-Condition -Condition ($LASTEXITCODE -eq 0) -Message "PHP lint failed: $($file.Name)"
    }
    $phpStatus = 'PHP lint passed.'
}

Write-Output "Web translators verified: $orcishCount Orcish terms, $elvishCount Elvish terms. $phpStatus"
