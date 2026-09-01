<?php

declare(strict_types=1);

/** Common fail-closed envelope for every authenticated protected response. */
final class ProtectedResourceContract
{
    public const SCHEMA_VERSION = 1;
    public const MAX_LIFETIME_SECONDS = 300;

    public static function hasValidContext(array $account, array $session, ?int $now = null): bool
    {
        $now ??= time();
        return preg_match('/^[a-f0-9]{32}$/', (string)($account['id'] ?? '')) === 1
            && (int)($session['session_version'] ?? 0) > 0
            && (int)($session['issued_at'] ?? 0) > 0
            && (int)($session['absolute_expires_at'] ?? 0) > $now;
    }

    public static function decorate(array $body, array $account, array $session, string $route, ?int $now = null): array
    {
        $now ??= time();
        $accountId = (string)($account['id'] ?? '');
        $sessionVersion = (int)($session['session_version'] ?? 0);
        $issuedAt = (int)($session['issued_at'] ?? 0);
        $expiresAt = (int)($session['absolute_expires_at'] ?? 0);
        if (preg_match('/^[a-f0-9]{32}$/', $accountId) !== 1
            || $sessionVersion <= 0 || $issuedAt <= 0 || $expiresAt <= $now) {
            throw new RuntimeException('Protected response context is invalid.');
        }
        $generation = hash('sha256', implode('|', [$accountId, $sessionVersion, $issuedAt]));
        $body['_protected_resource'] = [
            'schema_version' => self::SCHEMA_VERSION,
            'account_id' => $accountId,
            'resource' => $route,
            'generation' => $generation,
            'issued_at' => gmdate(DATE_ATOM, $issuedAt),
            'expires_at' => gmdate(DATE_ATOM, min($expiresAt, $now + self::MAX_LIFETIME_SECONDS)),
            'resource_revision' => hash('sha256', json_encode($body, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_PRESERVE_ZERO_FRACTION | JSON_THROW_ON_ERROR)),
            'nonce' => bin2hex(random_bytes(16)),
        ];
        return $body;
    }

    public static function validate(array $body, string $accountId, string $route, ?int $now = null): bool
    {
        $now ??= time();
        $meta = $body['_protected_resource'] ?? null;
        if (!is_array($meta)
            || (int)($meta['schema_version'] ?? 0) !== self::SCHEMA_VERSION
            || !hash_equals($accountId, (string)($meta['account_id'] ?? ''))
            || (string)($meta['resource'] ?? '') !== $route
            || !preg_match('/^[a-f0-9]{64}$/', (string)($meta['generation'] ?? ''))
            || !preg_match('/^[a-f0-9]{64}$/', (string)($meta['resource_revision'] ?? ''))
            || !preg_match('/^[a-f0-9]{32}$/', (string)($meta['nonce'] ?? ''))) return false;
        $expires = strtotime((string)($meta['expires_at'] ?? ''));
        $issued = strtotime((string)($meta['issued_at'] ?? ''));
        $withoutMetadata = $body;
        unset($withoutMetadata['_protected_resource']);
        $revision = hash('sha256', json_encode($withoutMetadata, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_PRESERVE_ZERO_FRACTION | JSON_THROW_ON_ERROR));
        return $issued !== false && $expires !== false && $issued <= $now + 5 && $expires > $now
            && hash_equals($revision, (string)$meta['resource_revision']);
    }
}
