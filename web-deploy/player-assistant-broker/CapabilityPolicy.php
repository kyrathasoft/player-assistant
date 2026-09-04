<?php

declare(strict_types=1);

/** Least-privilege capability definitions shared by administration and bearer APIs. */
final class CapabilityPolicy
{
    private const ROUTES = [
        'GET /v1/admin/health' => 'admin.health.read',
        'GET /v1/admin/character-accounts' => 'accounts.read',
        'POST /v1/admin/character-accounts' => 'accounts.create',
        'POST /v1/admin/character-accounts/import' => 'accounts.import',
        'PATCH /v1/admin/character-accounts/{account_id}' => 'accounts.update',
        'POST /v1/tokens' => 'tokens.issue',
        'DELETE /v1/tokens/{token_id}' => 'tokens.revoke',
        'PUT /v1/admin/word-counts' => 'word-counts.publish',
        'PUT /v1/snapshots/page' => 'snapshots.publish',
        'GET /v1/snapshots/page' => 'snapshots.read',
        'GET /v1/rpol/page' => 'rpol.read',
    ];

    public static function forRoute(string $method, string $route): ?string
    {
        $key = strtoupper($method) . ' ' . $route;
        if (isset(self::ROUTES[$key])) return self::ROUTES[$key];
        if (preg_match('#^PATCH /v1/admin/character-accounts/[a-f0-9]{32}$#', $key) === 1) return 'accounts.update';
        if (preg_match('#^DELETE /v1/tokens/[a-f0-9]{32}$#', $key) === 1) return 'tokens.revoke';
        return null;
    }

    public static function known(string $name): bool
    {
        return in_array($name, array_values(self::ROUTES), true);
    }

    public static function requiresResource(string $name): bool
    {
        return in_array($name, ['snapshots.read', 'rpol.read', 'snapshots.publish', 'accounts.update', 'tokens.revoke'], true);
    }

    public static function permits(array $grant, string $operation, ?string $resource = null, ?string $accountId = null): bool
    {
        if (($grant['name'] ?? null) !== $operation || !self::known($operation)) return false;
        if (self::requiresResource($operation) && (!isset($grant['resource']) || !is_string($grant['resource']) || $grant['resource'] === '')) return false;
        if (isset($grant['account_id']) && (!is_string($accountId) || !hash_equals($grant['account_id'], $accountId))) return false;
        if (isset($grant['resource']) && $resource !== null && !hash_equals((string)$grant['resource'], $resource)) return false;
        return true;
    }

    public static function validateGrants(mixed $grants): array
    {
        if (!is_array($grants) || $grants === [] || count($grants) > 8) throw new InvalidArgumentException('At least one narrowly scoped capability is required.');
        $normalized = [];
        foreach ($grants as $grant) {
            if (!is_array($grant) || !is_string($grant['name'] ?? null) || !self::known($grant['name'])) throw new InvalidArgumentException('The capability grant is unknown.');
            $entry = ['name' => $grant['name']];
            foreach (['account_id', 'resource'] as $key) {
                if (array_key_exists($key, $grant)) {
                    if (!is_string($grant[$key]) || $grant[$key] === '' || strlen($grant[$key]) > 512) throw new InvalidArgumentException('The capability scope is invalid.');
                    $entry[$key] = $grant[$key];
                }
            }
            if (self::requiresResource($entry['name']) && !isset($entry['resource'])) throw new InvalidArgumentException('The capability requires an explicit resource scope.');
            if (isset($entry['resource']) && str_contains($entry['resource'], '*')) throw new InvalidArgumentException('Wildcard capability resources are forbidden.');
            $fingerprint = json_encode($entry, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
            if (isset($normalized[$fingerprint])) throw new InvalidArgumentException('Duplicate capability grants are forbidden.');
            $normalized[$fingerprint] = $entry;
        }
        return array_values($normalized);
    }
}
