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
$sundayMorningTrigger = [TimeSpan]::FromHours(8)
$sundayTrigger = @($task.Triggers | Where-Object {
    ([DateTimeOffset]$_.StartBoundary).TimeOfDay -eq $sundayMorningTrigger -and
    ([int]$_.DaysOfWeek -band 1) -eq 1
})
$wednesdayEveningTrigger = [TimeSpan]::FromHours(17)
$wednesdayTrigger = @($task.Triggers | Where-Object {
    ([DateTimeOffset]$_.StartBoundary).TimeOfDay -eq $wednesdayEveningTrigger -and
    ([int]$_.DaysOfWeek -band 8) -eq 8
})

if (-not [bool]$task.Settings.StartWhenAvailable -or
    $triggerTimes.Count -ne 4 -or
    $triggerTimes -notcontains $morningTrigger -or
    $triggerTimes -notcontains $eveningTrigger -or
    $sundayTrigger.Count -ne 1 -or
    $wednesdayTrigger.Count -ne 1 -or
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
    Triggers           = @('Daily 04:00', 'Daily 20:30', 'Sunday 08:00', 'Wednesday 17:00')
    StartWhenAvailable = [bool]$task.Settings.StartWhenAvailable
    NextRunTime        = $taskInfo.NextRunTime
    PublisherPath      = $publisherPath
}
