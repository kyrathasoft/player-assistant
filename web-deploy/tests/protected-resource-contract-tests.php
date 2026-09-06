<?php
declare(strict_types=1);
require_once __DIR__ . '/../player-assistant-broker/ProtectedResourceContract.php';
function protectedAssert(bool $ok, string $message): void { if (!$ok) throw new RuntimeException($message); }
$pair = sodium_crypto_sign_keypair();
$secret = sodium_crypto_sign_secretkey($pair);
$public = sodium_crypto_sign_publickey($pair);
$trust = ['key_id' => 'protected-test-2026', 'public_key' => base64_encode($public)];
$signing = $trust + ['signing_key' => base64_encode($secret)];
$account = ['id' => str_repeat('a', 32)];
$session = ['session_version' => 3, 'issued_at' => 1720000000, 'absolute_expires_at' => 1720003600];
$body = ProtectedResourceContract::decorate(['schema_version' => 1, 'value' => 'fixture'], $account, $session, 'GET', '/v1/protected', 1720000100, $signing);
protectedAssert(ProtectedResourceContract::validate($body, $account['id'], 'GET', '/v1/protected', 1720000100, $trust, $session), 'valid protected envelope rejected');
protectedAssert($body['_protected_resource']['issued_at'] === gmdate(DATE_ATOM, 1720000100), 'response freshness was confused with session issuance time');
protectedAssert(!ProtectedResourceContract::validate($body, $account['id'], 'GET', '/v1/protected', 1720000401, $trust, $session), '301-second response was accepted');
$nearExpirySession = $session;
$nearExpirySession['absolute_expires_at'] = 1720000400;
$nearExpiry = ProtectedResourceContract::decorate(['schema_version' => 1, 'value' => 'near expiry'], $account, $nearExpirySession, 'GET', '/v1/protected', 1720000399, $signing);
protectedAssert(ProtectedResourceContract::validate($nearExpiry, $account['id'], 'GET', '/v1/protected', 1720000399, $trust, $nearExpirySession), 'near-expiry response was rejected');
protectedAssert(!ProtectedResourceContract::validate($nearExpiry, $account['id'], 'GET', '/v1/protected', 1720000400, $trust, $nearExpirySession), 'absolute-session-expiry response was accepted');
$future = ProtectedResourceContract::decorate(['schema_version' => 1, 'value' => 'future'], $account, $session, 'GET', '/v1/protected', 1720000106, $signing);
protectedAssert(!ProtectedResourceContract::validate($future, $account['id'], 'GET', '/v1/protected', 1720000100, $trust, $session), 'future-issued response was accepted');
$revokedSession = $session;
$revokedSession['session_version']++;
protectedAssert(!ProtectedResourceContract::validate($body, $account['id'], 'GET', '/v1/protected', 1720000100, $trust, $revokedSession), 'revoked-session response was accepted');
foreach ([
    ['account', fn() => ProtectedResourceContract::validate($body, str_repeat('b', 32), 'GET', '/v1/protected', 1720000100, $trust)],
    ['route', fn() => ProtectedResourceContract::validate($body, $account['id'], 'GET', '/v1/other', 1720000100, $trust)],
    ['method', fn() => ProtectedResourceContract::validate($body, $account['id'], 'POST', '/v1/protected', 1720000100, $trust)],
    ['body', fn() => ProtectedResourceContract::validate(array_replace($body, ['value' => 'tampered']), $account['id'], 'GET', '/v1/protected', 1720000100, $trust)],
] as [$name, $check]) protectedAssert(!$check(), "$name tampering was accepted");
$stale = $body; $stale['_protected_resource']['expires_at'] = gmdate(DATE_ATOM, 1720000000);
protectedAssert(!ProtectedResourceContract::validate($stale, $account['id'], 'GET', '/v1/protected', 1720000100, $trust), 'expired envelope accepted');
$downgraded = $body; $downgraded['_protected_resource']['schema_version'] = 1;
protectedAssert(!ProtectedResourceContract::validate($downgraded, $account['id'], 'GET', '/v1/protected', 1720000100, $trust), 'downgraded schema accepted');
$unknown = $body; $unknown['_protected_resource']['algorithm'] = 'HMAC-SHA256';
protectedAssert(!ProtectedResourceContract::validate($unknown, $account['id'], 'GET', '/v1/protected', 1720000100, $trust), 'unknown algorithm accepted');
$rotated = $body; $rotated['_protected_resource']['key_id'] = 'rotated-key';
protectedAssert(!ProtectedResourceContract::validate($rotated, $account['id'], 'GET', '/v1/protected', 1720000100, $trust), 'key rotation tampering accepted');
$replay = $body; $replay['_protected_resource']['nonce'] = 'bad';
protectedAssert(!ProtectedResourceContract::validate($replay, $account['id'], 'GET', '/v1/protected', 1720000100, $trust), 'replay nonce tampering accepted');
$reordered = $body; $reordered['_protected_resource']['route'] = '/v1/other';
protectedAssert(!ProtectedResourceContract::validate($reordered, $account['id'], 'GET', '/v1/protected', 1720000100, $trust), 'canonicalization tampering accepted');
echo "Protected resource contract tests passed.\n";
