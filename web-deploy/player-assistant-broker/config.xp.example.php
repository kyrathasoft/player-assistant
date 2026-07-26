<?php

declare(strict_types=1);

/*
 * Merge this `xp` section into the private production config.php array.
 * The source URLs remain server-side and are never returned by the API.
 */
return [
    'xp' => [
        'source_url' => 'https://publish.obsidian.md/example/XP',
        'character_source_url' => 'https://publish.obsidian.md/example/PCs/Player+Characters+Listing',
        'class_progression_index_url' => 'https://publish.obsidian.md/example/Classes/Class+Level+Progression',
        'connect_timeout_seconds' => 3,
        'timeout_seconds' => 8,
        'maximum_response_bytes' => 524288,
        'cache_ttl_seconds' => 300,
        'maximum_stale_seconds' => 86400,
    ],
];
