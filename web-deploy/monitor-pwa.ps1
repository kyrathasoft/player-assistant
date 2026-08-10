[CmdletBinding()]
param(
    [uri]$BaseUri = 'https://bryanmiller.us/scarlethorizons/pwa/',
    [switch]$RequireCurrentXpApi,
    [switch]$RequireProtectedApi,
    [string]$MonitorCharacterName = $env:PWA_MONITOR_CHARACTER_NAME,
    [string]$MonitorPassword = $env:PWA_MONITOR_PASSWORD,
    [ValidateRange(1, 2147483647)][int]$MaximumXpAgeSeconds = 86400,
    [ValidateRange(1, 2147483647)][int]$MaximumWordCountAgeSeconds = 604800
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$pwaVerifier = Join-Path $repoRoot 'pwa\test-deployment.ps1'
if (!(Test-Path -LiteralPath $pwaVerifier -PathType Leaf)) {
    throw "PWA deployment verifier not found: $pwaVerifier"
}

$params = @{
    BaseUri = $BaseUri
    PwaRoot = Join-Path $repoRoot 'pwa'
    MaximumXpAgeSeconds = $MaximumXpAgeSeconds
    MaximumWordCountAgeSeconds = $MaximumWordCountAgeSeconds
}
if ($RequireCurrentXpApi) {
    $params.RequireCurrentXpApi = $true
}
if ($RequireProtectedApi) {
    if ([string]::IsNullOrWhiteSpace($MonitorCharacterName) -or
        [string]::IsNullOrWhiteSpace($MonitorPassword)) {
        throw 'Protected production monitoring requires PWA_MONITOR_CHARACTER_NAME and PWA_MONITOR_PASSWORD.'
    }
    $params.RequireProtectedApi = $true
    $params.MonitorCharacterName = $MonitorCharacterName
    $params.MonitorPassword = $MonitorPassword
}

& $pwaVerifier @params
if ($LASTEXITCODE -ne 0) {
    throw "PWA synthetic monitor failed with exit code $LASTEXITCODE."
}
Write-Output "PWA synthetic monitor passed: $BaseUri"
