param(
    [uri]$BaseUri = 'https://bryanmiller.us/scarlethorizons/pwa/',
    [string]$PwaRoot = $PSScriptRoot,
    [switch]$RequireCurrentXpApi,
    [switch]$RequireProtectedApi,
    [string]$MonitorCharacterName = '',
    [string]$MonitorPassword = '',
    [ValidateRange(1, 2147483647)][int]$MaximumXpAgeSeconds = 86400,
    [ValidateRange(1, 2147483647)][int]$MaximumWordCountAgeSeconds = 604800,
    [switch]$ExcludeQuests
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'production-response-contracts.ps1')

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
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha256.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant() } finally { $sha256.Dispose() }
}

function Get-ComparableSha256 {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Bytes,
        [Parameter(Mandatory = $true)][bool]$NormalizeText
    )
    if (!$NormalizeText) {
        return Get-Sha256 -Bytes $Bytes
    }
    $text = [System.Text.Encoding]::UTF8.GetString($Bytes).Replace("`r`n", "`n").Replace("`r", "`n")
    return Get-Sha256 -Bytes ([System.Text.Encoding]::UTF8.GetBytes($text))
}

function Get-ProductionStaticResponse {
    param(
        [Parameter(Mandatory = $true)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)][uri]$RequestUri,
        [ValidateRange(1, 6)][int]$MaximumAttempts = 5
    )

    $backoffSeconds = @(2, 5, 10, 20, 620)
    $lastError = $null
    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        $response = $null
        try {
            $response = $Client.GetAsync($RequestUri).GetAwaiter().GetResult()
            $statusCode = [int]$response.StatusCode
            $retryable = ($statusCode -eq 403) -or ($statusCode -eq 408) -or ($statusCode -eq 429) -or ($statusCode -ge 500)
            if (!$retryable -or $attempt -eq $MaximumAttempts) {
                return $response
            }
            $response.Dispose()
        }
        catch {
            $lastError = $_
            if ($attempt -eq $MaximumAttempts) {
                throw
            }
        }
        Start-Sleep -Seconds $backoffSeconds[$attempt - 1]
    }
    if ($null -ne $lastError) {
        throw $lastError
    }
    throw 'The production static-response retry loop ended unexpectedly.'
}

function Invoke-ProductionMonitorLogout {
    param(
        [Parameter(Mandatory = $true)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)][uri]$ApiBaseUri,
        [Parameter(Mandatory = $true)][string]$Origin,
        [Parameter(Mandatory = $true)][string]$CsrfToken
    )

    Assert-Condition -Condition (![string]::IsNullOrWhiteSpace($CsrfToken)) -Message 'The production monitor cannot close its authenticated session without a CSRF token.'
    $logoutRequest = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::Post,
        [uri]::new($ApiBaseUri, 'logout'))
    $logoutRequest.Headers.TryAddWithoutValidation('Origin', $Origin) | Out-Null
    $logoutRequest.Headers.TryAddWithoutValidation('X-CSRF-Token', $CsrfToken) | Out-Null
    $logoutResponse = $null
    $postLogoutSessionResponse = $null
    try {
        $logoutResponse = $Client.SendAsync($logoutRequest).GetAwaiter().GetResult()
        $logoutPayload = $logoutResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() |
            ConvertFrom-Json
        Assert-Condition -Condition $logoutResponse.IsSuccessStatusCode -Message 'The production monitor session could not log out.'
        Assert-ProductionAnonymousSessionResponse -Payload $logoutPayload
        Assert-Condition -Condition ((Get-HeaderValue $logoutResponse 'Cache-Control') -match 'no-store') -Message 'Logout responses must use Cache-Control: no-store.'

        $postLogoutSessionResponse = $Client.GetAsync([uri]::new($ApiBaseUri, 'session')).GetAwaiter().GetResult()
        $postLogoutSessionPayload = $postLogoutSessionResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() |
            ConvertFrom-Json
        Assert-Condition -Condition $postLogoutSessionResponse.IsSuccessStatusCode -Message 'The post-logout session endpoint is unavailable.'
        Assert-ProductionAnonymousSessionResponse -Payload $postLogoutSessionPayload
        Assert-Condition -Condition ((Get-HeaderValue $postLogoutSessionResponse 'Cache-Control') -match 'no-store') -Message 'Post-logout session responses must use Cache-Control: no-store.'
    }
    finally {
        $logoutRequest.Dispose()
        if ($null -ne $logoutResponse) { $logoutResponse.Dispose() }
        if ($null -ne $postLogoutSessionResponse) { $postLogoutSessionResponse.Dispose() }
    }
}

