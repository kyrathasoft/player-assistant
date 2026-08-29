# Persistent refresh inventory

This inventory records the persistent refresh paths that exist in this repository and the transaction boundary used by this backlog item.

## Covered

- **XP award-history and cumulative progression state** — `web-deploy/player-assistant-broker/XpTrackingService.php` refreshes configured progression JSON files plus `.xp-award-state.json`. It uses the `.xp-refresh.lock` collection lock, durable staged files, rollback copies, `.xp-award-transaction.json`, directory synchronization where supported, and recovery before reads. The journal is the commit boundary: a surviving journal is rolled back; only a fully promoted, synchronized generation removes the journal. Existing `web-deploy/tests/xp-tracking-tests.php` covers stale valid sources, equal-date events, reset/decrease rejection, detached state, partial promotion, interrupted recovery, replay cleanup, and lock contention.
- **Word-count snapshot** — `WordCountService` persists one logical snapshot in the `word_count_snapshots` SQLite row, not several files. It now serializes writes with SQLite `BEGIN IMMEDIATE` and a per-collection lock (`refresh_lock_path`, or the status path plus `.lock`). The observed source timestamp is the generation token; older and equal generations are no-ops, while a newer generation may legitimately decrease counts. `web-deploy/tests/persistent-refresh-tests.php` covers stale-but-valid, equal-date, and newer reset/decrease behavior.

## Not multi-file refresh transactions in this repository

- **Lexicons** — the canonical desktop lexicons are embedded/generated artifacts. PWA optional lexicons are content-addressed Cache Storage entries and compiled IndexedDB records keyed by schema/language/content hash; they are independently validated and promoted, not one multi-file logical snapshot. Existing `pwa/translator-worker-tests.mjs`, `pwa/service-worker-tests.mjs`, and `verify-lexicon-artifacts.py` cover their cache/content-addressed contracts.
- **Runtime caches** — service-worker shell/data caches are separate cache collections with validated per-entry writes and failed-install cleanup. They do not participate in the XP or word-count refresh transaction and have no shared generation spanning those resources. Existing service-worker tests cover partial install/quota/corruption and obsolete-worker behavior.
- **RPOL/public downloaded artifacts** — these are independent single-file caches or deployment transactions, covered by their own deployment/recovery workflows; no repository path combines them with XP, word-count, or lexicon files into one refresh snapshot.

No security boundary is widened by the word-count change: source validation, signature validation, authenticated route handling, and response shaping remain unchanged.
