<?php

declare(strict_types=1);

interface BrokerFtpsClient
{
    public function upload(string $localPath, string $remotePath): void;

    public function download(string $remotePath, string $localPath): void;

    public function rename(string $sourcePath, string $destinationPath): void;

    public function delete(string $remotePath): void;

    public function listFiles(string $directory): array;
}

final class BrokerBackupCipher
{
    private const MAGIC = 'PABACKUPENCV1';
    private const IV_BYTES = 16;
    private const MAC_BYTES = 32;

    public static function encryptFile(string $sourcePath, string $destinationPath, string $secret): array
    {
        if (!function_exists('openssl_encrypt')) {
            throw new RuntimeException('The PHP OpenSSL extension is required for encrypted broker backups.');
        }
        if (!is_file($sourcePath)) {
            throw new RuntimeException('The plaintext broker backup does not exist.');
        }
        [$encryptionKey, $authenticationKey] = self::deriveKeys($secret);
        $plaintext = file_get_contents($sourcePath);
        if ($plaintext === false) {
            throw new RuntimeException('Unable to read the plaintext broker backup.');
        }
        $iv = random_bytes(self::IV_BYTES);
        $ciphertext = openssl_encrypt($plaintext, 'aes-256-cbc', $encryptionKey, OPENSSL_RAW_DATA, $iv);
        if ($ciphertext === false) {
            throw new RuntimeException('Unable to encrypt the broker backup.');
        }
        $payload = self::MAGIC . $iv . $ciphertext;
        $mac = hash_hmac('sha256', $payload, $authenticationKey, true);
        if (file_put_contents($destinationPath, $payload . $mac, LOCK_EX) === false) {
            throw new RuntimeException('Unable to write the encrypted broker backup.');
        }
        chmod($destinationPath, 0600);
        self::clear($plaintext, $encryptionKey, $authenticationKey);
        return [
            'format' => 'player-assistant-backup-v1',
            'algorithm' => 'AES-256-CBC+HMAC-SHA256',
            'bytes' => filesize($destinationPath),
            'sha256' => hash_file('sha256', $destinationPath),
        ];
    }

    public static function decryptFile(string $sourcePath, string $destinationPath, string $secret): void
    {
        if (!function_exists('openssl_decrypt')) {
            throw new RuntimeException('The PHP OpenSSL extension is required for encrypted broker backups.');
        }
        [$encryptionKey, $authenticationKey] = self::deriveKeys($secret);
        $document = file_get_contents($sourcePath);
        $minimumLength = strlen(self::MAGIC) + self::IV_BYTES + self::MAC_BYTES + 1;
        if ($document === false || strlen($document) < $minimumLength || !str_starts_with($document, self::MAGIC)) {
            throw new RuntimeException('The encrypted broker backup format is invalid.');
        }
        $payload = substr($document, 0, -self::MAC_BYTES);
        $actualMac = substr($document, -self::MAC_BYTES);
        $expectedMac = hash_hmac('sha256', $payload, $authenticationKey, true);
        if (!hash_equals($expectedMac, $actualMac)) {
            throw new RuntimeException('The encrypted broker backup authentication failed.');
        }
        $offset = strlen(self::MAGIC);
        $iv = substr($payload, $offset, self::IV_BYTES);
        $ciphertext = substr($payload, $offset + self::IV_BYTES);
        $plaintext = openssl_decrypt($ciphertext, 'aes-256-cbc', $encryptionKey, OPENSSL_RAW_DATA, $iv);
        if ($plaintext === false || file_put_contents($destinationPath, $plaintext, LOCK_EX) === false) {
            throw new RuntimeException('Unable to decrypt the broker backup.');
        }
        chmod($destinationPath, 0600);
        self::clear($document, $plaintext, $encryptionKey, $authenticationKey);
    }

    private static function deriveKeys(string $secret): array
    {
        if (strlen($secret) < 32) {
            throw new RuntimeException('BACKUP_ENCRYPTION_KEY must contain at least 32 characters.');
        }
        $material = hash('sha512', $secret, true);
        return [substr($material, 0, 32), substr($material, 32, 32)];
    }

    private static function clear(string &...$values): void
    {
        if (!function_exists('sodium_memzero')) {
            return;
        }
        foreach ($values as &$value) {
            sodium_memzero($value);
        }
    }
}

final class CurlBrokerFtpsClient implements BrokerFtpsClient
{
    private string $host;
    private int $port;
    private string $username;
    private string $password;

