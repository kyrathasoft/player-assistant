<?php

declare(strict_types=1);

/** Common fail-closed, request-bound envelope for authenticated responses. */
final class ProtectedResourceContract
{
    public const SCHEMA_VERSION = 2;
    public const ALGORITHM = 'Ed25519';
    public const MAX_LIFETIME_SECONDS = 300;

    public static function hasValidContext(array $account, array $session, ?int $now = null): bool
    {
        $now ??= time();
        return preg_match('/^[a-f0-9]{32}$/', (string)($account['id'] ?? '')) === 1
            && (int)($session['session_version'] ?? 0) > 0
            && (int)($session['issued_at'] ?? 0) > 0
            && (int)($session['absolute_expires_at'] ?? 0) > $now;
    }

    public static function decorate(
        array $body,
        array $account,
        array $session,
        string $method,
        string $route,
        ?int $now = null,
        ?array $signingConfig = null): array
    {
        $now ??= time();
        $accountId = (string)($account['id'] ?? '');
        $sessionVersion = (int)($session['session_version'] ?? 0);
        $issuedAt = $now;
        $expiresAt = min((int)($session['absolute_expires_at'] ?? 0), $now + self::MAX_LIFETIME_SECONDS);
        $signingConfig ??= [];
        $secret = self::decodeKey((string)($signingConfig['signing_key'] ?? ''), SODIUM_CRYPTO_SIGN_SECRETKEYBYTES);
        $public = self::decodeKey((string)($signingConfig['public_key'] ?? ''), SODIUM_CRYPTO_SIGN_PUBLICKEYBYTES);
        $keyId = (string)($signingConfig['key_id'] ?? '');
        if (!self::validAccount($accountId) || $sessionVersion <= 0 || $expiresAt <= $now
            || $secret === null || $public === null || $keyId === ''
            || !hash_equals($public, sodium_crypto_sign_publickey_from_secretkey($secret))) {
            throw new RuntimeException('Protected response context or signing key is invalid.');
        }
        $nonce = bin2hex(random_bytes(16));
        $bodyDigest = self::digest($body);
        $meta = [
            'schema_version' => self::SCHEMA_VERSION,
            'algorithm' => self::ALGORITHM,
            'key_id' => $keyId,
            'method' => strtoupper($method),
            'route' => $route,
            'resource' => $route,
            'account_id' => $accountId,
            'generation' => hash('sha256', implode('|', [$accountId, $sessionVersion, (int)($session['issued_at'] ?? 0)])),
            'body_digest' => $bodyDigest,
            'nonce' => $nonce,
            'issued_at' => gmdate(DATE_ATOM, $issuedAt),
            'expires_at' => gmdate(DATE_ATOM, $expiresAt),
        ];
        $meta['signature'] = base64_encode(sodium_crypto_sign_detached(self::canonical($meta), $secret));
        $body['_protected_resource'] = $meta;
        return $body;
    }

    public static function validate(
        array $body,
        string $accountId,
        string $method,
        string $route,
        ?int $now = null,
        ?array $trustConfig = null): bool
    {
        $now ??= time();
        $meta = $body['_protected_resource'] ?? null;
        if (!is_array($meta) || (int)($meta['schema_version'] ?? 0) !== self::SCHEMA_VERSION
            || ($meta['algorithm'] ?? null) !== self::ALGORITHM
            || !self::validAccount($accountId) || !hash_equals($accountId, (string)($meta['account_id'] ?? ''))
            || (string)($meta['method'] ?? '') !== strtoupper($method)
            || (string)($meta['route'] ?? '') !== $route
            || (string)($meta['resource'] ?? '') !== $route
            || !preg_match('/^[a-f0-9]{64}$/', (string)($meta['generation'] ?? ''))
            || !preg_match('/^[a-f0-9]{64}$/', (string)($meta['body_digest'] ?? ''))
            || !preg_match('/^[a-f0-9]{32}$/', (string)($meta['nonce'] ?? ''))
            || !isset($meta['signature'])) return false;
        $withoutMetadata = $body;
        unset($withoutMetadata['_protected_resource']);
        if (!hash_equals((string)$meta['body_digest'], self::digest($withoutMetadata))) return false;
        $issued = strtotime((string)($meta['issued_at'] ?? ''));
        $expires = strtotime((string)($meta['expires_at'] ?? ''));
        if ($issued === false || $expires === false || $issued > $now + 5 || $expires <= $now
            || $expires - $issued > self::MAX_LIFETIME_SECONDS) return false;
        $trustConfig ??= [];
        if (($trustConfig['key_id'] ?? null) !== ($meta['key_id'] ?? null)
            || ($meta['algorithm'] ?? null) !== self::ALGORITHM) return false;
        $public = self::decodeKey((string)($trustConfig['public_key'] ?? ''), SODIUM_CRYPTO_SIGN_PUBLICKEYBYTES);
        $signature = base64_decode((string)$meta['signature'], true);
        if ($public === null || $signature === false || strlen($signature) !== SODIUM_CRYPTO_SIGN_BYTES) return false;
        $signed = $meta;
        unset($signed['signature']);
        return sodium_crypto_sign_verify_detached($signature, self::canonical($signed), $public);
    }

    public static function digest(array $value): string
    {
        return hash('sha256', self::canonical($value));
    }

    private static function canonical(array $value): string
    {
        $normalize = static function (mixed $item) use (&$normalize): mixed {
            if (!is_array($item)) return $item;
            if (array_is_list($item)) return array_map($normalize, $item);
            $result = [];
            foreach ($item as $key => $child) $result[(string)$key] = $normalize($child);
            ksort($result, SORT_STRING);
            return $result;
        };
        return json_encode($normalize($value), JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_PRESERVE_ZERO_FRACTION | JSON_THROW_ON_ERROR);
    }

    private static function validAccount(string $accountId): bool
    {
        return preg_match('/^[a-f0-9]{32}$/', $accountId) === 1;
    }

    private static function decodeKey(string $value, int $length): ?string
    {
        $decoded = base64_decode($value, true);
        return $decoded !== false && strlen($decoded) === $length ? $decoded : null;
    }
}
