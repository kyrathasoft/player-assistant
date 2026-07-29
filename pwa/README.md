# Player Assistant progressive web app

`pwa/` is a static, installable web version of Player Assistant. It provides:

- English ↔ Orcish and English ↔ Elvish translation in a background worker
- live translation while typing or pasting
- forward-translation export with UTF-8 byte counts in the filename
- full-text public campaign-vault search across page titles and Markdown content
- whole-word search by default with explicit suffix (`*king`), prefix (`king*`), and contains (`*king*`) wildcards
- a cryptographically unbiased dice roller with local history
- public Scarlet Horizons campaign links
- responsive phone, tablet, Chromebook, and desktop layouts
- an offline app shell and runtime-cached translator/search data
- server-validated character login with secure cookie sessions and explicit logout
- the logged-in player's active hero token, sourced from the public Player Characters Listing, or the Dungeon Master token for the DM account; wiki images are preferred with website-hosted fallbacks, and player tokens link to their wiki pages with hover guidance
- a protected current-XP card that returns one authorized character to players, calculates XP till next level (TNL) from the published class progression pages, and exposes party totals only to the Dungeon Master
- a protected Quests dashboard with validated visibility and lifecycle statuses; quests are ordered Active, Available, Available (Abandoned), Completed, then Withdrawn, with an indented sidebar control that cycles through the states currently represented
- a Dungeon Master-only list of other users, marking those active in the PWA within the last two minutes and showing the last login date/time for inactive accounts; player heartbeat responses never expose other accounts

No RPOL password, XP password, password hash, encrypted local setting, session identifier, or private player note is embedded in the PWA. Character credentials are sent only to the same-origin PHP broker over HTTPS. The broker validates the password server-side and keeps the session identifier in a `Secure`, `HttpOnly`, `SameSite=Strict` cookie.

## Build the static data

From the repository root:

```powershell
.\pwa\build-data.ps1
```

The generator compacts the reviewed Orcish and Elvish artifacts into browser-oriented dictionaries, validates the current full-text campaign-search snapshot, and produces the required 192px and 512px install icons from the existing dragon artwork.

Refresh the full-text campaign search index from the live public Obsidian Publish sitemap and Markdown endpoints with:

```powershell
.\pwa\refresh-campaign-search.ps1
```

The generated root-level `campaign-search.json` contains only sitemap-listed public pages and excludes the protected XP Tracking source. Run `build-data.ps1 -RefreshCampaignSearch` when language data and the live campaign index should be refreshed together.

Check the active hero and Dungeon Master token images on the wiki and update only changed website-hosted fallback copies with:

```powershell
.\pwa\refresh-hero-tokens.ps1
```

Run `build-data.ps1 -RefreshHeroTokens` when language data and hero tokens should be refreshed together. The generated manifest keeps both the current wiki image URL and the local `pwa/data/hero-tokens/` fallback. At runtime, the PWA tries the wiki image first and automatically falls back to the website copy if the wiki is unavailable. The Dungeon Master entry remains pinned to its locally approved image because the published wiki asset currently contains the wrong portrait.

Validate the complete deployable directory with:

```powershell
.\pwa\verify-pwa.ps1
```

Test the deployed PWA against the reviewed local runtime files with:

```powershell
.\pwa\test-deployment.ps1
```

After the current-XP broker update is deployed and configured, include its anonymous-access and readiness checks with:

```powershell
.\pwa\test-deployment.ps1 -RequireCurrentXpApi
```

## Deploy

Upload the complete contents of `pwa/` to an HTTPS directory such as:

```text
https://bryanmiller.us/scarlethorizons/pwa/
```

Keep the directory structure unchanged. The web server must serve:

- `.webmanifest` as `application/manifest+json` or `application/json`
- `.js` as JavaScript
- `.json` as JSON
- `.png` as PNG

Character login additionally requires the PHP broker files under `web-deploy/` to be deployed at `/scarlethorizons/api/v1/`. Import the existing private XP password hashes after deploying the broker update:

```powershell
.\web-deploy\import-character-accounts.ps1
```

The import sends only the existing salted PBKDF2 hashes through the administrator-protected HTTPS endpoint. On a character's first successful login, the broker replaces that legacy hash with PHP's current native password-hash format.

Current XP is loaded through the protected same-origin `GET /scarlethorizons/api/v1/xp` route. The PHP broker fetches the fixed Obsidian Publish XP page, validates the latest markdown table, resolves each character's class through the published Class Level Progression index, and subtracts current XP from the next-level threshold to calculate TNL. Players never receive other characters' totals or the configured source URLs. The Dungeon Master role receives the validated current party table.

Quest records are defined in root-level `quests.json`, beside
`magic-items.json`. The protected same-origin
`GET /scarlethorizons/api/v1/quests` route reads and validates that file,
applies `gated-by` visibility for the authenticated account, and returns only
authorized records. Because `quests.json` is publicly deployed, its gates
control PWA display but do not make the source data confidential.
The `available (abandoned)` state means that a quest has been abandoned at
least once and is currently available.

Class XP thresholds are defined in root-level `level-progression.json`, beside
`magic-items.json` and `quests.json`. It contains the six classes linked from
the published Class Level Progression index, with level and minimum-XP entries
from levels 1 through 36.

The included `.htaccess` supplies the important Apache MIME and cache headers when overrides are enabled. The PWA also works on another HTTPS static host with equivalent server configuration.

After loading the secure URL, supported browsers expose an install prompt. The always-visible **Install app** button invokes the browser prompt when available and otherwise shows platform-appropriate installation instructions.

## Caching

The service worker immediately caches only the app shell. Large translator dictionaries are fetched and cached the first time they are prepared. This keeps the initial install responsive while allowing previously loaded languages to work offline.

When publishing a new PWA release, change `CACHE_VERSION` in `service-worker.js` so clients retire old cached files. Shell installation uses `cache: 'reload'` so a new service worker cannot accidentally repopulate its cache from long-lived stale browser responses.

## Local smoke test

Serve the directory over HTTP on localhost; do not open `index.html` directly with a `file://` URL:

```powershell
python -m http.server 8765 --directory .\pwa
```

Then open `http://127.0.0.1:8765/`.
