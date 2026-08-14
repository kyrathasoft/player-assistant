<?php

declare(strict_types=1);

require_once __DIR__ . '/DatabaseMigrationService.php';

$configPath = getenv('PLAYER_ASSISTANT_BROKER_CONFIG');
$configPath = is_string($configPath) && $configPath !== '' ? $configPath : __DIR__ . '/config.php';
if (!is_file($configPath)) {
    throw new RuntimeException('The private broker configuration is unavailable.');
}
$config = require $configPath;
if (!is_array($config)) {
    throw new RuntimeException('The private broker configuration must return an array.');
}
$api = is_array($config['api'] ?? null) ? $config['api'] : [];
$databasePath = (string)($api['database_path'] ?? '');
if ($databasePath === '') {
    throw new RuntimeException('The broker database path is not configured.');
}
$migrations = is_array($config['migrations'] ?? null) ? $config['migrations'] : [];
$backupDirectory = (string)($migrations['backup_directory'] ?? dirname($databasePath) . '/migration-backups');
$database = new PDO('sqlite:' . $databasePath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
$result = (new DatabaseMigrationService($database, $backupDirectory))->migrate();
$version = (int)$database->query('PRAGMA user_version')->fetchColumn();
if ($version !== DatabaseMigrationService::LATEST_VERSION) {
    throw new RuntimeException('The broker migration did not reach the latest schema version.');
}
echo json_encode([
    'service' => 'player-assistant-broker',
    'from_version' => $result['from_version'],
    'to_version' => $version,
    'applied_versions' => $result['applied_versions'],
], JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR) . PHP_EOL;
