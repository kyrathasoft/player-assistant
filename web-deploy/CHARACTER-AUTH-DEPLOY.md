# Character authentication deployment

## Configure another Windows computer

Run `setup-dreamhost-codex-access.ps1` from the repository to create or reuse a
dedicated SSH key, enable the Windows SSH agent, configure a stable host alias,
and verify access to the three existing deployment directories. If the public
key is not installed at DreamHost yet, the script displays it and pauses while
you add it through the DreamHost panel. It never stores a website password,
token, or private key in the repository.

## Public API file

Upload:

```text
web-deploy/bryanmiller.us/scarlethorizons/api/index.php
```

to:

```text
/home/DREAMHOST_USER/bryanmiller.us/scarlethorizons/api/index.php
```

## Private broker files

Upload these beside the existing private `config.php` and `broker.sqlite`:

```text
BrokerHttpException.php
CharacterAuthService.php
BrokerService.php
MessageService.php
RevisionService.php
QuestService.php
RpolClient.php
WordCountService.php
XpTrackingService.php
refresh-word-counts.php
broker-recovery.php
```

Destination:

```text
/home/DREAMHOST_USER/player-assistant-broker/
```

Keep the directory mode `0700` and the private files mode `0600`. Do not put `config.php`, `broker.sqlite`, `xp-passwords.json`, password hashes, PHP session files, or snapshots under the website document root.

## Private configuration

Merge the `auth` section from `player-assistant-broker/config.auth.example.php` into the existing private `config.php`. The production origin must remain:

```text
https://bryanmiller.us
```

Optionally merge `player-assistant-broker/config.messages.example.php` to override the default 90-day and 500-read-message retention limits. Retention applies only to read messages; unread messages are paginated and retained until acknowledged.

Merge the `xp` section from `player-assistant-broker/config.xp.example.php` into the same private `config.php`. Keep the XP and active-character source URLs in this private configuration; never place them in PWA JavaScript or accept them from a browser request. When `character_source_url` is omitted, the broker derives the `PCs/Player Characters Listing` page from the fixed XP source's Obsidian vault.

Set the optional `word_counts` section to enable signed automatic word-count refresh:

```php
'word_counts' => [
    'source_url' => 'https://.../word-counts.json',
    'maximum_stale_seconds' => 604800,
    'status_path' => '/home/DREAMHOST_USER/player-assistant-broker/word-count-refresh-status.json',
    'signature_key_id' => 'word-counts-...',
    'signature_public_key' => 'BASE64_ED25519_PUBLIC_KEY',
],
```

An authenticated
`GET /v1/word-counts` returns the cached snapshot and, when its observation time
is older than `maximum_stale_seconds`, attempts to replace it from `source_url`.
If `source_url` is empty, the broker retains manual administrator uploads and
does not attempt automatic refresh.

The production source is a public, data-only JSON file outside the PWA:

```text
https://bryanmiller.us/scarlethorizons/data/word-counts.json
```

Run `setup-word-count-signing-key.ps1` once to store the Ed25519 private key in
Windows Credential Manager and create `word-count-signing-public.json`.
`publish-word-counts.ps1` signs the source, stages it through the dedicated
DreamHost SSH key, verifies the public copy, then publishes the matching
administrator snapshot. Any source verification or broker failure restores the
previous public source. Use `-SkipSourceUpload` only when intentionally retaining
the existing source file.

Run `deploy-word-count-refresh.ps1` to atomically install the private services,
merge public signing metadata into `config.php`, install the cron entry, prune
only the approved backup patterns to five copies each, force one signed refresh,
and run production drift verification:

```cron
17 */6 * * * /usr/bin/php /home/DREAMHOST_USER/player-assistant-broker/refresh-word-counts.php >> /home/DREAMHOST_USER/player-assistant-broker/word-count-refresh-cron.log 2>&1
```

The runner forces signature verification on every scheduled execution and writes
atomic private status. Public health exposes only safe readiness, success time,
scheduler time/status, and fixed error codes. Run
`test-word-count-refresh-deployment.ps1` independently to check hashes,
permissions, signing metadata, source signature, cron, health, and retention.

## Broker database recovery and observability

Deploy `player-assistant-broker/broker-recovery.php` beside `broker.sqlite`, then
schedule it on DreamHost:

```cron
23 */6 * * * /usr/bin/php /home/DREAMHOST_USER/player-assistant-broker/broker-recovery.php >> /home/DREAMHOST_USER/player-assistant-broker/broker-recovery.log 2>&1
```

