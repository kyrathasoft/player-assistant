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
$env:PLAYER_ASSISTANT_BROKER_CONFIG = $configPath
$env:PLAYER_ASSISTANT_TEST_DATABASE = $databasePath
$env:PLAYER_ASSISTANT_TEST_SNAPSHOTS = $snapshotPath

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

    $adminHeaders = @{ 'X-Broker-Admin-Key' = 'http-test-administrator-key' }
    $createResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Method Post `
        -Uri "$baseUrl/v1/admin/character-accounts" `
        -Headers $adminHeaders `
        -ContentType 'application/json' `
        -Body (@{
            character_name = 'HTTP Hero'
            password = 'http integration password'
            character_key = 'http-hero'
            role = 'player'
        } | ConvertTo-Json -Compress)
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

    $sessionResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Uri "$baseUrl/v1/session" `
        -Headers @{ Cookie = $cookieHeader }
    $sessionBody = $sessionResponse.Content | ConvertFrom-Json
    Assert-Condition -Condition ($sessionBody.authenticated -eq $true) -Message 'The HTTP session was not restored.'

    $identityResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Uri "$baseUrl/v1/me" `
        -Headers @{ Cookie = $cookieHeader }
    $identity = $identityResponse.Content | ConvertFrom-Json
    Assert-Condition -Condition ($identity.account.character_key -eq 'http-hero') -Message 'The protected identity was not session-authorized.'

    $logoutResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Method Post `
        -Uri "$baseUrl/v1/logout" `
        -Headers @{
            Cookie = $cookieHeader
            Origin = 'https://example.test'
            'X-CSRF-Token' = [string]$sessionBody.csrf_token
        } `
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
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if ($resolvedTemporaryRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTemporaryRoot) -like 'player-assistant-http-auth-*') {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
