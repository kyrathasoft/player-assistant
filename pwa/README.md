# Player Assistant progressive web app

`pwa/` is a static, installable web version of Player Assistant. It provides:

- English ↔ Orcish and English ↔ Elvish translation in a background worker
- live translation while typing or pasting
- forward-translation export with UTF-8 byte counts in the filename
- public campaign-vault search
- a cryptographically unbiased dice roller with local history
- public Scarlet Horizons campaign links
- responsive phone, tablet, Chromebook, and desktop layouts
- an offline app shell and runtime-cached translator/search data

No RPOL password, XP password, encrypted local setting, authenticated browser state, or private player note is included. Those features require a separately secured server API.

## Build the static data

From the repository root:

```powershell
.\pwa\build-data.ps1
```

The generator compacts the reviewed Orcish and Elvish artifacts into browser-oriented dictionaries and copies the tracked public campaign-search snapshot. It also produces the required 192px and 512px install icons from the existing dragon artwork.

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

The included `.htaccess` supplies the important Apache MIME and cache headers when overrides are enabled. The PWA also works on another HTTPS static host with equivalent server configuration.

After loading the secure URL, supported browsers expose an install prompt. The always-visible **Install app** button invokes the browser prompt when available and otherwise shows platform-appropriate installation instructions.

## Caching

The service worker immediately caches only the app shell. Large translator dictionaries are fetched and cached the first time they are prepared. This keeps the initial install responsive while allowing previously loaded languages to work offline.

When publishing a new PWA release, change `CACHE_VERSION` in `service-worker.js` so clients retire old cached files.

## Local smoke test

Serve the directory over HTTP on localhost; do not open `index.html` directly with a `file://` URL:

```powershell
python -m http.server 8765 --directory .\pwa
```

Then open `http://127.0.0.1:8765/`.
