<?php

declare(strict_types=1);

require_once __DIR__ . '/OrcishTranslator.php';

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');
header('X-Content-Type-Options: nosniff');

function respond(array $payload, int $status): void
{
    http_response_code($status);
    echo json_encode($payload, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
    exit;
}

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    header('Allow: POST');
    respond(['error' => 'Use POST with an English text value.'], 405);
}

$contentType = isset($_SERVER['CONTENT_TYPE']) ? strtolower((string)$_SERVER['CONTENT_TYPE']) : '';
$input = '';

if (strpos($contentType, 'application/json') === 0) {
    try {
        $body = json_decode((string)file_get_contents('php://input'), true, 32, JSON_THROW_ON_ERROR);
        $input = is_array($body) && isset($body['english']) ? trim((string)$body['english']) : '';
    } catch (JsonException $exception) {
        respond(['error' => 'The request body is not valid JSON.'], 400);
    }
} else {
    $input = isset($_POST['english']) ? trim((string)$_POST['english']) : '';
}

if ($input === '') {
    respond(['error' => 'The English text is required.'], 400);
}

if (OrcishTranslator::countWords($input) > OrcishTranslator::MAX_INPUT_WORDS) {
    respond(['error' => 'Please limit the English text to 5,000 words.'], 413);
}

try {
    $translator = new OrcishTranslator(__DIR__ . '/orcish-lexicon.json');
    $result = $translator->translateSentenceWithUnknownWords($input);
    respond([
        'english' => $input,
        'orcish' => $result['translation'],
        'untranslatedWords' => $result['untranslatedWords'],
        'knownEnglishTerms' => $translator->getEnglishTermCount(),
    ], 200);
} catch (Throwable $exception) {
    error_log($exception->getMessage());
    respond(['error' => 'The translator is temporarily unavailable.'], 500);
}
