param(
    [string]$ReleaseDir = (Join-Path $PSScriptRoot 'Release'),
    [string]$PublishDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [int]$StartupTimeoutSeconds = 45,
    [int]$PostHealthRunSeconds = 5,
    [string]$VerifyHealthFileOnly,
    [string[]]$TrackedReleaseFile = @(
        'settings.json',
        'settings.local.json',
        'keyword-index.json',
        'game-posts-key-terms.md',
        'sitemap.xml',
        'sitemap-keyword-urls.json',
        'game-forum-chapter-prefixes.txt',
        'game-forum-chapter-downloads.txt',
        'game-forum-aside-downloads.txt',
        'game-forum-ooc-downloads.txt',
        'rpol-storage-state.json',
        'startup-errors.log',
        'startup-health.json',
        'startup-remediation.txt'
    ),
    [switch]$KeepAppRunning,
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'

$ExecutableFileName = 'player-assistant.exe'
$HealthFileName = 'startup-health.json'
$StartupHealthSchemaVersion = 1
$StartupLogFileName = 'startup-errors.log'
$RequiredHealthPhases = @(
    'settings load',
    'runtime housekeeping',
    'configuration validation'
)
$PublishDiagnosticsToRestore = @(
    'startup-health.json',
    'startup-errors.log',
    'startup-remediation.txt',
    'rpol-storage-state.json'
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

function Get-FileManifestEntry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseDirectory,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $path = Join-Path $BaseDirectory $RelativePath
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        return [pscustomobject]@{
            RelativePath = $RelativePath
            Exists = $false
            Length = $null
            LastWriteTimeUtcTicks = $null
            Sha256 = $null
        }
    }

    $item = Get-Item -LiteralPath $path
    return [pscustomobject]@{
        RelativePath = $RelativePath
        Exists = $true
        Length = $item.Length
        LastWriteTimeUtcTicks = $item.LastWriteTimeUtc.Ticks
        Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
}

function Get-ReleaseManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [string[]]$RelativePaths
    )

    $manifest = [ordered]@{}
    foreach ($relativePath in $RelativePaths) {
        $manifest[$relativePath] = Get-FileManifestEntry -BaseDirectory $Directory -RelativePath $relativePath
    }

    return $manifest
}

function Assert-ManifestsEqual {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Before,

        [Parameter(Mandatory = $true)]
        [object]$After
    )

    $differences = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in $Before.Keys) {
        $beforeEntry = $Before[$relativePath]
        $afterEntry = $After[$relativePath]

        if ($beforeEntry.Exists -ne $afterEntry.Exists) {
            [void]$differences.Add("$relativePath existence changed from $($beforeEntry.Exists) to $($afterEntry.Exists)")
            continue
        }

        if (!$beforeEntry.Exists) {
            continue
        }

        if ($beforeEntry.Length -ne $afterEntry.Length) {
            [void]$differences.Add("$relativePath length changed from $($beforeEntry.Length) to $($afterEntry.Length)")
        }

        if ($beforeEntry.LastWriteTimeUtcTicks -ne $afterEntry.LastWriteTimeUtcTicks) {
            [void]$differences.Add("$relativePath LastWriteTimeUtc changed")
        }

        if ($beforeEntry.Sha256 -ne $afterEntry.Sha256) {
            [void]$differences.Add("$relativePath SHA256 changed")
        }
    }

    if ($differences.Count -gt 0) {
        Write-Output "Parent Release runtime artifact differences:"
        $differences | ForEach-Object { Write-Output "  $_" }
        throw "Published-folder startup modified parent Release runtime artifacts."
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
                    $health = Get-Content -Raw -LiteralPath $HealthPath | ConvertFrom-Json
                    if (Test-StartupHealthHasRequiredPhases -Health $health) {
                        return $health
                    }
                }
                catch {
                    Start-Sleep -Milliseconds 250
                    continue
                }
            }
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Timed out after $TimeoutSeconds seconds waiting for fresh published $HealthFileName."
}

function Test-StartupHealthHasRequiredPhases {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Health
    )

    if ($null -eq $Health.PSObject.Properties['phases']) {
        return $false
    }

    foreach ($phaseName in $RequiredHealthPhases) {
        $phase = @($Health.phases | Where-Object { $_.phase -eq $phaseName } | Select-Object -First 1)
        if ($phase.Count -eq 0) {
            return $false
        }
    }

    return $true
}

function Assert-StartupHealth {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Health
    )

    if ($null -eq $Health.PSObject.Properties['phases']) {
        throw "$HealthFileName does not contain a phases array."
    }

    if ($null -ne $Health.PSObject.Properties['schema_version']) {
        $schemaVersion = [int]$Health.schema_version
        if ($schemaVersion -gt $StartupHealthSchemaVersion) {
            throw "$HealthFileName schema_version $schemaVersion is newer than supported version $StartupHealthSchemaVersion."
        }
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

function Backup-PublishDiagnostics {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [string]$BackupDirectory
    )

    $backedUp = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in $PublishDiagnosticsToRestore) {
        $sourcePath = Join-Path $Directory $relativePath
        if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            continue
        }

        $backupPath = Join-Path $BackupDirectory $relativePath
        $backupParent = Split-Path -Parent $backupPath
        if (![string]::IsNullOrWhiteSpace($backupParent)) {
            New-Item -ItemType Directory -Force -Path $backupParent | Out-Null
        }

        Copy-Item -LiteralPath $sourcePath -Destination $backupPath -Force
        Remove-Item -LiteralPath $sourcePath -Force
        [void]$backedUp.Add($relativePath)
    }

    return [string[]]$backedUp
}

