param(
    [uri]$BaseUri = 'https://bryanmiller.us/scarlethorizons/pwa/',
    [string]$PwaRoot = $PSScriptRoot,
    [switch]$RequireCurrentXpApi,
    [switch]$ExcludeQuests
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

function Get-HeaderValue {
    param(
        [Parameter(Mandatory = $true)]$Response,
        [Parameter(Mandatory = $true)][string]$Name
    )
    $values = [System.Collections.Generic.IEnumerable[string]]$null
    if ($Response.Headers.TryGetValues($Name, [ref]$values)) {
        return $values -join ', '
    }
    $values = [System.Collections.Generic.IEnumerable[string]]$null
    if ($Response.Content.Headers.TryGetValues($Name, [ref]$values)) {
        return $values -join ', '
    }
    return ''
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)
    return [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($Bytes))
}

Assert-Condition -Condition ($BaseUri.Scheme -eq 'https') -Message 'The deployed PWA must use HTTPS.'
Assert-Condition -Condition ($BaseUri.AbsolutePath.EndsWith('/')) -Message 'BaseUri must end with a slash.'
Assert-Condition -Condition (Test-Path -LiteralPath $PwaRoot -PathType Container) -Message "PWA root not found: $PwaRoot"

$runtimeFiles = [ordered]@{
    'index.html' = @('text/html')
    'styles.css' = @('text/css')
    'app.js' = @('application/javascript', 'text/javascript')
    'modules/translator.js' = @('application/javascript', 'text/javascript')
    'modules/search.js' = @('application/javascript', 'text/javascript')
    'modules/dice.js' = @('application/javascript', 'text/javascript')
    'translator-worker.js' = @('application/javascript', 'text/javascript')
    'service-worker.js' = @('application/javascript', 'text/javascript')
    'offline.html' = @('text/html')
    'manifest.webmanifest' = @('application/manifest+json', 'application/json')
    'icons/icon-192.png' = @('image/png')
    'icons/icon-512.png' = @('image/png')
    'icons/dragon-mark.png' = @('image/png')
    'data/orcish.json' = @('application/json', 'text/json')
    'data/elvish.json' = @('application/json', 'text/json')
    'data/ghukliak.json' = @('application/json', 'text/json')
    'data/heroes.json' = @('application/json', 'text/json')
    'level-progression.json' = @('application/json', 'text/json')
    'magic-items.json' = @('application/json', 'text/json')
    'party-funds.json' = @('application/json', 'text/json')
    'quests.json' = @('application/json', 'text/json')
    'campaign-search.json' = @('application/json', 'text/json')
}
if ($ExcludeQuests) {
    $runtimeFiles.Remove('quests.json')
}

