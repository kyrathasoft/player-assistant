<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerOperations.php';

putenv('BACKUP_ENCRYPTION_KEY=test-backup-encryption-key-with-sufficient-entropy');

function ftpsAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

/**
 * Run a fixture with explicit FTPS environment values and restore the caller's environment afterward.
 * A null value means the variable must be absent for the duration of the fixture.
 */
function withFtpsEnvironment(array $values, callable $callback): void
{
    $originalValues = [];
    foreach (array_keys($values) as $name) {
        $originalValues[$name] = getenv($name);
        $value = $values[$name];
        putenv($value === null ? $name : $name . '=' . $value);
    }

    try {
        $callback();
    } finally {
        foreach ($originalValues as $name => $value) {
            putenv($value === false ? $name : $name . '=' . $value);
        }
    }
}

$ftpsEnvironmentNames = [
    'BACKUP_FTPS_HOST',
    'BACKUP_FTPS_PORT',
    'BACKUP_FTPS_USERNAME',
    'BACKUP_FTPS_PASSWORD',
    'BACKUP_FTPS_REMOTE_PATH',
];
$callerFtpsEnvironment = [];
foreach ($ftpsEnvironmentNames as $name) {
    $callerFtpsEnvironment[$name] = getenv($name);
    putenv($name);
}
register_shutdown_function(static function () use ($callerFtpsEnvironment): void {
    foreach ($callerFtpsEnvironment as $name => $value) {
        putenv($value === false ? $name : $name . '=' . $value);
    }
});

final class InMemoryBrokerFtpsClient implements BrokerFtpsClient
{
    public bool $corruptDownloads = false;
    public string $failUploadsContaining = '';
    public array $downloads = [];
    public array $files = [];

    public function upload(string $localPath, string $remotePath): void
    {
        if ($this->failUploadsContaining !== '' && str_contains($remotePath, $this->failUploadsContaining)) {
            throw new RuntimeException('Simulated FTPS upload failure.');
        }
        $this->files[$remotePath] = (string)file_get_contents($localPath);
    }

    public function download(string $remotePath, string $localPath): void
    {
        $this->downloads[] = $remotePath;
        $content = $this->files[$remotePath];
        file_put_contents($localPath, $this->corruptDownloads ? $content . '-corrupt' : $content);
    }

    public function rename(string $sourcePath, string $destinationPath): void
    {
        $this->files[$destinationPath] = $this->files[$sourcePath];
        unset($this->files[$sourcePath]);
    }

    public function delete(string $remotePath): void
    {
        unset($this->files[$remotePath]);
    }

    public function listFiles(string $directory): array
    {
        $prefix = rtrim($directory, '/') . '/';
        return array_values(array_map(
            'basename',
            array_filter(array_keys($this->files), static fn(string $path): bool => str_starts_with($path, $prefix))));
    }
}

$operations = new BrokerOperations([
    'api' => ['database_path' => '/private/broker.sqlite'],
    'operations' => [
        'backup_directory' => '/private/broker-backups',
        'restore_test_directory' => '/private/broker-restore-tests',
        'status_path' => '',
        'offsite' => [
            'transport' => 'ftps',
            'host' => 'backup.example.com',
            'port' => 21,
            'username' => 'backup-user',
            'password' => 'test-only-password',
            'directory' => 'htdocs/scarlet-horizons/pwa-backups',
        ],
    ],
]);

ftpsAssert(
    $operations->healthStatus()['offsite_backup_configured'] === true,
    'A complete FTPS destination was not reported as configured.');

$environmentValues = [
    'BACKUP_FTPS_HOST' => 'environment-backup.example.com',
    'BACKUP_FTPS_PORT' => '21',
    'BACKUP_FTPS_USERNAME' => 'environment-user',
    'BACKUP_FTPS_PASSWORD' => 'environment-password',
    'BACKUP_FTPS_REMOTE_PATH' => '/account-root/private-backups',
    'BACKUP_ENCRYPTION_KEY' => 'environment-backup-encryption-key-with-sufficient-entropy',
];
withFtpsEnvironment($environmentValues, static function (): void {
    $environmentOperations = new BrokerOperations([
        'api' => ['database_path' => '/private/broker.sqlite'],
        'operations' => ['offsite' => ['transport' => 'ftps']],
    ]);
    ftpsAssert(
        $environmentOperations->healthStatus()['offsite_backup_configured'] === true,
        'FTPS credentials supplied only through the process environment were not recognized.');
});

