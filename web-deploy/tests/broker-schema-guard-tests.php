<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/DatabaseMigrationService.php';
require_once __DIR__ . '/../player-assistant-broker/BrokerAlertService.php';
require_once __DIR__ . '/../player-assistant-broker/RpolClient.php';
require_once __DIR__ . '/../player-assistant-broker/BrokerService.php';

function schemaGuardAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$databasePath = tempnam(sys_get_temp_dir(), 'pa-broker-schema-');
$backupDirectory = sys_get_temp_dir() . '/pa-broker-schema-backups-' . bin2hex(random_bytes(5));
if ($databasePath === false) {
    throw new RuntimeException('Unable to create the schema guard fixture.');
}

try {
    $database = new PDO('sqlite:' . $databasePath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    $config = [
        'api' => ['database_path' => $databasePath],
        'rpol' => ['base_url' => 'https://rpol.net'],
    ];

    try {
        new BrokerService($config, new RpolClient($config['rpol']));
        throw new RuntimeException('An unmigrated broker database was accepted.');
    } catch (RuntimeException $exception) {
        schemaGuardAssert(
            str_contains($exception->getMessage(), 'run migrate-broker.php'),
            'The migration-required error was not actionable.');
    }

    $objectCount = (int)$database->query(
        "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('table', 'index', 'trigger')")->fetchColumn();
    schemaGuardAssert($objectCount === 0, 'Request startup created or altered SQLite schema.');

    (new DatabaseMigrationService($database, $backupDirectory))->migrate();
    new BrokerService($config, new RpolClient($config['rpol']));
    schemaGuardAssert(
        (int)$database->query('PRAGMA user_version')->fetchColumn() === DatabaseMigrationService::LATEST_VERSION,
        'A migrated broker database was not accepted at the expected version.');

    $database->exec('DROP INDEX ix_level_up_notification_receipts_account_time');
    try {
        new BrokerService($config, new RpolClient($config['rpol']));
        throw new RuntimeException('A database missing the receipt index was accepted.');
    } catch (RuntimeException $exception) {
        schemaGuardAssert(str_contains($exception->getMessage(), 'ix_level_up_notification_receipts_account_time'), 'Missing receipt index did not fail closed.');
    }
    $database->exec('CREATE INDEX ix_level_up_notification_receipts_account_time ON level_up_notification_receipts(account_id, notified_at)');
    $database->exec('DROP TABLE message_send_rate_limits');
    try {
        new BrokerService($config, new RpolClient($config['rpol']));
        throw new RuntimeException('A v7 database missing message throttling was accepted.');
    } catch (RuntimeException $exception) {
        schemaGuardAssert(
            str_contains($exception->getMessage(), 'message_send_rate_limits')
                && str_contains($exception->getMessage(), 'run migrate-broker.php'),
            'The incomplete v7 message-throttle schema did not fail closed with an actionable error.');
    }

    echo "Broker schema guard tests passed.\n";
} finally {
    @unlink($databasePath);
    if (is_dir($backupDirectory)) {
        foreach (glob($backupDirectory . '/*') ?: [] as $file) {
            @unlink($file);
        }
        @rmdir($backupDirectory);
    }
}
