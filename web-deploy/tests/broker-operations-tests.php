<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerOperations.php';

function operationsAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$root = sys_get_temp_dir() . '/pa-broker-operations-' . bin2hex(random_bytes(6));
$databasePath = $root . '/broker.sqlite';
$backupDirectory = $root . '/backups';
$restoreDirectory = $root . '/restore-tests';
$offsiteDirectory = $root . '/offsite';
$statusPath = $root . '/operations-status.json';

try {
    if (!mkdir($root, 0700, true) && !is_dir($root)) {
        throw new RuntimeException('Unable to create the broker operations fixture directory.');
    }
    $database = new PDO('sqlite:' . $databasePath, null, null, [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
    ]);
    $database->exec('CREATE TABLE fixture (id INTEGER PRIMARY KEY, value TEXT NOT NULL)');
    $database->exec("INSERT INTO fixture (value) VALUES ('healthy')");
    $database = null;

    $operations = new BrokerOperations([
        'api' => ['database_path' => $databasePath],
        'operations' => [
            'backup_directory' => $backupDirectory,
            'restore_test_directory' => $restoreDirectory,
            'status_path' => $statusPath,
            'retention_count' => 2,
            'server_error_threshold' => 3,
            'server_error_window_seconds' => 900,
            'alert_cooldown_seconds' => 3600,
            'offsite' => ['local_directory' => $offsiteDirectory],
        ],
    ]);

    $maintenance = $operations->runMaintenance();
    operationsAssert($maintenance['status'] === 'ok', 'Broker maintenance did not succeed.');
    operationsAssert($maintenance['integrity_check'] === 'ok', 'The live integrity check did not pass.');
    operationsAssert($maintenance['restore_test'] === 'ok', 'The restore test did not pass.');
    operationsAssert($maintenance['backup']['offsite'] === false, 'The test offsite mode was misreported.');
    operationsAssert(count(glob($backupDirectory . '/broker-*.sqlite') ?: []) === 1, 'The local backup was not created.');
    operationsAssert(count(glob($offsiteDirectory . '/broker-*.sqlite') ?: []) === 1, 'The offsite backup was not created.');
    operationsAssert(count(glob($restoreDirectory . '/*.sqlite') ?: []) === 0, 'Restore test files were not cleaned up.');

    $operations->recordServerError('request-one');
    $operations->recordServerError('request-two');
    $operations->recordServerError('request-three');
    $health = $operations->healthStatus();
    operationsAssert($health['server_error_count'] === 3, 'Repeated server errors were not counted.');
    operationsAssert($health['healthy'] === false, 'Health remained healthy after repeated server errors.');

    $status = json_decode((string)file_get_contents($statusPath), true, 16, JSON_THROW_ON_ERROR);
    operationsAssert($status['last_maintenance_status'] === 'success', 'Maintenance status was not persisted.');
    operationsAssert($status['last_alert_event'] === 'repeated_server_errors', 'The repeated-error alert was not recorded.');
    echo "Broker operations tests passed.\n";
} finally {
    if (is_dir($root)) {
        $iterator = new RecursiveIteratorIterator(
            new RecursiveDirectoryIterator($root, FilesystemIterator::SKIP_DOTS),
            RecursiveIteratorIterator::CHILD_FIRST);
        foreach ($iterator as $path) {
            $path->isDir() ? rmdir($path->getPathname()) : unlink($path->getPathname());
        }
        rmdir($root);
    }
}
