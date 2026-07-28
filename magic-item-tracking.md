# Magic Item Tracking

## Data sources

- Preferred source: [Kirkilston Crew Magic Items](https://publish.obsidian.md/scarlethorizons/Magic+Items/Kirkilston+Crew+Magic+Items) on the Scarlet Horizons Obsidian wiki.
- Offline fallback and tracked source file: `pwa\magic-items.json`.
- Development output: `Release\magic-items.json`, beside `player-assistant.exe`.
- Published and installed output: `Release\publish\magic-items.json`, beside `player-assistant.exe`.
- The PWA deployment must include `magic-items.json` in the PWA root.

The PWA attempts to load the wiki index and its linked magic-item pages first. If the wiki is unavailable or returns invalid data, it displays the bundled JSON fallback.

## JSON schema

The root object uses `schema_version`, `source`, and an `items` array. Every item contains:

- `name`
- `description`
- `date-acquired`
- `meta-date-acquired`
- `longevity`: `one-shot`, `limited-use`, or `permanent`
- `provenance`
- `whereabouts`: a PC, NPC, location, or `lost`
- `viewable-by`: a comma-separated audience list; defaults to `all`. An item is
  displayed when this field contains `all`, or when it contains the logged-in
  PC's canonical name as a case-insensitive substring.

Keep the fallback synchronized with the wiki. The Release build copies the tracked PWA file beside the app executable, and publishing carries it into the installer payload as a critical file.

`viewable-by` controls PWA display only. The fallback JSON and public wiki pages
remain publicly retrievable and must not contain secrets.
