<?php

declare(strict_types=1);

/*
 * Merge this section into the private production config.php. The real character
 * name and password are installed from repository secrets and must never be
 * committed. Keep the status path outside the public document root.
 */
return [
    'pwa_monitor' => [
        'base_url' => 'https://bryanmiller.us/scarlethorizons',
        'character_name' => 'replace-with-dedicated-monitor-account',
        'password' => 'replace-with-private-monitor-password',
        'status_path' => '/home/dh_4gg2za/player-assistant-broker/pwa-monitor-status.json',
        'maximum_xp_age_seconds' => 86400,
        'maximum_word_count_age_seconds' => 604800,
        'alert_cooldown_seconds' => 3600,
        'alert_email' => 'replace-with-operations-alert@example.com',
        'alert_from' => 'player-assistant@bryanmiller.us',
    ],
];
