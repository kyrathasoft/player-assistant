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

- [x] Strengthen PWA runtime resilience and coverage.
  - [x] Authenticated browser coverage covers XP Awards, current XP, quests, messages, party funds, failed login, logout, account switching, expired sessions, and cross-account denial.
  - [x] The browser fixture proves that a newly published cumulative total produces exactly one XP award with the correct date and delta, without duplication on subsequent refreshes.
  - [x] Protected views expose local freshness timestamps and explicit retry or refresh controls; broker stale/fallback metadata and full message/quest coverage remain.
  - [x] Service-worker installation cleans up failed partial caches, handles quota failures, validates corrupted cache entries, protects newer workers from obsolete activation, and has failure-injection coverage.
  - [x] Browser smoke covers dialog focus containment/restoration, accessible names, visible focus contracts, table-backed protected data, reduced motion, and narrow mobile layouts.
  - [x] Production security headers now include `object-src`, `frame-src`, and `upgrade-insecure-requests`; host-level HSTS remains enabled pending any future verified subdomain expansion.
  - [x] Static PWA verification validates generated data schemas, source URLs, record counts, token hashes, cache revisions, and deployment parity.
  - [x] Deployment verification and the scheduled monitor provide production coverage for anonymous API denial, asset parity, security/cache headers, and public runtime files; the live PWA deployment passed SHA-256 verification.
  - [x] Add explicit production detection for stale broker/source conditions and authorized protected-response shape.
    - [x] The scheduled monitor now authenticates, validates login/identity plus XP and word-count contracts, rejects stale XP/source/broker timestamps, and fails closed when monitor credentials are absent.
    - [x] Focused contract tests, canonical PWA verification, HTTP authentication tests, browser/service-worker smoke tests, parser and CI-policy checks, secret scanning, and an independent fail-closed review all pass.
    - [x] Configure `PWA_MONITOR_CHARACTER_NAME` and `PWA_MONITOR_PASSWORD` for a dedicated production monitor account, then verify the first authenticated live run.
      - Repository secrets are configured. The authenticated monitor now runs every 15 minutes from private DreamHost cron because DreamHost rejects GitHub-hosted Azure runners before authentication; it persists private status, alerts on failure/recovery with cooldown, and exposes only sanitized state through admin health.
  - [x] Startup timing and 320px narrow-layout overflow are enforced by browser smoke; campaign search and large dictionaries remain demand-loaded/cached.

## High-Priority-Fixes

Cross-cutting reliability, concurrency, API, and identity corrections to implement as a coordinated backlog.

- [x] Service worker: validate network responses before returning them, treat 404/503/wrong-MIME/captive-portal/corrupt-JSON responses as network failures when a valid cached copy exists, and add a bounded navigation timeout.
- [x] Offline party funds: place `party-funds.json` in the data cache and route its fallback through the installed data-cache path.
- [x] Messages: add server-side pagination, unread counts, cursor navigation, and retention so responses remain valid beyond 200 unread messages.
  - [x] The broker returns stable keyset-cursor pages of up to 100 unread messages with a total unread count and bounded cursor validation.
  - [x] The PWA displays the total count and lets users load older unread pages without discarding an already loaded page on continuation failure.
  - [x] Read-message retention is bounded by age and per-recipient count; unread messages are never pruned.
- [x] Stale data: add an Activity/Inbox view and a lightweight revisions endpoint with visibility-aware polling so open PWAs discover new messages and quest decisions.
  - [x] The authenticated `/v1/revisions` response exposes only account-scoped message and quest revision tokens plus activity counts.
  - [x] Polling runs only while the authenticated PWA is visible and online; changed tokens refresh the existing protected message or quest response.
- [x] PHP concurrency: resolve and copy the authenticated identity, call `session_write_close()`, then perform read-only XP, word-count, quest, revision, and message work.
  - [x] The API entry point provides a session-release callback; BrokerService closes the session after authorization and before slow read-only service work.
  - [x] Routing regression coverage proves the XP path releases the session lock.
- [x] Broker startup: move migrations to deployment and lazily instantiate only the requested service; keep `/health` free of unnecessary SQLite, schema, and subsystem startup work.
  - [x] Added deployment-time `migrate-broker.php`; request startup now verifies the schema version instead of applying migrations.
  - [x] Broker services are lazy factories, and public `/v1/health` exits before broker subsystem loading or database access.
  - [x] Removed request-time schema creation from broker service constructors and wired deployment migration execution into the release script.
