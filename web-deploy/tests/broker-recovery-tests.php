<?php

declare(strict_types=1);

function recoveryAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$root = sys_get_temp_dir() . '/pa-broker-recovery-' . bin2hex(random_bytes(6));
$databasePath = $root . '/broker.sqlite';
$configPath = $root . '/config.php';
$backupDirectory = $root . '/backups';
$statusPath = $root . '/status.json';
if (!mkdir($root, 0700, true)) {
    throw new RuntimeException('Unable to create recovery fixture.');
}

try {
    $database = new PDO('sqlite:' . $databasePath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    $database->exec('CREATE TABLE fixture (id INTEGER PRIMARY KEY, value TEXT)');
    $database->exec("INSERT INTO fixture (value) VALUES ('recovery fixture')");
    file_put_contents($configPath, "<?php return " . var_export([
        'api' => ['database_path' => $databasePath],
        'database_recovery' => [
            'backup_directory' => $backupDirectory,
            'status_path' => $statusPath,
            'retention_count' => 2,
            'health_url' => '',
        ],
    ], true) . ";\n");

    $script = __DIR__ . '/../player-assistant-broker/broker-recovery.php';
    $command = escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($script)
        . ' --config=' . escapeshellarg($configPath)
        . ' --keep=2';
    exec($command . ' 2>&1', $output, $exitCode);
    recoveryAssert($exitCode === 0, 'The broker recovery command failed: ' . implode("\n", $output));
    $status = json_decode((string)file_get_contents($statusPath), true, 32, JSON_THROW_ON_ERROR);
    recoveryAssert($status['status'] === 'ok', 'Recovery status was not ok.');
    recoveryAssert($status['integrity_check'] === 'ok', 'Live integrity check did not pass.');
    recoveryAssert($status['restore_test'] === 'ok', 'Restore integrity check did not pass.');
    $backups = glob($backupDirectory . '/broker-*.sqlite') ?: [];
    recoveryAssert(count($backups) === 1, 'The first recovery run did not create exactly one backup.');

    sleep(1);
    exec($command . ' 2>&1', $output, $exitCode);
    recoveryAssert($exitCode === 0, 'The second broker recovery command failed.');
    $backups = glob($backupDirectory . '/broker-*.sqlite') ?: [];
    recoveryAssert(count($backups) === 2, 'Retention did not preserve two backups.');

    $database->exec("INSERT INTO fixture (value) VALUES ('retention fixture')");
    exec($command . ' 2>&1', $output, $exitCode);
    recoveryAssert($exitCode === 0, 'The third broker recovery command failed.');
    $backups = glob($backupDirectory . '/broker-*.sqlite') ?: [];
    recoveryAssert(count($backups) === 2, 'Retention exceeded the configured limit.');
    echo "Broker recovery tests passed.\n";
} finally {
    $files = glob($root . '/*') ?: [];
    foreach ($files as $file) {
        if (is_dir($file)) {
            foreach (glob($file . '/*') ?: [] as $nested) {
                @unlink($nested);
            }
            @rmdir($file);
        } else {
            @unlink($file);
        }
    }
    @rmdir($root);
}
