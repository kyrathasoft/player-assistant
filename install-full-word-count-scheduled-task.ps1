[CmdletBinding()]
param(
    [string]$TaskName = 'Player Assistant Full Word Count Publisher'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$publisherPath = Join-Path $repositoryRoot 'publish-full-word-counts.ps1'

if (-not $IsWindows -and $PSVersionTable.PSVersion.Major -ge 6) {
    throw 'The full word-count scheduled task can only be installed on Windows.'
}
if (-not (Get-Command Register-ScheduledTask -ErrorAction SilentlyContinue)) {
    throw 'The Windows ScheduledTasks module is unavailable.'
}
if (-not (Test-Path -LiteralPath $publisherPath -PathType Leaf)) {
    throw "The scheduled full word-count publisher is missing: $publisherPath"
}

$timeZone = Get-TimeZone
if ($timeZone.Id -ne 'Central Standard Time') {
    throw "This task must be installed on a computer using the 'Central Standard Time' Windows time zone. Current time zone: $($timeZone.Id)"
}

& $publisherPath -InstallScheduledTask -TaskName $TaskName

$task = Get-ScheduledTask -TaskName $TaskName
$triggerTimes = @($task.Triggers | ForEach-Object {
    ([DateTimeOffset]$_.StartBoundary).TimeOfDay
})
$morningTrigger = [TimeSpan]::FromHours(4)
$eveningTrigger = [TimeSpan]::FromHours(20) + [TimeSpan]::FromMinutes(30)

if (-not [bool]$task.Settings.StartWhenAvailable -or
    $triggerTimes.Count -ne 2 -or
    $triggerTimes -notcontains $morningTrigger -or
    $triggerTimes -notcontains $eveningTrigger -or
    $task.Actions.Count -ne 1 -or
    -not $task.Actions[0].Execute.EndsWith('pwsh.exe', [StringComparison]::OrdinalIgnoreCase) -or
    -not $task.Actions[0].Arguments.Contains($publisherPath)) {
    throw "Scheduled task '$TaskName' was created but failed verification."
}

$taskInfo = Get-ScheduledTaskInfo -TaskName $TaskName
[pscustomobject]@{
    TaskName           = $task.TaskName
    State              = [string]$task.State
    TimeZone           = $timeZone.Id
    Triggers           = @('04:00', '20:30')
    StartWhenAvailable = [bool]$task.Settings.StartWhenAvailable
    NextRunTime        = $taskInfo.NextRunTime
    PublisherPath      = $publisherPath
}
