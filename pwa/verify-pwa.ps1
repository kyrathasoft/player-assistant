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
    'data\heroes.json',
    'campaign-search.json'
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

$campaignSearch = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'campaign-search.json') | ConvertFrom-Json
Assert-Condition -Condition ([int]$campaignSearch.schemaVersion -eq 2) -Message 'Campaign search data must use the full-text schema.'
Assert-Condition -Condition (@($campaignSearch.pages).Count -gt 0) -Message 'Campaign search data contains no pages.'
Assert-Condition -Condition ([int]$campaignSearch.pageCount -eq @($campaignSearch.pages).Count) -Message 'Campaign search pageCount does not match its pages.'
Assert-Condition -Condition (@($campaignSearch.pages | Where-Object { ![string]::IsNullOrWhiteSpace($_.content) }).Count -gt 0) -Message 'Campaign search data contains no Markdown content.'
Assert-Condition -Condition (@($campaignSearch.pages | Where-Object { [string]::IsNullOrWhiteSpace($_.title) -or $_.url -notmatch '^https://' }).Count -eq 0) -Message 'Campaign search data contains an invalid page title or URL.'
Assert-Condition -Condition (@($campaignSearch.pages | Where-Object { $_.title -eq 'XP Tracking' }).Count -eq 0) -Message 'The protected XP Tracking page must not be included in public PWA search data.'

$heroData = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'data\heroes.json') | ConvertFrom-Json
Assert-Condition -Condition ([int]$heroData.schemaVersion -eq 1) -Message 'Hero-token data must use schema version 1.'
Assert-Condition -Condition ($heroData.source -eq 'https://publish.obsidian.md/scarlethorizons/PCs/Player+Characters+Listing') -Message 'Hero-token data has an unexpected source.'
Assert-Condition -Condition (@($heroData.heroes).Count -gt 0) -Message 'Hero-token data contains no active heroes.'
Assert-Condition -Condition ($heroData.dungeonMaster.name -eq 'Dungeon Master') -Message 'Dungeon Master token data is missing.'
Assert-Condition -Condition ($heroData.dungeonMaster.preferLocal -eq $true) -Message 'The approved local Dungeon Master token must override the incorrect wiki image.'
foreach ($hero in @($heroData.heroes) + @($heroData.dungeonMaster)) {
    Assert-Condition -Condition (![string]::IsNullOrWhiteSpace($hero.name)) -Message 'A hero-token entry has no character name.'
    Assert-Condition -Condition (@($hero.aliases).Count -gt 0) -Message "Hero-token entry has no aliases: $($hero.name)"
    Assert-Condition -Condition ([string]$hero.token -match '^data/hero-tokens/[a-zA-Z0-9][a-zA-Z0-9._-]*\.(?:avif|gif|jpe?g|png|webp)$') -Message "Hero-token entry has an unsafe path: $($hero.name)"
    Assert-Condition -Condition ([string]$hero.wikiToken -match '^https://publish-\d+\.obsidian\.md/access/[a-zA-Z0-9]+/[^?#]+$') -Message "Hero-token entry has an unsafe wiki URL: $($hero.name)"
    Assert-Condition -Condition ([string]$hero.sha256 -match '^[a-f0-9]{64}$') -Message "Hero-token entry has an invalid SHA-256 hash: $($hero.name)"
    $tokenPath = Join-Path $PwaRoot ([string]$hero.token).Replace('/', '\')
    Assert-Condition -Condition (Test-Path -LiteralPath $tokenPath -PathType Leaf) -Message "Hero token is missing: $($hero.token)"
    Assert-Condition -Condition ((Get-Item -LiteralPath $tokenPath).Length -ge 16) -Message "Hero token is empty: $($hero.token)"
    $actualHash = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.IO.File]::ReadAllBytes($tokenPath))).ToLowerInvariant()
    Assert-Condition -Condition ($actualHash -eq [string]$hero.sha256) -Message "Website fallback does not match the current wiki token: $($hero.name)"
}

foreach ($script in @('app.js', 'translator-worker.js', 'service-worker.js')) {
    & node --check (Join-Path $PwaRoot $script)
    Assert-Condition -Condition ($LASTEXITCODE -eq 0) -Message "JavaScript syntax check failed: $script"
}

