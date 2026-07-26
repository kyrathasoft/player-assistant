# Online RPOL Credentialing

Status date: 2026-07-23

## Objective

Move RPOL administrator credentials out of Player Assistant distributions and end-user machines. A PHP broker on `bryanmiller.us` will retain the RPOL credentials, authenticate to RPOL server-side, and return only approved game-page responses to authenticated Player Assistant clients.

The desktop app must never receive the RPOL administrator username, password, cookies, or browser session state.

## Completed

- Chosen a server-side PHP credential broker rather than distributing shared administrator credentials.
- Confirmed `http://bryanmiller.us` redirects to HTTPS and the broker is available over HTTPS.
- Confirmed the DreamHost account supports PHP.
- Created a public API under `bryanmiller.us/scarlethorizons/api`.
- Created a private broker directory as a sibling of the `bryanmiller.us` website directory.
- Generated the six broker files locally under `web-deploy`.
- Uploaded `index.php` and `.htaccess` to the public `/scarlethorizons/api/` directory.
- Uploaded `config.php`, `RpolClient.php`, `BrokerService.php`, and `broker.sqlite` to the private `/player-assistant-broker/` directory.
- Verified all PHP files with PHP 8.4 syntax linting.
- Verified the SQLite schema and reset it to zero token, audit, and rate-limit rows after local smoke testing.
- Verified local health-route and bearer-token issuance smoke tests.
- Added character-name/password authentication to the same private PHP/SQLite broker, including strict cookie sessions, CSRF-protected logout, legacy XP-hash migration, account/IP throttling, administrator account management, and server-derived character authorization.
- Added protected current-XP delivery through the broker, including a fixed Obsidian Publish source, validated latest-table parsing, one-character player responses, DM-only party totals, and bounded last-known-good caching.
- Verified the live health endpoint returns HTTP 200:

```text
https://bryanmiller.us/scarlethorizons/api/v1/health
```

- Verified the current live response:

```json
{
  "service": "player-assistant-broker",
  "schema_version": 2,
  "status": "ok",
  "rpol_credentials_configured": true,
  "character_account_count": 6
}
```
- Configured the private server with a server-generated administrator key and the existing RPOL credentials.
- Stored the broker administrator key in Windows Credential Manager under `PlayerAssistant/Broker/AdminKey`.
- Applied mode `0700` to the private broker directory and mode `0600` to its four private files.
- Confirmed the DreamHost PHP runtime provides `curl`, `dom`, and `pdo_sqlite`.
- Fixed the broker rate-limit transaction so its manual `BEGIN IMMEDIATE` uses matching manual `COMMIT` and `ROLLBACK` statements.
- Issued and revoked one-day test tokens successfully.
- Confirmed the approved RPOL page request reaches the upstream-fetch boundary and returns `502 rpol_unavailable`.
- Confirmed through a direct server-side client test that RPOL requires browser verification that PHP cURL cannot complete.

## Local Deployment Files

Public files:

```text
C:\repos\player-assistant\web-deploy\bryanmiller.us\scarlethorizons\api\index.php
C:\repos\player-assistant\web-deploy\bryanmiller.us\scarlethorizons\api\.htaccess
```

Private files:

```text
C:\repos\player-assistant\web-deploy\player-assistant-broker\config.php
C:\repos\player-assistant\web-deploy\player-assistant-broker\BrokerHttpException.php
C:\repos\player-assistant\web-deploy\player-assistant-broker\CharacterAuthService.php
C:\repos\player-assistant\web-deploy\player-assistant-broker\XpTrackingService.php
C:\repos\player-assistant\web-deploy\player-assistant-broker\RpolClient.php
C:\repos\player-assistant\web-deploy\player-assistant-broker\BrokerService.php
C:\repos\player-assistant\web-deploy\player-assistant-broker\broker.sqlite
```

`broker.sqlite` is ignored by Git so issued tokens, audit records, and runtime state cannot be committed accidentally.

## Server Layout

```text
/home/DREAMHOST_USER/
|-- bryanmiller.us/
|   `-- scarlethorizons/
|       `-- api/
|           |-- index.php
|           `-- .htaccess
`-- player-assistant-broker/
    |-- config.php
    |-- BrokerHttpException.php
    |-- CharacterAuthService.php
    |-- XpTrackingService.php
    |-- RpolClient.php
    |-- BrokerService.php
    `-- broker.sqlite