$heroData = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'data\heroes.json') | ConvertFrom-Json
$heroContentTypes = @{
    '.avif' = @('image/avif')
    '.gif' = @('image/gif')
    '.jpeg' = @('image/jpeg')
    '.jpg' = @('image/jpeg')
    '.png' = @('image/png')
    '.webp' = @('image/webp')
}
foreach ($hero in @($heroData.heroes) + @($heroData.dungeonMaster)) {
    $relativePath = [string]$hero.token
    $extension = [System.IO.Path]::GetExtension($relativePath).ToLowerInvariant()
    Assert-Condition -Condition ($heroContentTypes.ContainsKey($extension)) -Message "Unsupported hero-token extension: $relativePath"
    $runtimeFiles[$relativePath] = $heroContentTypes[$extension]
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AutomaticDecompression = [System.Net.DecompressionMethods]::All
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(90)

try {
    $responses = @{}
    foreach ($entry in $runtimeFiles.GetEnumerator()) {
        $relativePath = $entry.Key
        $localPath = Join-Path $PwaRoot ($relativePath -replace '/', '\')
        Assert-Condition -Condition (Test-Path -LiteralPath $localPath -PathType Leaf) -Message "Missing local deployment file: $relativePath"

        $requestUri = [uri]::new(
            $BaseUri,
            "$relativePath`?deployment-test=$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())")
        $response = $client.GetAsync($requestUri).GetAwaiter().GetResult()
        $responses[$relativePath] = $response
        Assert-Condition -Condition $response.IsSuccessStatusCode -Message "$relativePath returned HTTP $([int]$response.StatusCode)."

        $mediaType = [string]$response.Content.Headers.ContentType.MediaType
        Assert-Condition -Condition ($entry.Value -contains $mediaType) -Message "$relativePath returned unexpected content type '$mediaType'."

        $remoteBytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        $localBytes = [System.IO.File]::ReadAllBytes($localPath)
        Assert-Condition -Condition ((Get-Sha256 $remoteBytes) -eq (Get-Sha256 $localBytes)) -Message "$relativePath does not match the local deployment file."
    }

    $indexResponse = $responses['index.html']
    Assert-Condition -Condition ((Get-HeaderValue $indexResponse 'X-Content-Type-Options') -eq 'nosniff') -Message 'The PWA is missing X-Content-Type-Options: nosniff.'
    Assert-Condition -Condition ((Get-HeaderValue $indexResponse 'Strict-Transport-Security') -match 'max-age=') -Message 'The PWA is missing HSTS.'
    $contentSecurityPolicy = Get-HeaderValue $indexResponse 'Content-Security-Policy'
    Assert-Condition -Condition ($contentSecurityPolicy.Contains("default-src 'self'") -and $contentSecurityPolicy.Contains("frame-ancestors 'none'") -and $contentSecurityPolicy.Contains("connect-src 'self' https://publish-01.obsidian.md")) -Message 'The PWA Content-Security-Policy is incomplete.'

    $uncachedFiles = @('service-worker.js', 'manifest.webmanifest', 'level-progression.json', 'magic-items.json', 'party-funds.json', 'quests.json') |
        Where-Object { $responses.ContainsKey($_) }
    foreach ($uncachedFile in $uncachedFiles) {
        $cacheControl = Get-HeaderValue $responses[$uncachedFile] 'Cache-Control'
        Assert-Condition -Condition ($cacheControl -match 'no-cache') -Message "$uncachedFile must be served with Cache-Control: no-cache."
    }

    $manifest = [System.Text.Encoding]::UTF8.GetString(
        [System.IO.File]::ReadAllBytes((Join-Path $PwaRoot 'manifest.webmanifest'))) | ConvertFrom-Json
    $resolvedStart = [uri]::new($BaseUri, [string]$manifest.start_url)
    $resolvedScope = [uri]::new($BaseUri, [string]$manifest.scope)
    Assert-Condition -Condition ($resolvedStart.GetLeftPart([System.UriPartial]::Authority) -eq $BaseUri.GetLeftPart([System.UriPartial]::Authority)) -Message 'Manifest start_url escapes the deployment origin.'
    Assert-Condition -Condition ($resolvedStart.AbsolutePath.StartsWith($resolvedScope.AbsolutePath, [StringComparison]::Ordinal)) -Message 'Manifest start_url is outside its scope.'

    $campaignSearch = [System.Text.Encoding]::UTF8.GetString(
        [System.IO.File]::ReadAllBytes((Join-Path $PwaRoot 'campaign-search.json'))) | ConvertFrom-Json
    Assert-Condition -Condition (@($campaignSearch.pages | Where-Object { $_.title -eq 'XP Tracking' }).Count -eq 0) -Message 'The deployed public search index contains the protected XP Tracking page.'

    $apiBaseUri = [uri]"$($BaseUri.GetLeftPart([System.UriPartial]::Authority))/scarlethorizons/api/v1/"
    $sessionResponse = $client.GetAsync([uri]::new($apiBaseUri, 'session')).GetAwaiter().GetResult()
    $sessionPayload = $sessionResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    Assert-Condition -Condition $sessionResponse.IsSuccessStatusCode -Message 'The public session-status endpoint is unavailable.'
    Assert-Condition -Condition ($sessionPayload.authenticated -eq $false) -Message 'An anonymous deployment test unexpectedly received an authenticated session.'
    Assert-Condition -Condition ((Get-HeaderValue $sessionResponse 'Cache-Control') -match 'no-store') -Message 'Session-status responses must use Cache-Control: no-store.'

    $legacyXpResponse = $client.GetAsync([uri]::new($BaseUri, 'XP/index.json')).GetAwaiter().GetResult()
    Assert-Condition -Condition (([int]$legacyXpResponse.StatusCode -eq 403) -or ([int]$legacyXpResponse.StatusCode -eq 404)) -Message 'Legacy public XP paths must return HTTP 403 or 404.'

    if ($RequireCurrentXpApi) {
        $healthResponse = $client.GetAsync([uri]::new($apiBaseUri, 'health')).GetAwaiter().GetResult()
        $healthPayload = $healthResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        Assert-Condition -Condition $healthResponse.IsSuccessStatusCode -Message 'The broker health endpoint is unavailable.'
        Assert-Condition -Condition ([int]$healthPayload.schema_version -eq 6) -Message 'The broker health schema is not version 6.'
        Assert-Condition -Condition ($healthPayload.PSObject.Properties.Name -contains 'xp_tracking_configured') -Message 'The live broker does not expose XP tracking readiness.'
        Assert-Condition -Condition ($healthPayload.xp_tracking_configured -eq $true) -Message 'XP tracking is not configured on the live broker.'
        Assert-Condition -Condition ($healthPayload.PSObject.Properties.Name -contains 'word_count_snapshot_available') -Message 'The live broker does not expose word-count snapshot readiness.'
        Assert-Condition -Condition ($healthPayload.PSObject.Properties.Name -contains 'word_count_refresh') -Message 'The live broker does not expose word-count refresh readiness.'
        Assert-Condition -Condition ($healthPayload.word_count_refresh.configured -eq $true) -Message 'Word-count refresh is not configured on the live broker.'
        Assert-Condition -Condition ($healthPayload.word_count_refresh.signing_configured -eq $true) -Message 'Signed word-count refresh is not configured on the live broker.'
        Assert-Condition -Condition ($healthPayload.word_count_refresh.healthy -eq $true) -Message 'The live word-count refresh is unhealthy.'
        Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$healthPayload.word_count_refresh.last_scheduler_run_at)) -Message 'The live broker does not report a word-count scheduler run.'
        Assert-Condition -Condition ($healthPayload.quest_request_workflow_configured -eq $true) -Message 'The live broker does not expose quest-request workflow readiness.'

        $xpResponse = $client.GetAsync([uri]::new($apiBaseUri, 'xp')).GetAwaiter().GetResult()
        $xpPayload = $xpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        Assert-Condition -Condition ([int]$xpResponse.StatusCode -eq 401) -Message 'Anonymous XP access must return HTTP 401.'
        Assert-Condition -Condition ([string]$xpPayload.error -eq 'authentication_required') -Message 'Anonymous XP access failed with the wrong error.'
        Assert-Condition -Condition ((Get-HeaderValue $xpResponse 'Cache-Control') -match 'no-store') -Message 'XP responses must use Cache-Control: no-store.'

        $wordCountResponse = $client.GetAsync([uri]::new($apiBaseUri, 'word-counts')).GetAwaiter().GetResult()
        $wordCountPayload = $wordCountResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        Assert-Condition -Condition ([int]$wordCountResponse.StatusCode -eq 401) -Message 'Anonymous word-count access must return HTTP 401.'
        Assert-Condition -Condition ([string]$wordCountPayload.error -eq 'authentication_required') -Message 'Anonymous word-count access failed with the wrong error.'
        Assert-Condition -Condition ((Get-HeaderValue $wordCountResponse 'Cache-Control') -match 'no-store') -Message 'Word-count responses must use Cache-Control: no-store.'

        $presenceResponse = $client.GetAsync([uri]::new($apiBaseUri, 'presence')).GetAwaiter().GetResult()
        $presencePayload = $presenceResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        Assert-Condition -Condition ([int]$presenceResponse.StatusCode -eq 401) -Message 'Anonymous presence access must return HTTP 401.'
        Assert-Condition -Condition ([string]$presencePayload.error -eq 'authentication_required') -Message 'Anonymous presence access failed with the wrong error.'
        Assert-Condition -Condition ((Get-HeaderValue $presenceResponse 'Cache-Control') -match 'no-store') -Message 'Presence responses must use Cache-Control: no-store.'

        $questsResponse = $client.GetAsync([uri]::new($apiBaseUri, 'quests')).GetAwaiter().GetResult()
        $questsPayload = $questsResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        Assert-Condition -Condition ([int]$questsResponse.StatusCode -eq 401) -Message 'Anonymous quest access must return HTTP 401.'
        Assert-Condition -Condition ([string]$questsPayload.error -eq 'authentication_required') -Message 'Anonymous quest access failed with the wrong error.'
        Assert-Condition -Condition ((Get-HeaderValue $questsResponse 'Cache-Control') -match 'no-store') -Message 'Quest responses must use Cache-Control: no-store.'
    }

    Write-Output "PWA deployment verified: $($runtimeFiles.Count) public runtime files match, security/cache headers are valid, and anonymous session handling is safe."
    if (!$RequireCurrentXpApi) {
        Write-Output 'Current-XP API readiness was not required. Add -RequireCurrentXpApi after deploying the broker update.'
    }
}
finally {
    if ($null -ne $responses) {
        foreach ($response in $responses.Values) {
            $response.Dispose()
        }
    }
    $client.Dispose()
    $handler.Dispose()
}
