param(
    [string]$PwaRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (!$Condition) {
        throw $Message
    }
}

$requiredFiles = @(
    'index.html',
    'styles.css',
    'app.js',
    'translator-worker.js',
    'service-worker.js',
    'manifest.webmanifest',
    'offline.html',
    'icons\icon-192.png',
    'icons\icon-512.png',
    'data\orcish.json',
    'data\elvish.json',
    'data\campaign-search.json'
)
foreach ($relativePath in $requiredFiles) {
    Assert-Condition -Condition (Test-Path -LiteralPath (Join-Path $PwaRoot $relativePath) -PathType Leaf) -Message "Missing PWA file: $relativePath"
}

$manifest = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'manifest.webmanifest') | ConvertFrom-Json
Assert-Condition -Condition (![string]::IsNullOrWhiteSpace($manifest.name)) -Message 'Manifest name is required.'
Assert-Condition -Condition (![string]::IsNullOrWhiteSpace($manifest.short_name)) -Message 'Manifest short_name is required.'
Assert-Condition -Condition (![string]::IsNullOrWhiteSpace($manifest.start_url)) -Message 'Manifest start_url is required.'
Assert-Condition -Condition (@('standalone', 'fullscreen', 'minimal-ui') -contains $manifest.display) -Message 'Manifest display must be install-capable.'

Add-Type -AssemblyName System.Drawing
foreach ($size in @(192, 512)) {
    $iconPath = Join-Path $PwaRoot "icons\icon-$size.png"
    $icon = [System.Drawing.Image]::FromFile($iconPath)
    try {
        Assert-Condition -Condition ($icon.Width -eq $size -and $icon.Height -eq $size) -Message "Install icon must be ${size}x${size}: $iconPath"
    }
    finally {
        $icon.Dispose()
    }
    Assert-Condition -Condition (@($manifest.icons | Where-Object { $_.sizes -eq "${size}x${size}" }).Count -gt 0) -Message "Manifest is missing the ${size}x${size} icon."
}

$lexiconCounts = [ordered]@{}
foreach ($language in @('orcish', 'elvish')) {
    $payload = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot "data\$language.json") | ConvertFrom-Json
    $actualCount = @($payload.terms.PSObject.Properties).Count
    Assert-Condition -Condition ($actualCount -gt 0) -Message "$language lexicon is empty."
    Assert-Condition -Condition ([int]$payload.entryCount -eq $actualCount) -Message "$language lexicon entryCount does not match its terms."
    $lexiconCounts[$language] = $actualCount
}

[void](Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'data\campaign-search.json') | ConvertFrom-Json)

foreach ($script in @('app.js', 'translator-worker.js', 'service-worker.js')) {
    & node --check (Join-Path $PwaRoot $script)
    Assert-Condition -Condition ($LASTEXITCODE -eq 0) -Message "JavaScript syntax check failed: $script"
}

$html = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'index.html')
$appScript = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'app.js')
$referencedIds = [regex]::Matches($appScript, "byId\('([^']+)'\)") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
foreach ($id in $referencedIds) {
    Assert-Condition -Condition ($html.Contains("id=`"$id`"")) -Message "app.js references a missing HTML element: $id"
}

Write-Output "PWA verified: $($lexiconCounts.orcish) Orcish terms, $($lexiconCounts.elvish) Elvish terms, install manifest and offline shell valid."
