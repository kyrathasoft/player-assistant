<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/MagicItemService.php';

function magicItemAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$sourcePath = tempnam(sys_get_temp_dir(), 'pa-magic-items-');
if ($sourcePath === false) {
    throw new RuntimeException('Unable to create the magic-item source fixture.');
}
$ownerId = str_repeat('a', 32);
$otherId = str_repeat('b', 32);
try {
    $item = static function (string $name, string $viewers): array {
        return [
            'name' => $name,
            'description' => 'A protected fixture item.',
            'date-acquired' => '7.31.2026',
            'meta-date-acquired' => '07/31/2026',
            'longevity' => 'permanent',
            'provenance' => 'Synthetic fixture.',
            'whereabouts' => 'Fixture Hero',
            'viewable-by' => $viewers,
        ];
    };
    file_put_contents($sourcePath, json_encode([
        'schema_version' => 2,
        'source' => 'private-test-source',
        'items' => [
            $item('Owner Item', $ownerId),
            $item('Other Item', $otherId),
            $item('Public Item', 'all'),
            $item('Substring Collision', substr($ownerId, 0, 31) . '0'),
        ],
    ], JSON_THROW_ON_ERROR));

    $service = new MagicItemService($sourcePath);
    $ownerItems = $service->forAccount(['id' => $ownerId]);
    $ownerNames = array_column($ownerItems['items'], 'name');
    magicItemAssert(in_array('Owner Item', $ownerNames, true), 'The exact owner ID did not receive its item.');
    magicItemAssert(in_array('Public Item', $ownerNames, true), 'The public item was not returned.');
    magicItemAssert(!in_array('Other Item', $ownerNames, true), 'Another account received a protected item.');
    magicItemAssert(!in_array('Substring Collision', $ownerNames, true), 'Substring matching granted protected access.');
    magicItemAssert(
        $ownerItems['items'][0]['viewable-by'] === 'all',
        'The broker leaked private authorization metadata in the response.');

    $otherItems = $service->forAccount(['id' => $otherId]);
    $otherNames = array_column($otherItems['items'], 'name');
    magicItemAssert(in_array('Other Item', $otherNames, true), 'The second exact owner ID did not receive its item.');
    magicItemAssert(!in_array('Owner Item', $otherNames, true), 'Account switching leaked the first account item.');

    $dmItems = $service->forAccount(['id' => str_repeat('d', 32), 'role' => 'dm']);
    $dmNames = array_column($dmItems['items'], 'name');
    magicItemAssert(count($dmNames) === 4, 'The Dungeon Master did not receive all public and protected magic items.');
    magicItemAssert(in_array('Owner Item', $dmNames, true) && in_array('Other Item', $dmNames, true), 'The Dungeon Master did not receive protected magic items.');
    magicItemAssert(in_array('Substring Collision', $dmNames, true), 'The Dungeon Master did not receive every valid magic-item record.');

    file_put_contents($sourcePath, json_encode([
        'schema_version' => 2,
        'source' => 'private-test-source',
        'items' => [$item('Invalid Viewer', 'fixture hero')],
    ], JSON_THROW_ON_ERROR));
    $rejected = false;
    try {
        $service->forAccount(['id' => $ownerId]);
    } catch (RuntimeException $exception) {
        $rejected = true;
        magicItemAssert(
            str_contains($exception->getMessage(), 'canonical account IDs'),
            'The invalid viewer failed for the wrong reason.');
    }
    magicItemAssert($rejected, 'A name-based private viewer was accepted.');
} finally {
    @unlink($sourcePath);
}

fwrite(STDOUT, "Magic-item authorization tests passed.\n");
