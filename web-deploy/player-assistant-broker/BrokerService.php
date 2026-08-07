<?php

declare(strict_types=1);

require_once __DIR__ . '/DatabaseMigrationService.php';
require_once __DIR__ . '/BrokerAlertService.php';

final class BrokerService
{
    private PDO $database;
    private array $apiConfig;
    private CharacterAuthService $characterAuth;
    private XpTrackingService $xpTracking;
    private WordCountService $wordCounts;
    private QuestService $quests;
    private MessageService $messages;
    private BrokerAlertService $alerts;
    private BrokerOperations $operations;

    public function __construct(
        private readonly array $config,
        private readonly RpolClient $rpolClient,
        ?callable $xpMarkdownFetcher = null,
        callable|string|null $wordCountFetcher = null,
        ?string $questDataPath = null)
    {
        if (is_string($wordCountFetcher) && $questDataPath === null) {
            $questDataPath = $wordCountFetcher;
            $wordCountFetcher = null;
        }

        if (!extension_loaded('pdo_sqlite')) {
            throw new RuntimeException('The PHP PDO SQLite extension is required.');
        }

        $this->apiConfig = $config['api'];
        $databasePath = (string)$this->apiConfig['database_path'];
        $this->database = new PDO('sqlite:' . $databasePath, null, null, [
            PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
            PDO::ATTR_EMULATE_PREPARES => false,
        ]);
        $this->database->exec('PRAGMA foreign_keys = ON');
        $this->database->exec('PRAGMA busy_timeout = 5000');
        $migrationConfig = is_array($config['migrations'] ?? null) ? $config['migrations'] : [];
        $migrationBackupDirectory = (string)($migrationConfig['backup_directory'] ?? dirname($databasePath) . '/migration-backups');
        (new DatabaseMigrationService($this->database, $migrationBackupDirectory))->migrate();
        $this->alerts = new BrokerAlertService(
            $this->database,
            is_array($config['observability'] ?? null) ? $config['observability'] : []);
        $this->operations = new BrokerOperations($config);
        $this->ensureSchema();
        $this->characterAuth = new CharacterAuthService(
            $this->database,
            is_array($config['auth'] ?? null) ? $config['auth'] : []);
        $this->xpTracking = new XpTrackingService(
            $this->database,
            is_array($config['xp'] ?? null) ? $config['xp'] : [],
            $xpMarkdownFetcher);
        $this->wordCounts = new WordCountService(
            $this->database,
            is_array($config['word_counts'] ?? null) ? $config['word_counts'] : [],
            $wordCountFetcher);
        $this->quests = new QuestService($this->database, (string)$questDataPath);
        $this->messages = new MessageService($this->database);
    }

