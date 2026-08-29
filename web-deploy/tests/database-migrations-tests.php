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
    migrationAssert((int)$database->query("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'character_account_aliases'")->fetchColumn() === 1, 'The account-alias migration did not run.');
    migrationAssert((int)$database->query("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'mutation_idempotency'")->fetchColumn() === 1, 'The mutation idempotency ledger migration did not run.');
    migrationAssert((int)$database->query("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_mutation_idempotency_expiry'")->fetchColumn() === 1, 'The mutation idempotency expiry index was not created.');
    migrationAssert((int)$database->query("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'level_up_notification_receipts'")->fetchColumn() === 1, 'The level-up notification receipt migration did not run.');
    migrationAssert((int)$database->query("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_level_up_notification_receipts_account_time'")->fetchColumn() === 1, 'The level-up notification receipt index was not created.');
    $receiptColumns = $database->query("PRAGMA table_info('level_up_notification_receipts')")->fetchAll(PDO::FETCH_ASSOC);
    $receiptColumnsByName = array_column($receiptColumns, null, 'name');
    migrationAssert(
        isset($receiptColumnsByName['notified_at'])
            && (int)$receiptColumnsByName['notified_at']['notnull'] === 0,
        'The receipt acknowledgement timestamp must remain nullable until browser display.');
    migrationAssert(
        (int)($receiptColumnsByName['account_id']['pk'] ?? 0) === 1
            && (int)($receiptColumnsByName['progression_key']['pk'] ?? 0) === 2
            && (int)($receiptColumnsByName['target_level']['pk'] ?? 0) === 3,
        'The receipt table does not enforce account/progression/level uniqueness.');
    $receiptForeignKeys = $database->query("PRAGMA foreign_key_list('level_up_notification_receipts')")->fetchAll(PDO::FETCH_ASSOC);
    migrationAssert(
        count($receiptForeignKeys) === 1
            && $receiptForeignKeys[0]['table'] === 'character_accounts'
            && $receiptForeignKeys[0]['from'] === 'account_id'
            && strtoupper((string)$receiptForeignKeys[0]['on_delete']) === 'CASCADE',
        'The receipt table is not bound to character-account lifecycle.');
    $backups = glob($backupDirectory . '/broker-migration-*.sqlite') ?: [];
    migrationAssert(count($backups) === 5, 'The upgrade did not create one pre-migration backup per upgrade step.');
    $backup = new PDO('sqlite:' . $backups[0], null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
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
