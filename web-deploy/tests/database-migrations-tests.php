<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/DatabaseMigrationService.php';

function migrationAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$root = sys_get_temp_dir() . '/pa-migrations-' . bin2hex(random_bytes(6));
$databasePath = $root . '/broker.sqlite';
$backupDirectory = $root . '/backups';
if (!mkdir($root, 0700, true)) {
    throw new RuntimeException('Unable to create migration fixture.');
}

try {
    $database = new PDO('sqlite:' . $databasePath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    $database->exec('CREATE TABLE legacy_fixture (value TEXT NOT NULL)');
    $database->exec(
        "CREATE TABLE character_accounts (
            id TEXT PRIMARY KEY,
            normalized_name TEXT NOT NULL UNIQUE,
            display_name TEXT NOT NULL,
            character_key TEXT NOT NULL,
            role TEXT NOT NULL,
            enabled INTEGER NOT NULL,
            password_hash TEXT NULL,
            legacy_algorithm TEXT NULL,
            legacy_iterations INTEGER NULL,
            legacy_salt TEXT NULL,
            legacy_hash TEXT NULL,
            created_at INTEGER NOT NULL,
            password_changed_at INTEGER NOT NULL,
            last_login_at INTEGER NULL
        )");
    $database->exec('PRAGMA user_version = 1');

    $migration = new DatabaseMigrationService($database, $backupDirectory);
    $result = $migration->migrate();
    migrationAssert($result['from_version'] === 1, 'The upgrade fixture did not start at version 1.');
    migrationAssert($result['to_version'] === DatabaseMigrationService::LATEST_VERSION, 'The migration did not reach the latest version.');
    migrationAssert((int)$database->query('PRAGMA user_version')->fetchColumn() === DatabaseMigrationService::LATEST_VERSION, 'PRAGMA user_version was not advanced.');
    migrationAssert((int)$database->query("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'broker_alert_events'")->fetchColumn() === 1, 'The alert-events migration did not run.');
    migrationAssert((int)$database->query("SELECT COUNT(*) FROM pragma_table_info('character_accounts') WHERE name = 'session_version'")->fetchColumn() === 1, 'The session-version upgrade did not run.');
    $messageIndexSql = (string)$database->query(
        "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'ix_message_notifications_recipient_read'")
        ->fetchColumn();
    migrationAssert(
        str_contains($messageIndexSql, 'sent_at DESC, id DESC'),
        'The message pagination index was not upgraded for stable cursor ordering.');
    $backups = glob($backupDirectory . '/broker-migration-*.sqlite') ?: [];
    migrationAssert(count($backups) === 3, 'The upgrade did not create one backup per migration step.');
    $versionOneBackups = glob($backupDirectory . '/broker-migration-v1-to-v2-*.sqlite') ?: [];
    migrationAssert(count($versionOneBackups) === 1, 'The version-one pre-migration backup is missing.');
    $backup = new PDO('sqlite:' . $versionOneBackups[0], null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    migrationAssert((int)$backup->query('PRAGMA user_version')->fetchColumn() === 1, 'The pre-migration backup was not from the old version.');

    echo "Database migration tests passed.\n";
} finally {
    foreach (glob($backupDirectory . '/*') ?: [] as $file) {
        @unlink($file);
    }
    @rmdir($backupDirectory);
    @unlink($databasePath);
    @rmdir($root);
}