- [x] Login hardening: rate-limit primarily by account-plus-source, retain a stronger address throttle, and use progressive delays instead of globally locking a known character name for everyone.
  - [x] Account-source failures now use an exponentially increasing bounded delay; a different source can still authenticate the same account.
  - [x] A separate higher-threshold address scope limits distributed account-name attempts without being cleared by one successful login.
  - [x] Deterministic clock-driven coverage proves account isolation, progressive expiry, and address-throttle recovery.
- [x] Identity schema: audit existing data, add a unique `character_key` constraint, use opaque IDs for authorization, and retain aliases only as login convenience.
  - [x] Production audit found 7 enabled accounts, no duplicate `character_key` values, and opaque 32-character account IDs.
  - [x] Migration v4 adds the unique index, normalized alias table, collision triggers, and legacy alias backfill.
  - [x] Authentication resolves aliases to canonical opaque account IDs; authorization continues from session account IDs rather than aliases.
- [x] API client: add an `AbortController`-based timeout/cancellation layer, typed structured errors, request IDs, centralized expired-session handling, idempotency keys, and generation guards.
  - [x] Authenticated requests use bounded timeout/abort signals, request IDs, structured error codes, status, retryability, and server request-ID propagation.
  - [x] Identity transitions and expired 401 responses cancel in-flight requests and invalidate stale generations.
  - [x] Mutating authenticated requests send idempotency keys; browser smoke verifies request and mutation headers.
- [x] Presence: restrict polling to the DM presence view, pause it while hidden or offline, and derive activity from useful requests where possible instead of polling every player every 30 seconds.
  - [x] Presence requests now run only for Dungeon Masters on the dashboard presence view.
  - [x] Visibility and online-state changes cancel/clear presence refreshes; no background interval remains.
  - [x] Existing revision requests refresh the DM presence snapshot, while browser smoke covers player suppression and hidden-state pause.

## PWA performance ordering

- [x] Step 1: Reduce campaign-search work without changing behavior.
  - Cache compiled search expressions with a bounded cache.
  - Precompute each entry's normalized title/content combination when the corpus is loaded.
  - Add focused expression-cache coverage to the PWA test command.
- [x] Step 2: Move campaign search loading, normalization, scoring, and sorting into a dedicated Web Worker.
  - [x] Initial worker vertical slice now owns corpus loading, normalization, scoring, sorting, and bounded expression caching.
  - [x] Use request IDs so stale worker responses cannot overwrite newer queries.
  - [x] Keep snippets and DOM rendering on the main thread.
  - [x] Browser smoke verifies the worker starts, returns online results, and is available from the offline shell cache.
- [x] Step 3: Add a build-time exact-term inverted index consumed by the search worker.
  - [x] Generate normalized exact-term postings for title/content pages, including hyphen/apostrophe component terms for behavior compatibility.
  - [x] Intersect candidate page IDs for ordinary multi-term searches.
  - [x] Preserve wildcard searches through a worker-side full-corpus fallback path.
  - [x] Validate generated schema, page-ID bounds, sorted unique postings, complete term coverage, and behavior parity against the current corpus.
  - [x] Bump the PWA cache revision because the generated campaign-search payload and worker contract changed.
- [x] Step 4: Optimize lexicon construction while preserving background preload.
  - [x] Generate reverse maximum-phrase metadata and use it to avoid the redundant reverse-map scan.
  - [x] Validate the declared reverse maximum against generated translation values for all three lexicons.
  - [x] Benchmark JSON parsing, normalization, Map construction, and translation readiness before adopting the metadata field; preserve a compatibility fallback for older payloads.
  - [x] Bump synchronized app/cache revisions and query-busted runtime references.
- [x] Step 5: Improve perceived lexicon readiness by preloading the last-used language after first paint.
  - [x] Persist only supported language selections and restore the preference on startup.
  - [x] Schedule exactly the restored/default language preload after the first paint; no preference still uses Orcish.
  - [x] Do not preload all three large dictionaries.
  - [x] Browser smoke verifies persistence, restoration, Elvish readiness, and switching back to Orcish.
