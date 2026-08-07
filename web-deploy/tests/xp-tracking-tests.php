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
        'character_source_url' => 'https://publish.obsidian.md/example/PCs/Player+Characters+Listing',
        'class_progression_index_url' => 'https://publish.obsidian.md/example/Classes/Class+Level+Progression',
        'connect_timeout_seconds' => 1,
        'timeout_seconds' => 2,
        'maximum_response_bytes' => 65536,
        'cache_ttl_seconds' => 5,
        'maximum_stale_seconds' => 300,
    ];
}

$xpTrackingReflection = new ReflectionClass(XpTrackingService::class);
$serverProgressionEntryLimit = $xpTrackingReflection->getConstant(
    'MAXIMUM_AWARD_PROGRESSION_ENTRIES');
$pwaScript = file_get_contents(__DIR__ . '/../../pwa/app.js');
xpAssert(
    is_int($serverProgressionEntryLimit),
    'The server XP award progression limit contract is missing.');
xpAssert(is_string($pwaScript), 'The PWA script could not be read.');
xpAssert(
    preg_match(
        '/const MAXIMUM_XP_AWARD_PROGRESSION_ENTRIES = (?<limit>\d+);/',
        $pwaScript,
        $pwaLimitMatch) === 1,
    'The PWA XP award progression limit contract is missing.');
xpAssert(
    (int)$pwaLimitMatch['limit'] === $serverProgressionEntryLimit,
    'The server and PWA XP award progression limits differ.');
xpAssert(
    str_contains(
        $pwaScript,
        'payload.length > MAXIMUM_XP_AWARD_PROGRESSION_ENTRIES'),
    'The PWA does not enforce the shared XP award progression limit.');

$databasePath = tempnam(sys_get_temp_dir(), 'pa-xp-test-');
$invalidDatabasePath = tempnam(sys_get_temp_dir(), 'pa-xp-invalid-');
$awardsRoot = sys_get_temp_dir() . '/pa-xp-private-root-' . bin2hex(random_bytes(6));
$awardsDirectory = $awardsRoot . '/xp-awards';
if ($databasePath === false || $invalidDatabasePath === false) {
    throw new RuntimeException('Unable to create the XP test databases.');
}
if (!mkdir($awardsDirectory, 0700, true) && !is_dir($awardsDirectory)) {
    throw new RuntimeException('Unable to create the XP award limit fixture directory.');
}

