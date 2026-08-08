[CmdletBinding()]
param(
    [uri]$BaseUri = 'https://bryanmiller.us/scarlethorizons/pwa/',
    [switch]$RequireCurrentXpApi
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
}
if ($RequireCurrentXpApi) {
    $params.RequireCurrentXpApi = $true
}

& $pwaVerifier @params
if ($LASTEXITCODE -ne 0) {
    throw "PWA synthetic monitor failed with exit code $LASTEXITCODE."
}
Write-Output "PWA synthetic monitor passed: $BaseUri"
