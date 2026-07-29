# Character authentication deployment

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
QuestService.php
RpolClient.php
WordCountService.php
XpTrackingService.php
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

Merge the `xp` section from `player-assistant-broker/config.xp.example.php` into the same private `config.php`. Keep the XP and active-character source URLs in this private configuration; never place them in PWA JavaScript or accept them from a browser request. When `character_source_url` is omitted, the broker derives the `PCs/Player Characters Listing` page from the fixed XP source's Obsidian vault.

## Account import

After the updated API and private broker files are deployed, import the existing character password hashes from the repository root:

```powershell
.\web-deploy\import-character-accounts.ps1
```

The script prompts securely for the broker administrator key and sends only the salted password-hash document to the administrator-protected HTTPS endpoint. It does not upload the file into the public website directory.

## PWA files

Upload the complete `pwa/` directory, or at minimum:

```text
.htaccess
app.js
index.html
service-worker.js
styles.css
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

- Health response reports schema version `3`, a nonzero `character_account_count`, `xp_tracking_configured: true`, and the word-count snapshot availability state.
- Successful login sets `pa_character_session` with `Secure`, `HttpOnly`, `SameSite=Strict`, and path `/scarlethorizons/api/`.
- `GET /v1/me` returns the logged-in account's server-stored character key.
- `GET /v1/xp` returns one matching character's XP, class, attained level, and hit points for a player account and never includes the party array.
- A Dungeon Master session receives the validated current party XP table.
- Authenticated clients heartbeat through `/v1/presence`; players receive no other-user data, while the Dungeon Master receives every other enabled account with active-within-two-minutes state and the last login time for inactive users.
- `GET /v1/quests` reads `/scarlethorizons/pwa/quests.json`, validates its schema, and removes `gated-by` metadata before returning quests authorized for the current account.
- A missing or ambiguous character-key mapping fails with `xp_not_authorized`.
- XP responses omit the configured source URL and include `Cache-Control: no-store`.
- Anonymous word-count reads fail with HTTP 401; a logged-in session receives the latest validated wiki, IC, and OOC totals.
- Invalid uploads do not replace the broker's last known good word-count snapshot.
- Logout without the CSRF token fails.
- Correct logout expires the session cookie.
- Disabled accounts and expired sessions fail closed.
- Protected responses include `Cache-Control: no-store`.
