<?php

declare(strict_types=1);

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');
header('X-Content-Type-Options: nosniff');
header('Referrer-Policy: no-referrer');
header("Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; frame-src 'none'; object-src 'none'; upgrade-insecure-requests");
header('Permissions-Policy: camera=(), microphone=(), geolocation=()');
header('Strict-Transport-Security: max-age=31536000');

$requestId = bin2hex(random_bytes(8));
header('X-Request-Id: ' . $requestId);
$config = null;

try {
    requireHttps();

    $privateDirectory = dirname(__DIR__, 3) . '/player-assistant-broker';
    require_once $privateDirectory . '/BrokerHttpException.php';
    require_once $privateDirectory . '/RpolClient.php';
    require_once $privateDirectory . '/CharacterAuthService.php';
    require_once $privateDirectory . '/XpTrackingService.php';
    require_once $privateDirectory . '/WordCountService.php';
    require_once $privateDirectory . '/BrokerOperations.php';
    require_once $privateDirectory . '/QuestService.php';
    require_once $privateDirectory . '/MessageService.php';
    require_once $privateDirectory . '/MagicItemService.php';
    require_once $privateDirectory . '/BrokerService.php';
    require_once $privateDirectory . '/BrokerAlertService.php';
    $configPathOverride = getenv('PLAYER_ASSISTANT_BROKER_CONFIG');
    $configPath = is_string($configPathOverride) && $configPathOverride !== ''
        ? $configPathOverride
        : $privateDirectory . '/config.php';
    if (!is_file($configPath)) {
        throw new RuntimeException('The private broker configuration is unavailable.');
    }
    $config = require $configPath;
    $config['xp'] = is_array($config['xp'] ?? null) ? $config['xp'] : [];
    $config['xp']['awards_root'] = $privateDirectory;
    $questDataPathOverride = getenv('PLAYER_ASSISTANT_QUESTS_PATH');
    $questDataPath = is_string($questDataPathOverride) && $questDataPathOverride !== ''
        ? $questDataPathOverride
        : dirname(__DIR__) . '/pwa/quests.json';

    $method = strtoupper($_SERVER['REQUEST_METHOD'] ?? 'GET');
    $route = getRoutePath((string)($config['api']['base_path'] ?? ''));
    if ($method === 'GET' && $route === '/v1/health') {
        sendJson(200, [
            'service' => 'player-assistant-broker',
            'schema_version' => 7,
            'status' => 'ok',
        ]);
        return;
    }
    requireJsonContentType($method);
    $requestBody = readJsonRequestBody(8 * 1024 * 1024);
    $sessionState = [];
    $regenerateSession = null;
    $destroySession = null;
    if (isCharacterSessionRoute($route)) {
        startCharacterSession(is_array($config['auth'] ?? null) ? $config['auth'] : []);
        $sessionState =& $_SESSION;
        $regenerateSession = static function (): void {
            if (!session_regenerate_id(true)) {
                throw new RuntimeException('Unable to regenerate the character session.');
            }
        };
        $destroySession = static function (): void {
            destroyCharacterSession();
        };
    }

    $service = new BrokerService(
        $config,
        new RpolClient($config['rpol']),
        null,
        null,
        $questDataPath);
    $response = $service->dispatch(
        $method,
        $route,
        $_GET,
        $requestBody,
        getRequestHeadersForBroker(),
        (string)($_SERVER['REMOTE_ADDR'] ?? 'unknown'),
        $sessionState,
        $regenerateSession,
        $destroySession);

    sendJson($response['status'], $response['body']);
} catch (BrokerHttpException $exception) {
    sendJson($exception->status, [
        'error' => $exception->errorName,
        'message' => $exception->getMessage(),
        'request_id' => $requestId,
    ]);
} catch (JsonException) {
    sendJson(400, [
        'error' => 'invalid_json',
        'message' => 'The request body must be valid JSON.',
        'request_id' => $requestId,
    ]);
} catch (Throwable $exception) {
    if (isset($operations) && $operations instanceof BrokerOperations) {
        $operations->recordServerError($requestId, 'internal_error');
    }
    error_log(sprintf(
        '[player-assistant-broker:%s] %s in %s:%d',
        $requestId,
        $exception->getMessage(),
        $exception->getFile(),
        $exception->getLine()));
    if (is_array($config)) {
        recordBrokerServerError($config, $exception);
    }
    sendJson(500, [
        'error' => 'internal_error',
        'message' => 'The broker could not complete the request.',
        'request_id' => $requestId,
    ]);
}