$html = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'index.html')
$appScript = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'app.js')
$styles = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'styles.css')
$serviceWorker = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'service-worker.js')
$referencedIds = [regex]::Matches($appScript, "byId\('([^']+)'\)") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
foreach ($id in $referencedIds) {
    Assert-Condition -Condition ($html.Contains("id=`"$id`"")) -Message "app.js references a missing HTML element: $id"
}
Assert-Condition -Condition ($appScript.Contains("credentials: 'same-origin'")) -Message 'Character authentication must use same-origin cookies.'
Assert-Condition -Condition ($appScript.Contains("cache: 'no-store'")) -Message 'Character authentication requests must bypass browser caching.'
Assert-Condition -Condition ($appScript.Contains("'/login'") -and $appScript.Contains("'/session'") -and $appScript.Contains("'/xp'") -and $appScript.Contains("'/word-counts'") -and $appScript.Contains("'/logout'")) -Message 'Character authentication, XP, and word-count routes are incomplete.'
Assert-Condition -Condition ($html.Contains('autocomplete="current-password"')) -Message 'The character password field is not configured safely.'
Assert-Condition -Condition ($html.Contains('id="xp-card"') -and $html.Contains('id="xp-total"') -and $html.Contains('id="xp-class-level"') -and $html.Contains('id="xp-hit-points"') -and $html.Contains('id="xp-tnl"') -and $html.Contains('id="xp-party-rows"')) -Message 'The protected XP dashboard card is incomplete.'
Assert-Condition -Condition ($html.Contains('id="word-count-card"') -and $html.Contains('id="word-count-wiki"') -and $html.Contains('id="word-count-ic"') -and $html.Contains('id="word-count-ooc"') -and $html.Contains('id="word-count-date"')) -Message 'The protected word-count dashboard card is incomplete.'
Assert-Condition -Condition ($html.Contains('id="auth-dashboard-token"') -and $html.Contains('id="auth-account-token"')) -Message 'Authenticated hero-token image elements are missing.'
Assert-Condition -Condition ($appScript.Contains("fetch('data/heroes.json?v=2'") -and $appScript.Contains('findAuthenticatedHero') -and $appScript.Contains("account.role === 'dm'")) -Message 'Authenticated hero-token selection is incomplete or lacks cache-safe manifest versioning.'
Assert-Condition -Condition ($appScript.Contains('image.src = hero.token') -and $appScript.Contains('wikiImage.src = hero.wikiToken')) -Message 'Hero tokens must display the website copy immediately and prefer the wiki once it loads.'
Assert-Condition -Condition ($appScript.Contains('if (hero.preferLocal === true) return;')) -Message 'The Dungeon Master token must be able to retain its approved local image.'
Assert-Condition -Condition ($appScript.Contains("navigator.serviceWorker.addEventListener('controllerchange'") -and $appScript.Contains('window.location.reload()')) -Message 'Existing PWA clients must reload after a service-worker update.'
Assert-Condition -Condition ($styles.Contains('width: 128px;') -and $styles.Contains('.authenticated-hero-token')) -Message 'Authenticated hero tokens must be displayed at 128 pixels wide.'
Assert-Condition -Condition ($styles.Contains('transform: translateY(10px);')) -Message 'Authenticated hero tokens must retain their horizontal position and sit 10 pixels lower.'
$heroTokenStyle = [regex]::Match($styles, '(?s)\.authenticated-hero-token\s*\{(?<body>.*?)\}')
Assert-Condition -Condition ($heroTokenStyle.Success -and $heroTokenStyle.Groups['body'].Value -notmatch '(?m)^\s*border(?:-(?:top|right|bottom|left|width|style|color))?\s*:') -Message 'Authenticated hero tokens must not have a CSS border.'
Assert-Condition -Condition (!$appScript.Contains('publish.obsidian.md') -and !$html.Contains('XP+Tracking')) -Message 'The XP source URL must remain outside the browser application.'
Assert-Condition -Condition ($serviceWorker.Contains("url.pathname.startsWith('/scarlethorizons/api/')")) -Message 'The service worker must exclude protected API responses.'
Assert-Condition -Condition ($serviceWorker.Contains("new Request(asset, { cache: 'reload' })")) -Message 'Service-worker upgrades must bypass stale browser shell caches.'
Assert-Condition -Condition ($serviceWorker.Contains('networkFirstData') -and $serviceWorker.Contains("url.pathname.endsWith('/data/heroes.json')") -and $serviceWorker.Contains("url.pathname.includes('/data/hero-tokens/')")) -Message 'Hero-token manifests and images must refresh from the network before using cached copies.'
Assert-Condition -Condition ($appScript.Contains("updateViaCache: 'none'") -and $appScript.Contains('await registration.update()')) -Message 'The PWA must explicitly check for uncached service-worker updates.'
Assert-Condition -Condition ($manifest.start_url -eq './#dashboard' -and $manifest.scope -eq './') -Message 'The manifest must keep navigation inside the deployed PWA directory.'
Assert-Condition -Condition ($html.Contains('href="manifest.webmanifest"') -and $appScript.Contains('service-worker.js')) -Message 'The install manifest or service-worker registration is missing.'
Assert-Condition -Condition ($html.Contains('href="styles.css?v=20"') -and $html.Contains('src="app.js?v=20"') -and $serviceWorker.Contains("'./styles.css?v=20'") -and $serviceWorker.Contains("'./app.js?v=20'")) -Message 'The PWA shell must use cache-busting stylesheet and application-script URLs.'
$apacheConfig = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot '.htaccess')
Assert-Condition -Condition ($apacheConfig.Contains('AddType image/webp .webp')) -Message 'Apache must serve WebP hero tokens with the correct MIME type.'
Assert-Condition -Condition ($apacheConfig.Contains('img-src ''self'' data: https://*.obsidian.md')) -Message 'The content security policy must allow preferred wiki hero images.'
Assert-Condition -Condition ($apacheConfig.Contains('data/heroes\.json|data/hero-tokens/[^/]+')) -Message 'Apache must require revalidation for hero-token metadata and images.'

Write-Output "PWA verified: $($lexiconCounts.orcish) Orcish terms, $($lexiconCounts.elvish) Elvish terms, $(@($heroData.heroes).Count) player tokens and the Dungeon Master token, $($campaignSearch.pageCount) full-text campaign pages, install manifest and offline shell valid."
