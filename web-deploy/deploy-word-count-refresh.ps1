[CmdletBinding()]
param(
    [string]$DreamHostTarget = 'player-assistant-dreamhost',
    [string]$SshKeyPath = (Join-Path $HOME '.ssh\dreamhost_player_assistant'),
    [string]$PrivateDirectory = '/home/dh_4gg2za/player-assistant-broker',
    [string]$PublicApiPath = '/home/dh_4gg2za/bryanmiller.us/scarlethorizons/api/index.php',
    [uri]$SourceUrl = 'https://bryanmiller.us/scarlethorizons/data/word-counts.json',
    [string]$SigningMetadataPath = (Join-Path $PSScriptRoot 'word-count-signing-public.json'),
    [int]$KeepBackups = 5,
    [string]$CronSchedule = '17 */6 * * *',
    [string]$MaintenanceCronSchedule = '47 3 * * *'
)

$ErrorActionPreference = 'Stop'

if ($DreamHostTarget -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$') {
    throw 'The DreamHost target must be a simple SSH host alias or hostname.'
}
if ($PrivateDirectory -cne '/home/dh_4gg2za/player-assistant-broker') {
    throw 'The private directory must be the approved Player Assistant broker root.'
}
if ($PublicApiPath -cne '/home/dh_4gg2za/bryanmiller.us/scarlethorizons/api/index.php') {
    throw 'The public API path must be the approved Player Assistant API entry point.'
}

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [ValidateRange(1, 5)][int]$Attempts = 3
    )
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $output = & $Action
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) {
            return $output
        }
        if ($attempt -lt $Attempts) {
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
    throw "Native command failed with exit code $exitCode after $Attempts attempts."
}

function Invoke-RemotePhp {
    param(
        [Parameter(Mandatory = $true)][string]$Code,
        [ValidateRange(1, 5)][int]$Attempts = 3
    )
    $scriptId = [Guid]::NewGuid().ToString('N')
    $localScript = Join-Path ([IO.Path]::GetTempPath()) "player-assistant-remote-$scriptId.php"
    $remoteScript = "$PrivateDirectory/.player-assistant-remote-$scriptId.php"
    [IO.File]::WriteAllText($localScript, "<?php`n" + $Code, [Text.UTF8Encoding]::new($false))
    try {
        Invoke-CheckedNative {
            & scp -q -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
                -- $localScript "${DreamHostTarget}:$remoteScript"
        } | Out-Null
        return Invoke-CheckedNative -Attempts $Attempts -Action {
            & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
                $DreamHostTarget "/usr/bin/php '$remoteScript'"
        }
    }
    finally {
        Remove-Item -LiteralPath $localScript -Force -ErrorAction SilentlyContinue
        try {
            & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
                $DreamHostTarget "rm -f -- '$remoteScript'" | Out-Null
        }
        catch {
            Write-Warning "The temporary remote PHP script could not be removed: $remoteScript"
        }
    }
}

if (-not (Test-Path -LiteralPath $SshKeyPath -PathType Leaf)) {
    throw "SSH key not found: $SshKeyPath"
}
$metadata = Get-Content -Raw -LiteralPath $SigningMetadataPath | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace([string]$metadata.key_id) -or [string]::IsNullOrWhiteSpace([string]$metadata.public_key)) {
    throw 'Signing metadata is incomplete.'
}

$deployId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$deployFiles = @('RevisionService.php', 'BrokerService.php', 'BrokerAlertService.php', 'BrokerOperations.php', 'DatabaseMigrationService.php', 'migrate-broker.php', 'CharacterAuthService.php', 'MessageService.php', 'XpTrackingService.php', 'QuestService.php', 'WordCountService.php', 'refresh-word-counts.php', 'broker-maintenance.php')
$remoteStage = "$PrivateDirectory/.word-count-deploy-$deployId"
$remoteArchive = "$PrivateDirectory/.word-count-deploy-$deployId.tar"
$remotePublicIndex = "$remoteStage/public-index.php"
$rollbackDirectory = "$PrivateDirectory/.word-count-rollback-$deployId"
$localArchive = Join-Path ([IO.Path]::GetTempPath()) "player-assistant-word-count-$deployId.tar"
$remoteTemps = @{}
foreach ($file in $deployFiles) {
    $remoteTemp = "$remoteStage/$file"
    $remoteTemps[$file] = $remoteTemp
}

