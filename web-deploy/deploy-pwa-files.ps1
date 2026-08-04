[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({
        $valid = (($_ -eq '.htaccess') -or ($_ -match '^[A-Za-z0-9][A-Za-z0-9._/-]*$')) `
            -and ($_ -notmatch '(^|/)\.\.($|/)') `
            -and (-not [IO.Path]::IsPathRooted($_))
        if (-not $valid) {
            throw "Unsafe PWA deployment path: $_"
        }
        return $true
    })]
    [string[]]$Files,

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._@-]*$')]
    [string]$DreamHostTarget = 'player-assistant-dreamhost',
    [string]$SshKeyPath = (Join-Path $HOME '.ssh\dreamhost_player_assistant'),
    [ValidateScript({
        if ($_ -notmatch '^/[A-Za-z0-9._/-]+$' -or $_ -match '(^|/)\.\.($|/)') {
            throw "Unsafe remote PWA directory: $_"
        }
        return $true
    })]
    [string]$RemoteDirectory = '/home/dh_4gg2za/bryanmiller.us/scarlethorizons/pwa',
    [ValidateScript({
        if ($_ -notmatch '^/[A-Za-z0-9._/-]+$' -or $_ -match '(^|/)\.\.($|/)') {
            throw "Unsafe remote PWA release root: $_"
        }
        return $true
    })]
    [string]$RemoteReleaseRoot = '/home/dh_4gg2za/.player-assistant-pwa-releases',
    [uri]$PublicBaseUri = 'https://bryanmiller.us/scarlethorizons/pwa/'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'pwa-deployment.ps1')

$releaseId = [Guid]::NewGuid().ToString('N')
$localStage = Join-Path ([IO.Path]::GetTempPath()) "player-assistant-pwa-$releaseId"
$localArchive = "$localStage.tar"
$remoteSeparator = $RemoteDirectory.LastIndexOf('/')
$remoteParent = $RemoteDirectory.Substring(0, $remoteSeparator)
$remoteName = $RemoteDirectory.Substring($remoteSeparator + 1)
$remoteStage = "$RemoteReleaseRoot/release-$releaseId"
$remoteArchive = "$remoteStage.tar"
$remoteInstaller = "$RemoteReleaseRoot/installer-$releaseId.php"
$remoteLink = "$remoteParent/.$remoteName-link-$releaseId"
$pwaDirectory = Join-Path $PSScriptRoot '..\pwa'
$installerPath = Join-Path $PSScriptRoot 'pwa-release-installer.php'
$deploymentTestPath = Join-Path $PSScriptRoot '..\pwa\test-deployment.ps1'

if ($PublicBaseUri.Scheme -ne 'https' -or !$PublicBaseUri.AbsolutePath.EndsWith('/')) {
    throw 'PublicBaseUri must be an HTTPS URL ending with a slash.'
}
if ($Files.Count -ne @($Files | Sort-Object -Unique).Count) {
    throw 'PWA deployment file paths must be unique.'
}
if ($Files.Count -eq 0) {
    throw 'At least one PWA deployment file is required.'
}
if ($RemoteReleaseRoot -eq $RemoteDirectory -or $RemoteReleaseRoot.StartsWith("$RemoteDirectory/", [StringComparison]::Ordinal)) {
    throw 'RemoteReleaseRoot must be outside the public PWA directory.'
}
if (-not (Test-Path -LiteralPath $SshKeyPath -PathType Leaf)) {
    throw "SSH key not found: $SshKeyPath"
}

