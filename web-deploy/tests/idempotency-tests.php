<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/IdempotencyLedger.php';

function idempotencyAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$database = new PDO('sqlite::memory:', null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
$database->exec('CREATE TABLE mutation_idempotency (
    account_id TEXT NOT NULL, method TEXT NOT NULL, route TEXT NOT NULL,
    idempotency_key TEXT NOT NULL, request_hash TEXT NOT NULL,
    status INTEGER NULL, response_json TEXT NULL, created_at INTEGER NOT NULL,
    expires_at INTEGER NOT NULL, PRIMARY KEY (account_id, method, route, idempotency_key)
)');
$ledger = new IdempotencyLedger($database, 3600);
$count = 0;
$first = $ledger->execute('account-1', 'POST', '/v1/messages', 'message-key', ['message' => 'hello'], function () use (&$count): array {
    $count++;
    return ['status' => 201, 'body' => ['id' => 'fixed', 'ok' => true]];
});
$second = $ledger->execute('account-1', 'POST', '/v1/messages', 'message-key', ['message' => 'hello'], function () use (&$count): array {
    $count++;
    return ['status' => 500, 'body' => ['unexpected' => true]];
});
idempotencyAssert($count === 1, 'A duplicate mutation must not execute twice.');
idempotencyAssert($second === $first, 'A duplicate mutation must replay the original response.');
try {
    $ledger->execute('account-1', 'POST', '/v1/messages', 'message-key', ['message' => 'different'], static fn(): array => ['status' => 201, 'body' => []]);
    throw new RuntimeException('A changed request body was accepted.');
} catch (BrokerHttpException $exception) {
    idempotencyAssert($exception->status === 409 && $exception->errorName === 'idempotency_key_collision', 'Changed bodies must produce documented 409 collision errors.');
}
$failedCount = 0;
try {
    $ledger->execute('account-1', 'POST', '/v1/messages', 'failed-key', [], function () use (&$failedCount): array {
        $failedCount++;
        throw new BrokerHttpException(422, 'invalid_message', 'invalid');
    });
} catch (BrokerHttpException) {
}
$ledger->execute('account-1', 'POST', '/v1/messages', 'failed-key', [], function () use (&$failedCount): array {
    $failedCount++;
    return ['status' => 201, 'body' => ['ok' => true]];
});
idempotencyAssert($failedCount === 2, 'Failed mutations must not leave replayable success records.');
foreach (['', 'bad key', str_repeat('x', 129)] as $invalidKey) {
    try {
        $ledger->execute('account-1', 'POST', '/v1/messages', $invalidKey, [], static fn(): array => ['status' => 201, 'body' => []]);
        throw new RuntimeException('An invalid idempotency key was accepted.');
    } catch (BrokerHttpException $exception) {
        idempotencyAssert($exception->status === 400 && $exception->errorName === 'invalid_idempotency_key', 'Invalid keys must be rejected at the broker boundary.');
    }
}
$faultDatabase = new PDO('sqlite::memory:', null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
$faultDatabase->exec('CREATE TABLE mutation_idempotency (account_id TEXT NOT NULL, method TEXT NOT NULL, route TEXT NOT NULL, idempotency_key TEXT NOT NULL, request_hash TEXT NOT NULL, status INTEGER NULL, response_json TEXT NULL, created_at INTEGER NOT NULL, expires_at INTEGER NOT NULL, PRIMARY KEY (account_id, method, route, idempotency_key))');
$faultCount = 0;
$faultLedger = new IdempotencyLedger($faultDatabase, 3600, 20, static function (): void { throw new RuntimeException('injected finalization disconnect'); });
try {
    $faultLedger->execute('account-1', 'POST', '/v1/messages', 'fault-key', [], function () use (&$faultCount): array {
        $faultCount++;
        return ['status' => 201, 'body' => ['ok' => true]];
    });
    throw new RuntimeException('Finalization fault was not surfaced.');
} catch (RuntimeException $exception) {
    idempotencyAssert($exception->getMessage() === 'injected finalization disconnect', 'The finalization fault was not propagated.');
}
idempotencyAssert($faultCount === 1, 'A finalization fault must not rerun the mutation.');
idempotencyAssert((int)$faultDatabase->query("SELECT COUNT(*) FROM mutation_idempotency WHERE idempotency_key = 'fault-key'")->fetchColumn() === 1, 'Finalization failure deleted replay evidence.');
try {
    $faultLedger->execute('account-1', 'POST', '/v1/messages', 'fault-key', [], static fn(): array => ['status' => 201, 'body' => []]);
    throw new RuntimeException('An ambiguous mutation was silently rerun.');
} catch (BrokerHttpException $exception) {
    idempotencyAssert($exception->errorName === 'idempotency_in_progress', 'An ambiguous mutation did not remain recoverable.');
}

echo "Idempotency tests passed.\n";