Each run performs a live `PRAGMA integrity_check`, creates a consistent SQLite
copy with `VACUUM INTO`, opens the copy and repeats the integrity check, retains
the newest configured number of backups, checks the public broker health endpoint,
and atomically writes `broker-recovery-status.json`. Configure an optional
`database_recovery` section from `player-assistant-broker/config.recovery.example.php` into the private `config.php` to override the backup folder, retention count, health URL, status path, or alert email. Failures and
unhealthy word-count refresh status are reported through the configured email
alert when DreamHost mail is available.

Run `backup-broker-database.ps1` from a trusted Windows machine after the remote
run. It downloads the newest backup and status file, verifies the SHA-256 hash,
and applies local retention. Use multiple `-BackupRoots` values to place the
same verified backup in more than one destination.

Recommended locations:

- Primary off-host copy: a synchronized OneDrive folder such as
  `onedrive:/backups of sites or directories/player-assistant-broker/`.
- Independent second copy: the NAS backup share, such as
  `/homes/kyrathasoft/Backup/player-assistant-broker/`.
- Same-host staging only: DreamHost
  `/home/DREAMHOST_USER/deploy-backups/player-assistant-broker/`; useful for
  quick recovery but not sufficient as the only backup.

Keep at least 14 daily copies on each off-host target. Test restoration monthly
by opening the newest copy with SQLite and running `PRAGMA integrity_check`;
never overwrite the live database during a restore test.

## Account import

After the updated API and private broker files are deployed, import the existing character password hashes from the repository root:

```powershell
.\web-deploy\import-character-accounts.ps1
```

The script prompts securely for the broker administrator key and sends only the salted password-hash document to the administrator-protected HTTPS endpoint. It does not upload the file into the public website directory.

## PWA files

Deploy changed public runtime files through the transactional deployment script:

```powershell
.\web-deploy\deploy-pwa-files.ps1 -Files @(
    '.htaccess',
    'app.js',
    'index.html',
    'level-progression.json',
    'magic-items.json',
    'modules/dice.js',
    'modules/search.js',
    'modules/translator.js',
    'service-worker.js',
    'styles.css',
    'version.js'
)
```

Include every changed runtime file in one invocation. The script verifies staged
SHA-256 hashes, clones the complete live release under the private
`~/.player-assistant-pwa-releases` directory, and activates the selected release
with one symlink rename. The first run migrates the
existing PWA directory into the managed-release layout; later activations and
rollbacks switch the public symlink atomically, so clients never observe a mixed
release. The prior release remains available until verification completes.

The script commits only after HTTPS hashes, security/cache headers, and current
broker API behavior pass `pwa/test-deployment.ps1`. If verification fails, it
atomically restores the previous release; newly introduced files disappear with
the rejected release directory. `.htaccess` is verified through its observable
HTTPS headers because the file itself is not publicly retrievable. Interrupted
install commands are retried idempotently, and a persistent transaction marker
prevents a later deployment from silently replacing an unresolved release.

The complete PWA runtime includes at minimum:

```text
.htaccess
app.js
index.html
level-progression.json
modules/dice.js
modules/search.js
modules/translator.js
service-worker.js
styles.css
version.js
quests.json
```

## API routes

Player routes:

```text
POST /scarlethorizons/api/v1/login
GET  /scarlethorizons/api/v1/session
GET  /scarlethorizons/api/v1/me
GET  /scarlethorizons/api/v1/xp
GET  /scarlethorizons/api/v1/word-counts
GET  /scarlethorizons/api/v1/presence
GET  /scarlethorizons/api/v1/quests
GET  /scarlethorizons/api/v1/revisions
POST /scarlethorizons/api/v1/quest-requests
POST /scarlethorizons/api/v1/quest-requests/{request-id}/decision
POST /scarlethorizons/api/v1/quest-requests/{request-id}/acknowledge
GET  /scarlethorizons/api/v1/messages
POST /scarlethorizons/api/v1/messages
POST /scarlethorizons/api/v1/messages/{message-id}/read
POST /scarlethorizons/api/v1/logout
```

Administrator routes:

```text
POST  /scarlethorizons/api/v1/admin/character-accounts/import
GET   /scarlethorizons/api/v1/admin/character-accounts
POST  /scarlethorizons/api/v1/admin/character-accounts
PATCH /scarlethorizons/api/v1/admin/character-accounts/{account-id}
PUT   /scarlethorizons/api/v1/admin/word-counts
```

