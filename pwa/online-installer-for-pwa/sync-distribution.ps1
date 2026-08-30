[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$manifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'installer-source-manifest.json') | ConvertFrom-Json
foreach ($entry in @($manifest.files)) {
    $source = Join-Path $PSScriptRoot ([string]$entry.source)
    $distribution = Join-Path $PSScriptRoot ([string]$entry.distribution)
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Canonical installer source is missing: $source" }
    if ($VerifyOnly) {
        if (!(Test-Path -LiteralPath $distribution -PathType Leaf)) { throw "Generated installer distribution is missing: $distribution" }
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $distributionHash = (Get-FileHash -LiteralPath $distribution -Algorithm SHA256).Hash
        if ($sourceHash -cne $distributionHash) { throw "Installer source/dist drift: $($entry.source)" }
        continue
    }
    $destinationDirectory = Split-Path -Parent $distribution
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    Copy-Item -LiteralPath $source -Destination $distribution -Force
}
if ($VerifyOnly) { Write-Output 'Installer source/dist verification passed.' }
else { Write-Output 'Installer distributions synchronized from canonical sources.' }
