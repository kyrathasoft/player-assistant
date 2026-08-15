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
        // Retained as the compatibility default for both delay caps below.
        'login_lockout_seconds' => 900,
        'login_progressive_delay_base_seconds' => 2,
        'login_progressive_delay_max_seconds' => 900,
        // This address-wide threshold must remain higher than login_max_failures.
        'login_address_max_failures' => 20,
        'login_address_delay_seconds' => 900,
        'audit_retention_seconds' => 7776000,
        'audit_address_mode' => 'hash',
        // Optional stable secret for audit-address pseudonymization.
        'audit_address_hash_key' => 'CHANGE_ME_TO_A_RANDOM_SECRET',
    ],
];
