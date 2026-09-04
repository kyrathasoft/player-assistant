<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/DatabaseMigrationService.php';
require_once __DIR__ . '/../player-assistant-broker/CharacterAuthService.php';

function authorizationPolicyAssert(bool $condition, string $message): void { if (!$condition) throw new RuntimeException($message); }
function authorizationPolicyError(callable $action, int $status, string $name): void {
    try { $action(); } catch (BrokerHttpException $exception) {
        authorizationPolicyAssert($exception->status === $status, "Expected $status, received {$exception->status}.");
        authorizationPolicyAssert($exception->errorName === $name, "Expected $name, received {$exception->errorName}."); return;
    }
    throw new RuntimeException("Expected $name.");
}

$policy = json_decode((string)file_get_contents(__DIR__ . '/authorization-policy-fixture.json'), true, 16, JSON_THROW_ON_ERROR);
$routes = $policy['protected_routes'] ?? [];
$cases = $policy['cases'] ?? [];
authorizationPolicyAssert(count($routes) > 0 && $cases === ['same_scope', 'cross_account', 'role_confusion', 'alias', 'anonymous'], 'The canonical authorization policy fixture is incomplete.');
$brokerSource = (string)file_get_contents(__DIR__ . '/../player-assistant-broker/BrokerService.php');
foreach ($routes as $entry) {
    $route = (string)$entry['route']; $guard = (string)$entry['guard'];
    $sourceFragment = (string)($entry['source_fragment'] ?? "'" . $route . "'");
    authorizationPolicyAssert(str_contains($brokerSource, $sourceFragment), "Protected route missing from BrokerService: $route");
    authorizationPolicyAssert(str_contains($brokerSource, $guard), "Protected route has no canonical guard: $route");
}

$databasePath = tempnam(sys_get_temp_dir(), 'pa-auth-policy-');
if ($databasePath === false) throw new RuntimeException('Unable to create the authorization policy database.');
try {
    $database = new PDO('sqlite:' . $databasePath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION, PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC]);
    $database->exec('PRAGMA foreign_keys = ON');
    (new DatabaseMigrationService($database, sys_get_temp_dir() . '/pa-policy-migrations-' . bin2hex(random_bytes(4))))->migrate();
    $service = new CharacterAuthService($database, ['expected_origin' => 'https://example.test', 'idle_timeout_seconds' => 60, 'absolute_timeout_seconds' => 600, 'login_window_seconds' => 300, 'login_max_failures' => 3, 'login_lockout_seconds' => 300, 'audit_retention_seconds' => 3600, 'audit_address_mode' => 'hash', 'audit_address_hash_key' => 'fixture-only']);
    $accounts = [];
    foreach ([['alpha','player',['A']], ['beta','player',['B']], ['warden','dm',['DM']]] as [$key, $role, $aliases]) $accounts[$key] = $service->createAccount(['character_name' => ucfirst($key), 'password' => "$key synthetic password", 'character_key' => $key, 'role' => $role, 'aliases' => $aliases]);
    $sessions = [];
    foreach (['alpha','beta','warden'] as $key) { $sessions[$key] = []; $service->login(['character_name' => $key, 'password' => "$key synthetic password"], '192.0.2.10', 'https://example.test', $sessions[$key]); }
    $same = $service->requireCurrentAccount($sessions['alpha']);
    authorizationPolicyAssert($same['account']['id'] === $accounts['alpha']['id'] && $same['account']['role'] === 'player', 'same_scope did not preserve canonical identity and role.');
    $crossAccountSession = ['account_id' => $accounts['alpha']['id'], 'session_version' => 1];
    authorizationPolicyError(fn() => $service->requireCurrentAccount($crossAccountSession), 401, 'authentication_required');
    $roleConfusionSession = ['account_id' => $accounts['alpha']['id'], 'role' => 'dm'];
    authorizationPolicyError(fn() => $service->requireCurrentAccount($roleConfusionSession), 401, 'authentication_required');
    $aliasSession = []; $aliasLogin = $service->login(['character_name' => 'A', 'password' => 'alpha synthetic password'], '192.0.2.10', 'https://example.test', $aliasSession);
    authorizationPolicyAssert($aliasLogin['account']['id'] === $accounts['alpha']['id'], 'alias did not resolve to the canonical account.');
    $anonymousSession = [];
    authorizationPolicyError(fn() => $service->requireCurrentAccount($anonymousSession), 401, 'authentication_required');
    authorizationPolicyError(fn() => $service->requireMutationAccount(['origin' => 'https://example.test', 'csrf-token' => 'wrong'], $sessions['alpha']), 403, 'csrf_rejected');
} finally { @unlink($databasePath); }

fwrite(STDOUT, "Canonical authorization policy tests passed (" . count($routes) . " protected routes, " . count($cases) . " identity cases).\n");
