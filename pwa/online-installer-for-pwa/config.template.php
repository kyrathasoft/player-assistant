<?php

declare(strict_types=1);

/*
 * Copy this file outside the target website document root, replace every
 * CHANGE_ME value, and pass it to install-player-assistant-web.php through
 * --config-source. Leave __TARGET_ORIGIN__, __PRIVATE_ROOT__, and
 * __ACCOUNT_HOME__ unchanged; the installer replaces only those declared
 * placeholders after validating its target arguments.
 */
return [
    'api' => [
        'base_path' => '/scarlethorizons/api',
        'database_path' => '__PRIVATE_ROOT__/broker.sqlite',
        'admin_key' => 'CHANGE_ME_RANDOM_ADMIN_KEY_AT_LEAST_32_CHARACTERS',
        'default_token_lifetime_days' => 30,
        'max_token_lifetime_days' => 365,
        'requests_per_minute' => 60,
        'snapshot_directory' => '__PRIVATE_ROOT__/snapshots',
        'snapshot_signing_key' => 'CHANGE_ME_BASE64_32_BYTE_SIGNING_KEY',
        'snapshot_max_age_seconds' => 86400,
        'snapshot_retention_seconds' => 2592000,
    ],
    'auth' => [
        'expected_origin' => '__TARGET_ORIGIN__',
        'cookie_path' => '/scarlethorizons/api/',
        'idle_timeout_seconds' => 1800,
        'absolute_timeout_seconds' => 28800,
        'login_window_seconds' => 900,
        'login_max_failures' => 5,
        'login_lockout_seconds' => 900,
        'login_progressive_delay_base_seconds' => 2,
        'login_progressive_delay_max_seconds' => 900,
        'login_address_max_failures' => 20,
        'login_address_delay_seconds' => 900,
        'audit_retention_seconds' => 7776000,
        'audit_address_mode' => 'hash',
        'audit_address_hash_key' => 'CHANGE_ME_RANDOM_AUDIT_HASH_KEY',
    ],
    'rpol' => [
        'username' => 'CHANGE_ME_RPOL_USERNAME',
        'password' => 'CHANGE_ME_RPOL_PASSWORD',
        'game_id' => 'CHANGE_ME_RPOL_GAME_ID',
    ],
    'xp' => [
        'source_url' => 'CHANGE_ME_HTTPS_XP_TRACKING_URL',
        'character_source_url' => 'CHANGE_ME_HTTPS_CHARACTER_LISTING_URL',
        'class_progression_index_url' => 'CHANGE_ME_HTTPS_CLASS_PROGRESSION_URL',
        'connect_timeout_seconds' => 3,
        'timeout_seconds' => 8,
        'maximum_response_bytes' => 524288,
        'maximum_stale_seconds' => 86400,
        'awards_directory' => '__PRIVATE_ROOT__/xp-awards',
        'award_groups' => [],
    ],
    'word_counts' => [
        'source_url' => 'CHANGE_ME_HTTPS_WORD_COUNT_SOURCE_URL',
        'connect_timeout_seconds' => 5,
        'timeout_seconds' => 15,
        'maximum_response_bytes' => 1048576,
        'maximum_stale_seconds' => 604800,
        'status_path' => '__PRIVATE_ROOT__/word-count-refresh-status.json',
        'signature_key_id' => 'CHANGE_ME_WORD_COUNT_KEY_ID',
        'signature_public_key' => 'CHANGE_ME_BASE64_ED25519_PUBLIC_KEY',
    ],
    'operations' => [
        'backup_directory' => '__PRIVATE_ROOT__/broker-backups',
        'restore_test_directory' => '__PRIVATE_ROOT__/broker-restore-tests',
        'status_path' => '__PRIVATE_ROOT__/broker-operations-status.json',
        'environment_file' => '__ACCOUNT_HOME__/.player-assistant-ftps.env',
        'retention_count' => 14,
        'server_error_threshold' => 5,
        'server_error_window_seconds' => 900,
        'alert_cooldown_seconds' => 3600,
        'alert_email' => 'CHANGE_ME_ALERT_RECIPIENT',
        'alert_from' => 'CHANGE_ME_SENDER_ON_TARGET_DOMAIN',
        'offsite' => [
            'transport' => 'ftps',
            'port' => 21,
        ],
    ],
    'database_recovery' => [
        'backup_directory' => '__PRIVATE_ROOT__/broker-backups',
        'status_path' => '__PRIVATE_ROOT__/broker-recovery-status.json',
        'health_url' => '__TARGET_ORIGIN__/scarlethorizons/api/v1/health',
        'retention_count' => 14,
    ],
    'observability' => [
        'alert_email' => 'CHANGE_ME_ALERT_RECIPIENT',
        'alert_from' => 'CHANGE_ME_SENDER_ON_TARGET_DOMAIN',
        'alert_cooldown_seconds' => 3600,
        'server_error_threshold' => 5,
        'server_error_window_seconds' => 900,
    ],
    'pwa_monitor' => [
        'base_url' => '__TARGET_ORIGIN__/scarlethorizons',
        'character_name' => 'CHANGE_ME_MONITOR_CHARACTER_NAME',
        'password' => 'CHANGE_ME_MONITOR_CHARACTER_PASSWORD',
        'status_path' => '__PRIVATE_ROOT__/pwa-monitor-status.json',
        'maximum_xp_age_seconds' => 86400,
        'maximum_word_count_age_seconds' => 604800,
        'alert_cooldown_seconds' => 3600,
        'alert_email' => 'CHANGE_ME_ALERT_RECIPIENT',
        'alert_from' => 'CHANGE_ME_SENDER_ON_TARGET_DOMAIN',
    ],
];