    public function __construct(array $config)
    {
        if (!function_exists('curl_init')) {
            throw new RuntimeException('The PHP cURL extension is required for FTPS backups.');
        }
        $this->host = trim((string)($config['host'] ?? ''));
        $this->port = (int)($config['port'] ?? 21);
        $this->username = (string)($config['username'] ?? '');
        $this->password = (string)($config['password'] ?? '');
        if ($this->host === '' || $this->port < 1 || $this->port > 65535
            || $this->username === '' || $this->password === '') {
            throw new RuntimeException('The FTPS backup credentials are incomplete.');
        }
    }

    public function upload(string $localPath, string $remotePath): void
    {
        $stream = fopen($localPath, 'rb');
        if ($stream === false) {
            throw new RuntimeException('Unable to open the broker backup for FTPS upload.');
        }
        $handle = $this->initialize($remotePath);
        try {
            curl_setopt_array($handle, [
                CURLOPT_UPLOAD => true,
                CURLOPT_INFILE => $stream,
                CURLOPT_INFILESIZE => filesize($localPath),
            ]);
            $this->execute($handle);
        } finally {
            fclose($stream);
        }
    }

    public function download(string $remotePath, string $localPath): void
    {
        $stream = fopen($localPath, 'wb');
        if ($stream === false) {
            throw new RuntimeException('Unable to open the FTPS verification file.');
        }
        $handle = $this->initialize($remotePath);
        try {
            curl_setopt($handle, CURLOPT_WRITEFUNCTION,
                static function (CurlHandle $handle, string $data) use ($stream): int {
                    $written = fwrite($stream, $data);
                    return $written === false ? 0 : $written;
                });
            $this->execute($handle);
        } finally {
            fclose($stream);
        }
    }

    public function rename(string $sourcePath, string $destinationPath): void
    {
        $handle = $this->initialize('');
        curl_setopt_array($handle, [
            CURLOPT_NOBODY => true,
            CURLOPT_QUOTE => [
                'RNFR ' . $this->commandPath($sourcePath),
                'RNTO ' . $this->commandPath($destinationPath),
            ],
        ]);
        $this->execute($handle);
    }

    public function delete(string $remotePath): void
    {
        $handle = $this->initialize('');
        curl_setopt_array($handle, [
            CURLOPT_NOBODY => true,
            CURLOPT_QUOTE => ['DELE ' . $this->commandPath($remotePath)],
        ]);
        $this->execute($handle);
    }

    public function listFiles(string $directory): array
    {
        $handle = $this->initialize(rtrim($directory, '/') . '/');
        curl_setopt($handle, CURLOPT_DIRLISTONLY, true);
        $listing = $this->execute($handle);
        $names = [];
        foreach (preg_split('/\r\n|\r|\n/', trim($listing)) ?: [] as $entry) {
            $name = basename(str_replace('\\', '/', trim($entry)));
            if ($name !== '' && $name !== '.' && $name !== '..') {
                $names[] = $name;
            }
        }
        return array_values(array_unique($names));
    }

    private function initialize(string $remotePath): CurlHandle
    {
        $handle = curl_init($this->url($remotePath));
        if ($handle === false) {
            throw new RuntimeException('Unable to initialize the FTPS client.');
        }
        curl_setopt_array($handle, [
            CURLOPT_USERPWD => $this->username . ':' . $this->password,
            CURLOPT_USE_SSL => CURLUSESSL_ALL,
            CURLOPT_SSL_VERIFYPEER => true,
            CURLOPT_SSL_VERIFYHOST => 2,
            CURLOPT_FTPSSLAUTH => CURLFTPAUTH_TLS,
            CURLOPT_FTP_USE_EPSV => true,
            CURLOPT_CONNECTTIMEOUT => 15,
            CURLOPT_TIMEOUT => 180,
            CURLOPT_FAILONERROR => true,
            CURLOPT_RETURNTRANSFER => true,
        ]);
        return $handle;
    }

    private function execute(CurlHandle $handle): string
    {
        $result = curl_exec($handle);
        if ($result === false) {
            throw new RuntimeException('The broker FTPS operation failed: ' . curl_error($handle));
        }
        return is_string($result) ? $result : '';
    }

