<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/XpTrackingService.php';

function xpAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

function expectXpError(callable $action, int $status, string $errorName): void
{
    try {
        $action();
    } catch (BrokerHttpException $exception) {
        xpAssert($exception->status === $status, "Expected HTTP $status, received {$exception->status}.");
        xpAssert($exception->errorName === $errorName, "Expected $errorName, received {$exception->errorName}.");
        return;
    }
    throw new RuntimeException("Expected BrokerHttpException $errorName.");
}

function xpDatabase(string $path): PDO
{
    return new PDO('sqlite:' . $path, null, null, [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
        PDO::ATTR_EMULATE_PREPARES => false,
    ]);
}

function xpConfiguration(): array
{
    return [
        'source_url' => 'https://publish.obsidian.md/example/XP',
        'connect_timeout_seconds' => 1,
        'timeout_seconds' => 2,
        'maximum_response_bytes' => 65536,
        'cache_ttl_seconds' => 5,
        'maximum_stale_seconds' => 300,
    ];
}

$databasePath = tempnam(sys_get_temp_dir(), 'pa-xp-test-');
$invalidDatabasePath = tempnam(sys_get_temp_dir(), 'pa-xp-invalid-');
if ($databasePath === false || $invalidDatabasePath === false) {
    throw new RuntimeException('Unable to create the XP test databases.');
}

try {
    $fetchCount = 0;
    $markdown = implode("\n", [
        'As of 7.23.2026',
        '',
        '| Name | Class | XP Total |',
        '| --- | --- | ---: |',
        '| [[Jelb]] | Wizard | 12,345 |',
        '| Dorn | Fighter | 98,765 |',
        '| Max | Theurge | 6,100 |',
        '',
        'As of 7.20.2026',
        '| Name | XP Total |',
        '| --- | ---: |',
        '| Jelb | 1 |',
    ]);
    $service = new XpTrackingService(
        xpDatabase($databasePath),
        xpConfiguration(),
        static function (string $url) use (&$fetchCount, $markdown): string {
            $fetchCount++;
            return $markdown;
        });

    $player = $service->getForAccount([
        'role' => 'player',
        'character_key' => 'jelb',
    ]);
    xpAssert($player['scope'] === 'character', 'A player did not receive character-scoped XP.');
    xpAssert($player['character']['character_name'] === 'Jelb', 'The player received another character name.');
    xpAssert($player['character']['xp_total'] === 12345, 'The current player XP total was incorrect.');
    xpAssert($player['date_label'] === 'As of 7.23.2026', 'The latest XP date was not selected.');
    xpAssert(!isset($player['characters']), 'A player response exposed the party XP array.');

    $maximilian = $service->getForAccount([
        'role' => 'player',
        'character_key' => 'maximilian',
    ]);
    xpAssert($maximilian['scope'] === 'character', 'Maximilian did not receive character-scoped XP.');
    xpAssert($maximilian['character']['character_name'] === 'Max', 'Maximilian did not receive the Max XP row.');
    xpAssert($maximilian['character']['xp_total'] === 6100, 'Maximilian received the wrong current XP total.');
    xpAssert(!isset($maximilian['characters']), 'Maximilian received the party XP array.');

    $dm = $service->getForAccount([
        'role' => 'dm',
        'character_key' => 'dungeon-master',
    ]);
    xpAssert($dm['scope'] === 'party', 'The Dungeon Master did not receive party-scoped XP.');
    xpAssert(count($dm['characters']) === 3, 'The Dungeon Master did not receive every current XP row.');
    xpAssert($fetchCount === 1, 'The validated XP snapshot was not served from the server cache.');

    expectXpError(
        fn() => $service->getForAccount(['role' => 'player', 'character_key' => 'missing']),
        403,
        'xp_not_authorized');

    $database = xpDatabase($databasePath);
    $database->exec('UPDATE xp_tracking_cache SET fetched_at = fetched_at - 10');
    $staleService = new XpTrackingService(
        $database,
        xpConfiguration(),
        static fn(string $url): never => throw new RuntimeException('simulated upstream failure'));
    $stale = $staleService->getForAccount(['role' => 'player', 'character_key' => 'jelb']);
    xpAssert($stale['stale'] === true, 'A recent last-known-good XP snapshot was not preserved.');
    xpAssert($stale['character']['xp_total'] === 12345, 'The stale fallback XP total changed.');

    $invalidService = new XpTrackingService(
        xpDatabase($invalidDatabasePath),
        xpConfiguration(),
        static fn(string $url): string => "As of 7.23.2026\n\nnot a table");
    expectXpError(
        fn() => $invalidService->getForAccount(['role' => 'player', 'character_key' => 'jelb']),
        502,
        'xp_unavailable');

    $ambiguousPath = tempnam(sys_get_temp_dir(), 'pa-xp-ambiguous-');
    if ($ambiguousPath === false) {
        throw new RuntimeException('Unable to create the ambiguous XP test database.');
    }
    try {
        $ambiguousService = new XpTrackingService(
            xpDatabase($ambiguousPath),
            xpConfiguration(),
            static fn(string $url): string => implode("\n", [
                'As of 7.23.2026',
                '| Name | XP Total |',
                '| --- | ---: |',
                '| Jelb North | 1 |',
                '| Jelb South | 2 |',
            ]));
        expectXpError(
            fn() => $ambiguousService->getForAccount(['role' => 'player', 'character_key' => 'jelb']),
            403,
            'xp_not_authorized');
    } finally {
        @unlink($ambiguousPath);
    }

    try {
        new XpTrackingService(
            xpDatabase(':memory:'),
            array_replace(xpConfiguration(), ['source_url' => 'http://127.0.0.1/private']),
            static fn(string $url): string => $markdown);
        throw new RuntimeException('An unsafe XP source URL was accepted.');
    } catch (RuntimeException $exception) {
        xpAssert(
            str_contains($exception->getMessage(), 'fixed Obsidian Publish HTTPS page'),
            'The unsafe XP source failed for the wrong reason.');
    }

    fwrite(STDOUT, "XP tracking tests passed.\n");
} finally {
    @unlink($databasePath);
    @unlink($invalidDatabasePath);
}
