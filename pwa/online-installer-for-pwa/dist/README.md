# Player Assistant online PWA installer

This directory builds and tests a CLI-only PHP installer that deploys Player Assistant to another HTTPS domain while retaining the fixed URL layout:

- `/scarlethorizons/pwa/`
- `/scarlethorizons/api/`

The installer deploys the website application. It cannot silently install the PWA onto an end user's browser or operating system; that remains a browser-mediated action.

## Files

- `build-package.ps1` — creates the versioned deployment payload and checksum.
- `install-player-assistant-web.php` — CLI-only transactional installer, verifier, finalizer, and rollback command.
- `config.template.php` — complete target configuration template with no real credentials.
- `package-layout.json` — allowlisted package inventory used by the builder.
- `tests/installer-tests.php` — package, rejection, migration, install, upgrade, reporting, and rollback contracts.
- `dist/` — generated distributable files; do not place completed private configuration here.

## Installed components

The payload includes:

- The complete production PWA runtime, modules, icons, data files, and hero tokens.
- The public API `.htaccess` and a controlled `index.php` template.
- The private broker runtime and database migration entry point.
- Word-count refresh, maintenance, database recovery, and authenticated PWA-monitor commands.

It never packages `config.php`, credentials, `broker.sqlite`, snapshots, status files, logs, or backups.

## Build on the trusted development machine

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\pwa\online-installer-for-pwa\build-package.ps1
php .\pwa\online-installer-for-pwa\tests\installer-tests.php
```

The default output directory is `pwa\online-installer-for-pwa\dist`. It contains:

- `install-player-assistant-web.php`
- `player-assistant-web-payload-<version>.tar`
- `player-assistant-web-payload-<version>.tar.sha256`
- `config.template.php`
- `README.md`

The SHA-256 sidecar detects corruption or modification. It does not establish publisher identity by itself, so transfer all files through an authenticated SSH/SFTP session from a trusted build machine.

## Server prerequisites

- An authenticated SSH or hosting-control-panel terminal. Do not expose this installer through HTTP.
- PHP CLI 8.1 or newer.
- PHP extensions: `curl`, `openssl`, `pdo_sqlite`, `phar`, and `sodium`.
- `exec()` available for PHP lint, migration, and verification subprocesses.
- Apache rewrite and header support for the packaged `.htaccess` files.
- HTTPS already active for the target domain.
- The target document root must already exist and its final directory name must equal the target hostname.
- A private broker root that is a sibling of—not inside—the document root.
- `/usr/bin/php` and `/usr/bin/crontab` when managed cron installation is enabled.
- Sufficient free space for staging, the live release, and one rollback snapshot.

Example layout:

```text
/home/account/example.com/                         public document root
/home/account/example.com/scarlethorizons/pwa/    installed PWA
/home/account/example.com/scarlethorizons/api/    installed public API
/home/account/player-assistant-broker-example/    private broker and SQLite database
/home/account/installer-upload/                    temporary private upload directory
```

## Prepare the private configuration

1. Copy `config.template.php` into a private upload directory outside every website document root.
2. Replace every `CHANGE_ME_...` value with the target's real secret or URL.
3. Leave these installer-owned placeholders unchanged:
   - `__TARGET_ORIGIN__`
   - `__PRIVATE_ROOT__`
   - `__ACCOUNT_HOME__`
4. Protect the completed file:

```sh
chmod 600 /home/account/installer-upload/config.php
```

The installer replaces only those three declared placeholders. It then loads the resulting configuration in a separate PHP process and validates:

- Target origin, API base path, and authentication cookie path.
- SQLite, snapshot, XP-award, status, backup, recovery, and monitor paths remain under the private root.
- Required secrets are no longer placeholders.
- Signing keys decode to the required sizes.
- RPOL and authenticated monitor credentials are present.
- XP and word-count source URLs use HTTPS.
- Recovery and monitor URLs point to the selected target origin.

Treat `--config-source` as trusted executable PHP supplied by the server administrator.

## Upload over SSH/SFTP

Upload these four files into a private, mode-`0700` server directory:

```text
install-player-assistant-web.php
player-assistant-web-payload-<version>.tar
player-assistant-web-payload-<version>.tar.sha256
completed config.php
```

Do not upload them beneath the target document root. Use one authenticated, host-key-verified SSH/SFTP transport where the hosting environment is sensitive to repeated connections.

## Install or upgrade with full verification

Run the installer through SSH:

```sh
php /home/account/installer-upload/install-player-assistant-web.php \
  --package=/home/account/installer-upload/player-assistant-web-payload-0.9.8.tar \
  --origin=https://example.com \
  --public-root=/home/account/example.com \
  --private-root=/home/account/player-assistant-broker-example \
  --config-source=/home/account/installer-upload/config.php
