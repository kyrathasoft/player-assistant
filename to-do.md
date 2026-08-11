# To Do

## Security and delivery

- [x] Remove legacy XP histories from the current public PWA and repository tree.
  - [x] Production `pwa/XP/` was removed; XP histories now live outside the web root.
  - [x] The PWA loads authorized histories through authenticated `GET /v1/xp-awards` requests.
  - [x] Anonymous legacy XP requests return `404`, and the service worker no longer caches `/XP/` paths.
  - [x] Tracked deletions were committed and pushed; Git-history purging was intentionally deferred.
- [x] Make the full regression suite a required CI gate.
  - [x] Run all 435 desktop tests instead of only focused filters.
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
- [x] Try RPOL snapshot creation again and resolve the browser-worker failure.
  - [x] Make the WinForms publisher connect reliably to its temporary Chrome/Edge CDP session.
  - [x] Publish and verify at least one fresh snapshot before relying on the broker freshness cron.
- [x] Schedule and deploy the PWA campaign word-count refresh.
  - [x] Refresh `pwa/campaign-search.json` every Friday at 07:00 Central time with payload integrity checks.
  - [x] Deploy the refreshed public index through the protected DreamHost SSH workflow using the `DREAMHOST_SSH_PRIVATE_KEY` repository secret.
  - [x] Verify the live artifact with SHA-256 and run the production PWA deployment verifier.

## Architecture and maintainability

- [x] Decompose `Form1` into feature controllers or presenters with injected services.
- [x] **Split the custom regression harness into discoverable domain-focused test classes.**
  - The 435-test catalog now delegates to partial application, campaign, release, shared, and translator test classes while preserving name-based filtering and failure aggregation.
  - `verify-test-harness-structure.ps1` enforces catalog uniqueness, domain file presence, and the runner/catalog boundary in PR smoke and full regression CI.
- [x] Modularize `pwa/app.js` by feature without introducing an unnecessary framework.
- [x] Make schema-rich lexicon data the canonical source for desktop, PWA, and web-translator artifacts.
- [x] Centralize desktop, installer, PWA, and cache version metadata.
- [x] Add formatting verification to CI and fix existing .NET formatting violations.
- [x] Formalize repository hygiene for local corpus directories and `.hermes-tmp*` files.

## PWA hardening backlog

- [ ] Strengthen PWA runtime resilience and coverage.
  - [x] Authenticated browser coverage covers XP Awards, current XP, quests, messages, party funds, failed login, logout, account switching, expired sessions, and cross-account denial.
  - [x] The browser fixture proves that a newly published cumulative total produces exactly one XP award with the correct date and delta, without duplication on subsequent refreshes.
  - [x] Protected views expose local freshness timestamps and explicit retry or refresh controls; broker stale/fallback metadata and full message/quest coverage remain.
  - [x] Service-worker installation cleans up failed partial caches, handles quota failures, validates corrupted cache entries, protects newer workers from obsolete activation, and has failure-injection coverage.
  - [x] Browser smoke covers dialog focus containment/restoration, accessible names, visible focus contracts, table-backed protected data, reduced motion, and narrow mobile layouts.
  - [x] Production security headers now include `object-src`, `frame-src`, and `upgrade-insecure-requests`; host-level HSTS remains enabled pending any future verified subdomain expansion.
  - [x] Static PWA verification validates generated data schemas, source URLs, record counts, token hashes, cache revisions, and deployment parity.
  - [x] Deployment verification and the scheduled monitor provide production coverage for anonymous API denial, asset parity, security/cache headers, and public runtime files; the live PWA deployment passed SHA-256 verification.
  - [ ] Add explicit production detection for stale broker/source conditions and authorized protected-response shape.
    - [x] The scheduled monitor now authenticates, validates login/identity plus XP and word-count contracts, rejects stale XP/source/broker timestamps, and fails closed when monitor credentials are absent.
    - [x] Focused contract tests, canonical PWA verification, HTTP authentication tests, browser/service-worker smoke tests, parser and CI-policy checks, secret scanning, and an independent fail-closed review all pass.
    - [ ] Configure `PWA_MONITOR_CHARACTER_NAME` and `PWA_MONITOR_PASSWORD` for a dedicated production monitor account, then verify the first authenticated live run.
  - [x] Startup timing and 320px narrow-layout overflow are enforced by browser smoke; campaign search and large dictionaries remain demand-loaded/cached.

## PWA performance ordering

- [x] Step 1: Reduce campaign-search work without changing behavior.
  - Cache compiled search expressions with a bounded cache.
  - Precompute each entry's normalized title/content combination when the corpus is loaded.
  - Add focused expression-cache coverage to the PWA test command.
- [ ] Step 2: Move campaign search loading, normalization, scoring, and sorting into a dedicated Web Worker.
  - [x] Initial worker vertical slice now owns corpus loading, normalization, scoring, sorting, and bounded expression caching.
  - [x] Use request IDs so stale worker responses cannot overwrite newer queries.
  - [x] Keep snippets and DOM rendering on the main thread.
  - [ ] Add browser smoke coverage for the worker path.
- [ ] Step 3: Add a build-time exact-term inverted index consumed by the search worker.
  - Intersect candidate page IDs for ordinary multi-term searches.
  - Preserve wildcard searches through a worker-side fallback path.
  - Verify generated counts, schema, and behavior parity against the current corpus.
- [ ] Step 4: Optimize lexicon construction while preserving background preload.
  - Generate reverse maximum-phrase metadata and avoid the second reverse-map scan.
  - Benchmark JSON parsing, normalization, Map construction, and translation readiness before changing data format.
- [ ] Step 5: Improve perceived lexicon readiness by preloading the last-used language after first paint.
  - Keep the current default-language preload when no preference exists.
  - Do not preload all three large dictionaries.
- [ ] Step 6: Evaluate persisted compiled lexicons with IndexedDB only if startup benchmarks justify the added invalidation/storage complexity.
  - Version the cache from the generated lexicon schema/hash.
  - Compare cold-load, repeat-load, memory, and first-translation timings before adoption.

## Completed

- [x] Schedule fail-closed full wiki and local IC/OOC recounts at 4:00 AM and 8:30 PM Central with authenticated publication verification.
- [x] Add private cron refresh observability and safe broker health fields.
- [x] Add publisher transaction and rollback tests.
- [x] Add idempotent private broker deployment automation.
- [x] Add production deployment drift detection.
- [x] Sign and verify the canonical word-count source with Ed25519.
- [x] Add exact-pattern production backup retention.

Completed work is preserved in [to-do-archive-2026-07-30.md](to-do-archive-2026-07-30.md).
