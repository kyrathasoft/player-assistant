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
. (Join-Path $PSScriptRoot 'backup-encryption.ps1')
$ssh = Join-Path $env:WINDIR 'System32\OpenSSH\ssh.exe'
$scp = Join-Path $env:WINDIR 'System32\OpenSSH\scp.exe'
$remote = "$DreamHostUser@$DreamHostHost"

foreach ($path in @($ssh, $scp, $SshKeyPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required deployment file is missing: $path"
    }
}
if ([string]::IsNullOrWhiteSpace($env:BACKUP_ENCRYPTION_KEY) -or $env:BACKUP_ENCRYPTION_KEY.Length -lt 32) {
    throw 'BACKUP_ENCRYPTION_KEY must be set to at least 32 characters.'
}

function Assert-ValidBrokerBackupName {
    param([Parameter(Mandatory = $true)][string]$Name)
    if ($Name -notmatch '^broker-\d{8}T\d{6}Z-[a-f0-9]{8}\.sqlite$' -or
        [IO.Path]::GetFileName($Name) -cne $Name) {
        throw 'Remote broker recovery returned an invalid backup filename.'
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

Assert-ValidBrokerBackupName -Name ([string]$recovery.backup_file)
$remoteBackup = "$RemoteDirectory/backups/$($recovery.backup_file)"
$remoteStatus = "$RemoteDirectory/broker-recovery-status.json"
$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ('pa-broker-backup-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingRoot | Out-Null
try {
    $stagedBackup = Join-Path $stagingRoot $recovery.backup_file
    $stagedStatus = Join-Path $stagingRoot 'broker-recovery-status.json'
    & $scp -q -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
        "$remote`:$remoteBackup" $stagedBackup
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to download the broker backup into private temporary staging.'
    }
    & $scp -q -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
        "$remote`:$remoteStatus" $stagedStatus
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to download broker recovery status into private temporary staging.'
    }

    $localHash = (Get-FileHash -LiteralPath $stagedBackup -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($localHash -ne ([string]$recovery.backup_sha256).ToLowerInvariant()) {
        throw 'Broker backup hash mismatch in private temporary staging.'
    }

    foreach ($root in $BackupRoots) {
        New-Item -ItemType Directory -Force -Path $root | Out-Null
        $encryptedName = $recovery.backup_file + '.enc'
        $encryptedPath = Join-Path $root $encryptedName
        Protect-BrokerBackup -SourcePath $stagedBackup -DestinationPath $encryptedPath -Secret $env:BACKUP_ENCRYPTION_KEY
        Copy-Item -LiteralPath $stagedStatus -Destination (Join-Path $root 'broker-recovery-status.json') -Force

        [ordered]@{
            schema_version = 1
            file = $encryptedName
            format = 'player-assistant-backup-v1'
            algorithm = 'AES-256-CBC+HMAC-SHA256'
            sha256 = (Get-FileHash -LiteralPath $encryptedPath -Algorithm SHA256).Hash.ToLowerInvariant()
            created_at = [string]$recovery.checked_at
        } | ConvertTo-Json | Set-Content -LiteralPath ($encryptedPath + '.json') -Encoding UTF8
        Set-BrokerBackupPrivateAcl -Path ($encryptedPath + '.json')
        Set-BrokerBackupPrivateAcl -Path (Join-Path $root 'broker-recovery-status.json')

        $backups = @(Get-ChildItem -LiteralPath $root -Filter 'broker-*.sqlite.enc' -File | Sort-Object LastWriteTime -Descending)
        foreach ($obsolete in $backups | Select-Object -Skip ([Math]::Max(1, $KeepLocal))) {
            Remove-Item -LiteralPath $obsolete.FullName -Force
            Remove-Item -LiteralPath ($obsolete.FullName + '.json') -Force -ErrorAction SilentlyContinue
        }
    }
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

Write-Output "Broker recovery backup verified in $($BackupRoots.Count) location(s)."
Write-Output "  Backup: $($recovery.backup_file).enc"
Write-Output "  SHA-256: $($recovery.backup_sha256)"
Write-Output "  Restore test: $($recovery.restore_test)"
Write-Output "  Health check: $($recovery.health_check)"
