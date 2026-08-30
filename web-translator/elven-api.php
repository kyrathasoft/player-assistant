<?php

declare(strict_types=1);

require_once __DIR__ . '/ElvenTranslator.php';
require_once __DIR__ . '/TranslatorApiGuard.php';

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');
header('X-Content-Type-Options: nosniff');

function respondElven(array $payload, int $status): void
{
    http_response_code($status);
    echo json_encode($payload, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
    exit;
}

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    header('Allow: POST');
    respondElven(['error' => 'Use POST with an English text value.'], 405);
}

$contentType = isset($_SERVER['CONTENT_TYPE']) ? strtolower((string)$_SERVER['CONTENT_TYPE']) : '';
TranslatorApiGuard::enforceRateLimit((string)($_SERVER['REMOTE_ADDR'] ?? 'unknown'));
$input = TranslatorApiGuard::englishFromBody(
    TranslatorApiGuard::enforceRequestBodyLimit(),
    $contentType);

if (ElvenTranslator::countWords($input) > ElvenTranslator::MAX_INPUT_WORDS) {
    respondElven(['error' => 'Please limit the English text to 5,000 words.'], 413);
}

try {
    /** @var ElvenTranslator $translator */
    $translator = TranslatorApiGuard::translator('elven', __DIR__ . '/elvish-lexicon.json');
    $result = $translator->translateSentenceWithUnknownWords($input);
    if (strlen($result['translation']) > TranslatorApiGuard::MAX_OUTPUT_BYTES) {
        respondElven(['error' => 'The translation result is too large.'], 413);
    }
    respondElven([
        'english' => $input,
        'elvish' => $result['translation'],
        'untranslatedWords' => $result['untranslatedWords'],
        'knownEnglishTerms' => $translator->getEnglishTermCount(),
    ], 200);
} catch (Throwable $exception) {
    error_log($exception->getMessage());
    respondElven(['error' => 'The translator is temporarily unavailable.'], 500);
}