try {
    $brokerDirectory = Join-Path $PSScriptRoot 'player-assistant-broker'
    Invoke-CheckedNative {
        & tar -cf $localArchive -C $brokerDirectory -- @deployFiles
    } | Out-Null
    Invoke-CheckedNative {
        & scp -q -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 -- `
            $localArchive "${DreamHostTarget}:$remoteArchive"
    } | Out-Null
    $extractCommand = "rm -rf -- '$remoteStage' && mkdir '$remoteStage' && tar -xf '$remoteArchive' -C '$remoteStage' && rm -f -- '$remoteArchive'"
    Invoke-CheckedNative {
        & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 `
            $DreamHostTarget $extractCommand
    } | Out-Null
    Invoke-CheckedNative {
        & scp -q -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 -- `
            (Join-Path $PSScriptRoot 'bryanmiller.us\scarlethorizons\api\index.php') `
            "${DreamHostTarget}:$remotePublicIndex"
    } | Out-Null
}
finally {
    Remove-Item -LiteralPath $localArchive -Force -ErrorAction SilentlyContinue
}

$installData = @{
    private_directory = $PrivateDirectory
    source_url = $SourceUrl.AbsoluteUri
    status_path = "$PrivateDirectory/word-count-refresh-status.json"
    key_id = [string]$metadata.key_id
    public_key = [string]$metadata.public_key
    keep_backups = $KeepBackups
    deploy_id = $deployId
    rollback_directory = $rollbackDirectory
    files = $remoteTemps
} | ConvertTo-Json -Compress
$installData64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($installData))

$installCode = @'
$data = json_decode(base64_decode('__INSTALL_DATA__'), true, 32, JSON_THROW_ON_ERROR);
$directory = $data['private_directory'];
$rollbackDirectory = $data['rollback_directory'];
if (is_file($rollbackDirectory . '/manifest.json')) {
    throw new RuntimeException('The deployment rollback snapshot is already initialized.');
}
if (!mkdir($rollbackDirectory, 0700, true) && !is_dir($rollbackDirectory)) {
    throw new RuntimeException('Unable to create the deployment rollback directory.');
}
chmod($rollbackDirectory, 0700);
$rollbackManifest = ['files' => []];
foreach ($data['files'] as $file => $temporaryPath) {
    $target = $directory . '/' . $file;
    $rollbackManifest['files'][$file] = is_file($target);
    if (is_file($target)) {
        if (!copy($target, $rollbackDirectory . '/' . $file)) {
            throw new RuntimeException('Unable to snapshot ' . $file . ' for rollback.');
        }
        chmod($rollbackDirectory . '/' . $file, 0600);
    }
}
$configPath = $directory . '/config.php';
$rollbackManifest['config_originally_existed'] = is_file($configPath);
if (is_file($configPath)) {
    if (!copy($configPath, $rollbackDirectory . '/config.php')) {
        throw new RuntimeException('Unable to snapshot private config for rollback.');
    }
    chmod($rollbackDirectory . '/config.php', 0600);
}
file_put_contents(
    $rollbackDirectory . '/manifest.json',
    json_encode($rollbackManifest, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR),
    LOCK_EX);
chmod($rollbackDirectory . '/manifest.json', 0600);
foreach ($data['files'] as $file => $temporaryPath) {
    $lintOutput = [];
    $lintExit = 0;
    exec('/usr/bin/php -l ' . escapeshellarg($temporaryPath) . ' 2>&1', $lintOutput, $lintExit);
    if ($lintExit !== 0) {
        throw new RuntimeException('PHP lint failed for ' . $file . ': ' . implode("\n", $lintOutput));
    }
}

