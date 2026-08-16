<?php

declare(strict_types=1);

final class DatabaseMigrationService
{
    public const LATEST_VERSION = 4;

    public function __construct(
        private readonly PDO $database,
        private readonly string $backupDirectory)
    {
        $this->database->exec('PRAGMA foreign_keys = ON');
        $this->database->exec('PRAGMA busy_timeout = 5000');
    }

    public function migrate(): array
    {
        $currentVersion = (int)$this->database->query('PRAGMA user_version')->fetchColumn();
        if ($currentVersion < 0 || $currentVersion > self::LATEST_VERSION) {
            throw new RuntimeException('The broker database schema version is unsupported.');
        }
        $applied = [];
        for ($version = $currentVersion + 1; $version <= self::LATEST_VERSION; $version++) {
            $this->createPreMigrationBackup($currentVersion, $version);
            $this->database->beginTransaction();
            try {
                $this->applyMigration($version);
                $this->database->exec('PRAGMA user_version = ' . $version);
                $this->database->commit();
                $currentVersion = $version;
                $applied[] = $version;
            } catch (Throwable $exception) {
                if ($this->database->inTransaction()) {
                    $this->database->rollBack();
                }
                throw $exception;
            }
        }

        return [
            'from_version' => $currentVersion - count($applied),
            'to_version' => $currentVersion,
            'applied_versions' => $applied,
        ];
    }

    private function createPreMigrationBackup(int $currentVersion, int $targetVersion): void
    {
        if (!is_dir($this->backupDirectory)
            && !mkdir($this->backupDirectory, 0700, true)
            && !is_dir($this->backupDirectory)) {
            throw new RuntimeException('Unable to create the migration backup directory.');
        }
        $path = $this->backupDirectory . '/broker-migration-v' . $currentVersion
            . '-to-v' . $targetVersion . '-' . gmdate('Ymd\THis\Z')
            . '-' . bin2hex(random_bytes(4)) . '.sqlite';
        $temporary = $path . '.tmp';
        $this->database->exec('VACUUM INTO ' . $this->database->quote($temporary));
        if (!is_file($temporary) || filesize($temporary) === 0 || !rename($temporary, $path)) {
            throw new RuntimeException('The pre-migration backup could not be promoted.');
        }
        chmod($path, 0600);
    }

    private function applyMigration(int $version): void
    {
        match ($version) {
            1 => $this->migrationOne(),
            2 => $this->migrationTwo(),
            3 => $this->migrationThree(),
            4 => $this->migrationFour(),
            default => throw new RuntimeException("Unknown broker migration version: $version"),
        };
    }

