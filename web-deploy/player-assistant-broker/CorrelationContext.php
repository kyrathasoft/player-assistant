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
        return preg_replace('/(?i)(password|token|cookie|authorization|set-cookie)\\s*[:=]\\s*[^\\s,;]+/', '$1=[REDACTED]', $value) ?? '[REDACTED]';
    }
}
