<?php

declare(strict_types=1);

final class CharacterAuthService
{
    private const SESSION_KEY = 'character_auth';
    private const LEGACY_ALGORITHM = 'PBKDF2-HMAC-SHA256';
    private const LEGACY_MINIMUM_ITERATIONS = 600000;
    private const LEGACY_HASH_BYTES = 32;
    private const LEGACY_MINIMUM_SALT_BYTES = 16;
    private const PRESENCE_WINDOW_SECONDS = 120;


    private array $authConfig;
    private $clock;


    public function __construct(
        private readonly PDO $database,
        array $authConfig,
        ?callable $clock = null)
    {
        $this->clock = $clock ?? static fn(): int => time();
        $this->authConfig = array_replace([
            'expected_origin' => 'https://bryanmiller.us',
            'idle_timeout_seconds' => 1800,
            'absolute_timeout_seconds' => 28800,
            'login_window_seconds' => 900,
            'login_max_failures' => 5,
            'login_progressive_delay_base_seconds' => 2,
            'login_progressive_delay_max_seconds' => 300,
            'login_address_max_failures' => 20,
            'login_address_delay_seconds' => 300,
            'login_lockout_seconds' => 900,
            'audit_retention_seconds' => 90 * 86400,
            'audit_address_mode' => 'hash',
            'audit_address_hash_key' => '',
        ], $authConfig);
        $this->validateConfiguration();
    }

    public function accountCount(): int
    {
        return (int)$this->database->query('SELECT COUNT(*) FROM character_accounts')->fetchColumn();
    }

    public function login(
        array $body,
        string $remoteAddress,
        string $origin,
        array &$session,
        ?callable $regenerateSession = null): array
    {
        $this->requireExpectedOrigin($origin);
        $remoteAddress = $this->normalizeRemoteAddress($remoteAddress);
        $displayName = $this->validateDisplayName((string)($body['character_name'] ?? ''));
        $normalizedName = $this->resolveLoginNameAlias($this->normalizeName($displayName));
        $password = (string)($body['password'] ?? '');
        if ($password === '' || strlen($password) > 4096) {
            $this->rejectLogin($normalizedName, $remoteAddress, null, $password);
        }

        if ($this->isLoginBlocked($normalizedName, $remoteAddress)) {
            $this->performDummyPasswordVerification($password);
            throw new BrokerHttpException(
                429,
                'login_failed',
                'The character name or password did not match. Please wait before trying again.');
        }

        $statement = $this->database->prepare(
            'SELECT * FROM character_accounts WHERE normalized_name = ? LIMIT 1');
        $statement->execute([$normalizedName]);
        $account = $statement->fetch();
        $passwordValid = is_array($account)
            ? $this->verifyPassword($password, $account)
            : $this->performDummyPasswordVerification($password);

        if (!is_array($account) || !$passwordValid || (int)$account['enabled'] !== 1) {
            $this->rejectLogin(
                $normalizedName,
                $remoteAddress,
                is_array($account) ? (string)$account['id'] : null,
                $password,
                $passwordValid || !is_array($account));
        }

        $this->upgradePasswordHashIfNeeded($password, $account);
        $account = $this->loadEnabledAccount((string)$account['id']);
        $this->clearLoginFailures($normalizedName, $remoteAddress);
        if ($regenerateSession !== null) {
            $regenerateSession();
        }

        $now = time();
        $session[self::SESSION_KEY] = [
            'account_id' => (string)$account['id'],
            'presence_id' => bin2hex(random_bytes(16)),
            'issued_at' => $now,
            'last_seen_at' => $now,
            'absolute_expires_at' => $now + (int)$this->authConfig['absolute_timeout_seconds'],
            'csrf_token' => $this->base64UrlEncode(random_bytes(32)),
            'session_version' => (int)$account['session_version'],
        ];
        $this->recordPresence($session[self::SESSION_KEY]);
        $this->database->prepare(
            'UPDATE character_accounts SET last_login_at = ? WHERE id = ?')
            ->execute([$now, $account['id']]);
        $this->recordAuthAudit((string)$account['id'], $remoteAddress, 'login_success');

        $freshAccount = $this->loadEnabledAccount((string)$account['id']);
        return $this->sessionResponse($freshAccount, $session[self::SESSION_KEY]);
    }

    public function currentSession(array &$session): array
    {
        $resolved = $this->resolveSession($session, false);
        if ($resolved === null) {
            return ['authenticated' => false];
        }

        return $this->sessionResponse($resolved['account'], $resolved['session']);
    }

    public function requireCurrentAccount(array &$session): array
    {
        $resolved = $this->resolveSession($session, true);
        return [
            'authenticated' => true,
            'account' => $this->publicAccount($resolved['account']),
        ];
    }

