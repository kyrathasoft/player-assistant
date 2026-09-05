[CmdletBinding()]
param([string]$RepoRoot)
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path }
$ErrorActionPreference = 'Stop'
function Assert-Condition([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
$script = Get-Content -Raw (Join-Path $RepoRoot 'keep-alive-mouse.ps1')
$vbs = Get-Content -Raw (Join-Path $RepoRoot 'keep-alive-mouse-hidden.vbs')
Assert-Condition (-not ($vbs -match '(?i)[A-Z]:\\repos\\player-assistant')) 'hidden launcher contains a repository-specific path.'
Assert-Condition ($vbs.Contains('WScript.ScriptFullName') -and $vbs.Contains('GetAbsolutePathName')) 'hidden launcher must derive and validate its absolute installed script path.'
Assert-Condition ($script.Contains('KeepDisplayAwake') -and $script.Contains('MoveMouse')) 'keep-alive operations are missing.'
Assert-Condition ($script.Contains('throw') -and $script.Contains('SetLastError')) 'native failures must fail the task.'
Assert-Condition ($script.Contains('status') -and $script.Contains('1024')) 'bounded diagnostic status is missing.'
Assert-Condition ($script.Contains('IntervalSeconds') -and $script.Contains('DurationSeconds')) 'cadence and supervised duration controls are missing.'
Assert-Condition ($script.Contains('ES_DISPLAY_REQUIRED') -and -not $script.Contains('ES_SYSTEM_REQUIRED')) 'display-only behavior must not silently request system sleep prevention.'
$statusPath = Join-Path ([IO.Path]::GetTempPath()) ('keep-alive-verification-' + [Guid]::NewGuid().ToString('N') + '.json')
try {
    & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $RepoRoot 'keep-alive-mouse.ps1') -StatusPath $statusPath
    Assert-Condition ($LASTEXITCODE -eq 0) 'keep-alive task failed during the actual one-shot smoke run.'
    Assert-Condition (Test-Path -LiteralPath $statusPath -PathType Leaf) 'keep-alive task did not write observable status.'
    $status = Get-Content -Raw -LiteralPath $statusPath | ConvertFrom-Json
    Assert-Condition ([string]$status.state -eq 'success' -and [string]$status.operation -eq 'display-and-input') 'keep-alive status did not report the completed display/input operations.'
    Assert-Condition ((Get-Item -LiteralPath $statusPath).Length -le 1024) 'keep-alive diagnostic status exceeded its bound.'
}
finally { Remove-Item -LiteralPath $statusPath -Force -ErrorAction SilentlyContinue }
Write-Output 'Keep-alive policy verified.'