    private function url(string $remotePath): string
    {
        $directoryUrl = str_ends_with(str_replace('\\', '/', $remotePath), '/');
        $path = $this->normalizePath($remotePath);
        $encoded = implode('/', array_map('rawurlencode', $path === '' ? [] : explode('/', $path)));
        return 'ftp://' . $this->host . ':' . $this->port . '/' . $encoded
            . ($directoryUrl && $encoded !== '' ? '/' : '');
    }

    private function normalizePath(string $path): string
    {
        $path = trim(str_replace('\\', '/', $path), '/');
        if (preg_match('/[\x00-\x1F\x7F]/', $path) === 1) {
            throw new RuntimeException('The FTPS path contains control characters.');
        }
        foreach ($path === '' ? [] : explode('/', $path) as $segment) {
            if ($segment === '' || $segment === '.' || $segment === '..') {
                throw new RuntimeException('The FTPS path is invalid.');
            }
        }
        return $path;
    }

    private function commandPath(string $path): string
    {
        return $this->normalizePath($path);
    }
}

final class BrokerOperations
{
    private array $operationsConfig;
    private string $databasePath;
    private ?BrokerFtpsClient $ftpsClient;

    public function __construct(private readonly array $config, ?BrokerFtpsClient $ftpsClient = null)
    {
        $this->ftpsClient = $ftpsClient;
        $apiConfig = is_array($config['api'] ?? null) ? $config['api'] : [];
        $this->databasePath = (string)($apiConfig['database_path'] ?? '');
        $this->operationsConfig = array_replace([
            'backup_directory' => dirname($this->databasePath) . '/broker-backups',
            'restore_test_directory' => dirname($this->databasePath) . '/broker-restore-tests',
            'status_path' => dirname($this->databasePath) . '/broker-operations-status.json',
            'retention_count' => 14,
            'server_error_threshold' => 5,
            'server_error_window_seconds' => 900,
            'alert_cooldown_seconds' => 3600,
            'alert_email' => '',
            'alert_from' => '',
            'offsite' => [],
        ], is_array($config['operations'] ?? null) ? $config['operations'] : []);
        $offsite = is_array($this->operationsConfig['offsite'])
            ? $this->operationsConfig['offsite']
            : [];
        if (strtolower((string)($offsite['transport'] ?? '')) === 'ftps') {
            foreach ([
                'host' => 'BACKUP_FTPS_HOST',
                'username' => 'BACKUP_FTPS_USERNAME',
                'password' => 'BACKUP_FTPS_PASSWORD',
                'directory' => 'BACKUP_FTPS_REMOTE_PATH',
            ] as $configKey => $environmentName) {
                $environmentValue = getenv($environmentName);
                if ($environmentValue !== false && $environmentValue !== '') {
                    $offsite[$configKey] = $environmentValue;
                }
            }
            $environmentPort = getenv('BACKUP_FTPS_PORT');
            if ($environmentPort !== false && $environmentPort !== '') {
                $offsite['port'] = (int)$environmentPort;
            }
            $offsite['port'] ??= 21;
            $this->operationsConfig['offsite'] = $offsite;
        }
    }

    public function healthStatus(): array
    {
        $status = $this->readStatus();
        $serverErrors = $this->serverErrorState($status);
        $healthy = ($status['last_maintenance_status'] ?? null) === 'success'
            && $serverErrors['count'] < $this->serverErrorThreshold();
        if (($status['last_maintenance_status'] ?? null) === 'failed') {
            $this->alert('health_failure', 'Broker health reports a failed maintenance run.');
        } elseif ($serverErrors['count'] >= $this->serverErrorThreshold()) {
            $this->alert('health_failure', 'Broker health reports repeated server errors.');
        }

        return [
            'configured' => $this->databasePath !== ''
                && (string)$this->operationsConfig['backup_directory'] !== ''
                && (string)$this->operationsConfig['restore_test_directory'] !== '',
            'alerting_configured' => $this->alertingConfigured(),
            'offsite_backup_configured' => $this->offsiteConfigured(),
            'last_integrity_check_at' => $status['last_integrity_check_at'] ?? null,
            'last_integrity_check_result' => $status['last_integrity_check_result'] ?? null,
            'last_backup_at' => $status['last_backup_at'] ?? null,
            'last_backup_status' => $status['last_backup_status'] ?? null,
            'last_restore_test_at' => $status['last_restore_test_at'] ?? null,
            'last_restore_test_status' => $status['last_restore_test_status'] ?? null,
            'last_maintenance_at' => $status['last_maintenance_at'] ?? null,
            'last_maintenance_status' => $status['last_maintenance_status'] ?? null,
            'last_failure_code' => $status['last_failure_code'] ?? null,
            'server_error_count' => $serverErrors['count'],
            'server_error_window_started_at' => $serverErrors['window_started'] === 0
                ? null
                : gmdate(DATE_ATOM, $serverErrors['window_started']),
            'last_server_error_at' => $serverErrors['last_at'],
            'alert_last_sent_at' => $status['alert_last_sent_at'] ?? null,
            'healthy' => $healthy,
        ];
    }

