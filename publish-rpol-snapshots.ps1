[CmdletBinding()]
param(
    [switch]$InstallScheduledTask,
    [string]$TaskName = 'Player Assistant RPOL Snapshot Publisher',
    [int]$TimeoutSeconds = 630,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$executablePath = Join-Path (Join-Path $repositoryRoot 'Release') 'player-assistant.exe'
$resultsRoot = Join-Path (Join-Path $repositoryRoot 'Release') 'rpol-results'

function Write-AtomicJsonResult {
    param(
        [Parameter(Mandatory)] $Value,
        [Parameter(Mandatory)] [string] $Path
    )

    $directory = Split-Path -Parent $Path
    $temporaryPath = Join-Path $directory ('.rpol-result-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporaryPath -Encoding UTF8 -NoNewline
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function New-TerminalFallbackResult {
    param(
        [Parameter(Mandatory)] [string] $RunId,
        [Parameter(Mandatory)] [string] $StartedAt,
        [Parameter(Mandatory)] [ValidateSet('timeout', 'crash')] [string] $Status,
        [Parameter(Mandatory)] [string] $Stage,
        [Parameter(Mandatory)] [string] $Message,
        [string] $TimeoutCategory
    )

    [ordered]@{
        schema_version = 1
        run_id = $RunId
        started_at = $StartedAt
        ended_at = [DateTimeOffset]::UtcNow.ToString('O')
        terminal_status = $Status
        terminal_stage = $Stage
        timeout_category = $TimeoutCategory
        discovered = -1
        attempted = 0
        published = 0
        failed = 1
        errors = @($Message)
        target_outcomes = @()
        cleanup_errors = @()
        upload_completed = $false
        cursor_persisted = $false
        recovery_stage = 'terminal-fallback'
    }
}

function Assert-ResultContract {
    param(
        [Parameter(Mandatory)] $Result,
        [Parameter(Mandatory)] [string] $ExpectedRunId,
        [Parameter(Mandatory)] [DateTimeOffset] $StartedAt
    )

    if ([int]$Result.schema_version -ne 1) { throw 'RPOL result schema is unsupported.' }
    if ([string]$Result.run_id -cne $ExpectedRunId) { throw 'RPOL result run ID does not match the current invocation.' }
    $resultStartedAt = [DateTimeOffset]::Parse([string]$Result.started_at)
    $resultEndedAt = [DateTimeOffset]::Parse([string]$Result.ended_at)
    $earliestAcceptedStart = $StartedAt.AddSeconds(-5)
    if ($resultStartedAt -lt $earliestAcceptedStart -or $resultEndedAt -lt $resultStartedAt) { throw 'RPOL result timestamps are stale or invalid.' }

    $status = [string]$Result.terminal_status
    $attempted = [int]$Result.attempted
    $published = [int]$Result.published
    $failed = [int]$Result.failed
    $discovered = [int]$Result.discovered
    $outcomes = @($Result.target_outcomes)
    if ($status -in @('timeout', 'crash')) {
        if ($attempted -ne 0 -or $discovered -ge 0 -or $failed -ne 1) { throw 'RPOL terminal fallback result is inconsistent.' }
        return
    }

    if ($discovered -le 0 -or $attempted -ne 1 -or $published -lt 0 -or $failed -lt 0 -or ($published + $failed) -ne $attempted -or $outcomes.Count -ne $attempted) {
        throw 'RPOL result count invariants are invalid.'
    }
    if ($status -eq 'success' -and ($published -ne 1 -or $failed -ne 0 -or -not [bool]$Result.upload_completed -or -not [bool]$Result.cursor_persisted -or @($Result.cleanup_errors).Count -ne 0 -or -not [string]::IsNullOrWhiteSpace([string]$Result.recovery_stage))) { throw 'RPOL success result is not one-target truthful.' }
    if ($status -notin @('success', 'failure')) { throw 'RPOL terminal status is invalid.' }
}

if ($InstallScheduledTask) {
    $timeZone = Get-TimeZone
    if ($timeZone.Id -ne 'Central Standard Time') {
        throw "This task must be installed on a computer using the 'Central Standard Time' Windows time zone. Current time zone: $($timeZone.Id)"
    }
    $scriptPath = $MyInvocation.MyCommand.Path
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument (
        "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$scriptPath`"")
    $triggers = @(
        New-ScheduledTaskTrigger -Daily -At '3:00 AM'
        New-ScheduledTaskTrigger -Weekly -DaysOfWeek Wednesday -At '5:00 PM'
    )
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 1)
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Settings $settings `
        -Description 'Publishes signed RPOL game 80170 snapshots for Player Assistant.' -Force | Out-Null
    Write-Output "Installed scheduled task '$TaskName'."
    return
}

if ($ValidateOnly) {
    New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null
    [ordered]@{ results_root = $resultsRoot; valid = $true } | ConvertTo-Json
    return
}

if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }
if (-not (Test-Path -LiteralPath $executablePath)) { throw "Release executable not found: $executablePath" }

$runId = [Guid]::NewGuid().ToString('N')
$startedAt = [DateTimeOffset]::UtcNow
$runDirectory = Join-Path $resultsRoot $runId
$resultPath = Join-Path $runDirectory 'result.json'
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$process = $null
$result = $null
$resultIsValid = $false
try {
    $process = Start-Process -FilePath $executablePath -ArgumentList @('--publish-rpol-snapshots', '--rpol-run-id', $runId, '--rpol-result-path', $resultPath) -PassThru -WindowStyle Hidden
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $process.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }

    if (-not $process.HasExited) {
        $cleanupErrors = @()
        try {
            $process.Kill($true)
            if (-not $process.WaitForExit(5000)) {
                $cleanupErrors += 'process-tree cleanup did not exit within 5 seconds'
            }
        }
        catch {
            $cleanupErrors += ('process-tree cleanup failed: ' + $_.Exception.Message)
        }
        $result = New-TerminalFallbackResult -RunId $runId -StartedAt $startedAt.ToString('O') -Status 'timeout' -Stage 'wrapper-timeout' -TimeoutCategory 'wrapper-deadline' -Message 'The RPOL publisher child process exceeded the wrapper deadline.'
        $result.cleanup_errors = $cleanupErrors
        Write-AtomicJsonResult -Value $result -Path $resultPath
        $resultIsValid = $true
        $result
        throw 'RPOL snapshot publishing timed out; the child process tree was terminated.'
    }

    if (-not (Test-Path -LiteralPath $resultPath)) {
        $result = New-TerminalFallbackResult -RunId $runId -StartedAt $startedAt.ToString('O') -Status 'crash' -Stage 'wrapper-read-result' -Message ('The RPOL publisher exited without a result. Exit code: ' + $process.ExitCode)
        Write-AtomicJsonResult -Value $result -Path $resultPath
        $resultIsValid = $true
        $result
        throw 'The RPOL publisher exited without writing a terminal result.'
    }

    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    Assert-ResultContract -Result $result -ExpectedRunId $runId -StartedAt $startedAt
    $resultIsValid = $true
    $result
    if ($process.ExitCode -ne 0 -or [string]$result.terminal_status -ne 'success') {
        throw "RPOL snapshot publishing failed: status=$($result.terminal_status), discovered=$($result.discovered), attempted=$($result.attempted), published=$($result.published), failed=$($result.failed)."
    }
}
catch {
    if (-not $resultIsValid) {
        $result = New-TerminalFallbackResult -RunId $runId -StartedAt $startedAt.ToString('O') -Status 'crash' -Stage 'wrapper-invalid-result' -Message ('The RPOL wrapper rejected or could not read the child result: ' + $_.Exception.Message)
        try { Write-AtomicJsonResult -Value $result -Path $resultPath } catch { }
    }
    throw
}
finally {
    if ($null -ne $process) { $process.Dispose() }
}
