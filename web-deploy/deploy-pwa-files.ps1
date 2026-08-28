[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({
        $valid = ($_ -match '^[A-Za-z0-9][A-Za-z0-9._/-]*$') `
            -and ($_ -notmatch '(^|/)\.\.($|/)') `
            -and (-not [IO.Path]::IsPathRooted($_))
        if (-not $valid) { throw "Unsafe PWA deployment path: $_" }
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
$remoteState = "$remoteStage/.transaction.json"
$pwaDirectory = Join-Path $PSScriptRoot '..\pwa'
$sshOptions = @('-i', $SshKeyPath, '-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes', '-o', 'ConnectTimeout=15', '-o', 'ConnectionAttempts=1', '-o', 'ServerAliveInterval=5', '-o', 'ServerAliveCountMax=3')

function Invoke-RemoteSsh([string]$Command) {
    & ssh @sshOptions $DreamHostTarget $Command
    return $LASTEXITCODE
}

function Get-RemoteStatus {
    $output = & ssh @sshOptions $DreamHostTarget "/usr/bin/php '$remoteStage/install.php' status"
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "Unable to query PWA release transaction status (exit code $exitCode). Do not rerun release $releaseId until the remote transaction is inspected." }
    try { return ($output -join "`n" | ConvertFrom-Json) }
    catch { throw "Remote PWA release status was not valid JSON. Do not rerun release $releaseId until the remote transaction is inspected." }
}

function Invoke-RemoteRecovery {
    $status = Get-RemoteStatus
    switch ($status.state) {
        'promoted' { return $status }
        'finalized' { return $status }
        'preparing' {
            $resumeExit = Invoke-RemoteSsh "/usr/bin/php '$remoteStage/install.php' resume"
            if ($resumeExit -ne 0) { throw "PWA release $releaseId remains in preparing state and could not be resumed." }
            return Get-RemoteStatus
        }
        default { throw "PWA release $releaseId is in unexpected state '$($status.state)'. Manual transaction recovery is required." }
    }
}

try {
    New-Item -ItemType Directory -Path $localStage | Out-Null
    $hashes = @{}
    foreach ($file in $Files) {
        $source = Join-Path $pwaDirectory $file
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "PWA deployment source file not found: $file" }
        $destination = Join-Path $localStage $file
        $destinationDirectory = Split-Path -Parent $destination
        if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container)) { New-Item -ItemType Directory -Path $destinationDirectory | Out-Null }
        Copy-Item -LiteralPath $source -Destination $destination
        $hashes[$file] = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant()
    }

    $manifest = @{
        directory = $RemoteDirectory; stage = $remoteStage; archive = $remoteArchive
        state = $remoteState; release_id = $releaseId; files = $Files; hashes = $hashes
    } | ConvertTo-Json -Compress
    $manifest64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($manifest))
    $controller = @'