    public function runMaintenance(): array
    {
        $startedAt = gmdate(DATE_ATOM);
        try {
            $integrity = $this->checkIntegrity($this->databasePath);
            $this->updateStatus(static function (array $status) use ($startedAt, $integrity): array {
                $status['last_integrity_check_at'] = $startedAt;
                $status['last_integrity_check_result'] = $integrity;
                return $status;
            });

            if ($integrity !== 'ok') {
                throw new RuntimeException('The broker database integrity check failed.');
            }

            $backup = $this->createBackup();
            $restore = $this->restoreTest($backup['path']);
            if ($restore !== 'ok') {
                throw new RuntimeException('The broker backup restore test failed.');
            }

            $completedAt = gmdate(DATE_ATOM);
            $this->updateStatus(static function (array $status) use ($completedAt): array {
                $status['last_maintenance_at'] = $completedAt;
                $status['last_maintenance_status'] = 'success';
                $status['last_failure_code'] = null;
                return $status;
            });

            return [
                'status' => 'ok',
                'integrity_check' => $integrity,
                'backup' => $backup,
                'restore_test' => $restore,
            ];
        } catch (Throwable $error) {
            $failureCode = 'maintenance_failed';
            $this->updateStatus(static function (array $status) use ($startedAt, $failureCode): array {
                $status['last_maintenance_at'] = $startedAt;
                $status['last_maintenance_status'] = 'failed';
                $status['last_failure_code'] = $failureCode;
                return $status;
            });
            $this->alert('maintenance_failure', 'Broker maintenance failed: ' . $failureCode);
            throw $error;
        }
    }

    public function recordRefreshFailure(string $errorCode): void
    {
        $code = $this->sanitizeErrorCode($errorCode);
        $this->updateStatus(static function (array $status) use ($code): array {
            $status['last_refresh_failure_at'] = gmdate(DATE_ATOM);
            $status['last_refresh_failure_code'] = $code;
            return $status;
        });
        $this->alert('refresh_failure', 'Broker refresh failed: ' . $code);
    }

    public function recordServerError(string $requestId, string $errorCode = 'internal_error'): void
    {
        $now = time();
        $state = null;
        $this->updateStatus(function (array $status) use ($now, $requestId, $errorCode, &$state): array {
            $current = $this->serverErrorState($status);
            if ($current['window_started'] === 0
                || $now - $current['window_started'] > $this->serverErrorWindowSeconds()) {
                $current = ['count' => 0, 'window_started' => $now, 'last_at' => null];
            }
            $current['count']++;
            $current['last_at'] = gmdate(DATE_ATOM, $now);
            $status['server_error_count'] = $current['count'];
            $status['server_error_window_started_at'] = gmdate(DATE_ATOM, $current['window_started']);
            $status['last_server_error_at'] = $current['last_at'];
            $status['last_server_error_code'] = $this->sanitizeErrorCode($errorCode);
            $status['last_server_error_request_id'] = substr($requestId, 0, 32);
            $state = $current;
            return $status;
        });

        if (is_array($state) && $state['count'] >= $this->serverErrorThreshold()) {
            $this->alert(
                'repeated_server_errors',
                sprintf(
                    'Broker recorded %d server errors within %d seconds.',
                    $state['count'],
                    $this->serverErrorWindowSeconds()));
        }
    }

    public function recordRefreshSuccess(): void
    {
        $this->updateStatus(static function (array $status): array {
            $status['last_refresh_success_at'] = gmdate(DATE_ATOM);
            $status['last_refresh_failure_at'] = null;
            $status['last_refresh_failure_code'] = null;
            return $status;
        });
    }

