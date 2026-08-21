param(
    [string]$RepoRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @()
    )

    & $FilePath @ArgumentList
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Native test '$Name' failed with exit code $exitCode."
    }
}

$fixture = Join-Path ([System.IO.Path]::GetTempPath()) "ci-native-fail-fast-$([guid]::NewGuid().ToString('N')).ps1"
try {
    $fixtureContent = @(
        '$ErrorActionPreference = ''Stop'''
        'Write-Output ''intentional intermediate failure'''
        'exit 17'
        'Write-Output ''this must never be reached'''
    )
    Set-Content -LiteralPath $fixture -Value $fixtureContent -Encoding UTF8

try {
    Invoke-CheckedNative -Name 'intentional intermediate failure' -FilePath (Get-Command powershell).Source -ArgumentList @('-NoProfile', '-File', $fixture)
    throw 'The CI native-command self-test did not fail on the intentional intermediate command.'
}
catch {
    if ($_.Exception.Message -notlike "*Native test 'intentional intermediate failure' failed with exit code 17.*") {
        throw
    }
}

Write-Output 'Native test fail-fast policy self-test passed.'
$global:LASTEXITCODE = 0
}
finally {
    Remove-Item -LiteralPath $fixture -Force -ErrorAction SilentlyContinue
}