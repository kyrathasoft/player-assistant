<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerOperations.php';

function backupFilenameAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$valid = 'broker-20260830T191143Z-a1b2c3d4.sqlite';
BrokerBackupPathValidator::assertProducerBasename($valid);

foreach ([
    '../' . $valid,
    '/tmp/' . $valid,
    'broker-20260830T191143Z-a1b2c3d4.sqlite/evil',
    'broker-20260830T191143Z-a1b2c3d4.sqlite.php',
    'broker-20260830T191143Z-a1b2c3d5.sqlitе',
    'broker-20260830T19114Z-a1b2c3d4.sqlite',
] as $malformed) {
    try {
        BrokerBackupPathValidator::assertProducerBasename($malformed);
        throw new RuntimeException("Malformed backup basename was accepted: $malformed");
    } catch (RuntimeException $exception) {
        backupFilenameAssert(
            str_contains($exception->getMessage(), 'not a valid producer basename'),
            'Malformed backup rejection returned the wrong diagnostic.');
    }
}

$root = sys_get_temp_dir() . '/pa-backup-name-' . bin2hex(random_bytes(4));
mkdir($root, 0700, true);
try {
    $path = $root . '/' . $valid;
    file_put_contents($path, 'fixture');
    BrokerBackupPathValidator::assertApprovedPath($path, $root);
    $outside = dirname($root) . '/' . $valid;
    file_put_contents($outside, 'outside');
    try {
        BrokerBackupPathValidator::assertApprovedPath($outside, $root);
        throw new RuntimeException('A backup outside the approved root was accepted.');
    } catch (RuntimeException $exception) {
        backupFilenameAssert(
            str_contains($exception->getMessage(), 'outside the approved root'),
            'Out-of-root backup rejection returned the wrong diagnostic.');
    } finally {
        @unlink($outside);
    }
} finally {
    @unlink($root . '/' . $valid);
    @rmdir($root);
}

echo "Broker backup filename tests passed.\n";
