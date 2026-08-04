<?php

/*
 * Optional section for the private production config.php.
 * Keep the backup directory and status path outside the website document root.
 */
return [
    'database_recovery' => [
        'backup_directory' => '/home/DREAMHOST_USER/player-assistant-broker/backups',
        'status_path' => '/home/DREAMHOST_USER/player-assistant-broker/broker-recovery-status.json',
        'retention_count' => 14,
        'health_url' => 'https://bryanmiller.us/scarlethorizons/api/v1/health',
        'alert_email' => 'alerts@example.invalid',
    ],
];
