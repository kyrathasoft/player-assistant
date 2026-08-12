param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$RefreshCampaignSearch,
    [switch]$RefreshHeroTokens
)

$ErrorActionPreference = 'Stop'

function Read-Json {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required JSON source is missing: $Path"
    }
    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}

$lexiconManifest = Read-Json -Path (Join-Path $RepositoryRoot 'lexicons\manifest.json')

function Get-CanonicalLexiconSourcePath {
    param(
        [Parameter(Mandatory = $true)][string]$Language,
        [Parameter(Mandatory = $true)][string]$Role,
        [int]$Index = 0
    )

    $contract = $lexiconManifest.languages.PSObject.Properties[$Language].Value
    $sources = @($contract.canonicalSources | Where-Object { $_.role -eq $Role })
    if ($Index -lt 0 -or $Index -ge $sources.Count) {
        throw "Canonical lexicon source is missing: $Language/$Role[$Index]"
    }
    return Join-Path $RepositoryRoot ([string]$sources[$Index].path).Replace('/', '\')
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
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

function Get-TermsContentHash {
    param([Parameter(Mandatory = $true)][System.Collections.Generic.Dictionary[string,string]]$Terms)

    $canonicalTerms = [ordered]@{}
    foreach ($english in ($Terms.get_Keys() | Sort-Object)) {
        $canonicalTerms[$english] = $Terms[$english]
    }
    $options = [System.Text.Json.JsonSerializerOptions]::new()
    $options.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
    $json = [System.Text.Json.JsonSerializer]::Serialize($canonicalTerms, $options)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $hash = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($hash.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hash.Dispose()
    }
}

function Get-MaxPhraseWords {
    param([Parameter(Mandatory = $true)][System.Collections.Generic.Dictionary[string,string]]$Terms)
    $maximum = 1
    foreach ($term in $Terms.get_Keys()) {
        $words = ($term -split '\s+') | Where-Object { $_.Length -gt 0 }
        $count = @($words).Count
        if ($count -gt $maximum) {
            $maximum = $count
        }
    }
    return $maximum
}

function Get-MaxTranslationPhraseWords {
    param([Parameter(Mandatory = $true)][System.Collections.Generic.Dictionary[string,string]]$Terms)

    $maximum = 1
    foreach ($english in $Terms.get_Keys()) {
        $translation = ([string]$Terms[$english]).Trim() -replace '\s+', ' '
        if ($translation.Length -eq 0) {
            continue
        }
        $maximum = [Math]::Max($maximum, @($translation -split '\s+' | Where-Object { $_.Length -gt 0 }).Count)
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

$orcishSource = Read-Json -Path (Get-CanonicalLexiconSourcePath -Language 'orcish' -Role 'assembled-candidate-lexicon')
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
    reverseMaxPhraseWords = Get-MaxTranslationPhraseWords -Terms $orcishTerms
    contentHash = Get-TermsContentHash -Terms $orcishTerms
    terms = $orcishTerms
}
Write-CompactJson -Value $orcishPayload -Path (Join-Path $dataDirectory 'orcish.json')

$elvishTerms = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$elvishBase = Read-Json -Path (Get-CanonicalLexiconSourcePath -Language 'elvish' -Role 'finalized-reviewed-selection')
foreach ($property in $elvishBase.translations.PSObject.Properties) {
    Add-TermIfMissing -Dictionary $elvishTerms -English $property.Name -Translation ([string]$property.Value[0])
}

$elvishFirst = Read-Json -Path (Get-CanonicalLexiconSourcePath -Language 'elvish' -Role 'reviewed-morphology-layer' -Index 0)
foreach ($entry in $elvishFirst.entries) {
    Add-TermIfMissing -Dictionary $elvishTerms -English ([string]$entry.english) -Translation ([string]$entry.elvish)
}

$elvishSecond = Read-Json -Path (Get-CanonicalLexiconSourcePath -Language 'elvish' -Role 'reviewed-morphology-layer' -Index 1)
foreach ($entry in $elvishSecond.entries) {
    Add-TermIfMissing -Dictionary $elvishTerms -English ([string]$entry.english) -Translation ([string]$entry.elvish)
}

$elvishComplete = Read-Json -Path (Get-CanonicalLexiconSourcePath -Language 'elvish' -Role 'audited-complete-coverage-layer')
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
    reverseMaxPhraseWords = Get-MaxTranslationPhraseWords -Terms $elvishTerms
    contentHash = Get-TermsContentHash -Terms $elvishTerms
    terms = $elvishTerms
}
Write-CompactJson -Value $elvishPayload -Path (Join-Path $dataDirectory 'elvish.json')

$ghukliakSource = Read-Json -Path (Get-CanonicalLexiconSourcePath -Language 'ghukliak' -Role 'campaign-candidate-lexicon')
$ghukliakCoverage = Read-Json -Path (Get-CanonicalLexiconSourcePath -Language 'ghukliak' -Role 'audited-complete-coverage-layer')
$ghukliakTerms = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($property in $ghukliakSource.terms.PSObject.Properties) {
    $candidates = @($property.Value)
    if ($candidates.Count -gt 0 -and @($candidates[0]).Count -gt 0) {
        Add-TermIfMissing -Dictionary $ghukliakTerms -English $property.Name -Translation ([string]$candidates[0][0])
    }
}

foreach ($entry in @($ghukliakCoverage.entries)) {
    if (@($entry).Count -lt 2) {
        throw 'A complete-coverage Ghukliak entry is malformed.'
    }
    Add-TermIfMissing -Dictionary $ghukliakTerms -English ([string]$entry[0]) -Translation ([string]$entry[1])
}

$ghukliakPayload = [ordered]@{
    schemaVersion = 1
    language = 'Ghukliak'
    source = 'IssendaCampaign Meta/Ghukliak (Goblin Tongue).md + deterministic complete coverage'
    entryCount = $ghukliakTerms.get_Count()
    maxPhraseWords = Get-MaxPhraseWords -Terms $ghukliakTerms
    reverseMaxPhraseWords = Get-MaxTranslationPhraseWords -Terms $ghukliakTerms
    contentHash = Get-TermsContentHash -Terms $ghukliakTerms
    terms = $ghukliakTerms
}
Write-CompactJson -Value $ghukliakPayload -Path (Join-Path $dataDirectory 'ghukliak.json')

$campaignSearchDestination = Join-Path $PSScriptRoot 'campaign-search.json'
if ($RefreshCampaignSearch) {
    & (Join-Path $PSScriptRoot 'refresh-campaign-search.ps1') -OutputPath $campaignSearchDestination
}
$campaignSearch = Read-Json -Path $campaignSearchDestination
if ([int]$campaignSearch.schemaVersion -ne 2 -or
    [int]$campaignSearch.termIndexVersion -ne 1 -or
    $null -eq $campaignSearch.termIndex -or
    @($campaignSearch.pages).Count -eq 0) {
    throw 'The PWA campaign search index is missing full-text page data or its exact-term index. Run pwa
efresh-campaign-search.ps1.'
}
if (@($campaignSearch.pages | Where-Object { $_.title -eq 'XP Tracking' }).Count -gt 0) {
    throw 'The protected XP Tracking page must not be included in the public PWA campaign search index.'
}

$packDefinitions = @(
    [ordered]@{ id = 'translator-orcish'; kind = 'translator'; language = 'orcish'; relativePath = 'data\orcish.json'; payload = $orcishPayload },
    [ordered]@{ id = 'translator-elvish'; kind = 'translator'; language = 'elvish'; relativePath = 'data\elvish.json'; payload = $elvishPayload },
    [ordered]@{ id = 'translator-ghukliak'; kind = 'translator'; language = 'ghukliak'; relativePath = 'data\ghukliak.json'; payload = $ghukliakPayload },
    [ordered]@{ id = 'campaign-search'; kind = 'campaign-search'; language = $null; relativePath = 'campaign-search.json'; payload = $campaignSearch }
)
$optionalPacks = @(
    foreach ($definition in $packDefinitions) {
        $packPath = Join-Path $PSScriptRoot $definition.relativePath
        $packPayload = $definition.payload
        $validation = if ($definition.kind -eq 'translator') {
            [ordered]@{
                entryCount = [int]$packPayload.entryCount
                maxPhraseWords = [int]$packPayload.maxPhraseWords
                reverseMaxPhraseWords = [int]$packPayload.reverseMaxPhraseWords
            }
        } else {
            [ordered]@{
                pageCount = [int]$packPayload.pageCount
                termIndexVersion = [int]$packPayload.termIndexVersion
            }
        }
        [ordered]@{
            id = $definition.id
            kind = $definition.kind
            language = $definition.language
            url = $definition.relativePath.Replace('\', '/')
            schemaVersion = [int]$packPayload.schemaVersion
            contentHash = Get-FileSha256 -Path $packPath
            byteSize = [int64](Get-Item -LiteralPath $packPath).Length
            recordCount = if ($definition.kind -eq 'translator') { [int]$packPayload.entryCount } else { [int]$packPayload.pageCount }
            validation = $validation
        }
    }
)
$optionalManifest = [ordered]@{
    schemaVersion = 1
    manifestVersion = 1
    packs = $optionalPacks
}
Write-CompactJson -Value $optionalManifest -Path (Join-Path $PSScriptRoot 'optional-packs.json')

if ($RefreshHeroTokens) {
    & (Join-Path $PSScriptRoot 'refresh-hero-tokens.ps1')
}
$heroData = Read-Json -Path (Join-Path $dataDirectory 'heroes.json')
if ([int]$heroData.schemaVersion -ne 1 -or
    @($heroData.heroes).Count -eq 0 -or
    [string]::IsNullOrWhiteSpace([string]$heroData.dungeonMaster.token)) {
    throw 'The PWA hero-token data is missing. Run pwa\refresh-hero-tokens.ps1.'
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

Write-Output "PWA data generated: $($orcishTerms.get_Count()) Orcish terms, $($elvishTerms.get_Count()) Elvish terms, $($ghukliakTerms.get_Count()) Ghukliak terms, $(@($heroData.heroes).Count) player tokens and the Dungeon Master token."