    public function dispatch(
        string $method,
        string $route,
        array $query,
        array $body,
        array $headers,
        string $remoteAddress,
        array &$session,
        ?callable $regenerateSession = null,
        ?callable $destroySession = null): array
    {
        if ($method === 'GET' && $route === '/v1/health') {
            return $this->response(200, [
                'service' => 'player-assistant-broker',
                'schema_version' => 7,
                'status' => 'ok',
            ]);
        }

        if ($method === 'GET' && $route === '/v1/admin/health') {
            $this->requireAdminSignature($method, $route, $body, $headers);
            $wordCountRefresh = $this->wordCounts->refreshStatus();
            if (($wordCountRefresh['healthy'] ?? null) === false) {
                $this->alerts->recordHealthFailure(
                    (string)($wordCountRefresh['last_error_code'] ?? 'word_count_refresh_failed'),
                    'The word-count refresh health check is failing.');
            }
            return $this->response(200, [
                'service' => 'player-assistant-broker',
                'schema_version' => 7,
                'database_schema_version' => (int)$this->database->query('PRAGMA user_version')->fetchColumn(),
                'status' => 'ok',
                'rpol_credentials_configured' => $this->rpolCredentialsConfigured(),
                'snapshot_signing_configured' => $this->snapshotSigningConfigured(),
                'snapshot_count' => $this->snapshotCount(),
                'character_account_count' => $this->characterAuth->accountCount(),
                'xp_tracking_configured' => $this->xpTracking->isConfigured(),
                'word_count_snapshot_available' => $this->wordCounts->hasSnapshot(),
                'word_count_refresh' => $wordCountRefresh,
                'operations' => $this->operations->healthStatus(),
                'quest_request_workflow_configured' => true,
            ]);
        }

        if ($method === 'POST' && $route === '/v1/login') {
            return $this->response(
                200,
                $this->characterAuth->login(
                    $body,
                    $remoteAddress,
                    (string)($headers['origin'] ?? ''),
                    $session,
                    $regenerateSession));
        }

        if ($method === 'GET' && $route === '/v1/session') {
            return $this->response(200, $this->characterAuth->currentSession($session));
        }

        if ($method === 'GET' && $route === '/v1/me') {
            return $this->response(200, $this->characterAuth->requireCurrentAccount($session));
        }

        if ($method === 'GET' && $route === '/v1/xp') {
            $current = $this->characterAuth->requireCurrentAccount($session);
            return $this->response(
                200,
                $this->xpTracking->getForAccount($current['account']));
        }

        if ($method === 'GET' && $route === '/v1/xp-awards') {
            $current = $this->characterAuth->requireCurrentAccount($session);
            return $this->response(
                200,
                $this->xpTracking->getAwardsForAccount($current['account']));
        }

        if ($method === 'GET' && $route === '/v1/word-counts') {
            $this->characterAuth->requireCurrentAccount($session);
            return $this->response(200, $this->wordCounts->latest());
        }

        if ($method === 'GET' && $route === '/v1/presence') {
            return $this->response(200, $this->characterAuth->presence($session));
        }

        if ($method === 'GET' && $route === '/v1/quests') {
            $current = $this->characterAuth->requireCurrentAccount($session);
            return $this->response(200, $this->quests->forAccount($current['account']));
        }

        if ($method === 'POST' && $route === '/v1/quest-requests') {
            $current = $this->characterAuth->requireMutationAccount($headers, $session);
            return $this->response(
                201,
                $this->quests->requestInterest($current['account'], $body));
        }

        if ($method === 'POST'
            && preg_match(
                '#^/v1/quest-requests/([a-f0-9]{32})/decision$#',
                $route,
                $matches) === 1) {
            $current = $this->characterAuth->requireMutationAccount($headers, $session);
            return $this->response(
                200,
                $this->quests->decide($current['account'], $matches[1], $body));
        }

        if ($method === 'POST'
            && preg_match(
                '#^/v1/quest-requests/([a-f0-9]{32})/acknowledge$#',
                $route,
                $matches) === 1) {
            $current = $this->characterAuth->requireMutationAccount($headers, $session);
            return $this->response(
                200,
                $this->quests->acknowledge($current['account'], $matches[1]));
        }

        if ($method === 'GET' && $route === '/v1/messages') {
            $current = $this->characterAuth->requireCurrentAccount($session);
            return $this->response(200, $this->messages->forAccount($current['account']));
        }

        if ($method === 'POST' && $route === '/v1/messages') {
            $current = $this->characterAuth->requireMutationAccount($headers, $session);
            return $this->response(
                201,
                $this->messages->sendForAccount($current['account'], $body));
        }

        if ($method === 'POST'
            && preg_match(
                '#^/v1/messages/([a-f0-9]{32})/read$#',
                $route,
                $matches) === 1) {
            $current = $this->characterAuth->requireMutationAccount($headers, $session);
            return $this->response(
                200,
                $this->messages->markRead($current['account'], $matches[1]));
        }

        if ($method === 'POST' && $route === '/v1/logout') {
            return $this->response(
                200,
                $this->characterAuth->logout(
                    $headers,
                    $remoteAddress,
                    $session,
                    $destroySession));
        }

        if ($route === '/v1/admin/character-accounts/import' && $method === 'POST') {
            $this->requireAdminSignature($method, $route, $body, $headers);
            return $this->response(200, $this->characterAuth->importLegacyAccounts($body));
        }

        if ($route === '/v1/admin/word-counts' && $method === 'PUT') {
            $this->requireAdminSignature($method, $route, $body, $headers);
            return $this->response(201, $this->wordCounts->store($body));
        }

        if ($route === '/v1/admin/character-accounts' && $method === 'GET') {
            $this->requireAdminSignature($method, $route, $body, $headers);
            return $this->response(200, ['accounts' => $this->characterAuth->listAccounts()]);
        }

        if ($route === '/v1/admin/character-accounts' && $method === 'POST') {
            $this->requireAdminSignature($method, $route, $body, $headers);
            return $this->response(201, $this->characterAuth->createAccount($body));
        }

        if ($method === 'PATCH'
            && preg_match('#^/v1/admin/character-accounts/([a-f0-9]{32})$#', $route, $matches) === 1) {
            $this->requireAdminSignature($method, $route, $body, $headers);
            return $this->response(
                200,
                $this->characterAuth->updateAccount($matches[1], $body));
        }

        if ($method === 'POST' && $route === '/v1/tokens') {
            $this->requireAdminSignature($method, $route, $body, $headers);
            return $this->response(201, $this->issueToken($body));
        }

        if ($method === 'DELETE' && preg_match('#^/v1/tokens/([a-f0-9]{32})$#', $route, $matches) === 1) {
            $this->requireAdminSignature($method, $route, $body, $headers);
            $this->revokeToken($matches[1]);
            return $this->response(200, ['revoked' => true, 'token_id' => $matches[1]]);
        }

        if ($method === 'PUT' && $route === '/v1/snapshots/page') {
            $this->requireAdminSignature($method, $route, $body, $headers);
            $snapshot = $this->validateSnapshot($body, false);
            $this->storeSnapshot($snapshot);
            return $this->response(201, [
                'stored' => true,
                'source_url' => $snapshot['source_url'],
                'fetched_at' => $snapshot['fetched_at'],
            ]);
        }

        if ($method === 'GET' && $route === '/v1/snapshots/page') {
            $token = $this->authenticateBearerToken($headers);
            $this->enforceRateLimit($token['id']);
            $url = is_string($query['url'] ?? null) ? $query['url'] : '';
            $this->rpolClient->validateTargetUrl($url);
            $snapshot = $this->loadSnapshot($url);
            $this->recordAudit($token['id'], $remoteAddress, $url, 'snapshot_success');
            return $this->response(200, $snapshot);
        }

        if ($method === 'GET' && $route === '/v1/rpol/page') {
            $token = $this->authenticateBearerToken($headers);
            $this->enforceRateLimit($token['id']);
            $url = is_string($query['url'] ?? null) ? $query['url'] : '';
            if ($url === '') {
                throw new BrokerHttpException(400, 'missing_url', 'The RPOL URL is required.');
            }

            try {
                $page = $this->rpolClient->fetchPage($url);
                $this->recordAudit($token['id'], $remoteAddress, $url, 'success');
                return $this->response(200, [
                    'schema_version' => 1,
                    'source_url' => $page['url'],
                    'content_type' => $page['content_type'],
                    'fetched_at' => gmdate(DATE_ATOM),
                    'content_base64' => base64_encode($page['html']),
                ]);
            } catch (InvalidArgumentException $exception) {
                $this->recordAudit($token['id'], $remoteAddress, $url, 'rejected');
                throw new BrokerHttpException(400, 'invalid_rpol_url', $exception->getMessage(), $exception);
            } catch (Throwable $exception) {
                $this->recordAudit($token['id'], $remoteAddress, $url, 'upstream_failure');
                throw new BrokerHttpException(
                    502,
                    'rpol_unavailable',
                    'The broker could not retrieve the requested RPOL page.',
                    $exception);
            }
        }

        throw new BrokerHttpException(404, 'not_found', 'The requested broker endpoint was not found.');
    }

