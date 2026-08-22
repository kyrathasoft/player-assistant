param(
    [Parameter(Mandatory = $true)][string]$PhpPath
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

function New-BrokerAdminHeaders {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Route,
        [Parameter(Mandatory = $true)][string]$BodyJson,
        [Parameter(Mandatory = $true)][string]$Key
    )
    $timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
    $nonce = [Guid]::NewGuid().ToString('N')
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bodyHash = [BitConverter]::ToString(
            $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($BodyJson)))
    }
    finally {
        $sha.Dispose()
    }
    $bodyHash = $bodyHash.Replace('-', '').ToLowerInvariant()
    $canonical = "$timestamp`n$nonce`n$($Method.ToUpperInvariant())`n$Route`n$bodyHash"
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Key))
    try {
        $signature = [BitConverter]::ToString(
            $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hmac.Dispose()
    }
    return @{
        'X-Broker-Admin-Timestamp' = $timestamp
        'X-Broker-Admin-Nonce' = $nonce
        'X-Broker-Admin-Signature' = $signature
    }
}

function Invoke-WebRequestAllowError {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [hashtable]$Headers = @{},
        [Microsoft.PowerShell.Commands.WebRequestSession]$WebSession,
        [string]$Method = 'Get',
        [string]$ContentType = '',
        [string]$Body = ''
    )
    $requestParameters = @{
        UseBasicParsing = $true
        Uri = $Uri
        Headers = $Headers
        Method = $Method
    }
    if ($null -ne $WebSession) {
        $requestParameters.WebSession = $WebSession
    }
    if (![string]::IsNullOrWhiteSpace($ContentType)) {
        $requestParameters.ContentType = $ContentType
    }
    if ($Body -ne '') {
        $requestParameters.Body = $Body
    }
    try {
        return Invoke-WebRequest @requestParameters
    }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response) {
            throw
        }
        if ($response.GetType().FullName -eq 'System.Net.Http.HttpResponseMessage') {
            $content = [string]$_.ErrorDetails.Message
            if ([string]::IsNullOrWhiteSpace($content)) {
                try {
                    $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                }
                catch [ObjectDisposedException] {
                    $content = ''
                }
            }
            $responseHeaders = @{}
            foreach ($header in $response.Headers) {
                $responseHeaders[$header.Key] = @($header.Value) -join ', '
            }
            foreach ($header in $response.Content.Headers) {
                $responseHeaders[$header.Key] = @($header.Value) -join ', '
            }
        }
        else {
            $reader = [IO.StreamReader]::new($response.GetResponseStream())
            try {
                $content = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
            $responseHeaders = $response.Headers
        }
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Headers = $responseHeaders
            Content = $content
        }
    }
}

$resolvedPhpPath = (Resolve-Path -LiteralPath $PhpPath).Path
$phpRoot = Split-Path -Parent $resolvedPhpPath
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$documentRoot = Join-Path $repositoryRoot 'web-deploy\bryanmiller.us'
$routerPath = Join-Path $repositoryRoot 'web-deploy\tests\http-router.php'
$configPath = Join-Path $repositoryRoot 'web-deploy\tests\http-config.php'
$temporaryRoot = Join-Path $env:TEMP "player-assistant-http-auth-$([guid]::NewGuid().ToString('N'))"
$databasePath = Join-Path $temporaryRoot 'broker.sqlite'
$snapshotPath = Join-Path $temporaryRoot 'snapshots'
New-Item -ItemType Directory -Path $temporaryRoot, $snapshotPath -Force | Out-Null

$listener = [System.Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()
$baseUrl = "http://127.0.0.1:$port/scarlethorizons/api"

$previousConfig = $env:PLAYER_ASSISTANT_BROKER_CONFIG
$previousDatabase = $env:PLAYER_ASSISTANT_TEST_DATABASE
$previousSnapshots = $env:PLAYER_ASSISTANT_TEST_SNAPSHOTS
$previousQuests = $env:PLAYER_ASSISTANT_QUESTS_PATH
$env:PLAYER_ASSISTANT_BROKER_CONFIG = $configPath
$env:PLAYER_ASSISTANT_TEST_DATABASE = $databasePath
$env:PLAYER_ASSISTANT_TEST_SNAPSHOTS = $snapshotPath
$env:PLAYER_ASSISTANT_QUESTS_PATH = Join-Path $repositoryRoot 'pwa\quests.json'

$migrationPath = Join-Path $repositoryRoot 'web-deploy\player-assistant-broker\migrate-broker.php'
$migrationOutput = & $resolvedPhpPath `
    '-n' `
    '-d' "extension_dir=$(Join-Path $phpRoot 'ext')" `
    '-d' 'extension=pdo_sqlite' `
    $migrationPath 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "The HTTP test broker migration failed: $migrationOutput"
}

