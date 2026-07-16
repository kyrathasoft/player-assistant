<?php

declare(strict_types=1);

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');
header('X-Content-Type-Options: nosniff');
header('Referrer-Policy: no-referrer');
header("Content-Security-Policy: default-src 'none'; frame-ancestors 'none'");

$requestId = bin2hex(random_bytes(8));
header('X-Request-Id: ' . $requestId);

try {
    requireHttps();

    $privateDirectory = dirname(__DIR__, 3) . '/player-assistant-broker';
    require_once $privateDirectory . '/RpolClient.php';
    require_once $privateDirectory . '/BrokerService.php';
    $config = require $privateDirectory . '/config.php';

    $requestBody = readJsonRequestBody(8 * 1024 * 1024);
    $service = new BrokerService($config, new RpolClient($config['rpol']));
    $response = $service->dispatch(
        strtoupper($_SERVER['REQUEST_METHOD'] ?? 'GET'),
        getRoutePath((string)($config['api']['base_path'] ?? '')),
        $_GET,
        $requestBody,
        getRequestHeadersForBroker(),
        (string)($_SERVER['REMOTE_ADDR'] ?? 'unknown'));

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
    error_log(sprintf(
        '[player-assistant-broker:%s] %s in %s:%d',
        $requestId,
        $exception->getMessage(),
        $exception->getFile(),
        $exception->getLine()));
    sendJson(500, [
        'error' => 'internal_error',
        'message' => 'The broker could not complete the request.',
        'request_id' => $requestId,
    ]);
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

function getRequestHeadersForBroker(): array
{
    return [
        'authorization' => (string)(
            $_SERVER['HTTP_AUTHORIZATION']
            ?? $_SERVER['REDIRECT_HTTP_AUTHORIZATION']
            ?? ''),
        'admin-key' => (string)($_SERVER['HTTP_X_BROKER_ADMIN_KEY'] ?? ''),
    ];
}

function sendJson(int $status, array $body): never
{
    http_response_code($status);
    echo json_encode($body, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
    exit;
}
