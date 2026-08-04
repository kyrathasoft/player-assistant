[CmdletBinding()]
param(
    [string]$DreamHostHost = 'pdx1-shared-a1-13.dreamhost.com',
    [string]$DreamHostUser = 'dh_4gg2za',
    [string]$SshKeyPath = (Join-Path $HOME '.ssh\dreamhost_player_assistant'),
    [string]$RemoteDirectory = '/home/dh_4gg2za/player-assistant-broker',
    [string[]]$BackupRoots = @((Join-Path $HOME 'Documents\Player Assistant\broker-backups')),
    [int]$KeepLocal = 14
)

$ErrorActionPreference = 'Stop'
$ssh = Join-Path $env:WINDIR 'System32\OpenSSH\ssh.exe'
$scp = Join-Path $env:WINDIR 'System32\OpenSSH\scp.exe'
$remote = "$DreamHostUser@$DreamHostHost"

foreach ($path in @($ssh, $scp, $SshKeyPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required deployment file is missing: $path"
    }
}

$remoteOutput = & $ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 $remote "/usr/bin/php '$RemoteDirectory/broker-recovery.php'"
if ($LASTEXITCODE -ne 0) {
    throw 'Remote broker recovery failed.'
}
$recovery = ($remoteOutput | Out-String).Trim() | ConvertFrom-Json
if ($recovery.status -ne 'ok' -or [string]::IsNullOrWhiteSpace([string]$recovery.backup_file)) {
    throw 'Remote broker recovery did not report a successful backup.'
}

$remoteBackup = "$RemoteDirectory/backups/$($recovery.backup_file)"
$remoteStatus = "$RemoteDirectory/broker-recovery-status.json"
foreach ($root in $BackupRoots) {
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    & $scp -q -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
        "$remote`:$remoteBackup" (Join-Path $root $recovery.backup_file)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to download broker backup to $root."
    }
    & $scp -q -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
        "$remote`:$remoteStatus" (Join-Path $root 'broker-recovery-status.json')
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to download broker recovery status to $root."
    }

    $localBackup = Join-Path $root $recovery.backup_file
    $localHash = (Get-FileHash -LiteralPath $localBackup -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($localHash -ne ([string]$recovery.backup_sha256).ToLowerInvariant()) {
        throw "Broker backup hash mismatch in $root."
    }

    $backups = @(Get-ChildItem -LiteralPath $root -Filter 'broker-*.sqlite' -File | Sort-Object LastWriteTime -Descending)
    foreach ($obsolete in $backups | Select-Object -Skip ([Math]::Max(1, $KeepLocal))) {
        Remove-Item -LiteralPath $obsolete.FullName -Force
    }
}

Write-Output "Broker recovery backup verified in $($BackupRoots.Count) location(s)."
Write-Output "  Backup: $($recovery.backup_file)"
Write-Output "  SHA-256: $($recovery.backup_sha256)"
Write-Output "  Restore test: $($recovery.restore_test)"
Write-Output "  Health check: $($recovery.health_check)"
