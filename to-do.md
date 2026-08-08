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
- [x] Add broker database recovery and observability operations.
  - [x] Schedule consistent `broker.sqlite` backups and configure verified FTPS off-host retention.
  - [x] Run regular `PRAGMA integrity_check` checks and test restoration.
  - [x] Implement health, word-count refresh, and repeated-server-error alerting with cooldowns and a configured production recipient.
- [x] Add ordered SQLite migrations using `PRAGMA user_version`, transactions, pre-migration backups, and upgrade fixtures.
- [x] Harden the release supply chain.
  - [x] Pin GitHub Actions to immutable versions or hashes.
  - [x] Enable NuGet locked restore and dependency scanning.
  - [x] Move generated recovery archives out of the tracked source tree.

## Architecture and maintainability

- [x] Decompose `Form1` into feature controllers or presenters with injected services.
- [x] **Split the custom regression harness into discoverable domain-focused test classes.**
  - The 407-test catalog now delegates to partial application, campaign, release, shared, and translator test classes while preserving name-based filtering and failure aggregation.
  - `verify-test-harness-structure.ps1` enforces catalog uniqueness, domain file presence, and the runner/catalog boundary in PR smoke and full regression CI.
- [x] Modularize `pwa/app.js` by feature without introducing an unnecessary framework.
- [x] Make schema-rich lexicon data the canonical source for desktop, PWA, and web-translator artifacts.
- [x] Centralize desktop, installer, PWA, and cache version metadata.
- [x] Add formatting verification to CI and fix existing .NET formatting violations.
- [x] Formalize repository hygiene for local corpus directories and `.hermes-tmp*` files.

## PWA hardening backlog

- [ ] Strengthen PWA runtime resilience and coverage.
  - [ ] Add authenticated browser coverage for XP Awards, current XP, quests, messages, and party funds, including failed login, logout, account switching, expired sessions, and cross-account denial.
  - [ ] Add an XP regression test proving that a newly published cumulative total produces exactly one award with the correct date and delta, without duplication on subsequent refreshes.
  - [ ] Add freshness timestamps, stale/fallback indicators, and explicit retry or refresh controls for protected data views.
  - [ ] Test service-worker install failures, quota exhaustion, corrupted caches, interrupted updates, and obsolete-worker protection; add an in-app update-available prompt.
  - [ ] Add automated accessibility coverage for focus trapping/restoration, keyboard navigation, visible focus, labels, table semantics, reduced motion, larger text, and narrow mobile layouts.
  - [ ] Review and strengthen production security headers, including `object-src`, `frame-src`, `upgrade-insecure-requests`, and HSTS options after subdomain verification.
  - [ ] Extend integrity and hash-pinned validation to additional generated public data, including timestamps, source hashes, and declared record counts.
  - [ ] Add synthetic monitoring for anonymous API denial, authorized response shape, protected-cache exclusion, asset versions, service-worker activation, and stale broker/source conditions.
  - [ ] Measure and improve mobile startup, install, first-interaction, and campaign-search performance; consider lazy-loading or indexing large search data.

## Completed

- [x] Add private cron refresh observability and safe broker health fields.
- [x] Add publisher transaction and rollback tests.
- [x] Add idempotent private broker deployment automation.
- [x] Add production deployment drift detection.
- [x] Sign and verify the canonical word-count source with Ed25519.
- [x] Add exact-pattern production backup retention.

Completed work is preserved in [to-do-archive-2026-07-30.md](to-do-archive-2026-07-30.md).
