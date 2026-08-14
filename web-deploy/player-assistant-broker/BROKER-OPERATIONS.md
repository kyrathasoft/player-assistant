# Broker operations

## Schema migrations

`DatabaseMigrationService.php` is the single ordered migration runner. It reads
`PRAGMA user_version`, creates a pre-migration SQLite backup with `VACUUM INTO`,
runs each migration inside a transaction, and advances `user_version` only after
the transaction commits. Migration 1 creates the existing broker schema;
migration 2 adds `character_accounts.session_version` for older databases and the
`broker_alert_events` table.

Configure `migrations.backup_directory` in the private configuration. Keep this
outside the website document root and retain it separately from routine broker
recovery backups.

## Alerts

`BrokerAlertService.php` persists alert events in `broker_alert_events` and
supports three alert classes:

- `health_failure`: health or refresh readiness failure; default threshold 1.
- `word_count_refresh_failure`: scheduled source refresh failure; default threshold 1.
- `server_error`: repeated broker 500 failures; default threshold 3 in 15 minutes.

`alert_cooldown_seconds` prevents repeated email storms. Set `alert_email` in the
private configuration to enable DreamHost mail alerts. Alert events remain in
SQLite even when email is unavailable, so the failure is still diagnosable.

The public health route records unhealthy word-count refresh state. The API
500-error path records server errors, and `refresh-word-counts.php` records
scheduled refresh failures. `broker-recovery.php` independently checks the
health endpoint and records health failures during scheduled recovery checks.

## Verification

Run:

- `php web-deploy/tests/database-migrations-tests.php`
- `php web-deploy/tests/message-pagination-tests.php`
- `php web-deploy/tests/broker-alert-tests.php`
- `php web-deploy/tests/broker-auth-routing-tests.php`

The migration fixture starts at an older schema version, asserts a pre-migration
backup, verifies the upgrade, and checks `PRAGMA user_version`. Alert fixtures
cover the repeated-server-error threshold, refresh and health alerts, and the
cooldown behavior.
