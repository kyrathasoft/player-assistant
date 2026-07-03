param(
    [string]$ReleaseDir = (Join-Path $PSScriptRoot 'Release'),
    [int]$StartupTimeoutSeconds = 45,
    [int]$PostHealthRunSeconds = 5,
    [switch]$RefreshRuntimeArtifacts,
    [switch]$KeepAppRunning,
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'

$HealthFileName = 'startup-health.json'
$StartupLogFileName = 'startup-errors.log'
$RequiredHealthPhases = @(
    'settings load',
    'runtime housekeeping',
    'configuration validation'
)
$RuntimeArtifactsToRefresh = @(
    'startup-health.json',
    'startup-errors.log',
    'game-forum-chapter-prefixes.txt',
    'game-forum-chapter-downloads.txt',
    'game-forum-aside-downloads.txt',
    'game-forum-ooc-downloads.txt'
)
$RuntimeDirectoriesToRefresh = @(
    'Posts',
    'PCs\active',
    'Images\Maps'
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

    if (!$fullPath.StartsWith($repoRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
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

function Copy-IfExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    if (Test-Path -LiteralPath $SourcePath -PathType Container) {
        Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Recurse -Force
        return $true
    }

    if (Test-Path -LiteralPath $SourcePath -PathType Leaf) {
        $destinationDirectory = Split-Path -Parent $DestinationPath
        if (![string]::IsNullOrWhiteSpace($destinationDirectory)) {
            New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
        }

        Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
        return $true
    }

    return $false
}

function Backup-AndRemove {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseDirectory,

        [Parameter(Mandatory = $true)]
        [string]$BackupDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$RelativePaths
    )

    $backedUp = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in $RelativePaths) {
        $sourcePath = Join-Path $ReleaseDirectory $relativePath
        $backupPath = Join-Path $BackupDirectory $relativePath
        if (Copy-IfExists -SourcePath $sourcePath -DestinationPath $backupPath) {
            Remove-Item -LiteralPath $sourcePath -Recurse -Force
            [void]$backedUp.Add($relativePath)
        }
    }

    return [string[]]$backedUp
}

function Restore-Backup {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseDirectory,

        [Parameter(Mandatory = $true)]
        [string]$BackupDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$RelativePaths
    )

    foreach ($relativePath in $RelativePaths) {
        $targetPath = Join-Path $ReleaseDirectory $relativePath
        if (Test-Path -LiteralPath $targetPath) {
            Remove-Item -LiteralPath $targetPath -Recurse -Force
        }
    }

    foreach ($relativePath in $RelativePaths) {
        $backupPath = Join-Path $BackupDirectory $relativePath
        if (Test-Path -LiteralPath $backupPath) {
            $targetPath = Join-Path $ReleaseDirectory $relativePath
            $targetParent = Split-Path -Parent $targetPath
            if (![string]::IsNullOrWhiteSpace($targetParent)) {
                New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
            }

            Move-Item -LiteralPath $backupPath -Destination $targetPath -Force
        }
    }
}

function Wait-ForStartupHealth {
    param(
        [Parameter(Mandatory = $true)]
        [string]$HealthPath,

        [Parameter(Mandatory = $true)]
        [DateTime]$StartedAfterUtc,

        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $HealthPath -PathType Leaf) {
            $healthFile = Get-Item -LiteralPath $HealthPath
            if ($healthFile.LastWriteTimeUtc -ge $StartedAfterUtc) {
                try {
                    return Get-Content -Raw -LiteralPath $HealthPath | ConvertFrom-Json
                }
                catch {
                    Start-Sleep -Milliseconds 250
                    continue
                }
            }
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Timed out after $TimeoutSeconds seconds waiting for fresh $HealthFileName."
}

function Assert-StartupHealth {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Health
    )

    if ($null -eq $Health.PSObject.Properties['phases']) {
        throw "$HealthFileName does not contain a phases array."
    }

    foreach ($phaseName in $RequiredHealthPhases) {
        $phase = @($Health.phases | Where-Object { $_.phase -eq $phaseName } | Select-Object -First 1)
        if ($phase.Count -eq 0) {
            throw "$HealthFileName is missing startup phase '$phaseName'."
        }

        if ($phase[0].status -ne 'succeeded') {
            $exceptionMessage = $phase[0].last_exception.message
            throw "Startup phase '$phaseName' was '$($phase[0].status)'. $exceptionMessage"
        }
    }
}

