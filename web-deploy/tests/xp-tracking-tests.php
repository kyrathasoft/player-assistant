<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/DatabaseMigrationService.php';
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
    $database = new PDO('sqlite:' . $path, null, null, [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
        PDO::ATTR_EMULATE_PREPARES => false,
    ]);
    (new DatabaseMigrationService($database, sys_get_temp_dir() . '/pa-xp-migration-backups-' . bin2hex(random_bytes(4))))->migrate();
    return $database;
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
        'maximum_stale_seconds' => 300,
        'character_key_aliases' => [
            'jelb' => 'jelb',
            'jelb-garrick' => 'jelb',
            'max' => 'maximilian',
            'maximilian' => 'maximilian',
            'dorn' => 'dorn',
            'borca' => 'borca',
            'arilia' => 'arilia',
            'neria' => 'neria',
            'shade' => 'shade',
            'ari-stoneward' => 'ari-stoneward',
            'ari-valesong' => 'ari-valesong',
            'limit-hero' => 'limit',
            'dynamic-hero' => 'dynamic',
            'alpha-hero' => 'alpha',
            'beta-hero' => 'beta',
        ],
    ];
}

if (($argv[1] ?? '') === 'lock-reader') {
    $awardsRootArgument = (string)($argv[2] ?? '');
    $awardsDirectoryArgument = (string)($argv[3] ?? '');
    $lockReaderMarkdown = implode("\n", [
        'As of 7.31.2026',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Alpha Hero | Fighter | 1 | 100 |',
    ]);
    $lockReaderService = new XpTrackingService(
        xpDatabase(':memory:'),
        array_replace(xpConfiguration(), [
            'awards_root' => $awardsRootArgument,
            'awards_directory' => $awardsDirectoryArgument,
            'award_groups' => ['alpha-owner' => ['alpha-xp']],
        ]),
        static fn(string $url): string => $lockReaderMarkdown);
    $lockReaderService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'alpha-owner',
    ]);
    fwrite(STDOUT, "lock reader passed\n");
    exit(0);
}

if (($argv[1] ?? '') === 'crash-writer') {
    $awardsRootArgument = (string)($argv[2] ?? '');
    $awardsDirectoryArgument = (string)($argv[3] ?? '');
    $crashMarkdown = implode("\n", [
        'As of 8.01.2026',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Alpha Hero | Fighter | 1 | 150 |',
        '| Beta Hero | Fighter | 1 | 150 |',
        '',
        'As of 7.31.2026',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Alpha Hero | Fighter | 1 | 100 |',
        '| Beta Hero | Fighter | 1 | 100 |',
    ]);
    $crashPromotionCount = 0;
    $crashService = new XpTrackingService(
        xpDatabase(':memory:'),
        array_replace(xpConfiguration(), [
            'awards_root' => $awardsRootArgument,
            'awards_directory' => $awardsDirectoryArgument,
            'award_groups' => [
                'dynamic-hero' => ['dynamic-xp'],
                'atomic-owner' => ['alpha-xp', 'beta-xp'],
            ],
        ]),
        static fn(string $url): string => $crashMarkdown,
        static function (string $temporaryPath, string $targetPath) use (
            &$crashPromotionCount): bool {
            $crashPromotionCount++;
            if ($crashPromotionCount === 2) {
                exit(91);
            }
            return rename($temporaryPath, $targetPath);
        });
    $crashService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'atomic-owner',
    ]);
    exit(92);
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
$awardStatePath = $awardsDirectory . '/.xp-award-state.json';
if ($databasePath === false || $invalidDatabasePath === false) {
    throw new RuntimeException('Unable to create the XP test databases.');
}
if (!mkdir($awardsDirectory, 0700, true) && !is_dir($awardsDirectory)) {
    throw new RuntimeException('Unable to create the XP award limit fixture directory.');
}

