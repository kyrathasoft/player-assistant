param(
    [string]$ScratchDir = (Join-Path $PSScriptRoot 'codex-scratch'),
    [int]$DiagnosticRetentionDays = 14,
    [int]$ScratchRetentionDays = 7,
    [int]$MaxDiagnosticZipCount = 10,
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'

$DiagnosticZipPattern = 'player-assistant-diagnostics-*.zip'
$DiagnosticStagingPattern = 'player-assistant-diagnostics-*'
$ScratchDirectoryPrefixes = @(
    'diagnostics-test-',
    'publish-verification-',
    'publish-integrity-backup-',
    'release-smoke-backup-'
)
$ScratchDirectoryNames = @(
    'rc-diagnostics'
)

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-PathInsideRepo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $repoRoot = Resolve-FullPath $PSScriptRoot
    $fullPath = Resolve-FullPath $Path
    $repoRootWithSeparator = $repoRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

    if (!$fullPath.StartsWith($repoRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase) -and
        !$fullPath.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use $Description outside repo root: $fullPath"
    }
}

function Get-ItemSizeBytes {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileSystemInfo]$Item
    )

    if ($Item -is [System.IO.FileInfo]) {
        return $Item.Length
    }

    try {
        $sum = (Get-ChildItem -LiteralPath $Item.FullName -Recurse -Force -File -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
        if ($null -eq $sum) {
            return 0L
        }

        return [long]$sum
    }
    catch {
        return 0L
    }
}

function Remove-RetentionItem {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileSystemInfo]$Item,

        [Parameter(Mandatory = $true)]
        [string]$Reason,

        [Parameter(Mandatory = $true)]
        [hashtable]$Report
    )

    $bytes = Get-ItemSizeBytes -Item $Item
    $kind = if ($Item -is [System.IO.DirectoryInfo]) { 'directory' } else { 'file' }

    if ($PlanOnly) {
        Write-Output "Would remove $kind ($Reason): $($Item.FullName)"
    }
    else {
        if ($Item -is [System.IO.DirectoryInfo]) {
            Remove-Item -LiteralPath $Item.FullName -Recurse -Force
        }
        else {
            Remove-Item -LiteralPath $Item.FullName -Force
        }
        Write-Output "Removed $kind ($Reason): $($Item.FullName)"
    }

    $Report.RemovedCount++
    $Report.ReclaimedBytes += $bytes
}

function Remove-StaleItems {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.IO.FileSystemInfo[]]$Items,

        [Parameter(Mandatory = $true)]
        [datetime]$CutoffUtc,

        [Parameter(Mandatory = $true)]
        [string]$Reason,

        [Parameter(Mandatory = $true)]
        [hashtable]$Report
    )

    foreach ($item in $Items) {
        if ($item.LastWriteTimeUtc -lt $CutoffUtc) {
            Remove-RetentionItem -Item $item -Reason $Reason -Report $Report
        }
    }
}

if ($DiagnosticRetentionDays -lt 1) {
    throw 'DiagnosticRetentionDays must be at least 1.'
}

if ($ScratchRetentionDays -lt 1) {
    throw 'ScratchRetentionDays must be at least 1.'
}

if ($MaxDiagnosticZipCount -lt 1) {
    throw 'MaxDiagnosticZipCount must be at least 1.'
}

$resolvedScratchDir = Resolve-FullPath $ScratchDir
Assert-PathInsideRepo -Path $resolvedScratchDir -Description 'scratch directory'

$report = @{
    RemovedCount = 0
    ReclaimedBytes = 0L
}

if (!(Test-Path -LiteralPath $resolvedScratchDir -PathType Container)) {
    Write-Output "Diagnostic retention cleanup skipped; scratch directory does not exist: $resolvedScratchDir"
    return
}

$nowUtc = [DateTime]::UtcNow
$diagnosticCutoffUtc = $nowUtc.AddDays(-$DiagnosticRetentionDays)
$scratchCutoffUtc = $nowUtc.AddDays(-$ScratchRetentionDays)
$diagnosticsDir = Join-Path $resolvedScratchDir 'diagnostics'

if (Test-Path -LiteralPath $diagnosticsDir -PathType Container) {
    $diagnosticZips = @(Get-ChildItem -LiteralPath $diagnosticsDir -Filter $DiagnosticZipPattern -File -Force -ErrorAction SilentlyContinue)
    Remove-StaleItems -Items $diagnosticZips -CutoffUtc $diagnosticCutoffUtc -Reason "older than $DiagnosticRetentionDays day diagnostic retention" -Report $report

    $remainingZips = @(Get-ChildItem -LiteralPath $diagnosticsDir -Filter $DiagnosticZipPattern -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTimeUtc -ge $diagnosticCutoffUtc } |
        Sort-Object LastWriteTimeUtc -Descending)
    if ($remainingZips.Count -gt $MaxDiagnosticZipCount) {
        $remainingZips |
            Select-Object -Skip $MaxDiagnosticZipCount |
            ForEach-Object {
                Remove-RetentionItem -Item $_ -Reason "exceeds newest $MaxDiagnosticZipCount diagnostic bundles" -Report $report
            }
    }

    $diagnosticStagingDirectories = @(Get-ChildItem -LiteralPath $diagnosticsDir -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like $DiagnosticStagingPattern })
    Remove-StaleItems -Items $diagnosticStagingDirectories -CutoffUtc $diagnosticCutoffUtc -Reason "older than $DiagnosticRetentionDays day diagnostic staging retention" -Report $report
}

$scratchDirectories = @(Get-ChildItem -LiteralPath $resolvedScratchDir -Directory -Force -ErrorAction SilentlyContinue |
    Where-Object {
        $directoryName = $_.Name
        ($ScratchDirectoryNames -contains $directoryName) -or
            @($ScratchDirectoryPrefixes | Where-Object { $directoryName.StartsWith($_, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
    })
Remove-StaleItems -Items $scratchDirectories -CutoffUtc $scratchCutoffUtc -Reason "older than $ScratchRetentionDays day scratch retention" -Report $report

$prefix = if ($PlanOnly) { 'Diagnostic retention cleanup would remove' } else { 'Diagnostic retention cleanup removed' }
Write-Output "$prefix $($report.RemovedCount) item(s), reclaiming $($report.ReclaimedBytes) byte(s)."
