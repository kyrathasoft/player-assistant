# Restore the current DreamHost Player Assistant PWA

This folder is intentionally specific to Bryan's existing DreamHost installation. It wraps the reusable installer in `../online-installer-for-pwa/dist/` and pins every production target rather than accepting arbitrary hosts or paths.

## Fixed target

- HTTPS origin: `https://bryanmiller.us`
- SSH host: `pdx1-shared-a1-13.dreamhost.com`
- SSH account: `dh_4gg2za`
- Local identity: `C:\Users\Bryan\.ssh\dreamhost_player_assistant`
- Account home: `/home/dh_4gg2za`
- Public document root: `/home/dh_4gg2za/bryanmiller.us`
- PWA: `/home/dh_4gg2za/bryanmiller.us/scarlethorizons/pwa`
- API: `/home/dh_4gg2za/bryanmiller.us/scarlethorizons/api`
- Private broker: `/home/dh_4gg2za/player-assistant-broker`
- Existing private configuration: `/home/dh_4gg2za/player-assistant-broker/config.php`
- Existing SQLite state: `/home/dh_4gg2za/player-assistant-broker/broker.sqlite`
- PHP CLI: `/usr/bin/php`
- Private upload staging: `/home/dh_4gg2za/.player-assistant-restore/<run-id>`

The controller contains no passwords, private configuration, database contents, or SSH private-key material. It only records the local key path.

## Verified host assumptions

A read-only preflight confirmed:

- PHP `8.2.30` at `/usr/bin/php`.
- Required `phar`, `pdo_sqlite`, `sodium`, `curl`, and `openssl` extensions.
- Existing PWA, API, private runtime, `config.php`, and `broker.sqlite` paths.
- Five current crontab entries referencing the private Player Assistant broker.

The controller rechecks the relevant assumptions every time and fails closed if roots move, symlinks appear, configuration/state disappears, required PHP extensions are unavailable, a payload checksum fails, or an unresolved installer transaction exists.

## Setup

From the repository root:

```powershell
python -m pip install -r pwa\restore-dreamhost-pwa\requirements.txt
```

Paramiko uses the existing system `known_hosts` entry and rejects unknown host keys. The private key is never printed or uploaded.

## Read-only preflight (default)

```powershell
pwa\restore-dreamhost-pwa\restore-dreamhost-pwa.ps1
```

Equivalent explicit action:

```powershell
pwa\restore-dreamhost-pwa\restore-dreamhost-pwa.ps1 preflight
```

`status` also performs preflight and lists only unresolved transaction IDs and states:

```powershell
pwa\restore-dreamhost-pwa\restore-dreamhost-pwa.ps1 status
```

## Reinstall production

The production action requires an exact confirmation value:

```powershell
pwa\restore-dreamhost-pwa\restore-dreamhost-pwa.ps1 install --confirm-production-reinstall bryanmiller.us
```

The controller then:

1. Validates the checked-in reusable installer against its canonical source, then validates the payload and SHA-256 sidecar.
2. Opens one host-key-verified Paramiko transport and reuses it for SFTP and commands.
3. Revalidates the exact DreamHost roots, existing private config/database, PHP binary, and extensions.
4. Refuses to begin if an unresolved installer transaction exists.
5. Creates a private mode-700 run directory outside the document root.
6. Uploads only the reusable PHP installer, payload TAR, and checksum sidecar through atomic temporary names with mode 600, then reads each remote file back through SFTP and verifies its SHA-256.
7. Runs one remote installation attempt with fixed origin/roots and `--verification=local`; the existing private `config.php` is reused and never downloaded.
8. Verifies the public manifest and service worker over HTTPS against exact local SHA-256 hashes without redirects, and validates the API health JSON identifies the expected healthy broker schema.
9. Requests remote HTTPS finalization for the exact returned transaction ID.
10. Removes the private controller upload directory only after confirmed finalization.

If local HTTPS verification fails after a known pending transaction is returned, the controller invokes rollback for that exact ID. If SSH completion is ambiguous before valid final JSON is received, it does not blindly repeat installation; use `status` and inspect the server-side transaction.

## Resume a known transaction

```powershell
pwa\restore-dreamhost-pwa\restore-dreamhost-pwa.ps1 finalize --transaction-id 20260815T120000Z-deadbeef
pwa\restore-dreamhost-pwa\restore-dreamhost-pwa.ps1 rollback --transaction-id 20260815T120000Z-deadbeef
```

Transaction IDs are strictly validated. Finalize and rollback upload a fresh copy of only the reusable installer into a new private run directory, execute one exact transaction action, and remove that controller directory after confirmed success.

## Safety boundaries

- CLI-only; no browser-accessible installer endpoint is created.
- Fixed DreamHost account, origin, public root, private root, PHP path, and key path.
- No option permits alternate remote filesystem targets.
- Existing private configuration and SQLite state stay outside the web root.
- The reusable installer handles maintenance gating, consistent SQLite snapshots, migrations, cron preservation, transactional promotion, HTTPS verification, restartable rollback, and cleanup.
- Remote mutation is never retried automatically after ambiguous SSH completion.
- Transaction discovery treats permission/read failures, symlinked or non-canonical transaction roots, malformed directory names, malformed manifests, and unknown states as blocking instead of assuming the server is clean.
- Finalize and rollback accept only the requested transaction ID and their exact expected terminal status before controller staging is removed.
- Cleanup re-canonicalizes both the fixed staging root and exact run directory immediately before deleting only controller artifacts.
- This controller has been exercised only in read-only preflight mode; creating it does not reinstall production.