function Assert-RegeneratedArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseDirectory
    )

    Assert-RequiredFile -Path (Join-Path $ReleaseDirectory $HealthFileName) -Description $HealthFileName
    Assert-RequiredFile -Path (Join-Path $ReleaseDirectory 'game-forum-chapter-prefixes.txt') -Description 'game-forum chapter prefix manifest'
    Assert-RequiredFile -Path (Join-Path $ReleaseDirectory 'game-forum-chapter-downloads.txt') -Description 'game-forum chapter download manifest'
    Assert-RequiredFile -Path (Join-Path $ReleaseDirectory 'game-forum-aside-downloads.txt') -Description 'game-forum aside download manifest'
    Assert-RequiredFile -Path (Join-Path $ReleaseDirectory 'game-forum-ooc-downloads.txt') -Description 'game-forum OOC download manifest'

    $postsDirectory = Join-Path $ReleaseDirectory 'Posts'
    if (!(Test-Path -LiteralPath $postsDirectory -PathType Container)) {
        throw "Posts directory was not regenerated: $postsDirectory"
    }
}

$resolvedReleaseDir = Resolve-FullPath $ReleaseDir
Assert-PathInsideRepo -Path $resolvedReleaseDir -Description 'Release directory'

$exePath = Join-Path $resolvedReleaseDir 'player-assistant.exe'
Assert-RequiredFile -Path $exePath -Description 'Release player-assistant.exe'
Assert-RequiredFile -Path (Join-Path $resolvedReleaseDir 'settings.json') -Description 'Release settings.json'
Assert-RequiredFile -Path (Join-Path $resolvedReleaseDir 'settings.local.json') -Description 'Release settings.local.json'
Assert-RequiredFile -Path (Join-Path $resolvedReleaseDir 'keyword-index.json') -Description 'Release keyword-index.json'
Assert-RequiredFile -Path (Join-Path $resolvedReleaseDir 'game-posts-key-terms.md') -Description 'Release keyword terms file'
Assert-RequiredFile -Path (Join-Path $resolvedReleaseDir 'sitemap.xml') -Description 'Release sitemap.xml'

$pathsToRefresh = @($RuntimeArtifactsToRefresh)
if ($RefreshRuntimeArtifacts) {
    $pathsToRefresh += $RuntimeDirectoriesToRefresh
}

if ($PlanOnly) {
    Write-Output "Release startup smoke plan:"
    Write-Output "  ReleaseDir: $resolvedReleaseDir"
    Write-Output "  Executable: $exePath"
    Write-Output "  RefreshRuntimeArtifacts: $RefreshRuntimeArtifacts"
    Write-Output "  Artifacts refreshed:"
    foreach ($relativePath in $pathsToRefresh) {
        Write-Output "    $relativePath"
    }
    return
}

$runningReleaseApp = @(Get-Process -Name 'player-assistant' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and (Resolve-FullPath $_.Path) -eq (Resolve-FullPath $exePath) })
if ($runningReleaseApp.Count -gt 0) {
    throw "Release player-assistant.exe is already running. Close it before running the startup smoke verification."
}

$backupDirectory = Join-Path $PSScriptRoot "codex-scratch\release-smoke-backup-$([Guid]::NewGuid().ToString('N'))"
$backedUpPaths = @()
$process = $null

try {
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    $backedUpPaths = Backup-AndRemove -ReleaseDirectory $resolvedReleaseDir -BackupDirectory $backupDirectory -RelativePaths $pathsToRefresh

    $startedAtUtc = [DateTime]::UtcNow
    $process = Start-Process -FilePath $exePath -ArgumentList '--suppress-hero-images' -WorkingDirectory $resolvedReleaseDir -PassThru
    $health = Wait-ForStartupHealth -HealthPath (Join-Path $resolvedReleaseDir $HealthFileName) -StartedAfterUtc $startedAtUtc -TimeoutSeconds $StartupTimeoutSeconds
    Assert-StartupHealth -Health $health

    if ($PostHealthRunSeconds -gt 0) {
        Start-Sleep -Seconds $PostHealthRunSeconds
    }

    if ($RefreshRuntimeArtifacts) {
        Assert-RegeneratedArtifacts -ReleaseDirectory $resolvedReleaseDir
    }

    Write-Output "Release startup smoke verification passed."
    Write-Output "  Health phases: $(@($health.phases).Count)"
    Write-Output "  Backed up artifacts: $($backedUpPaths.Count)"
}
finally {
    if ($process -and !$process.HasExited -and !$KeepAppRunning) {
        $process.CloseMainWindow() | Out-Null
        if (!$process.WaitForExit(5000)) {
            $process.Kill()
            $process.WaitForExit()
        }
    }

    if (Test-Path -LiteralPath $backupDirectory) {
        Restore-Backup -ReleaseDirectory $resolvedReleaseDir -BackupDirectory $backupDirectory -RelativePaths $pathsToRefresh
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }
}
