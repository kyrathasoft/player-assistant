<?php

declare(strict_types=1);

final class WordCountService
{
    private array $wordCountConfig;
    private $wordCountFetcher;

    public function __construct(
        private readonly PDO $database,
        array $wordCountConfig = [],
        ?callable $wordCountFetcher = null)
    {
        $this->wordCountConfig = array_replace([
            'source_url' => '',
            'maximum_stale_seconds' => 604800,
            'connect_timeout_seconds' => 3,
            'timeout_seconds' => 8,
            'maximum_response_bytes' => 524288,
            'status_path' => '',
            'signature_key_id' => '',
            'signature_public_key' => '',
        ], $wordCountConfig);
        $this->wordCountFetcher = $wordCountFetcher;

        $this->validateConfiguration();
    }

    public function store(array $body): array
    {
        $snapshot = $this->validate($body);
        $uploadedAt = time();
        $statement = $this->database->prepare(
            'INSERT INTO word_count_snapshots (
                id, schema_version, observed_at, counting_rule_version,
                wiki_pages, wiki_words, ic_files, ic_words, ooc_files, ooc_words, uploaded_at
             ) VALUES (1, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
             ON CONFLICT(id) DO UPDATE SET
                schema_version = excluded.schema_version,
                observed_at = excluded.observed_at,
                counting_rule_version = excluded.counting_rule_version,
                wiki_pages = excluded.wiki_pages,
                wiki_words = excluded.wiki_words,
                ic_files = excluded.ic_files,
                ic_words = excluded.ic_words,
                ooc_files = excluded.ooc_files,
                ooc_words = excluded.ooc_words,
                uploaded_at = excluded.uploaded_at');
        $statement->execute([
            $snapshot['schema_version'],
            $snapshot['observed_at'],
            $snapshot['counting_rule_version'],
            $snapshot['wiki']['pages'],
            $snapshot['wiki']['words'],
            $snapshot['ic']['files'],
            $snapshot['ic']['words'],
            $snapshot['ooc']['files'],
            $snapshot['ooc']['words'],
            $uploadedAt,
        ]);

        return $this->format($snapshot, $uploadedAt);
    }

    public function latest(): array
    {
        $snapshot = $this->loadCachedSnapshot();
        if ($snapshot === null) {
            if (!$this->canRefreshFromSource()) {
                throw new BrokerHttpException(
                    503,
                    'word_counts_unavailable',
                    'No validated campaign word-count snapshot is available.');
            }

            return $this->refreshFromSource();
        }

        if ($this->isStale($snapshot['observed_at']) && $this->canRefreshFromSource()) {
            try {
                return $this->refreshFromSource();
            } catch (Throwable) {
                return $snapshot;
            }
        }

        return $snapshot;
    }

    public function refreshNow(): array
    {
        return $this->refreshFromSource();
    }

    public function hasSnapshot(): bool
    {
        return (int)$this->database
            ->query('SELECT COUNT(*) FROM word_count_snapshots WHERE id = 1')
            ->fetchColumn() === 1;
    }

    public function refreshStatus(): array
    {
        $status = [
            'configured' => $this->canRefreshFromSource(),
            'signing_configured' => $this->signingConfigured(),
            'healthy' => null,
            'last_attempt_at' => null,
            'last_success_at' => null,
            'last_error_code' => null,
            'last_scheduler_run_at' => null,
            'last_scheduler_status' => null,
            'last_scheduler_error_code' => null,
        ];
        $path = (string)$this->wordCountConfig['status_path'];
        if ($path === '' || !is_file($path) || filesize($path) > 8192) {
            return $status;
        }

        try {
            $decoded = json_decode((string)file_get_contents($path), true, 16, JSON_THROW_ON_ERROR);
            if (!is_array($decoded)) {
                return $status;
            }
            foreach (array_keys($status) as $key) {
                if (array_key_exists($key, $decoded)
                    && $key !== 'configured'
                    && $key !== 'signing_configured') {
                    $status[$key] = $decoded[$key];
                }
            }
        } catch (Throwable) {
        }

        return $status;
    }

    public function recordSchedulerRun(bool $success, ?string $errorCode = null): void
    {
        $this->writeRefreshStatus([
            'last_scheduler_run_at' => gmdate(DATE_ATOM),
            'last_scheduler_status' => $success ? 'success' : 'failed',
            'last_scheduler_error_code' => $success ? null : $this->sanitizeErrorCode($errorCode),
        ]);
    }

    private function validate(array $body): array
    {
        if (($body['schema_version'] ?? null) !== 1) {
            throw new BrokerHttpException(
                400,
                'invalid_word_counts',
                'The word-count snapshot schema version is invalid.');
        }

        $observedAt = $body['observed_at'] ?? null;
        if (!is_string($observedAt)
            || strlen($observedAt) > 40
            || preg_match(
                '/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,9})?(?:Z|[+-]\d{2}:\d{2})$/D',
                $observedAt) !== 1) {
            throw new BrokerHttpException(400, 'invalid_word_counts', 'The observation time is invalid.');
        }
        try {
            $observed = new DateTimeImmutable($observedAt);
        } catch (Throwable) {
            throw new BrokerHttpException(400, 'invalid_word_counts', 'The observation time is invalid.');
        }
        if ($observed->getTimestamp() > time() + 300) {
            throw new BrokerHttpException(400, 'invalid_word_counts', 'The observation time is invalid.');
        }

        $ruleVersion = $body['counting_rule_version'] ?? null;
        if (!is_string($ruleVersion)
            || trim($ruleVersion) !== $ruleVersion
            || strlen($ruleVersion) < 1
            || strlen($ruleVersion) > 100) {
            throw new BrokerHttpException(
                400,
                'invalid_word_counts',
                'The counting-rule version is invalid.');
        }

        foreach (['wiki' => 'pages', 'ic' => 'files', 'ooc' => 'files'] as $section => $unitKey) {
            $value = $body[$section] ?? null;
            if (!is_array($value)
                || !$this->isCount($value[$unitKey] ?? null, true)
                || !$this->isCount($value['words'] ?? null, false)) {
                throw new BrokerHttpException(
                    400,
                    'invalid_word_counts',
                    'The word-count snapshot contains an invalid count.');
            }
        }

        return [
            'schema_version' => 1,
            'observed_at' => $observedAt,
            'counting_rule_version' => $ruleVersion,
            'wiki' => ['pages' => $body['wiki']['pages'], 'words' => $body['wiki']['words']],
            'ic' => ['files' => $body['ic']['files'], 'words' => $body['ic']['words']],
            'ooc' => ['files' => $body['ooc']['files'], 'words' => $body['ooc']['words']],
        ];
    }

    private function refreshFromSource(): array
    {
        $sourceUrl = (string)$this->wordCountConfig['source_url'];
        if ($sourceUrl === '') {
            throw new BrokerHttpException(
                503,
                'word_counts_unavailable',
                'No validated campaign word-count snapshot is available.');
        }

        $attemptAt = gmdate(DATE_ATOM);
        try {
            $payload = $this->loadSourcePayload($sourceUrl);
            $stored = $this->store($payload);
            $this->writeRefreshStatus([
                'healthy' => true,
                'last_attempt_at' => $attemptAt,
                'last_success_at' => gmdate(DATE_ATOM),
                'last_error_code' => null,
            ]);
            return $stored;
        } catch (Throwable $error) {
            $this->writeRefreshStatus([
                'healthy' => false,
                'last_attempt_at' => $attemptAt,
                'last_error_code' => $this->classifyRefreshError($error),
            ]);
            throw $error;
        }
    }

    private function loadSourcePayload(string $sourceUrl): array
    {
        $json = is_callable($this->wordCountFetcher)
            ? $this->fetchWithCallback($sourceUrl)
            : $this->fetchFromUrl($sourceUrl);
        $decoded = json_decode($json, true, 32, JSON_THROW_ON_ERROR);
        if (!is_array($decoded)) {
            throw new RuntimeException('The word-count source did not return an object.');
        }

        if ($this->signingConfigured()) {
            $decoded = $this->verifySignedEnvelope($decoded);
        }

        return $this->validate($decoded);
    }

    private function canRefreshFromSource(): bool
    {
        return (string)$this->wordCountConfig['source_url'] !== '';
    }

    private function signingConfigured(): bool
    {
        return (string)$this->wordCountConfig['signature_key_id'] !== ''
            && (string)$this->wordCountConfig['signature_public_key'] !== '';
    }

    private function verifySignedEnvelope(array $envelope): array
    {
        $payload = $envelope['payload'] ?? null;
        $signature = $envelope['signature'] ?? null;
        if (!is_array($payload)
            || !is_array($signature)
            || ($signature['algorithm'] ?? null) !== 'Ed25519'
            || ($signature['key_id'] ?? null) !== $this->wordCountConfig['signature_key_id']
            || !is_string($signature['value'] ?? null)) {
            throw new RuntimeException('The word-count source signature envelope is invalid.');
        }

        $signatureBytes = base64_decode($signature['value'], true);
        $publicKey = base64_decode((string)$this->wordCountConfig['signature_public_key'], true);
        if (!is_string($signatureBytes)
            || strlen($signatureBytes) !== SODIUM_CRYPTO_SIGN_BYTES
            || !is_string($publicKey)
            || strlen($publicKey) !== SODIUM_CRYPTO_SIGN_PUBLICKEYBYTES) {
            throw new RuntimeException('The word-count source signature is invalid.');
        }

        $canonicalPayload = json_encode(
            $payload,
            JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
        if (!sodium_crypto_sign_verify_detached($signatureBytes, $canonicalPayload, $publicKey)) {
            throw new RuntimeException('The word-count source signature verification failed.');
        }

        return $payload;
    }

    private function isStale(string $observedAt): bool
    {
        $observedTimestamp = strtotime($observedAt);
        return $observedTimestamp === false
            || time() - $observedTimestamp > (int)$this->wordCountConfig['maximum_stale_seconds'];
    }

    private function isCount(mixed $value, bool $mustBePositive): bool
    {
        return is_int($value)
            && $value <= 1000000000
            && ($mustBePositive ? $value > 0 : $value >= 0);
    }

    private function format(array $snapshot, int $uploadedAt): array
    {
        return $snapshot + ['uploaded_at' => gmdate(DATE_ATOM, $uploadedAt)];
    }

    private function fetchWithCallback(string $sourceUrl): string
    {
        $content = (string)($this->wordCountFetcher)($sourceUrl);
        if (!is_string($content)
            || $content === ''
            || strlen($content) > (int)$this->wordCountConfig['maximum_response_bytes']) {
            throw new RuntimeException('The word-count source fixture returned invalid payload data.');
        }

        return $content;
    }

    private function fetchFromUrl(string $sourceUrl): string
    {
        if (!extension_loaded('curl')) {
            throw new RuntimeException('The PHP cURL extension is required for word-count auto-refresh.');
        }
        $this->validateSourceUrl($sourceUrl);
        $content = '';
        $tooLarge = false;
        $maximumResponseBytes = (int)$this->wordCountConfig['maximum_response_bytes'];

        $handle = curl_init($sourceUrl);
        if ($handle === false) {
            throw new RuntimeException('The word-count source request could not be initialized.');
        }
        curl_setopt_array($handle, [
            CURLOPT_RETURNTRANSFER => false,
            CURLOPT_FOLLOWLOCATION => false,
            CURLOPT_PROTOCOLS => CURLPROTO_HTTPS,
            CURLOPT_REDIR_PROTOCOLS => CURLPROTO_HTTPS,
            CURLOPT_CONNECTTIMEOUT => (int)$this->wordCountConfig['connect_timeout_seconds'],
            CURLOPT_TIMEOUT => (int)$this->wordCountConfig['timeout_seconds'],
            CURLOPT_ENCODING => '',
            CURLOPT_HTTPHEADER => ['Accept: application/json'],
            CURLOPT_USERAGENT => 'PlayerAssistant-WordCountBroker/1.0',
            CURLOPT_WRITEFUNCTION => static function ($curl, string $chunk) use (
                &$content,
                &$tooLarge,
                $maximumResponseBytes): int {
                if (strlen($content) + strlen($chunk) > $maximumResponseBytes) {
                    $tooLarge = true;
                    return 0;
                }
                $content .= $chunk;
                return strlen($chunk);
            },
        ]);

        try {
            $success = curl_exec($handle);
            $status = (int)curl_getinfo($handle, CURLINFO_RESPONSE_CODE);
            $contentType = strtolower(
                trim(explode(';', (string)curl_getinfo($handle, CURLINFO_CONTENT_TYPE), 2)[0]));
            if ($success === false || $tooLarge || $status !== 200 || $content === '') {
                throw new RuntimeException('The word-count source returned an unusable response.');
            }
            if ($contentType !== 'application/json' && $contentType !== 'text/json') {
                throw new RuntimeException('The word-count source returned an unexpected content type.');
            }
            return $content;
        } finally {
            curl_close($handle);
        }
    }

    private function validateConfiguration(): void
    {
        foreach ([
            'maximum_stale_seconds',
            'connect_timeout_seconds',
            'timeout_seconds',
            'maximum_response_bytes',
        ] as $key) {
            $validated = filter_var($this->wordCountConfig[$key] ?? null, FILTER_VALIDATE_INT, [
                'options' => ['min_range' => 1],
            ]);
            if ($validated === false) {
                throw new RuntimeException("Word-count setting '$key' must be a positive integer.");
            }
            $this->wordCountConfig[$key] = $validated;
        }

        if ((string)$this->wordCountConfig['source_url'] !== '') {
            $this->validateSourceUrl((string)$this->wordCountConfig['source_url']);
            if ((int)$this->wordCountConfig['timeout_seconds']
                < (int)$this->wordCountConfig['connect_timeout_seconds']) {
                throw new RuntimeException('The word-count timeout settings are invalid.');
            }
        }

        foreach (['status_path', 'signature_key_id', 'signature_public_key'] as $key) {
            if (!is_string($this->wordCountConfig[$key] ?? null)
                || strlen((string)$this->wordCountConfig[$key]) > 4096) {
                throw new RuntimeException("Word-count setting '$key' is invalid.");
            }
        }
        $hasKeyId = (string)$this->wordCountConfig['signature_key_id'] !== '';
        $hasPublicKey = (string)$this->wordCountConfig['signature_public_key'] !== '';
        if ($hasKeyId !== $hasPublicKey) {
            throw new RuntimeException('Word-count signature settings must be configured together.');
        }
        if ($hasPublicKey) {
            if (!extension_loaded('sodium')) {
                throw new RuntimeException('The PHP Sodium extension is required for signed word-count refresh.');
            }
            $publicKey = base64_decode((string)$this->wordCountConfig['signature_public_key'], true);
            if (!is_string($publicKey) || strlen($publicKey) !== SODIUM_CRYPTO_SIGN_PUBLICKEYBYTES) {
                throw new RuntimeException('The word-count signature public key is invalid.');
            }
            if (preg_match('/^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/D', (string)$this->wordCountConfig['signature_key_id']) !== 1) {
                throw new RuntimeException('The word-count signature key identifier is invalid.');
            }
        }
    }

    private function writeRefreshStatus(array $updates): void
    {
        $path = (string)$this->wordCountConfig['status_path'];
        if ($path === '') {
            return;
        }

        try {
            $status = $this->refreshStatus();
            foreach ($updates as $key => $value) {
                if (array_key_exists($key, $status)) {
                    $status[$key] = $value;
                }
            }
            $directory = dirname($path);
            if (!is_dir($directory)) {
                return;
            }
            $temporaryPath = $path . '.tmp-' . bin2hex(random_bytes(8));
            $json = json_encode(
                $status,
                JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
            if (file_put_contents($temporaryPath, $json, LOCK_EX) === false) {
                return;
            }
            @chmod($temporaryPath, 0600);
            if (!@rename($temporaryPath, $path)) {
                @unlink($temporaryPath);
            }
        } catch (Throwable) {
        }
    }

    private function classifyRefreshError(Throwable $error): string
    {
        $message = strtolower($error->getMessage());
        if (str_contains($message, 'signature')) {
            return 'signature_invalid';
        }
        if ($error instanceof JsonException || str_contains($message, 'invalid')) {
            return 'source_invalid';
        }
        if (str_contains($message, 'curl')
            || str_contains($message, 'response')
            || str_contains($message, 'request')) {
            return 'source_unavailable';
        }
        return 'refresh_failed';
    }

    private function sanitizeErrorCode(?string $errorCode): string
    {
        return in_array($errorCode, [
            'signature_invalid',
            'source_invalid',
            'source_unavailable',
            'refresh_failed',
            'scheduler_failed',
        ], true) ? (string)$errorCode : 'scheduler_failed';
    }

    private function validateSourceUrl(string $sourceUrl): void
    {
        $parts = parse_url($sourceUrl);
        if (!is_array($parts)
            || (string)($parts['scheme'] ?? '') !== 'https'
            || (string)($parts['host'] ?? '') === ''
            || isset($parts['user'])
            || isset($parts['pass'])
            || (isset($parts['port']) && (int)$parts['port'] !== 443)) {
            throw new RuntimeException('The word-count source URL is invalid.');
        }
    }

    private function loadCachedSnapshot(): ?array
    {
        $statement = $this->database->query(
            'SELECT schema_version, observed_at, counting_rule_version,
                    wiki_pages, wiki_words, ic_files, ic_words, ooc_files, ooc_words, uploaded_at
             FROM word_count_snapshots WHERE id = 1');
        $row = $statement->fetch();
        if (!is_array($row)) {
            return null;
        }

        return $this->format([
            'schema_version' => (int)$row['schema_version'],
            'observed_at' => (string)$row['observed_at'],
            'counting_rule_version' => (string)$row['counting_rule_version'],
            'wiki' => ['pages' => (int)$row['wiki_pages'], 'words' => (int)$row['wiki_words']],
            'ic' => ['files' => (int)$row['ic_files'], 'words' => (int)$row['ic_words']],
            'ooc' => ['files' => (int)$row['ooc_files'], 'words' => (int)$row['ooc_words']],
        ], (int)$row['uploaded_at']);
    }
}