function Restore-PublishDiagnostics {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [string]$BackupDirectory,

        [AllowNull()]
        [string[]]$BackedUpPaths = @()
    )

    foreach ($relativePath in $PublishDiagnosticsToRestore) {
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

        $targetPath = Join-Path $Directory $relativePath
        Copy-Item -LiteralPath $backupPath -Destination $targetPath -Force
    }
}

$resolvedReleaseDir = Resolve-FullPath $ReleaseDir
$resolvedPublishDir = Resolve-FullPath $PublishDir

if (![string]::IsNullOrWhiteSpace($VerifyHealthFileOnly)) {
    $resolvedHealthFile = Resolve-FullPath $VerifyHealthFileOnly
    Assert-PathInsideRepo -Path $resolvedHealthFile -Description 'startup health fixture'
    Assert-RequiredFile -Path $resolvedHealthFile -Description 'startup health fixture'
    $health = Get-Content -Raw -LiteralPath $resolvedHealthFile | ConvertFrom-Json
    Assert-StartupHealth -Health $health
    Write-Output "Startup health fixture verification passed: $resolvedHealthFile"
    return
}

Assert-PathInsideRepo -Path $resolvedReleaseDir -Description 'Release directory'
Assert-PathInsideRepo -Path $resolvedPublishDir -Description 'publish directory'

$exePath = Join-Path $resolvedPublishDir $ExecutableFileName
Assert-RequiredFile -Path $exePath -Description 'published player-assistant.exe'
Assert-RequiredFile -Path (Join-Path $resolvedPublishDir 'settings.json') -Description 'published settings.json'
Assert-RequiredFile -Path (Join-Path $resolvedPublishDir 'settings.local.json') -Description 'published settings.local.json'
Assert-RequiredFile -Path (Join-Path $resolvedPublishDir 'keyword-index.json') -Description 'published keyword-index.json'
Assert-RequiredFile -Path (Join-Path $resolvedPublishDir 'game-posts-key-terms.md') -Description 'published keyword terms file'
Assert-RequiredFile -Path (Join-Path $resolvedPublishDir 'sitemap.xml') -Description 'published sitemap.xml'

if ($PlanOnly) {
    Write-Output "Published-folder runtime integrity plan:"
    Write-Output "  ReleaseDir: $resolvedReleaseDir"
    Write-Output "  PublishDir: $resolvedPublishDir"
    Write-Output "  Executable: $exePath"
    Write-Output "  Tracked parent Release files:"
    $TrackedReleaseFile | ForEach-Object { Write-Output "    $_" }
    return
}

$runningPublishApp = @(Get-Process -Name 'player-assistant' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and (Resolve-FullPath $_.Path) -eq (Resolve-FullPath $exePath) })
if ($runningPublishApp.Count -gt 0) {
    throw "Published player-assistant.exe is already running. Close it before running runtime integrity verification."
}

$backupDirectory = Join-Path $PSScriptRoot "codex-scratch\publish-integrity-backup-$([Guid]::NewGuid().ToString('N'))"
$backedUpDiagnostics = [string[]]@()
$process = $null

try {
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    $backedUpDiagnostics = Backup-PublishDiagnostics -Directory $resolvedPublishDir -BackupDirectory $backupDirectory

    $beforeManifest = Get-ReleaseManifest -Directory $resolvedReleaseDir -RelativePaths $TrackedReleaseFile
    $startedAtUtc = [DateTime]::UtcNow
    $process = Start-Process -FilePath $exePath -ArgumentList '--suppress-hero-images' -WorkingDirectory $resolvedPublishDir -PassThru
    $health = Wait-ForStartupHealth -HealthPath (Join-Path $resolvedPublishDir $HealthFileName) -StartedAfterUtc $startedAtUtc -TimeoutSeconds $StartupTimeoutSeconds
    Assert-StartupHealth -Health $health

    if ($PostHealthRunSeconds -gt 0) {
        Start-Sleep -Seconds $PostHealthRunSeconds
    }

    $afterManifest = Get-ReleaseManifest -Directory $resolvedReleaseDir -RelativePaths $TrackedReleaseFile
    Assert-ManifestsEqual -Before $beforeManifest -After $afterManifest

    Write-Output "Published-folder runtime integrity verification passed."
    Write-Output "  Health phases: $(@($health.phases).Count)"
    Write-Output "  Parent Release files checked: $($TrackedReleaseFile.Count)"
    Write-Output "  Backed up published diagnostics: $($backedUpDiagnostics.Count)"
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
        Restore-PublishDiagnostics -Directory $resolvedPublishDir -BackupDirectory $backupDirectory -BackedUpPaths ([string[]]$backedUpDiagnostics)
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }
}