try {
    $fetchCount = 0;
    $markdown = implode("\n", [
        'As of 7.20.2026 (question bonus)',
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
        '| Borca | Fighter | 1 | 304 |',
        '| Arilia | Feycaster | 1 | 200 |',
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

    $sameFirstMarkdown = implode("\n", [
        'As of 8.17.2026',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Ari Stoneward | Fighter | 2 | 2,000 |',
        '| Ari Valesong | Fighter | 3 | 4,000 |',
    ]);
    $sameFirstCharacters = implode("\n", [
        '| Name | Class | Level | Token | HP |',
        '| --- | --- | ---: | --- | ---: |',
        '| [[Ari Stoneward]] | Fighter | 2 | ![[ari-stoneward.webp]] | 20 |',
        '| [[Ari Valesong]] | Fighter | 3 | ![[ari-valesong.webp]] | 24 |',
    ]);
    $sameFirstService = new XpTrackingService(
        xpDatabase(':memory:'),
        xpConfiguration(),
        static function (string $url) use (
            $sameFirstMarkdown,
            $sameFirstCharacters,
            $progressionFixture): string {
            if (str_contains($url, 'Player+Characters+Listing')) {
                return $sameFirstCharacters;
            }
            return $progressionFixture($url) ?? $sameFirstMarkdown;
        });
    $ariStoneward = $sameFirstService->getForAccount([
        'role' => 'player',
        'character_key' => 'ari-stoneward',
    ]);
    $ariValesong = $sameFirstService->getForAccount([
        'role' => 'player',
        'character_key' => 'ari-valesong',
    ]);
    xpAssert(
        $ariStoneward['character']['character_name'] === 'Ari Stoneward'
            && $ariValesong['character']['character_name'] === 'Ari Valesong',
        'same-first-name XP authorization did not remain scoped to the full canonical character key.');
    $unmappedSameFirstService = new XpTrackingService(
        xpDatabase(':memory:'),
        array_replace(xpConfiguration(), ['character_key_aliases' => ['jelb' => 'jelb']]),
        static function (string $url) use (
            $sameFirstMarkdown,
            $sameFirstCharacters,
            $progressionFixture): string {
            if (str_contains($url, 'Player+Characters+Listing')) {
                return $sameFirstCharacters;
            }
            return $progressionFixture($url) ?? $sameFirstMarkdown;
        });
    expectXpError(
        fn() => $unmappedSameFirstService->getForAccount([
            'role' => 'player',
            'character_key' => 'ari-stoneward',
        ]),
        403,
        'xp_not_authorized');

    $levelRefreshMarkdown = implode("\n", [
        'As of 8.16.2026',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Neria | Paladin | 1 | 2,400 |',
        '| Shade | Ranger | 2 | 4,050 |',
    ]);
    $levelRefreshProgressionFixture = static function (string $url): ?string {
        if (str_contains($url, 'Class+Level+Progression')) {
            return implode("\n", [
                '| Class | Link |',
                '| --- | --- |',
                '[[Paladin]]',
                '[[Ranger]]',
            ]);
        }
        if (str_contains($url, '/Classes/Paladin')) {
            return implode("\n", [
                '| 1 | 0 |', '| --- | --- |', '| 2 | 2,750 |', '| 3 | 5,500 |', '| 4 | 12,000 |',
            ]);
        }
        if (str_contains($url, '/Classes/Ranger')) {
            return implode("\n", [
                '| 1 | 0 |', '| --- | --- |', '| 2 | 2,250 |', '| 3 | 4,500 |', '| 4 | 10,000 |',
            ]);
        }
        return null;
    };
    file_put_contents($awardsDirectory . '/neria-xp.json', json_encode([[
        'character_name' => 'Neria', 'character_class' => 'Paladin',
        'level_before_award' => 1, 'xp_award' => 1000,
        'xp_award_date' => '8.16.2026', 'level_after_award' => 2,
    ]], JSON_THROW_ON_ERROR));
    file_put_contents($awardsDirectory . '/shade-xp.json', json_encode([[
        'character_name' => 'Shade', 'character_class' => 'Ranger',
        'level_before_award' => 2, 'xp_award' => 1000,
        'xp_award_date' => '8.16.2026', 'level_after_award' => 3,
    ]], JSON_THROW_ON_ERROR));
    $levelRefreshService = new XpTrackingService(
        xpDatabase(':memory:'),
        array_replace(xpConfiguration(), [
            'awards_directory' => $awardsDirectory,
            'awards_root' => $awardsRoot,
            'award_groups' => ['dm' => ['neria-xp', 'shade-xp']],
        ]),
        static function (string $url) use ($levelRefreshMarkdown, $levelRefreshProgressionFixture): string {
            if (str_contains($url, '/Classes/') || str_contains($url, 'Class+Level+Progression')) {
                return $levelRefreshProgressionFixture($url) ?? '';
            }
            return $levelRefreshMarkdown;
        });
    $levelRefresh = $levelRefreshService->getForAccount([
        'role' => 'dm',
        'character_key' => 'dm',
    ]);
    $levelRefreshByName = [];
    foreach ($levelRefresh['characters'] as $character) {
        $levelRefreshByName[$character['character_name']] = $character;
    }
    xpAssert($levelRefreshByName['Neria']['level'] === 2, 'Neria current level was not taken from the latest award history.');
    xpAssert($levelRefreshByName['Neria']['xp_to_next_level'] === 3100, 'Neria TNL was calculated from the stale level.');
    xpAssert($levelRefreshByName['Shade']['level'] === 3, 'Shade current level was not taken from the latest award history.');
    xpAssert($levelRefreshByName['Shade']['xp_to_next_level'] === 5950, 'Shade TNL was calculated from the stale level.');

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
    xpAssert(
        $maximumAwards['progressions'][0]['is_account_character'] === true,
        'The XP awards response omitted the player identity marker.');
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

    $cachedAwardPath = $awardsDirectory . '/jelb-xp.json';
    file_put_contents($cachedAwardPath, json_encode([[
        'character_name' => 'Jelb',
        'character_class' => 'Illusionist',
        'level_before_award' => 3,
        'xp_award' => 500,
        'xp_award_date' => '7.23.2026',
        'level_after_award' => 4,
    ]], JSON_THROW_ON_ERROR));
    $cachedAwardFetchCount = 0;
    $cachedAwardService = new XpTrackingService(
        xpDatabase(':memory:'),
        array_replace(xpConfiguration(), [
            'awards_directory' => $awardsDirectory,
            'awards_root' => $awardsRoot,
            'award_groups' => ['jelb' => ['jelb-xp']],
        ]),
        static function (string $url) use (&$cachedAwardFetchCount, $markdown): string {
            if (str_ends_with($url, '/XP')) {
                $cachedAwardFetchCount++;
                return $markdown;
            }
            throw new RuntimeException('Optional XP enrichment should not be fetched by XP Awards.');
        });
    $cachedAwardService->getForAccount([
        'role' => 'player',
        'character_key' => 'jelb',
    ]);
    $cachedAwardService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'jelb',
    ]);
    xpAssert(
        $cachedAwardFetchCount === 2,
        'XP Awards performed an unnecessary second live XP/enrichment fetch.');

    $reservedKeyService = new XpTrackingService(
        xpDatabase(':memory:'),
        array_replace(xpConfiguration(), [
            'awards_directory' => $awardsDirectory,
            'awards_root' => $awardsRoot,
            'award_groups' => ['reserved-owner' => ['cumulative-state']],
        ]));
    expectXpError(
        fn() => $reservedKeyService->getAwardsForAccount([
            'role' => 'player',
            'character_key' => 'reserved-owner',
        ]),
        503,
        'xp_awards_unavailable');

    $dynamicAwardPath = $awardsDirectory . '/dynamic-xp.json';
    file_put_contents($dynamicAwardPath, json_encode([[
        'character_name' => 'Dynamic Hero',
        'character_class' => 'Fighter',
        'level_before_award' => 1,
        'xp_award' => 400,
        'xp_award_date' => '7.31.2026',
        'level_after_award' => 1,
    ]], JSON_THROW_ON_ERROR));
    $dynamicMarkdown = implode("\n", [
        'As of  8.04.2026',
        '',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Dynamic Hero | Fighter | 2 | 600 |',
        '',
        'As of 7.31.2026',
        '',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Dynamic Hero | Fighter | 1 | 400 |',
    ]);
    $dynamicAwardConfiguration = array_replace(xpConfiguration(), [
        'awards_directory' => $awardsDirectory,
        'awards_root' => $awardsRoot,
        'award_groups' => ['dynamic-hero' => ['dynamic-xp']],
    ]);
    $dynamicAwardService = new XpTrackingService(
        xpDatabase(':memory:'),
        $dynamicAwardConfiguration,
        static fn(string $url): string => $dynamicMarkdown);
    $dynamicAwards = $dynamicAwardService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'dynamic-hero',
    ]);
    $dynamicEntries = $dynamicAwards['progressions'][0]['entries'];
    xpAssert(count($dynamicEntries) === 2, 'The live XP source did not extend the cached progression.');
    xpAssert(
        $dynamicEntries[1]['xp_award_date'] === '8.04.2026'
            && $dynamicEntries[1]['xp_award'] === 200
            && $dynamicEntries[1]['level_before_award'] === 1
            && $dynamicEntries[1]['level_after_award'] === 2,
        'The latest live XP total was not converted into the expected award entry.');
    $dynamicFallbackService = new XpTrackingService(
        xpDatabase(':memory:'),
        $dynamicAwardConfiguration,
        static fn(string $url): never => throw new RuntimeException('simulated upstream failure'));
    $dynamicFallbackAwards = $dynamicFallbackService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'dynamic-hero',
    ]);
    xpAssert(
        count($dynamicFallbackAwards['progressions'][0]['entries']) === 2,
        'The last live XP progression was not cached for upstream failure fallback.');
    xpAssert(
        is_file($awardsDirectory . '/.xp-refresh.lock'),
        'The XP award refresh lock was not created.');

    file_put_contents($dynamicAwardPath, json_encode([[
        'character_name' => 'Dynamic Hero',
        'character_class' => 'Fighter',
        'level_before_award' => 1,
        'xp_award' => 400,
        'xp_award_date' => '7.31.2026',
        'level_after_award' => 1,
    ]], JSON_THROW_ON_ERROR));
    $missingBaselineMarkdown = implode("\n", [
        'As of 8.04.2026',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Dynamic Hero | Fighter | 2 | 600 |',
        '',
        'As of 7.30.2026',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Dynamic Hero | Fighter | 1 | 300 |',
    ]);
    $missingBaselineService = new XpTrackingService(
        xpDatabase(':memory:'),
        $dynamicAwardConfiguration,
        static fn(string $url): string => $missingBaselineMarkdown);
    $missingBaselineAwards = $missingBaselineService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'dynamic-hero',
    ]);
    xpAssert(
        count($missingBaselineAwards['progressions'][0]['entries']) === 1,
        'A nonmatching historical snapshot was used as an award baseline.');

    file_put_contents($dynamicAwardPath, json_encode([[
        'character_name' => 'Dynamic Hero',
        'character_class' => 'Fighter',
        'level_before_award' => 1,
        'xp_award' => 400,
        'xp_award_date' => '7.31.2026',
        'level_after_award' => 1,
    ]], JSON_THROW_ON_ERROR));
    @unlink($awardStatePath);
    $sameDateMarkdown = implode("\n", [
        'As of 7.31.2026',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Dynamic Hero | Fighter | 1 | 450 |',
    ]);
    $sameDateService = new XpTrackingService(
        xpDatabase(':memory:'),
        $dynamicAwardConfiguration,
        static fn(string $url): string => $sameDateMarkdown);
    $sameDateAwards = $sameDateService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'dynamic-hero',
    ]);
    $sameDateEntries = $sameDateAwards['progressions'][0]['entries'];
    xpAssert(
        count($sameDateEntries) === 1,
        'Initial cumulative XP state migration created a spurious same-date award.');
    $sameDateState = json_decode(
        (string)file_get_contents($awardStatePath),
        true,
        16,
        JSON_THROW_ON_ERROR);
    xpAssert(
        ($sameDateState['progressions']['dynamic-xp']['source_date'] ?? '') === '7.31.2026'
            && ($sameDateState['progressions']['dynamic-xp']['xp_total'] ?? -1) === 450,
        'Initial cumulative XP state was not persisted.');

    $sameDateFollowupMarkdown = str_replace('450', '500', $sameDateMarkdown);
    $sameDateFollowupService = new XpTrackingService(
        xpDatabase(':memory:'),
        $dynamicAwardConfiguration,
        static fn(string $url): string => $sameDateFollowupMarkdown);
    $sameDateFollowupAwards = $sameDateFollowupService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'dynamic-hero',
    ]);
    $sameDateFollowupEntries = $sameDateFollowupAwards['progressions'][0]['entries'];
    xpAssert(
        count($sameDateFollowupEntries) === 2
            && $sameDateFollowupEntries[1]['xp_award'] === 50
            && $sameDateFollowupEntries[1]['xp_award_date'] === '7.31.2026',
        'A post-migration cumulative XP increase on the same date was not recorded.');

    $trustedAwardState = (string)file_get_contents($awardStatePath);
    $trustedDynamicProgression = (string)file_get_contents($dynamicAwardPath);
    $tamperedAwardState = json_decode($trustedAwardState, true, 16, JSON_THROW_ON_ERROR);
    $tamperedAwardState['progressions']['dynamic-xp']['xp_total']++;
    file_put_contents(
        $awardStatePath,
        json_encode($tamperedAwardState, JSON_THROW_ON_ERROR));
    $tamperedPromotionCount = 0;
    $tamperedStateService = new XpTrackingService(
        xpDatabase(':memory:'),
        $dynamicAwardConfiguration,
        static fn(string $url): string => str_replace('450', '550', $sameDateMarkdown),
        static function (string $temporaryPath, string $targetPath) use (
            &$tamperedPromotionCount): bool {
            $tamperedPromotionCount++;
            return rename($temporaryPath, $targetPath);
        });
    $tamperedStateAwards = $tamperedStateService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'dynamic-hero',
    ]);
    xpAssert(
        $tamperedPromotionCount === 0
            && count($tamperedStateAwards['progressions'][0]['entries']) === 2
            && file_get_contents($dynamicAwardPath) === $trustedDynamicProgression,
        'A cumulative XP sidecar detached from its progression was trusted.');
    file_put_contents($awardStatePath, $trustedAwardState);

    $baselineDynamicEntries = [[
        'character_name' => 'Dynamic Hero',
        'character_class' => 'Fighter',
        'level_before_award' => 1,
        'xp_award' => 400,
        'xp_award_date' => '7.31.2026',
        'level_after_award' => 1,
    ]];
    file_put_contents($dynamicAwardPath, json_encode($baselineDynamicEntries, JSON_THROW_ON_ERROR));
    $duplicateDateMarkdown = $sameDateMarkdown . "\n\n" . str_replace('450', '460', $sameDateMarkdown);
    $duplicatePromotionCount = 0;
    $duplicateDateService = new XpTrackingService(
        xpDatabase(':memory:'),
        $dynamicAwardConfiguration,
        static fn(string $url): string => $duplicateDateMarkdown,
        static function (string $temporaryPath, string $targetPath) use (
            &$duplicatePromotionCount): bool {
            $duplicatePromotionCount++;
            return rename($temporaryPath, $targetPath);
        });
    $duplicateDateAwards = $duplicateDateService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'dynamic-hero',
    ]);
    xpAssert(
        $duplicatePromotionCount === 0
            && count($duplicateDateAwards['progressions'][0]['entries']) === 1,
        'Duplicate same-date XP snapshots were accepted or promoted.');

    $negativeMarkdown = str_replace('450', '350', $sameDateMarkdown)
        . "\n\nAs of 7.30.2026\n"
        . "| Name | Class | Level | XP Total |\n"
        . "| --- | --- | ---: | ---: |\n"
        . "| Dynamic Hero | Fighter | 1 | 300 |\n";
    $negativePromotionCount = 0;
    $negativeService = new XpTrackingService(
        xpDatabase(':memory:'),
        $dynamicAwardConfiguration,
        static fn(string $url): string => $negativeMarkdown,
        static function (string $temporaryPath, string $targetPath) use (
            &$negativePromotionCount): bool {
            $negativePromotionCount++;
            return rename($temporaryPath, $targetPath);
        });
    $negativeAwards = $negativeService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'dynamic-hero',
    ]);
    xpAssert(
        $negativePromotionCount === 0
            && count($negativeAwards['progressions'][0]['entries']) === 1,
        'A negative or reset XP total was promoted into the award cache.');

    $atomicMarkdown = implode("\n", [
        'As of 8.01.2026',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Alpha Hero | Fighter | 1 | 150 |',
        '| Beta Hero | Fighter | 1 | 150 |',
        '',
        'As of 7.31.2026',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Alpha Hero | Fighter | 1 | 100 |',
        '| Beta Hero | Fighter | 1 | 100 |',
    ]);
    $atomicPaths = [
        'alpha-xp' => $awardsDirectory . '/alpha-xp.json',
        'beta-xp' => $awardsDirectory . '/beta-xp.json',
    ];
    foreach ($atomicPaths as $progressionKey => $atomicPath) {
        $characterName = ucfirst(str_replace('-xp', '', $progressionKey)) . ' Hero';
        file_put_contents($atomicPath, json_encode([[
            'character_name' => $characterName,
            'character_class' => 'Fighter',
            'level_before_award' => 1,
            'xp_award' => 100,
            'xp_award_date' => '7.31.2026',
            'level_after_award' => 1,
        ]], JSON_THROW_ON_ERROR));
    }
    $atomicBefore = array_map(
        static fn(string $path): string => (string)file_get_contents($path),
        $atomicPaths);
    $atomicStateBefore = (string)file_get_contents($awardStatePath);
    $atomicAwardGroups = $dynamicAwardConfiguration['award_groups'];
    $atomicAwardGroups['atomic-owner'] = array_keys($atomicPaths);
    $promotionCount = 0;
    $atomicService = new XpTrackingService(
        xpDatabase(':memory:'),
        array_replace($dynamicAwardConfiguration, [
            'award_groups' => $atomicAwardGroups,
        ]),
        static fn(string $url): string => $atomicMarkdown,
        static function (string $temporaryPath, string $targetPath) use (&$promotionCount): bool {
            $promotionCount++;
            return $promotionCount === 2 ? false : rename($temporaryPath, $targetPath);
        });
    $atomicService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'atomic-owner',
    ]);
    foreach ($atomicPaths as $progressionKey => $atomicPath) {
        xpAssert(
            file_get_contents($atomicPath) === $atomicBefore[$progressionKey],
            'A failed multi-progression refresh exposed a mixed cache generation.');
    }
    xpAssert(
        $promotionCount === 2
            && file_get_contents($awardStatePath) === $atomicStateBefore,
        'A failed multi-progression refresh did not restore the cumulative state.');
    xpAssert(
        (glob($awardsDirectory . '/.xp-cache-*') ?: []) === []
            && (glob($awardsDirectory . '/*.rollback-*') ?: []) === []
            && (glob($awardsDirectory . '/.xp-award-state.json.rollback-*') ?: []) === [],
        'A failed multi-progression refresh left staging or rollback artifacts.');

    $stateFailureService = new XpTrackingService(
        xpDatabase(':memory:'),
        array_replace($dynamicAwardConfiguration, ['award_groups' => $atomicAwardGroups]),
        static fn(string $url): string => $atomicMarkdown,
        static function (string $temporaryPath, string $targetPath): bool {
            return basename($targetPath) === '.xp-award-state.json'
                ? false
                : rename($temporaryPath, $targetPath);
        });
    $stateFailureService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'atomic-owner',
    ]);
    foreach ($atomicPaths as $progressionKey => $atomicPath) {
        xpAssert(
            file_get_contents($atomicPath) === $atomicBefore[$progressionKey],
            'A sidecar promotion failure did not restore an XP progression.');
    }
    xpAssert(
        file_get_contents($awardStatePath) === $atomicStateBefore,
        'A sidecar promotion failure did not restore the prior cumulative state.');

    $crashProcess = proc_open(
        [PHP_BINARY, __FILE__, 'crash-writer', $awardsRoot, $awardsDirectory],
        [1 => ['pipe', 'w'], 2 => ['pipe', 'w']],
        $crashPipes,
        __DIR__);
    xpAssert(is_resource($crashProcess), 'The crash-recovery writer could not be started.');
    foreach ($crashPipes as $crashPipe) {
        stream_get_contents($crashPipe);
        fclose($crashPipe);
    }
    xpAssert(proc_close($crashProcess) === 91, 'The crash-recovery writer did not stop mid-commit.');
    xpAssert(
        is_file($awardsDirectory . '/.xp-award-transaction.json'),
        'The interrupted XP transaction did not preserve its recovery journal.');
    $recoveryService = new XpTrackingService(
        xpDatabase(':memory:'),
        array_replace($dynamicAwardConfiguration, ['award_groups' => $atomicAwardGroups]),
        static function (string $url): string {
            throw new RuntimeException('Simulated source outage after process interruption.');
        });
    $recoveredAwards = $recoveryService->getAwardsForAccount([
        'role' => 'player',
        'character_key' => 'atomic-owner',
    ]);
    xpAssert(count($recoveredAwards['progressions']) === 2, 'Recovered XP progressions were unavailable.');
    foreach ($atomicPaths as $progressionKey => $atomicPath) {
        xpAssert(
            file_get_contents($atomicPath) === $atomicBefore[$progressionKey],
            'Crash recovery did not restore an original XP progression.');
    }
    xpAssert(
        file_get_contents($awardStatePath) === $atomicStateBefore
            && !is_file($awardsDirectory . '/.xp-award-transaction.json'),
        'Crash recovery did not restore the cumulative XP state or clear its journal.');

    $lockHandle = fopen($awardsDirectory . '/.xp-refresh.lock', 'c');
    xpAssert(is_resource($lockHandle), 'The XP refresh lock fixture could not be opened.');
    xpAssert(flock($lockHandle, LOCK_EX), 'The XP refresh lock fixture could not be acquired.');
    $hiddenAlphaPath = $atomicPaths['alpha-xp'] . '.hidden';
    xpAssert(
        rename($atomicPaths['alpha-xp'], $hiddenAlphaPath),
        'The XP progression could not be hidden during the lock contention test.');
    $readerPipes = [];
    $readerProcess = proc_open(
        [PHP_BINARY, __FILE__, 'lock-reader', $awardsRoot, $awardsDirectory],
        [1 => ['pipe', 'w'], 2 => ['pipe', 'w']],
        $readerPipes,
        __DIR__);
    xpAssert(is_resource($readerProcess), 'The concurrent XP reader could not be started.');
    try {
        usleep(150000);
        $readerStatus = proc_get_status($readerProcess);
        xpAssert(
            ($readerStatus['running'] ?? false) === true,
            'A concurrent XP reader bypassed the collection lock while a file was hidden.');
    } finally {
        rename($hiddenAlphaPath, $atomicPaths['alpha-xp']);
        flock($lockHandle, LOCK_UN);
        fclose($lockHandle);
    }
    $readerOutput = stream_get_contents($readerPipes[1]);
    $readerError = stream_get_contents($readerPipes[2]);
    fclose($readerPipes[1]);
    fclose($readerPipes[2]);
    $readerExitCode = proc_close($readerProcess);
    xpAssert(
        $readerExitCode === 0 && str_contains((string)$readerOutput, 'lock reader passed'),
        'The concurrent XP reader failed after the collection lock was released: '
            . trim((string)$readerError));

    $mismatchPath = $awardsDirectory . '/mismatch-xp.json';
    file_put_contents($mismatchPath, json_encode([[
        'character_name' => 'Dynamic Hero',
        'character_class' => 'Fighter',
        'level_before_award' => 1,
        'xp_award' => 400,
        'xp_award_date' => '7.31.2026',
        'level_after_award' => 1,
    ]], JSON_THROW_ON_ERROR));
    $mismatchService = new XpTrackingService(
        xpDatabase(':memory:'),
        array_replace($dynamicAwardConfiguration, [
            'award_groups' => ['mismatch-owner' => ['mismatch-xp']],
        ]),
        static fn(string $url): string => $dynamicMarkdown);
    expectXpError(
        fn() => $mismatchService->getAwardsForAccount([
            'role' => 'player',
            'character_key' => 'mismatch-owner',
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
    xpAssert(
        count($player['authorized_characters']) === 1
            && $player['authorized_characters'][0]['character_key'] === 'jelb'
            && $player['authorized_characters'][0]['character']['character_name'] === 'Jelb',
        'The player current-XP response omitted its authorized character collection.');
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
    xpAssert(count($dm['characters']) === 5, 'The Dungeon Master did not receive every current XP row.');
    $borca = array_values(array_filter(
        $dm['characters'],
        static fn(array $character): bool => $character['character_name'] === 'Borca'));
    xpAssert(
        count($borca) === 1 && $borca[0]['hit_points'] === 0,
        'A current XP character absent from the active roster did not receive the safe HP fallback.');
    $arilia = array_values(array_filter(
        $dm['characters'],
        static fn(array $character): bool => $character['character_name'] === 'Arilia'));
    xpAssert(
        count($arilia) === 1 && $arilia[0]['xp_to_next_level'] === null,
        'An unavailable class progression prevented the live XP snapshot from loading.');
    xpAssert($fetchCount === 18, 'Each XP request did not attempt the live source before using cached data.');

    $olderMarkdown = implode("\n", [
        'As of 7.20.2026',
        '| Name | Class | Level | XP Total |',
        '| --- | --- | ---: | ---: |',
        '| Jelb | Illusionist | 3 | 1 |',
    ]);
    $olderLiveService = new XpTrackingService(
        xpDatabase($databasePath),
        xpConfiguration(),
        static function (string $url) use (
            $olderMarkdown,
            $characterMarkdown,
            $progressionFixture): string {
            if (str_contains($url, 'Player+Characters+Listing')) {
                return $characterMarkdown;
            }
            return $progressionFixture($url) ?? $olderMarkdown;
        });
    $olderLive = $olderLiveService->getForAccount([
        'role' => 'player',
        'character_key' => 'jelb',
    ]);
    xpAssert(
        $olderLive['date_label'] === 'As of 7.23.2026'
            && $olderLive['character']['xp_total'] === 12345
            && $olderLive['stale'] === true,
        'An older successful live snapshot replaced the newer cached XP snapshot.');

    $sameDateRollbackMarkdown = str_replace('12,345', '12,000', $markdown);
    $sameDateRollbackService = new XpTrackingService(
        xpDatabase($databasePath),
        xpConfiguration(),
        static function (string $url) use (
            $sameDateRollbackMarkdown,
            $characterMarkdown,
            $progressionFixture): string {
            if (str_contains($url, 'Player+Characters+Listing')) {
                return $characterMarkdown;
            }
            return $progressionFixture($url) ?? $sameDateRollbackMarkdown;
        });
    $sameDateRollback = $sameDateRollbackService->getForAccount([
        'role' => 'player',
        'character_key' => 'jelb',
    ]);
    xpAssert(
        $sameDateRollback['character']['xp_total'] === 12345
            && $sameDateRollback['stale'] === true,
        'A lower same-date live XP total replaced the last-known-good total.');

    $enrichmentFallbackService = new XpTrackingService(
        xpDatabase($databasePath),
        xpConfiguration(),
        static fn(string $url): string => str_ends_with($url, '/XP')
            ? $markdown
            : throw new RuntimeException('simulated optional enrichment failure'));
    $enrichmentFallback = $enrichmentFallbackService->getForAccount([
        'role' => 'player',
        'character_key' => 'jelb',
    ]);
    xpAssert(
        $enrichmentFallback['stale'] === false
            && $enrichmentFallback['character']['xp_total'] === 12345
            && $enrichmentFallback['character']['hit_points'] === 13
            && $enrichmentFallback['character']['xp_to_next_level'] === 7655,
        'Optional enrichment failure discarded last-known-good HP or TNL values.');

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

    $newerXpMarkdown = str_replace(
        ['As of 7.23.2026', '12,345'],
        ['As of 7.24.2026', '13,000'],
        $markdown);
    $newerXpWithoutEnrichmentService = new XpTrackingService(
        xpDatabase($databasePath),
        xpConfiguration(),
        static fn(string $url): string => str_ends_with($url, '/XP')
            ? $newerXpMarkdown
            : throw new RuntimeException('simulated optional enrichment failure'));
    $newerXpWithoutEnrichment = $newerXpWithoutEnrichmentService->getForAccount([
        'role' => 'player',
        'character_key' => 'jelb',
    ]);
    xpAssert(
        $newerXpWithoutEnrichment['stale'] === false
            && $newerXpWithoutEnrichment['character']['xp_total'] === 13000
            && $newerXpWithoutEnrichment['character']['hit_points'] === 13
            && $newerXpWithoutEnrichment['character']['xp_to_next_level'] === null,
        'A newer XP total with unavailable optional enrichment discarded cached HP.');

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
        @unlink($awardsDirectory . '/.xp-refresh.lock');
        @unlink($awardStatePath);
        foreach (glob($awardsDirectory . '/*') ?: [] as $awardFile) {
            @unlink($awardFile);
        }
        @rmdir($awardsDirectory);
    }
    @rmdir($awardsRoot);
}