$configOriginallyExisted = is_file($configPath);
$oldConfig = $configOriginallyExisted ? (string)file_get_contents($configPath) : '';
$configChanged = false;
$installedFiles = [];
try {
foreach ($data['files'] as $file => $temporaryPath) {
    $target = $directory . '/' . $file;
    if (is_file($target) && hash_file('sha256', $target) === hash_file('sha256', $temporaryPath)) {
        unlink($temporaryPath);
        chmod($target, 0600);
        continue;
    }
    $targetOriginallyExisted = is_file($target);
    $backup = $target . '.bak-deploy-' . $data['deploy_id'];
    if ($targetOriginallyExisted) {
        if (!copy($target, $backup)) {
            throw new RuntimeException('Unable to back up ' . $file);
        }
        chmod($backup, 0600);
    }
    chmod($temporaryPath, 0600);
    if (!rename($temporaryPath, $target)) {
        throw new RuntimeException('Unable to install ' . $file);
    }
    $installedFiles[] = [
        'target' => $target,
        'backup' => $backup,
        'originally_existed' => $targetOriginallyExisted,
    ];
}

$config = $configOriginallyExisted ? require $configPath : [];
if (!is_array($config)) {
    throw new RuntimeException('Private config did not return an array.');
}
$config['word_counts'] = array_merge(
    is_array($config['word_counts'] ?? null) ? $config['word_counts'] : [],
    [
        'source_url' => $data['source_url'],
        'max_stale_seconds' => 604800,
        'status_path' => $data['status_path'],
        'signature_key_id' => $data['key_id'],
        'signature_public_key' => $data['public_key'],
    ]
);
$config['operations'] = array_merge(
    is_array($config['operations'] ?? null) ? $config['operations'] : [],
    [
        'backup_directory' => $directory . '/broker-backups',
        'restore_test_directory' => $directory . '/broker-restore-tests',
        'status_path' => $directory . '/broker-operations-status.json',
        'retention_count' => 14,
        'server_error_threshold' => 5,
        'server_error_window_seconds' => 900,
        'alert_cooldown_seconds' => 3600,
    ]
);
$offsite = is_array($config['operations']['offsite'] ?? null)
    ? $config['operations']['offsite']
    : [];
if (strtolower((string)($offsite['transport'] ?? '')) === 'ftps') {
    $config['operations']['offsite'] = [
        'transport' => 'ftps',
        'port' => (int)($offsite['port'] ?? 21),
    ];
}
$newConfig = "<?php\nreturn " . var_export($config, true) . ";\n";
$oldConfig = is_file($configPath) ? file_get_contents($configPath) : '';
if ($oldConfig !== $newConfig) {
    $temporaryConfig = $configPath . '.tmp-' . $data['deploy_id'];
    file_put_contents($temporaryConfig, $newConfig, LOCK_EX);
    chmod($temporaryConfig, 0600);
    if (!rename($temporaryConfig, $configPath)) {
        throw new RuntimeException('Unable to install private config.');
    }
    $configChanged = true;
}
chmod($configPath, 0600);
$migrationConfig = is_array($config['api'] ?? null) ? $config['api'] : [];
$databasePath = (string)($migrationConfig['database_path'] ?? '');
if ($databasePath === '') {
    throw new RuntimeException('The broker database path is not configured.');
}
$databaseOriginallyExisted = is_file($databasePath);
$databaseBackupPath = $rollbackDirectory . '/broker.sqlite';
$schemaVersionBeforeMigration = 0;
if ($databaseOriginallyExisted) {
    $migrationDatabase = new PDO('sqlite:' . $databasePath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    $schemaVersionBeforeMigration = (int)$migrationDatabase->query('PRAGMA user_version')->fetchColumn();
    $quotedBackupPath = str_replace("'", "''", $databaseBackupPath);
    $migrationDatabase->exec("VACUUM INTO '" . $quotedBackupPath . "'");
    chmod($databaseBackupPath, 0600);
}
$rollbackManifest['database'] = [
    'path' => $databasePath,
    'originally_existed' => $databaseOriginallyExisted,
    'backup' => $databaseBackupPath,
    'schema_version' => $schemaVersionBeforeMigration,
];
file_put_contents($rollbackDirectory . '/manifest.json', json_encode($rollbackManifest, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR), LOCK_EX);
chmod($rollbackDirectory . '/manifest.json', 0600);
$migrationOutput = [];
$migrationExit = 0;
exec('/usr/bin/php ' . escapeshellarg($directory . '/migrate-broker.php') . ' 2>&1', $migrationOutput, $migrationExit);
if ($migrationExit !== 0) {
    throw new RuntimeException('Broker deployment migration failed: ' . implode("\n", $migrationOutput));
}

$configBackupPatterns = [
    'config.php.bak-deploy-*',
    'config.php.bak-word-count-refresh-*',
];
foreach ($configBackupPatterns as $pattern) {
    foreach (glob($directory . '/' . $pattern) ?: [] as $obsolete) {
        $resolved = realpath($obsolete);
        if ($resolved !== false && dirname($resolved) === realpath($directory) && is_file($resolved)) {
            unlink($resolved);
        }
    }
}
$patterns = [
    'BrokerService.php.bak-deploy-*',
    'BrokerAlertService.php.bak-deploy-*',
    'DatabaseMigrationService.php.bak-deploy-*',
    'migrate-broker.php.bak-deploy-*',
    'CharacterAuthService.php.bak-deploy-*',
    'MessageService.php.bak-deploy-*',
    'XpTrackingService.php.bak-deploy-*',
    'QuestService.php.bak-deploy-*',
    'RevisionService.php.bak-deploy-*',
    'WordCountService.php.bak-deploy-*',
    'WordCountService.php.bak-source-refresh-*',
    'refresh-word-counts.php.bak-deploy-*',
    'BrokerOperations.php.bak-deploy-*',
    'broker-maintenance.php.bak-deploy-*',
];
foreach ($patterns as $pattern) {
    $matches = glob($directory . '/' . $pattern) ?: [];
    usort($matches, static fn(string $left, string $right): int => filemtime($right) <=> filemtime($left));
    foreach (array_slice($matches, $data['keep_backups']) as $obsolete) {
        $resolved = realpath($obsolete);
        if ($resolved !== false && dirname($resolved) === realpath($directory) && is_file($resolved)) {
            unlink($resolved);
        }
    }
}
foreach (['.BrokerService.php.deploy-*', '.BrokerAlertService.php.deploy-*', '.BrokerOperations.php.deploy-*', '.DatabaseMigrationService.php.deploy-*', '.migrate-broker.php.deploy-*', '.CharacterAuthService.php.deploy-*', '.MessageService.php.deploy-*', '.XpTrackingService.php.deploy-*', '.QuestService.php.deploy-*', '.RevisionService.php.deploy-*', '.WordCountService.php.deploy-*', '.refresh-word-counts.php.deploy-*', '.broker-maintenance.php.deploy-*'] as $pattern) {
    foreach (glob($directory . '/' . $pattern) ?: [] as $abandonedTemporaryFile) {
        if (is_file($abandonedTemporaryFile)) {
            unlink($abandonedTemporaryFile);
        }
    }
}
@rmdir($directory . '/.word-count-deploy-' . $data['deploy_id']);
} catch (Throwable $error) {
    if ($configChanged) {
        if ($configOriginallyExisted) {
            $rollbackConfig = $configPath . '.rollback-' . $data['deploy_id'];
            file_put_contents($rollbackConfig, $oldConfig, LOCK_EX);
            chmod($rollbackConfig, 0600);
            rename($rollbackConfig, $configPath);
            chmod($configPath, 0600);
        } else {
            @unlink($configPath);
        }
    }
    foreach (array_reverse($installedFiles) as $installedFile) {
        if ($installedFile['originally_existed'] && is_file($installedFile['backup'])) {
            copy($installedFile['backup'], $installedFile['target']);
            chmod($installedFile['target'], 0600);
        } else {
            @unlink($installedFile['target']);
        }
    }
    throw $error;
}
'@.Replace('__INSTALL_DATA__', $installData64)

$transactionData = @{
    private_directory = $PrivateDirectory
    public_api_path = $PublicApiPath
    public_index_temporary = $remotePublicIndex
    rollback_directory = $rollbackDirectory
    files = $deployFiles
} | ConvertTo-Json -Compress
$transactionData64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($transactionData))
$publicInstallCode = @'
$data = json_decode(base64_decode('__TRANSACTION_DATA__'), true, 16, JSON_THROW_ON_ERROR);
$temporary = $data['public_index_temporary'];
$target = $data['public_api_path'];
$rollbackDirectory = $data['rollback_directory'];
$lintOutput = [];
$lintExit = 0;
exec('/usr/bin/php -l ' . escapeshellarg($temporary) . ' 2>&1', $lintOutput, $lintExit);
if ($lintExit !== 0) {
    throw new RuntimeException('Public API PHP lint failed: ' . implode("\n", $lintOutput));
}
$state = ['originally_existed' => is_file($target)];
if ($state['originally_existed']) {
    if (!copy($target, $rollbackDirectory . '/public-index.php')) {
        throw new RuntimeException('Unable to snapshot the public API entry point.');
    }
    chmod($rollbackDirectory . '/public-index.php', 0600);
}
file_put_contents(
    $rollbackDirectory . '/public-index-state.json',
    json_encode($state, JSON_THROW_ON_ERROR),
    LOCK_EX);
