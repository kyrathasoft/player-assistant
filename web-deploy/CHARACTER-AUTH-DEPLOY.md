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
RpolClient.php
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
```

## API routes

Player routes:

```text
POST /scarlethorizons/api/v1/login
GET  /scarlethorizons/api/v1/session
GET  /scarlethorizons/api/v1/me
POST /scarlethorizons/api/v1/logout
```

Administrator routes:

```text
POST  /scarlethorizons/api/v1/admin/character-accounts/import
GET   /scarlethorizons/api/v1/admin/character-accounts
POST  /scarlethorizons/api/v1/admin/character-accounts
PATCH /scarlethorizons/api/v1/admin/character-accounts/{account-id}
```

## Verification

- Health response reports schema version `2` and a nonzero `character_account_count`.
- Successful login sets `pa_character_session` with `Secure`, `HttpOnly`, `SameSite=Strict`, and path `/scarlethorizons/api/`.
- `GET /v1/me` returns the logged-in account's server-stored character key.
- Logout without the CSRF token fails.
- Correct logout expires the session cookie.
- Disabled accounts and expired sessions fail closed.
- Protected responses include `Cache-Control: no-store`.
