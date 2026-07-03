param(
    [string]$PublishDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [int]$TimeoutSeconds = 20,
    [switch]$TreatWarningsAsFailure
)

$ErrorActionPreference = 'Stop'

$ExecutableFileName = 'player-assistant.exe'
$DiagnosticsToRestore = @(
    'startup-errors.log',
    'startup-remediation.txt'
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

function Assert-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required $Description is missing: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.Length -le 0) {
        throw "Required $Description is empty: $Path"
    }
}

function Invoke-HealthCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ExecutablePath
    $startInfo.Arguments = '--health'
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Unable to start published health command: $ExecutablePath --health"
    }

    if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            $process.Kill()
            $process.WaitForExit()
        }
        catch {
        }

        throw "Published health command timed out after $TimeoutSeconds seconds."
    }

    $standardOutput = $process.StandardOutput.ReadToEnd()
    $standardError = $process.StandardError.ReadToEnd()
    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = (($standardOutput, $standardError) -join [Environment]::NewLine).Trim()
    }
}

function Assert-HealthOutput {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result
    )

    if ($Result.ExitCode -ne 0) {
        throw "Published health command failed with exit code $($Result.ExitCode). Output: $($Result.Output)"
    }

    if ($Result.Output -notmatch '(?m)^player-assistant\s+') {
        throw "Published health output did not include the app version line. Output: $($Result.Output)"
    }

    if ($Result.Output -notmatch '(?m)^runtime:\s+') {
        throw "Published health output did not include runtime path. Output: $($Result.Output)"
    }

    $statusMatch = [regex]::Match($Result.Output, '(?m)^status:\s*(?<status>\S+)')
    if (!$statusMatch.Success) {
        throw "Published health output did not include status. Output: $($Result.Output)"
    }

    $status = $statusMatch.Groups['status'].Value
    if ($status -eq 'error') {
        throw "Published health reported status: error. Output: $($Result.Output)"
    }

    if ($TreatWarningsAsFailure -and $status -ne 'ok') {
        throw "Published health reported status: $status. Output: $($Result.Output)"
    }

    return $status
}

function Backup-Diagnostics {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [string]$BackupDirectory
    )

    $backedUp = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in $DiagnosticsToRestore) {
        $sourcePath = Join-Path $Directory $relativePath
        if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            continue
        }

        $backupPath = Join-Path $BackupDirectory $relativePath
        Copy-Item -LiteralPath $sourcePath -Destination $backupPath -Force
        Remove-Item -LiteralPath $sourcePath -Force
        [void]$backedUp.Add($relativePath)
    }

    return [string[]]$backedUp
}

function Restore-Diagnostics {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [string]$BackupDirectory,

        [AllowNull()]
        [string[]]$BackedUpPaths = @()
    )

    foreach ($relativePath in $DiagnosticsToRestore) {
        $targetPath = Join-Path $Directory $relativePath
        if (Test-Path -LiteralPath $targetPath -PathType Leaf) {
            Remove-Item -LiteralPath $targetPath -Force
        }
    }

    foreach ($relativePath in @($BackedUpPaths)) {
        $backupPath = Join-Path $BackupDirectory $relativePath
        if (!(Test-Path -LiteralPath $backupPath -PathType Leaf)) {
            continue
        }

        Copy-Item -LiteralPath $backupPath -Destination (Join-Path $Directory $relativePath) -Force
    }
}

$resolvedPublishDir = Resolve-FullPath $PublishDir
Assert-PathInsideRepo -Path $resolvedPublishDir -Description 'publish directory'

$exePath = Join-Path $resolvedPublishDir $ExecutableFileName
Assert-RequiredFile -Path $exePath -Description 'published player-assistant.exe'

$backupDirectory = Join-Path $PSScriptRoot "codex-scratch\published-health-backup-$([Guid]::NewGuid().ToString('N'))"
$backedUpDiagnostics = [string[]]@()

try {
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    $backedUpDiagnostics = Backup-Diagnostics -Directory $resolvedPublishDir -BackupDirectory $backupDirectory

    $result = Invoke-HealthCommand -ExecutablePath $exePath -WorkingDirectory $resolvedPublishDir -TimeoutSeconds $TimeoutSeconds
    $status = Assert-HealthOutput -Result $result
}
finally {
    if (Test-Path -LiteralPath $backupDirectory) {
        Restore-Diagnostics -Directory $resolvedPublishDir -BackupDirectory $backupDirectory -BackedUpPaths ([string[]]$backedUpDiagnostics)
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }
}

Write-Output "Published health verification passed."
Write-Output "  Status: $status"
Write-Output "  Executable: $exePath"
