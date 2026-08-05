<?php

declare(strict_types=1);

/*
 * Merge this `operations` section into the private production config.php.
 * Keep every local path outside the website document root. BrokerOperations reads
 * BACKUP_FTPS_HOST, BACKUP_FTPS_PORT, BACKUP_FTPS_USERNAME,
 * BACKUP_FTPS_PASSWORD, and BACKUP_FTPS_REMOTE_PATH at runtime so deployment never
 * serializes those values into config.php or its retained backups.
 */
return [
    'operations' => [
        'backup_directory' => '/home/dh_4gg2za/player-assistant-broker/broker-backups',
        'restore_test_directory' => '/home/dh_4gg2za/player-assistant-broker/broker-restore-tests',
        'status_path' => '/home/dh_4gg2za/player-assistant-broker/broker-operations-status.json',
        'retention_count' => 14,
        'server_error_threshold' => 5,
        'server_error_window_seconds' => 900,
        'alert_cooldown_seconds' => 3600,
        'alert_email' => 'replace-with-operations-alert@example.com',
        'alert_from' => 'player-assistant-broker@example.com',
        'offsite' => [
            'transport' => 'ftps',
            'port' => 21,
        ],
    ],
];