- [x] Step 6: Evaluate persisted compiled lexicons with IndexedDB only if startup benchmarks justify the added invalidation/storage complexity.
  - [x] Add generated content hashes for each lexicon and use schema/hash/language cache keys for invalidation.
  - [x] Persist compiled forward/reverse Map entries in an optional IndexedDB store from the translator worker.
  - [x] Restore only matching compiled data; remove obsolete same-language hashes when a new version is written.
  - [x] Fail open to normal JSON parsing when IndexedDB is unavailable, corrupt, quota-limited, or otherwise fails.
  - [x] Real-browser benchmark: Orcish cold fetch/parse/map construction median 280.8 ms versus 44.6 ms IndexedDB restore, with a 42.5 ms initial write; the measured repeat-startup benefit justifies the bounded complexity.
  - [x] Browser smoke verifies the compiled Orcish record is written while translation behavior remains online/offline compatible.

## Improved-PWA

Planned correction for the PWA's approximately 10.1 MB optional-data installation cost. The service worker must install a small usable shell; translator and campaign-search packs must be independently downloadable, schema-validated, retriable, removable, and retained across app updates when their content hashes remain unchanged.

- [x] 1. Establish the optional-pack inventory and a red-capable regression harness.
  - Record the current pack sizes and declared records: Orcish 3.87 MB/80,874 terms, Elvish 2.23 MB/84,460 terms, Ghukliak 2.11 MB/81,204 terms, and campaign search 1.90 MB/1,055 pages.
  - Add static assertions that no large dictionary or complete campaign index appears in the service-worker install shell list.
  - Add an install fixture that proves the shell activates successfully when every optional pack is unavailable.
  - Add failure fixtures for truncated, wrong-MIME, invalid-schema, stale-hash, HTTP-error, timeout, and quota-exceeded pack downloads.
  - Keep the target measurable: initial install must not request the approximately 10.1 MB optional payload.

- [x] 2. Define a generated, content-addressed optional-pack manifest.
  - Generate one manifest entry per translator language and the campaign-search pack with URL, pack kind, schema version, content hash, byte size, record/page count, and validation metadata.
  - Hash the exact served bytes, not a source description or mutable timestamp.
  - Validate that declared counts, schema versions, and hashes match the generated files before packaging or deployment.
  - Make the manifest itself a small shell asset and version it independently from the application shell.
  - Add verifier coverage for missing, duplicate, mismatched, and unexpectedly large manifest entries.

- [x] 3. Reduce service-worker installation to the minimal shell.
  - Remove all three dictionaries and `campaign-search.json` from `OFFLINE_DATA_ASSETS` and any install-time `cacheAssets` call.
  - Keep only the shell, feature modules/workers, small required UI data, offline page, manifest, and icons in the install transaction.
  - Ensure one optional-pack failure cannot delete the shell cache or abort worker activation.
  - Preserve fail-closed validation for assets that remain mandatory for shell startup.
  - Bump centralized app/cache revisions and update the HTML, service-worker inventory, verifier, and deployment file manifest together.

- [x] 4. Implement an independent optional-pack download and validation controller.
  - Add a shared pack loader used by the translator worker and campaign-search worker.
  - Fetch one pack at a time with bounded retries and backoff for idempotent GETs; do not retry indefinitely or block shell activation.
  - Validate response status, MIME type, byte presence, content hash, schema, declared counts, and language/pack identity before storing.
  - Write to a temporary or isolated cache entry first, then promote atomically only after every validation succeeds.
  - Preserve the previous valid pack when a replacement fails; expose a retryable error state instead of deleting usable data.

- [x] 5. Create content-addressed, independently removable caches.
  - Store packs under cache keys containing pack kind, schema version, and content hash so an app-shell revision does not invalidate unchanged packs.
  - Retain valid packs across service-worker activation and app updates when the manifest hash still matches.
  - Remove obsolete hashes only after the new manifest is validated and no active client needs the old pack.
  - Add explicit per-pack removal controls and ensure removal does not delete the shell or unrelated packs.
  - Handle storage quota failures by preserving existing packs and returning a usable network result or clear retry state.

- [x] 6. Integrate translator readiness without restoring the install-time download.
  - Preload only the selected/default language after shell startup or on the first translation request, preserving the instant-response requirement after that pack is ready.
  - Keep Elvish and Ghukliak demand-loaded; do not download all three languages merely because the worker starts.
  - Report loading, ready, stale, unavailable, retrying, and removed states in the translator UI.
  - Ensure language switching cannot display a stale response from a previous language or identity of the pack.
  - Verify already-cached packs work offline while an uncached language reports an actionable unavailable state.