    private function validateSnapshot(array $snapshot, bool $requireFresh): array
    {
        $required = [
            'schema_version', 'game_id', 'source_url', 'fetched_at', 'content_type',
            'content_sha256', 'content_base64', 'signature_algorithm', 'signature',
        ];
        foreach ($required as $key) {
            if (!array_key_exists($key, $snapshot) || (!is_int($snapshot[$key]) && !is_string($snapshot[$key]))) {
                throw new BrokerHttpException(400, 'invalid_snapshot', 'The snapshot schema is incomplete.');
            }
        }

        $url = (string)$snapshot['source_url'];
        $this->rpolClient->validateTargetUrl($url);
        if ((int)$snapshot['schema_version'] !== 1
            || (string)$snapshot['game_id'] !== (string)$this->config['rpol']['game_id']
            || (string)$snapshot['signature_algorithm'] !== 'HMAC-SHA256'
            || !str_starts_with(strtolower((string)$snapshot['content_type']), 'text/html')) {
            throw new BrokerHttpException(400, 'invalid_snapshot', 'The snapshot metadata is invalid.');
        }

        $fetchedAt = strtotime((string)$snapshot['fetched_at']);
        if ($fetchedAt === false || $fetchedAt > time() + 300) {
            throw new BrokerHttpException(400, 'invalid_snapshot_time', 'The snapshot timestamp is invalid.');
        }
        if ($requireFresh && time() - $fetchedAt > (int)($this->apiConfig['snapshot_max_age_seconds'] ?? 86400)) {
            throw new BrokerHttpException(410, 'snapshot_stale', 'The stored RPOL snapshot is stale.');
        }

        $content = base64_decode((string)$snapshot['content_base64'], true);
        if ($content === false || strlen($content) === 0 || strlen($content) > 5 * 1024 * 1024
            || !hash_equals(hash('sha256', $content), strtolower((string)$snapshot['content_sha256']))) {
            throw new BrokerHttpException(400, 'invalid_snapshot_content', 'The snapshot content failed validation.');
        }
        foreach (['username', 'password'] as $credentialKey) {
            $credential = (string)($this->config['rpol'][$credentialKey] ?? '');
            if ($credential !== '' && stripos($content, $credential) !== false) {
                throw new BrokerHttpException(400, 'snapshot_contains_secret', 'The snapshot contains a configured credential value.');
            }
        }
        if (preg_match('#<form\b[^>]*?/login\.cgi#i', $content) === 1) {
            throw new BrokerHttpException(400, 'snapshot_contains_login', 'The snapshot contains an RPOL login form.');
        }

        $canonical = implode("\n", [
            (string)$snapshot['schema_version'], (string)$snapshot['game_id'], $url,
            (string)$snapshot['fetched_at'], (string)$snapshot['content_type'],
            strtolower((string)$snapshot['content_sha256']),
        ]);
        $expected = hash_hmac('sha256', $canonical, $this->snapshotSigningKey(), false);
        if (!hash_equals($expected, strtolower((string)$snapshot['signature']))) {
            throw new BrokerHttpException(400, 'invalid_snapshot_signature', 'The snapshot signature is invalid.');
        }

        return $snapshot;
    }