function recordBrokerServerError(array $config, Throwable $exception): void
{
    try {
        $databasePath = (string)($config['api']['database_path'] ?? '');
        if ($databasePath === '') {
            return;
        }
        $database = new PDO('sqlite:' . $databasePath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
        $database->exec('PRAGMA busy_timeout = 5000');
        (new BrokerAlertService(
            $database,
            is_array($config['observability'] ?? null) ? $config['observability'] : []))
            ->recordServerError('internal_error', $exception->getMessage());
    } catch (Throwable $alertError) {
        error_log('[player-assistant-broker-alert] ' . $alertError->getMessage());
    }
}

function requireHttps(): void
{
    $https = strtolower((string)($_SERVER['HTTPS'] ?? ''));
    if ($https !== 'on' && $https !== '1') {
        throw new RuntimeException('The broker requires HTTPS.');
    }
}

function getRoutePath(string $basePath): string
{
    $requestPath = parse_url((string)($_SERVER['REQUEST_URI'] ?? '/'), PHP_URL_PATH);
    if (!is_string($requestPath)) {
        throw new BrokerHttpException(400, 'invalid_path', 'The request path is invalid.');
    }

    $normalizedBasePath = '/' . trim($basePath, '/');
    if ($normalizedBasePath !== '/' && !str_starts_with($requestPath, $normalizedBasePath)) {
        throw new BrokerHttpException(404, 'not_found', 'The requested broker endpoint was not found.');
    }

    $route = $normalizedBasePath === '/'
        ? $requestPath
        : substr($requestPath, strlen($normalizedBasePath));
    return '/' . ltrim($route === '' ? '/' : $route, '/');
}

function readJsonRequestBody(int $maxBytes): array
{
    $contentLength = (int)($_SERVER['CONTENT_LENGTH'] ?? 0);
    if ($contentLength > $maxBytes) {
        throw new BrokerHttpException(413, 'request_too_large', 'The request body is too large.');
    }

    $raw = file_get_contents('php://input', false, null, 0, $maxBytes + 1);
    if ($raw === false || strlen($raw) > $maxBytes) {
        throw new BrokerHttpException(413, 'request_too_large', 'The request body is too large.');
    }

    if ($raw === '') {
        return [];
    }

    $decoded = json_decode($raw, true, 32, JSON_THROW_ON_ERROR);
    if (!is_array($decoded)) {
        throw new BrokerHttpException(400, 'invalid_json', 'The request body must be a JSON object.');
    }

    return $decoded;
}

function requireJsonContentType(string $method): void
{
    if (!in_array($method, ['POST', 'PUT', 'PATCH'], true)
        || (int)($_SERVER['CONTENT_LENGTH'] ?? 0) === 0) {
        return;
    }

    $contentType = strtolower(trim(explode(';', (string)($_SERVER['CONTENT_TYPE'] ?? ''), 2)[0]));
    if ($contentType !== 'application/json') {
        throw new BrokerHttpException(
            415,
            'unsupported_media_type',
            'The request body must use application/json.');
    }
}

function getRequestHeadersForBroker(): array
{
    return [
        'authorization' => (string)(
            $_SERVER['HTTP_AUTHORIZATION']
            ?? $_SERVER['REDIRECT_HTTP_AUTHORIZATION']
            ?? ''),
        'admin-timestamp' => (string)($_SERVER['HTTP_X_BROKER_ADMIN_TIMESTAMP'] ?? ''),
        'admin-nonce' => (string)($_SERVER['HTTP_X_BROKER_ADMIN_NONCE'] ?? ''),
        'admin-signature' => (string)($_SERVER['HTTP_X_BROKER_ADMIN_SIGNATURE'] ?? ''),
        'csrf-token' => (string)($_SERVER['HTTP_X_CSRF_TOKEN'] ?? ''),
        'origin' => (string)($_SERVER['HTTP_ORIGIN'] ?? ''),
    ];
}

function isCharacterSessionRoute(string $route): bool
{
    return in_array(
        $route,
        [
            '/v1/login',
            '/v1/session',
            '/v1/me',
            '/v1/xp',
            '/v1/xp-awards',
            '/v1/word-counts',
            '/v1/presence',
            '/v1/quests',
            '/v1/magic-items',
            '/v1/quest-requests',
            '/v1/messages',
            '/v1/logout',
        ],
        true)
        || preg_match(
            '#^/v1/quest-requests/[a-f0-9]{32}/(?:decision|acknowledge)$#',
            $route) === 1
        || preg_match(
            '#^/v1/messages/[a-f0-9]{32}/read$#',
            $route) === 1;
}

function startCharacterSession(array $authConfig): void
{
    if (session_status() === PHP_SESSION_ACTIVE) {
        return;
    }

    $cookiePath = (string)($authConfig['cookie_path'] ?? '/scarlethorizons/api/');
    if (!str_starts_with($cookiePath, '/') || str_contains($cookiePath, "\r") || str_contains($cookiePath, "\n")) {
        throw new RuntimeException('The character session cookie path is invalid.');
    }

    ini_set('session.use_strict_mode', '1');
    ini_set('session.use_only_cookies', '1');
    ini_set('session.use_trans_sid', '0');
    ini_set('session.cookie_secure', '1');
    ini_set('session.cookie_httponly', '1');
    ini_set('session.cookie_samesite', 'Strict');
    ini_set(
        'session.gc_maxlifetime',
        (string)max(28800, (int)($authConfig['absolute_timeout_seconds'] ?? 28800)));
    session_name('pa_character_session');
    session_cache_limiter('nocache');
    if (!session_start([
        'cookie_lifetime' => 0,
        'cookie_path' => $cookiePath,
        'cookie_secure' => true,
        'cookie_httponly' => true,
        'cookie_samesite' => 'Strict',
    ])) {
        throw new RuntimeException('Unable to start the character session.');
    }
}

function destroyCharacterSession(): void
{
    if (session_status() !== PHP_SESSION_ACTIVE) {
        return;
    }

    $cookie = session_get_cookie_params();
    $_SESSION = [];
    setcookie(session_name(), '', [
        'expires' => time() - 42000,
        'path' => (string)$cookie['path'],
        'domain' => (string)$cookie['domain'],
        'secure' => true,
        'httponly' => true,
        'samesite' => 'Strict',
    ]);
    if (!session_destroy()) {
        throw new RuntimeException('Unable to destroy the character session.');
    }
}

function sendJson(int $status, array $body): never
{
    http_response_code($status);
    echo json_encode($body, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
    exit;
}