- [x] 7. Integrate campaign-search readiness without restoring the install-time download.
  - Demand-load the campaign-search pack when the user first opens or queries campaign search.
  - Keep search worker initialization independent from pack availability so the rest of the PWA remains usable.
  - Validate the pack before search begins and return a clear retryable search-unavailable state on failure.
  - Preserve the existing worker request-ID behavior and offline search for a previously retained valid pack.
  - Ensure a failed search-pack replacement never discards the last known-good index.

- [x] 8. Add service-worker lifecycle and cache-retention coverage.
  - Prove install and activation succeed with zero optional packs cached or reachable.
  - Prove each pack can be downloaded, validated, used offline, removed, retried, and independently replaced.
  - Prove an unchanged content hash survives an app-shell cache revision, while a changed hash creates a new pack entry and retires the old one safely.
  - Prove corrupt cached packs are deleted without damaging the shell or other packs.
  - Prove optional-pack network failures never delete the current shell or unrelated valid packs.

- [x] 9. Add browser, offline, and performance acceptance coverage.
  - Measure initial install requests, bytes, activation time, and first usable shell time; assert the optional 10.1 MB payload is absent from install.
  - Exercise first translation, language switching, first campaign search, retries, removal, offline reload, and shell navigation through the real browser.
  - Assert Cache Storage contains only requested/validated packs and that protected API responses never enter any pack cache.
  - Verify status/error messaging and accessible retry/remove controls at normal and narrow viewport sizes.
  - Keep full PWA smoke, service-worker failure injection, and CI policy checks synchronized with the new pack lifecycle.

- [x] 10. Correct documentation, deployment, and operational contracts.
  - Update `pwa/README.md` so it accurately describes shell-only installation and independently demand-loaded packs.
  - Update `verify-pwa.ps1`, `test-deployment.ps1`, service-worker inventories, generated-data validation, and release checklists for the manifest and pack caches.
  - Deploy the complete runtime slice, including the manifest, loader, workers, generated packs, and cache-busting metadata; do not deploy only the changed browser module.
  - Verify public HTTPS hashes, MIME/cache headers, pack manifest parity, anonymous protected-data denial, and representative online/offline pack behavior after deployment.
  - Record pack hashes and cache-generation evidence without exposing protected data or credentials.

## Fix-Same-First-Name-Flaw

Planned security correction: eliminate first-name equivalence from authentication and every authenticated character-data path. Authentication must return an immutable canonical account identity; a Boolean result is insufficient because later lookups can otherwise use the attacker's originally entered name.

- [x] 1. Create a deterministic regression harness before changing production code.
  - [x] Add two character fixtures with the same first name, distinct full names, canonical IDs, passwords, XP totals, party sheets, and hero-briefing data.
  - [x] Reproduce the current failure: Character A authenticates in the synthetic sidecar, then the legacy name-based XP lookup returns Character B's data.
  - [x] Add negative cases for an ambiguous first-name alias, an unknown canonical ID, a mismatched password, and a selected hero whose display name collides with another hero.
  - [x] Keep the fixture independent of real password hashes and never expose production credentials.

- [x] 2. Replace Boolean password validation with an immutable identity result.
  - [x] Introduce an immutable `XpAuthenticatedIdentity` containing a stable canonical account/character ID, canonical character name, explicit aliases, and the Dungeon Master/account scope needed by callers.
  - [x] Change `XpPasswordStoreUtility.ValidatePassword` to return that identity or a fail-closed invalid result, never `true`/`false` alone.
  - [x] Remove candidate enumeration across every stored account sharing a first name.
  - [x] Resolve only the exact canonical name or an explicitly declared alias; aliases remain empty until the versioned sidecar work in Step 3.
  - [x] Preserve constant-time password verification and ensure failed authentication does not leak which identity or alias matched.

