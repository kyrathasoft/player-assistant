<?php

declare(strict_types=1);

$databasePath = getenv('PLAYER_ASSISTANT_TEST_DATABASE');
$snapshotDirectory = getenv('PLAYER_ASSISTANT_TEST_SNAPSHOTS');
if (!is_string($databasePath) || $databasePath === ''
    || !is_string($snapshotDirectory) || $snapshotDirectory === '') {
    throw new RuntimeException('The HTTP integration test paths are not configured.');
}

return [
    'api' => [
        'base_path' => '/scarlethorizons/api',
        'database_path' => $databasePath,
        'admin_key' => 'http-test-administrator-key',
        'default_token_lifetime_days' => 1,
        'max_token_lifetime_days' => 30,
        'requests_per_minute' => 10,
        'snapshot_directory' => $snapshotDirectory,
        'snapshot_signing_key' => base64_encode(str_repeat('s', 32)),
        'protected_response' => [
            'key_id' => 'protected-http-test-2026',
            'signing_key' => base64_encode(str_repeat('p', 64)),
            'public_key' => 'cHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHA=',
        ],
    ],
    'auth' => [
        'expected_origin' => 'https://example.test',
        'cookie_path' => '/scarlethorizons/api/',
        'idle_timeout_seconds' => 60,
        'absolute_timeout_seconds' => 600,
        'login_window_seconds' => 300,
        'login_max_failures' => 3,
        'login_lockout_seconds' => 300,
    ],
    'rpol' => [
        'username' => 'unused-test-user',
        'password' => 'unused-test-password',
        'game_id' => '80170',
    ],
];