    private function storeSnapshot(array $snapshot): void
    {
        $directory = $this->snapshotDirectory();
        if (!is_dir($directory) && !mkdir($directory, 0700, true) && !is_dir($directory)) {
            throw new RuntimeException('Unable to create the private snapshot directory.');
        }
        chmod($directory, 0700);
        $path = $directory . '/' . hash('sha256', (string)$snapshot['source_url']) . '.json';
        $temporaryPath = $path . '.tmp-' . bin2hex(random_bytes(6));
        $json = json_encode($snapshot, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
        if (file_put_contents($temporaryPath, $json, LOCK_EX) === false) {
            throw new RuntimeException('Unable to stage the RPOL snapshot.');
        }
        chmod($temporaryPath, 0600);
        if (!rename($temporaryPath, $path)) {
            @unlink($temporaryPath);
            throw new RuntimeException('Unable to promote the RPOL snapshot.');
        }
        chmod($path, 0600);
        $this->pruneExpiredSnapshots($path);
    }

    private function loadSnapshot(string $url): array
    {
        $path = $this->snapshotDirectory() . '/' . hash('sha256', $url) . '.json';
        if (!is_file($path)) {
            throw new BrokerHttpException(404, 'snapshot_not_found', 'No RPOL snapshot is available for the requested URL.');
        }
        $json = file_get_contents($path, false, null, 0, 8 * 1024 * 1024);
        if ($json === false) {
            throw new RuntimeException('Unable to read the stored RPOL snapshot.');
        }
        $snapshot = json_decode($json, true, 32, JSON_THROW_ON_ERROR);
        if (!is_array($snapshot) || (string)($snapshot['source_url'] ?? '') !== $url) {
            throw new BrokerHttpException(400, 'invalid_snapshot', 'The stored RPOL snapshot is invalid.');
        }
        return $this->validateSnapshot($snapshot, true);
    }

    private function snapshotDirectory(): string
    {
        return (string)($this->apiConfig['snapshot_directory'] ?? (__DIR__ . '/snapshots'));
    }

    private function pruneExpiredSnapshots(string $preservePath): void
    {
        $maximumAge = max(1, (int)($this->apiConfig['snapshot_max_age_seconds'] ?? 86400));
        $configuredRetention = (int)($this->apiConfig['snapshot_retention_seconds'] ?? 604800);
        $retentionSeconds = max($maximumAge, $configuredRetention);
        $cutoff = time() - $retentionSeconds;
        $files = glob($this->snapshotDirectory() . '/*.json');
        if (!is_array($files)) {
            return;
        }

        foreach ($files as $path) {
            if ($path === $preservePath
                || preg_match('/[\\\\\/][a-f0-9]{64}\.json$/D', $path) !== 1) {
                continue;
            }
            $metadata = @lstat($path);
            if (!is_array($metadata) || (int)$metadata['mtime'] >= $cutoff) {
                continue;
            }
            if (!@unlink($path)) {
                throw new RuntimeException('Unable to remove an expired RPOL snapshot.');
            }
        }
    }

    private function snapshotSigningKey(): string
    {
        $key = base64_decode((string)($this->apiConfig['snapshot_signing_key'] ?? ''), true);
        if ($key === false || strlen($key) < 32) {
            throw new RuntimeException('The snapshot signing key is not configured.');
        }
        return $key;
    }

    private function snapshotSigningConfigured(): bool
    {
        try {
            return strlen($this->snapshotSigningKey()) >= 32;
        } catch (Throwable) {
            return false;
        }
    }

    private function snapshotCount(): int
    {
        $files = glob($this->snapshotDirectory() . '/*.json');
        return is_array($files) ? count($files) : 0;
    }

    private function issueToken(array $body): array
    {
        $label = trim((string)($body['label'] ?? ''));
        if ($label === '' || strlen($label) > 100) {
            throw new BrokerHttpException(400, 'invalid_label', 'A token label of 1-100 characters is required.');
        }

        $requestedDays = filter_var(
            $body['expires_in_days'] ?? $this->apiConfig['default_token_lifetime_days'],
            FILTER_VALIDATE_INT,
            ['options' => ['min_range' => 1, 'max_range' => (int)$this->apiConfig['max_token_lifetime_days']]]);
        if ($requestedDays === false) {
            throw new BrokerHttpException(400, 'invalid_expiration', 'The token lifetime is outside the approved range.');
        }

        $tokenId = bin2hex(random_bytes(16));
        $token = 'pa_' . $this->base64UrlEncode(random_bytes(32));
        $now = time();
        $expiresAt = $now + ((int)$requestedDays * 86400);
        $statement = $this->database->prepare(
            'INSERT INTO api_tokens (id, label, token_hash, created_at, expires_at) VALUES (?, ?, ?, ?, ?)');
        $statement->execute([$tokenId, $label, hash('sha256', $token), $now, $expiresAt]);

        return [
            'token_id' => $tokenId,
            'token' => $token,
            'expires_at' => gmdate(DATE_ATOM, $expiresAt),
        ];
    }

    private function revokeToken(string $tokenId): void
    {
        $statement = $this->database->prepare(
            'UPDATE api_tokens SET revoked_at = ? WHERE id = ? AND revoked_at IS NULL');
        $statement->execute([time(), $tokenId]);
        if ($statement->rowCount() !== 1) {
            throw new BrokerHttpException(404, 'token_not_found', 'The active token was not found.');
        }
    }

    private function authenticateBearerToken(array $headers): array
    {
        $authorization = trim((string)($headers['authorization'] ?? ''));
        if (preg_match('/^Bearer\s+(pa_[A-Za-z0-9_-]{43})$/', $authorization, $matches) !== 1) {
            throw new BrokerHttpException(401, 'invalid_token', 'A valid bearer token is required.');
        }

        $tokenHash = hash('sha256', $matches[1]);
        $statement = $this->database->prepare(
            'SELECT id, expires_at, revoked_at FROM api_tokens WHERE token_hash = ? LIMIT 1');
        $statement->execute([$tokenHash]);
        $token = $statement->fetch();
        if (!is_array($token)
            || $token['revoked_at'] !== null
            || (int)$token['expires_at'] <= time()) {
            throw new BrokerHttpException(401, 'invalid_token', 'A valid bearer token is required.');
        }

        $this->database->prepare('UPDATE api_tokens SET last_used_at = ? WHERE id = ?')
            ->execute([time(), $token['id']]);
        return $token;
    }

    private function enforceRateLimit(string $tokenId): void
    {
        $windowStart = intdiv(time(), 60) * 60;
        $transactionStarted = false;
        try {
            $this->database->exec('BEGIN IMMEDIATE');
            $transactionStarted = true;
            $this->database->prepare(
                'INSERT INTO rate_limits (token_id, window_start, request_count) VALUES (?, ?, 1)
                 ON CONFLICT(token_id, window_start) DO UPDATE SET request_count = request_count + 1')
                ->execute([$tokenId, $windowStart]);
            $statement = $this->database->prepare(
                'SELECT request_count FROM rate_limits WHERE token_id = ? AND window_start = ?');
            $statement->execute([$tokenId, $windowStart]);
            $requestCount = (int)$statement->fetchColumn();
            $this->database->prepare('DELETE FROM rate_limits WHERE window_start < ?')
                ->execute([$windowStart - 3600]);
            $this->database->exec('COMMIT');
            $transactionStarted = false;
        } catch (Throwable $exception) {
            if ($transactionStarted) {
                $this->database->exec('ROLLBACK');
            }
            throw $exception;
        }

        if ($requestCount > (int)$this->apiConfig['requests_per_minute']) {
            throw new BrokerHttpException(429, 'rate_limited', 'The broker request limit has been reached.');
        }
    }

    private function recordAudit(
        string $tokenId,
        string $remoteAddress,
        string $url,
        string $outcome): void
    {
        $urlParts = parse_url($url);
        $safeTarget = is_array($urlParts)
            ? (string)($urlParts['path'] ?? '/')
            : 'invalid';
        $statement = $this->database->prepare(
            'INSERT INTO audit_events (token_id, occurred_at, remote_address, target_path, outcome)
             VALUES (?, ?, ?, ?, ?)');
        $statement->execute([$tokenId, time(), $remoteAddress, $safeTarget, $outcome]);
        $this->database->prepare('DELETE FROM audit_events WHERE occurred_at < ?')
            ->execute([time() - (30 * 86400)]);
    }

    private function requireAdminSignature(
        string $method,
        string $route,
        array $body,
        array $headers): void
    {
        $configuredKey = (string)($this->apiConfig['admin_key'] ?? '');
        $timestamp = (string)($headers['admin-timestamp'] ?? '');
        $nonce = strtolower((string)($headers['admin-nonce'] ?? ''));
        $providedSignature = strtolower((string)($headers['admin-signature'] ?? ''));
        if ($configuredKey === '' || str_starts_with($configuredKey, 'CHANGE_ME')
            || !preg_match('/^[0-9]{10}$/', $timestamp)
            || abs(time() - (int)$timestamp) > 120
            || preg_match('/^[a-f0-9]{32}$/', $nonce) !== 1
            || preg_match('/^[a-f0-9]{64}$/', $providedSignature) !== 1) {
            throw new BrokerHttpException(403, 'admin_forbidden', 'Broker administration is not authorized.');
        }

        $bodyJson = json_encode(
            $body,
            JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_PRESERVE_ZERO_FRACTION | JSON_THROW_ON_ERROR);
        $canonical = implode("\n", [
            $timestamp,
            $nonce,
            strtoupper($method),
            $route,
            hash('sha256', $bodyJson),
        ]);
        $expectedSignature = hash_hmac('sha256', $canonical, $configuredKey);
        if (!hash_equals($expectedSignature, $providedSignature)) {
            throw new BrokerHttpException(403, 'admin_forbidden', 'Broker administration is not authorized.');
        }

        $transactionStarted = false;
        try {
            $this->database->exec('BEGIN IMMEDIATE');
            $transactionStarted = true;
            $this->database->prepare('DELETE FROM admin_request_nonces WHERE used_at < ?')
                ->execute([time() - 300]);
            $insert = $this->database->prepare(
                'INSERT INTO admin_request_nonces (nonce, used_at) VALUES (?, ?)');
            $insert->execute([$nonce, time()]);
            $this->database->exec('COMMIT');
            $transactionStarted = false;
        } catch (Throwable $exception) {
            if ($transactionStarted) {
                $this->database->exec('ROLLBACK');
            }
            if ($exception instanceof PDOException && str_contains($exception->getMessage(), 'UNIQUE')) {
                throw new BrokerHttpException(
                    403,
                    'admin_replay',
                    'The broker administration request was already used.',
                    $exception);
            }
            throw $exception;
        }
    }

    private function rpolCredentialsConfigured(): bool
    {
        foreach (['username', 'password'] as $key) {
            $value = (string)($this->config['rpol'][$key] ?? '');
            if ($value === '' || str_starts_with($value, 'CHANGE_ME')) {
                return false;
            }
        }

        return true;
    }

    private function ensureSchema(): void
    {
        $this->database->exec(
            'CREATE TABLE IF NOT EXISTS api_tokens (
                id TEXT PRIMARY KEY,
                label TEXT NOT NULL,
                token_hash TEXT NOT NULL UNIQUE,
                created_at INTEGER NOT NULL,
                expires_at INTEGER NOT NULL,
                revoked_at INTEGER NULL,
                last_used_at INTEGER NULL
            );
            CREATE TABLE IF NOT EXISTS rate_limits (
                token_id TEXT NOT NULL,
                window_start INTEGER NOT NULL,
                request_count INTEGER NOT NULL,
                PRIMARY KEY (token_id, window_start),
                FOREIGN KEY (token_id) REFERENCES api_tokens(id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS admin_request_nonces (
                nonce TEXT PRIMARY KEY,
                used_at INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS audit_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                token_id TEXT NOT NULL,
                occurred_at INTEGER NOT NULL,
                remote_address TEXT NOT NULL,
                target_path TEXT NOT NULL,
                outcome TEXT NOT NULL,
                FOREIGN KEY (token_id) REFERENCES api_tokens(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_audit_events_token_time
                ON audit_events(token_id, occurred_at);');
    }

    private function base64UrlEncode(string $bytes): string
    {
        return rtrim(strtr(base64_encode($bytes), '+/', '-_'), '=');
    }

    private function response(int $status, array $body): array
    {
        return ['status' => $status, 'body' => $body];
    }
}