- [x] 3. Make the password sidecar identity-addressable and reject ambiguous aliases at load time.
  - [x] Revise the sidecar schema to v2; v1 name-only sidecars are rejected, while explicit conversion paths emit v2.
  - [x] Store an immutable canonical ID, canonical name, password hash, and an explicit alias list per entry.
  - [x] Normalize names and aliases consistently for comparison while preserving display values.
  - [x] Reject duplicate canonical IDs, duplicate canonical names, aliases that collide across accounts, aliases that collide with another account's canonical name, blank/untrimmed aliases, and duplicate aliases within an entry.
  - [x] Treat first names as ordinary text unless deliberately listed as an alias; never infer them automatically.
  - [x] Update password-generation, import, sidecar validation, installer, release-sidecar, and runtime-sidecar contracts together.

- [x] 4. Thread the returned identity through Form1 and XP retrieval.
  - [x] Replace the entered `characterName` as the source of authorization state with the returned immutable identity.
  - [x] Make XP snapshot filtering accept the authenticated identity/canonical ID and resolve the exact authorized record; do not perform a second lookup from the originally entered name.
  - [x] Determine Dungeon Master scope from the returned identity, not from a case-insensitive name comparison.
  - [x] Update the required XP, optional XP, publisher-task, login, and error/status paths so valid authentication always carries the same identity object forward.
  - [x] Clear the identity on cancellation, failed authentication, feature completion, and account transition.

- [x] 5. Correct PartyHeroUtility identity handling.
  - [x] Add canonical identity data to the roster input and carry it into `PartyHeroSheet`; pass the authenticated identity to `WithVisibleXpTotals`.
  - [x] Replace first-name XP matching with exact, unique canonical-ID matching; missing or duplicate identities fail closed.
  - [x] Keep Dungeon Master XP visibility explicitly scope-based while still requiring canonical roster-to-XP matches.
  - [x] Derive new hero markdown paths from canonical IDs or full names; retain first-name paths only through a collision-checked migration fallback.
  - [x] Keep display aliases separate from authorization identity and add regression coverage for same-first-name heroes.

- [x] 6. Correct My Hero Briefing identity handling.
  - [x] Extend `MyHeroBriefingRequest` with the authenticated canonical identity and use it to resolve the authenticated hero exactly.
  - [x] Replace `FindHeroByNameOrFirstName` with stable-ID resolution; make Dungeon Master hero selection return a stable ID rather than an ambiguous display name.
  - [x] Make XP totals, hero cards, recent activity, response detection, encrypted-note access, and quick-link generation use the resolved identity and its explicit aliases.
  - [x] Remove automatic first-name aliases from `GetHeroAliases`; only use aliases declared by the identity registry, with ambiguity rejected before runtime.
  - [x] Ensure a same-first-name hero cannot inherit another hero's briefing, XP, posts, or encrypted-note access.

- [x] 7. Audit all remaining name-based identity comparisons and boundaries.
  - [x] Review Form1, XpTrackingUtility, PartyHeroUtility, MyHeroBriefingUtility, TaggedNoteCipherUtility, hero roster loaders, import scripts, and generated manifests.
  - [x] Classify each name use as display/search text or authorization identity.
  - [x] Replace authorization comparisons that use first names, inferred aliases, or user-entered names; retain display/search behavior only where it cannot grant access.
  - [x] Document intentional public/login aliases separately from protected account identity.

- [x] 8. Update tests and release/deployment contracts.
  - [x] Add unit/regression coverage for exact-ID success, same-first-name cross-password denial, ambiguous-alias load rejection, explicit unique-alias success, DM scope, party XP visibility, hero selection, briefing activity, encrypted-note access, and account switching.
  - [x] Add negative fixtures proving that a first-name-only input cannot authenticate or select a protected hero when multiple identities share it.
  - [x] Update custom regression-harness catalogs, sidecar schema validators, installer/runtime checks, migration tests, and release checklists.
  - [x] Verify that no protected lookup receives the originally entered name after authentication.

- [x] 9. Migrate, deploy, and verify the identity data atomically.
  - [x] Back up and migrate the password sidecar and any canonical identity/roster data with rollback support.
  - [x] Verify local hashes, release contents, and generated roster/hero paths before deployment.
  - [x] Run authorized positive tests for each identity plus anonymous, wrong-password, ambiguous-alias, cross-character, logout, and account-switch negative tests locally.
  - [x] Verify production behavior through the authenticated deployment contract: the authoritative seven-entry v2 sidecar was reconciled to production opaque account IDs, imported transactionally, and read back with exact IDs, canonical names, character keys, and enabled state.


## Code-review implementation backlog