<?php
$data = json_decode(base64_decode('__MANIFEST__'), true, 32, JSON_THROW_ON_ERROR);
$statePath = $data['state'];
function write_state(array $data, string $state, array $installed): void {
    global $statePath;
    $payload = json_encode(['release_id'=>$data['release_id'], 'state'=>$state, 'installed'=>$installed], JSON_THROW_ON_ERROR);
    $tmp = $statePath . '.tmp';
    if (file_put_contents($tmp, $payload, LOCK_EX) === false || !rename($tmp, $statePath)) { throw new RuntimeException('Transaction state could not be persisted.'); }
}
function read_state(): array {
    global $statePath;
    if (!is_file($statePath)) { return ['state'=>'new', 'installed'=>[]]; }
    $state = json_decode(file_get_contents($statePath), true, 16, JSON_THROW_ON_ERROR);
    if (($state['release_id'] ?? '') !== $GLOBALS['data']['release_id']) { throw new RuntimeException('Transaction identity mismatch.'); }
    return $state;
}
function rollback_release(array $data, array $installed): void {
    foreach (array_reverse($installed) as $file) {
        $target = $data['directory'].'/'.$file; $backup = $target.'.rollback-'.$data['release_id'];
        if (is_file($backup)) {
            if (is_file($target) && !unlink($target)) { throw new RuntimeException('Rollback could not remove installed file: '.$file); }
            if (!rename($backup, $target)) { throw new RuntimeException('Rollback could not restore file: '.$file); }
        } elseif (is_file($target) && !unlink($target)) { throw new RuntimeException('Rollback could not remove new file: '.$file); }
    }
}
function install_release(array $data): void {
    $state = read_state();
    if ($state['state'] === 'promoted' || $state['state'] === 'finalized') { return; }
    if ($state['state'] === 'rolled_back') { throw new RuntimeException('Transaction was rolled back.'); }
    foreach ($data['files'] as $file) {
        $candidate = $data['stage'].'/'.$file;
        if (!is_file($candidate) || strtolower(hash_file('sha256', $candidate)) !== strtolower($data['hashes'][$file])) { throw new RuntimeException('Release hash mismatch: '.$file); }
    }
    $installed = $state['installed'] ?? [];
    write_state($data, 'preparing', $installed);
    try {
        foreach ($data['files'] as $file) {
            $target = $data['directory'].'/'.$file; $backup = $target.'.rollback-'.$data['release_id'];
            if (in_array($file, $installed, true) && is_file($target)
                && strtolower(hash_file('sha256', $target)) === strtolower($data['hashes'][$file])) { continue; }
            if (is_file($target) && !is_file($backup) && !copy($target, $backup)) { throw new RuntimeException('Release backup failed: '.$file); }
            if (!in_array($file, $installed, true)) { $installed[] = $file; write_state($data, 'preparing', $installed); }
            chmod($data['stage'].'/'.$file, 0644);
            $temporary = $target.'.tmp-'.$data['release_id'];
            @unlink($temporary);
            if (!copy($data['stage'].'/'.$file, $temporary) || !rename($temporary, $target)) {
                @unlink($temporary);
                throw new RuntimeException('Release install failed: '.$file);
            }
            write_state($data, 'preparing', $installed);
        }
        write_state($data, 'promoted', $installed);
    } catch (Throwable $error) {
        rollback_release($data, $installed); write_state($data, 'rolled_back', []); throw $error;
    }
}
$action = $argv[1] ?? 'status';
if ($action === 'status') { echo json_encode(read_state(), JSON_THROW_ON_ERROR); }
elseif ($action === 'install' || $action === 'resume') { install_release($data); echo json_encode(read_state(), JSON_THROW_ON_ERROR); }
elseif ($action === 'finalize') {
    $state = read_state(); if ($state['state'] === 'finalized') { echo json_encode($state, JSON_THROW_ON_ERROR); exit; }
    if ($state['state'] !== 'promoted') { throw new RuntimeException('Only a promoted transaction may be finalized.'); }
    foreach ($data['files'] as $file) { @unlink($data['directory'].'/'.$file.'.rollback-'.$data['release_id']); }
    write_state($data, 'finalized', []); echo json_encode(read_state(), JSON_THROW_ON_ERROR);
}
elseif ($action === 'rollback') {
    $state = read_state(); if ($state['state'] === 'finalized') { throw new RuntimeException('A finalized transaction cannot be rolled back.'); }
    if ($state['state'] === 'rolled_back') { echo json_encode($state, JSON_THROW_ON_ERROR); exit; }
    rollback_release($data, $state['installed'] ?? []); write_state($data, 'rolled_back', []); echo json_encode(read_state(), JSON_THROW_ON_ERROR);
}
else { throw new RuntimeException('Unknown release action: '.$action); }
'@.Replace('__MANIFEST__', $manifest64)
    [IO.File]::WriteAllText((Join-Path $localStage 'install.php'), $controller, [Text.UTF8Encoding]::new($false))
    & tar -cf $localArchive -C $localStage -- @Files 'install.php'
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create the PWA release archive.' }

    $uploaded = $false
    for ($attempt = 1; $attempt -le 3 -and -not $uploaded; $attempt++) {
        & scp -q @sshOptions -- $localArchive "${DreamHostTarget}:$remoteArchive"
        $uploaded = $LASTEXITCODE -eq 0
        if (-not $uploaded) { Start-Sleep -Seconds (2 * $attempt) }
    }
    if (-not $uploaded) { throw 'Unable to upload the PWA release archive.' }

    $prepareExit = Invoke-RemoteSsh "mkdir '$remoteStage' && tar -xf '$remoteArchive' -C '$remoteStage'"
    if ($prepareExit -ne 0) { throw 'Unable to prepare the PWA release transaction.' }
    $installExit = Invoke-RemoteSsh "/usr/bin/php '$remoteStage/install.php' install"
    if ($installExit -ne 0) { $status = Invoke-RemoteRecovery } else { $status = Get-RemoteStatus }
    if ($status.state -ne 'promoted' -and $status.state -ne 'finalized') { throw "PWA release $releaseId did not reach promoted state." }

    $finalized = $false
    try {
        & (Join-Path $PSScriptRoot '..\pwa\test-deployment.ps1') -BaseUri $PublicBaseUri
        if ($LASTEXITCODE -ne 0) { throw 'The public PWA verification failed.' }
        $finalizeExit = Invoke-RemoteSsh "/usr/bin/php '$remoteStage/install.php' finalize"
        if ($finalizeExit -ne 0) {
            $status = Get-RemoteStatus
            if ($status.state -ne 'finalized') { throw "PWA release $releaseId was not finalized." }
        }
        $finalized = $true
    } catch {
        if (-not $finalized) {
            $status = Get-RemoteStatus
            if ($status.state -eq 'finalized') { throw }
            $rollbackExit = Invoke-RemoteSsh "/usr/bin/php '$remoteStage/install.php' rollback"
            if ($rollbackExit -ne 0) { throw "PWA release $releaseId requires manual rollback; status query succeeded but rollback failed." }
        }
        throw
    }
    $cleanupExit = Invoke-RemoteSsh "rm -rf -- '$remoteStage' '$remoteArchive'"
    if ($cleanupExit -ne 0) { throw 'Unable to clean up the finalized PWA release staging files.' }
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolvedStage = [IO.Path]::GetFullPath($localStage)
    if ($resolvedStage.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $localStage -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $localArchive -Force -ErrorAction SilentlyContinue
    }
}
