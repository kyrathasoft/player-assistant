[CmdletBinding()]
param(
    [switch]$InstallScheduledTask,
    [string]$TaskName = 'Player Assistant RPOL Snapshot Publisher'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$executablePath = Join-Path $repositoryRoot 'Release\player-assistant.exe'
$resultPath = Join-Path $repositoryRoot 'Release\rpol-snapshot-publish-result.json'

if ($InstallScheduledTask) {
    $scriptPath = $MyInvocation.MyCommand.Path
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument (
        "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`"")
    $trigger = New-ScheduledTaskTrigger -Daily -At '3:00 AM'
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 1)
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
        -Description 'Publishes signed RPOL game 80170 snapshots for Player Assistant.' -Force | Out-Null
    Write-Output "Installed scheduled task '$TaskName'."
    return
}

if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "Release executable not found: $executablePath"
}

Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
$process = Start-Process -FilePath $executablePath -ArgumentList '--publish-rpol-snapshots' -Wait -PassThru -WindowStyle Hidden
if (-not (Test-Path -LiteralPath $resultPath)) {
    throw 'The snapshot publisher did not write its result file.'
}

$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$result
if ($process.ExitCode -ne 0 -or $result.failed -ne 0 -or $result.published -lt 1) {
    throw "RPOL snapshot publishing failed: $($result.failed) failure(s), $($result.published) published."
}
