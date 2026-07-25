<?php

declare(strict_types=1);

final class XpTrackingService
{
    private const CACHE_KEY = 'current';
    private const MAXIMUM_CHARACTERS = 200;
    private const CHARACTER_KEY_ALIASES = [
        'max' => 'maximilian',
    ];

    private array $xpConfig;
    private $markdownFetcher;

    public function __construct(
        private readonly PDO $database,
        array $xpConfig,
        ?callable $markdownFetcher = null)
    {
        $this->xpConfig = array_replace([
            'source_url' => '',
            'connect_timeout_seconds' => 3,
            'timeout_seconds' => 8,
            'maximum_response_bytes' => 524288,
            'cache_ttl_seconds' => 300,
            'maximum_stale_seconds' => 86400,
        ], $xpConfig);
        $this->markdownFetcher = $markdownFetcher;
        $this->validateConfiguration();
        $this->ensureSchema();
    }

    public function getForAccount(array $account): array
    {
        $role = (string)($account['role'] ?? '');
        $characterKey = (string)($account['character_key'] ?? '');
        if (!in_array($role, ['player', 'dm'], true)
            || preg_match('/^[a-z0-9][a-z0-9._:-]{0,99}$/', $characterKey) !== 1) {
            throw new BrokerHttpException(
                403,
                'xp_not_authorized',
                'XP access is not authorized for this account.');
        }

        $snapshot = $this->loadCurrentSnapshot();
        $baseResponse = [
            'schema_version' => 1,
            'date_label' => $snapshot['date_label'],
            'fetched_at' => gmdate(DATE_ATOM, $snapshot['fetched_at']),
            'stale' => $snapshot['stale'],
        ];

        if ($role === 'dm') {
            return $baseResponse + [
                'scope' => 'party',
                'characters' => $snapshot['characters'],
            ];
        }

        $matches = array_values(array_filter(
            $snapshot['characters'],
            fn(array $character): bool => hash_equals(
                $characterKey,
                $this->characterKeyForName((string)$character['character_name']))));
        if (count($matches) !== 1) {
            throw new BrokerHttpException(
                403,
                'xp_not_authorized',
                'No unambiguous XP total is authorized for this account.');
        }

        return $baseResponse + [
            'scope' => 'character',
            'character' => $matches[0],
        ];
    }

    public function isConfigured(): bool
    {
        return (string)$this->xpConfig['source_url'] !== '';
    }

    private function loadCurrentSnapshot(): array
    {
        if (!$this->isConfigured()) {
            throw new BrokerHttpException(
                503,
                'xp_unavailable',
                'XP totals are not configured on the server.');
        }

        $cached = $this->loadCachedSnapshot();
        $now = time();
        if ($cached !== null
            && $now - $cached['fetched_at'] <= (int)$this->xpConfig['cache_ttl_seconds']) {
            return $cached + ['stale' => false];
        }

        try {
            $markdown = $this->fetchMarkdown();
            $parsed = $this->parseSnapshot($markdown);
            $snapshot = [
                'date_label' => $parsed['date_label'],
                'characters' => $parsed['characters'],
                'fetched_at' => $now,
            ];
            $this->storeCachedSnapshot($snapshot);
            return $snapshot + ['stale' => false];
        } catch (Throwable $exception) {
            if ($cached !== null
                && $now - $cached['fetched_at'] <= (int)$this->xpConfig['maximum_stale_seconds']) {
                return $cached + ['stale' => true];
            }
            throw new BrokerHttpException(
                502,
                'xp_unavailable',
                'Current XP totals could not be loaded.',
                $exception);
        }
    }

    private function fetchMarkdown(): string
    {
        if ($this->markdownFetcher !== null) {
            $markdown = ($this->markdownFetcher)((string)$this->xpConfig['source_url']);
            if (!is_string($markdown)
                || strlen($markdown) === 0
                || strlen($markdown) > (int)$this->xpConfig['maximum_response_bytes']) {
                throw new RuntimeException('The XP markdown fixture was invalid.');
            }
            return $markdown;
        }

        $page = $this->fetchUrl(
            (string)$this->xpConfig['source_url'],
            ['text/html', 'text/markdown', 'text/plain']);
        if ($page['content_type'] === 'text/markdown' || $page['content_type'] === 'text/plain') {
            return $page['content'];
        }

        if (preg_match(
            '/window\.preloadPage=f\("(?<url>https:\/\/[^"]+\.md)"\)/',
            $page['content'],
            $matches) !== 1) {
            throw new RuntimeException('The XP page did not expose a markdown document.');
        }
        $markdownUrl = (string)$matches['url'];
        $this->validateMarkdownUrl($markdownUrl);
        $markdown = $this->fetchUrl($markdownUrl, ['text/markdown', 'text/plain']);
        return $markdown['content'];
    }