```

Only the two files under `/scarlethorizons/api/` are web-accessible. The private broker directory must not have a public URL.

## Implemented Broker Controls

- HTTPS enforcement.
- No-store responses and restrictive browser security headers.
- Private configuration outside the website document root.
- Random, revocable, expiring bearer tokens.
- SHA-256 token hashes in SQLite; raw bearer tokens are returned only when issued.
- Separate administrator key for token creation and revocation.
- Per-token request-rate limiting.
- Bounded RPOL responses with TLS certificate verification enabled.
- Strict RPOL host restriction to `https://rpol.net`.
- Strict game restriction to game ID `80170`.
- Read-only path allowlist for `/game.php`, `/gameinfo.php`, and `/display.cgi`.
- Query-parameter allowlists that reject `markread` and unknown operations.
- Manual redirect validation to prevent cross-host credential or cookie forwarding.
- Audit records that retain only token ID, time, remote address, target path, and outcome.
- Generic client-facing upstream errors without RPOL credentials or session details.
- RPOL content returned as base64 inside bounded JSON to preserve the original response bytes.
- Character passwords validated only by the private broker; existing PBKDF2 hashes are upgraded to PHP-native hashes after successful login.
- Path-restricted `Secure`, `HttpOnly`, `SameSite=Strict` sessions with ID regeneration, idle/absolute expiry, and administrative session revocation.
- Exact-Origin login/logout enforcement, CSRF-protected logout, generic failures, per-account/per-address throttling, and redacted authentication audits.
- Server-side character keys and roles loaded from the authenticated account on every protected request.
- Fixed server-side XP source with HTTPS, redirect rejection, host/path allowlisting, strict time and response-size limits, and validated content types.
- Fail-closed player XP filtering requiring exactly one row to match the authenticated character key.
- DM-only party XP response scope; ordinary player responses never contain the party array.
- Validated bounded server cache with stale-response labeling; protected XP responses remain `Cache-Control: no-store`.

## Current Blocker

The private server configuration, permissions, PHP extensions, health check, and token flow are working. Live RPOL authentication is blocked because RPOL requires browser verification that DreamHost PHP cURL cannot complete.

The broker correctly returns HTTP 502 with `rpol_unavailable`, does not expose the upstream exception to clients, and records the failed fetch through its existing audit path. Do not weaken TLS verification or distribute RPOL credentials or browser cookies to work around this blocker.

## Remaining Steps

### 1. Configure Private Server Secrets - Completed

Edit the uploaded `/player-assistant-broker/config.php` file and replace these three placeholders:

```text
CHANGE_ME_TO_A_RANDOM_64_CHARACTER_ADMIN_KEY
CHANGE_ME_RPOL_USERNAME
CHANGE_ME_RPOL_PASSWORD
```

Generate the administrator key on the DreamHost shell rather than inventing a human-readable password:

```bash
php -r "echo bin2hex(random_bytes(32)), PHP_EOL;"
```

Do not add the resulting key or RPOL credentials to this repository, this document, email, chat, logs, or screenshots.

### 2. Restrict Private File Permissions - Completed

From the DreamHost user home directory:

```bash
chmod 700 player-assistant-broker
chmod 600 player-assistant-broker/config.php
chmod 600 player-assistant-broker/broker.sqlite
chmod 600 player-assistant-broker/RpolClient.php
chmod 600 player-assistant-broker/BrokerService.php
```

Confirm PHP can still read the files because DreamHost PHP should execute as the assigned website user.

### 3. Verify Required PHP Extensions - Completed

The broker requires these PHP extensions:

```text
curl
dom
pdo_sqlite
```

Check them through SSH:

```bash
php -m | grep -E 'curl|dom|pdo_sqlite'
```

### 4. Recheck Health - Completed

After updating `config.php`, request:

```text
https://bryanmiller.us/scarlethorizons/api/v1/health
```

Expected result:

```json
{
  "service": "player-assistant-broker",
  "schema_version": 1,
  "status": "ok",
  "rpol_credentials_configured": true
}
```

### 5. Issue the First Client Token - Completed

Use the private administrator key to create a short-lived test token. Avoid placing the administrator key directly in shell history:

```bash
read -s BROKER_ADMIN_KEY
curl --fail-with-body \
  -X POST \
  -H "X-Broker-Admin-Key: $BROKER_ADMIN_KEY" \
  -H "Content-Type: application/json" \
  --data '{"label":"player-assistant-test","expires_in_days":1}' \
  https://bryanmiller.us/scarlethorizons/api/v1/tokens
unset BROKER_ADMIN_KEY
```

The response contains the raw bearer token once. Keep it private. The server stores only its SHA-256 hash.

### 6. Test a Signed RPOL Snapshot - Root Page Completed

Use the short-lived bearer token to request one approved RPOL page:

```bash
read -s BROKER_TOKEN
curl --fail-with-body \
  -G \
  -H "Authorization: Bearer $BROKER_TOKEN" \
  --data-urlencode "url=https://rpol.net/game.php?gi=80170" \
  https://bryanmiller.us/scarlethorizons/api/v1/snapshots/page
unset BROKER_TOKEN
```