chmod($rollbackDirectory . '/public-index-state.json', 0600);
chmod($temporary, 0644);
if (!rename($temporary, $target)) {
    throw new RuntimeException('Unable to install the public API entry point.');
}
chmod($target, 0644);
'@.Replace('__TRANSACTION_DATA__', $transactionData64)
$rollbackCode = @'
$data = json_decode(base64_decode('__TRANSACTION_DATA__'), true, 16, JSON_THROW_ON_ERROR);
$directory = $data['private_directory'];
$rollbackDirectory = $data['rollback_directory'];
$manifestPath = $rollbackDirectory . '/manifest.json';
if (!is_file($manifestPath)) {
    throw new RuntimeException('The deployment rollback manifest is missing.');
}
$manifest = json_decode((string)file_get_contents($manifestPath), true, 16, JSON_THROW_ON_ERROR);
$failures = [];
$restore = static function (string $snapshot, string $target, int $mode) use (&$failures): void {
    $temporary = $target . '.rollback-' . bin2hex(random_bytes(4));
    if (!copy($snapshot, $temporary)
        || !chmod($temporary, $mode)
        || !rename($temporary, $target)
        || !is_file($target)
        || hash_file('sha256', $snapshot) !== hash_file('sha256', $target)) {
        @unlink($temporary);
        $failures[] = basename($target);
    }
};
$remove = static function (string $target) use (&$failures): void {
    if ((file_exists($target) || is_link($target))
        && (!@unlink($target) || file_exists($target) || is_link($target))) {
        $failures[] = basename($target);
    }
};
foreach ($manifest['files'] as $file => $originallyExisted) {
    $target = $directory . '/' . $file;
    if ($originallyExisted) {
        $restore($rollbackDirectory . '/' . $file, $target, 0600);
    } else {
        $remove($target);
    }
}
$configPath = $directory . '/config.php';
if ($manifest['config_originally_existed']) {
    $restore($rollbackDirectory . '/config.php', $configPath, 0600);
} else {
    $remove($configPath);
}
if (isset($manifest['database']) && is_array($manifest['database'])) {
    $database = $manifest['database'];
    $databasePath = (string)$database['path'];
    if ($database['originally_existed']) {
        $restore((string)$database['backup'], $databasePath, 0600);
        @unlink($databasePath . '-wal');
        @unlink($databasePath . '-shm');
    } else {
        $remove($databasePath);
        $remove($databasePath . '-wal');
        $remove($databasePath . '-shm');
    }
}
$publicStatePath = $rollbackDirectory . '/public-index-state.json';
if (is_file($publicStatePath)) {
    $publicState = json_decode((string)file_get_contents($publicStatePath), true, 8, JSON_THROW_ON_ERROR);
    if ($publicState['originally_existed']) {
        $restore($rollbackDirectory . '/public-index.php', $data['public_api_path'], 0644);
    } else {
        $remove($data['public_api_path']);
    }
}
$cronSnapshot = $rollbackDirectory . '/crontab.txt';
if (is_file($cronSnapshot)) {
    $cronOutput = [];
    $cronExit = 0;
    exec('timeout 10 /usr/bin/crontab ' . escapeshellarg($cronSnapshot) . ' 2>&1', $cronOutput, $cronExit);
    if ($cronExit !== 0) {
        $failures[] = 'crontab';
    }
}
if ($failures !== []) {
    throw new RuntimeException('Rollback failed for: ' . implode(', ', $failures));
}
'@.Replace('__TRANSACTION_DATA__', $transactionData64)

