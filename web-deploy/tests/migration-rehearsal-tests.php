<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/DatabaseMigrationService.php';
require_once __DIR__ . '/../player-assistant-broker/BrokerSchemaContract.php';

function rehearsalAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

function rehearsalPdo(string $path): PDO
{
    return new PDO('sqlite:' . $path, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
}

function rehearsalSnapshot(PDO $database): string
{
    $objects = $database->query("SELECT type, name, sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type, name")->fetchAll(PDO::FETCH_ASSOC);
    $rows = [];
    foreach ($objects as $object) {
        if ($object['type'] !== 'table') {
            continue;
        }
        $name = str_replace('"', '""', (string)$object['name']);
        $rows[$object['name']] = $database->query('SELECT * FROM "' . $name . '" ORDER BY rowid')->fetchAll(PDO::FETCH_ASSOC);
    }
    return hash('sha256', json_encode([
        'version' => (int)$database->query('PRAGMA user_version')->fetchColumn(),
        'objects' => $objects,
        'rows' => $rows,
    ], JSON_THROW_ON_ERROR));
}

function buildVersionFixtures(string $root): array
{
    $sourcePath = $root . '/source.sqlite';
    $backupPath = $root . '/source-backups';
    mkdir($backupPath, 0700, true);
    $source = rehearsalPdo($sourcePath);
    (new DatabaseMigrationService($source, $backupPath))->migrate();
    $source = null;
    $fixtures = [];
    for ($version = 0; $version < DatabaseMigrationService::LATEST_VERSION; $version++) {
        $matches = glob($backupPath . '/broker-migration-v' . $version . '-to-v' . ($version + 1) . '-*.sqlite') ?: [];
        rehearsalAssert(count($matches) === 1, "Missing deterministic source fixture for v$version.");
        $path = $root . '/fixture-v' . $version . '.sqlite';
        copy($matches[0], $path);
        $fixtures[$version] = $path;
    }
    return $fixtures;
}

function seedRepresentativeData(string $path, int $version): void
{
    if ($version === 0) {
        return;
    }
    $database = rehearsalPdo($path);
    $database->exec("INSERT INTO character_accounts
        (id, normalized_name, display_name, character_key, role, enabled, password_hash,
         created_at, password_changed_at)
        VALUES ('fixture-account', 'fixture hero', 'Fixture Hero', 'fixture-key', 'player', 1,
                'fixture-hash', 1700000000, 1700000000)");
    $database = null;
}

$root = sys_get_temp_dir() . '/pa-migration-rehearsal-' . bin2hex(random_bytes(6));
mkdir($root, 0700, true);
$fixtures = [];
try {
    $fixtures = buildVersionFixtures($root);
    rehearsalAssert(DatabaseMigrationService::supportedPriorVersions() === range(0, 7), 'Supported migration inventory is incomplete.');

    foreach ($fixtures as $version => $path) {
        seedRepresentativeData($path, $version);
        $before = rehearsalSnapshot(rehearsalPdo($path));
        $beforeBytes = hash_file('sha256', $path);
        $backupDir = $root . '/fault-backups-v' . $version;
        mkdir($backupDir, 0700, true);
        foreach (['backup-promotion', 'migration-apply', 'migration-commit'] as $point) {
            $faultPath = $root . '/fault-v' . $version . '-' . $point . '.sqlite';
            copy($path, $faultPath);
            $faultBackups = $backupDir . '-' . $point;
            $seen = false;
            try {
                (new DatabaseMigrationService(
                    rehearsalPdo($faultPath),
                    $faultBackups,
                    static function (string $actualPoint, int $target) use (&$seen, $point, $version): void {
                        if ($actualPoint === $point && $target === $version + 1) {
                            $seen = true;
                            throw new RuntimeException('deterministic rehearsal fault');
                        }
                    }
                ))->migrate();
                throw new RuntimeException("Fault '$point' did not fail at v$version.");
            } catch (RuntimeException $exception) {
                rehearsalAssert($seen, "Fault '$point' was not reached at v$version.");
            }
            $failed = rehearsalPdo($faultPath);
            rehearsalAssert((int)$failed->query('PRAGMA user_version')->fetchColumn() === $version, "Partial version committed at v$version/$point.");
            rehearsalAssert(rehearsalSnapshot($failed) === $before, "Pre-existing state changed at v$version/$point.");
            rehearsalAssert(hash_file('sha256', $faultPath) === $beforeBytes, "Pre-existing bytes changed at v$version/$point.");
            $failed = null;
            foreach (glob($faultBackups . '/*.tmp') ?: [] as $temporary) {
                @unlink($temporary);
            }
            @unlink($faultPath);
        }

        $successPath = $root . '/success-v' . $version . '.sqlite';
        copy($path, $successPath);
        $successBackups = $root . '/success-backups-v' . $version;
        $result = (new DatabaseMigrationService(rehearsalPdo($successPath), $successBackups))->migrate();
        $database = rehearsalPdo($successPath);
        rehearsalAssert($result['from_version'] === $version && $result['to_version'] === 8, "Upgrade path v{$version}->v8 is incomplete.");
        rehearsalAssert((int)$database->query('PRAGMA user_version')->fetchColumn() === 8, "Upgrade path v{$version} did not reach v8.");
        rehearsalAssert((string)$database->query('PRAGMA integrity_check')->fetchColumn() === 'ok', "Upgrade path v$version failed integrity check.");
        rehearsalAssert(BrokerSchemaContract::diagnostics(BrokerSchemaContract::load(), BrokerSchemaContract::inspect($database)) === [], "Upgrade path v$version drifted from the schema contract.");
        rehearsalAssert(count(glob($successBackups . '/broker-migration-*.sqlite') ?: []) === 8 - $version, "Upgrade path v$version lacks boundary backups.");
    }

    $corrupt = $root . '/corrupt.sqlite';
    copy($fixtures[3], $corrupt);
    $corruptBackups = $root . '/corrupt-backups';
    mkdir($corruptBackups, 0700, true);
    $corruptBackup = $corruptBackups . '/broker-migration-v3-to-v4-corrupt.sqlite';
    file_put_contents($corruptBackup, 'not sqlite');
    try {
        DatabaseMigrationService::validatePreMigrationBackup($corruptBackup, 3);
        throw new RuntimeException('Corrupted backup was accepted.');
    } catch (RuntimeException $exception) {
        rehearsalAssert(str_contains($exception->getMessage(), 'corrupted') || str_contains($exception->getMessage(), 'incompatible'), 'Corrupted backup diagnostic was not fail-closed.');
    }

    $interruptedBackups = $root . '/interrupted-backups';
    mkdir($interruptedBackups, 0700, true);
    file_put_contents($interruptedBackups . '/broker-migration-v3-to-v4.sqlite.tmp', 'partial');
    try {
        (new DatabaseMigrationService(rehearsalPdo($fixtures[3]), $interruptedBackups))->migrate();
        throw new RuntimeException('Interrupted recovery state was accepted.');
    } catch (RuntimeException $exception) {
        rehearsalAssert(str_contains($exception->getMessage(), 'interrupted'), 'Interrupted recovery diagnostic was not fail-closed.');
    }

    $unsupported = rehearsalPdo($root . '/unsupported.sqlite');
    $unsupported->exec('PRAGMA user_version = 99');
    try {
        (new DatabaseMigrationService($unsupported, $root . '/unsupported-backups'))->migrate();
        throw new RuntimeException('Unsupported schema version was accepted.');
    } catch (RuntimeException $exception) {
        rehearsalAssert(str_contains($exception->getMessage(), 'unsupported'), 'Unsupported-version diagnostic was not fail-closed.');
    }

    echo "Migration rehearsal tests passed.\n";
} finally {
    $iterator = new RecursiveIteratorIterator(
        new RecursiveDirectoryIterator($root, FilesystemIterator::SKIP_DOTS),
        RecursiveIteratorIterator::CHILD_FIRST
    );
    foreach ($iterator as $file) {
        $file->isDir() ? @rmdir($file->getPathname()) : @unlink($file->getPathname());
    }
    @rmdir($root);
}
