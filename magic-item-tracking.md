# Magic Item Tracking

## Data sources

- Private source: a schema-v2 JSON file outside the public document root, configured as `magic_items.source_path` in the broker's private `config.php`.
- Public fallback and tracked source file: `pwa\magic-items.json`; it may contain only records whose `viewable-by` value is `all`.
- Development output: `Release\magic-items.json`, beside `player-assistant.exe`.
- Published and installed output: `Release\publish\magic-items.json`, beside `player-assistant.exe`.
- The PWA deployment may include only the public fallback `magic-items.json` in the PWA root.

The authenticated PWA requests `GET /v1/magic-items`. The broker filters the private source by the immutable authenticated account ID and returns only public records or records explicitly assigned to that exact ID. The browser never authorizes protected records from display names, first names, substring matches, or the public fallback.

## JSON schemas

The public fallback uses schema version 1 with `source` describing its campaign provenance. Every public item contains:

- `name`
- `description`
- `date-acquired`
- `meta-date-acquired`
- `longevity`: `one-shot`, `limited-use`, or `permanent`
- `provenance`
- `whereabouts`: a PC, NPC, location, or `lost`
- `viewable-by`: `all` only

The private broker source uses schema version 2 with the same item fields. Its `viewable-by` value is a comma-separated list containing `all` or lowercase 32-character canonical account IDs. Names and inferred aliases are invalid private authorization viewers. Broker responses replace the private viewer metadata with `all` after filtering.

Keep the public fallback free of restricted records. Do not copy the private source into the public PWA tree, service-worker cache, installer payload, or public wiki output.