$cronLines = @(
    "$CronSchedule /usr/bin/php $PrivateDirectory/refresh-word-counts.php >> $PrivateDirectory/word-count-refresh-cron.log 2>&1",
    "$MaintenanceCronSchedule /usr/bin/php $PrivateDirectory/broker-maintenance.php >> $PrivateDirectory/broker-maintenance-cron.log 2>&1"
)
$cronData = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($cronLines -join "`n")))
$cronCode = @'
$newLines = preg_split('/\R/', base64_decode('__CRON_LINE__'));
$rollbackDirectory = '__ROLLBACK_DIRECTORY__';
$output = [];
$listExit = 0;
exec('timeout 10 /usr/bin/crontab -l 2>/dev/null', $output, $listExit);
if ($listExit !== 0 && $listExit !== 1) {
    throw new RuntimeException('Unable to read existing cron within the timeout.');
}
$existing = implode("\n", $output);
file_put_contents($rollbackDirectory . '/crontab.txt', $existing . ($existing === '' ? '' : "\n"), LOCK_EX);
chmod($rollbackDirectory . '/crontab.txt', 0600);
$lines = preg_split('/\R/', trim($existing));
$lines = array_values(array_filter($lines, static function ($candidate): bool {
    return $candidate !== ''
        && !str_contains($candidate, '/player-assistant-broker/refresh-word-counts.php')
        && !str_contains($candidate, '/player-assistant-broker/broker-maintenance.php');
}));
$lines = array_merge($lines, array_values(array_filter($newLines, static fn($line): bool => is_string($line) && $line !== '')));
$temporary = tempnam(sys_get_temp_dir(), 'pa-cron-');
file_put_contents($temporary, implode("\n", $lines) . "\n");
$output = [];
$exit = 0;
exec('timeout 10 /usr/bin/crontab ' . escapeshellarg($temporary) . ' 2>&1', $output, $exit);
unlink($temporary);
if ($exit !== 0) {
    throw new RuntimeException('Unable to install cron within the timeout: ' . implode("\n", $output));
}
'@.Replace('__CRON_LINE__', $cronData).Replace('__ROLLBACK_DIRECTORY__', $rollbackDirectory)

