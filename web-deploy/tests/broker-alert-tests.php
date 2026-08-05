<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/DatabaseMigrationService.php';
require_once __DIR__ . '/../player-assistant-broker/BrokerAlertService.php';

function alertAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$root = sys_get_temp_dir() . '/pa-alerts-' . bin2hex(random_bytes(6));
$databasePath = $root . '/broker.sqlite';
if (!mkdir($root, 0700, true)) {
    throw new RuntimeException('Unable to create alert fixture.');
}

try {
    $database = new PDO('sqlite:' . $databasePath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    $migration = new DatabaseMigrationService($database, $root . '/migration-backups');
    $migration->migrate();
    $alerts = new BrokerAlertService($database, [
        'alert_email' => '',
        'server_error_threshold' => 3,
        'server_error_window_seconds' => 60,
        'alert_cooldown_seconds' => 3600,
    ]);

    alertAssert($alerts->recordServerError('internal_error', 'one')['alert_triggered'] === false, 'The first server error alerted too early.');
    alertAssert($alerts->recordServerError('internal_error', 'two')['alert_triggered'] === false, 'The second server error alerted too early.');
    alertAssert($alerts->recordServerError('internal_error', 'three')['alert_triggered'] === true, 'The repeated server-error threshold did not alert.');
    alertAssert($alerts->recordServerError('internal_error', 'four')['alert_triggered'] === false, 'The server-error cooldown was ignored.');
    alertAssert($alerts->recordRefreshFailure('source_timeout', 'refresh failed')['alert_triggered'] === true, 'The refresh failure did not alert.');
    alertAssert($alerts->recordHealthFailure('health_degraded', 'health failed')['alert_triggered'] === true, 'The health failure did not alert.');
    alertAssert((int)$database->query('SELECT COUNT(*) FROM broker_alert_events')->fetchColumn() === 6, 'Alert events were not persisted.');
    echo "Broker alert tests passed.\n";
} finally {
    foreach (glob($root . '/migration-backups/*') ?: [] as $file) {
        @unlink($file);
    }
    @rmdir($root . '/migration-backups');
    @unlink($databasePath);
    @rmdir($root);
}