    private function createBackup(): array
    {
        if (!is_file($this->databasePath)) {
            throw new RuntimeException('The broker database file was not found.');
        }

        $directory = $this->prepareDirectory((string)$this->operationsConfig['backup_directory']);
        $stamp = gmdate('Ymd\THis\Z');
        $backupPath = $directory . '/broker-' . $stamp . '-' . bin2hex(random_bytes(4)) . '.sqlite';
        $temporaryPath = $backupPath . '.tmp';
        try {
            $database = $this->openDatabase($this->databasePath);
            $database->exec('VACUUM INTO ' . $database->quote($temporaryPath));
            $database = null;
            if (!is_file($temporaryPath) || !rename($temporaryPath, $backupPath)) {
                throw new RuntimeException('Unable to promote the broker database backup.');
            }
            chmod($backupPath, 0600);
            if ($this->checkIntegrity($backupPath) !== 'ok') {
                throw new RuntimeException('The newly created broker database backup failed integrity validation.');
            }

            $metadata = [
                'schema_version' => 1,
                'created_at' => gmdate(DATE_ATOM),
                'file' => basename($backupPath),
                'bytes' => filesize($backupPath),
                'sha256' => hash_file('sha256', $backupPath),
            ];
            $metadataPath = $backupPath . '.json';
            $this->writePrivateJson($metadataPath, $metadata);
            $this->copyOffsite($backupPath, $metadataPath);
            $this->pruneBackups($directory);
            $this->updateStatus(static function (array $status) use ($metadata): array {
                $status['last_backup_at'] = $metadata['created_at'];
                $status['last_backup_status'] = 'success';
                $status['last_backup_sha256'] = $metadata['sha256'];
                return $status;
            });

            return [
                'created_at' => $metadata['created_at'],
                'file' => $metadata['file'],
                'bytes' => $metadata['bytes'],
                'sha256' => $metadata['sha256'],
                'offsite' => $this->offsiteConfigured(),
                'path' => $backupPath,
            ];
        } catch (Throwable $error) {
            @unlink($temporaryPath);
            $this->updateStatus(static function (array $status): array {
                $status['last_backup_at'] = gmdate(DATE_ATOM);
                $status['last_backup_status'] = 'failed';
                return $status;
            });
            throw $error;
        }
    }

    private function restoreTest(string $backupPath): string
    {
        $directory = $this->prepareDirectory((string)$this->operationsConfig['restore_test_directory']);
        $restorePath = $directory . '/restore-' . gmdate('Ymd\THis\Z') . '-' . bin2hex(random_bytes(4)) . '.sqlite';
        if (!copy($backupPath, $restorePath)) {
            throw new RuntimeException('Unable to stage the broker backup for restore testing.');
        }
        chmod($restorePath, 0600);
        try {
            $result = $this->checkIntegrity($restorePath);
            if ($result !== 'ok') {
                throw new RuntimeException('The restored broker database failed integrity validation.');
            }
            $this->updateStatus(static function (array $status): array {
                $status['last_restore_test_at'] = gmdate(DATE_ATOM);
                $status['last_restore_test_status'] = 'success';
                return $status;
            });
            return $result;
        } catch (Throwable $error) {
            $this->updateStatus(static function (array $status): array {
                $status['last_restore_test_at'] = gmdate(DATE_ATOM);
                $status['last_restore_test_status'] = 'failed';
                return $status;
            });
            throw $error;
        } finally {
            @unlink($restorePath);
        }
    }

    private function copyOffsite(string $backupPath, string $metadataPath): void
    {
        $secret = (string)(getenv('BACKUP_ENCRYPTION_KEY') ?: '');
        if ($secret === '') {
            throw new RuntimeException('BACKUP_ENCRYPTION_KEY is required for offsite broker backups.');
        }
        $stagingDirectory = sys_get_temp_dir() . '/pa-backup-encrypted-' . bin2hex(random_bytes(6));
        if (!mkdir($stagingDirectory, 0700, true) && !is_dir($stagingDirectory)) {
            throw new RuntimeException('Unable to create encrypted backup staging.');
        }
        $encryptedPath = $stagingDirectory . '/' . basename($backupPath) . '.enc';
        $encryptedMetadataPath = $encryptedPath . '.json';
        try {
            $encryption = BrokerBackupCipher::encryptFile($backupPath, $encryptedPath, $secret);
            $plaintextMetadata = json_decode((string)file_get_contents($metadataPath), true);
            $this->writePrivateJson($encryptedMetadataPath, [
                'schema_version' => 2,
                'created_at' => is_array($plaintextMetadata)
                    ? ($plaintextMetadata['created_at'] ?? gmdate(DATE_ATOM))
                    : gmdate(DATE_ATOM),
                'file' => basename($encryptedPath),
                'bytes' => $encryption['bytes'],
                'sha256' => $encryption['sha256'],
                'encryption' => [
                    'format' => $encryption['format'],
                    'algorithm' => $encryption['algorithm'],
                ],
            ]);
            $this->transferOffsite($encryptedPath, $encryptedMetadataPath);
        } finally {
            @unlink($encryptedPath);
            @unlink($encryptedMetadataPath);
            @rmdir($stagingDirectory);
        }
    }

