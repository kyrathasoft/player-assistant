<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/PwaSyntheticMonitor.php';
require_once __DIR__ . '/../player-assistant-broker/CorrelationContext.php';

function monitorAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$root = sys_get_temp_dir() . '/pa-pwa-monitor-' . bin2hex(random_bytes(6));
if (!mkdir($root, 0700, true)) {
    throw new RuntimeException('Unable to create monitor fixture.');
}

try {
    $now = strtotime('2026-08-12T03:00:00Z');
    $statusPath = $root . '/status.json';
    $requests = [];
    $alerts = [];
    $cookie = 'pa_character_session=fixture';
    $csrf = 'abcdefghijklmnopqrstuvwxyzABCDEFGH123456789_-';
    $accountId = '0123456789abcdef0123456789abcdef';
    $responder = static function (string $method, string $url, array $headers, ?array $body) use (&$requests, $cookie, $csrf, $accountId, $now): array {
        $requests[] = compact('method', 'url', 'headers', 'body');
        $path = parse_url($url, PHP_URL_PATH);
        return match ([$method, $path]) {
            ['GET', '/scarlethorizons/pwa/index.html'] => [
                'status' => 200,
                'headers' => ['content-type' => 'text/html', 'x-content-type-options' => 'nosniff'],
                'body' => '<!doctype html><title>Scarlet Horizons</title>',
            ],
            ['GET', '/scarlethorizons/api/v1/health'] => [
                'status' => 200,
                'headers' => ['content-type' => 'application/json', 'cache-control' => 'no-store'],
                'body' => json_encode(['service' => 'player-assistant-broker', 'schema_version' => 7, 'status' => 'ok']),
            ],
            ['GET', '/scarlethorizons/api/v1/session'] => [
                'status' => 200,
                'headers' => ['content-type' => 'application/json', 'cache-control' => 'no-store'],
                'body' => json_encode(['authenticated' => false]),
            ],
            ['POST', '/scarlethorizons/api/v1/login'] => [
                'status' => 200,
                'headers' => ['content-type' => 'application/json', 'cache-control' => 'no-store', 'set-cookie' => $cookie],
                'body' => json_encode([
                    'authenticated' => true,
                    'account' => ['id' => $accountId, 'character_name' => 'Monitor Hero', 'character_key' => 'monitor-hero', 'role' => 'player', 'enabled' => true],
                    'csrf_token' => $csrf,
                    'idle_expires_at' => gmdate(DATE_ATOM, $now + 600),
                    'absolute_expires_at' => gmdate(DATE_ATOM, $now + 3600),
                ]),
            ],
            ['GET', '/scarlethorizons/api/v1/me'] => [
                'status' => 200,
                'headers' => ['content-type' => 'application/json', 'cache-control' => 'no-store'],
                'body' => json_encode(['authenticated' => true, 'account' => ['id' => $accountId, 'character_key' => 'monitor-hero']]),
            ],
            ['GET', '/scarlethorizons/api/v1/xp'] => [
                'status' => 200,
                'headers' => ['content-type' => 'application/json', 'cache-control' => 'no-store'],
                'body' => json_encode(['schema_version' => 1, 'fetched_at' => gmdate(DATE_ATOM, $now - 60), 'stale' => false, 'scope' => 'character', 'character' => ['character_name' => 'Monitor Hero', 'xp_total' => 100]]),
            ],
            ['GET', '/scarlethorizons/api/v1/word-counts'] => [
                'status' => 200,
                'headers' => ['content-type' => 'application/json', 'cache-control' => 'no-store'],
                'body' => json_encode(['schema_version' => 1, 'observed_at' => gmdate(DATE_ATOM, $now - 120), 'uploaded_at' => gmdate(DATE_ATOM, $now - 60), 'counting_rule_version' => 'obsidian-publish-word-count-v1', 'wiki' => ['pages' => 1, 'words' => 2], 'ic' => ['files' => 1, 'words' => 2], 'ooc' => ['files' => 1, 'words' => 2]]),
            ],
            ['POST', '/scarlethorizons/api/v1/logout'] => [
                'status' => 200,
                'headers' => ['content-type' => 'application/json', 'cache-control' => 'no-store'],
                'body' => json_encode(['authenticated' => false]),
            ],
            default => throw new RuntimeException("Unexpected fixture request: {$method} {$path}"),
        };
    };
    $mailer = static function (string $subject, string $body) use (&$alerts): bool {
        $alerts[] = compact('subject', 'body');
        return true;
    };
    $config = [
        'base_url' => 'https://example.test/scarlethorizons',
        'character_name' => 'Monitor Hero',
        'password' => 'fixture-password',
        'status_path' => $statusPath,
        'maximum_xp_age_seconds' => 86400,
        'maximum_word_count_age_seconds' => 604800,
        'alert_cooldown_seconds' => 3600,
    ];

    $monitor = new PwaSyntheticMonitor($config, $responder, $mailer, static fn(): int => $now);
    $result = $monitor->run();
    monitorAssert($result['status'] === 'ok', 'The healthy monitor did not pass.');
    monitorAssert(CorrelationContext::create($result['correlation_id'] ?? null) === $result['correlation_id'], 'The monitor did not return a sanitized correlation ID.');
    foreach ($requests as $request) {
        monitorAssert(($request['headers']['X-Correlation-ID'] ?? null) === $result['correlation_id'], 'The monitor did not propagate one correlation ID to every request.');
        monitorAssert(($request['headers']['X-Request-ID'] ?? null) === $result['correlation_id'], 'The monitor did not align request and correlation IDs.');
    }
    monitorAssert(count($alerts) === 0, 'The healthy initial run sent an alert.');
    monitorAssert(is_file($statusPath), 'The monitor did not persist status.');
    if (DIRECTORY_SEPARATOR === '/') {
        monitorAssert((fileperms($statusPath) & 0777) === 0600, 'The monitor status is not mode 600.');
    }
    $status = json_decode((string)file_get_contents($statusPath), true, 16, JSON_THROW_ON_ERROR);
    monitorAssert($status['healthy'] === true && $status['last_success_at'] !== null, 'Healthy status was not persisted.');
    $health = $monitor->healthStatus();
    monitorAssert($health['configured'] === true && $health['healthy'] === true && $health['last_success_at'] === $status['last_success_at'], 'The sanitized health summary is incorrect.');
    monitorAssert(!array_key_exists('last_alert_unix', $health) && !array_key_exists('password', $health), 'The health summary exposed private monitor state.');
    monitorAssert(!str_contains((string)file_get_contents($statusPath), 'fixture-password'), 'The monitor status leaked credentials.');
    $lastRequest = end($requests);
    monitorAssert($lastRequest['method'] === 'GET' && str_ends_with($lastRequest['url'], '/api/v1/session'), 'The monitor did not verify the post-logout session.');
    $logout = $requests[count($requests) - 2];
    monitorAssert($logout['method'] === 'POST' && str_ends_with($logout['url'], '/api/v1/logout'), 'The monitor did not log out.');
    monitorAssert(($logout['headers']['X-CSRF-Token'] ?? null) === $csrf, 'Logout did not use the exact CSRF header.');

    $failingResponder = static function (): array {
        return ['status' => 503, 'headers' => ['content-type' => 'text/html'], 'body' => 'unavailable'];
    };
    $failed = new PwaSyntheticMonitor($config, $failingResponder, $mailer, static fn(): int => $now + 60);
    try {
        $failed->run();
        throw new RuntimeException('The failed monitor unexpectedly passed.');
    } catch (PwaMonitorFailure $error) {
        monitorAssert($error->errorCode === 'public_pwa_unavailable', 'The failed monitor used the wrong safe error code.');
    }
    monitorAssert(count($alerts) === 1, 'The healthy-to-failed transition did not alert exactly once.');

    $failedAgain = new PwaSyntheticMonitor($config, $failingResponder, $mailer, static fn(): int => $now + 120);
    try {
        $failedAgain->run();
    } catch (PwaMonitorFailure) {
    }
    monitorAssert(count($alerts) === 1, 'A repeated failure inside cooldown sent another alert.');

    $recovered = new PwaSyntheticMonitor($config, $responder, $mailer, static fn(): int => $now + 180);
    monitorAssert($recovered->run()['status'] === 'ok', 'The recovery run did not pass.');
    monitorAssert(count($alerts) === 2 && str_contains($alerts[1]['subject'], 'recovered'), 'Recovery did not send one transition alert.');

    echo "DreamHost PWA monitor tests passed.\n";
} finally {
    foreach (glob($root . '/*') ?: [] as $path) {
        @unlink($path);
    }
    @rmdir($root);
}
