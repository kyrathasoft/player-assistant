<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/RpolClient.php';
require_once __DIR__ . '/../player-assistant-broker/CharacterAuthService.php';
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
        'rpol' => [
            'username' => 'unused',
            'password' => 'unused',
            'game_id' => '80170',
        ],
    ];
    $broker = new BrokerService($config, new RpolClient($config['rpol']));
    $session = [];
    $adminHeaders = ['admin-key' => $config['api']['admin_key']];

    $created = $broker->dispatch(
        'POST',
        '/v1/admin/character-accounts',
        [],
        [
            'character_name' => 'Routing Hero',
            'password' => 'routing password',
            'character_key' => 'routing-hero',
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
        $identity['body']['account']['character_key'] === 'routing-hero',
        'The protected identity route did not use the session account.');

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

    fwrite(STDOUT, "Broker authentication routing tests passed.\n");
} finally {
    @unlink($databasePath);
    if (is_dir($snapshotDirectory)) {
        @rmdir($snapshotDirectory);
    }
}