try {
    $fetchCount = 0;
    $markdown = implode("\n", [
        'As of 7.20.2026',
        '',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Jelb | Illusionist | 3 | 1 |',
        '',
        'As of 7.23.2026',
        '',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| [[Jelb]] | Illusionist | 4 | 12,345 |',
        '| Dorn | Fighter | 5 | 98,765 |',
        '| Max | Theurge | **3** | 6,100 |',
    ]);
    $characterMarkdown = implode("\n", [
        '| Name | Class | Level | Token | HP |',
        '| --- | --- | ---: | --- | ---: |',
        '| [[Jelb Garrick, Illusionist\|Jelb]] | Illusionist | 4 | ![[jelb.webp]] | 13 |',
        '| [[Dorn]] | Fighter | 5 | ![[dorn.webp]] | 27 |',
        '| [[Maximilian Yragerne\|Maximilian]] | Theurge | 2 | ![[max.webp]] | 5 |',
    ]);
    $progressionIndexMarkdown = implode("\n", [
        '- [[Fighter]]',
        '- [[Illusionist]]',
        '- [[Mystic Theurge]]',
    ]);
    $fighterProgressionMarkdown = implode("\n", [
        '| 1 | 0 |',
        '| --- | --- |',
        '| 2 | 2,000 |',
        '| 3 | 4,000 |',
        '| 4 | 8,000 |',
        '| 5 | 16,000 |',
        '| 6 | 32,000 |',
    ]);
    $illusionistProgressionMarkdown = implode("\n", [
        '| 1 | 0 |',
        '| --- | --- |',
        '| 2 | 2,500 |',
        '| 3 | 5,000 |',
        '| 4 | 10,000 |',
        '| 5 | 20,000 |',
        '| 6 | 40,000 |',
    ]);
    $theurgeProgressionMarkdown = implode("\n", [
        'XP and Level Progression',
        'Level Spell Progression Level Spell Progression XP HD',
        '1 1 - - - - - 1 - - - - - 0 1d4',
        '2 1 - - - - - 2 1 - - - - 2,750 2d4',
        '3 2 - - - - - 3 1 - - - - 5,500 3d4',
        '4 2 - - - - - 4 2 - - - - 11,000 4d4',
        'Spellcasting: fixture text',
    ]);
    $progressionFixture = static function (string $url) use (
        $progressionIndexMarkdown,
        $fighterProgressionMarkdown,
        $illusionistProgressionMarkdown,
        $theurgeProgressionMarkdown): ?string {
        if (str_contains($url, 'Class+Level+Progression')) {
            return $progressionIndexMarkdown;
        }
        if (str_contains($url, '/Classes/Fighter')) {
            return $fighterProgressionMarkdown;
        }
        if (str_contains($url, '/Classes/Illusionist')) {
            return $illusionistProgressionMarkdown;
        }
        if (str_contains($url, '/Classes/Mystic%20Theurge')) {
            return $theurgeProgressionMarkdown;
        }
        return null;
    };
    $service = new XpTrackingService(
        xpDatabase($databasePath),
        xpConfiguration(),
        static function (string $url) use (
            &$fetchCount,
            $markdown,
            $characterMarkdown,
            $progressionFixture): string {
            $fetchCount++;
            if (str_contains($url, 'Player+Characters+Listing')) {
                return $characterMarkdown;
            }
            return $progressionFixture($url) ?? $markdown;
        });

    $awardEntry = [
        'character_name' => 'Limit Hero',
        'character_class' => 'Fighter',
        'level_before_award' => 1,
        'xp_award' => 1,
        'xp_award_date' => '8.4.2026',
        'level_after_award' => 1,
    ];
    $awardPath = $awardsDirectory . '/limit-xp.json';
    file_put_contents(
        $awardPath,
        json_encode(
            array_fill(0, $serverProgressionEntryLimit, $awardEntry),
            JSON_THROW_ON_ERROR));
    $awardService = new XpTrackingService(
        xpDatabase(':memory:'),
        array_replace(xpConfiguration(), [
            'awards_directory' => $awardsDirectory,
            'awards_root' => $awardsRoot,
            'award_groups' => ['limit' => ['limit-xp']],
        ]));
    $maximumAwards = $awardService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'limit',
    ]);
    xpAssert(
        count($maximumAwards['progressions'][0]['entries']) === $serverProgressionEntryLimit,
        'The server rejected the shared XP award progression entry limit.');
    file_put_contents(
        $awardPath,
        json_encode(
            array_fill(0, $serverProgressionEntryLimit + 1, $awardEntry),
            JSON_THROW_ON_ERROR));
    expectXpError(
        fn() => $awardService->getAwardsForAccount([
            'role' => 'player',
            'character_key' => 'limit',
        ]),
        503,
        'xp_awards_unavailable');

    $publicRootService = new XpTrackingService(
        xpDatabase(':memory:'),
        array_replace(xpConfiguration(), [
            'awards_directory' => $awardsDirectory,
            'awards_root' => $awardsDirectory,
            'award_groups' => ['limit' => ['limit-xp']],
        ]));
    expectXpError(
        fn() => $publicRootService->getAwardsForAccount([
            'role' => 'player',
            'character_key' => 'limit',
        ]),
        503,
        'xp_awards_unavailable');

    $player = $service->getForAccount([
        'role' => 'player',
        'character_key' => 'jelb',
    ]);
    xpAssert($player['scope'] === 'character', 'A player did not receive character-scoped XP.');
    xpAssert($player['character']['character_name'] === 'Jelb', 'The player received another character name.');
    xpAssert($player['character']['character_class'] === 'Illusionist', 'The player received the wrong class.');
    xpAssert($player['character']['level'] === 4, 'The player received the wrong class level.');
    xpAssert($player['character']['hit_points'] === 13, 'The player received the wrong hit-point total.');
    xpAssert($player['character']['xp_total'] === 12345, 'The current player XP total was incorrect.');
    xpAssert($player['character']['xp_to_next_level'] === 7655, 'The player received the wrong TNL value.');
    xpAssert($player['date_label'] === 'As of 7.23.2026', 'The latest XP date was not selected.');
    xpAssert(!isset($player['characters']), 'A player response exposed the party XP array.');

    $maximilian = $service->getForAccount([
        'role' => 'player',
        'character_key' => 'maximilian',
    ]);
    xpAssert($maximilian['scope'] === 'character', 'Maximilian did not receive character-scoped XP.');
    xpAssert($maximilian['character']['character_name'] === 'Max', 'Maximilian did not receive the Max XP row.');
    xpAssert($maximilian['character']['character_class'] === 'Theurge', 'Maximilian received the wrong class.');
    xpAssert($maximilian['character']['level'] === 3, 'Maximilian received the stale listing level.');
    xpAssert($maximilian['character']['hit_points'] === 5, 'Maximilian received the wrong hit-point total.');
    xpAssert($maximilian['character']['xp_total'] === 6100, 'Maximilian received the wrong current XP total.');
    xpAssert($maximilian['character']['xp_to_next_level'] === 4900, 'Maximilian received the wrong TNL value.');
    xpAssert(!isset($maximilian['characters']), 'Maximilian received the party XP array.');

    $dm = $service->getForAccount([
        'role' => 'dm',
        'character_key' => 'dungeon-master',
    ]);
    xpAssert($dm['scope'] === 'party', 'The Dungeon Master did not receive party-scoped XP.');
    xpAssert(count($dm['characters']) === 3, 'The Dungeon Master did not receive every current XP row.');
    xpAssert($fetchCount === 6, 'The validated XP, character, and class snapshots were not served from the server cache.');

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
    xpAssert($stale['character']['hit_points'] === 13, 'The stale fallback hit-point total changed.');
    xpAssert($stale['character']['xp_to_next_level'] === 7655, 'The stale fallback TNL value changed.');

    $invalidService = new XpTrackingService(
        xpDatabase($invalidDatabasePath),
        xpConfiguration(),
        static fn(string $url): string => str_contains($url, 'Player+Characters+Listing')
            ? $characterMarkdown
            : "As of 7.23.2026\n\nnot a table");
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
            static function (string $url) use ($progressionFixture): string {
                if (str_contains($url, 'Player+Characters+Listing')) {
                    return implode("\n", [
                        '| Name | HP |',
                        '| --- | ---: |',
                        '| Jelb | 13 |',
                    ]);
                }
                $progression = $progressionFixture($url);
                if ($progression !== null) {
                    return $progression;
                }
                return implode("\n", [
                    'As of 7.23.2026',
                    '| Name | Class | Level | XP Total |',
                    '| --- | --- | ---: | ---: |',
                    '| Jelb North | Illusionist | 4 | 1 |',
                    '| Jelb South | Illusionist | 4 | 2 |',
                ]);
            });
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

    try {
        new XpTrackingService(
            xpDatabase(':memory:'),
            array_replace(
                xpConfiguration(),
                ['character_source_url' => 'http://127.0.0.1/private']),
            static fn(string $url): string => $markdown);
        throw new RuntimeException('An unsafe character source URL was accepted.');
    } catch (RuntimeException $exception) {
        xpAssert(
            str_contains($exception->getMessage(), 'fixed Obsidian Publish HTTPS page'),
            'The unsafe character source failed for the wrong reason.');
    }

    fwrite(STDOUT, "XP tracking tests passed.\n");
} finally {
    @unlink($databasePath);
    @unlink($invalidDatabasePath);
    if (is_dir($awardsDirectory)) {
        foreach (glob($awardsDirectory . '/*') ?: [] as $awardFile) {
            @unlink($awardFile);
        }
        @rmdir($awardsDirectory);
    }
    @rmdir($awardsRoot);
}
