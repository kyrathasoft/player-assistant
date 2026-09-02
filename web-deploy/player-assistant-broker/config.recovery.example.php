<?php

/*
 * Optional section for the private production config.php.
 * Keep the backup directory and status path outside the website document root.
 */
return [
    'migrations' => [
        'backup_directory' => '/home/DREAMHOST_USER/player-assistant-broker/migration-backups',
    ],
    'observability' => [
        'alert_email' => 'alerts@example.invalid',
        'from_email' => 'player-assistant@example.invalid',
        'server_error_threshold' => 3,
        'server_error_window_seconds' => 900,
        'refresh_failure_threshold' => 1,
        'health_failure_threshold' => 1,
        'alert_cooldown_seconds' => 3600,
    ],
    'database_recovery' => [
        'backup_directory' => '/home/DREAMHOST_USER/player-assistant-broker/backups',
        'status_path' => '/home/DREAMHOST_USER/player-assistant-broker/broker-recovery-status.json',
        'retention_count' => 14,
        'retention_seconds' => 1209600,
        'health_url' => 'https://bryanmiller.us/scarlethorizons/api/v1/health',
        'alert_email' => 'alerts@example.invalid',
    ],
];