    public function requireMutationAccount(array $headers, array &$session): array
    {
        $this->requireExpectedOrigin((string)($headers['origin'] ?? ''));
        $resolved = $this->resolveSession($session, true);
        $providedToken = (string)($headers['csrf-token'] ?? '');
        $expectedToken = (string)($resolved['session']['csrf_token'] ?? '');
        if ($providedToken === ''
            || $expectedToken === ''
            || !hash_equals($expectedToken, $providedToken)) {
            throw new BrokerHttpException(
                403,
                'csrf_rejected',
                'The request could not be authorized.');
        }
        return [
            'authenticated' => true,
            'account' => $this->publicAccount($resolved['account']),
        ];
    }

    public function presence(array &$session): array
    {
        $resolved = $this->resolveSession($session, true);
        $account = $resolved['account'];
        $response = [
            'schema_version' => 2,
            'scope' => (string)$account['role'] === 'dm' ? 'party' : 'self',
            'observed_at' => gmdate(DATE_ATOM),
            'active_window_seconds' => self::PRESENCE_WINDOW_SECONDS,
            'users' => [],
        ];
        if ((string)$account['role'] !== 'dm') {
            return $response;
        }

        $now = time();
        $cutoff = $now - self::PRESENCE_WINDOW_SECONDS;
        $this->database->prepare(
            'DELETE FROM character_session_presence
             WHERE last_seen_at < ? OR absolute_expires_at <= ?')
            ->execute([$cutoff, $now]);
        $statement = $this->database->prepare(
            'SELECT
                accounts.id,
                accounts.display_name,
                accounts.role,
                accounts.last_login_at,
                MAX(presence.last_seen_at) AS active_last_seen_at
             FROM character_accounts AS accounts
             LEFT JOIN character_session_presence AS presence
               ON accounts.id = presence.account_id
              AND presence.last_seen_at >= ?
              AND presence.absolute_expires_at > ?
             WHERE accounts.id <> ?
               AND accounts.enabled = 1
             GROUP BY accounts.id, accounts.display_name, accounts.role, accounts.last_login_at
             ORDER BY (MAX(presence.last_seen_at) IS NOT NULL) DESC, accounts.normalized_name');
        $statement->execute([$cutoff, $now, $account['id']]);
        $response['users'] = array_map(
            static fn(array $row): array => [
                'account_id' => (string)$row['id'],
                'character_name' => (string)$row['display_name'],
                'role' => (string)$row['role'],
                'active' => $row['active_last_seen_at'] !== null,
                'last_seen_at' => $row['active_last_seen_at'] === null
                    ? null
                    : gmdate(DATE_ATOM, (int)$row['active_last_seen_at']),
                'last_login_at' => $row['last_login_at'] === null
                    ? null
                    : gmdate(DATE_ATOM, (int)$row['last_login_at']),
            ],
            $statement->fetchAll());
        return $response;
    }

    public function logout(
        array $headers,
        string $remoteAddress,
        array &$session,
        ?callable $destroySession = null): array
    {
        $this->requireExpectedOrigin((string)($headers['origin'] ?? ''));
        $resolved = $this->resolveSession($session, true);
        $providedToken = (string)($headers['csrf-token'] ?? '');
        $expectedToken = (string)($resolved['session']['csrf_token'] ?? '');
        if ($providedToken === '' || $expectedToken === '' || !hash_equals($expectedToken, $providedToken)) {
            throw new BrokerHttpException(403, 'csrf_rejected', 'The request could not be authorized.');
        }

        $this->recordAuthAudit(
            (string)$resolved['account']['id'],
            $remoteAddress,
            'logout');
        $this->removePresence((string)($resolved['session']['presence_id'] ?? ''));
        $session = [];
        if ($destroySession !== null) {
            $destroySession();
        }
        return ['authenticated' => false];
    }

    public function importLegacyAccounts(array $document): array
    {
        if ((int)($document['schema_version'] ?? 0) !== 2
            || (string)($document['format'] ?? '') !== 'xp-password-hashes-v2'
            || !is_array($document['entries'] ?? null)
            || count($document['entries']) === 0) {
            throw new BrokerHttpException(
                400,
                'invalid_password_import',
                'The password import document is invalid.');
        }

        $records = [];
        $names = [];
        $canonicalIds = [];
        $aliases = [];
        foreach ($document['entries'] as $entry) {
            if (!is_array($entry)) {
                throw new BrokerHttpException(400, 'invalid_password_import', 'A password entry is invalid.');
            }
            $canonicalName = $this->validateDisplayName((string)($entry['canonical_name'] ?? ''));
            $normalizedName = $this->normalizeName($canonicalName);
            if (isset($names[$normalizedName])) {
                throw new BrokerHttpException(400, 'invalid_password_import', 'The password import contains duplicate canonical names.');
            }
            $names[$normalizedName] = true;
        }
        $salts = [];
        foreach ($document['entries'] as $entry) {
            $displayName = $this->validateDisplayName((string)($entry['canonical_name'] ?? ''));
            $normalizedName = $this->normalizeName($displayName);
            $canonicalId = $this->validateCharacterKey((string)($entry['canonical_id'] ?? ''));
            if (isset($canonicalIds[$canonicalId])) {
                throw new BrokerHttpException(400, 'invalid_password_import', 'The password import contains duplicate canonical IDs.');
            }
            $canonicalIds[$canonicalId] = true;
            if (!is_array($entry['aliases'] ?? null)) {
                throw new BrokerHttpException(400, 'invalid_password_import', 'A password entry must declare an aliases array.');
            }
            $entryAliases = [];
            foreach ($entry['aliases'] as $alias) {
                $displayAlias = $this->validateDisplayName((string)$alias);
                $normalizedAlias = $this->normalizeName($displayAlias);
                if (isset($names[$normalizedAlias]) || isset($aliases[$normalizedAlias])) {
                    throw new BrokerHttpException(400, 'invalid_password_import', 'The password import contains colliding aliases.');
                }
                $aliases[$normalizedAlias] = true;
                $entryAliases[] = [$normalizedAlias, $displayAlias];
            }
            $iterations = filter_var(
                $entry['iterations'] ?? null,
                FILTER_VALIDATE_INT,
                ['options' => ['min_range' => self::LEGACY_MINIMUM_ITERATIONS, 'max_range' => 5000000]]);
            $salt = $this->decodeLegacyValue((string)($entry['salt'] ?? ''), self::LEGACY_MINIMUM_SALT_BYTES, null);
            $hash = $this->decodeLegacyValue((string)($entry['hash'] ?? ''), self::LEGACY_HASH_BYTES, self::LEGACY_HASH_BYTES);
            if ((string)($entry['algorithm'] ?? '') !== self::LEGACY_ALGORITHM || $iterations === false) {
                throw new BrokerHttpException(400, 'invalid_password_import', 'A password entry uses an unsupported algorithm.');
            }
            $saltKey = base64_encode($salt);
            if (isset($salts[$saltKey])) {
                throw new BrokerHttpException(400, 'invalid_password_import', 'The password import reuses a salt.');
            }
            $salts[$saltKey] = true;
            $records[] = [
                'id' => bin2hex(random_bytes(16)),
                'normalized_name' => $normalizedName,
                'display_name' => $displayName,
                'character_key' => $canonicalId,
                'role' => array_key_exists('role', $entry)
                    ? $this->validateRole((string)$entry['role'])
                    : null,
                'aliases' => $entryAliases,
                'iterations' => (int)$iterations,
                'salt' => base64_encode($salt),
                'hash' => base64_encode($hash),
            ];
        }

        $this->database->beginTransaction();
        try {
            $now = time();
            foreach ($records as $record) {
                $accountIdStatement = $this->database->prepare(
                    'SELECT * FROM character_accounts WHERE character_key = ?');
                $accountIdStatement->execute([$record['character_key']]);
                $accountMatches = $accountIdStatement->fetchAll();
                if (count($accountMatches) > 1) {
                    throw new RuntimeException('The existing character key is ambiguous.');
                }
                $existingAccount = $accountMatches[0] ?? null;
                $accountId = is_array($existingAccount)
                    ? (string)$existingAccount['id']
                    : $record['id'];
                $this->assertIdentityNamespaceAvailable(
                    $record['normalized_name'],
                    $record['aliases'],
                    is_array($existingAccount) ? $accountId : null);
                if (is_array($existingAccount)) {
                    $this->database->prepare(
                        'UPDATE character_accounts SET
                            normalized_name = ?, display_name = ?, role = ?, enabled = 1,
                            password_hash = NULL, legacy_algorithm = ?, legacy_iterations = ?,
                            legacy_salt = ?, legacy_hash = ?, password_changed_at = ?,
                            session_version = session_version + 1
                         WHERE id = ?')->execute([
                            $record['normalized_name'],
                            $record['display_name'],
                            $record['role'] ?? (string)$existingAccount['role'],
                            self::LEGACY_ALGORITHM,
                            $record['iterations'],
                            $record['salt'],
                            $record['hash'],
                            $now,
                            $accountId,
                        ]);
                } else {
                    if ($record['role'] === null) {
                        throw new RuntimeException('A new imported account must declare its role.');
                    }
                    $this->database->prepare(
                        'INSERT INTO character_accounts (
                            id, normalized_name, display_name, character_key, role, enabled,
                            password_hash, legacy_algorithm, legacy_iterations, legacy_salt, legacy_hash,
                            created_at, password_changed_at
                         ) VALUES (?, ?, ?, ?, ?, 1, NULL, ?, ?, ?, ?, ?, ?)')->execute([
                            $accountId,
                            $record['normalized_name'],
                            $record['display_name'],
                            $record['character_key'],
                            $record['role'],
                            self::LEGACY_ALGORITHM,
                            $record['iterations'],
                            $record['salt'],
                            $record['hash'],
                            $now,
                            $now,
                        ]);
                }
                $this->database->prepare(
                    'DELETE FROM character_account_aliases WHERE account_id = ?')->execute([$accountId]);
                foreach ($record['aliases'] as [$normalizedAlias, $displayAlias]) {
                    $this->database->prepare(
                        'INSERT INTO character_account_aliases
                            (account_id, normalized_alias, display_alias, created_at)
                         VALUES (?, ?, ?, ?)')->execute([
                            $accountId,
                            $normalizedAlias,
                            $displayAlias,
                            $now,
                        ]);
                }
            }
            $this->database->commit();
        } catch (Throwable $exception) {
            $this->database->rollBack();
            throw new BrokerHttpException(
                409,
                'account_import_conflict',
                'The character accounts could not be imported.',
                $exception);
        }

        return ['imported' => count($records)];
    }

    public function createAccount(array $body): array
    {
        $displayName = $this->validateDisplayName((string)($body['character_name'] ?? ''));
        $normalizedName = $this->normalizeName($displayName);
        $password = $this->validateNewPassword((string)($body['password'] ?? ''));
        $role = $this->validateRole((string)($body['role'] ?? 'player'));
        $characterKey = $this->validateCharacterKey((string)($body['character_key'] ?? ''));
        $aliases = $this->validateExplicitAliases($body['aliases'] ?? [], $normalizedName);
        $now = time();
        $id = bin2hex(random_bytes(16));

        $this->database->beginTransaction();
        try {
            $this->assertIdentityNamespaceAvailable($normalizedName, $aliases, null);
            $statement = $this->database->prepare(
                'INSERT INTO character_accounts (
                    id, normalized_name, display_name, character_key, role, enabled,
                    password_hash, created_at, password_changed_at
                 ) VALUES (?, ?, ?, ?, ?, 1, ?, ?, ?)');
            $statement->execute([
                $id,
                $normalizedName,
                $displayName,
                $characterKey,
                $role,
                password_hash($password, PASSWORD_DEFAULT),
                $now,
                $now,
            ]);
            foreach ($aliases as [$normalizedAlias, $displayAlias]) {
                $this->database->prepare(
                    'INSERT INTO character_account_aliases
                        (account_id, normalized_alias, display_alias, created_at)
                     VALUES (?, ?, ?, ?)')->execute([
                        $id,
                        $normalizedAlias,
                        $displayAlias,
                        $now,
                    ]);
            }
            $this->database->commit();
        } catch (Throwable $exception) {
            if ($this->database->inTransaction()) {
                $this->database->rollBack();
            }
            if ($exception instanceof BrokerHttpException) {
                throw $exception;
            }
            throw new BrokerHttpException(
                409,
                'account_conflict',
                'The character account conflicts with an existing account.',
                $exception);
        }

        return $this->publicAccount($this->loadAccount($id));
    }

    public function updateAccount(string $accountId, array $body): array
    {
        $account = $this->loadAccount($accountId);
        $updates = [];
        $parameters = [];

        if (array_key_exists('character_name', $body)) {
            $displayName = $this->validateDisplayName((string)$body['character_name']);
            $normalizedName = $this->normalizeName($displayName);
            $this->assertIdentityNamespaceAvailable($normalizedName, [], (string)$account['id']);
            $updates[] = 'display_name = ?';
            $parameters[] = $displayName;
            $updates[] = 'normalized_name = ?';
            $parameters[] = $normalizedName;
        }
        if (array_key_exists('character_key', $body)) {
            $updates[] = 'character_key = ?';
            $parameters[] = $this->validateCharacterKey((string)$body['character_key']);
        }
        if (array_key_exists('role', $body)) {
            $updates[] = 'role = ?';
            $parameters[] = $this->validateRole((string)$body['role']);
        }
        if (array_key_exists('enabled', $body)) {
            if (!is_bool($body['enabled'])) {
                throw new BrokerHttpException(400, 'invalid_account', 'The enabled value must be true or false.');
            }
            $updates[] = 'enabled = ?';
            $parameters[] = $body['enabled'] ? 1 : 0;
        }
        if (array_key_exists('password', $body)) {
            $password = $this->validateNewPassword((string)$body['password']);
            $updates[] = 'password_hash = ?';
            $parameters[] = password_hash($password, PASSWORD_DEFAULT);
            $updates[] = 'legacy_algorithm = NULL';
            $updates[] = 'legacy_iterations = NULL';
            $updates[] = 'legacy_salt = NULL';
            $updates[] = 'legacy_hash = NULL';
            $updates[] = 'password_changed_at = ?';
            $parameters[] = time();
        }
        if ($updates === []) {
            throw new BrokerHttpException(400, 'invalid_account', 'No supported account changes were supplied.');
        }

        $updates[] = 'session_version = session_version + 1';
        $parameters[] = $account['id'];
        try {
            $statement = $this->database->prepare(
                'UPDATE character_accounts SET ' . implode(', ', $updates) . ' WHERE id = ?');
            $statement->execute($parameters);
        } catch (PDOException $exception) {
            throw new BrokerHttpException(
                409,
                'account_conflict',
                'The character account conflicts with an existing account.',
                $exception);
        }

        return $this->publicAccount($this->loadAccount($accountId));
    }

    public function listAccounts(): array
    {
        $accounts = $this->database->query(
            'SELECT * FROM character_accounts ORDER BY normalized_name')->fetchAll();
        return array_map(fn(array $account): array => $this->publicAccount($account), $accounts);
    }

    private function resolveSession(array &$session, bool $required): ?array
    {
        $payload = $session[self::SESSION_KEY] ?? null;
        if (!is_array($payload)) {
            return $this->missingSession($required);
        }

        $now = time();
        $lastSeenAt = (int)($payload['last_seen_at'] ?? 0);
        $absoluteExpiresAt = (int)($payload['absolute_expires_at'] ?? 0);
        $accountId = (string)($payload['account_id'] ?? '');
        if (preg_match('/^[a-f0-9]{32}$/', $accountId) !== 1
            || $lastSeenAt <= 0
            || $absoluteExpiresAt <= $now
            || $now - $lastSeenAt > (int)$this->authConfig['idle_timeout_seconds']) {
            $this->removePresence((string)($payload['presence_id'] ?? ''));
            unset($session[self::SESSION_KEY]);
            return $this->missingSession($required);
        }

        try {
            $account = $this->loadEnabledAccount($accountId);
        } catch (BrokerHttpException) {
            $this->removePresence((string)($payload['presence_id'] ?? ''));
            unset($session[self::SESSION_KEY]);
            return $this->missingSession($required);
        }
        if ((int)($payload['session_version'] ?? 0) !== (int)$account['session_version']) {
            $this->removePresence((string)($payload['presence_id'] ?? ''));
            unset($session[self::SESSION_KEY]);
            return $this->missingSession($required);
        }

        if (preg_match('/^[a-f0-9]{32}$/', (string)($payload['presence_id'] ?? '')) !== 1) {
            $payload['presence_id'] = bin2hex(random_bytes(16));
        }
        $payload['last_seen_at'] = $now;
        $session[self::SESSION_KEY] = $payload;
        $this->recordPresence($payload);
        return ['account' => $account, 'session' => $payload];
    }

    private function recordPresence(array $session): void
    {
        $presenceId = (string)($session['presence_id'] ?? '');
        $accountId = (string)($session['account_id'] ?? '');
        if (preg_match('/^[a-f0-9]{32}$/', $presenceId) !== 1
            || preg_match('/^[a-f0-9]{32}$/', $accountId) !== 1) {
            return;
        }
        $this->database->prepare(
            'INSERT INTO character_session_presence (
                presence_id, account_id, last_seen_at, absolute_expires_at
             ) VALUES (?, ?, ?, ?)
             ON CONFLICT(presence_id) DO UPDATE SET
                account_id = excluded.account_id,
                last_seen_at = excluded.last_seen_at,
                absolute_expires_at = excluded.absolute_expires_at')
            ->execute([
                $presenceId,
                $accountId,
                (int)($session['last_seen_at'] ?? time()),
                (int)($session['absolute_expires_at'] ?? time()),
            ]);
    }

    private function removePresence(string $presenceId): void
    {
        if (preg_match('/^[a-f0-9]{32}$/', $presenceId) !== 1) {
            return;
        }
        $this->database->prepare(
            'DELETE FROM character_session_presence WHERE presence_id = ?')
            ->execute([$presenceId]);
    }

    private function missingSession(bool $required): ?array
    {
        if ($required) {
            throw new BrokerHttpException(401, 'authentication_required', 'Character login is required.');
        }
        return null;
    }

    private function sessionResponse(array $account, array $session): array
    {
        return [
            'authenticated' => true,
            'account' => $this->publicAccount($account),
            'csrf_token' => (string)$session['csrf_token'],
            'resource_generation' => hash('sha256', implode('|', [
                (string)$account['id'],
                (int)$session['session_version'],
                (int)$session['issued_at'],
            ])),
            'idle_expires_at' => gmdate(
                DATE_ATOM,
                (int)$session['last_seen_at'] + (int)$this->authConfig['idle_timeout_seconds']),
            'absolute_expires_at' => gmdate(DATE_ATOM, (int)$session['absolute_expires_at']),
        ];
    }

    private function publicAccount(array $account): array
    {
        return [
            'id' => (string)$account['id'],
            'character_name' => (string)$account['display_name'],
            'character_key' => (string)$account['character_key'],
            'role' => (string)$account['role'],
            'enabled' => (int)$account['enabled'] === 1,
            'password_changed_at' => gmdate(DATE_ATOM, (int)$account['password_changed_at']),
            'last_login_at' => $account['last_login_at'] === null
                ? null
                : gmdate(DATE_ATOM, (int)$account['last_login_at']),
        ];
    }

    private function verifyPassword(string $password, array $account): bool
    {
        $passwordHash = (string)($account['password_hash'] ?? '');
        if ($passwordHash !== '') {
            return password_verify($password, $passwordHash);
        }
        if ((string)($account['legacy_algorithm'] ?? '') !== self::LEGACY_ALGORITHM) {
            return $this->performDummyPasswordVerification($password);
        }

        $salt = base64_decode((string)$account['legacy_salt'], true);
        $expectedHash = base64_decode((string)$account['legacy_hash'], true);
        $iterations = (int)$account['legacy_iterations'];
        if ($salt === false
            || $expectedHash === false
            || strlen($salt) < self::LEGACY_MINIMUM_SALT_BYTES
            || strlen($expectedHash) !== self::LEGACY_HASH_BYTES
            || $iterations < self::LEGACY_MINIMUM_ITERATIONS) {
            return false;
        }

        $candidateHash = hash_pbkdf2(
            'sha256',
            $password,
            $salt,
            $iterations,
            strlen($expectedHash),
            true);
        return hash_equals($expectedHash, $candidateHash);
    }

    private function upgradePasswordHashIfNeeded(string $password, array $account): void
    {
        $existingHash = (string)($account['password_hash'] ?? '');
        if ($existingHash !== '' && !password_needs_rehash($existingHash, PASSWORD_DEFAULT)) {
            return;
        }

        $this->database->prepare(
            'UPDATE character_accounts SET
                password_hash = ?,
                legacy_algorithm = NULL,
                legacy_iterations = NULL,
                legacy_salt = NULL,
                legacy_hash = NULL,
                password_changed_at = ?
             WHERE id = ?')
            ->execute([password_hash($password, PASSWORD_DEFAULT), time(), $account['id']]);
    }

    private function performDummyPasswordVerification(string $password): bool
    {
        static $dummyHash = null;
        if (!is_string($dummyHash)) {
            $dummyHash = password_hash(bin2hex(random_bytes(32)), PASSWORD_DEFAULT);
        }
        password_verify($password, $dummyHash);
        return false;
    }

    private function rejectLogin(
        string $normalizedName,
        string $remoteAddress,
        ?string $accountId,
        string $password,
        bool $passwordWasVerified = false): never
    {
        if (!$passwordWasVerified && $accountId === null) {
            $this->performDummyPasswordVerification($password);
        }
        $this->recordLoginFailure($normalizedName, $remoteAddress);
        $this->recordAuthAudit($accountId, $remoteAddress, 'login_failure');
        throw new BrokerHttpException(
            401,
            'login_failed',
            'The character name or password did not match.');
    }

    private function isLoginBlocked(string $normalizedName, string $remoteAddress): bool
    {
        $statement = $this->database->prepare(
            'SELECT blocked_until FROM auth_rate_limits WHERE scope_hash IN (?, ?)');
        $statement->execute($this->loginScopeHashes($normalizedName, $remoteAddress));
        $now = $this->now();
        foreach ($statement->fetchAll() as $row) {
            if ((int)$row['blocked_until'] > $now) {
                return true;
            }
        }
        return false;
    }

    private function recordLoginFailure(string $normalizedName, string $remoteAddress): void
    {
        $now = $this->now();
        $windowSeconds = (int)$this->authConfig['login_window_seconds'];
        $this->database->beginTransaction();
        try {
            $select = $this->database->prepare(
                'SELECT window_start, failure_count, blocked_until FROM auth_rate_limits WHERE scope_hash = ?');
            $upsert = $this->database->prepare(
                'INSERT INTO auth_rate_limits (scope_hash, window_start, failure_count, blocked_until)
                 VALUES (?, ?, ?, ?)
                 ON CONFLICT(scope_hash) DO UPDATE SET
                    window_start = excluded.window_start,
                    failure_count = excluded.failure_count,
                    blocked_until = excluded.blocked_until');
            foreach ($this->loginScopeHashes($normalizedName, $remoteAddress) as $index => $scopeHash) {
                $select->execute([$scopeHash]);
                $row = $select->fetch();
                $failureCount = 1;
                $windowStart = $now;
                if (is_array($row) && $now - (int)$row['window_start'] < $windowSeconds) {
                    $windowStart = (int)$row['window_start'];
                    $failureCount = (int)$row['failure_count'] + 1;
                }
                $blockedUntil = 0;
                if ($index === 0) {
                    $threshold = (int)$this->authConfig['login_max_failures'];
                    if ($failureCount >= $threshold) {
                        $exponent = $failureCount - $threshold;
                        $delay = min(
                            (int)$this->authConfig['login_progressive_delay_max_seconds'],
                            (int)$this->authConfig['login_progressive_delay_base_seconds'] * (2 ** $exponent));
                        $blockedUntil = $now + $delay;
                    }
                } else {
                    $threshold = (int)$this->authConfig['login_address_max_failures'];
                    if ($failureCount >= $threshold) {
                        $blockedUntil = $now + (int)$this->authConfig['login_address_delay_seconds'];
                    }
                }
                $upsert->execute([$scopeHash, $windowStart, $failureCount, $blockedUntil]);
            }
            $this->database->prepare('DELETE FROM auth_rate_limits WHERE window_start < ? AND blocked_until < ?')
                ->execute([$now - (2 * $windowSeconds), $now]);
            $this->database->commit();
        } catch (Throwable $exception) {
            $this->database->rollBack();
            throw $exception;
        }
    }

    private function clearLoginFailures(string $normalizedName, string $remoteAddress): void
    {
        $scopeHashes = $this->loginScopeHashes($normalizedName, $remoteAddress);
        $statement = $this->database->prepare(
            'DELETE FROM auth_rate_limits WHERE scope_hash = ?');
        $statement->execute([$scopeHashes[0]]);
    }

    private function loginScopeHashes(string $normalizedName, string $remoteAddress): array
    {
        $addressScope = bin2hex(inet_pton($this->normalizeRemoteAddress($remoteAddress)));
        return [
            hash('sha256', 'account-source:' . $normalizedName . "\0" . $addressScope),
            hash('sha256', 'address:' . $addressScope),
        ];
    }

    private function normalizeRemoteAddress(string $remoteAddress): string
    {
        $remoteAddress = trim($remoteAddress);
        $packedAddress = inet_pton($remoteAddress);
        if ($packedAddress === false) {
            throw new BrokerHttpException(
                400,
                'login_failed',
                'The character name or password did not match.');
        }
        return inet_ntop($packedAddress);
    }

    private function recordAuthAudit(?string $accountId, string $remoteAddress, string $event): void
    {
        $address = $this->normalizeRemoteAddress($remoteAddress);
        if ($this->authConfig['audit_address_mode'] === 'hash') {
            $hashKey = (string)$this->authConfig['audit_address_hash_key'];
            if ($hashKey === '') {
                $hashKey = hash('sha256', (string)$this->authConfig['expected_origin']);
            }
            $address = hash_hmac('sha256', $address, $hashKey);
        }
        $this->database->prepare(
            'INSERT INTO auth_audit_events (account_id, occurred_at, remote_address, event)
             VALUES (?, ?, ?, ?)')
            ->execute([$accountId, time(), $address, $event]);
        $this->database->prepare('DELETE FROM auth_audit_events WHERE occurred_at < ?')
            ->execute([time() - $this->authConfig['audit_retention_seconds']]);
    }

    private function loadAccount(string $accountId): array
    {
        if (preg_match('/^[a-f0-9]{32}$/', $accountId) !== 1) {
            throw new BrokerHttpException(404, 'account_not_found', 'The character account was not found.');
        }
        $statement = $this->database->prepare(
            'SELECT * FROM character_accounts WHERE id = ? LIMIT 1');
        $statement->execute([$accountId]);
        $account = $statement->fetch();
        if (!is_array($account)) {
            throw new BrokerHttpException(404, 'account_not_found', 'The character account was not found.');
        }
        return $account;
    }

    private function loadEnabledAccount(string $accountId): array
    {
        $account = $this->loadAccount($accountId);
        if ((int)$account['enabled'] !== 1) {
            throw new BrokerHttpException(401, 'authentication_required', 'Character login is required.');
        }
        return $account;
    }

    private function validateDisplayName(string $value): string
    {
        $value = preg_replace('/\s+/u', ' ', trim($value));
        if (!is_string($value)
            || $value === ''
            || strlen($value) > 100
            || preg_match('//u', $value) !== 1
            || preg_match('/[\x00-\x1F\x7F]/u', $value) === 1) {
            throw new BrokerHttpException(
                400,
                'invalid_credentials',
                'The character name or password did not match.');
        }
        return $value;
    }

    private function now(): int
    {
        return (int)($this->clock)();
    }

    private function normalizeName(string $value): string
    {
        return function_exists('mb_strtolower')
            ? mb_strtolower($value, 'UTF-8')
            : strtolower($value);
    }

    private function resolveLoginNameAlias(string $normalizedName): string
    {
        $canonicalStatement = $this->database->prepare(
            'SELECT normalized_name FROM character_accounts WHERE normalized_name = ? LIMIT 1');
        $canonicalStatement->execute([$normalizedName]);
        if ($canonicalStatement->fetchColumn() !== false) {
            return $normalizedName;
        }
        $statement = $this->database->prepare(
            'SELECT accounts.normalized_name
               FROM character_account_aliases AS aliases
               JOIN character_accounts AS accounts ON accounts.id = aliases.account_id
              WHERE aliases.normalized_alias = ?
              LIMIT 1');
        $statement->execute([$normalizedName]);
        $canonicalName = $statement->fetchColumn();
        return is_string($canonicalName) && $canonicalName !== ''
            ? $canonicalName
            : $normalizedName;
    }

    private function validateExplicitAliases(mixed $value, string $normalizedName): array
    {
        if (!is_array($value)) {
            throw new BrokerHttpException(400, 'invalid_account', 'Account aliases must be an array.');
        }
        $aliases = [];
        $seen = [];
        foreach ($value as $alias) {
            $displayAlias = $this->validateDisplayName((string)$alias);
            $normalizedAlias = $this->normalizeName($displayAlias);
            if ($normalizedAlias === $normalizedName || isset($seen[$normalizedAlias])) {
                throw new BrokerHttpException(400, 'invalid_account', 'Account aliases must be distinct from the canonical name and each other.');
            }
            $seen[$normalizedAlias] = true;
            $aliases[] = [$normalizedAlias, $displayAlias];
        }
        return $aliases;
    }

    private function assertIdentityNamespaceAvailable(
        string $normalizedName,
        array $aliases,
        ?string $accountId): void
    {
        $canonicalAliasStatement = $this->database->prepare(
            $accountId === null
                ? 'SELECT 1 FROM character_account_aliases WHERE normalized_alias = ? LIMIT 1'
                : 'SELECT 1 FROM character_account_aliases
                    WHERE normalized_alias = ? AND account_id <> ? LIMIT 1');
        $canonicalAliasStatement->execute(
            $accountId === null ? [$normalizedName] : [$normalizedName, $accountId]);
        if ($canonicalAliasStatement->fetchColumn() !== false) {
            throw new BrokerHttpException(
                409,
                'account_conflict',
                'A canonical name collides with an existing account alias.');
        }

        $aliasNameStatement = $this->database->prepare(
            $accountId === null
                ? 'SELECT 1 FROM character_accounts WHERE normalized_name = ? LIMIT 1'
                : 'SELECT 1 FROM character_accounts
                    WHERE normalized_name = ? AND id <> ? LIMIT 1');
        foreach ($aliases as [$normalizedAlias]) {
            $aliasNameStatement->execute(
                $accountId === null ? [$normalizedAlias] : [$normalizedAlias, $accountId]);
            if ($aliasNameStatement->fetchColumn() !== false) {
                throw new BrokerHttpException(
                    409,
                    'account_conflict',
                    'An account alias collides with an existing canonical name.');
            }
        }
    }

    private function validateNewPassword(string $password): string
    {
        if ($password === '' || strlen($password) > 4096) {
            throw new BrokerHttpException(400, 'invalid_account', 'A non-empty password is required.');
        }
        return $password;
    }

    private function validateRole(string $role): string
    {
        if (!in_array($role, ['player', 'dm'], true)) {
            throw new BrokerHttpException(400, 'invalid_account', 'The account role is invalid.');
        }
        return $role;
    }

    private function validateCharacterKey(string $characterKey): string
    {
        $characterKey = trim($characterKey);
        if (preg_match('/^[A-Za-z0-9][A-Za-z0-9._:-]{0,99}$/', $characterKey) !== 1) {
            throw new BrokerHttpException(400, 'invalid_account', 'The character key is invalid.');
        }
        return strtolower($characterKey);
    }


    private function decodeLegacyValue(string $value, int $minimumBytes, ?int $exactBytes): string
    {
        $decoded = base64_decode($value, true);
        if ($decoded === false
            || strlen($decoded) < $minimumBytes
            || ($exactBytes !== null && strlen($decoded) !== $exactBytes)) {
            throw new BrokerHttpException(400, 'invalid_password_import', 'A password hash value is invalid.');
        }
        return $decoded;
    }

    private function requireExpectedOrigin(string $origin): void
    {
        $expectedOrigin = rtrim((string)$this->authConfig['expected_origin'], '/');
        if ($origin === '' || !hash_equals($expectedOrigin, rtrim($origin, '/'))) {
            throw new BrokerHttpException(403, 'origin_rejected', 'The request origin is not authorized.');
        }
    }

    private function validateConfiguration(): void
    {
        if (!filter_var($this->authConfig['expected_origin'], FILTER_VALIDATE_URL)
            || !str_starts_with((string)$this->authConfig['expected_origin'], 'https://')) {
            throw new RuntimeException('Character authentication requires an HTTPS expected origin.');
        }
        foreach ([
            'idle_timeout_seconds',
            'absolute_timeout_seconds',
            'login_window_seconds',
            'login_max_failures',
            'login_progressive_delay_base_seconds',
            'login_progressive_delay_max_seconds',
            'login_address_max_failures',
            'login_address_delay_seconds',
            'login_lockout_seconds',
            'audit_retention_seconds',
        ] as $key) {
            if (!is_int($this->authConfig[$key]) || $this->authConfig[$key] <= 0) {
                throw new RuntimeException("Character authentication setting '$key' must be a positive integer.");
            }
        }
        if ($this->authConfig['absolute_timeout_seconds'] <= $this->authConfig['idle_timeout_seconds']) {
            throw new RuntimeException('The absolute session timeout must exceed the idle timeout.');
        }
        if (!in_array($this->authConfig['audit_address_mode'], ['hash', 'raw'], true)) {
            throw new RuntimeException("Character authentication setting 'audit_address_mode' must be hash or raw.");
        }
    }

    private function base64UrlEncode(string $bytes): string
    {
        return rtrim(strtr(base64_encode($bytes), '+/', '-_'), '=');
    }
}
