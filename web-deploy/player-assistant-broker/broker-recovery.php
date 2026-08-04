<?php

declare(strict_types=1);

$options = getopt('', [
    'config::',
    'backup-dir::',
    'status-path::',
    'keep::',
    'health-url::',
    'alert-email::',
]);
$configPath = (string)($options['config'] ?? __DIR__ . '/config.php');
$config = require $configPath;
if (!is_array($config)) {
    throw new RuntimeException('Broker configuration must return an array.');
}
$recovery = is_array($config['database_recovery'] ?? null) ? $config['database_recovery'] : [];
$databasePath = (string)($config['api']['database_path'] ?? '');
$backupDirectory = (string)($options['backup-dir'] ?? $recovery['backup_directory'] ?? __DIR__ . '/backups');
$statusPath = (string)($options['status-path'] ?? $recovery['status_path'] ?? __DIR__ . '/broker-recovery-status.json');
$keep = max(1, (int)($options['keep'] ?? $recovery['retention_count'] ?? 14));
$healthUrl = (string)($options['health-url'] ?? $recovery['health_url'] ?? 'https://bryanmiller.us/scarlethorizons/api/v1/health');
$alertEmail = trim((string)($options['alert-email'] ?? $recovery['alert_email'] ?? ''));
$startedAt = microtime(true);
$status = [
    'schema_version' => 1,
    'checked_at' => gmdate(DATE_ATOM),
    'status' => 'failed',
    'integrity_check' => 'not_run',
    'restore_test' => 'not_run',
    'health_check' => 'not_run',
    'backup_file' => null,
    'backup_sha256' => null,
    'retained_backups' => 0,
    'error_code' => null,
];

function writeRecoveryStatus(string $path, array $status): void
{
    $directory = dirname($path);
    if (!is_dir($directory) && !mkdir($directory, 0700, true) && !is_dir($directory)) {
        return;
    }
    $temporary = $path . '.tmp-' . bin2hex(random_bytes(6));
    if (file_put_contents($temporary, json_encode($status, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR), LOCK_EX) !== false) {
        chmod($temporary, 0600);
        rename($temporary, $path);
    }
}

function sendRecoveryAlert(string $email, array $status): void
{
    if ($email === '' || !function_exists('mail')) {
        return;
    }
    @mail($email, 'Player Assistant broker recovery failure', 'Broker recovery status: ' . json_encode($status, JSON_UNESCAPED_SLASHES));
}

try {
    if ($databasePath === '' || !is_file($databasePath)) {
        throw new RuntimeException('Broker database is unavailable.');
    }
    if (!is_dir($backupDirectory) && !mkdir($backupDirectory, 0700, true) && !is_dir($backupDirectory)) {
        throw new RuntimeException('Unable to create the broker backup directory.');
    }
    chmod($backupDirectory, 0700);

    $database = new PDO('sqlite:' . $databasePath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    $database->exec('PRAGMA busy_timeout = 5000');
    $integrity = (string)$database->query('PRAGMA integrity_check')->fetchColumn();
    $status['integrity_check'] = $integrity;
    if ($integrity !== 'ok') {
        throw new RuntimeException('Broker database integrity check failed.');
    }

    $stamp = gmdate('Ymd\THis\Z');
    $backupPath = $backupDirectory . '/broker-' . $stamp . '-' . bin2hex(random_bytes(4)) . '.sqlite';
    $temporaryBackup = $backupPath . '.tmp';
    $database->exec('VACUUM INTO ' . $database->quote($temporaryBackup));
    if (!is_file($temporaryBackup) || filesize($temporaryBackup) === 0 || !rename($temporaryBackup, $backupPath)) {
        throw new RuntimeException('Broker database backup promotion failed.');
    }
    chmod($backupPath, 0600);

    $restore = new PDO('sqlite:' . $backupPath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    $restoreIntegrity = (string)$restore->query('PRAGMA integrity_check')->fetchColumn();
    $status['restore_test'] = $restoreIntegrity;
    if ($restoreIntegrity !== 'ok') {
        throw new RuntimeException('Broker backup restore test failed.');
    }
    $status['backup_file'] = basename($backupPath);
    $status['backup_sha256'] = hash_file('sha256', $backupPath);

    $status['health_check'] = 'not_configured';
    if ($healthUrl !== '' && function_exists('curl_init')) {
        $curl = curl_init($healthUrl);
        curl_setopt_array($curl, [CURLOPT_RETURNTRANSFER => true, CURLOPT_TIMEOUT => 20, CURLOPT_CONNECTTIMEOUT => 10, CURLOPT_FAILONERROR => false]);
        $body = curl_exec($curl);
        $httpCode = (int)curl_getinfo($curl, CURLINFO_RESPONSE_CODE);
        curl_close($curl);
        $health = is_string($body) ? json_decode($body, true) : null;
        $refreshHealthy = !is_array($health['word_count_refresh'] ?? null)
            || ($health['word_count_refresh']['healthy'] ?? true) !== false;
        $status['health_check'] = ($httpCode === 200
            && is_array($health)
            && ($health['status'] ?? null) === 'ok'
            && $refreshHealthy) ? 'ok' : 'failed';
        if ($status['health_check'] !== 'ok') {
            throw new RuntimeException('Broker health endpoint reported a failure.');
        }
    }

    $backups = glob($backupDirectory . '/broker-*.sqlite') ?: [];
    usort($backups, static fn(string $a, string $b): int => filemtime($b) <=> filemtime($a));
    foreach (array_slice($backups, $keep) as $obsolete) {
        if (is_file($obsolete)) {
            unlink($obsolete);
        }
    }
    $status['retained_backups'] = count(array_filter(glob($backupDirectory . '/broker-*.sqlite') ?: [], 'is_file'));
    $status['status'] = 'ok';
} catch (Throwable $exception) {
    $status['error_code'] = preg_replace('/[^a-z0-9_]+/i', '_', strtolower($exception->getMessage())) ?: 'recovery_failed';
    sendRecoveryAlert($alertEmail, $status);
    writeRecoveryStatus($statusPath, $status);
    fwrite(STDERR, "Broker recovery failed.\n");
    exit(1);
}

$status['duration_ms'] = (int)round((microtime(true) - $startedAt) * 1000);
writeRecoveryStatus($statusPath, $status);
echo json_encode($status, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE) . PHP_EOL;