    private function transferOffsite(string $backupPath, string $metadataPath): void
    {
        $offsite = is_array($this->operationsConfig['offsite'])
            ? $this->operationsConfig['offsite']
            : [];
        if (isset($offsite['local_directory']) && (string)$offsite['local_directory'] !== '') {
            $directory = $this->prepareDirectory((string)$offsite['local_directory']);
            foreach ([$backupPath, $metadataPath] as $path) {
                $destination = $directory . '/' . basename($path);
                if (!copy($path, $destination)) {
                    throw new RuntimeException('Unable to copy the broker backup to the configured offsite directory.');
                }
                chmod($destination, 0600);
            }
            return;
        }

        if (strtolower((string)($offsite['transport'] ?? '')) === 'ftps') {
            if (!$this->offsiteConfigured()) {
                throw new RuntimeException('The broker FTPS backup destination is not configured.');
            }
            $this->copyOffsiteFtps($backupPath, $metadataPath, $offsite);
            return;
        }

        $target = (string)($offsite['ssh_target'] ?? '');
        $directory = (string)($offsite['directory'] ?? '');
        if ($target === '' || $directory === '') {
            throw new RuntimeException('The broker offsite backup destination is not configured.');
        }
        $identity = (string)($offsite['identity_file'] ?? '');
        $sshOptions = '-o BatchMode=yes -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes';
        if ($identity !== '') {
            $sshOptions .= ' -i ' . escapeshellarg($identity);
        }
        $mkdirCommand = 'ssh ' . $sshOptions . ' ' . escapeshellarg($target)
            . ' mkdir -p -- ' . escapeshellarg($directory);
        $this->runCommand($mkdirCommand);
        $scpCommand = 'scp ' . $sshOptions . ' -- '
            . escapeshellarg($backupPath) . ' '
            . escapeshellarg($metadataPath) . ' '
            . escapeshellarg($target . ':' . rtrim($directory, '/') . '/');
        $this->runCommand($scpCommand);
        $retention = max(1, (int)$this->operationsConfig['retention_count']);
        $cleanupCommand = 'find ' . escapeshellarg(rtrim($directory, '/'))
            . ' -maxdepth 1 -type f -name ' . escapeshellarg('broker-*.sqlite.enc')
            . " -printf '%T@ %p\\n' | sort -nr | tail -n +" . ($retention + 1)
            . " | cut -d' ' -f2- | while IFS= read -r file; do rm -f -- \"\$file\" \"\$file.json\"; done";
        $this->runCommand(
            'ssh ' . $sshOptions . ' ' . escapeshellarg($target) . ' ' . escapeshellarg($cleanupCommand));
    }

    private function copyOffsiteFtps(string $backupPath, string $metadataPath, array $offsite): void
    {
        $directory = trim(str_replace('\\', '/', (string)$offsite['directory']), '/');
        $client = $this->ftpsClient ??= new CurlBrokerFtpsClient($offsite);
        $staged = [];
        $promoted = [];
        try {
            foreach ([$backupPath, $metadataPath] as $localPath) {
                $remotePath = $directory . '/' . basename($localPath);
                $staged[] = [
                    'temporary' => $this->stageVerifiedFtpsFile($client, $localPath, $remotePath),
                    'final' => $remotePath,
                ];
            }
            foreach ($staged as $upload) {
                $client->rename($upload['temporary'], $upload['final']);
                $promoted[] = $upload['final'];
            }
        } catch (Throwable $error) {
            foreach ($staged as $upload) {
                try {
                    $client->delete($upload['temporary']);
                } catch (Throwable) {
                }
            }
            foreach ($promoted as $remotePath) {
                try {
                    $client->delete($remotePath);
                } catch (Throwable) {
                }
            }
            throw $error;
        }
        $this->pruneFtpsBackups($client, $directory);
    }

