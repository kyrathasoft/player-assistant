# To Do

## Security and delivery

- [x] Remove legacy XP histories from the current public PWA and repository tree.
  - [x] Production `pwa/XP/` was removed; XP histories now live outside the web root.
  - [x] The PWA loads authorized histories through authenticated `GET /v1/xp-awards` requests.
  - [x] Anonymous legacy XP requests return `404`, and the service worker no longer caches `/XP/` paths.
  - [x] Tracked deletions were committed and pushed; Git-history purging was intentionally deferred.
- [x] Make the full regression suite a required CI gate.
  - [x] Run all 405 desktop tests instead of only focused filters.
  - [x] Run `pwa/verify-pwa.ps1` and the PHP broker suites.
  - [x] Add browser-level PWA smoke tests for authentication, translation, navigation, and offline startup.
- [x] Replace stale hard-coded quest-count expectations in PHP broker and HTTP tests with fixture-derived expectations.
- [x] Make PWA deployment release-atomic and self-verifying.
  - [x] Remove newly introduced files during rollback when a later deployment step fails.
  - [x] Verify public HTTPS hashes, headers, and API behavior before declaring deployment success.
- [ ] Add broker database recovery and observability operations.
  - [x] Schedule consistent `broker.sqlite` backups and configure verified FTPS off-host retention.
  - [x] Run regular `PRAGMA integrity_check` checks and test restoration.
  - [x] Implement health, word-count refresh, and repeated-server-error alerting with cooldowns; production `alert_email` still requires a configured recipient.
- [x] Add ordered SQLite migrations using `PRAGMA user_version`, transactions, pre-migration backups, and upgrade fixtures.
- [ ] Harden the release supply chain.
  - Pin GitHub Actions and installer dependencies to immutable versions or hashes.
  - Enable NuGet locked restore and dependency scanning.
  - [x] Move generated recovery archives out of the tracked source tree.

## Architecture and maintainability

- [ ] Decompose `Form1` into feature controllers or presenters with injected services.
- [ ] Split the custom 405-test harness into discoverable domain-focused test classes.
- [ ] Modularize `pwa/app.js` by feature without introducing an unnecessary framework.
- [ ] Make schema-rich lexicon data the canonical source for desktop, PWA, and web-translator artifacts.
- [ ] Centralize desktop, installer, PWA, and cache version metadata.
- [ ] Add formatting verification to CI and fix the existing `RpolAuthUtility.cs` formatting violation.
- [ ] Formalize repository hygiene for local corpus directories and `.hermes-tmp*` files.

## Completed

- [x] Add private cron refresh observability and safe broker health fields.
- [x] Add publisher transaction and rollback tests.
- [x] Add idempotent private broker deployment automation.
- [x] Add production deployment drift detection.
- [x] Sign and verify the canonical word-count source with Ed25519.
- [x] Add exact-pattern production backup retention.

Completed work is preserved in [to-do-archive-2026-07-30.md](to-do-archive-2026-07-30.md).