```

For an upgrade, `--config-source` may be omitted when the existing private `config.php` is already correct. If it is supplied during an upgrade, the materialized configuration must exactly match the installed configuration; the installer will not silently replace production secrets.

The default command:

1. Takes an exclusive per-account installer lock and rejects unresolved prior transactions.
2. Verifies package and per-file hashes, byte sizes, inventory, paths, JSON, PHP syntax, and prerequisites.
3. Stages privately, applies explicit public/private permissions, and snapshots existing private runtime and configuration.
4. Moves the existing API into restricted rollback storage and places the public API path behind an HTTP 503 maintenance gate.
5. Creates a consistent SQLite rollback snapshot only after API writes are quiesced.
6. Runs the SQLite migration before promoting runtime code that expects the current schema.
7. Promotes private dependencies, snapshots/promotes the public PWA, and replaces the maintenance gate with the new API.
8. Preserves the original crontab and installs managed refresh, maintenance, recovery, and monitoring entries.
9. Verifies installed hashes, permissions, PHP syntax, SQLite schema/integrity, all public PWA bytes over HTTPS, security headers, health, and anonymous session behavior.
10. Removes staging and rollback files only after successful HTTPS verification; a cleanup failure is reported without attempting an unsafe post-verification rollback.

The final stdout line is JSON. A more detailed machine-readable report is stored privately under:

```text
/home/account/.player-assistant-install-reports/<transaction-id>.json
```

## Two-phase local verification

Use this only when public HTTPS cannot yet reach the newly promoted target:

```sh
php /home/account/installer-upload/install-player-assistant-web.php \
  --package=/home/account/installer-upload/player-assistant-web-payload-0.9.8.tar \
  --origin=https://example.com \
  --public-root=/home/account/example.com \
  --private-root=/home/account/player-assistant-broker-example \
  --config-source=/home/account/installer-upload/config.php \
  --verification=local
```

Local verification preserves a restricted rollback transaction and returns its ID. No second installation may start until that transaction is finalized or rolled back.

Finalize after DNS and HTTPS are live:

```sh
php /home/account/installer-upload/install-player-assistant-web.php \
  --finalize-transaction=<transaction-id> \
  --origin=https://example.com \
  --public-root=/home/account/example.com \
  --private-root=/home/account/player-assistant-broker-example
```

Or roll it back:

```sh
php /home/account/installer-upload/install-player-assistant-web.php \
  --rollback-transaction=<transaction-id> \
  --origin=https://example.com \
  --public-root=/home/account/example.com \
  --private-root=/home/account/player-assistant-broker-example
```

Rollback restores the prior PWA, API, private runtime, configuration, SQLite snapshot, and managed crontab. Targets that did not exist before a new installation are removed.

If SSH disconnects before the installer prints its final JSON, inspect the newest private transaction `manifest.json`. A transaction left in `preparing` or `promoted` is deliberately recoverable with the same `--rollback-transaction` command. A transaction in `pending_https_verification` can be finalized or rolled back. A transaction in `finalize_cleanup` has already passed HTTPS verification and must be completed by rerunning the same `--finalize-transaction` command; do not roll it back. Do not edit transaction state manually on a real host.

## Optional controls

- `--skip-cron` — skip all managed cron changes. This reduces operational functionality and should only be used when the host uses another scheduler.
- `--retain-backup` — retain restricted rollback evidence after a successful one-phase HTTPS-verified installation.
- `--verification=local` — defer public HTTPS verification and preserve a transaction that must be finalized or rolled back.
- `--help` — print the complete command contract.

Unknown and duplicate options are rejected. All value options use `--name=value` syntax.

## Security and operational notes

- Never put a completed configuration, database, installer transaction, or rollback snapshot under a document root.
- The installer rejects HTTP execution, non-HTTPS origins, traversal, aliases/symlinks at critical roots, undeclared archive entries, case-colliding paths, oversized archives, malformed JSON, and mismatched hashes.
- Public files are promoted as complete directories rather than copied piecemeal.
- Existing API traffic receives a temporary 503 maintenance response while schema and private runtime changes are promoted, preventing old request code from observing a newly migrated incompatible schema.
- Mutating operations should not be blindly rerun after an ambiguous SSH acknowledgement. Inspect the transaction report and live state, then finalize or roll back.
- Delete the private upload directory after the report confirms installation and cleanup.