$invalidPortConfig = [
    'api' => ['database_path' => '/private/broker.sqlite'],
    'operations' => [
        'offsite' => [
            'transport' => 'ftps',
            'host' => 'backup.example.com',
            'port' => 70000,
            'username' => 'backup-user',
            'password' => 'test-only-password',
            'directory' => 'private-backups',
        ],
    ],
];

withFtpsEnvironment([
    'BACKUP_FTPS_HOST' => null,
    'BACKUP_FTPS_PORT' => null,
    'BACKUP_FTPS_USERNAME' => null,
    'BACKUP_FTPS_PASSWORD' => null,
    'BACKUP_FTPS_REMOTE_PATH' => null,
], static function () use ($invalidPortConfig): void {
    $invalidPortOperations = new BrokerOperations($invalidPortConfig);
    ftpsAssert(
        $invalidPortOperations->healthStatus()['offsite_backup_configured'] === false,
        'An out-of-range FTPS port was reported as configured in a clean environment.');
});

withFtpsEnvironment([
    'BACKUP_FTPS_HOST' => 'environment-backup.example.com',
    'BACKUP_FTPS_PORT' => '21',
    'BACKUP_FTPS_USERNAME' => 'environment-user',
    'BACKUP_FTPS_PASSWORD' => 'environment-password',
    'BACKUP_FTPS_REMOTE_PATH' => '/account-root/private-backups',
], static function () use ($invalidPortConfig): void {
    $environmentOverrideOperations = new BrokerOperations($invalidPortConfig);
    ftpsAssert(
        $environmentOverrideOperations->healthStatus()['offsite_backup_configured'] === true,
        'The documented FTPS environment override did not take precedence over fixture values.');
});

$exampleConfig = (string)file_get_contents(__DIR__ . '/../player-assistant-broker/config.operations.example.php');
ftpsAssert(
    !str_contains($exampleConfig, 'getenv('),
    'The example operations config would serialize evaluated FTPS environment secrets.');

$pathClient = (new ReflectionClass(CurlBrokerFtpsClient::class))->newInstanceWithoutConstructor();
$commandPath = new ReflectionMethod($pathClient, 'commandPath');
ftpsAssert(
    $commandPath->invoke($pathClient, '/htdocs/scarlet-horizons/pwa-backups/test.sqlite')
        === 'htdocs/scarlet-horizons/pwa-backups/test.sqlite',
    'FTPS quote commands did not use the same account-root-relative semantics as transfer URLs.');

if (function_exists('curl_init')) {
    $curlClient = new CurlBrokerFtpsClient([
        'host' => 'backup.example.com',
        'port' => 21,
        'username' => 'backup-user',
        'password' => 'test-only-password',
    ]);
    $buildUrl = new ReflectionMethod($curlClient, 'url');
    $directoryUrl = $buildUrl->invoke($curlClient, 'htdocs/scarlet-horizons/pwa-backups/');
    ftpsAssert(
        str_ends_with($directoryUrl, '/htdocs/scarlet-horizons/pwa-backups/'),
        'The FTPS directory URL did not retain its trailing slash.');
}

