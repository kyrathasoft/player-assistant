<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/DatabaseMigrationService.php';
require_once __DIR__ . '/../player-assistant-broker/CharacterAuthService.php';

function loginHardeningAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

function loginHardeningExpect(callable $action, int $status): BrokerHttpException
{
    try {
        $action();
    } catch (BrokerHttpException $exception) {
        loginHardeningAssert($exception->status === $status, "Expected HTTP $status, received {$exception->status}.");
        loginHardeningAssert($exception->errorName === 'login_failed', 'The login failure contract changed.');
        return $exception;
    }
    throw new RuntimeException("Expected HTTP $status login failure.");
}

function loginHardeningScopeHash(string $scope, string $name, string $address): string
{
    $packedAddress = inet_pton($address);
    $addressScope = $packedAddress === false ? $address : bin2hex($packedAddress);
    $value = $scope === 'account-source'
        ? 'account-source:' . $name . "\0" . $addressScope
        : 'address:' . $addressScope;
    return hash('sha256', $value);
}

$databasePath = tempnam(sys_get_temp_dir(), 'pa-login-hardening-');
if ($databasePath === false) {
    throw new RuntimeException('Unable to create the login-hardening database.');
}

try {
    $database = new PDO('sqlite:' . $databasePath, null, null, [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
        PDO::ATTR_EMULATE_PREPARES => false,
    ]);
    (new DatabaseMigrationService($database, dirname($databasePath) . '/migration-backups'))->migrate();
    $now = 1_800_000_000;
    $service = new CharacterAuthService($database, [
        'expected_origin' => 'https://example.test',
        'idle_timeout_seconds' => 60,
        'absolute_timeout_seconds' => 600,
        'login_window_seconds' => 300,
        'login_max_failures' => 2,
        'login_progressive_delay_base_seconds' => 2,
        'login_progressive_delay_max_seconds' => 32,
        'login_address_max_failures' => 4,
        'login_address_delay_seconds' => 30,
        'login_lockout_seconds' => 300,
        'audit_retention_seconds' => 3600,
        'audit_address_mode' => 'hash',
        'audit_address_hash_key' => 'login-hardening-test-key',
    ], static function () use (&$now): int {
        return $now;
    });
    $password = 'a sufficiently long password';
    $service->createAccount([
        'character_name' => 'Known Hero',
        'password' => $password,
        'character_key' => 'known-hero',
        'role' => 'player',
    ]);

    $session = [];
    for ($attempt = 0; $attempt < 2; $attempt++) {
        loginHardeningExpect(
            fn() => $service->login(
                ['character_name' => 'Known Hero', 'password' => 'wrong password'],
                '192.0.2.10',
                'https://example.test',
                $session),
            401);
    }
    loginHardeningExpect(
        fn() => $service->login(
            ['character_name' => 'Known Hero', 'password' => $password],
            '192.0.2.10',
            'https://example.test',
            $session),
        429);

    $otherSourceSession = [];
    $otherSourceLogin = $service->login(
        ['character_name' => 'Known Hero', 'password' => $password],
        '192.0.2.11',
        'https://example.test',
        $otherSourceSession);
    loginHardeningAssert(
        $otherSourceLogin['authenticated'] === true,
        'Failures from one source globally locked the known character name.');

    $now += 2;
    loginHardeningExpect(
        fn() => $service->login(
            ['character_name' => 'Known Hero', 'password' => 'wrong password'],
            '192.0.2.10',
            'https://example.test',
            $session),
        401);
    loginHardeningExpect(
        fn() => $service->login(
            ['character_name' => 'Known Hero', 'password' => $password],
            '192.0.2.10',
            'https://example.test',
            $session),
        429);
    $now += 3;
    loginHardeningExpect(
        fn() => $service->login(
            ['character_name' => 'Known Hero', 'password' => $password],
            '192.0.2.10',
            'https://example.test',
            $session),
        429);
    $now += 1;
    $recovered = $service->login(
        ['character_name' => 'Known Hero', 'password' => $password],
        '192.0.2.10',
        'https://example.test',
        $session);
    loginHardeningAssert($recovered['authenticated'] === true, 'The progressive account-source delay did not expire.');
    $scopeQuery = $database->prepare(
        'SELECT failure_count, blocked_until FROM auth_rate_limits WHERE scope_hash = ?');
    $scopeQuery->execute([loginHardeningScopeHash('account-source', 'known hero', '192.0.2.10')]);
    loginHardeningAssert(
        $scopeQuery->fetchColumn() === false,
        'Successful login did not clear its account-source throttle.');
    $scopeQuery->execute([loginHardeningScopeHash('address', '', '192.0.2.10')]);
    loginHardeningAssert(
        (int)$scopeQuery->fetchColumn() === 3,
        'Successful login incorrectly cleared the address-wide failure history.');

    $progressiveService = new CharacterAuthService($database, [
        'expected_origin' => 'https://example.test',
        'idle_timeout_seconds' => 60,
        'absolute_timeout_seconds' => 600,
        'login_window_seconds' => 300,
        'login_max_failures' => 2,
        'login_progressive_delay_base_seconds' => 2,
        'login_progressive_delay_max_seconds' => 32,
        'login_address_max_failures' => 100,
        'login_address_delay_seconds' => 30,
        'login_lockout_seconds' => 300,
        'audit_retention_seconds' => 3600,
        'audit_address_mode' => 'hash',
        'audit_address_hash_key' => 'login-hardening-test-key',
    ], static function () use (&$now): int {
        return $now;
    });
    $progressiveSession = [];
    loginHardeningExpect(
        fn() => $progressiveService->login(
            ['character_name' => 'Delay Target', 'password' => 'wrong password'],
            '192.0.2.60',
            'https://example.test',
            $progressiveSession),
        401);
    foreach ([2, 4, 8, 16, 32, 32] as $expectedDelay) {
        loginHardeningExpect(
            fn() => $progressiveService->login(
                ['character_name' => 'Delay Target', 'password' => 'wrong password'],
                '192.0.2.60',
                'https://example.test',
                $progressiveSession),
            401);
        $scopeQuery->execute([loginHardeningScopeHash('account-source', 'delay target', '192.0.2.60')]);
        $scopeRow = $scopeQuery->fetch();
        $blockedUntil = is_array($scopeRow) ? (int)$scopeRow['blocked_until'] : 0;
        loginHardeningAssert(
            $blockedUntil - $now === $expectedDelay,
            "Expected progressive delay $expectedDelay seconds, received " . ($blockedUntil - $now) . '.');
        $now += $expectedDelay;
    }

    $malformedSourceSession = [];
    loginHardeningExpect(
        fn() => $service->login(
            ['character_name' => 'Known Hero', 'password' => $password],
            'not-an-ip-address',
            'https://example.test',
            $malformedSourceSession),
        400);

    $ipv6Session = [];
    for ($attempt = 0; $attempt < 2; $attempt++) {
        loginHardeningExpect(
            fn() => $service->login(
                ['character_name' => 'Known Hero', 'password' => 'wrong password'],
                '2001:db8::1',
                'https://example.test',
                $ipv6Session),
            401);
    }
    loginHardeningExpect(
        fn() => $service->login(
            ['character_name' => 'Known Hero', 'password' => $password],
            '2001:0db8:0000:0000:0000:0000:0000:0001',
            'https://example.test',
            $ipv6Session),
        429);

    $addressSession = [];
    foreach (['Unknown One', 'Unknown Two', 'Unknown Three', 'Unknown Four'] as $name) {
        loginHardeningExpect(
            fn() => $service->login(
                ['character_name' => $name, 'password' => 'wrong password'],
                '192.0.2.50',
                'https://example.test',
                $addressSession),
            401);
    }
    loginHardeningExpect(
        fn() => $service->login(
            ['character_name' => 'Known Hero', 'password' => $password],
            '192.0.2.50',
            'https://example.test',
            $addressSession),
        429);
    $now += 30;
    $addressRecovered = $service->login(
        ['character_name' => 'Known Hero', 'password' => $password],
        '192.0.2.50',
        'https://example.test',
        $addressSession);
    loginHardeningAssert($addressRecovered['authenticated'] === true, 'The stronger address throttle did not expire.');

    fwrite(STDOUT, "Login hardening tests passed.\n");
} finally {
    @unlink($databasePath);
}
