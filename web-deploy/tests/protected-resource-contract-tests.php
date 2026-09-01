<?php
declare(strict_types=1);
require_once __DIR__ . '/../player-assistant-broker/ProtectedResourceContract.php';
function protectedAssert(bool $ok, string $message): void { if (!$ok) throw new RuntimeException($message); }
$account = ['id' => str_repeat('a', 32)];
$session = ['session_version' => 3, 'issued_at' => 1720000000, 'absolute_expires_at' => 1720003600];
$body = ProtectedResourceContract::decorate(['schema_version' => 1, 'value' => 'fixture'], $account, $session, '/v1/protected', 1720000100);
protectedAssert(ProtectedResourceContract::validate($body, $account['id'], '/v1/protected', 1720000100), 'valid protected envelope rejected');
protectedAssert(!ProtectedResourceContract::validate($body, str_repeat('b', 32), '/v1/protected', 1720000100), 'cross-account envelope accepted');
$bodyTampered = $body; $bodyTampered['value'] = 'tampered';
protectedAssert(!ProtectedResourceContract::validate($bodyTampered, $account['id'], '/v1/protected', 1720000100), 'tampered protected body accepted');
protectedAssert(!ProtectedResourceContract::validate($body, $account['id'], '/v1/other', 1720000100), 'cross-resource envelope accepted');
$stale = $body; $stale['_protected_resource']['expires_at'] = gmdate(DATE_ATOM, 1720000000);
protectedAssert(!ProtectedResourceContract::validate($stale, $account['id'], '/v1/protected', 1720000100), 'expired envelope accepted');
$replay = $body; $replay['_protected_resource']['nonce'] = 'bad';
protectedAssert(!ProtectedResourceContract::validate($replay, $account['id'], '/v1/protected', 1720000100), 'malformed replay nonce accepted');
$downgraded = $body; $downgraded['_protected_resource']['schema_version'] = 0;
protectedAssert(!ProtectedResourceContract::validate($downgraded, $account['id'], '/v1/protected', 1720000100), 'downgraded schema accepted');
$revoked = ProtectedResourceContract::decorate(['schema_version' => 1], $account, ['session_version' => 4, 'issued_at' => 1720000000, 'absolute_expires_at' => 1720003600], '/v1/protected', 1720000100);
protectedAssert($revoked['_protected_resource']['generation'] !== $body['_protected_resource']['generation'], 'session revocation did not change generation');
echo "Protected resource contract tests passed.
";
