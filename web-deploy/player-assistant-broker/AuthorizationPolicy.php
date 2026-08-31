<?php

declare(strict_types=1);

/** Canonical protected-surface classification shared by the HTTP boundary. */
final class AuthorizationPolicy
{
    public static function canReadAccount(?string $sessionAccountId, ?string $resourceOwnerId): bool
    {
        return is_string($sessionAccountId)
            && $sessionAccountId !== ''
            && is_string($resourceOwnerId)
            && $resourceOwnerId !== ''
            && hash_equals($sessionAccountId, $resourceOwnerId);
    }

    public static function canUseAdminCapability(?string $adminKey, ?string $expectedKey): bool
    {
        return is_string($adminKey)
            && is_string($expectedKey)
            && $adminKey !== ''
            && $expectedKey !== ''
            && hash_equals($expectedKey, $adminKey);
    }

    public static function isCharacterSessionRoute(string $route): bool
    {
        return in_array($route, [
            '/v1/login', '/v1/session', '/v1/me', '/v1/xp', '/v1/xp-awards',
            '/v1/xp-level-up-notifications/claim',
            '/v1/xp-level-up-notifications/acknowledge',
            '/v1/word-counts', '/v1/presence', '/v1/quests', '/v1/revisions',
            '/v1/magic-items', '/v1/quest-requests', '/v1/messages', '/v1/logout',
        ], true)
            || preg_match('#^/v1/quest-requests/[a-f0-9]{32}/(?:decision|acknowledge)$#', $route) === 1
            || preg_match('#^/v1/messages/[a-f0-9]{32}/read$#', $route) === 1;
    }

    public static function isBearerRoute(string $method, string $route): bool
    {
        return ($method === 'GET' && in_array($route, ['/v1/snapshots/page', '/v1/rpol/page'], true));
    }

    public static function isAdminRoute(string $method, string $route): bool
    {
        return str_starts_with($route, '/v1/admin/')
            || ($method === 'POST' && $route === '/v1/tokens')
            || ($method === 'DELETE' && preg_match('#^/v1/tokens/[a-f0-9]{32}$#', $route) === 1)
            || ($method === 'PUT' && $route === '/v1/snapshots/page');
    }
}
