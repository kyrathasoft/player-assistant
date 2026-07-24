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

The generated root-level `campaign-search.json` contains only sitemap-listed public pages. Run `build-data.ps1 -RefreshCampaignSearch` when language data and the live campaign index should be refreshed together.

Validate the complete deployable directory with:

```powershell
.\pwa\verify-pwa.ps1
```

## Deploy

Upload the complete contents of `pwa/` to an HTTPS directory such as:

```text
https://bryanmiller.us/pwa/
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
