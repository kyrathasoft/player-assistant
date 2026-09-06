<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/AuthorizationPolicy.php';

function authorizationPolicyAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$protectedRoutes = [
    'GET /v1/session', 'GET /v1/me', 'GET /v1/xp', 'GET /v1/xp-awards',
    'GET /v1/word-counts', 'GET /v1/presence', 'GET /v1/quests',
    'GET /v1/revisions', 'GET /v1/magic-items', 'GET /v1/messages',
    'POST /v1/login', 'POST /v1/logout', 'POST /v1/messages',
    'POST /v1/quest-requests', 'POST /v1/xp-level-up-notifications/claim',
    'POST /v1/xp-level-up-notifications/acknowledge',
    'POST /v1/quest-requests/' . str_repeat('a', 32) . '/decision',
    'POST /v1/quest-requests/' . str_repeat('a', 32) . '/acknowledge',
    'POST /v1/messages/' . str_repeat('a', 32) . '/read',
];
foreach ($protectedRoutes as $operation) {
    [$method, $route] = explode(' ', $operation, 2);
    authorizationPolicyAssert(
        AuthorizationPolicy::isCharacterSessionRoute($route),
        "$operation is missing from the canonical character-session policy");
}

foreach (['GET /v1/snapshots/page', 'GET /v1/rpol/page'] as $operation) {
    [$method, $route] = explode(' ', $operation, 2);
    authorizationPolicyAssert(AuthorizationPolicy::isBearerRoute($method, $route), "$operation is not bearer protected");
}
foreach (['GET /v1/admin/health', 'GET /v1/admin/character-accounts', 'POST /v1/tokens', 'PUT /v1/snapshots/page'] as $operation) {
    [$method, $route] = explode(' ', $operation, 2);
    authorizationPolicyAssert(AuthorizationPolicy::isAdminRoute($method, $route), "$operation is not admin protected");
}

$matrix = [
    'anonymous' => [null, false],
    'same-scope' => ['account-a', true],
    'cross-account' => ['account-b', false],
    'alias-only' => ['Ari', false],
];
foreach ($matrix as $name => [$identity, $expected]) {
    authorizationPolicyAssert(
        AuthorizationPolicy::canReadAccount($identity, 'account-a') === $expected,
        "account policy mismatch for $name");
    authorizationPolicyAssert(
        AuthorizationPolicy::canUseAdminCapability($identity, 'admin-key') === false,
        "admin capability was granted to $name");
}

echo "Authorization policy matrix passed.\n";