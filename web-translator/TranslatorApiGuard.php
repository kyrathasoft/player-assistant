<?php

declare(strict_types=1);

final class TranslatorApiGuard
{
    public const MAX_BODY_BYTES = 65536;
    public const MAX_INPUT_BYTES = 32768;
    public const MAX_OUTPUT_BYTES = 131072;
    private const DEFAULT_RATE_LIMIT = 30;
    private const DEFAULT_RATE_WINDOW_SECONDS = 60;
    /** @var array<string,object> */
    private static $translators = [];

    public static function translator(string $language, string $path): object
    {
        if (!isset(self::$translators[$language])) {
            self::$translators[$language] = $language === 'orcish'
                ? new OrcishTranslator($path)
                : new ElvenTranslator($path);
        }
        return self::$translators[$language];
    }

    public static function enforceRequestBodyLimit(): string
    {
        $contentLength = isset($_SERVER['CONTENT_LENGTH']) ? (int)$_SERVER['CONTENT_LENGTH'] : null;
        if ($contentLength !== null && $contentLength > self::MAX_BODY_BYTES) {
            self::reject('The request body is too large.', 413);
        }

        $body = file_get_contents('php://input', false, null, 0, self::MAX_BODY_BYTES + 1);
        if ($body === false || strlen($body) > self::MAX_BODY_BYTES) {
            self::reject('The request body is too large.', 413);
        }
        return $body;
    }

    public static function englishFromBody(string $body, string $contentType): string
    {
        if (strpos($contentType, 'application/json') === 0) {
            try {
                $document = json_decode($body, true, 32, JSON_THROW_ON_ERROR);
            } catch (JsonException $exception) {
                self::reject('The request body is not valid JSON.', 400);
            }
            $input = is_array($document) && isset($document['english']) ? trim((string)$document['english']) : '';
        } else {
            parse_str($body, $form);
            $input = isset($form['english']) ? trim((string)$form['english']) : '';
        }
        if ($input === '') {
            self::reject('The English text is required.', 400);
        }
        if (strlen($input) > self::MAX_INPUT_BYTES) {
            self::reject('The English text is too large.', 413);
        }
        return $input;
    }

    public static function enforceRateLimit(string $source): void
    {
        $limit = max(1, (int)(getenv('TRANSLATOR_RATE_LIMIT') ?: self::DEFAULT_RATE_LIMIT));
        $window = max(1, (int)(getenv('TRANSLATOR_RATE_WINDOW_SECONDS') ?: self::DEFAULT_RATE_WINDOW_SECONDS));
        $path = getenv('TRANSLATOR_RATE_FILE') ?: sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'player-assistant-translator-rate.json';
        $handle = @fopen($path, 'c+');
        if ($handle === false || !flock($handle, LOCK_EX)) {
            if (is_resource($handle)) fclose($handle);
            self::reject('The translator is temporarily unavailable.', 503);
        }
        $now = time();
        $state = json_decode(stream_get_contents($handle) ?: '{}', true);
        $state = is_array($state) ? $state : [];
        foreach ($state as $key => $oldEntry) {
            if (!is_array($oldEntry) || $now - (int)($oldEntry['started'] ?? 0) >= $window) {
                unset($state[$key]);
            }
        }
        $entry = isset($state[$source]) && is_array($state[$source]) ? $state[$source] : ['started' => $now, 'count' => 0];
        if ($now - (int)$entry['started'] >= $window) {
            $entry = ['started' => $now, 'count' => 0];
        }
        $entry['count']++;
        $state[$source] = $entry;
        ftruncate($handle, 0);
        rewind($handle);
        fwrite($handle, json_encode($state, JSON_THROW_ON_ERROR));
        fflush($handle);
        flock($handle, LOCK_UN);
        fclose($handle);
        if ($entry['count'] > $limit) {
            header('Retry-After: ' . max(1, $window - ($now - (int)$entry['started'])));
            self::reject('Too many translation requests. Please try again later.', 429);
        }
    }

    public static function reject(string $message, int $status): void
    {
        http_response_code($status);
        echo json_encode(['error' => $message], JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
        exit;
    }
}
