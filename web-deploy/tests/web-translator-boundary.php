<?php

declare(strict_types=1);

function translatorBoundaryAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

function translatorBoundaryRequest(string $url, string $body, string $contentType): array
{
    $context = stream_context_create(['http' => [
        'method' => 'POST',
        'header' => "Content-Type: $contentType\r\nContent-Length: " . strlen($body) . "\r\n",
        'content' => $body,
        'ignore_errors' => true,
        'timeout' => 10,
    ]]);
    global $http_response_header;
    $response = file_get_contents($url, false, $context);
    $status = 0;
    foreach ($http_response_header ?? [] as $header) {
        if (preg_match('/^HTTP\/\S+ (\d+)/', $header, $match)) {
            $status = (int)$match[1];
        }
    }
    return [$status, json_decode((string)$response, true)];
}

$base = rtrim((string)getenv('TRANSLATOR_BASE_URL'), '/') . '/';
translatorBoundaryAssert($base !== '/', 'TRANSLATOR_BASE_URL must point to a running translator HTTP fixture.');
$maximum = implode(' ', array_fill(0, 5000, 'hello'));
[$status, $payload] = translatorBoundaryRequest($base . 'api.php', str_repeat('!', 70000), 'application/json');
translatorBoundaryAssert($status === 413, 'Oversized non-word JSON body was not rejected at the HTTP boundary.');
[$status, $payload] = translatorBoundaryRequest($base . 'api.php', '{"english":', 'application/json');
translatorBoundaryAssert($status === 400, 'Malformed JSON did not return HTTP 400.');
[$status, $payload] = translatorBoundaryRequest($base . 'api.php', json_encode(['english' => $maximum], JSON_THROW_ON_ERROR), 'application/json');
translatorBoundaryAssert($status === 200 && strlen((string)($payload['orcish'] ?? '')) < 131072, 'Maximum valid input failed or exceeded the output bound.');
$rateFile = getenv('TRANSLATOR_RATE_FILE');
if ($rateFile !== false) {
    @unlink($rateFile);
}
$statuses = [];
for ($i = 0; $i < 4; $i++) {
    [$statuses[$i], $payload] = translatorBoundaryRequest($base . 'api.php', '{"english":"hello"}', 'application/json');
}
translatorBoundaryAssert(count(array_filter($statuses, static fn (int $value): bool => $value === 429)) === 1, 'The per-source rate limit did not reject the deterministic burst.');
sleep(3);
[$status, $payload] = translatorBoundaryRequest($base . 'api.php', '{"english":"hello"}', 'application/json');
translatorBoundaryAssert($status === 200, 'The rate limit did not recover after its window.');
if (function_exists('curl_multi_init')) {
    @unlink($rateFile);
    $multi = curl_multi_init();
    $handles = [];
    for ($i = 0; $i < 8; $i++) {
        $handle = curl_init($base . 'api.php');
        curl_setopt_array($handle, [
            CURLOPT_POST => true,
            CURLOPT_POSTFIELDS => '{"english":"hello"}',
            CURLOPT_HTTPHEADER => ['Content-Type: application/json'],
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_TIMEOUT => 10,
        ]);
        curl_multi_add_handle($multi, $handle);
        $handles[] = $handle;
    }
    do {
        $running = 0;
        curl_multi_exec($multi, $running);
        if ($running > 0) {
            curl_multi_select($multi, 1.0);
        }
    } while ($running > 0);
    $burstStatuses = array_map(static fn ($handle): int => (int)curl_getinfo($handle, CURLINFO_HTTP_CODE), $handles);
    foreach ($handles as $handle) {
        curl_multi_remove_handle($multi, $handle);
        curl_close($handle);
    }
    curl_multi_close($multi);
    translatorBoundaryAssert(count(array_filter($burstStatuses, static fn (int $value): bool => $value === 429)) >= 1, 'Concurrent burst bypassed the per-source rate limit.');
}
echo "PASS translator HTTP boundary limits, malformed input, maximum input, and rate recovery.\n";
