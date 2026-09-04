<?php

declare(strict_types=1);

final class StructuredCorrelation
{
    private const REDACTED = '[REDACTED]';
    private const SENSITIVE_KEYS = [
        'authorization', 'cookie', 'cookies', 'csrf', 'csrf_token', 'password',
        'password_hash', 'secret', 'token', 'protected_body', 'protected_response',
        'protected_response_body', 'request_body', 'response_body',
    ];

    public static function create(?string $candidate = null): string
    {
        return self::sanitizeId($candidate) ?? bin2hex(random_bytes(16));
    }

    public static function sanitizeId(?string $value): ?string
    {
        return is_string($value) && preg_match('/^[a-f0-9]{32}$/D', $value) === 1
            ? $value
            : null;
    }

    public static function fromHeaders(array $headers): string
    {
        $normalized = array_change_key_case($headers, CASE_LOWER);
        return self::create($normalized['x-correlation-id'] ?? $normalized['x-request-id'] ?? null);
    }

    public static function redact(mixed $value, ?string $key = null): mixed
    {
        if ($key !== null && self::isSensitiveKey($key)) {
            return self::REDACTED;
        }
        if (is_array($value)) {
            $safe = [];
            foreach ($value as $field => $item) {
                $safe[$field] = self::redact($item, is_string($field) ? $field : null);
            }
            return $safe;
        }
        if (is_string($value)) {
            return substr(trim(preg_replace('/[\\x00-\\x1F\\x7F]+/', ' ', $value) ?? ''), 0, 1000);
        }
        return $value;
    }

    public static function context(string $correlationId, array $fields = []): array
    {
        $id = self::sanitizeId($correlationId);
        if ($id === null) {
            throw new InvalidArgumentException('The correlation ID is invalid.');
        }
        return ['correlation_id' => $id] + self::redact($fields);
    }

    private static function isSensitiveKey(string $key): bool
    {
        $normalized = strtolower(str_replace(['-', ' '], '_', $key));
        return in_array($normalized, self::SENSITIVE_KEYS, true)
            || str_contains($normalized, 'protected_response');
    }
}