    private function stageVerifiedFtpsFile(
        BrokerFtpsClient $client,
        string $localPath,
        string $remotePath
    ): string {
        $temporaryRemotePath = $remotePath . '.part-' . bin2hex(random_bytes(4));
        $verificationPath = tempnam(sys_get_temp_dir(), 'pa-ftps-verify-');
        if ($verificationPath === false) {
            throw new RuntimeException('Unable to create the FTPS verification file.');
        }
        try {
            $client->upload($localPath, $temporaryRemotePath);
            $client->download($temporaryRemotePath, $verificationPath);
            $localHash = hash_file('sha256', $localPath);
            $remoteHash = hash_file('sha256', $verificationPath);
            if ($localHash === false || $remoteHash === false || !hash_equals($localHash, $remoteHash)) {
                throw new RuntimeException('The FTPS backup failed SHA-256 verification.');
            }
            return $temporaryRemotePath;
        } catch (Throwable $error) {
            try {
                $client->delete($temporaryRemotePath);
            } catch (Throwable) {
            }
            throw $error;
        } finally {
            @unlink($verificationPath);
        }
    }

    private function pruneFtpsBackups(BrokerFtpsClient $client, string $directory): void
    {
        $names = $client->listFiles($directory);
        $backups = array_values(array_filter(
            $names,
            static fn(string $name): bool => preg_match(
                '/^broker-\d{8}T\d{6}Z-[a-f0-9]{8}\.sqlite\.enc$/D',
                $name) === 1));
        rsort($backups, SORT_STRING);
        $retention = max(1, (int)$this->operationsConfig['retention_count']);
        foreach (array_slice($backups, $retention) as $obsolete) {
            $client->delete($directory . '/' . $obsolete);
            if (in_array($obsolete . '.json', $names, true)) {
                $client->delete($directory . '/' . $obsolete . '.json');
            }
        }
    }

    private function checkIntegrity(string $databasePath): string
    {
        if (!is_file($databasePath)) {
            throw new RuntimeException('The broker database path does not exist.');
        }
        $database = $this->openDatabase($databasePath);
        $result = (string)$database->query('PRAGMA integrity_check')->fetchColumn();
        $database = null;
        return strtolower(trim($result));
    }

    private function openDatabase(string $path): PDO
    {
        $database = new PDO('sqlite:' . $path, null, null, [
            PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
            PDO::ATTR_EMULATE_PREPARES => false,
        ]);
        $database->exec('PRAGMA busy_timeout = 5000');
        return $database;
    }

    private function pruneBackups(string $directory): void
    {
        $retention = max(1, (int)$this->operationsConfig['retention_count']);
        $files = glob($directory . '/broker-*.sqlite') ?: [];
        usort($files, static fn(string $left, string $right): int => filemtime($right) <=> filemtime($left));
        foreach (array_slice($files, $retention) as $obsolete) {
            @unlink($obsolete);
            @unlink($obsolete . '.json');
        }
    }

    private function prepareDirectory(string $directory): string
    {
        if ($directory === '') {
            throw new RuntimeException('The broker operations directory is not configured.');
        }
        if (!is_dir($directory) && !mkdir($directory, 0700, true) && !is_dir($directory)) {
            throw new RuntimeException('Unable to create the broker operations directory.');
        }
        chmod($directory, 0700);
        return rtrim($directory, '/\\');
    }

    private function writePrivateJson(string $path, array $data): void
    {
        $temporaryPath = $path . '.tmp-' . bin2hex(random_bytes(4));
        if (file_put_contents(
            $temporaryPath,
            json_encode($data, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR),
            LOCK_EX) === false) {
            throw new RuntimeException('Unable to write broker operations metadata.');
        }
        chmod($temporaryPath, 0600);
        if (!rename($temporaryPath, $path)) {
            @unlink($temporaryPath);
            throw new RuntimeException('Unable to promote broker operations metadata.');
        }
        chmod($path, 0600);
    }

    private function readStatus(): array
    {
        $path = (string)$this->operationsConfig['status_path'];
        if ($path === '' || !is_file($path) || filesize($path) > 16384) {
            return [];
        }
        try {
            $status = json_decode((string)file_get_contents($path), true, 16, JSON_THROW_ON_ERROR);
            return is_array($status) ? $status : [];
        } catch (Throwable) {
            return [];
        }
    }

