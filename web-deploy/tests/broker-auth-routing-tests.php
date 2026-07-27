<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/RpolClient.php';
require_once __DIR__ . '/../player-assistant-broker/CharacterAuthService.php';
require_once __DIR__ . '/../player-assistant-broker/XpTrackingService.php';
require_once __DIR__ . '/../player-assistant-broker/WordCountService.php';
require_once __DIR__ . '/../player-assistant-broker/BrokerService.php';

function routingAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$databasePath = tempnam(sys_get_temp_dir(), 'pa-broker-route-');
$snapshotDirectory = sys_get_temp_dir() . '/pa-broker-snapshots-' . bin2hex(random_bytes(6));
$snapshotSigningKey = random_bytes(32);
if ($databasePath === false) {
    throw new RuntimeException('Unable to create the broker routing test database.');
}

try {
    $config = [
        'api' => [
            'database_path' => $databasePath,
            'admin_key' => 'test-admin-key-with-sufficient-entropy',
            'default_token_lifetime_days' => 1,
            'max_token_lifetime_days' => 30,
            'requests_per_minute' => 10,
            'snapshot_directory' => $snapshotDirectory,
            'snapshot_signing_key' => base64_encode($snapshotSigningKey),
            'snapshot_max_age_seconds' => 30,
            'snapshot_retention_seconds' => 60,
        ],
        'auth' => [
            'expected_origin' => 'https://example.test',
            'idle_timeout_seconds' => 60,
            'absolute_timeout_seconds' => 600,
            'login_window_seconds' => 300,
            'login_max_failures' => 3,
            'login_lockout_seconds' => 300,
        ],
        'xp' => [
            'source_url' => 'https://publish.obsidian.md/example/XP',
            'character_source_url' => 'https://publish.obsidian.md/example/PCs/Player+Characters+Listing',
            'class_progression_index_url' => 'https://publish.obsidian.md/example/Classes/Class+Level+Progression',
            'connect_timeout_seconds' => 1,
            'timeout_seconds' => 2,
            'maximum_response_bytes' => 65536,
            'cache_ttl_seconds' => 60,
            'maximum_stale_seconds' => 600,
        ],
        'rpol' => [
            'username' => 'unused',
            'password' => 'unused',
            'game_id' => '80170',
        ],
    ];
    $broker = new BrokerService(
        $config,
        new RpolClient($config['rpol']),
        static function (string $url): string {
            if (str_contains($url, 'Player+Characters+Listing')) {
                return implode("\n", [
                '| Name | Class | Level | HP |',
                '| --- | --- | ---: | ---: |',
                '| Routing Hero | Ranger | 4 | 17 |',
                '| Another Hero | Fighter | 5 | 29 |',
                ]);
            }
            if (str_contains($url, 'Class+Level+Progression')) {
                return "- [[Fighter]]\n- [[Ranger]]";
            }
            if (str_contains($url, '/Classes/Ranger')) {
                return implode("\n", [
                    '| 1 | 0 |',
                    '| 2 | 2,250 |',
                    '| 3 | 4,500 |',
                    '| 4 | 10,000 |',
                    '| 5 | 20,000 |',
                ]);
            }
            if (str_contains($url, '/Classes/Fighter')) {
                return implode("\n", [
                    '| 1 | 0 |',
                    '| 2 | 2,000 |',
                    '| 3 | 4,000 |',
                    '| 4 | 8,000 |',
                    '| 5 | 16,000 |',
                    '| 6 | 32,000 |',
                ]);
            }
            return implode("\n", [
                'As of 7.23.2026',
                '',
                '| Name | Class | Level | XP Total |',
                '| --- | --- | ---: | ---: |',
                '| Routing Hero | Ranger | 4 | 12,345 |',
                '| Another Hero | Fighter | 5 | 98,765 |',
            ]);
        });
    $session = [];
    $adminHeaders = ['admin-key' => $config['api']['admin_key']];
    if (!mkdir($snapshotDirectory, 0700, true) && !is_dir($snapshotDirectory)) {
        throw new RuntimeException('Unable to create the snapshot retention test directory.');
    }
    $staleSnapshotPath = $snapshotDirectory . '/' . str_repeat('a', 64) . '.json';
    file_put_contents($staleSnapshotPath, '{}');
    touch($staleSnapshotPath, time() - 120);

    $snapshotSourceUrl = 'https://rpol.net/game.php?gi=80170';
    $snapshotContent = '<html><body>Sanitized RPOL fixture</body></html>';
    $snapshotFetchedAt = gmdate(DATE_ATOM);
    $snapshotContentHash = hash('sha256', $snapshotContent);
    $snapshotCanonical = implode("\n", [
        '1',
        $config['rpol']['game_id'],
        $snapshotSourceUrl,
        $snapshotFetchedAt,
        'text/html; charset=utf-8',
        $snapshotContentHash,
    ]);
    $snapshot = [
        'schema_version' => 1,
        'game_id' => $config['rpol']['game_id'],
        'source_url' => $snapshotSourceUrl,
        'fetched_at' => $snapshotFetchedAt,
        'content_type' => 'text/html; charset=utf-8',
        'content_sha256' => $snapshotContentHash,
        'content_base64' => base64_encode($snapshotContent),
        'signature_algorithm' => 'HMAC-SHA256',
        'signature' => hash_hmac('sha256', $snapshotCanonical, $snapshotSigningKey),
    ];
    $storedSnapshot = $broker->dispatch(
        'PUT',
        '/v1/snapshots/page',
        [],
        $snapshot,
        $adminHeaders,
        '192.0.2.30',
        $session);
    routingAssert($storedSnapshot['status'] === 201, 'The signed RPOL snapshot upload failed.');
    routingAssert(!file_exists($staleSnapshotPath), 'The expired RPOL snapshot was not pruned.');
    $currentSnapshotPath = $snapshotDirectory . '/' . hash('sha256', $snapshotSourceUrl) . '.json';
    routingAssert(is_file($currentSnapshotPath), 'The current RPOL snapshot was pruned.');

    $rpolClient = new RpolClient($config['rpol']);
    $rpolClient->validateTargetUrl('https://rpol.net/usermodules/diceroller.cgi?gi=80170');
    try {
        $rpolClient->validateTargetUrl('https://rpol.net/usermodules/diceroller.cgi?gi=80170&admin=1');
        throw new RuntimeException('The Dice Roller allowlist accepted an unsupported query parameter.');
    } catch (InvalidArgumentException) {
    }

    try {
        $broker->dispatch('GET', '/v1/xp', [], [], [], '192.0.2.30', $session);
        throw new RuntimeException('The protected XP route accepted an unauthenticated request.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 401 && $exception->errorName === 'authentication_required',
            'The protected XP route failed with the wrong unauthenticated response.');
    }

    try {
        $broker->dispatch('GET', '/v1/word-counts', [], [], [], '192.0.2.30', $session);
        throw new RuntimeException('The protected word-count route accepted an unauthenticated request.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 401 && $exception->errorName === 'authentication_required',
            'The protected word-count route failed with the wrong unauthenticated response.');
    }

    try {
        $broker->dispatch('GET', '/v1/presence', [], [], [], '192.0.2.30', $session);
        throw new RuntimeException('The protected presence route accepted an unauthenticated request.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 401 && $exception->errorName === 'authentication_required',
            'The protected presence route failed with the wrong unauthenticated response.');
    }

    $wordCountSnapshot = [
        'schema_version' => 1,
        'observed_at' => '2026-07-26T18:30:00Z',
        'counting_rule_version' => 'obsidian-publish-word-count-v1',
        'wiki' => ['pages' => 985, 'words' => 232048],
        'ic' => ['files' => 8, 'words' => 14998],
        'ooc' => ['files' => 6, 'words' => 18652],
    ];
    $storedWordCounts = $broker->dispatch(
        'PUT',
        '/v1/admin/word-counts',
        [],
        $wordCountSnapshot,
        $adminHeaders,
        '192.0.2.30',
        $session);
    routingAssert($storedWordCounts['status'] === 201, 'The word-count upload route failed.');
    routingAssert(
        $storedWordCounts['body']['wiki']['words'] === 232048,
        'The word-count upload route returned the wrong total.');
    try {
        $invalidSnapshot = $wordCountSnapshot;
        $invalidSnapshot['wiki'] = ['pages' => 0, 'words' => 1];
        $broker->dispatch(
            'PUT',
            '/v1/admin/word-counts',
            [],
            $invalidSnapshot,
            $adminHeaders,
            '192.0.2.30',
            $session);
        throw new RuntimeException('The word-count upload route accepted an invalid snapshot.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 400 && $exception->errorName === 'invalid_word_counts',
            'The word-count upload route rejected invalid data with the wrong response.');
    }

    $created = $broker->dispatch(
        'POST',
        '/v1/admin/character-accounts',
        [],
        [
            'character_name' => 'Routing Hero',
            'password' => 'routing password',
            'character_key' => 'routing',
            'role' => 'player',
        ],
        $adminHeaders,
        '192.0.2.30',
        $session);
    routingAssert($created['status'] === 201, 'The account administration route did not create an account.');

    $regenerated = false;
    $login = $broker->dispatch(
        'POST',
        '/v1/login',
        [],
        ['character_name' => 'Routing Hero', 'password' => 'routing password'],
        ['origin' => 'https://example.test'],
        '192.0.2.30',
        $session,
        function () use (&$regenerated): void {
            $regenerated = true;
        });
    routingAssert($login['status'] === 200 && $regenerated, 'The broker login route failed.');

    $restored = $broker->dispatch(
        'GET',
        '/v1/session',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert($restored['body']['authenticated'] === true, 'The broker session route failed.');

    $identity = $broker->dispatch(
        'GET',
        '/v1/me',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert(
        $identity['body']['account']['character_key'] === 'routing',
        'The protected identity route did not use the session account.');

    $xp = $broker->dispatch(
        'GET',
        '/v1/xp',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert($xp['status'] === 200, 'The protected XP route failed.');
    routingAssert($xp['body']['scope'] === 'character', 'The player XP response had the wrong scope.');
    routingAssert($xp['body']['character']['xp_total'] === 12345, 'The player XP response had the wrong total.');
    routingAssert($xp['body']['character']['character_class'] === 'Ranger', 'The player XP response had the wrong class.');
    routingAssert($xp['body']['character']['level'] === 4, 'The player XP response had the wrong level.');
    routingAssert($xp['body']['character']['hit_points'] === 17, 'The player XP response had the wrong hit points.');
    routingAssert($xp['body']['character']['xp_to_next_level'] === 7655, 'The player XP response had the wrong TNL value.');
    routingAssert(!isset($xp['body']['characters']), 'The player XP response exposed party totals.');
    routingAssert(!isset($xp['body']['source_url']), 'The player XP response exposed the configured source URL.');

    $wordCounts = $broker->dispatch(
        'GET',
        '/v1/word-counts',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert($wordCounts['status'] === 200, 'The protected word-count route failed.');
    routingAssert($wordCounts['body']['wiki']['words'] === 232048, 'The wiki word count was incorrect.');
    routingAssert($wordCounts['body']['ic']['words'] === 14998, 'The IC word count was incorrect.');
    routingAssert($wordCounts['body']['ooc']['words'] === 18652, 'The OOC word count was incorrect.');
    routingAssert(
        $wordCounts['body']['observed_at'] === $wordCountSnapshot['observed_at'],
        'The word-count observation time changed during storage.');

    $presence = $broker->dispatch(
        'GET',
        '/v1/presence',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert(
        $presence['status'] === 200
            && $presence['body']['scope'] === 'self'
            && $presence['body']['users'] === [],
        'The player presence route exposed other users.');

    $destroyed = false;
    $logout = $broker->dispatch(
        'POST',
        '/v1/logout',
        [],
        [],
        [
            'origin' => 'https://example.test',
            'csrf-token' => $restored['body']['csrf_token'],
        ],
        '192.0.2.30',
        $session,
        null,
        function () use (&$destroyed): void {
            $destroyed = true;
        });
    routingAssert(
        $logout['body']['authenticated'] === false && $destroyed,
        'The broker logout route failed.');

    $health = $broker->dispatch(
        'GET',
        '/v1/health',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert($health['body']['schema_version'] === 4, 'The broker schema version was not advanced.');
    routingAssert($health['body']['character_account_count'] === 1, 'The health route account count was incorrect.');
    routingAssert($health['body']['xp_tracking_configured'] === true, 'The health route XP configuration state was incorrect.');
    routingAssert(
        $health['body']['word_count_snapshot_available'] === true,
        'The health route word-count snapshot state was incorrect.');

    fwrite(STDOUT, "Broker authentication routing tests passed.\n");
} finally {
    @unlink($databasePath);
    if (is_dir($snapshotDirectory)) {
        foreach (glob($snapshotDirectory . '/*') ?: [] as $snapshotFile) {
            @unlink($snapshotFile);
        }
        @rmdir($snapshotDirectory);
    }
}