    private function fetchUrl(string $url, array $acceptedContentTypes): array
    {
        if (!extension_loaded('curl')) {
            throw new RuntimeException('The PHP cURL extension is required for XP tracking.');
        }

        $content = '';
        $tooLarge = false;
        $maximumBytes = (int)$this->xpConfig['maximum_response_bytes'];
        $handle = curl_init($url);
        if ($handle === false) {
            throw new RuntimeException('The XP request could not be initialized.');
        }

        curl_setopt_array($handle, [
            CURLOPT_RETURNTRANSFER => false,
            CURLOPT_FOLLOWLOCATION => false,
            CURLOPT_PROTOCOLS => CURLPROTO_HTTPS,
            CURLOPT_REDIR_PROTOCOLS => CURLPROTO_HTTPS,
            CURLOPT_CONNECTTIMEOUT => (int)$this->xpConfig['connect_timeout_seconds'],
            CURLOPT_TIMEOUT => (int)$this->xpConfig['timeout_seconds'],
            CURLOPT_ENCODING => '',
            CURLOPT_HTTPHEADER => [
                'Accept: ' . implode(', ', $acceptedContentTypes),
                'Cache-Control: no-cache',
            ],
            CURLOPT_USERAGENT => 'PlayerAssistant-XpBroker/1.0',
            CURLOPT_WRITEFUNCTION => static function ($curl, string $chunk) use (
                &$content,
                &$tooLarge,
                $maximumBytes): int {
                if (strlen($content) + strlen($chunk) > $maximumBytes) {
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
            $contentType = strtolower(trim(explode(
                ';',
                (string)curl_getinfo($handle, CURLINFO_CONTENT_TYPE),
                2)[0]));
            if ($success === false || $tooLarge || $status !== 200 || $content === '') {
                throw new RuntimeException('The XP source returned an unusable response.');
            }
            if (!in_array($contentType, $acceptedContentTypes, true)) {
                throw new RuntimeException('The XP source returned an unexpected content type.');
            }
            return ['content' => $content, 'content_type' => $contentType];
        } finally {
            curl_close($handle);
        }
    }

    private function parseSnapshot(string $markdown): array
    {
        $lines = preg_split('/\r\n|\r|\n/', $markdown);
        if (!is_array($lines)) {
            throw new RuntimeException('The XP markdown could not be read.');
        }

        foreach ($lines as $index => $line) {
            $dateLabel = trim(ltrim(trim((string)$line), '#'));
            if (!str_starts_with(strtolower($dateLabel), 'as of ')) {
                continue;
            }
            if ($dateLabel === ''
                || strlen($dateLabel) > 200
                || preg_match('//u', $dateLabel) !== 1
                || preg_match('/[\x00-\x1F\x7F]/u', $dateLabel) === 1) {
                throw new RuntimeException('The XP date label was invalid.');
            }

            $headerIndex = $this->findTableHeader($lines, $index + 1);
            if ($headerIndex < 0) {
                throw new RuntimeException('The latest XP date did not have a valid markdown table.');
            }
            return [
                'date_label' => $dateLabel,
                'characters' => $this->parseTable($lines, $headerIndex),
            ];
        }

        throw new RuntimeException('The XP markdown did not contain an As of section.');
    }

    private function findTableHeader(array $lines, int $startIndex): int
    {
        for ($index = $startIndex; $index < count($lines); $index++) {
            $cells = $this->splitTableRow((string)$lines[$index]);
            if ($cells !== []
                && $this->findCellIndex($cells, 'Name') >= 0
                && $this->findCellIndex($cells, 'XP Total') >= 0) {
                return $index;
            }
            if (str_starts_with(strtolower(trim(ltrim(trim((string)$lines[$index]), '#'))), 'as of ')) {
                return -1;
            }
        }
        return -1;
    }

    private function parseTable(array $lines, int $headerIndex): array
    {
        $headers = $this->splitTableRow((string)$lines[$headerIndex]);
        $nameIndex = $this->findCellIndex($headers, 'Name');
        $xpIndex = $this->findCellIndex($headers, 'XP Total');
        $characters = [];
        $seenNames = [];

        for ($index = $headerIndex + 1; $index < count($lines); $index++) {
            $line = (string)$lines[$index];
            if (!str_starts_with(ltrim($line), '|')) {
                break;
            }
            $cells = $this->splitTableRow($line);
            if ($cells === [] || $this->isSeparatorRow($cells)) {
                continue;
            }
            if (count($cells) <= max($nameIndex, $xpIndex)) {
                throw new RuntimeException('An XP table row was incomplete.');
            }

            $name = $this->cleanMarkdownCell($cells[$nameIndex]);
            $xpDigits = preg_replace('/\D+/', '', $cells[$xpIndex]);
            if ($name === ''
                || strlen($name) > 100
                || preg_match('//u', $name) !== 1
                || preg_match('/[\x00-\x1F\x7F]/u', $name) === 1
                || !is_string($xpDigits)
                || $xpDigits === ''
                || filter_var(
                    $xpDigits,
                    FILTER_VALIDATE_INT,
                    ['options' => ['min_range' => 0, 'max_range' => PHP_INT_MAX]]) === false) {
                throw new RuntimeException('An XP table row was invalid.');
            }
            $normalizedName = strtolower($name);
            if (isset($seenNames[$normalizedName])) {
                throw new RuntimeException('The XP table contained a duplicate character.');
            }
            $seenNames[$normalizedName] = true;
            $characters[] = [
                'character_name' => $name,
                'xp_total' => (int)$xpDigits,
            ];
            if (count($characters) > self::MAXIMUM_CHARACTERS) {
                throw new RuntimeException('The XP table contained too many characters.');
            }
        }

        if ($characters === []) {
            throw new RuntimeException('The XP table did not contain any character totals.');
        }
        return $characters;
    }

    private function splitTableRow(string $line): array
    {
        $trimmed = trim($line);
        if (!str_starts_with($trimmed, '|') || !str_ends_with($trimmed, '|')) {
            return [];
        }
        return array_map('trim', explode('|', trim($trimmed, '|')));
    }

    private function findCellIndex(array $cells, string $expected): int
    {
        foreach ($cells as $index => $cell) {
            if (strcasecmp((string)$cell, $expected) === 0) {
                return $index;
            }
        }
        return -1;
    }

    private function isSeparatorRow(array $cells): bool
    {
        foreach ($cells as $cell) {
            if ((string)$cell === '' || preg_match('/^[-: ]+$/', (string)$cell) !== 1) {
                return false;
            }
        }
        return true;
    }

    private function cleanMarkdownCell(string $value): string
    {
        $cleaned = trim($value);
        if (str_starts_with($cleaned, '[[') && str_ends_with($cleaned, ']]')) {
            $cleaned = substr($cleaned, 2, -2);
            $aliasIndex = strrpos($cleaned, '|');
            if ($aliasIndex !== false) {
                $cleaned = substr($cleaned, $aliasIndex + 1);
            }
        }
        return trim($cleaned);
    }

    private function characterKeyForName(string $name): string
    {
        $firstName = explode(' ', trim($name), 2)[0];
        $key = strtolower((string)preg_replace('/[^A-Za-z0-9]+/', '-', $firstName));
        $key = trim($key, '-');
        return self::CHARACTER_KEY_ALIASES[$key] ?? $key;
    }

    private function loadCachedSnapshot(): ?array
    {
        $statement = $this->database->prepare(
            'SELECT fetched_at, payload_json, content_sha256
             FROM xp_tracking_cache WHERE cache_key = ? LIMIT 1');
        $statement->execute([self::CACHE_KEY]);
        $row = $statement->fetch();
        if (!is_array($row)) {
            return null;
        }

        try {
            if (!hash_equals(
                strtolower((string)$row['content_sha256']),
                hash('sha256', (string)$row['payload_json']))) {
                return null;
            }
            $payload = json_decode((string)$row['payload_json'], true, 32, JSON_THROW_ON_ERROR);
            if (!is_array($payload)
                || !is_string($payload['date_label'] ?? null)
                || !is_array($payload['characters'] ?? null)
                || (int)$row['fetched_at'] <= 0) {
                return null;
            }
            $validated = $this->parseSnapshot($this->snapshotToMarkdown($payload));
            return [
                'date_label' => $validated['date_label'],
                'characters' => $validated['characters'],
                'fetched_at' => (int)$row['fetched_at'],
            ];
        } catch (Throwable) {
            return null;
        }
    }

    private function snapshotToMarkdown(array $payload): string
    {
        $lines = [
            (string)$payload['date_label'],
            '',
            '| Name | XP Total |',
            '| --- | ---: |',
        ];
        foreach ($payload['characters'] as $character) {
            if (!is_array($character)) {
                throw new RuntimeException('The cached XP snapshot was invalid.');
            }
            $lines[] = sprintf(
                '| %s | %s |',
                (string)($character['character_name'] ?? ''),
                (string)($character['xp_total'] ?? ''));
        }
        return implode("\n", $lines);
    }

    private function storeCachedSnapshot(array $snapshot): void
    {
        $payload = json_encode([
            'date_label' => $snapshot['date_label'],
            'characters' => $snapshot['characters'],
        ], JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
        $this->database->prepare(
            'INSERT INTO xp_tracking_cache (cache_key, fetched_at, payload_json, content_sha256)
             VALUES (?, ?, ?, ?)
             ON CONFLICT(cache_key) DO UPDATE SET
                fetched_at = excluded.fetched_at,
                payload_json = excluded.payload_json,
                content_sha256 = excluded.content_sha256')
            ->execute([
                self::CACHE_KEY,
                (int)$snapshot['fetched_at'],
                $payload,
                hash('sha256', $payload),
            ]);
    }

    private function validateConfiguration(): void
    {
        if ($this->isConfigured()) {
            $this->validatePageUrl((string)$this->xpConfig['source_url']);
        }
        foreach ([
            'connect_timeout_seconds',
            'timeout_seconds',
            'maximum_response_bytes',
            'cache_ttl_seconds',
            'maximum_stale_seconds',
        ] as $key) {
            if (!is_int($this->xpConfig[$key]) || $this->xpConfig[$key] <= 0) {
                throw new RuntimeException("XP tracking setting '$key' must be a positive integer.");
            }
        }
        if ($this->xpConfig['timeout_seconds'] < $this->xpConfig['connect_timeout_seconds']
            || $this->xpConfig['maximum_response_bytes'] > 2 * 1024 * 1024
            || $this->xpConfig['maximum_stale_seconds'] < $this->xpConfig['cache_ttl_seconds']) {
            throw new RuntimeException('The XP tracking configuration limits are invalid.');
        }
    }

    private function validatePageUrl(string $url): void
    {
        $parts = parse_url($url);
        if (!is_array($parts)
            || (string)($parts['scheme'] ?? '') !== 'https'
            || strtolower((string)($parts['host'] ?? '')) !== 'publish.obsidian.md'
            || isset($parts['user'])
            || isset($parts['pass'])
            || (isset($parts['port']) && (int)$parts['port'] !== 443)
            || (string)($parts['path'] ?? '/') === '/'
            || isset($parts['query'])
            || isset($parts['fragment'])) {
            throw new RuntimeException('The XP source URL must be a fixed Obsidian Publish HTTPS page.');
        }
    }

    private function validateMarkdownUrl(string $url): void
    {
        $parts = parse_url($url);
        $host = strtolower((string)($parts['host'] ?? ''));
        $path = (string)($parts['path'] ?? '');
        if (!is_array($parts)
            || (string)($parts['scheme'] ?? '') !== 'https'
            || preg_match('/^publish-\d+\.obsidian\.md$/', $host) !== 1
            || !str_starts_with($path, '/access/')
            || !str_ends_with(strtolower($path), '.md')
            || isset($parts['user'])
            || isset($parts['pass'])
            || (isset($parts['port']) && (int)$parts['port'] !== 443)
            || isset($parts['query'])
            || isset($parts['fragment'])) {
            throw new RuntimeException('The XP markdown URL was not allowlisted.');
        }
    }

    private function ensureSchema(): void
    {
        $this->database->exec(
            'CREATE TABLE IF NOT EXISTS xp_tracking_cache (
                cache_key TEXT PRIMARY KEY,
                fetched_at INTEGER NOT NULL,
                payload_json TEXT NOT NULL,
                content_sha256 TEXT NOT NULL
            )');
    }
}
