<?php

declare(strict_types=1);

require_once __DIR__ . '/CorrelationContext.php';

final class PwaMonitorFailure extends RuntimeException
{
    public function __construct(public readonly string $errorCode, string $message)
    {
        parent::__construct($message);
    }
}

final class PwaSyntheticMonitor
{
    private array $config;
    private $requester;
    private $mailer;
    private $clock;
    private string $cookie = '';
    private string $correlationId;

    public function __construct(array $config, ?callable $requester = null, ?callable $mailer = null, ?callable $clock = null)
    {
        $this->correlationId = CorrelationContext::create();
        $this->config = array_replace([
            'base_url' => 'https://bryanmiller.us/scarlethorizons',
            'character_name' => '',
            'password' => '',
            'status_path' => __DIR__ . '/pwa-monitor-status.json',
            'maximum_xp_age_seconds' => 86400,
            'maximum_word_count_age_seconds' => 604800,
            'alert_cooldown_seconds' => 3600,
            'alert_email' => '',
            'alert_from' => '',
        ], $config);
        $this->requester = $requester ?? fn(string $method, string $url, array $headers, ?array $body): array => $this->curlRequest($method, $url, $headers, $body);
        $this->mailer = $mailer ?? fn(string $subject, string $body): bool => $this->sendMail($subject, $body);
        $this->clock = $clock ?? static fn(): int => time();
    }

    public function run(): array
    {
        $previous = $this->readStatus();
        $now = ($this->clock)();
        try {
            $this->validateConfiguration();
            $this->performChecks($now);
            $status = [
                'schema_version' => 1,
                'healthy' => true,
                'last_run_at' => gmdate(DATE_ATOM, $now),
                'last_success_at' => gmdate(DATE_ATOM, $now),
                'last_failure_at' => $previous['last_failure_at'] ?? null,
                'last_error_code' => null,
                'last_alert_at' => $previous['last_alert_at'] ?? null,
                'last_alert_unix' => (int)($previous['last_alert_unix'] ?? 0),
                'last_alert_result' => $previous['last_alert_result'] ?? null,
            ];
            if (($previous['healthy'] ?? null) === false) {
                $this->recordAlert($status, 'recovered', 'The authenticated production PWA monitor recovered.', $now);
            }
            $this->writeStatus($status);
            return ['status' => 'ok', 'checked_at' => $status['last_run_at']];
        } catch (Throwable $error) {
            if ($error instanceof PwaMonitorFailure && $error->errorCode === 'status_write_failed') {
                throw $error;
            }
            $failure = $error instanceof PwaMonitorFailure
                ? $error
                : new PwaMonitorFailure('monitor_internal_error', 'The monitor encountered an internal error.');
            $status = [
                'schema_version' => 1,
                'healthy' => false,
                'last_run_at' => gmdate(DATE_ATOM, $now),
                'last_success_at' => $previous['last_success_at'] ?? null,
                'last_failure_at' => gmdate(DATE_ATOM, $now),
                'last_error_code' => $this->safeCode($failure->errorCode),
                'last_alert_at' => $previous['last_alert_at'] ?? null,
                'last_alert_unix' => (int)($previous['last_alert_unix'] ?? 0),
                'last_alert_result' => $previous['last_alert_result'] ?? null,
            ];
            $previousAlert = (int)($previous['last_alert_unix'] ?? 0);
            $cooldown = max(60, (int)$this->config['alert_cooldown_seconds']);
            if (($previous['healthy'] ?? null) !== false || $previousAlert === 0 || $now - $previousAlert >= $cooldown) {
                $this->recordAlert(
                    $status,
                    'failed',
                    'The authenticated production PWA monitor failed with code ' . $status['last_error_code'] . '.',
                    $now);
            }
            $this->writeStatus($status);
            throw $failure;
        }
    }

    public function healthStatus(): array
    {
        $status = $this->readStatus();
        $configured = str_starts_with((string)$this->config['base_url'], 'https://')
            && trim((string)$this->config['character_name']) !== ''
            && (string)$this->config['password'] !== ''
            && (string)$this->config['status_path'] !== '';
        return [
            'configured' => $configured,
            'healthy' => is_bool($status['healthy'] ?? null) ? $status['healthy'] : null,
            'last_run_at' => $status['last_run_at'] ?? null,
            'last_success_at' => $status['last_success_at'] ?? null,
            'last_failure_at' => $status['last_failure_at'] ?? null,
            'last_error_code' => $status['last_error_code'] ?? null,
        ];
    }