Publish a completed, zero-failure count with `web-deploy/publish-word-counts.ps1`.
The script prompts securely for the administrator key and verifies that the
broker returns the exact uploaded totals and observation time.

## Verification

- Health response reports schema version `7`, a nonzero `character_account_count`, `xp_tracking_configured: true`, the word-count snapshot availability state, `quest_request_workflow_configured: true`, and configured broker operations.
- Configure a dedicated, enabled player monitor account whose `character_key` maps to a current XP character. Store only its login name and password in the `PWA_MONITOR_CHARACTER_NAME` and `PWA_MONITOR_PASSWORD` GitHub repository secrets.
- The scheduled PWA monitor fails closed when either secret is absent. Its authenticated checks require the login and identity response contracts, `Cache-Control: no-store`, a non-stale XP snapshot fetched within 24 hours, and a source-observed and broker-uploaded word-count snapshot no older than seven days. It never prints protected response bodies or credentials.
- DreamHost denies GitHub-hosted Azure runners at Apache before authentication, so `.github/workflows/pwa-synthetic-monitor.yml` is a manual deployment/verification workflow rather than the recurring monitor. It installs `PwaSyntheticMonitor.php` and `run-pwa-monitor.php` in the private broker directory and idempotently schedules the runner every 15 minutes through DreamHost cron.
- The cron monitor stores credentials only in mode-600 private `config.php`, writes atomic mode-600 `pwa-monitor-status.json`, verifies logout and the subsequent anonymous session, and sends cooldown-limited email only for failure/recovery transitions. `/v1/admin/health` exposes only `configured`, `healthy`, run/success/failure timestamps, and the safe error code; the public health route remains unchanged.
- The deployment workflow uses a transient mode-600 credential JSON solely to move repository secret values over SSH. The remote installer deletes it immediately, backs up replaced runtime/config/crontab state under `/home/dh_4gg2za/deploy-backups`, atomically promotes the private files, preserves unrelated cron entries, runs the monitor once, and verifies permissions and cron installation.

## Broker database recovery and observability

Merge `config.operations.example.php` into the private production `config.php`, configure the private FTPS environment and alert address, and ensure PHP cURL is available. The maintenance job:

- Runs `PRAGMA integrity_check` against the live database.
- Creates a consistent SQLite backup with `VACUUM INTO` under `broker-backups/`.
- Uploads the backup and its SHA-256 manifest over explicit FTPS, downloads each uploaded file, and verifies its SHA-256 before applying off-host retention.
- Do not configure an `htdocs` FTPS destination for plaintext broker backups; add client-side encryption before using a publicly served remote directory.
- Restores the newest backup into a private temporary database and runs another integrity check.
- Retains the configured number of local backup copies and removes restore-test copies after validation.

The six-hour word-count cron records refresh failures. The daily maintenance cron records backup/restore failures, and the API records repeated HTTP 500 failures. Configured email alerts are rate-limited by `alert_cooldown_seconds`.
- Successful login sets `pa_character_session` with `Secure`, `HttpOnly`, `SameSite=Strict`, and path `/scarlethorizons/api/`.
- `GET /v1/me` returns the logged-in account's server-stored character key.
- `GET /v1/xp` returns one matching character's XP, class, attained level, and hit points for a player account and never includes the party array.
- A Dungeon Master session receives the validated current party XP table.
- Authenticated clients heartbeat through `/v1/presence`; players receive no other-user data, while the Dungeon Master receives every other enabled account with active-within-two-minutes state and the last login time for inactive users.
- `GET /v1/quests` reads `/scarlethorizons/pwa/quests.json`, validates its schema, and removes `gated-by` and `unlocked-by` metadata before returning quests authorized for the current account.
- Player accounts may request only quests whose current state is `available` or `available (abandoned)`; Dungeon Master accounts cannot request quests.
- The Dungeon Master may approve or deny pending PC requests. Approval atomically records the decision and overlays that quest's global runtime state as `active` in `broker.sqlite`.
- Pending requests and unread decisions persist across sessions. Players may acknowledge their own decisions; only the Dungeon Master may decide requests.
- A missing or ambiguous character-key mapping fails with `xp_not_authorized`.
- XP responses omit the configured source URL and include `Cache-Control: no-store`.
- Anonymous word-count reads fail with HTTP 401; a logged-in session receives the latest validated wiki, IC, and OOC totals.
- Invalid uploads do not replace the broker's last known good word-count snapshot.
- Logout without the CSRF token fails.
- Correct logout expires the session cookie.
- Disabled accounts and expired sessions fail closed.
- Protected responses include `Cache-Control: no-store`.
