<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/RpolClient.php';
require_once __DIR__ . '/../player-assistant-broker/CharacterAuthService.php';
require_once __DIR__ . '/../player-assistant-broker/XpTrackingService.php';
require_once __DIR__ . '/../player-assistant-broker/BrokerService.php';

function routingAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$databasePath = tempnam(sys_get_temp_dir(), 'pa-broker-route-');
$snapshotDirectory = sys_get_temp_dir() . '/pa-broker-snapshots-' . bin2hex(random_bytes(6));
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
            'snapshot_signing_key' => base64_encode(random_bytes(32)),
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
        static fn(string $url): string => implode("\n", [
            'As of 7.23.2026',
            '',
            '| Name | XP Total |',
            '| --- | ---: |',
            '| Routing Hero | 12,345 |',
            '| Another Hero | 98,765 |',
        ]));
    $session = [];
    $adminHeaders = ['admin-key' => $config['api']['admin_key']];
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
    routingAssert(!isset($xp['body']['characters']), 'The player XP response exposed party totals.');
    routingAssert(!isset($xp['body']['source_url']), 'The player XP response exposed the configured source URL.');

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
    routingAssert($health['body']['schema_version'] === 2, 'The broker schema version was not advanced.');
    routingAssert($health['body']['character_account_count'] === 1, 'The health route account count was incorrect.');
    routingAssert($health['body']['xp_tracking_configured'] === true, 'The health route XP configuration state was incorrect.');

    fwrite(STDOUT, "Broker authentication routing tests passed.\n");
} finally {
    @unlink($databasePath);
    if (is_dir($snapshotDirectory)) {
        @rmdir($snapshotDirectory);
    }
}
