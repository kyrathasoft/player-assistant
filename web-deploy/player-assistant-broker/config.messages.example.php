<?php

declare(strict_types=1);

/* Merge this optional section into the private production config.php. */
return [
    'messages' => [
        // Read messages older than this are deleted after a successful acknowledgement.
        'retention_days' => 90,
        // Keep at most this many recent read messages per recipient. Unread messages are never pruned.
        'max_read_messages_per_account' => 500,
    ],
];
