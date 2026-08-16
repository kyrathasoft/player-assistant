<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/RpolClient.php';
require_once __DIR__ . '/../player-assistant-broker/CharacterAuthService.php';
require_once __DIR__ . '/../player-assistant-broker/XpTrackingService.php';
require_once __DIR__ . '/../player-assistant-broker/WordCountService.php';
require_once __DIR__ . '/../player-assistant-broker/BrokerOperations.php';
require_once __DIR__ . '/../player-assistant-broker/QuestService.php';
require_once __DIR__ . '/../player-assistant-broker/MessageService.php';
require_once __DIR__ . '/../player-assistant-broker/BrokerService.php';

function routingAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

function routingAdminHeaders(string $method, string $route, array $body, string $key): array
{
    $timestamp = (string)time();
    $nonce = bin2hex(random_bytes(16));
    $bodyJson = json_encode(
        $body,
        JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_PRESERVE_ZERO_FRACTION | JSON_THROW_ON_ERROR);
    $canonical = implode("\n", [
        $timestamp,
        $nonce,
        strtoupper($method),
        $route,
        hash('sha256', $bodyJson),
    ]);
    return [
        'admin-timestamp' => $timestamp,
        'admin-nonce' => $nonce,
        'admin-signature' => hash_hmac('sha256', $canonical, $key),
    ];
}

$databasePath = tempnam(sys_get_temp_dir(), 'pa-broker-route-');
$snapshotDirectory = sys_get_temp_dir() . '/pa-broker-snapshots-' . bin2hex(random_bytes(6));
$snapshotSigningKey = random_bytes(32);
$wordCountStatusPath = tempnam(sys_get_temp_dir(), 'pa-word-count-status-');
$wordCountSigningKeypair = sodium_crypto_sign_keypair();
$wordCountSigningSecretKey = sodium_crypto_sign_secretkey($wordCountSigningKeypair);
$wordCountSigningPublicKey = sodium_crypto_sign_publickey($wordCountSigningKeypair);
$xpAwardsDirectory = sys_get_temp_dir() . '/pa-xp-awards-' . bin2hex(random_bytes(6));
if ($databasePath === false) {
    throw new RuntimeException('Unable to create the broker routing test database.');
}
if ($wordCountStatusPath === false) {
    throw new RuntimeException('Unable to create the word-count status test path.');
}
@unlink($wordCountStatusPath);