- [x] Move restricted magic-item data behind canonical authenticated authorization.
  - [x] Stop publishing and precaching protected `viewable-by` records in the complete public `magic-items.json` payload.
  - [x] Filter restricted records in `/v1/magic-items` by immutable account ID; authorization rejects substring matches, names, and inferred first-name aliases.
  - [x] Add direct-fetch, same-first-name/sub-string-collision, anonymous, account-switch, offline-fallback, and authorized-owner regression coverage.
- [ ] Restore migration-only ownership of the broker schema.
  - Remove request-time `CREATE TABLE`, `CREATE INDEX`, and `ALTER TABLE` work from `BrokerService`, `CharacterAuthService`, `MessageService`, `QuestService`, and `XpTrackingService` constructors.
  - Make service startup verify the expected `PRAGMA user_version` and required objects without mutating SQLite; keep all schema changes in ordered deployment migrations.
  - Add regression coverage proving ordinary API requests cannot create or alter schema and fail closed with an actionable deployment error when migration is required.
- [ ] Reinstate the PWA's shell-only optional-pack installation contract.
  - Remove translator dictionaries and `campaign-search.json` from service-worker install-time caching so an unavailable optional pack cannot abort shell installation.
  - Preserve content-addressed optional-pack caches across compatible shell revisions and retire only obsolete pack hashes.
  - Add `optional-pack-tests.mjs` to the canonical `npm test`/CI path, replace its permissive source-regex assertion with an install-request/runtime assertion, and prove a fresh install makes zero optional-pack requests.
- [ ] Restore validated stale-cache fallback and bounded navigation timeouts in the service worker.
  - Treat non-OK, wrong-MIME, empty, and schema-invalid network responses as failures before returning them from navigation and data strategies.
  - Fall back to the last valid cached response for 404/5xx/captive-portal/corrupt responses, and abort navigation fetches after a bounded timeout.
  - Add service-worker fixtures for each invalid-response class plus timeout, cache preservation, and no-valid-cache failure behavior.
- [ ] Make PWA remote promotion ambiguity-safe.
  - Use one persistent SSH/SFTP transport and a single-attempt mutating install command; do not blindly retry promotion after an unknown remote result.
  - Persist a transaction ID and expose an idempotent status/recovery command so a dropped connection can distinguish not-started, promoted-pending-verification, verified, and rollback-required states.
  - Hold one remote per-target lock across install, verification, finalize, rollback, and cleanup so concurrent CI/manual releases cannot interleave.
  - Test connection loss after promotion, duplicate invocation, lock contention, verification failure, rollback, finalization failure, and cleanup recovery.
- [ ] Scope desktop network resilience by operation instead of blocking an entire host.
  - Key circuit-breaker state by request purpose and bounded endpoint class so two failures on one GET path cannot suppress unrelated features on the same origin for five minutes.
  - Honor bounded `Retry-After` guidance for 429/503 responses and add capped exponential backoff with jitter for transient transport failures.
  - Add deterministic clock/delay tests for endpoint isolation, half-open recovery, retry limits, cancellation, and diagnostics redaction.
- [ ] Make desktop update checks obey form lifetime and shutdown cancellation.
  - Pass the form-lifetime cancellation token through manifest fetch and installer download, and re-check lifetime after every await before prompting, touching controls, or launching the installer.
  - Ensure closing the form cancels verification/download and cannot launch an installer or report through disposed UI.
  - Add deterministic close-during-check, close-during-download, post-verification-close, and cancellation-cleanup tests.
- [ ] Make background-task shutdown awaitable and race-safe.
  - Cancel and drain supervised tasks before disposing cancellation sources or allowing the WinForms shutdown path to complete.
  - Prevent post-disposal logging, file writes, or UI callbacks while preserving duplicate-phase suppression and bounded shutdown behavior.
  - Add tests with non-cooperative, faulting, and cancellation-aware tasks to prove shutdown completes predictably without unobserved failures.

- [ ] Make Authenticode inspection genuinely bounded and deadlock-safe.
  - Drain PowerShell stdout and stderr concurrently instead of synchronously reading both streams before the timed wait.
  - Apply cancellation and the advertised timeout to the whole process lifetime; kill and await exit on timeout.
  - Add high-stderr, hung-process, cancellation, malformed-output, and cleanup regressions.