$root = sys_get_temp_dir() . '/pa-broker-ftps-' . bin2hex(random_bytes(6));
mkdir($root, 0700, true);
try {
    $backupPath = $root . '/broker-20260805T150000Z-a1b2c3d4.sqlite';
    $metadataPath = $backupPath . '.json';
    file_put_contents($backupPath, 'verified sqlite backup');
    file_put_contents($metadataPath, '{"sha256":"fixture"}');

    $remoteDirectory = 'htdocs/scarlet-horizons/pwa-backups';
    $client = new InMemoryBrokerFtpsClient();
    foreach (['20260803T150000Z-11111111', '20260804T150000Z-22222222'] as $old) {
        $client->files[$remoteDirectory . '/broker-' . $old . '.sqlite.enc'] = 'old encrypted backup';
        $client->files[$remoteDirectory . '/broker-' . $old . '.sqlite.enc.json'] = '{}';
    }
    $ftpsOperations = new BrokerOperations([
        'api' => ['database_path' => '/private/broker.sqlite'],
        'operations' => [
            'retention_count' => 2,
            'offsite' => [
                'transport' => 'ftps',
                'host' => 'backup.example.com',
                'port' => 21,
                'username' => 'backup-user',
                'password' => 'test-only-password',
                'directory' => $remoteDirectory,
            ],
        ],
    ], $client);

    $copyOffsite = new ReflectionMethod($ftpsOperations, 'copyOffsite');
    $copyOffsite->invoke($ftpsOperations, $backupPath, $metadataPath);

    $remoteBackup = $remoteDirectory . '/' . basename($backupPath) . '.enc';
    $remoteMetadata = $remoteBackup . '.json';
    ftpsAssert(isset($client->files[$remoteBackup]), 'The encrypted FTPS database backup was not promoted.');
    ftpsAssert(!isset($client->files[$remoteDirectory . '/' . basename($backupPath)]), 'A plaintext FTPS database backup was promoted.');
    ftpsAssert(isset($client->files[$remoteMetadata]), 'The FTPS metadata was not promoted.');
    ftpsAssert(
        count(array_filter($client->downloads, static fn(string $path): bool => str_starts_with($path, $remoteBackup . '.part-'))) === 1,
        'The temporary database upload was not downloaded before promotion.');
    ftpsAssert(
        count(array_filter($client->downloads, static fn(string $path): bool => str_starts_with($path, $remoteMetadata . '.part-'))) === 1,
        'The temporary metadata upload was not downloaded before promotion.');
    ftpsAssert(
        count(array_filter(array_keys($client->files), static fn(string $path): bool => str_ends_with($path, '.sqlite.enc'))) === 2,
        'FTPS retention did not preserve exactly the configured number of database backups.');
    ftpsAssert(
        count(array_filter(array_keys($client->files), static fn(string $path): bool => str_contains($path, '.part-'))) === 0,
        'Temporary FTPS uploads were not promoted or removed.');

    $corruptClient = new InMemoryBrokerFtpsClient();
    $corruptClient->corruptDownloads = true;
    $corruptOperations = new BrokerOperations([
        'api' => ['database_path' => '/private/broker.sqlite'],
        'operations' => [
            'offsite' => [
                'transport' => 'ftps',
                'host' => 'backup.example.com',
                'port' => 21,
                'username' => 'backup-user',
                'password' => 'test-only-password',
                'directory' => $remoteDirectory,
            ],
        ],
    ], $corruptClient);
    $verificationFailed = false;
    try {
        $copyOffsite->invoke($corruptOperations, $backupPath, $metadataPath);
    } catch (RuntimeException $error) {
        $verificationFailed = str_contains($error->getMessage(), 'SHA-256 verification');
    }
    ftpsAssert($verificationFailed, 'A corrupted FTPS download did not fail verification.');
    ftpsAssert(
        !isset($corruptClient->files[$remoteBackup]),
        'The corrupted FTPS database backup was not removed.');

    $partialClient = new InMemoryBrokerFtpsClient();
    $partialClient->failUploadsContaining = '.sqlite.enc.json';
    $partialOperations = new BrokerOperations([
        'api' => ['database_path' => '/private/broker.sqlite'],
        'operations' => [
            'offsite' => [
                'transport' => 'ftps',
                'host' => 'backup.example.com',
                'port' => 21,
                'username' => 'backup-user',
                'password' => 'test-only-password',
                'directory' => $remoteDirectory,
            ],
        ],
    ], $partialClient);
    $partialFailed = false;
    try {
        $copyOffsite->invoke($partialOperations, $backupPath, $metadataPath);
    } catch (RuntimeException $error) {
        $partialFailed = str_contains($error->getMessage(), 'Simulated FTPS upload failure');
    }
    ftpsAssert($partialFailed, 'The simulated metadata upload failure did not reach the caller.');
    ftpsAssert(
        !isset($partialClient->files[$remoteBackup]) && !isset($partialClient->files[$remoteMetadata]),
        'An FTPS upload failure left a partial final backup pair.');
} finally {
    foreach (glob($root . '/*') ?: [] as $path) {
        unlink($path);
    }
    rmdir($root);
}

echo "Broker FTPS transport tests passed.\n";
