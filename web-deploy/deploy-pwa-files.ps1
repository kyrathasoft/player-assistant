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

    [string]$DreamHostTarget = 'dh_4gg2za@pdx1-shared-a1-13.dreamhost.com',
    [string]$SshKeyPath = (Join-Path $HOME '.ssh\dreamhost_player_assistant'),
    [string]$RemoteDirectory = '/home/dh_4gg2za/bryanmiller.us/scarlethorizons/pwa'
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
    $installer = @'
<?php
$data = json_decode(base64_decode('__MANIFEST__'), true, 32, JSON_THROW_ON_ERROR);
$installed = [];
foreach ($data['files'] as $file) {
    $candidate = $data['stage'] . '/' . $file;
    if (!is_file($candidate) || hash_file('sha256', $candidate) !== $data['hashes'][$file]) {
        throw new RuntimeException('Release hash mismatch: ' . $file);
    }
}
try {
    foreach ($data['files'] as $file) {
        $target = $data['directory'] . '/' . $file;
        $backup = $target . '.rollback-' . $data['release_id'];
        if (is_file($target) && !copy($target, $backup)) {
            throw new RuntimeException('Release backup failed: ' . $file);
        }
        chmod($data['stage'] . '/' . $file, 0644);
        if (!rename($data['stage'] . '/' . $file, $target)) {
            throw new RuntimeException('Release install failed: ' . $file);
        }
        $installed[] = $file;
    }
    foreach ($data['files'] as $file) {
        @unlink($data['directory'] . '/' . $file . '.rollback-' . $data['release_id']);
    }
} catch (Throwable $error) {
    foreach (array_reverse($installed) as $file) {
        $target = $data['directory'] . '/' . $file;
        $backup = $target . '.rollback-' . $data['release_id'];
        if (is_file($backup)) {
            rename($backup, $target);
        }
    }
    throw $error;
}
echo "PWA release installed.\n";
'@.Replace('__MANIFEST__', $manifest64)
    [IO.File]::WriteAllText(
        (Join-Path $localStage 'install.php'),
        $installer,
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

    $command = "mkdir '$remoteStage' && tar -xf '$remoteArchive' -C '$remoteStage' && /usr/bin/php '$remoteStage/install.php' && rm -rf -- '$remoteStage' '$remoteArchive'"
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
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolvedStage = [IO.Path]::GetFullPath($localStage)
    if ($resolvedStage.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $localStage -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $localArchive -Force -ErrorAction SilentlyContinue
    }
}