- [ ] Restore runtime backups atomically.
  - Copy and validate a selected backup through a same-directory temporary file, then promote it with `AtomicFileUtility` rather than overwriting the destination directly.
  - Revalidate the promoted artifact and add interruption, locked-destination, and failed-validation tests proving prior bytes remain recoverable.
- [ ] Let optional XP consumers fail closed without disabling their whole feature.
  - Classify malformed, schema-invalid, and unreadable XP sidecars as logged store-unavailable results for optional Party/My Hero Briefing enrichment.
  - Continue without protected XP while preserving strict failure for required authentication paths; add malformed and partial-deployment regressions.
- [x] Prevent startup session restoration from overwriting a newer login.
  - Capture the authentication generation before `/session`; ignore stale success and cancellation/error completion before changing account or protected snapshots.
  - Browser regression delays the anonymous startup session across a successful login and a failed `/me` refresh, proving the login remains active; the full smoke suite retains logout and account-switch coverage.
- [ ] Make optional-pack manifest retries recover after transient failure.
  - Evict rejected memoized manifest promises so explicit retry performs a fresh bounded fetch.
  - Add fail-then-success translator and campaign-search tests without recreating the page or worker.
- [ ] Recover the translator and campaign-search UI from worker failures.
  - Handle Worker `error` and `messageerror`, invalidate pending request IDs, clear loading state, and announce an actionable retry state.
  - Support bounded worker recreation and add load-failure, runtime-crash, serialization-failure, and successful-retry fixtures.
- [ ] Correct the party-funds offline cache key contract.
  - Make the service worker install/cache the same `data/party-funds.json` URL requested by the app, with one canonical generated source.
  - Add offline-first-load, update, corrupt-cache, and stale-copy parity tests.
- [ ] Make broker operations health freshness-aware.
  - Degrade health when maintenance, backup, integrity-check, or restore-test timestamps exceed configured maximum ages rather than trusting any historical success indefinitely.
  - Add disabled-cron, stale-backup, stale-restore-test, recent-success, and recovery tests.
- [ ] Apply broker alert cooldown only after confirmed delivery.
  - Record failed delivery attempts separately and do not update `alert_last_sent_unix` when `mail()` fails.
  - Add immediate-retry-after-failure, successful cooldown, repeated failure, and recovery-notification tests.
- [ ] Harden the private broker backup credential-file boundary.
  - Reject symlinks, canonicalize against the approved private path, and require owner-only permissions before loading FTPS credentials or encryption keys.
  - Add symlink, permissive-mode, wrong-owner/path, missing-file, and valid-private-file tests.
- [ ] Make focused desktop test filters fail when they select zero tests.
  - Return nonzero for a non-empty unmatched filter and report the filter plus selected count; preserve unfiltered behavior.
  - Add typo, exact-one-match, multi-match, and no-filter harness tests.
- [ ] Build and behaviorally test `PlayerAssistantLauncher` in required CI.
  - Build/publish the release solution project with locked restore and verify argument forwarding round-trips empty arguments, spaces, quotes, Windows paths, and trailing backslashes.
  - Smoke-test missing-app, missing-runtime, and successful-launch branches without opening real UI or uncontrolled child processes.
- [ ] Put restored installer and recovery suites on required CI paths.
  - Run the online-installer PHP suite and DreamHost restore Python suite in isolated temporary directories.
  - Prove checksum rejection, transaction-path rejection, rollback failure, unsafe archive handling, and remote-upload hash mismatch fail the job.
- [ ] Eliminate the unchecked stale-capable Orcish compiled fallback.
  - Either fail closed on missing/invalid canonical embedded data or generate the C# fallback deterministically from the canonical lexicon.
  - Verify exact ordered parity and prove corrupting/removing the embedded resource cannot silently load divergent vocabulary.
- [ ] Replace the placeholder root README with verified contributor documentation.
  - Document supported Windows/.NET/PHP/Node prerequisites, locked restore, Release build/publish, canonical test commands, local sidecars, architecture entry points, and deployment/recovery links.
  - Add a documentation contract check for referenced repository paths and executable commands.

- [ ] Make the desktop test catalog guard detect silent test loss.
  - Include `TestCases.Identity.cs` in the required domain list and derive the expected domain-file count instead of reporting six while seven `TestCases.*` files exist.
  - Reject duplicate targets and unregistered discoverable test methods in addition to duplicate display names; replace the stale 435-test floor with catalog/declaration parity.
  - Add verifier self-tests that remove a registration, duplicate a target, and add an unregistered test method, and require each fixture to fail for the intended reason.