$installationStarted = $false
$preserveRollback = $false
$deploymentFailed = $false
try {
    $installationStarted = $true
    Invoke-RemotePhp $installCode -Attempts 1 | Out-Null
    Invoke-RemotePhp $publicInstallCode -Attempts 1 | Out-Null
    Invoke-RemotePhp $cronCode -Attempts 1 | Out-Null

    $runnerCommand = "/usr/bin/php $PrivateDirectory/refresh-word-counts.php"
    Invoke-CheckedNative -Attempts 1 -Action {
        & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            $DreamHostTarget $runnerCommand
    } | Out-Null
    $maintenanceCommand = "/usr/bin/php $PrivateDirectory/broker-maintenance.php"
    Invoke-CheckedNative -Attempts 1 -Action {
        & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            $DreamHostTarget $maintenanceCommand
    } | Out-Null

    & (Join-Path $PSScriptRoot 'test-word-count-refresh-deployment.ps1') `
        -DreamHostTarget $DreamHostTarget `
        -SshKeyPath $SshKeyPath `
        -PrivateDirectory $PrivateDirectory `
        -PublicApiPath $PublicApiPath `
        -SourceUrl $SourceUrl `
        -SigningMetadataPath $SigningMetadataPath `
        -KeepBackups $KeepBackups
}
catch {
    $deploymentFailed = $true
    $deploymentError = $_
    if ($installationStarted) {
        try {
            Invoke-RemotePhp $rollbackCode | Out-Null
        }
        catch {
            $preserveRollback = $true
            throw "Deployment failed and automatic rollback also failed. Rollback data remains at ${rollbackDirectory}. Deployment error: $deploymentError Rollback error: $_"
        }
    }
    throw $deploymentError
}
finally {
    $cleanupTargets = @($remoteStage, $remoteArchive)
    if (-not $preserveRollback) {
        $cleanupTargets += $rollbackDirectory
    }
    $quotedCleanupTargets = ($cleanupTargets | ForEach-Object { "'" + $_.Replace("'", "'\\''") + "'" }) -join ' '
    try {
        & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            $DreamHostTarget "rm -rf -- $quotedCleanupTargets" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Remote cleanup exited with code $LASTEXITCODE."
        }
    }
    catch {
        if ($deploymentFailed) {
            Write-Warning 'Remote deployment staging cleanup failed while preserving the original deployment error.'
        } else {
            throw
        }
    }
}

Write-Output 'Word-count refresh deployment passed.'