    private function performChecks(int $now): void
    {
        $base = rtrim((string)$this->config['base_url'], '/');
        $origin = (string)parse_url($base, PHP_URL_SCHEME) . '://' . (string)parse_url($base, PHP_URL_HOST);
        $pwa = $this->request('GET', $base . '/pwa/index.html', [], null);
        $this->requireStatus($pwa, 200, 'public_pwa_unavailable', 'The public PWA is unavailable.');
        if (!$this->contentTypeIs($pwa, 'text/html') || !str_contains(strtolower($pwa['body']), '<!doctype html')) {
            throw new PwaMonitorFailure('public_pwa_invalid', 'The public PWA response is invalid.');
        }

        $api = $base . '/api/v1';
        $health = $this->requestJson('GET', $api . '/health');
        $this->requireNoStore($health, 'health_cacheable');
        if (($health['json']['service'] ?? null) !== 'player-assistant-broker'
            || ($health['json']['schema_version'] ?? null) !== 7
            || ($health['json']['status'] ?? null) !== 'ok') {
            throw new PwaMonitorFailure('health_invalid', 'The broker health response is invalid.');
        }

        $session = $this->requestJson('GET', $api . '/session');
        $this->requireNoStore($session, 'session_cacheable');
        if (($session['json']['authenticated'] ?? null) !== false) {
            throw new PwaMonitorFailure('anonymous_session_invalid', 'The initial session is not anonymous.');
        }

        $authenticated = false;
        $csrf = '';
        try {
            $login = $this->requestJson('POST', $api . '/login', ['Origin' => $origin], [
                'character_name' => (string)$this->config['character_name'],
                'password' => (string)$this->config['password'],
            ]);
            $this->requireNoStore($login, 'login_cacheable');
            if (($login['json']['authenticated'] ?? null) !== true
                || !is_array($login['json']['account'] ?? null)
                || !is_string($login['json']['account']['id'] ?? null)
                || preg_match('/^[a-f0-9]{32}$/', $login['json']['account']['id']) !== 1
                || !is_string($login['json']['csrf_token'] ?? null)
                || strlen($login['json']['csrf_token']) < 32) {
                throw new PwaMonitorFailure('login_invalid', 'The monitor account could not authenticate.');
            }
            $authenticated = true;
            $accountId = $login['json']['account']['id'];
            $csrf = $login['json']['csrf_token'];

            $identity = $this->requestJson('GET', $api . '/me');
            $this->requireNoStore($identity, 'identity_cacheable');
            if (($identity['json']['authenticated'] ?? null) !== true
                || ($identity['json']['account']['id'] ?? null) !== $accountId) {
                throw new PwaMonitorFailure('identity_invalid', 'The monitor identity response is invalid.');
            }

            $xp = $this->requestJson('GET', $api . '/xp');
            $this->requireNoStore($xp, 'xp_cacheable');
            if (($xp['json']['schema_version'] ?? null) !== 1
                || ($xp['json']['stale'] ?? null) !== false
                || !in_array($xp['json']['scope'] ?? null, ['character', 'party'], true)
                || !$this->freshTimestamp($xp['json']['fetched_at'] ?? null, (int)$this->config['maximum_xp_age_seconds'], $now)) {
                throw new PwaMonitorFailure('xp_invalid_or_stale', 'The protected XP response is invalid or stale.');
            }

            $wordCounts = $this->requestJson('GET', $api . '/word-counts');
            $this->requireNoStore($wordCounts, 'word_counts_cacheable');
            if (($wordCounts['json']['schema_version'] ?? null) !== 1
                || ($wordCounts['json']['counting_rule_version'] ?? null) !== 'obsidian-publish-word-count-v1'
                || !$this->freshTimestamp($wordCounts['json']['observed_at'] ?? null, (int)$this->config['maximum_word_count_age_seconds'], $now)
                || !$this->freshTimestamp($wordCounts['json']['uploaded_at'] ?? null, (int)$this->config['maximum_word_count_age_seconds'], $now)) {
                throw new PwaMonitorFailure('word_counts_invalid_or_stale', 'The protected word-count response is invalid or stale.');
            }
        } finally {
            if ($authenticated) {
                $logout = $this->requestJson('POST', $api . '/logout', ['Origin' => $origin, 'X-CSRF-Token' => $csrf], []);
                $this->requireNoStore($logout, 'logout_cacheable');
                if (($logout['json']['authenticated'] ?? null) !== false) {
                    throw new PwaMonitorFailure('logout_invalid', 'The monitor session was not closed.');
                }
                $postLogout = $this->requestJson('GET', $api . '/session');
                $this->requireNoStore($postLogout, 'post_logout_session_cacheable');
                if (($postLogout['json']['authenticated'] ?? null) !== false) {
                    throw new PwaMonitorFailure('logout_session_active', 'The monitor session remained authenticated after logout.');
                }
            }
        }
    }