try {
    if (!mkdir($xpAwardsDirectory, 0700, true) && !is_dir($xpAwardsDirectory)) {
        throw new RuntimeException('Unable to create the XP awards fixture directory.');
    }
    file_put_contents(
        $xpAwardsDirectory . '/routing-xp.json',
        json_encode([[
            'character_name' => 'Routing Hero',
            'character_class' => 'Ranger',
            'level_before_award' => 3,
            'xp_award' => 500,
            'xp_award_date' => '7.30.2026',
            'level_after_award' => 4,
        ]], JSON_THROW_ON_ERROR));
    file_put_contents(
        $xpAwardsDirectory . '/companion-xp.json',
        json_encode([[
            'character_name' => 'Companion Hero',
            'character_class' => 'Cleric',
            'level_before_award' => 2,
            'xp_award' => 250,
            'xp_award_date' => '7.30.2026',
            'level_after_award' => 2,
        ]], JSON_THROW_ON_ERROR));
    $questDataPath = __DIR__ . '/../../pwa/quests.json';
    $questFixture = json_decode(
        (string)file_get_contents($questDataPath),
        true,
        32,
        JSON_THROW_ON_ERROR);
    $questFixtureCount = count($questFixture['quests'] ?? []);
    if ($questFixtureCount === 0) {
        throw new RuntimeException('The quest routing fixture is empty.');
    }
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
            'maximum_stale_seconds' => 600,
            'awards_directory' => $xpAwardsDirectory,
            'awards_root' => dirname($xpAwardsDirectory),
            'award_groups' => [
                'routing' => ['routing-xp', 'companion-xp'],
            ],
        ],
        'word_counts' => [
            'source_url' => 'https://publish.obsidian.md/example/word-counts-latest.json',
            'connect_timeout_seconds' => 1,
            'timeout_seconds' => 2,
            'maximum_response_bytes' => 65536,
            'maximum_stale_seconds' => 60,
            'status_path' => $wordCountStatusPath,
            'signature_key_id' => 'test-word-count-key',
            'signature_public_key' => base64_encode($wordCountSigningPublicKey),
        ],
        'rpol' => [
            'username' => 'unused',
            'password' => 'unused',
            'game_id' => '80170',
        ],
    ];
    $wordCountRefreshCount = 0;
    $currentWordCountSnapshot = [
        'schema_version' => 1,
        'observed_at' => gmdate(DATE_ATOM),
        'counting_rule_version' => 'obsidian-publish-word-count-v1',
        'wiki' => ['pages' => 990, 'words' => 233048],
        'ic' => ['files' => 8, 'words' => 15099],
        'ooc' => ['files' => 6, 'words' => 18753],
    ];
    $wordCountFetcher = static function (string $url) use (
        &$wordCountRefreshCount,
        $currentWordCountSnapshot,
        $wordCountSigningSecretKey): string {
        if (str_contains($url, 'word-counts-latest.json')) {
            ++$wordCountRefreshCount;
            $payloadJson = json_encode(
                $currentWordCountSnapshot,
                JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
            return json_encode([
                'payload' => $currentWordCountSnapshot,
                'signature' => [
                    'algorithm' => 'Ed25519',
                    'key_id' => 'test-word-count-key',
                    'value' => base64_encode(sodium_crypto_sign_detached($payloadJson, $wordCountSigningSecretKey)),
                ],
            ], JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
        }

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
    };
    $broker = new BrokerService(
        $config,
        new RpolClient($config['rpol']),
        $wordCountFetcher,
        $wordCountFetcher,
        $questDataPath);
    $session = [];
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
        routingAdminHeaders('PUT', '/v1/snapshots/page', $snapshot, $config['api']['admin_key']),
        '192.0.2.30',
        $session);
    routingAssert($storedSnapshot['status'] === 201, 'The signed RPOL snapshot upload failed.');
    routingAssert(!file_exists($staleSnapshotPath), 'The expired RPOL snapshot was not pruned.');
    $currentSnapshotPath = $snapshotDirectory . '/' . hash('sha256', $snapshotSourceUrl) . '.json';
    routingAssert(is_file($currentSnapshotPath), 'The current RPOL snapshot was pruned.');
    $issuedToken = $broker->dispatch(
        'POST',
        '/v1/tokens',
        [],
        ['label' => 'snapshot route validation'],
        routingAdminHeaders(
            'POST',
            '/v1/tokens',
            ['label' => 'snapshot route validation'],
            $config['api']['admin_key']),
        '192.0.2.30',
        $session);
    try {
        $broker->dispatch(
            'GET',
            '/v1/snapshots/page',
            ['url' => 'https://rpol.net/display.cgi?gi=80170&ti=3&unsupported=1'],
            [],
            ['authorization' => 'Bearer ' . $issuedToken['body']['token']],
            '192.0.2.30',
            $session);
        throw new RuntimeException('The snapshot route accepted an unsupported RPOL query parameter.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 400 && $exception->errorName === 'invalid_rpol_url',
            'The snapshot route treated an invalid RPOL URL as a server error.');
    }
    $publicHealth = $broker->dispatch('GET', '/v1/health', [], [], [], '192.0.2.30', $session);
    routingAssert(
        $publicHealth['body']['schema_version'] === 7
            && !isset($publicHealth['body']['character_account_count']),
        'The public health route disclosed operational details.');
    $healthHeaders = routingAdminHeaders('GET', '/v1/admin/health', [], $config['api']['admin_key']);
    $adminHealth = $broker->dispatch(
        'GET',
        '/v1/admin/health',
        [],
        [],
        $healthHeaders,
        '192.0.2.30',
        $session);
    routingAssert(
        $adminHealth['body']['schema_version'] === 7
            && $adminHealth['body']['quest_request_workflow_configured'] === true,
        'The admin health route did not expose readiness details.');
    try {
        $broker->dispatch(
            'GET',
            '/v1/admin/health',
            [],
            [],
            $healthHeaders,
            '192.0.2.30',
            $session);
        throw new RuntimeException('The admin signature nonce was replayable.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 403 && $exception->errorName === 'admin_replay',
            'The admin signature replay was rejected with the wrong response.');
    }

    $rpolClient = new RpolClient($config['rpol']);
    $rpolClient->validateTargetUrl('https://rpol.net/usermodules/diceroller.cgi?gi=80170');
    $redirectValidator = new ReflectionMethod(RpolClient::class, 'validateRedirectUrl');
    $redirectValidator->setAccessible(true);
    $redirectValidator->invoke($rpolClient, 'https://rpol.net/login.cgi', true);
    try {
        $redirectValidator->invoke($rpolClient, 'https://rpol.net/admin.cgi', true);
        throw new RuntimeException('The RPOL redirect allowlist accepted an unsupported endpoint.');
    } catch (InvalidArgumentException) {
    }
    try {
        $redirectValidator->invoke($rpolClient, 'https://rpol.net/login.cgi?next=/admin.cgi', true);
        throw new RuntimeException('The RPOL redirect allowlist accepted a login query redirect.');
    } catch (InvalidArgumentException) {
    }
    try {
        $redirectValidator->invoke($rpolClient, 'https://rpol.net/usermodules/diceroller.cgi?gi=99999', true);
        throw new RuntimeException('The RPOL redirect allowlist accepted a different game.');
    } catch (InvalidArgumentException) {
    }
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
        $broker->dispatch('GET', '/v1/xp-awards', [], [], [], '192.0.2.30', $session);
        throw new RuntimeException('The protected XP awards route accepted an unauthenticated request.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 401 && $exception->errorName === 'authentication_required',
            'The protected XP awards route failed with the wrong unauthenticated response.');
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

    try {
        $broker->dispatch('GET', '/v1/quests', [], [], [], '192.0.2.30', $session);
        throw new RuntimeException('The protected quest route accepted an unauthenticated request.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 401 && $exception->errorName === 'authentication_required',
            'The protected quest route failed with the wrong unauthenticated response.');
    }

    try {
        $broker->dispatch('GET', '/v1/messages', [], [], [], '192.0.2.30', $session);
        throw new RuntimeException('The protected message route accepted an unauthenticated request.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 401 && $exception->errorName === 'authentication_required',
            'The protected message route failed with the wrong unauthenticated response.');
    }

    $wordCountSnapshot = [
        'schema_version' => 1,
        'observed_at' => gmdate(DATE_ATOM, time() - 120),
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
        routingAdminHeaders('PUT', '/v1/admin/word-counts', $wordCountSnapshot, $config['api']['admin_key']),
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
            routingAdminHeaders('PUT', '/v1/admin/word-counts', $invalidSnapshot, $config['api']['admin_key']),
            '192.0.2.30',
            $session);
        throw new RuntimeException('The word-count upload route accepted an invalid snapshot.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 400 && $exception->errorName === 'invalid_word_counts',
            'The word-count upload route rejected invalid data with the wrong response.');
    }

    $createdBody = [
        'character_name' => 'Routing Hero',
        'password' => 'routing password',
        'character_key' => 'routing',
        'role' => 'player',
    ];
    $created = $broker->dispatch(
        'POST',
        '/v1/admin/character-accounts',
        [],
        $createdBody,
        routingAdminHeaders('POST', '/v1/admin/character-accounts', $createdBody, $config['api']['admin_key']),
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

    $xpAwards = $broker->dispatch(
        'GET',
        '/v1/xp-awards',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert($xpAwards['status'] === 200, 'The protected XP awards route failed.');
    routingAssert(
        $xpAwards['body']['scope'] === 'character'
            && count($xpAwards['body']['progressions']) === 2,
        'The player XP awards response did not return the authorized progression group.');
    routingAssert(
        array_column($xpAwards['body']['progressions'], 'character_key') === [
            'routing-xp',
            'companion-xp',
        ],
        'The player XP awards response returned unauthorized progression data.');
    routingAssert(
        $xpAwards['body']['progressions'][0]['xp_to_next_level'] === 7655,
        'The XP awards response did not include the character TNL.');
    routingAssert(
        $xpAwards['body']['progressions'][1]['xp_to_next_level'] === null,
        'The XP awards response did not preserve unavailable TNL data.');
    routingAssert(
        !isset($xpAwards['body']['awards_directory'])
            && !isset($xpAwards['body']['file_name']),
        'The player XP awards response exposed private storage details.');

    $wordCounts = $broker->dispatch(
        'GET',
        '/v1/word-counts',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert($wordCounts['status'] === 200, 'The protected word-count route failed.');
    routingAssert($wordCountRefreshCount === 1, 'The word-count route did not refresh stale word-count data from source.');
    routingAssert($wordCounts['body']['wiki']['words'] === $currentWordCountSnapshot['wiki']['words'], 'The refreshed wiki word count was incorrect.');
    routingAssert($wordCounts['body']['ic']['words'] === $currentWordCountSnapshot['ic']['words'], 'The refreshed IC word count was incorrect.');
    routingAssert($wordCounts['body']['ooc']['words'] === $currentWordCountSnapshot['ooc']['words'], 'The refreshed OOC word count was incorrect.');
    routingAssert(
        $wordCounts['body']['observed_at'] === $currentWordCountSnapshot['observed_at'],
        'The refreshed word-count observation time was not applied from source.');

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

    $quests = $broker->dispatch(
        'GET',
        '/v1/quests',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert($quests['status'] === 200, 'The protected quest route failed.');
    $expectedQuestStatuses = [
        'find-jelenneth' => ['active', 'individual-or-party'],
        'three-items-for-nuanda' => ['completed', 'individual-or-party'],
        'k-r-k-caravan-run' => ['completed', 'party-only'],
        'plumb-lost-caverns' => ['available', 'party-only'],
        'reclaim-keep-on-borderlands' => ['available', 'party-only'],
        'construct-darkforest-fort' => ['available', 'individual-or-party'],
        'find-urvan-and-narinza' => ['active', 'individual-or-party'],
        'free-slaytonthorpe' => ['active', 'individual-or-party'],
        'investigate-cold-mouth' => ['available', 'party-only'],
        'unmask-surface-hand' => ['available', 'individual-or-party'],
        'cleanse-blightstone-pit' => ['available', 'party-only'],
        'trace-vanished-elven-holds' => ['available', 'individual-or-party'],
    ];
    $questsById = [];
    foreach ($quests['body']['quests'] as $quest) {
        $questsById[$quest['id']] = $quest;
    }
    routingAssert(
        $quests['body']['schema_version'] === 2
            && count($questsById) >= count($expectedQuestStatuses),
        'The quest route did not return the expected unlocked quests.');
    routingAssert(
        array_diff(array_keys($expectedQuestStatuses), array_keys($questsById)) === [],
        'The quest route omitted a required workflow fixture.');
    foreach ($expectedQuestStatuses as $questId => $expectedStatus) {
        $quest = $questsById[$questId];
        routingAssert(
            $quest['state'] === $expectedStatus[0]
                && $quest['visibility'] === $expectedStatus[1]
                && $quest['request_status'] === null,
            'A configured quest returned the wrong status tags.');
    }
    foreach ($quests['body']['quests'] as $quest) {
        routingAssert(
            !array_key_exists('gated-by', $quest)
                && !array_key_exists('gated_by', $quest)
                && !array_key_exists('unlocked-by', $quest)
                && !array_key_exists('unlocked_by', $quest),
            'The quest route exposed its authorization metadata.');
    }
    routingAssert(
        $quests['body']['status_values'] === [
            'individual-only',
            'party-only',
            'individual-or-party',
            'gated',
            'available',
            'active',
            'available (abandoned)',
            'completed',
            'withdrawn',
        ],
        'The quest route returned the wrong status vocabulary.');
    routingAssert(
        $quests['body']['request_status_values'] === ['pending', 'approved', 'denied']
            && $quests['body']['pending_requests'] === []
            && $quests['body']['notifications'] === [],
        'The player quest response returned invalid request metadata.');

    $playerMutationHeaders = [
        'origin' => 'https://example.test',
        'csrf-token' => $restored['body']['csrf_token'],
    ];
    $interest = $broker->dispatch(
        'POST',
        '/v1/quest-requests',
        [],
        ['quest_id' => 'plumb-lost-caverns'],
        $playerMutationHeaders,
        '192.0.2.30',
        $session);
    routingAssert(
        $interest['status'] === 201
            && $interest['body']['request']['status'] === 'pending'
            && $interest['body']['request']['quest_id'] === 'plumb-lost-caverns',
        'The player could not request an available quest.');
    $questRequestId = $interest['body']['request']['id'];
    routingAssert(
        is_string($questRequestId)
            && preg_match('/^[a-f0-9]{32}$/D', $questRequestId) === 1,
        'The quest request identifier was invalid.');

    try {
        $broker->dispatch(
            'POST',
            '/v1/quest-requests',
            [],
            ['quest_id' => 'map-kharaz-ankor-entrance'],
            $playerMutationHeaders,
            '192.0.2.30',
            $session);
        throw new RuntimeException('A player requested a prerequisite-locked quest.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 404 && $exception->errorName === 'quest_not_found',
            'A locked quest request failed with the wrong response.');
    }

    try {
        $broker->dispatch(
            'POST',
            '/v1/quest-requests/' . $questRequestId . '/decision',
            [],
            ['decision' => 'approved'],
            $playerMutationHeaders,
            '192.0.2.30',
            $session);
        throw new RuntimeException('A player account decided its own quest request.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 403
                && $exception->errorName === 'quest_decision_not_authorized',
            'A player quest decision failed with the wrong response.');
    }

    $createdDungeonMasterBody = [
        'character_name' => 'Dungeon Master',
        'password' => 'dungeon master routing password',
        'character_key' => 'dungeon-master',
        'role' => 'dm',
    ];
    $createdDungeonMaster = $broker->dispatch(
        'POST',
        '/v1/admin/character-accounts',
        [],
        $createdDungeonMasterBody,
        routingAdminHeaders('POST', '/v1/admin/character-accounts', $createdDungeonMasterBody, $config['api']['admin_key']),
        '192.0.2.30',
        $session);
    routingAssert(
        $createdDungeonMaster['status'] === 201,
        'The routing test could not create a Dungeon Master account.');

    $createdSecondPlayerBody = [
        'character_name' => 'Second Routing Hero',
        'password' => 'second routing password',
        'character_key' => 'second-routing-hero',
        'role' => 'player',
    ];
    $createdSecondPlayer = $broker->dispatch(
        'POST',
        '/v1/admin/character-accounts',
        [],
        $createdSecondPlayerBody,
        routingAdminHeaders('POST', '/v1/admin/character-accounts', $createdSecondPlayerBody, $config['api']['admin_key']),
        '192.0.2.30',
        $session);
    routingAssert(
        $createdSecondPlayer['status'] === 201,
        'The routing test could not create a second player account.');

    $dungeonMasterSession = [];
    $dungeonMasterLogin = $broker->dispatch(
        'POST',
        '/v1/login',
        [],
        [
            'character_name' => 'Dungeon Master',
            'password' => 'dungeon master routing password',
        ],
        ['origin' => 'https://example.test'],
        '192.0.2.31',
        $dungeonMasterSession);
    $dungeonMasterMutationHeaders = [
        'origin' => 'https://example.test',
        'csrf-token' => $dungeonMasterLogin['body']['csrf_token'],
    ];
    $dungeonMasterXpAwards = $broker->dispatch(
        'GET',
        '/v1/xp-awards',
        [],
        [],
        [],
        '192.0.2.31',
        $dungeonMasterSession);
    routingAssert(
        $dungeonMasterXpAwards['body']['scope'] === 'party'
            && count($dungeonMasterXpAwards['body']['progressions']) === 2,
        'The Dungeon Master did not receive every configured XP award progression.');
    $secondPlayerSession = [];
    $secondPlayerLogin = $broker->dispatch(
        'POST',
        '/v1/login',
        [],
        [
            'character_name' => 'Second Routing Hero',
            'password' => 'second routing password',
        ],
        ['origin' => 'https://example.test'],
        '192.0.2.32',
        $secondPlayerSession);
    $secondPlayerMutationHeaders = [
        'origin' => 'https://example.test',
        'csrf-token' => $secondPlayerLogin['body']['csrf_token'],
    ];
    try {
        $broker->dispatch(
            'GET',
            '/v1/xp-awards',
            [],
            [],
            [],
            '192.0.2.32',
            $secondPlayerSession);
        throw new RuntimeException('An unconfigured player received XP award data.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 403 && $exception->errorName === 'xp_awards_not_authorized',
            'An unconfigured player received the wrong XP award denial response.');
    }

    $messageForDungeonMaster = $broker->dispatch(
        'POST',
        '/v1/messages',
        [],
        [
            'recipient_role' => 'dm',
            'message' => 'A routing message for the Dungeon Master.',
        ],
        $playerMutationHeaders,
        '192.0.2.30',
        $session);
    $messageForDungeonMasterId = $messageForDungeonMaster['body']['message']['id'] ?? '';
    routingAssert(
        $messageForDungeonMaster['status'] === 201
            && preg_match('/^[a-f0-9]{32}$/D', (string)$messageForDungeonMasterId) === 1,
        'The player could not send a message to the Dungeon Master.');

    $dungeonMasterMessages = $broker->dispatch(
        'GET',
        '/v1/messages',
        [],
        [],
        [],
        '192.0.2.31',
        $dungeonMasterSession);
    routingAssert(
        $dungeonMasterMessages['status'] === 200
            && count($dungeonMasterMessages['body']['messages']) === 1
            && $dungeonMasterMessages['body']['schema_version'] === 2
            && count($dungeonMasterMessages['body']['player_recipients']) === 2
            && $dungeonMasterMessages['body']['messages'][0]['id'] === $messageForDungeonMasterId
            && $dungeonMasterMessages['body']['messages'][0]['sender_character_name'] === 'Routing Hero'
            && $dungeonMasterMessages['body']['messages'][0]['read_at'] === null,
        'The Dungeon Master did not receive the unread player message.');

    try {
        $broker->dispatch(
            'POST',
            '/v1/messages/' . $messageForDungeonMasterId . '/read',
            [],
            [],
            $playerMutationHeaders,
            '192.0.2.30',
            $session);
        throw new RuntimeException('A player marked another account message as read.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 404 && $exception->errorName === 'message_not_found',
            'Cross-account message acknowledgement failed with the wrong response.');
    }

    $readByDungeonMaster = $broker->dispatch(
        'POST',
        '/v1/messages/' . $messageForDungeonMasterId . '/read',
        [],
        [],
        $dungeonMasterMutationHeaders,
        '192.0.2.31',
        $dungeonMasterSession);
    routingAssert(
        $readByDungeonMaster['body']['message']['status'] === 'read',
        'The Dungeon Master could not mark the player message as read.');
    $dungeonMasterMessagesAfterRead = $broker->dispatch(
        'GET',
        '/v1/messages',
        [],
        [],
        [],
        '192.0.2.31',
        $dungeonMasterSession);
    routingAssert(
        $dungeonMasterMessagesAfterRead['body']['messages'] === [],
        'The read Dungeon Master message remained unread.');

    $playerRecipients = $broker->dispatch(
        'GET',
        '/v1/messages',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert(
        count($playerRecipients['body']['player_recipients']) === 1
            && $playerRecipients['body']['player_recipients'][0]['account_id']
                === $createdSecondPlayer['body']['id'],
        'A player did not receive the other available PC as a message recipient.');

    $messageForSecondPlayer = $broker->dispatch(
        'POST',
        '/v1/messages',
        [],
        [
            'recipient_account_id' => $createdSecondPlayer['body']['id'],
            'message' => 'A routing message from one player to another.',
        ],
        $playerMutationHeaders,
        '192.0.2.30',
        $session);
    $messageForSecondPlayerId = $messageForSecondPlayer['body']['message']['id'] ?? '';
    $secondPlayerMessages = $broker->dispatch(
        'GET',
        '/v1/messages',
        [],
        [],
        [],
        '192.0.2.32',
        $secondPlayerSession);
    routingAssert(
        $messageForSecondPlayer['status'] === 201
            && count($secondPlayerMessages['body']['messages']) === 1
            && $secondPlayerMessages['body']['messages'][0]['id'] === $messageForSecondPlayerId
            && $secondPlayerMessages['body']['messages'][0]['sender_character_name'] === 'Routing Hero',
        'The second player did not receive the unread player message.');
    $broker->dispatch(
        'POST',
        '/v1/messages/' . $messageForSecondPlayerId . '/read',
        [],
        [],
        $secondPlayerMutationHeaders,
        '192.0.2.32',
        $secondPlayerSession);

    $messageForPlayer = $broker->dispatch(
        'POST',
        '/v1/messages',
        [],
        [
            'recipient_account_id' => $created['body']['id'],
            'message' => 'A routing message for the player.',
        ],
        $dungeonMasterMutationHeaders,
        '192.0.2.31',
        $dungeonMasterSession);
    $messageForPlayerId = $messageForPlayer['body']['message']['id'] ?? '';
    routingAssert(
        $messageForPlayer['status'] === 201
            && preg_match('/^[a-f0-9]{32}$/D', (string)$messageForPlayerId) === 1,
        'The Dungeon Master could not send a message to the player.');

    $playerMessages = $broker->dispatch(
        'GET',
        '/v1/messages',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert(
        count($playerMessages['body']['messages']) === 1
            && $playerMessages['body']['messages'][0]['id'] === $messageForPlayerId
            && $playerMessages['body']['messages'][0]['sender_character_name'] === 'Dungeon Master'
            && $playerMessages['body']['messages'][0]['read_at'] === null,
        'The player did not receive the unread Dungeon Master message.');

    $readByPlayer = $broker->dispatch(
        'POST',
        '/v1/messages/' . $messageForPlayerId . '/read',
        [],
        [],
        $playerMutationHeaders,
        '192.0.2.30',
        $session);
    routingAssert(
        $readByPlayer['body']['message']['status'] === 'read',
        'The player could not mark the Dungeon Master message as read.');
    $playerMessagesAfterRead = $broker->dispatch(
        'GET',
        '/v1/messages',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert(
        $playerMessagesAfterRead['body']['messages'] === [],
        'The read player message remained unread.');

    $messageForEveryPlayer = $broker->dispatch(
        'POST',
        '/v1/messages',
        [],
        [
            'recipient_role' => 'all_players',
            'message' => 'A routing message for every player.',
        ],
        $dungeonMasterMutationHeaders,
        '192.0.2.31',
        $dungeonMasterSession);
    routingAssert(
        $messageForEveryPlayer['status'] === 201
            && $messageForEveryPlayer['body']['message']['recipient_count'] === 2,
        'The Dungeon Master could not message every player.');
    $firstPlayerBroadcast = $broker->dispatch(
        'GET',
        '/v1/messages',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    $secondPlayerBroadcast = $broker->dispatch(
        'GET',
        '/v1/messages',
        [],
        [],
        [],
        '192.0.2.32',
        $secondPlayerSession);
    routingAssert(
        count($firstPlayerBroadcast['body']['messages']) === 1
            && count($secondPlayerBroadcast['body']['messages']) === 1
            && $firstPlayerBroadcast['body']['messages'][0]['message']
                === 'A routing message for every player.'
            && $secondPlayerBroadcast['body']['messages'][0]['message']
                === 'A routing message for every player.',
        'The every-player message did not reach every player account.');

    $dungeonMasterQuests = $broker->dispatch(
        'GET',
        '/v1/quests',
        [],
        [],
        [],
        '192.0.2.31',
        $dungeonMasterSession);
    routingAssert(
        count($dungeonMasterQuests['body']['quests']) === $questFixtureCount
            && in_array('map-kharaz-ankor-entrance', array_column($dungeonMasterQuests['body']['quests'], 'id'), true)
            && in_array('keep-cult-from-valashinaz', array_column($dungeonMasterQuests['body']['quests'], 'id'), true)
            && count($dungeonMasterQuests['body']['pending_requests']) === 1
            && $dungeonMasterQuests['body']['pending_requests'][0]['id'] === $questRequestId
            && $dungeonMasterQuests['body']['pending_requests'][0]['requester_character_name']
                === 'Routing Hero',
        'The Dungeon Master did not receive the full quest list and pending quest alert.');

    try {
        $broker->dispatch(
            'POST',
            '/v1/quest-requests',
            [],
            ['quest_id' => 'reclaim-keep-on-borderlands'],
            $dungeonMasterMutationHeaders,
            '192.0.2.31',
            $dungeonMasterSession);
        throw new RuntimeException('The Dungeon Master requested a quest.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 403
                && $exception->errorName === 'quest_request_not_authorized',
            'The Dungeon Master quest-request restriction returned the wrong response.');
    }

    $approved = $broker->dispatch(
        'POST',
        '/v1/quest-requests/' . $questRequestId . '/decision',
        [],
        ['decision' => 'approved'],
        $dungeonMasterMutationHeaders,
        '192.0.2.31',
        $dungeonMasterSession);
    routingAssert(
        $approved['body']['request']['status'] === 'approved'
            && $approved['body']['quest_state'] === 'active',
        'Approving the quest request did not activate the quest.');

    $playerQuestsAfterApproval = $broker->dispatch(
        'GET',
        '/v1/quests',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    $approvedQuest = array_values(array_filter(
        $playerQuestsAfterApproval['body']['quests'],
        static fn(array $quest): bool => $quest['id'] === 'plumb-lost-caverns'))[0] ?? null;
    routingAssert(
        is_array($approvedQuest)
            && $approvedQuest['state'] === 'active'
            && $approvedQuest['request_status'] === 'approved'
            && count($playerQuestsAfterApproval['body']['notifications']) === 1
            && $playerQuestsAfterApproval['body']['notifications'][0]['status'] === 'approved',
        'The player did not receive the approval or active quest state.');

    $acknowledged = $broker->dispatch(
        'POST',
        '/v1/quest-requests/' . $questRequestId . '/acknowledge',
        [],
        [],
        $playerMutationHeaders,
        '192.0.2.30',
        $session);
    routingAssert(
        $acknowledged['body']['acknowledged'] === true,
        'The player could not dismiss the quest decision notification.');
    $afterAcknowledgement = $broker->dispatch(
        'GET',
        '/v1/quests',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert(
        $afterAcknowledgement['body']['notifications'] === [],
        'The dismissed quest notification remained unread.');

    try {
        $broker->dispatch(
            'POST',
            '/v1/quest-requests',
            [],
            ['quest_id' => 'plumb-lost-caverns'],
            $playerMutationHeaders,
            '192.0.2.30',
            $session);
        throw new RuntimeException('An active quest accepted another interest request.');
    } catch (BrokerHttpException $exception) {
        routingAssert(
            $exception->status === 409 && $exception->errorName === 'quest_not_available',
            'An active quest request failed with the wrong response.');
    }

    $denialInterest = $broker->dispatch(
        'POST',
        '/v1/quest-requests',
        [],
        ['quest_id' => 'reclaim-keep-on-borderlands'],
        $playerMutationHeaders,
        '192.0.2.30',
        $session);
    $denied = $broker->dispatch(
        'POST',
        '/v1/quest-requests/' . $denialInterest['body']['request']['id'] . '/decision',
        [],
        ['decision' => 'denied'],
        $dungeonMasterMutationHeaders,
        '192.0.2.31',
        $dungeonMasterSession);
    routingAssert(
        $denied['body']['request']['status'] === 'denied'
            && $denied['body']['quest_state'] === 'available',
        'Denying a quest request changed the quest lifecycle state.');
    $playerQuestsAfterDenial = $broker->dispatch(
        'GET',
        '/v1/quests',
        [],
        [],
        [],
        '192.0.2.30',
        $session);
    routingAssert(
        count($playerQuestsAfterDenial['body']['notifications']) === 1
            && $playerQuestsAfterDenial['body']['notifications'][0]['status'] === 'denied'
            && $playerQuestsAfterDenial['body']['notifications'][0]['quest_id']
                === 'reclaim-keep-on-borderlands',
        'The player did not receive the quest denial notification.');

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
    routingAssert(
        $health['body']['schema_version'] === 7
            && $health['body']['status'] === 'ok'
            && !isset($health['body']['character_account_count']),
        'The public health route disclosed operational details.');
    $finalAdminHealth = $broker->dispatch(
        'GET',
        '/v1/admin/health',
        [],
        [],
        routingAdminHeaders('GET', '/v1/admin/health', [], $config['api']['admin_key']),
        '192.0.2.30',
        $session);
    routingAssert(
        $finalAdminHealth['body']['schema_version'] === 7
            && $finalAdminHealth['body']['character_account_count'] === 3
            && $finalAdminHealth['body']['xp_tracking_configured'] === true
            && $finalAdminHealth['body']['quest_request_workflow_configured'] === true,
        'The admin health route readiness state was incorrect.');

    fwrite(STDOUT, "Broker authentication routing tests passed.\n");
} finally {
    @unlink($databasePath);
    @unlink($wordCountStatusPath);
    foreach (glob($wordCountStatusPath . '.tmp-*') ?: [] as $statusTemporaryFile) {
        @unlink($statusTemporaryFile);
    }
    if (is_dir($snapshotDirectory)) {
        foreach (glob($snapshotDirectory . '/*') ?: [] as $snapshotFile) {
            @unlink($snapshotFile);
        }
        @rmdir($snapshotDirectory);
    }
    if (is_dir($xpAwardsDirectory)) {
        foreach (glob($xpAwardsDirectory . '/*') ?: [] as $xpAwardsFile) {
            @unlink($xpAwardsFile);
        }
        @rmdir($xpAwardsDirectory);
    }
}
