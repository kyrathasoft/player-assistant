<?php

declare(strict_types=1);

require_once __DIR__ . '/OrcishTranslator.php';
require_once __DIR__ . '/TranslatorApiGuard.php';

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
TranslatorApiGuard::enforceRateLimit((string)($_SERVER['REMOTE_ADDR'] ?? 'unknown'));
$input = TranslatorApiGuard::englishFromBody(
    TranslatorApiGuard::enforceRequestBodyLimit(),
    $contentType);

if (OrcishTranslator::countWords($input) > OrcishTranslator::MAX_INPUT_WORDS) {
    respond(['error' => 'Please limit the English text to 5,000 words.'], 413);
}

try {
    /** @var OrcishTranslator $translator */
    $translator = TranslatorApiGuard::translator('orcish', __DIR__ . '/orcish-lexicon.json');
    $result = $translator->translateSentenceWithUnknownWords($input);
    if (strlen($result['translation']) > TranslatorApiGuard::MAX_OUTPUT_BYTES) {
        respond(['error' => 'The translation result is too large.'], 413);
    }
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
