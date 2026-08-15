[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RestoreArguments
)

$ErrorActionPreference = 'Stop'
$controller = Join-Path $PSScriptRoot 'restore_dreamhost_pwa.py'
& python $controller @RestoreArguments
if ($LASTEXITCODE -ne 0) {
    throw "DreamHost restore controller failed with exit code $LASTEXITCODE."
}
