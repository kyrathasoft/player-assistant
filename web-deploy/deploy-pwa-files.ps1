[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({
        $valid = ($_ -match '^[A-Za-z0-9][A-Za-z0-9._/-]*$') `
            -and ($_ -notmatch '(^|/)\.\.($|/)') `
            -and (-not [IO.Path]::IsPathRooted($_))
        if (-not $valid) {
            throw "Unsafe PWA deployment path: $_"
        }
        return $true
    })]
    [string[]]$Files,

    [string]$DreamHostTarget = 'player-assistant-dreamhost',
    [string]$SshKeyPath = (Join-Path $HOME '.ssh\dreamhost_player_assistant'),
    [string]$RemoteDirectory = '/home/dh_4gg2za/bryanmiller.us/scarlethorizons/pwa',
    [uri]$PublicBaseUri = 'https://bryanmiller.us/scarlethorizons/pwa/'
)

$ErrorActionPreference = 'Stop'
$releaseId = [Guid]::NewGuid().ToString('N')
$localStage = Join-Path ([IO.Path]::GetTempPath()) "player-assistant-pwa-$releaseId"
$localArchive = "$localStage.tar"
$remoteStage = "$RemoteDirectory/.release-$releaseId"
$remoteArchive = "$remoteStage.tar"
$pwaDirectory = Join-Path $PSScriptRoot '..\pwa'

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
        stage = $remoteStage
        archive = $remoteArchive
        release_id = $releaseId
        files = $Files
        hashes = $hashes
    } | ConvertTo-Json -Compress
    $manifest64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($manifest))
    $controller = @'
<?php
$data = json_decode(base64_decode('__MANIFEST__'), true, 32, JSON_THROW_ON_ERROR);
function rollback_release(array $data, ?array $installedFiles = null): void {
    $files = $installedFiles ?? $data['files'];
    foreach (array_reverse($files) as $file) {
        $target = $data['directory'] . '/' . $file;
        $backup = $target . '.rollback-' . $data['release_id'];
        if (is_file($backup)) {
            if (is_file($target) && !unlink($target)) {
                throw new RuntimeException('Rollback could not remove installed file: ' . $file);
            }
            if (!rename($backup, $target)) {
                throw new RuntimeException('Rollback could not restore file: ' . $file);
            }
        } elseif (is_file($target) && !unlink($target)) {
            throw new RuntimeException('Rollback could not remove newly introduced file: ' . $file);
        }
    }
}

$action = $argv[1] ?? 'install';
if ($action === 'install') {
    foreach ($data['files'] as $file) {
        $candidate = $data['stage'] . '/' . $file;
        if (!is_file($candidate) || hash_file('sha256', $candidate) !== $data['hashes'][$file]) {
            throw new RuntimeException('Release hash mismatch: ' . $file);
        }
    }
    $installed = [];
    try {
        foreach ($data['files'] as $file) {
            $target = $data['directory'] . '/' . $file;
            $backup = $target . '.rollback-' . $data['release_id'];
            if (is_file($target) && !copy($target, $backup)) {
                throw new RuntimeException('Release backup failed: ' . $file);
            }
            $installed[] = $file;
            chmod($data['stage'] . '/' . $file, 0644);
            if (!rename($data['stage'] . '/' . $file, $target)) {
                throw new RuntimeException('Release install failed: ' . $file);
            }
        }
    } catch (Throwable $error) {
        rollback_release($data, $installed);
        throw $error;
    }
    echo "PWA release staged.\n";
} elseif ($action === 'finalize') {
    foreach ($data['files'] as $file) {
        @unlink($data['directory'] . '/' . $file . '.rollback-' . $data['release_id']);
    }
    echo "PWA release finalized.\n";
} elseif ($action === 'rollback') {
    rollback_release($data);
    echo "PWA release rolled back.\n";
} else {
    throw new RuntimeException('Unknown release action: ' . $action);
}
'@.Replace('__MANIFEST__', $manifest64)
    [IO.File]::WriteAllText(
        (Join-Path $localStage 'install.php'),
        $controller,
        [Text.UTF8Encoding]::new($false))

    & tar -cf $localArchive -C $localStage -- @Files 'install.php'
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to create the PWA release archive.'
    }

    $uploaded = $false
    for ($attempt = 1; $attempt -le 3 -and -not $uploaded; $attempt++) {
        & scp -q -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 -- `
            $localArchive "${DreamHostTarget}:$remoteArchive"
        $uploaded = $LASTEXITCODE -eq 0
        if (-not $uploaded) {
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
    if (-not $uploaded) {
        throw 'Unable to upload the PWA release archive.'
    }

    $command = "mkdir '$remoteStage' && tar -xf '$remoteArchive' -C '$remoteStage' && /usr/bin/php '$remoteStage/install.php' install"
    $installed = $false
    for ($attempt = 1; $attempt -le 3 -and -not $installed; $attempt++) {
        & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 `
            $DreamHostTarget $command
        $installed = $LASTEXITCODE -eq 0
        if (-not $installed) {
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
    if (-not $installed) {
        throw 'Unable to install the PWA release.'
    }

    $finalized = $false
    try {
        & (Join-Path $PSScriptRoot '..\pwa\test-deployment.ps1') -BaseUri $PublicBaseUri
        if ($LASTEXITCODE -ne 0) {
            throw 'The public PWA verification failed.'
        }

        $finalizeCommand = "/usr/bin/php '$remoteStage/install.php' finalize"
        & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 `
            $DreamHostTarget $finalizeCommand
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to finalize the verified PWA release.'
        }
        $finalized = $true
    }
    catch {
        if (-not $finalized) {
            $rollbackCommand = "/usr/bin/php '$remoteStage/install.php' rollback; rm -rf -- '$remoteStage' '$remoteArchive'"
            & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
                -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 `
                $DreamHostTarget $rollbackCommand
        }
        throw
    }

    $cleanupCommand = "rm -rf -- '$remoteStage' '$remoteArchive'"
    & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
        -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 `
        $DreamHostTarget $cleanupCommand
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to clean up the finalized PWA release staging files.'
    }
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolvedStage = [IO.Path]::GetFullPath($localStage)
    if ($resolvedStage.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $localStage -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $localArchive -Force -ErrorAction SilentlyContinue
    }
}
