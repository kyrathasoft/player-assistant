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
    ],
];
