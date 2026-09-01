<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerOperations.php';

function restorePointAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

function restorePointFixture(string $directory, string $name, string $createdAt, string $sourceHash = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', string $restoreTest = 'ok'): string
{
    $path = $directory . '/' . $name;
    file_put_contents($path, 'isolated restore fixture ' . $name, LOCK_EX);
    $metadata = [
        'schema_version' => 2,
        'created_at' => $createdAt,
        'file' => $name,
        'bytes' => filesize($path),
        'sha256' => hash_file('sha256', $path),
        'source_sha256' => $sourceHash,
        'retention_class' => 'standard',
        'restore_test' => $restoreTest,
        'expires_at' => (new DateTimeImmutable($createdAt))->modify('+7200 seconds')->format(DATE_ATOM),
    ];
    file_put_contents($path . '.json', json_encode($metadata, JSON_THROW_ON_ERROR), LOCK_EX);
    return $path;
}

$root = sys_get_temp_dir() . '/pa-restore-point-' . bin2hex(random_bytes(6));
mkdir($root, 0700, true);
$now = new DateTimeImmutable('2026-08-31T12:00:00+00:00');
try {
    $old = restorePointFixture($root, 'broker-20260831T090000Z-aaaaaaaa.sqlite', '2026-08-31T09:00:00+00:00');
    $new = restorePointFixture($root, 'broker-20260831T110000Z-bbbbbbbb.sqlite', '2026-08-31T11:00:00+00:00');
    $selected = BrokerRestorePointSelector::select($root, $now, 7200);
    restorePointAssert($selected['file'] === basename($new), 'The newest verified non-expired restore point was not selected.');
    restorePointAssert($selected['expired'] === false, 'A restore point at the retention boundary was selected as valid.');

    $incomplete = $root . '/broker-20260831T114500Z-ffffffff.sqlite';
    file_put_contents($incomplete, 'partial transfer', LOCK_EX);
    try {
        BrokerRestorePointSelector::select($root, $now, 7200);
        throw new RuntimeException('Incomplete restore point was accepted.');
    } catch (RuntimeException $error) {
        restorePointAssert(str_contains($error->getMessage(), 'incomplete'), 'Incomplete restore-point diagnostic was not explicit.');
    }
    unlink($incomplete);

    file_put_contents($new, 'tampered', LOCK_EX);
    try {
        BrokerRestorePointSelector::select($root, $now, 7200);
        throw new RuntimeException('Hash-mismatched restore point was accepted.');
    } catch (RuntimeException $error) {
        restorePointAssert(str_contains($error->getMessage(), 'hash'), 'Hash mismatch diagnostic was not explicit.');
    }
    file_put_contents($new, 'isolated restore fixture ' . basename($new), LOCK_EX);

    $duplicate = restorePointFixture($root, 'broker-20260831T113000Z-cccccccc.sqlite', '2026-08-31T11:00:00+00:00');
    try {
        BrokerRestorePointSelector::select($root, $now, 7200);
        throw new RuntimeException('Duplicate restore timestamps were accepted.');
    } catch (RuntimeException $error) {
        restorePointAssert(str_contains($error->getMessage(), 'ambiguous'), 'Duplicate timestamp diagnostic was not explicit.');
    }
    unlink($duplicate . '.json');
    unlink($duplicate);

    $expired = restorePointFixture($root, 'broker-20260830T100000Z-dddddddd.sqlite', '2026-08-30T10:00:00+00:00');
    try {
        BrokerRestorePointSelector::verify($expired, $root, $now, 7200);
        throw new RuntimeException('Expired restore point was reported as verified.');
    } catch (RuntimeException) {
        // Verification remains valid metadata-wise; selection is responsible for expiration rejection.
    }
    unlink($expired . '.json');
    unlink($expired);

    $wrongGeneration = restorePointFixture($root, 'broker-20260831T114000Z-eeeeeeee.sqlite', '2026-08-31T11:40:00+00:00', str_repeat('b', 64));
    try {
        BrokerRestorePointSelector::select($root, $now, 7200, str_repeat('a', 64));
        throw new RuntimeException('Wrong-generation restore point was accepted.');
    } catch (RuntimeException $error) {
        restorePointAssert(str_contains($error->getMessage(), 'generation'), 'Wrong-generation diagnostic was not explicit.');
    }
    unlink($wrongGeneration . '.json');
    unlink($wrongGeneration);
    unlink($old . '.json');
    unlink($old);
    unlink($new . '.json');
    unlink($new);
    echo "Broker restore-point selection tests passed.\n";
} finally {
    if (is_dir($root)) {
        foreach (glob($root . '/*') ?: [] as $path) {
            @unlink($path);
        }
        @rmdir($root);
    }
}
