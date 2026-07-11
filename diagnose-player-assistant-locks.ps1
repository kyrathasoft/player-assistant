param(
    [string]$ReleasePath = (Join-Path $PSScriptRoot 'Release\player-assistant.exe'),
    [string]$PublishPath = (Join-Path $PSScriptRoot 'Release\publish\player-assistant.exe'),
    [string[]]$AdditionalPath = @()
)

$ErrorActionPreference = 'Stop'

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [System.IO.Path]::GetFullPath($Path)
}

function Get-TargetPathSet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$RawPaths
    )

    $paths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($rawPath in $RawPaths) {
        if ([string]::IsNullOrWhiteSpace($rawPath)) {
            continue
        }

        [void]$paths.Add((Resolve-FullPath $rawPath))
    }

    return $paths
}

$rawTargetPaths = @($ReleasePath, $PublishPath) + @($AdditionalPath)
$targetPaths = Get-TargetPathSet -RawPaths $rawTargetPaths
$processes = @(Get-Process -Name 'player-assistant' -ErrorAction SilentlyContinue)
$matches = [System.Collections.Generic.List[object]]::new()
$unknownPathProcesses = [System.Collections.Generic.List[object]]::new()

foreach ($process in $processes) {
    $processPath = $null
    try {
        $processPath = $process.Path
    }
    catch {
        $unknownPathProcesses.Add([pscustomobject]@{
            Id = $process.Id
            ProcessName = $process.ProcessName
            Path = '<unavailable>'
            Reason = $_.Exception.Message
        })
        continue
    }

    if ([string]::IsNullOrWhiteSpace($processPath)) {
        $unknownPathProcesses.Add([pscustomobject]@{
            Id = $process.Id
            ProcessName = $process.ProcessName
            Path = '<unavailable>'
            Reason = 'Process path was empty.'
        })
        continue
    }

    $resolvedProcessPath = Resolve-FullPath $processPath
    if ($targetPaths.Contains($resolvedProcessPath)) {
        $matches.Add([pscustomobject]@{
            Id = $process.Id
            ProcessName = $process.ProcessName
            Path = $resolvedProcessPath
            StartTime = $(try { $process.StartTime.ToString('O') } catch { '<unavailable>' })
        })
    }
}

Write-Output 'Player Assistant process-lock diagnostics'
Write-Output 'Target executable paths:'
foreach ($targetPath in $targetPaths) {
    Write-Output "  $targetPath"
}

if ($matches.Count -eq 0) {
    Write-Output 'No running player-assistant.exe process matched the target paths.'
}
else {
    Write-Output 'Running player-assistant.exe processes matching target paths:'
    foreach ($match in $matches) {
        Write-Output "  PID $($match.Id): $($match.Path) started $($match.StartTime)"
    }
}

if ($unknownPathProcesses.Count -gt 0) {
    Write-Output 'Running player-assistant.exe processes with unavailable paths:'
    foreach ($process in $unknownPathProcesses) {
        Write-Output "  PID $($process.Id): $($process.Reason)"
    }
}
