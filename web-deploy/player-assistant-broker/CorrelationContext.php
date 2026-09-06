<?php
declare(strict_types=1);

final class CorrelationContext
{
    public static function create(?string $candidate = null): string
    {
        if ($candidate !== null && preg_match('/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i', $candidate) === 1) return strtolower($candidate);
        return strtolower(bin2hex(random_bytes(4)) . '-' . bin2hex(random_bytes(2)) . '-4' . substr(bin2hex(random_bytes(2)), 1) . '-8' . substr(bin2hex(random_bytes(2)), 1) . '-' . bin2hex(random_bytes(6)));
    }
    public static function redact(string $value): string
    {
        $patterns = [
            '/(?i)(authorization\\s*:\\s*bearer\\s+)[^\\s,;]+/',
            '/(?i)(cookie\\s*:\\s*)[^\\r\\n]+/',
            '/(?i)((?:password|token|secret|admin[_-]?key|x-admin-key|storage[_-]?state|private[_-]?path|response[_-]?body|body|path|file|directory|profile)\\s*[:=]\\s*)[^\\s,;]+/',
        ];
        foreach ($patterns as $pattern) {
            $value = preg_replace($pattern, '$1[REDACTED]', $value) ?? '[REDACTED]';
        }
        return $value;
    }

    public static function fromRequest(array $server): string
    {
        return self::create(isset($server['HTTP_X_CORRELATION_ID']) ? (string)$server['HTTP_X_CORRELATION_ID'] : null);
    }
}