- [ ] Make required CI deterministic and independent of mutable production content.
  - Replace required-PR sitemap/runtime artifact generation from the live Obsidian endpoint with checked-in fixtures carrying known hashes and counts.
  - Keep live-source verification in scheduled or deployment monitoring rather than making unrelated changes depend on network availability and changing production content.
  - Add an offline CI-contract test proving all local build and regression gates are reachable with outbound access blocked.

## Next PWA robustness and usefulness backlog

- [ ] Reconcile the current branch before adding features.
  - [ ] Review the dirty worktree against the reviewed branch baseline.
  - [ ] Confirm deleted PWA features, tests, installer tooling, and broker support are intentional.
  - [ ] Restore or explicitly replace Activity / Inbox, message pagination, revision polling, search-worker support, optional-pack controls, and deployment/restore tooling.
  - [ ] Re-run the full PWA and broker regression gates.
- [ ] Restore responsive campaign-search performance.
  - [ ] Keep corpus loading, normalization, wildcard matching, scoring, and sorting in a dedicated worker.
  - [ ] Preserve request-ID protection against stale results.
  - [ ] Retain offline pack retry/removal behavior and benchmark typing responsiveness on the full index.
- [x] Strengthen offline and update behavior.
  - [x] Add an update-available banner with explicit reload/defer controls.
  - [x] Validate cached JSON schema and MIME types before use.
  - [x] Recover from corrupt or partially cached translator/search packs.
  - [x] Cover quota failures, interrupted installs, offline reloads, and stale workers.
- [ ] Make messaging a dependable campaign inbox.
  - [ ] Restore older-message pagination.
  - [ ] Add conversation grouping and per-thread unread counts.
  - [ ] Add retry/error recovery and draft preservation.
  - [ ] Add optional browser notifications for new messages.
  - [ ] Verify account switching never leaks prior-user messages.
- [ ] Add a campaign-session dashboard.
  - [ ] Show current date/location, active quests, party members/HP, recent messages, and unresolved quest decisions.
  - [ ] Add quick links between quests, characters, locations, and messages.
  - [ ] Support authenticated session notes through the broker.
- [ ] Improve quest usefulness.
  - [ ] Add filters for assigned character, prerequisite state, source location, and reward type.
  - [ ] Add a “What can I do next?” view for available, blocked, and active objectives.
  - [ ] Notify users when quest decisions or availability change.
- [ ] Improve party management.
  - [ ] Restore and expand party-funds support with transaction history and arithmetic reconciliation.
  - [ ] Add party inventory and magic-item ownership.
  - [ ] Add explicit last-refreshed and retry state to XP/level-up summaries.
- [ ] Make production health visible.
  - [ ] Add a safe authenticated health panel for broker, XP, and word-count freshness.
  - [ ] Add a credential-free “Report a problem” diagnostic export containing app revision, browser version, cache state, and failed endpoint names.

## Implemented this pass

- [x] Reconciled the reviewed PWA branch slice by restoring the previously removed search-worker, optional-pack, Activity / Inbox, revision-polling, message-pagination, installer, restore, and broker-support files without resetting unrelated worktree changes.
- [x] Restored campaign-search worker loading, request-ID protection, offline pack controls, and the matching PWA test coverage.
- [x] Restored Activity / Inbox, older-message pagination, revision polling, and protected account-transition coverage; browser smoke passes the player/DM, logout/session-expiry, navigation, and online/offline paths.
- [x] Corrected service-worker coverage for query-versioned translator/search workers plus the optional-pack loader and manifest, and advanced the cache revision to 109.

## Completed

- [x] Schedule fail-closed full wiki and local IC/OOC recounts at 4:00 AM and 8:30 PM Central with authenticated publication verification.
- [x] Add private cron refresh observability and safe broker health fields.
- [x] Add publisher transaction and rollback tests.
- [x] Add idempotent private broker deployment automation.
- [x] Add production deployment drift detection.
- [x] Sign and verify the canonical word-count source with Ed25519.
- [x] Add exact-pattern production backup retention.

Completed work is preserved in [to-do-archive-2026-07-30.md](to-do-archive-2026-07-30.md).