    private function updateStatus(callable $update): void
    {
        $path = (string)$this->operationsConfig['status_path'];
        $directory = dirname($path);
        $this->prepareDirectory($directory);
        $lockPath = $path . '.lock';
        $lock = fopen($lockPath, 'c');
        if ($lock === false || !flock($lock, LOCK_EX)) {
            throw new RuntimeException('Unable to lock broker operations status.');
        }
        try {
            $status = $update($this->readStatus());
            $this->writePrivateJson($path, $status);
        } finally {
            flock($lock, LOCK_UN);
            fclose($lock);
        }
        @unlink($lockPath);
    }

    private function alert(string $event, string $message): void
    {
        $now = time();
        $status = $this->readStatus();
        $lastAlert = (int)($status['alert_last_sent_unix'] ?? 0);
        if ($lastAlert > 0 && $now - $lastAlert < $this->alertCooldownSeconds()) {
            return;
        }

        $subject = 'Player Assistant broker alert: ' . $event;
        $sent = false;
        $email = trim((string)$this->operationsConfig['alert_email']);
        if ($email !== '') {
            $headers = '';
            $from = trim((string)$this->operationsConfig['alert_from']);
            if ($from !== '') {
                $headers = 'From: ' . $from . "\r\n";
            }
            $sent = mail($email, $subject, $message, $headers);
        }

        $this->updateStatus(static function (array $status) use ($event, $message, $now, $sent): array {
            $status['last_alert_event'] = $event;
            $status['last_alert_message'] = $message;
            $status['alert_last_sent_at'] = gmdate(DATE_ATOM, $now);
            $status['alert_last_sent_unix'] = $now;
            $status['alert_last_send_result'] = $sent ? 'sent' : 'not_sent';
            return $status;
        });
    }

    private function serverErrorState(array $status): array
    {
        $windowStartedAt = (string)($status['server_error_window_started_at'] ?? '');
        $windowStarted = $windowStartedAt !== '' ? strtotime($windowStartedAt) : false;
        if ($windowStarted === false || time() - $windowStarted > $this->serverErrorWindowSeconds()) {
            return ['count' => 0, 'window_started' => 0, 'last_at' => null];
        }
        return [
            'count' => max(0, (int)($status['server_error_count'] ?? 0)),
            'window_started' => $windowStarted,
            'last_at' => $status['last_server_error_at'] ?? null,
        ];
    }

    private function alertingConfigured(): bool
    {
        return trim((string)$this->operationsConfig['alert_email']) !== '';
    }

    private function offsiteConfigured(): bool
    {
        $offsite = is_array($this->operationsConfig['offsite'])
            ? $this->operationsConfig['offsite']
            : [];
        $hasEncryptionKey = strlen((string)(getenv('BACKUP_ENCRYPTION_KEY') ?: '')) >= 32;
        if (isset($offsite['local_directory']) && trim((string)$offsite['local_directory']) !== '') {
            return $hasEncryptionKey;
        }
        if (strtolower((string)($offsite['transport'] ?? '')) === 'ftps') {
            $port = (int)($offsite['port'] ?? 0);
            return $hasEncryptionKey
                && trim((string)($offsite['host'] ?? '')) !== ''
                && $port > 0
                && $port <= 65535
                && trim((string)($offsite['username'] ?? '')) !== ''
                && (string)($offsite['password'] ?? '') !== ''
                && trim((string)($offsite['directory'] ?? '')) !== '';
        }
        return $hasEncryptionKey
            && (string)($offsite['ssh_target'] ?? '') !== ''
            && (string)($offsite['directory'] ?? '') !== '';
    }

    private function runCommand(string $command): void
    {
        $output = [];
        $exitCode = 0;
        exec($command . ' 2>&1', $output, $exitCode);
        if ($exitCode !== 0) {
            throw new RuntimeException('The broker offsite backup command failed.');
        }
    }

    private function serverErrorThreshold(): int
    {
        return max(1, (int)$this->operationsConfig['server_error_threshold']);
    }

    private function serverErrorWindowSeconds(): int
    {
        return max(60, (int)$this->operationsConfig['server_error_window_seconds']);
    }

    private function alertCooldownSeconds(): int
    {
        return max(60, (int)$this->operationsConfig['alert_cooldown_seconds']);
    }

    private function sanitizeErrorCode(string $value): string
    {
        $value = preg_replace('/[^a-zA-Z0-9_.:-]+/', '_', strtolower($value)) ?? 'unknown';
        return substr(trim($value, '_'), 0, 120) ?: 'unknown';
    }
}
