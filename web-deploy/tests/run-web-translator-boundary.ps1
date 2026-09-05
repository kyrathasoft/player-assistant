[CmdletBinding()]
param(
    [string]$PhpPath = '',
    [int]$RateLimit = 3,
    [int]$RateWindowSeconds = 2
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

if ([string]::IsNullOrWhiteSpace($PhpPath)) {
    $phpCommand = Get-Command php -ErrorAction Stop
    $PhpPath = $phpCommand.Source
}
$resolvedPhpPath = (Resolve-Path -LiteralPath $PhpPath).Path
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$webTranslatorRoot = Join-Path $repositoryRoot 'web-translator'
$boundaryTestPath = Join-Path $PSScriptRoot 'web-translator-boundary.php'
Assert-Condition (Test-Path -LiteralPath (Join-Path $webTranslatorRoot 'api.php') -PathType Leaf) 'The web translator API fixture is missing.'
Assert-Condition (Test-Path -LiteralPath $boundaryTestPath -PathType Leaf) 'The web translator boundary test is missing.'
Assert-Condition ($RateLimit -gt 0 -and $RateWindowSeconds -gt 0) 'Rate-limit settings must be positive.'

$listener = [System.Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()
$baseUrl = "http://127.0.0.1:$port"
$rateFile = Join-Path $env:TEMP "player-assistant-translator-rate-$([guid]::NewGuid().ToString('N')).json"
$stdoutPath = Join-Path $env:TEMP "player-assistant-translator-stdout-$([guid]::NewGuid().ToString('N')).log"
$stderrPath = Join-Path $env:TEMP "player-assistant-translator-stderr-$([guid]::NewGuid().ToString('N')).log"
$process = $null
$previousBaseUrl = $env:TRANSLATOR_BASE_URL
$previousRateFile = $env:TRANSLATOR_RATE_FILE
$previousRateLimit = $env:TRANSLATOR_RATE_LIMIT
$previousRateWindow = $env:TRANSLATOR_RATE_WINDOW_SECONDS
try {
    $env:TRANSLATOR_BASE_URL = $baseUrl
    $env:TRANSLATOR_RATE_FILE = $rateFile
    $env:TRANSLATOR_RATE_LIMIT = [string]$RateLimit
    $env:TRANSLATOR_RATE_WINDOW_SECONDS = [string]$RateWindowSeconds
    $process = Start-Process `
        -FilePath $resolvedPhpPath `
        -ArgumentList @('-S', "127.0.0.1:$port", '-t', $webTranslatorRoot) `
        -WorkingDirectory $webTranslatorRoot `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -WindowStyle Hidden `
        -PassThru

    $ready = $false
    for ($attempt = 0; $attempt -lt 50 -and -not $ready; $attempt++) {
        try {
            $probe = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/api.php" -Method Get
            $ready = $probe.StatusCode -eq 405
        }
        catch {
            if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 405) {
                $ready = $true
            }
            else {
                Start-Sleep -Milliseconds 100
            }
        }
    }
    Assert-Condition $ready 'The web translator HTTP fixture did not start.'

    & $resolvedPhpPath $boundaryTestPath
    if ($LASTEXITCODE -ne 0) {
        throw "The web translator boundary test failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:TRANSLATOR_BASE_URL = $previousBaseUrl
    $env:TRANSLATOR_RATE_FILE = $previousRateFile
    $env:TRANSLATOR_RATE_LIMIT = $previousRateLimit
    $env:TRANSLATOR_RATE_WINDOW_SECONDS = $previousRateWindow
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    foreach ($path in @($rateFile, $stdoutPath, $stderrPath)) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }
}
