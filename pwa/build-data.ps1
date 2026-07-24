param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$RefreshCampaignSearch
)

$ErrorActionPreference = 'Stop'

function Read-Json {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required JSON source is missing: $Path"
    }
    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}

function Write-CompactJson {
    param(
        [Parameter(Mandatory = $true)][object]$Value,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $options = [System.Text.Json.JsonSerializerOptions]::new()
    $options.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
    $json = [System.Text.Json.JsonSerializer]::Serialize($Value, $options)
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
}

function Get-MaxPhraseWords {
    param([Parameter(Mandatory = $true)][System.Collections.Generic.Dictionary[string,string]]$Terms)
    $maximum = 1
    foreach ($term in $Terms.Keys) {
        $count = @($term -split '\s+' | Where-Object { $_.Length -gt 0 }).Count
        if ($count -gt $maximum) {
            $maximum = $count
        }
    }
    return $maximum
}

function Add-TermIfMissing {
    param(
        [Parameter(Mandatory = $true)][System.Collections.Generic.Dictionary[string,string]]$Dictionary,
        [Parameter(Mandatory = $true)][string]$English,
        [Parameter(Mandatory = $true)][string]$Translation
    )
    $englishTerm = $English.Trim()
    $translatedTerm = $Translation.Trim()
    if ($englishTerm.Length -gt 0 -and $translatedTerm.Length -gt 0 -and !$Dictionary.ContainsKey($englishTerm)) {
        $Dictionary.Add($englishTerm, $translatedTerm)
    }
}

function Write-InstallIcon {
    param(
        [Parameter(Mandatory = $true)][System.Drawing.Image]$Source,
        [Parameter(Mandatory = $true)][int]$Size,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#241813'))
            $margin = [int]($Size * 0.16)
            $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
            try {
                $matrix = [System.Drawing.Imaging.ColorMatrix]::new(@(
                    [single[]]@(0, 0, 0, 0, 0),
                    [single[]]@(0, 0, 0, 0, 0),
                    [single[]]@(0, 0, 0, 0, 0),
                    [single[]]@(0, 0, 0, 10, 0),
                    [single[]]@(0.835, 0.694, 0.451, 0, 1)
                ))
                $attributes.SetColorMatrix($matrix)
                $destination = [System.Drawing.Rectangle]::new($margin, $margin, $Size - (2 * $margin), $Size - (2 * $margin))
                $graphics.DrawImage($Source, $destination, 0, 0, $Source.Width, $Source.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
            }
            finally {
                $attributes.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
        }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

$dataDirectory = Join-Path $PSScriptRoot 'data'
$iconDirectory = Join-Path $PSScriptRoot 'icons'
New-Item -ItemType Directory -Force -Path $dataDirectory, $iconDirectory | Out-Null

$orcishSource = Read-Json -Path (Join-Path $RepositoryRoot 'web-translator\orcish-lexicon.json')
$orcishTerms = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($property in $orcishSource.terms.PSObject.Properties) {
    $record = $property.Value
    $candidates = @($record[1])
    if ($candidates.Count -gt 0 -and @($candidates[0]).Count -gt 0) {
        Add-TermIfMissing -Dictionary $orcishTerms -English $property.Name -Translation ([string]$candidates[0][0])
    }
}

$orcishPayload = [ordered]@{
    schemaVersion = 1
    language = 'Orcish'
    entryCount = $orcishTerms.get_Count()
    maxPhraseWords = Get-MaxPhraseWords -Terms $orcishTerms
    terms = $orcishTerms
}
Write-CompactJson -Value $orcishPayload -Path (Join-Path $dataDirectory 'orcish.json')

$elvishTerms = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$elvishBase = Read-Json -Path (Join-Path $RepositoryRoot 'web-translator\elven-translations.json')
foreach ($property in $elvishBase.translations.PSObject.Properties) {
    Add-TermIfMissing -Dictionary $elvishTerms -English $property.Name -Translation ([string]$property.Value[0])
}

$elvishFirst = Read-Json -Path (Join-Path $RepositoryRoot 'web-translator\elven-first-iteration.json')
foreach ($entry in $elvishFirst.entries) {
    Add-TermIfMissing -Dictionary $elvishTerms -English ([string]$entry.english) -Translation ([string]$entry.elvish)
}

$elvishSecond = Read-Json -Path (Join-Path $RepositoryRoot 'web-translator\elven-second-iteration.json')
foreach ($entry in $elvishSecond.entries) {
    Add-TermIfMissing -Dictionary $elvishTerms -English ([string]$entry.english) -Translation ([string]$entry.elvish)
}

$elvishComplete = Read-Json -Path (Join-Path $RepositoryRoot 'web-translator\elven-complete-coverage.json')
foreach ($entry in $elvishComplete.entries) {
    Add-TermIfMissing -Dictionary $elvishTerms -English ([string]$entry[0]) -Translation ([string]$entry[1])
}

$elvishPayload = [ordered]@{
    schemaVersion = 1
    language = 'Elvish'
    policy = 'Sindarin preferred; Quenya fallback; validator-reviewed generated forms and neologisms complete remaining coverage.'
    source = 'Eldamo 0.8.13, CC BY 4.0, plus project-generated reviewed forms.'
    entryCount = $elvishTerms.get_Count()
    maxPhraseWords = Get-MaxPhraseWords -Terms $elvishTerms
    terms = $elvishTerms
}
Write-CompactJson -Value $elvishPayload -Path (Join-Path $dataDirectory 'elvish.json')

$campaignSearchDestination = Join-Path $PSScriptRoot 'campaign-search.json'
if ($RefreshCampaignSearch) {
    & (Join-Path $PSScriptRoot 'refresh-campaign-search.ps1') -OutputPath $campaignSearchDestination
}
$campaignSearch = Read-Json -Path $campaignSearchDestination
if ([int]$campaignSearch.schemaVersion -ne 2 -or @($campaignSearch.pages).Count -eq 0) {
    throw 'The PWA campaign search index is missing full-text page data. Run pwa\refresh-campaign-search.ps1.'
}
if (@($campaignSearch.pages | Where-Object { $_.title -eq 'XP Tracking' }).Count -gt 0) {
    throw 'The protected XP Tracking page must not be included in the public PWA campaign search index.'
}

Add-Type -AssemblyName System.Drawing
$dragonPath = Join-Path $RepositoryRoot 'Assets\dragon-dim.png'
$dragon = [System.Drawing.Image]::FromFile($dragonPath)
try {
    Write-InstallIcon -Source $dragon -Size 192 -Path (Join-Path $iconDirectory 'icon-192.png')
    Write-InstallIcon -Source $dragon -Size 512 -Path (Join-Path $iconDirectory 'icon-512.png')
}
finally {
    $dragon.Dispose()
}
Copy-Item -LiteralPath $dragonPath -Destination (Join-Path $iconDirectory 'dragon-mark.png') -Force

Write-Output "PWA data generated: $($orcishTerms.get_Count()) Orcish terms, $($elvishTerms.get_Count()) Elvish terms."
