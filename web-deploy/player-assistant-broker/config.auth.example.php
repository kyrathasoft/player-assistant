<?php

declare(strict_types=1);

/*
 * Merge this `auth` section into the private production config.php array.
 * config.php, broker.sqlite, password hashes, and session files must remain
 * outside the website document root and must never be committed.
 */
return [
    'auth' => [
        'expected_origin' => 'https://bryanmiller.us',
        'cookie_path' => '/scarlethorizons/api/',
        'idle_timeout_seconds' => 1800,
        'absolute_timeout_seconds' => 28800,
        'login_window_seconds' => 900,
        'login_max_failures' => 5,
        'login_lockout_seconds' => 900,
        'audit_retention_seconds' => 7776000,
        'audit_address_mode' => 'hash',
        // Optional stable secret for audit-address pseudonymization.
        'audit_address_hash_key' => 'CHANGE_ME_TO_A_RANDOM_SECRET',
    ],
    'magic_items' => [
        // Keep this schema-v2 file outside the public document root.
        'source_path' => '/home/dh_4gg2za/player-assistant-broker/magic-items.json',
    ],
];