Assert-Condition -Condition ($BaseUri.Scheme -eq 'https') -Message 'The deployed PWA must use HTTPS.'
Assert-Condition -Condition ($BaseUri.AbsolutePath.EndsWith('/')) -Message 'BaseUri must end with a slash.'
Assert-Condition -Condition (Test-Path -LiteralPath $PwaRoot -PathType Container) -Message "PWA root not found: $PwaRoot"

$runtimeFiles = [ordered]@{
    'index.html' = @('text/html')
    'styles.css' = @('text/css')
    'version.js' = @('application/javascript', 'text/javascript')
    'app.js' = @('application/javascript', 'text/javascript')
    'modules/translator.js' = @('application/javascript', 'text/javascript')
    'modules/search.js' = @('application/javascript', 'text/javascript')
    'modules/dice.js' = @('application/javascript', 'text/javascript')
    'modules/inbox-state.js' = @('application/javascript', 'text/javascript')
    'modules/account-session.js' = @('application/javascript', 'text/javascript')
    'modules/messages-activity.js' = @('application/javascript', 'text/javascript')
    'modules/presence.js' = @('application/javascript', 'text/javascript')
    'modules/update-lifecycle.js' = @('application/javascript', 'text/javascript')
    'service-worker-controller.js' = @('application/javascript', 'text/javascript')
    'translator-worker.js' = @('application/javascript', 'text/javascript')
    'campaign-search-worker.js' = @('application/javascript', 'text/javascript')
    'optional-pack-loader.js' = @('application/javascript', 'text/javascript')
    'optional-packs.json' = @('application/json', 'text/json')
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
    'data/party-funds.json' = @('application/json', 'text/json')
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

Add-Type -AssemblyName System.Net.Http
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
$handler.CookieContainer = [System.Net.CookieContainer]::new()
$client = [System.Net.Http.HttpClient]::new($handler)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('PlayerAssistant-PwaMonitor/1.0')
$client.DefaultRequestHeaders.CacheControl = [System.Net.Http.Headers.CacheControlHeaderValue]::new()
$client.DefaultRequestHeaders.CacheControl.NoCache = $true
$client.DefaultRequestHeaders.Pragma.TryParseAdd('no-cache') | Out-Null
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
        $response = Get-ProductionStaticResponse -Client $client -RequestUri $requestUri
        $responses[$relativePath] = $response
        Assert-Condition -Condition $response.IsSuccessStatusCode -Message "$relativePath returned HTTP $([int]$response.StatusCode)."

        $mediaType = [string]$response.Content.Headers.ContentType.MediaType
        Assert-Condition -Condition ($entry.Value -contains $mediaType) -Message "$relativePath returned unexpected content type '$mediaType'."

        $remoteBytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        $localBytes = [System.IO.File]::ReadAllBytes($localPath)
        $isText = @($entry.Value | Where-Object {
            $_ -like 'text/*' -or $_ -in @('application/javascript', 'text/javascript', 'application/json', 'text/json', 'application/manifest+json')
        }).Count -gt 0
        Assert-Condition -Condition ((Get-ComparableSha256 -Bytes $remoteBytes -NormalizeText $isText) -eq (Get-ComparableSha256 -Bytes $localBytes -NormalizeText $isText)) -Message "$relativePath does not match the local deployment file."
    }

    $indexResponse = $responses['index.html']
    Assert-Condition -Condition ((Get-HeaderValue $indexResponse 'X-Content-Type-Options') -eq 'nosniff') -Message 'The PWA is missing X-Content-Type-Options: nosniff.'
    Assert-Condition -Condition ((Get-HeaderValue $indexResponse 'Strict-Transport-Security') -match 'max-age=') -Message 'The PWA is missing HSTS.'
    $contentSecurityPolicy = Get-HeaderValue $indexResponse 'Content-Security-Policy'
    Assert-Condition -Condition ($contentSecurityPolicy.Contains("default-src 'self'") -and $contentSecurityPolicy.Contains("frame-ancestors 'none'") -and $contentSecurityPolicy.Contains("frame-src 'none'") -and $contentSecurityPolicy.Contains("object-src 'none'") -and $contentSecurityPolicy.Contains('upgrade-insecure-requests') -and $contentSecurityPolicy.Contains("connect-src 'self' https://publish-01.obsidian.md")) -Message 'The PWA Content-Security-Policy is incomplete.'

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
    Assert-ProductionAnonymousSessionResponse -Payload $sessionPayload
    Assert-Condition -Condition ((Get-HeaderValue $sessionResponse 'Cache-Control') -match 'no-store') -Message 'Session-status responses must use Cache-Control: no-store.'

    $legacyXpResponse = $client.GetAsync([uri]::new($BaseUri, 'XP/index.json')).GetAwaiter().GetResult()
    Assert-Condition -Condition (([int]$legacyXpResponse.StatusCode -eq 403) -or ([int]$legacyXpResponse.StatusCode -eq 404)) -Message 'Legacy public XP paths must return HTTP 403 or 404.'

    if ($RequireCurrentXpApi -or $RequireProtectedApi) {
        $healthResponse = $client.GetAsync([uri]::new($apiBaseUri, 'health')).GetAwaiter().GetResult()
        $healthPayload = $healthResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        Assert-Condition -Condition $healthResponse.IsSuccessStatusCode -Message 'The broker health endpoint is unavailable.'
        Assert-Condition -Condition ([int]$healthPayload.schema_version -eq 7) -Message 'The broker liveness schema is not version 7.'
        Assert-Condition -Condition ($healthPayload.status -eq 'ok') -Message 'The broker liveness endpoint is not healthy.'
        Assert-Condition -Condition ($healthPayload.PSObject.Properties.Name -notcontains 'xp_tracking_configured') -Message 'The public broker health endpoint disclosed XP readiness.'
        Assert-Condition -Condition ($healthPayload.PSObject.Properties.Name -notcontains 'character_account_count') -Message 'The public broker health endpoint disclosed account counts.'

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

    if ($RequireProtectedApi) {
        Assert-Condition -Condition (![string]::IsNullOrWhiteSpace($MonitorCharacterName)) -Message 'PWA monitor character name is required for protected API verification.'
        Assert-Condition -Condition (![string]::IsNullOrWhiteSpace($MonitorPassword)) -Message 'PWA monitor password is required for protected API verification.'

        $loginRequest = [System.Net.Http.HttpRequestMessage]::new(
            [System.Net.Http.HttpMethod]::Post,
            [uri]::new($apiBaseUri, 'login'))
        $loginRequest.Headers.TryAddWithoutValidation(
            'Origin',
            $BaseUri.GetLeftPart([System.UriPartial]::Authority)) | Out-Null
        $loginBody = @{
            character_name = $MonitorCharacterName
            password = $MonitorPassword
        } | ConvertTo-Json -Compress
        $loginRequest.Content = [System.Net.Http.StringContent]::new(
            $loginBody,
            [System.Text.Encoding]::UTF8,
            'application/json')
        $loginResponse = $null
        $identityResponse = $null
        $protectedXpResponse = $null
        $protectedWordCountResponse = $null
        $authenticated = $false
        $monitorCsrfToken = ''
        $protectedFailure = $null
        try {
            $loginResponse = $client.SendAsync($loginRequest).GetAwaiter().GetResult()
            Assert-Condition -Condition $loginResponse.IsSuccessStatusCode -Message 'The production monitor account could not authenticate.'
            $authenticated = $true
            $loginPayload = $loginResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
            if ($loginPayload.csrf_token -is [string]) {
                $monitorCsrfToken = $loginPayload.csrf_token
            }
            Assert-ProductionLoginResponse -Payload $loginPayload
            Assert-Condition -Condition ((Get-HeaderValue $loginResponse 'Cache-Control') -match 'no-store') -Message 'Login responses must use Cache-Control: no-store.'

            $identityResponse = $client.GetAsync([uri]::new($apiBaseUri, 'me')).GetAwaiter().GetResult()
            $identityPayload = $identityResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
            Assert-Condition -Condition $identityResponse.IsSuccessStatusCode -Message 'The authorized identity endpoint is unavailable.'
            Assert-ProductionIdentityResponse -Payload $identityPayload -ExpectedAccountId $loginPayload.account.id
            Assert-Condition -Condition ((Get-HeaderValue $identityResponse 'Cache-Control') -match 'no-store') -Message 'Identity responses must use Cache-Control: no-store.'

            $protectedXpResponse = $client.GetAsync([uri]::new($apiBaseUri, 'xp')).GetAwaiter().GetResult()
            $xpPayload = $protectedXpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
            Assert-Condition -Condition $protectedXpResponse.IsSuccessStatusCode -Message 'The authorized XP endpoint is unavailable.'
            Assert-Condition -Condition ((Get-HeaderValue $protectedXpResponse 'Cache-Control') -match 'no-store') -Message 'Authorized XP responses must use Cache-Control: no-store.'
            Assert-ProductionXpResponse -Payload $xpPayload -MaximumAgeSeconds $MaximumXpAgeSeconds

            $protectedWordCountResponse = $client.GetAsync([uri]::new($apiBaseUri, 'word-counts')).GetAwaiter().GetResult()
            $wordCountPayload = $protectedWordCountResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
            Assert-Condition -Condition $protectedWordCountResponse.IsSuccessStatusCode -Message 'The authorized word-count endpoint is unavailable.'
            Assert-Condition -Condition ((Get-HeaderValue $protectedWordCountResponse 'Cache-Control') -match 'no-store') -Message 'Authorized word-count responses must use Cache-Control: no-store.'
            Assert-ProductionWordCountResponse -Payload $wordCountPayload -MaximumAgeSeconds $MaximumWordCountAgeSeconds
        }
        catch {
            $protectedFailure = $_
        }
        finally {
            $protectedFailure = Invoke-ProductionSessionCleanup `
                -Authenticated $authenticated `
                -PrimaryFailure $protectedFailure `
                -CleanupAction {
                    Invoke-ProductionMonitorLogout `
                        -Client $client `
                        -ApiBaseUri $apiBaseUri `
                        -Origin ($BaseUri.GetLeftPart([System.UriPartial]::Authority)) `
                        -CsrfToken $monitorCsrfToken
                }
            $loginRequest.Dispose()
            if ($null -ne $loginResponse) { $loginResponse.Dispose() }
            if ($null -ne $identityResponse) { $identityResponse.Dispose() }
            if ($null -ne $protectedXpResponse) { $protectedXpResponse.Dispose() }
            if ($null -ne $protectedWordCountResponse) { $protectedWordCountResponse.Dispose() }
        }
        if ($null -ne $protectedFailure) {
            throw $protectedFailure
        }
    }

    Write-Output "PWA deployment verified: $($runtimeFiles.Count) public runtime files match, security/cache headers are valid, and anonymous session handling is safe."
    if ($RequireProtectedApi) {
        Write-Output 'Authorized XP and word-count response shapes and freshness are valid.'
    }
    elseif (!$RequireCurrentXpApi) {
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