Success criteria:

- HTTP 200.
- JSON schema version 1.
- `source_url` remains on `https://rpol.net`.
- `content_type` is HTML.
- `content_base64` is present and its SHA-256 matches `content_sha256`.
- No RPOL username, password, cookie, or login form in the response.

Verified on July 16, 2026 with a one-day token that was revoked after the test. The stored root snapshot returned schema version 1, game ID `80170`, the exact approved source URL, matching content hash, an RPOL title, and no login form.

### 7. Cloudflare Fallback - Implemented with a Scheduled Publisher

Direct PHP/cURL authentication is rejected by RPOL. The selected design is a trusted Windows publisher that uploads signed, sanitized snapshots:

- `publish-rpol-snapshots.ps1` runs the Release executable in publisher mode.
- The publisher recognizes a loaded RPOL verification window and closes it automatically.
- HMAC-SHA256 snapshot metadata and content hashes are verified by the private broker before atomic storage.
- On each upload, the broker removes snapshot files older than the configured retention period (seven days by default, never shorter than `snapshot_max_age_seconds`).
- The daily `Player Assistant RPOL Snapshot Publisher` task runs at 3:00 AM with `StartWhenAvailable`.
- The live health response reports `snapshot_signing_configured: true` and `snapshot_count: 3`.
- Publisher state persists the approved URL queue and advances exactly one target after each accepted upload.

The root, Game Intro, and Cast pages publish successfully across separate executions. Eleven discovered thread pages remain queued for subsequent scheduled executions. Do not weaken TLS, publish browser cookies, or distribute the administrator credentials to accelerate that queue.

### 8. Integrate Player Assistant with the Broker - Implemented

Implemented desktop changes:

- Add an exact HTTPS broker URL setting.
- Extend the network allowlist only for the exact broker host and `/scarlethorizons/api/v1/` path.
- Add a bounded `ResponseHeadersRead` broker client.
- Store only the per-user broker bearer token in Windows Credential Manager.
- Request approved RPOL pages through the broker instead of using administrator credentials locally.
- Validate response schema, content type, source URL, size, and base64 content before use.
- Decode the RPOL response bytes and feed them into the existing HTML parsers.
- Add clear handling for expired, revoked, rate-limited, and unauthorized tokens.
- Fail closed when the broker is unavailable; do not fall back to distributed administrator credentials.
- Remove direct RPOL administrator credential requirements from startup validation and user-facing guidance.
- Add focused broker-client, allowlist, response-validation, and negative-path tests.

The direct RPOL path remains available only when no broker token is installed. When a broker token is present, broker retrieval fails closed and does not fall back to administrator credentials.

### 9. Remove Distributed Administrator Credentials

Only after every required approved page works end to end. The root page alone is not sufficient:

- Remove `RPOL user name` and `RPOL password` from hosted `settings.local.json`.
- Remove the same values from local and publish-time settings sidecars.
- Stop requiring those keys in publish and installer verification.
- Delete migrated RPOL administrator credentials from end-user Windows Credential Manager entries.
- Rotate the RPOL administrator password after all old copies have been removed.
- Prefer a dedicated least-privilege RPOL service account over a personal administrator account if RPOL permits it.

### 10. Release Verification

- Build the Release executable and test harness.
- Run focused broker credential and RPOL data-flow tests.
- Publish sequentially to `Release` and `Release/publish`.
- Run publish verification, runtime-sidecar verification, Release/publish parity, installer verification, and the RC checklist.
- Run a clean-machine smoke test with no RPOL administrator credentials on the client.
- Confirm the app can retrieve required RPOL data using only a revocable broker token.
- Confirm logs and diagnostics contain no administrator credentials, bearer tokens, cookies, or raw session state.
- Refresh Graphify after desktop integration.
- Commit and push only after the full broker flow passes.

## Security Limitations

- A bearer-token holder can inspect any RPOL content that token is authorized to request. The current schema gives every token the same approved game-page access.
- Add token scopes, hero restrictions, or endpoint-specific authorization before deployment if different users require different RPOL visibility.
- The broker currently authenticates to RPOL for each PHP request. Persistent server-side session caching may be added later, but cookies must remain private and encrypted or permission-restricted outside the web root.
- The broker does not make administrator-only RPOL page content player-safe automatically. Only expose paths and responses appropriate for all issued token holders.
- HTTPS protects transport but does not replace token revocation, least privilege, response validation, and credential rotation.

## Do Not Remove Yet

Keep the RPOL administrator credential only on the trusted snapshot-publisher machine until all required approved pages publish and a clean client works using only a broker token. Do not distribute it to end-user installations. Removing it from the publisher now would prevent scheduled refreshes; removing the direct client path before the remaining pages pass would break RPOL-backed application features.