try {
    New-Item -ItemType Directory -Path $localStage | Out-Null
    $hashes = @{}
    foreach ($file in $Files) {
        $source = Join-Path $pwaDirectory $file
        $destination = Join-Path $localStage $file
        $destinationDirectory = Split-Path -Parent $destination
        if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container)) {
            New-Item -ItemType Directory -Path $destinationDirectory | Out-Null
        }
        Copy-Item -LiteralPath $source -Destination $destination
        $hashes[$file] = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant()
    }

    $manifest = @{
        directory = $RemoteDirectory
        release_root = $RemoteReleaseRoot
        stage = $remoteStage
        release_id = $releaseId
        watchdog_seconds = 900
        files = $Files
        hashes = $hashes
    } | ConvertTo-Json -Compress
    $manifest64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($manifest))

    & tar -cf $localArchive -C $localStage -- @Files
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to create the PWA release archive.'
    }

    $prepareCommand = "mkdir -p -- '$RemoteReleaseRoot' && test ! -e '$RemoteReleaseRoot/transaction.json'"
    $prepared = $false
    for ($attempt = 1; $attempt -le 3 -and -not $prepared; $attempt++) {
        & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 `
            $DreamHostTarget $prepareCommand
        $prepared = $LASTEXITCODE -eq 0
        if (-not $prepared) {
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
    if (-not $prepared) {
        throw 'Unable to prepare the private PWA release root; check for a pending transaction.'
    }

    $uploaded = $false
    for ($attempt = 1; $attempt -le 3 -and -not $uploaded; $attempt++) {
        & scp -q -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 -- `
            $localArchive "${DreamHostTarget}:$remoteArchive"
        $uploaded = $LASTEXITCODE -eq 0
        if ($uploaded) {
            & scp -q -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
                -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 -- `
                $installerPath "${DreamHostTarget}:$remoteInstaller"
            $uploaded = $LASTEXITCODE -eq 0
        }
        if (-not $uploaded) {
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
    if (-not $uploaded) {
        throw 'Unable to upload the PWA release archive.'
    }

    $extractCommand = "set -e; mkdir -p -- '$RemoteReleaseRoot'; test ! -e '$RemoteReleaseRoot/transaction.json'; rm -rf -- '$remoteStage'; live_source='$RemoteDirectory'; if [ -L '$RemoteDirectory' ]; then live_source=`$(readlink -f -- '$RemoteDirectory'); fi; cp -a -- `"`$live_source`" '$remoteStage'; tar -xf '$remoteArchive' -C '$remoteStage'"
    $extracted = $false
    for ($attempt = 1; $attempt -le 3 -and -not $extracted; $attempt++) {
        & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 `
            $DreamHostTarget $extractCommand
        $extracted = $LASTEXITCODE -eq 0
        if (-not $extracted) {
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
    if (-not $extracted) {
        throw 'Unable to extract the PWA release.'
    }

    $invokeRemoteAction = {
        param(
            [ValidateSet('install', 'commit', 'rollback')][string]$Action,
            [ValidateRange(1, 3)][int]$Attempts = 1
        )
        $actionCommand = "/usr/bin/php '$remoteInstaller' '$Action' '$manifest64'"
        for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
            & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
                -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 `
                $DreamHostTarget $actionCommand
            if ($LASTEXITCODE -eq 0) {
                return
            }
            if ($attempt -lt $Attempts) {
                Start-Sleep -Seconds (2 * $attempt)
            }
        }
        throw "Unable to $Action the PWA release after $Attempts attempts."
    }
    $cleanupRemote = {
        $cleanupCommand = "rm -f -- '$remoteInstaller' '$remoteArchive' '$remoteLink'"
        & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 `
            $DreamHostTarget $cleanupCommand
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to clean up the staged PWA release.'
        }
    }

    Invoke-PwaDeploymentTransaction `
        -InstallRelease { & $invokeRemoteAction 'install' 3 } `
        -VerifyPublic {
            & $deploymentTestPath -BaseUri $PublicBaseUri -PwaRoot $pwaDirectory -Files $Files -RequireCurrentXpApi
        } `
        -CommitRelease { & $invokeRemoteAction 'commit' } `
        -RollbackRelease { & $invokeRemoteAction 'rollback'; & $cleanupRemote }

    try {
        & $cleanupRemote
    }
    catch {
        Write-Warning "The verified PWA release is active, but staged-file cleanup failed: $($_.Exception.Message)"
    }

    Write-Output "PWA release $releaseId installed and publicly verified."
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolvedStage = [IO.Path]::GetFullPath($localStage)
    if ($resolvedStage.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $localStage -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $localArchive -Force -ErrorAction SilentlyContinue
    }
}