    private function validateConfiguration(): void
    {
        if (!extension_loaded('curl') && $this->requester === null) {
            throw new PwaMonitorFailure('curl_unavailable', 'The PHP cURL extension is required.');
        }
        $base = (string)$this->config['base_url'];
        if (!str_starts_with($base, 'https://')
            || trim((string)$this->config['character_name']) === ''
            || (string)$this->config['password'] === ''
            || (string)$this->config['status_path'] === '') {
            throw new PwaMonitorFailure('configuration_invalid', 'The private monitor configuration is incomplete.');
        }
    }

    private function requestJson(string $method, string $url, array $headers = [], ?array $body = null): array
    {
        $response = $this->request($method, $url, $headers, $body);
        $this->requireStatus($response, 200, 'http_failure', 'A monitor endpoint returned an error.');
        if (!$this->contentTypeIs($response, 'application/json')) {
            throw new PwaMonitorFailure('json_mime_invalid', 'A monitor endpoint returned the wrong MIME type.');
        }
        try {
            $response['json'] = json_decode($response['body'], true, 32, JSON_THROW_ON_ERROR);
        } catch (Throwable) {
            throw new PwaMonitorFailure('json_invalid', 'A monitor endpoint returned invalid JSON.');
        }
        if (!is_array($response['json'])) {
            throw new PwaMonitorFailure('json_shape_invalid', 'A monitor endpoint returned an invalid JSON shape.');
        }
        return $response;
    }

    private function request(string $method, string $url, array $headers, ?array $body): array
    {
        if ($this->cookie !== '') {
            $headers['Cookie'] = $this->cookie;
        }
        $headers['X-Correlation-ID'] = $this->correlationId;
        $response = ($this->requester)($method, $url, $headers, $body);
        if (!is_array($response) || !isset($response['status'], $response['headers'], $response['body'])) {
            throw new PwaMonitorFailure('transport_invalid', 'The monitor transport returned an invalid response.');
        }
        $response['headers'] = array_change_key_case($response['headers'], CASE_LOWER);
        if (isset($response['headers']['set-cookie']) && preg_match('/^([^;]+)/', (string)$response['headers']['set-cookie'], $match) === 1) {
            $this->cookie = $match[1];
        }
        return $response;
    }

