<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/CharacterAuthService.php';

function assertTrue(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

function expectBrokerError(callable $action, int $status, string $errorName): BrokerHttpException
{
    try {
        $action();
    } catch (BrokerHttpException $exception) {
        assertTrue($exception->status === $status, "Expected HTTP $status, received {$exception->status}.");
        assertTrue($exception->errorName === $errorName, "Expected $errorName, received {$exception->errorName}.");
        return $exception;
    }
    throw new RuntimeException("Expected BrokerHttpException $errorName.");
}

$databasePath = tempnam(sys_get_temp_dir(), 'pa-auth-test-');
if ($databasePath === false) {
    throw new RuntimeException('Unable to create the authentication test database.');
}

try {
    $database = new PDO('sqlite:' . $databasePath, null, null, [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
        PDO::ATTR_EMULATE_PREPARES => false,
    ]);
    $database->exec('PRAGMA foreign_keys = ON');
    $service = new CharacterAuthService($database, [
        'expected_origin' => 'https://example.test',
        'idle_timeout_seconds' => 60,
        'absolute_timeout_seconds' => 600,
        'login_window_seconds' => 300,
        'login_max_failures' => 2,
        'login_lockout_seconds' => 300,
    ]);

    $legacyPassword = 'correct horse battery staple';
    $legacySalt = random_bytes(16);
    $legacyHash = hash_pbkdf2('sha256', $legacyPassword, $legacySalt, 600000, 32, true);
    $import = $service->importLegacyAccounts([
        'schema_version' => 1,
        'format' => 'xp-password-hashes-v1',
        'entries' => [[
            'name' => 'Test Hero',
            'algorithm' => 'PBKDF2-HMAC-SHA256',
            'iterations' => 600000,
            'salt' => base64_encode($legacySalt),
            'hash' => base64_encode($legacyHash),
        ]],
    ]);
    assertTrue($import['imported'] === 1, 'Legacy account import count was incorrect.');

    $session = [];
    $regenerated = false;
    $login = $service->login(
        ['character_name' => 'test hero', 'password' => $legacyPassword],
        '192.0.2.10',
        'https://example.test',
        $session,
        function () use (&$regenerated): void {
            $regenerated = true;
        });
    assertTrue($regenerated, 'The session ID was not regenerated after login.');
    assertTrue($login['authenticated'] === true, 'The valid character login failed.');
    assertTrue($login['account']['character_name'] === 'Test Hero', 'The authenticated character was incorrect.');
    assertTrue($login['account']['character_key'] === 'test', 'The server authorization key was incorrect.');
    assertTrue(isset($login['csrf_token']) && strlen($login['csrf_token']) >= 43, 'The CSRF token was not issued.');

    $stored = $database->query(
        "SELECT password_hash, legacy_hash FROM character_accounts WHERE normalized_name = 'test hero'")->fetch();
    assertTrue(is_string($stored['password_hash']) && $stored['password_hash'] !== '', 'The legacy hash was not upgraded.');
    assertTrue($stored['legacy_hash'] === null, 'The legacy hash remained after upgrading.');
    assertTrue(password_verify($legacyPassword, $stored['password_hash']), 'The upgraded password hash is invalid.');

    $current = $service->currentSession($session);
    assertTrue($current['authenticated'] === true, 'The active session was not restored.');
    $identity = $service->requireCurrentAccount($session);
    assertTrue($identity['account']['character_key'] === 'test', 'Protected identity did not come from the session.');

    expectBrokerError(
        fn() => $service->logout(
            ['origin' => 'https://example.test', 'csrf-token' => 'wrong'],
            '192.0.2.10',
            $session),
        403,
        'csrf_rejected');

    $destroyed = false;
    $logout = $service->logout(
        ['origin' => 'https://example.test', 'csrf-token' => $current['csrf_token']],
        '192.0.2.10',
        $session,
        function () use (&$destroyed): void {
            $destroyed = true;
        });
    assertTrue($destroyed && $logout['authenticated'] === false, 'Logout did not destroy the session.');
    assertTrue($service->currentSession($session)['authenticated'] === false, 'The logged-out session remained active.');

    expectBrokerError(
        fn() => $service->login(
            ['character_name' => 'Test Hero', 'password' => $legacyPassword],
            '192.0.2.11',
            'https://attacker.test',
            $session),
        403,
        'origin_rejected');

    $native = $service->createAccount([
        'character_name' => 'Second Hero',
        'password' => 'another long password',
        'character_key' => 'second-hero',
        'role' => 'player',
    ]);
    assertTrue($native['enabled'] === true, 'The native character account was not enabled.');
    $nativeSession = [];
    $service->login(
        ['character_name' => 'Second Hero', 'password' => 'another long password'],
        '192.0.2.12',
        'https://example.test',
        $nativeSession);
    $revokedSession = $nativeSession;
    $nativeSession['character_auth']['last_seen_at'] = time() - 61;
    assertTrue(
        $service->currentSession($nativeSession)['authenticated'] === false,
        'The idle session timeout did not expire the session.');

    $service->updateAccount($native['id'], ['enabled' => false]);
    assertTrue(
        $service->currentSession($revokedSession)['authenticated'] === false,
        'The administrative account change did not revoke an active session.');
    $disabledError = expectBrokerError(
        fn() => $service->login(
            ['character_name' => 'Second Hero', 'password' => 'another long password'],
            '192.0.2.13',
            'https://example.test',
            $nativeSession),
        401,
        'login_failed');
    $unknownError = expectBrokerError(
        fn() => $service->login(
            ['character_name' => 'No Such Hero', 'password' => 'another long password'],
            '192.0.2.15',
            'https://example.test',
            $nativeSession),
        401,
        'login_failed');
    assertTrue(
        $disabledError->getMessage() === $unknownError->getMessage(),
        'Disabled and unknown accounts returned distinguishable login failures.');

    $service->updateAccount($native['id'], ['enabled' => true]);
    $absoluteSession = [];
    $service->login(
        ['character_name' => 'Second Hero', 'password' => 'another long password'],
        '192.0.2.14',
        'https://example.test',
        $absoluteSession);
    $absoluteSession['character_auth']['absolute_expires_at'] = time() - 1;
    assertTrue(
        $service->currentSession($absoluteSession)['authenticated'] === false,
        'The absolute session timeout did not expire the session.');

    $throttleSession = [];
    for ($attempt = 0; $attempt < 2; $attempt++) {
        expectBrokerError(
            fn() => $service->login(
                ['character_name' => 'Unknown Hero', 'password' => 'wrong password'],
                '192.0.2.20',
                'https://example.test',
                $throttleSession),
            401,
            'login_failed');
    }
    expectBrokerError(
        fn() => $service->login(
            ['character_name' => 'Unknown Hero', 'password' => 'wrong password'],
            '192.0.2.20',
            'https://example.test',
            $throttleSession),
        429,
        'login_failed');

    $accounts = $service->listAccounts();
    assertTrue(count($accounts) === 2, 'The account listing count was incorrect.');
    foreach ($accounts as $account) {
        assertTrue(!array_key_exists('password_hash', $account), 'An account listing exposed a password hash.');
        assertTrue(!array_key_exists('legacy_hash', $account), 'An account listing exposed a legacy hash.');
    }

    $auditEvents = $database->query('SELECT event FROM auth_audit_events')->fetchAll(PDO::FETCH_COLUMN);
    assertTrue(in_array('login_success', $auditEvents, true), 'Successful login audit event was missing.');
    assertTrue(in_array('login_failure', $auditEvents, true), 'Failed login audit event was missing.');
    assertTrue(in_array('logout', $auditEvents, true), 'Logout audit event was missing.');

    fwrite(STDOUT, "Character authentication tests passed.\n");
} finally {
    @unlink($databasePath);
}
