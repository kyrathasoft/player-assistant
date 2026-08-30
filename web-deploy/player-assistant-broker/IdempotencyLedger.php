<?php

declare(strict_types=1);

final class IdempotencyLedger
{
    private $beforeFinalizationCommit;
    public function __construct(
        private readonly PDO $database,
        private readonly int $retentionSeconds = 86400,
        private readonly int $waitMilliseconds = 5000,
        ?callable $beforeFinalizationCommit = null,
        private readonly string $tableName = 'mutation_idempotency')
    {
        $this->beforeFinalizationCommit = $beforeFinalizationCommit;
        if (preg_match('/^[a-z_]+$/D', $this->tableName) !== 1) throw new InvalidArgumentException('Invalid idempotency ledger table.');
    }

    public function execute(
        string $accountId,
        string $method,
        string $route,
        string $key,
        array $body,
        callable $mutation): array
    {
        $this->validateKey($key);
        $hash = hash('sha256', json_encode($body, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_PRESERVE_ZERO_FRACTION | JSON_THROW_ON_ERROR));
        $started = microtime(true);
        while (true) {
            $this->database->exec('BEGIN IMMEDIATE');
            try {
                $this->prune();
                $statement = $this->database->prepare(
                    'SELECT request_hash, status, response_json FROM ' . $this->tableName . '
                     WHERE account_id = ? AND method = ? AND route = ? AND idempotency_key = ?');
                $statement->execute([$accountId, $method, $route, $key]);
                $existing = $statement->fetch();
                if (is_array($existing)) {
                    if (!hash_equals((string)$existing['request_hash'], $hash)) {
                        $this->database->exec('ROLLBACK');
                        throw new BrokerHttpException(409, 'idempotency_key_collision', 'The idempotency key was already used with a different request body.');
                    }
                    if ($existing['response_json'] !== null && $existing['status'] !== null) {
                        $this->database->exec('COMMIT');
                        $decoded = json_decode((string)$existing['response_json'], true, 32, JSON_THROW_ON_ERROR);
                        if (!is_array($decoded)) {
                            throw new RuntimeException('The idempotency response ledger entry is invalid.');
                        }
                        return ['status' => (int)$existing['status'], 'body' => $decoded];
                    }
                    $this->database->exec('ROLLBACK');
                    if ((microtime(true) - $started) * 1000 >= $this->waitMilliseconds) {
                        throw new BrokerHttpException(409, 'idempotency_in_progress', 'An identical mutation is already in progress.');
                    }
                    usleep(25000);
                    continue;
                }
                $this->database->prepare(
                    'INSERT INTO ' . $this->tableName . '
                     (account_id, method, route, idempotency_key, request_hash, created_at, expires_at)
                     VALUES (?, ?, ?, ?, ?, ?, ?)')
                    ->execute([$accountId, $method, $route, $key, $hash, time(), time() + $this->retentionSeconds]);
                $this->database->exec('COMMIT');
                break;
            } catch (Throwable $exception) {
                if ($this->database->inTransaction()) {
                    $this->database->rollBack();
                }
                throw $exception;
            }
        }

        $mutationCompleted = false;
        try {
            $response = $mutation();
            if (!is_array($response) || !isset($response['status'], $response['body']) || !is_array($response['body'])) {
                throw new RuntimeException('A mutation returned an invalid broker response.');
            }
            $responseJson = json_encode($response['body'], JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_PRESERVE_ZERO_FRACTION | JSON_THROW_ON_ERROR);
            $mutationCompleted = true;
            try {
                $this->database->beginTransaction();
                $this->database->prepare(
                    'UPDATE ' . $this->tableName . ' SET status = ?, response_json = ?
                     WHERE account_id = ? AND method = ? AND route = ? AND idempotency_key = ?')
                    ->execute([(int)$response['status'], $responseJson, $accountId, $method, $route, $key]);
                if ($this->beforeFinalizationCommit !== null) {
                    ($this->beforeFinalizationCommit)();
                }
                $this->database->commit();
                return $response;
            } catch (Throwable $exception) {
                if ($this->database->inTransaction()) $this->database->rollBack();
                // Never delete a reservation after the mutation may have committed.
                // A retry observes the pending row and can be recovered explicitly.
                throw $exception;
            }
        } catch (Throwable $exception) {
            if ($this->database->inTransaction()) $this->database->rollBack();
            if (!$mutationCompleted) {
                $this->database->prepare(
                    'DELETE FROM ' . $this->tableName . ' WHERE account_id = ? AND method = ? AND route = ? AND idempotency_key = ?')
                    ->execute([$accountId, $method, $route, $key]);
            }
            throw $exception;
        }
    }

    private function validateKey(string $key): void
    {
        if (preg_match('/^[A-Za-z0-9][A-Za-z0-9._~:-]{0,127}$/D', $key) !== 1) {
            throw new BrokerHttpException(400, 'invalid_idempotency_key', 'The Idempotency-Key must be 1-128 ASCII characters using letters, numbers, dot, underscore, tilde, colon, or hyphen.');
        }
    }

    private function prune(): void
    {
        $this->database->prepare('DELETE FROM ' . $this->tableName . ' WHERE expires_at <= ?')->execute([time()]);
    }
}
