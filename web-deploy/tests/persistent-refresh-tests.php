<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/WordCountService.php';

function refreshAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

function refreshDatabase(): PDO
{
    $database = new PDO('sqlite::memory:', null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    $database->exec('CREATE TABLE word_count_snapshots (
        id INTEGER PRIMARY KEY,
        schema_version INTEGER NOT NULL,
        observed_at TEXT NOT NULL,
        counting_rule_version TEXT NOT NULL,
        wiki_pages INTEGER NOT NULL,
        wiki_words INTEGER NOT NULL,
        ic_files INTEGER NOT NULL,
        ic_words INTEGER NOT NULL,
        ooc_files INTEGER NOT NULL,
        ooc_words INTEGER NOT NULL,
        uploaded_at INTEGER NOT NULL
    )');
    return $database;
}

function refreshSnapshot(string $observedAt, int $words): array
{
    return [
        'schema_version' => 1,
        'observed_at' => $observedAt,
        'counting_rule_version' => 'test-v1',
        'wiki' => ['pages' => 1, 'words' => $words],
        'ic' => ['files' => 1, 'words' => $words],
        'ooc' => ['files' => 1, 'words' => $words],
    ];
}

$database = refreshDatabase();
$service = new WordCountService(
    $database,
    ['source_url' => 'https://example.test/word-counts'],
    static fn(string $url): string => json_encode(
        refreshSnapshot('2026-08-01T00:00:00Z', 10),
        JSON_THROW_ON_ERROR));

$latest = $service->store(refreshSnapshot('2026-08-02T00:00:00Z', 20));
refreshAssert($latest['wiki']['words'] === 20, 'The newer baseline was not stored.');
$refreshed = $service->refreshNow();
refreshAssert(
    $refreshed['observed_at'] === '2026-08-02T00:00:00Z'
        && $refreshed['wiki']['words'] === 20,
    'A stale-but-valid word-count source overwrote the newer snapshot.');

$equal = new WordCountService(
    $database,
    ['source_url' => 'https://example.test/word-counts'],
    static fn(string $url): string => json_encode(
        refreshSnapshot('2026-08-02T00:00:00Z', 5),
        JSON_THROW_ON_ERROR));
$equalResult = $equal->refreshNow();
refreshAssert(
    $equalResult['wiki']['words'] === 20,
    'An equal-date word-count source replaced the established generation.');

$newerReset = new WordCountService(
    $database,
    ['source_url' => 'https://example.test/word-counts'],
    static fn(string $url): string => json_encode(
        refreshSnapshot('2026-08-03T00:00:00Z', 3),
        JSON_THROW_ON_ERROR));
$resetResult = $newerReset->refreshNow();
refreshAssert(
    $resetResult['observed_at'] === '2026-08-03T00:00:00Z'
        && $resetResult['wiki']['words'] === 3,
    'A legitimate newer generation that decreased counts was rejected.');

echo "Persistent refresh tests passed.\n";