    private function curlRequest(string $method, string $url, array $headers, ?array $body): array
    {
        if (!extension_loaded('curl')) {
            throw new PwaMonitorFailure('curl_unavailable', 'The PHP cURL extension is required.');
        }
        $responseHeaders = [];
        $curl = curl_init($url);
        $headerLines = ['Accept: application/json, text/html;q=0.9'];
        foreach ($headers as $name => $value) {
            $headerLines[] = $name . ': ' . $value;
        }
        $options = [
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_FOLLOWLOCATION => false,
            CURLOPT_CONNECTTIMEOUT => 10,
            CURLOPT_TIMEOUT => 30,
            CURLOPT_CUSTOMREQUEST => $method,
            CURLOPT_HTTPHEADER => $headerLines,
            CURLOPT_USERAGENT => 'PlayerAssistant-DreamHost-Monitor/1.0',
            CURLOPT_HEADERFUNCTION => static function ($handle, string $line) use (&$responseHeaders): int {
                $length = strlen($line);
                $parts = explode(':', $line, 2);
                if (count($parts) === 2) {
                    $name = strtolower(trim($parts[0]));
                    $value = trim($parts[1]);
                    $responseHeaders[$name] = isset($responseHeaders[$name])
                        ? $responseHeaders[$name] . ', ' . $value
                        : $value;
                }
                return $length;
            },
        ];
        if ($body !== null) {
            $json = json_encode($body, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
            $options[CURLOPT_POSTFIELDS] = $json;
            $options[CURLOPT_HTTPHEADER][] = 'Content-Type: application/json';
        }
        curl_setopt_array($curl, $options);
        $content = curl_exec($curl);
        if (!is_string($content)) {
            $error = curl_error($curl);
            curl_close($curl);
            throw new PwaMonitorFailure('transport_failure', 'The monitor request failed: ' . substr($error, 0, 120));
        }
        $status = (int)curl_getinfo($curl, CURLINFO_RESPONSE_CODE);
        curl_close($curl);
        return ['status' => $status, 'headers' => $responseHeaders, 'body' => $content];
    }

    private function requireStatus(array $response, int $status, string $code, string $message): void
    {
        if ((int)$response['status'] !== $status) {
            throw new PwaMonitorFailure($code, $message);
        }
    }

    private function requireNoStore(array $response, string $code): void
    {
        if (!str_contains(strtolower((string)($response['headers']['cache-control'] ?? '')), 'no-store')) {
            throw new PwaMonitorFailure($code, 'A protected response is cacheable.');
        }
    }

    private function contentTypeIs(array $response, string $expected): bool
    {
        return strtolower(trim(explode(';', (string)($response['headers']['content-type'] ?? ''))[0])) === $expected;
    }

    private function freshTimestamp(mixed $value, int $maximumAge, int $now): bool
    {
        if (!is_string($value) || preg_match('/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/', $value) !== 1) {
            return false;
        }
        $timestamp = strtotime($value);
        return $timestamp !== false && $timestamp <= $now + 300 && $now - $timestamp <= max(1, $maximumAge);
    }

    private function recordAlert(array &$status, string $event, string $message, int $now): void
    {
        $sent = ($this->mailer)('[Player Assistant PWA monitor] ' . $event, $message);
        $status['last_alert_at'] = gmdate(DATE_ATOM, $now);
        $status['last_alert_unix'] = $now;
        $status['last_alert_result'] = $sent ? 'sent' : 'not_sent';
    }

    private function sendMail(string $subject, string $body): bool
    {
        $email = trim((string)$this->config['alert_email']);
        $from = trim((string)$this->config['alert_from']);
        if ($email === '' || $from === '' || !function_exists('mail')) {
            return false;
        }
        $headers = "From: {$from}\r\nReply-To: {$email}\r\nContent-Type: text/plain; charset=UTF-8\r\n";
        return @mail($email, $subject, $body, $headers, '-f' . $from);
    }

    private function readStatus(): array
    {
        $path = (string)$this->config['status_path'];
        if (!is_file($path) || filesize($path) > 16384) {
            return [];
        }
        try {
            $status = json_decode((string)file_get_contents($path), true, 16, JSON_THROW_ON_ERROR);
            return is_array($status) ? $status : [];
        } catch (Throwable) {
            return [];
        }
    }

    private function writeStatus(array $status): void
    {
        $path = (string)$this->config['status_path'];
        $directory = dirname($path);
        if ((!is_dir($directory) && !mkdir($directory, 0700, true)) || !is_writable($directory)) {
            throw new PwaMonitorFailure('status_write_failed', 'Unable to prepare monitor status directory.');
        }
        chmod($directory, 0700);
        $temporary = $path . '.tmp-' . bin2hex(random_bytes(4));
        if (file_put_contents($temporary, json_encode($status, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR), LOCK_EX) === false) {
            throw new PwaMonitorFailure('status_write_failed', 'Unable to write monitor status.');
        }
        chmod($temporary, 0600);
        if (!rename($temporary, $path)) {
            @unlink($temporary);
            throw new PwaMonitorFailure('status_write_failed', 'Unable to promote monitor status.');
        }
        chmod($path, 0600);
    }

    private function safeCode(string $value): string
    {
        $value = strtolower((string)preg_replace('/[^a-z0-9_]+/i', '_', $value));
        return substr(trim($value, '_') ?: 'monitor_failure', 0, 80);
    }
}