    private function migrationOne(): void
    {
        $this->database->exec(
            'CREATE TABLE IF NOT EXISTS api_tokens (
                id TEXT PRIMARY KEY,
                label TEXT NOT NULL,
                token_hash TEXT NOT NULL UNIQUE,
                created_at INTEGER NOT NULL,
                expires_at INTEGER NOT NULL,
                revoked_at INTEGER NULL,
                last_used_at INTEGER NULL
            );
            CREATE TABLE IF NOT EXISTS rate_limits (
                token_id TEXT NOT NULL,
                window_start INTEGER NOT NULL,
                request_count INTEGER NOT NULL,
                PRIMARY KEY (token_id, window_start),
                FOREIGN KEY (token_id) REFERENCES api_tokens(id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS admin_request_nonces (
                nonce TEXT PRIMARY KEY,
                used_at INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS audit_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                token_id TEXT NOT NULL,
                occurred_at INTEGER NOT NULL,
                remote_address TEXT NOT NULL,
                target_path TEXT NOT NULL,
                outcome TEXT NOT NULL,
                FOREIGN KEY (token_id) REFERENCES api_tokens(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_audit_events_token_time
                ON audit_events(token_id, occurred_at);
            CREATE TABLE IF NOT EXISTS character_accounts (
                id TEXT PRIMARY KEY,
                normalized_name TEXT NOT NULL UNIQUE,
                display_name TEXT NOT NULL,
                character_key TEXT NOT NULL,
                role TEXT NOT NULL CHECK(role IN (\'player\', \'dm\')),
                enabled INTEGER NOT NULL CHECK(enabled IN (0, 1)),
                password_hash TEXT NULL,
                legacy_algorithm TEXT NULL,
                legacy_iterations INTEGER NULL,
                legacy_salt TEXT NULL,
                legacy_hash TEXT NULL,
                created_at INTEGER NOT NULL,
                password_changed_at INTEGER NOT NULL,
                last_login_at INTEGER NULL,
                session_version INTEGER NOT NULL DEFAULT 1,
                CHECK(password_hash IS NOT NULL OR legacy_hash IS NOT NULL)
            );
            CREATE TABLE IF NOT EXISTS auth_rate_limits (
                scope_hash TEXT PRIMARY KEY,
                window_start INTEGER NOT NULL,
                failure_count INTEGER NOT NULL,
                blocked_until INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS auth_audit_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_id TEXT NULL,
                occurred_at INTEGER NOT NULL,
                remote_address TEXT NOT NULL,
                event TEXT NOT NULL,
                FOREIGN KEY (account_id) REFERENCES character_accounts(id) ON DELETE SET NULL
            );
            CREATE TABLE IF NOT EXISTS character_session_presence (
                presence_id TEXT PRIMARY KEY,
                account_id TEXT NOT NULL,
                last_seen_at INTEGER NOT NULL,
                absolute_expires_at INTEGER NOT NULL,
                FOREIGN KEY (account_id) REFERENCES character_accounts(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_auth_audit_account_time
                ON auth_audit_events(account_id, occurred_at);
            CREATE INDEX IF NOT EXISTS ix_character_presence_activity
                ON character_session_presence(last_seen_at, absolute_expires_at);
            CREATE TABLE IF NOT EXISTS message_notifications (
                id TEXT PRIMARY KEY,
                sender_account_id TEXT NOT NULL,
                recipient_account_id TEXT NOT NULL,
                message TEXT NOT NULL,
                sent_at INTEGER NOT NULL,
                read_at INTEGER NULL,
                FOREIGN KEY (sender_account_id) REFERENCES character_accounts(id) ON DELETE CASCADE,
                FOREIGN KEY (recipient_account_id) REFERENCES character_accounts(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_message_notifications_recipient_read
                ON message_notifications(recipient_account_id, read_at, sent_at);
            CREATE TABLE IF NOT EXISTS quest_requests (
                id TEXT PRIMARY KEY,
                quest_id TEXT NOT NULL,
                requester_account_id TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN (\'pending\', \'approved\', \'denied\')),
                created_at INTEGER NOT NULL,
                decided_at INTEGER NULL,
                decided_by_account_id TEXT NULL,
                requester_acknowledged_at INTEGER NULL,
                FOREIGN KEY (requester_account_id) REFERENCES character_accounts(id) ON DELETE CASCADE,
                FOREIGN KEY (decided_by_account_id) REFERENCES character_accounts(id) ON DELETE SET NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_quest_requests_pending
                ON quest_requests(quest_id, requester_account_id) WHERE status = \'pending\';
            CREATE INDEX IF NOT EXISTS ix_quest_requests_status_time
                ON quest_requests(status, created_at);
            CREATE INDEX IF NOT EXISTS ix_quest_requests_requester_status
                ON quest_requests(requester_account_id, status);
            CREATE TABLE IF NOT EXISTS quest_state_overrides (
                quest_id TEXT PRIMARY KEY,
                base_state TEXT NOT NULL,
                state TEXT NOT NULL CHECK(state = \'active\'),
                updated_at INTEGER NOT NULL,
                updated_by_account_id TEXT NOT NULL,
                FOREIGN KEY (updated_by_account_id) REFERENCES character_accounts(id) ON DELETE RESTRICT
            );
            CREATE TABLE IF NOT EXISTS word_count_snapshots (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                schema_version INTEGER NOT NULL,
                observed_at TEXT NOT NULL,
                counting_rule_version TEXT NOT NULL,
                wiki_pages INTEGER NOT NULL,
                wiki_words INTEGER NOT NULL,
                ic_files INTEGER NOT NULL,
                ic_words INTEGER NOT NULL,
                ooc_files INTEGER NOT NULL,
                ooc_words INTEGER NOT NULL,
                uploaded_at INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS xp_tracking_cache (
                cache_key TEXT PRIMARY KEY,
                fetched_at INTEGER NOT NULL,
                payload_json TEXT NOT NULL,
                content_sha256 TEXT NOT NULL
            );');
    }

    private function migrationTwo(): void
    {
        $columns = $this->database->query('PRAGMA table_info(character_accounts)')->fetchAll();
        $columnNames = array_map(static fn(array $column): string => (string)$column['name'], $columns);
        if (!in_array('session_version', $columnNames, true)) {
            $this->database->exec(
                'ALTER TABLE character_accounts ADD COLUMN session_version INTEGER NOT NULL DEFAULT 1');
        }
        $this->database->exec(
            'CREATE TABLE IF NOT EXISTS broker_alert_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                alert_type TEXT NOT NULL,
                occurred_at INTEGER NOT NULL,
                error_code TEXT NOT NULL,
                message TEXT NOT NULL,
                alert_sent_at INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS ix_broker_alert_events_type_time
                ON broker_alert_events(alert_type, occurred_at);');
    }

    private function migrationThree(): void
    {
        $this->database->exec(
            'CREATE TABLE IF NOT EXISTS character_account_aliases (
                account_id TEXT NOT NULL,
                normalized_alias TEXT NOT NULL UNIQUE,
                display_alias TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                PRIMARY KEY (account_id, normalized_alias),
                FOREIGN KEY (account_id) REFERENCES character_accounts(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_character_account_aliases_account
                ON character_account_aliases(account_id);');
    }

    private function migrationFour(): void
    {
        $this->database->exec(
            'CREATE UNIQUE INDEX IF NOT EXISTS ux_character_accounts_character_key
                    ON character_accounts(character_key);
                 CREATE INDEX IF NOT EXISTS ix_character_account_aliases_account
                    ON character_account_aliases(account_id);
                 CREATE TRIGGER IF NOT EXISTS trg_character_accounts_alias_collision_insert
                 BEFORE INSERT ON character_accounts
                 WHEN EXISTS (SELECT 1 FROM character_account_aliases
                              WHERE normalized_alias = NEW.normalized_name)
                 BEGIN
                    SELECT RAISE(ABORT, \'normalized account name is already an alias\');
                 END;
                 CREATE TRIGGER IF NOT EXISTS trg_character_accounts_alias_collision_update
                 BEFORE UPDATE OF normalized_name ON character_accounts
                 WHEN EXISTS (SELECT 1 FROM character_account_aliases
                              WHERE normalized_alias = NEW.normalized_name
                                AND account_id <> OLD.id)
                 BEGIN
                    SELECT RAISE(ABORT, \'normalized account name is already an alias\');
                 END;
                 CREATE TRIGGER IF NOT EXISTS trg_character_account_aliases_name_collision_insert
                 BEFORE INSERT ON character_account_aliases
                 WHEN EXISTS (SELECT 1 FROM character_accounts
                              WHERE normalized_name = NEW.normalized_alias)
                 BEGIN
                    SELECT RAISE(ABORT, \'normalized alias is already an account name\');
                 END;
                 CREATE TRIGGER IF NOT EXISTS trg_character_account_aliases_name_collision_update
                 BEFORE UPDATE OF normalized_alias ON character_account_aliases
                 WHEN EXISTS (SELECT 1 FROM character_accounts
                              WHERE normalized_name = NEW.normalized_alias)
                 BEGIN
                    SELECT RAISE(ABORT, \'normalized alias is already an account name\');
                 END;');
    }
}