$process = $null
try {
    $process = Start-Process `
        -FilePath $resolvedPhpPath `
        -ArgumentList @(
            '-n',
            '-d', "extension_dir=$(Join-Path $phpRoot 'ext')",
            '-d', 'extension=pdo_sqlite',
            '-d', 'extension=curl',
            '-S', "127.0.0.1:$port",
            '-t', $documentRoot,
            $routerPath
        ) `
        -WindowStyle Hidden `
        -PassThru

    $health = $null
    for ($attempt = 0; $attempt -lt 30 -and $null -eq $health; $attempt++) {
        try {
            $health = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/v1/health"
        }
        catch {
            Start-Sleep -Milliseconds 100
        }
    }
    Assert-Condition -Condition ($null -ne $health -and $health.StatusCode -eq 200) -Message 'The HTTP test broker did not start.'
    Assert-Condition -Condition ($health.Headers['Cache-Control'] -contains 'no-store') -Message 'The API response was cacheable.'
    Assert-Condition -Condition ($health.Headers['Strict-Transport-Security'] -contains 'max-age=31536000') -Message 'The HSTS header was missing.'
    $healthBody = $health.Content | ConvertFrom-Json
    Assert-Condition -Condition ([int]$healthBody.schema_version -eq 7 -and $healthBody.status -eq 'ok' -and $null -eq $healthBody.character_account_count) -Message 'The public HTTP health response disclosed operational details.'
    $adminHealthResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Uri "$baseUrl/v1/admin/health" `
        -Headers (New-BrokerAdminHeaders -Method 'GET' -Route '/v1/admin/health' -BodyJson '[]' -Key 'http-test-administrator-key')
    $adminHealthBody = $adminHealthResponse.Content | ConvertFrom-Json
    Assert-Condition -Condition ([int]$adminHealthBody.schema_version -eq 7 -and $adminHealthBody.quest_request_workflow_configured -eq $true) -Message 'The admin HTTP health response did not expose readiness.'

    $unauthenticatedXp = Invoke-WebRequestAllowError -Uri "$baseUrl/v1/xp"
    Assert-Condition -Condition ($unauthenticatedXp.StatusCode -eq 401) -Message 'The HTTP XP route was not session-protected.'
    Assert-Condition -Condition ([string]$unauthenticatedXp.Headers['Cache-Control'] -match '(?i)(^|,\s*)no-store($|,)') -Message 'The rejected XP response was cacheable.'

    $createBody = @{
        character_name = 'HTTP Hero'
        password = 'http integration password'
        character_key = 'http-hero'
        role = 'player'
    } | ConvertTo-Json -Compress
    $createResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Method Post `
        -Uri "$baseUrl/v1/admin/character-accounts" `
        -Headers (New-BrokerAdminHeaders -Method 'POST' -Route '/v1/admin/character-accounts' -BodyJson $createBody -Key 'http-test-administrator-key') `
        -ContentType 'application/json' `
        -Body $createBody
    Assert-Condition -Condition ($createResponse.StatusCode -eq 201) -Message 'The HTTP account creation route failed.'

    $loginResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Method Post `
        -Uri "$baseUrl/v1/login" `
        -Headers @{ Origin = 'https://example.test' } `
        -ContentType 'application/json' `
        -Body (@{
            character_name = 'HTTP Hero'
            password = 'http integration password'
        } | ConvertTo-Json -Compress)
    $loginBody = $loginResponse.Content | ConvertFrom-Json
    Assert-Condition -Condition ($loginBody.authenticated -eq $true) -Message 'The HTTP character login failed.'
    $setCookie = [string]$loginResponse.Headers['Set-Cookie']
    Assert-Condition -Condition ($setCookie -match 'pa_character_session=') -Message 'The character session cookie was not issued.'
    Assert-Condition -Condition ($setCookie -match '(?i);\s*Secure') -Message 'The character session cookie was not Secure.'
    Assert-Condition -Condition ($setCookie -match '(?i);\s*HttpOnly') -Message 'The character session cookie was not HttpOnly.'
    Assert-Condition -Condition ($setCookie -match '(?i);\s*SameSite=Strict') -Message 'The character session cookie was not SameSite=Strict.'
    Assert-Condition -Condition ($setCookie -match '(?i);\s*path=/scarlethorizons/api/') -Message 'The character session cookie path was too broad.'
    $cookieHeader = $setCookie.Split(';')[0]
    $cookieParts = $cookieHeader.Split('=', 2)
    $localWebSession = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $localWebSession.Cookies.Add([Net.Cookie]::new(
            $cookieParts[0],
            $cookieParts[1],
            '/scarlethorizons/api/',
            '127.0.0.1'))

    $sessionResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Uri "$baseUrl/v1/session" `
        -WebSession $localWebSession
    $sessionBody = $sessionResponse.Content | ConvertFrom-Json
    Assert-Condition -Condition ($sessionBody.authenticated -eq $true) -Message 'The HTTP session was not restored.'

    $identityResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Uri "$baseUrl/v1/me" `
        -WebSession $localWebSession
    $identity = $identityResponse.Content | ConvertFrom-Json
    Assert-Condition -Condition ($identity.account.character_key -eq 'http-hero') -Message 'The protected identity was not session-authorized.'

    $claimResponse = Invoke-WebRequestAllowError `
        -Uri "$baseUrl/v1/xp-level-up-notifications/claim" `
        -Method Post `
        -Headers @{
            Origin = 'https://example.test'
            'X-CSRF-Token' = [string]$loginBody.csrf_token
        } `
        -WebSession $localWebSession
    Assert-Condition -Condition ($claimResponse.StatusCode -eq 503) -Message 'The authenticated level-up claim route did not retain the character session.'
    $claimBody = $claimResponse.Content | ConvertFrom-Json
    Assert-Condition -Condition ($claimBody.error -eq 'xp_awards_unavailable') -Message 'The authenticated level-up claim route failed for the wrong reason.'

    $acknowledgementResponse = Invoke-WebRequestAllowError `
        -Uri "$baseUrl/v1/xp-level-up-notifications/acknowledge" `
        -Method Post `
        -Headers @{
            Origin = 'https://example.test'
            'X-CSRF-Token' = [string]$loginBody.csrf_token
        } `
        -ContentType 'application/json' `
        -Body '{"notifications":[]}' `
        -WebSession $localWebSession
    Assert-Condition -Condition ($acknowledgementResponse.StatusCode -eq 503) -Message 'The authenticated level-up acknowledgement route did not retain the character session.'
    $acknowledgementBody = $acknowledgementResponse.Content | ConvertFrom-Json
    Assert-Condition -Condition ($acknowledgementBody.error -eq 'xp_awards_unavailable') -Message 'The authenticated level-up acknowledgement route failed for the wrong reason.'

    $unconfiguredXp = Invoke-WebRequestAllowError -Uri "$baseUrl/v1/xp" -WebSession $localWebSession
    $unconfiguredXpBody = $unconfiguredXp.Content | ConvertFrom-Json
    Assert-Condition -Condition ($unconfiguredXp.StatusCode -eq 503) -Message 'The unconfigured HTTP XP route did not fail closed.'
    Assert-Condition -Condition ($unconfiguredXpBody.error -eq 'xp_unavailable') -Message 'The unconfigured HTTP XP route returned the wrong error.'
    Assert-Condition -Condition ($unconfiguredXp.Content -notmatch 'publish\.obsidian\.md') -Message 'The HTTP XP error exposed its source URL.'

    $unauthenticatedWordCounts = Invoke-WebRequestAllowError -Uri "$baseUrl/v1/word-counts"
    Assert-Condition -Condition ($unauthenticatedWordCounts.StatusCode -eq 401) -Message 'The HTTP word-count route was not session-protected.'

    $unauthenticatedPresence = Invoke-WebRequestAllowError -Uri "$baseUrl/v1/presence"
    Assert-Condition -Condition ($unauthenticatedPresence.StatusCode -eq 401) -Message 'The HTTP presence route was not session-protected.'

    $observedAt = [DateTimeOffset]::UtcNow.ToString('o')
    $wordCountBody = @{
        schema_version = 1
        observed_at = $observedAt
        counting_rule_version = 'obsidian-publish-word-count-v1'
        wiki = @{ pages = 985; words = 232048 }
        ic = @{ files = 8; words = 14998 }
        ooc = @{ files = 6; words = 18652 }
    } | ConvertTo-Json -Depth 4 -Compress
    $wordCountUpload = Invoke-WebRequest `
        -UseBasicParsing `
        -Method Put `
        -Uri "$baseUrl/v1/admin/word-counts" `
        -Headers (New-BrokerAdminHeaders -Method 'PUT' -Route '/v1/admin/word-counts' -BodyJson $wordCountBody -Key 'http-test-administrator-key') `
        -ContentType 'application/json' `
        -Body $wordCountBody
    Assert-Condition -Condition ($wordCountUpload.StatusCode -eq 201) -Message 'The HTTP word-count upload failed.'

    $wordCountResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Uri "$baseUrl/v1/word-counts" `
        -WebSession $localWebSession
    $wordCountBody = $wordCountResponse.Content | ConvertFrom-Json
    Assert-Condition -Condition ($wordCountResponse.StatusCode -eq 200) -Message 'The HTTP word-count read failed.'
    Assert-Condition -Condition ([DateTimeOffset]$wordCountBody.observed_at -eq [DateTimeOffset]$observedAt) -Message 'The HTTP word-count observation time changed.'
    Assert-Condition -Condition ([long]$wordCountBody.wiki.words -eq 232048) -Message 'The HTTP wiki word count was incorrect.'
    Assert-Condition -Condition ([long]$wordCountBody.ic.words -eq 14998) -Message 'The HTTP IC word count was incorrect.'
    Assert-Condition -Condition ([long]$wordCountBody.ooc.words -eq 18652) -Message 'The HTTP OOC word count was incorrect.'
    Assert-Condition -Condition ([string]$wordCountResponse.Headers['Cache-Control'] -match '(?i)(^|,\s*)no-store($|,)') -Message 'Word-count responses must use Cache-Control: no-store.'

    $presenceResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Uri "$baseUrl/v1/presence" `
        -WebSession $localWebSession
    $presenceBody = $presenceResponse.Content | ConvertFrom-Json
    Assert-Condition -Condition ($presenceResponse.StatusCode -eq 200) -Message 'The HTTP presence route failed.'
    Assert-Condition -Condition ($presenceBody.scope -eq 'self' -and @($presenceBody.users).Count -eq 0) -Message 'A player presence response exposed other users.'
    Assert-Condition -Condition ([string]$presenceResponse.Headers['Cache-Control'] -match '(?i)(^|,\s*)no-store($|,)') -Message 'Presence responses must use Cache-Control: no-store.'

    $questResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Uri "$baseUrl/v1/quests" `
        -WebSession $localWebSession
    $questBody = $questResponse.Content | ConvertFrom-Json
    Assert-Condition -Condition ($questResponse.StatusCode -eq 200) -Message 'The HTTP quest route failed.'
    $validQuestBody = [int]$questBody.schema_version -eq 2 -and @($questBody.quests).Count -gt 0 -and (@($questBody.quests.id) -contains 'plumb-lost-caverns')
    Assert-Condition -Condition $validQuestBody -Message 'The HTTP quest route returned invalid JSON-backed data.'
    Assert-Condition -Condition (@($questBody.quests | Where-Object { $_.PSObject.Properties.Name -contains 'gated-by' -or $_.PSObject.Properties.Name -contains 'gated_by' -or $_.PSObject.Properties.Name -contains 'unlocked-by' -or $_.PSObject.Properties.Name -contains 'unlocked_by' }).Count -eq 0) -Message 'The HTTP quest route exposed gating metadata.'
    Assert-Condition -Condition ([string]$questResponse.Headers['Cache-Control'] -match '(?i)(^|,\s*)no-store($|,)') -Message 'Quest responses must use Cache-Control: no-store.'

    $questRequestResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Method Post `
        -Uri "$baseUrl/v1/quest-requests" `
        -Headers @{
            Origin = 'https://example.test'
            'X-CSRF-Token' = [string]$sessionBody.csrf_token
        } `
        -WebSession $localWebSession `
        -ContentType 'application/json' `
        -Body '{"quest_id":"plumb-lost-caverns"}'
    $questRequestBody = $questRequestResponse.Content | ConvertFrom-Json
    Assert-Condition -Condition ($questRequestResponse.StatusCode -eq 201 -and $questRequestBody.request.status -eq 'pending') -Message 'The HTTP quest-request route failed.'

    $logoutResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Method Post `
        -Uri "$baseUrl/v1/logout" `
        -Headers @{
            Origin = 'https://example.test'
            'X-CSRF-Token' = [string]$sessionBody.csrf_token
        } `
        -WebSession $localWebSession `
        -ContentType 'application/json' `
        -Body '{}'
    $logoutBody = $logoutResponse.Content | ConvertFrom-Json
    Assert-Condition -Condition ($logoutBody.authenticated -eq $false) -Message 'The HTTP logout failed.'

    Write-Output 'HTTP character authentication tests passed.'
}
finally {
    if ($process -and !$process.HasExited) {
        Stop-Process -Id $process.Id
    }
    $env:PLAYER_ASSISTANT_BROKER_CONFIG = $previousConfig
    $env:PLAYER_ASSISTANT_TEST_DATABASE = $previousDatabase
    $env:PLAYER_ASSISTANT_TEST_SNAPSHOTS = $previousSnapshots
    $env:PLAYER_ASSISTANT_QUESTS_PATH = $previousQuests
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if ($resolvedTemporaryRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTemporaryRoot) -like 'player-assistant-http-auth-*') {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
