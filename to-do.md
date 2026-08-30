# To Do

This file is the canonical implementation backlog. Other plans, reviews, and logs are historical reference only and must not be used to determine backlog status or implementation order.

## Security and delivery

- [x] Remove legacy XP histories from the current public PWA and repository tree.
  - [x] Production `pwa/XP/` was removed; XP histories now live outside the web root.
  - [x] The PWA loads authorized histories through authenticated `GET /v1/xp-awards` requests.
  - [x] Anonymous legacy XP requests return `404`, and the service worker no longer caches `/XP/` paths.
  - [x] Tracked deletions were committed and pushed; Git-history purging was intentionally deferred.
- [x] Make the full regression suite a required CI gate.
  - [x] Run all 536 desktop tests instead of only focused filters.
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

## 2026-08-30 security and edge-case review

Implement these twenty findings in order. Security boundaries, credential handling, authorization, destructive filesystem behavior, and transactional integrity precede availability and maintainability work.

### P1 — security and integrity boundaries

- [x] Require an exact HTTPS authority for every secret-bearing broker request.
  - [x] `NetworkUrlPurpose.PlayerAssistantBroker` now accepts only `https://bryanmiller.us:443/scarlethorizons/api/v1/`; HTTP, alternate ports, user-info, subdomains, and authority-changing redirects fail before authenticated follow-up traffic.
  - [x] Focused redirect/authority coverage and the complete 562-test Release harness pass.
- [ ] Close the verified-installer launch time-of-check/time-of-use window.
  - Bind hash/signature verification to stable file identity through process creation, using an ACL-protected non-replaceable launch location or equivalent fail-closed mechanism.
  - Regression: a deterministic post-verification swap prevents launch; unchanged verified bytes launch and cancellation launches nothing.
- [ ] Make RPOL profile cleanup reparse-point safe.
  - Reject root and descendant junctions/symlinks while scavenging `rpol-browser-verification-*`; delete links without traversing their targets, including under elevated scheduled execution.
  - Regression: target bytes outside the profile remain unchanged for root and nested file/directory reparse fixtures while ordinary stale profiles are removed.
- [ ] Constrain remote broker-backup filenames before any SCP or local path construction.
  - Accept only the producer's exact `broker-YYYYMMDDTHHMMSSZ-<8hex>.sqlite` basename and canonical descendants of approved roots; reject separators, rooted paths, traversal, confusable names, and alternate extensions before I/O.
  - Regression: every malformed name causes zero SSH/SCP/filesystem mutation; a valid basename still verifies and copies.
- [ ] Require an idempotency key on every authenticated broker mutation.
  - Remove the direct-execution fallback in `BrokerService::mutation()` and return `400 invalid_idempotency_key` when the key is absent or malformed.
  - Regression: every protected mutation rejects a missing key, while a retried keyed request replays one durable result and creates one effect.
- [ ] Close the idempotency ledger's mutation/finalization crash window.
  - Commit SQLite effects and the terminal ledger response atomically where possible; otherwise preserve an explicit recoverable ambiguous state and never delete evidence after a potentially committed effect.
  - Regression: fault injection after each mutation/ledger commit boundary cannot duplicate effects, strand a key permanently, or lose the replayable response.
- [ ] Add recoverable idempotency to signed administrator mutations.
  - Sign and persist an operation ID plus request hash and terminal response for account import/update, token issue/revoke, word-count, and snapshot mutations; replay exact duplicates and reject body collisions.
  - Regression: disconnects before and after each admin commit neither repeat effects nor make successful token issuance unrecoverable.
- [ ] Constrain optional-pack URLs before credentialed fetch or cache writes.
  - Require exact same-origin, ID-specific static paths; reject API, traversal-normalized, protocol-relative, query-lookalike, and cross-origin URLs; fetch packs with `credentials: 'omit'`.
  - Regression: invalid paths cause zero pack fetches/cache writes, and only the four declared static pack paths are accepted.
- [ ] Require schema verification to include the v7 message-throttle table.
  - Add `message_send_rate_limits` to the required migrated-object contract so a forged or partial `user_version=7` database fails during startup/deployment rather than on message send.
  - Regression: a v7 fixture missing the table fails closed with a schema diagnostic; a complete v7 fixture starts and throttles normally.
- [ ] Invalidate protected PWA state across tabs and windows.
  - Broadcast logout and account-generation transitions so every client immediately cancels requests and clears protected snapshots, dialogs, drafts, and DOM state, including hidden clients.
  - Regression: logging out or switching accounts in one of two pages clears the hidden page without polling or waiting for a `401`.
- [ ] Revalidate authenticated PWA state after BFCache restoration.
  - On `pageshow`, fail closed by hiding/clearing protected content and revalidating `/session` before restoring authenticated UI, especially when `event.persisted` is true.
  - Regression: Back navigation after remote logout/session expiry reveals no stale protected content before anonymous state is applied.
- [ ] Serialize all PWA release transactions across workflows and hosts.
  - Use one non-cancelling GitHub concurrency group and one shared host-side deployment lock for full PWA, campaign-search, and other release writers.
  - Regression: overlapping controllers cannot interleave backup/promotion/rollback, and either transaction's failure preserves the other's exact committed bytes.
- [ ] Treat only clean finalized installer transactions as resolved recovery state.
  - Accept `finalized` only when `rollback_forbidden=true` and `cleanup_complete=true`; retain fail-closed behavior for incomplete, malformed, or contradictory finalized manifests.
  - Regression: clean finalized/verified manifests pass preflight, while every incomplete finalized combination remains blocking.

### P2 — resilience, configuration, and supply chain

- [ ] Keep desktop network deadlines active through response-body consumption.
  - Carry the linked request-policy timeout through JSON decode, streaming copy, and disposal instead of ending it when headers arrive under `ResponseHeadersRead`.
  - Regression: a headers-then-stall fixture times out, disposes the response, removes partial files, and distinguishes policy timeout from caller cancellation.
- [ ] Prevent arbitrary in-scope navigation responses from replacing the cached PWA shell.
  - Promote network HTML to the canonical `index.html` cache key only for the normalized PWA root or `index.html`; serve other valid HTML without shell promotion.
  - Regression: visiting `offline.html` or another in-scope HTML path leaves cached shell bytes unchanged and offline root startup functional.
- [ ] Recover translator and campaign-search workers after crashes or deserialization failures.
  - Handle `error` and `messageerror`, terminate failed workers, clear loading/pending state, reject stale results, expose retry, and recreate exactly one worker.
  - Regression: startup and mid-request failures recover through one retry and the next request succeeds without stale rendering.
- [ ] Serialize the hosted-settings downgrade floor across processes.
  - Lock the full read/compare/max/write transaction for `trusted-hosted-settings-state.json`, matching the updater's highest-trusted-version policy.
  - Regression: reverse-completing child processes retain the maximum version; lower versions and abandoned-lock recovery cannot reduce it.
- [ ] Wire documented message-retention settings into the production broker service.
  - Pass validated `config['messages']` values to `MessageService` instead of silently using the 90-day/500-message defaults.
  - Regression: broker-boundary tests prove non-default pruning and fail closed on malformed production values.
- [ ] Pin and attest the Inno Setup compiler used by release CI.
  - Pin an approved Chocolatey/compiler version, verify package/compiler hash and publisher signature before execution, and record tool identity in release provenance.
  - Regression: unexpected version, hash, or signer fails before `ISCC`; the approved tool produces provenance containing its exact identity.
- [ ] Publish release-update artifacts as one recoverable generation.
  - Stage and verify the archive, manifest, signature, public key, and related outputs together, then promote them through a journaled/versioned commit with rollback to the prior complete set.
  - Regression: injected failure after every generation step leaves the old set byte-identical; success exposes only one complete verified new set.

## Architecture and maintainability

- [x] Decompose `Form1` into feature controllers or presenters with injected services.
- [x] **Split the custom regression harness into discoverable domain-focused test classes.**
  - The 536-test catalog now delegates to partial application, campaign, release, shared, and translator test classes while preserving name-based filtering and failure aggregation.
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
  - [x] Login shows server-derived attained level-ups only for each PC or hireling authorized to the account, using the published XP Tracking and Class Level Progression sources; durable claim/acknowledgement receipts prevent missed or repeated alerts, and XP Awards cards do not repeat them.
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
  - Extend `MyHeroBriefingRequest` with the authenticated canonical identity and use it to resolve the authenticated hero exactly.
  - Replace `FindHeroByNameOrFirstName` with stable-ID resolution; make Dungeon Master hero selection return a stable ID rather than an ambiguous display name.
  - Make XP totals, hero cards, recent activity, response detection, encrypted-note access, and quick-link generation use the resolved identity and its explicit aliases.
  - Remove automatic first-name aliases from `GetHeroAliases`; only use aliases declared by the identity registry, with ambiguity rejected before runtime.
  - Ensure a same-first-name hero cannot inherit another hero's briefing, XP, posts, or encrypted-note access.

- [x] 7. Audit all remaining name-based identity comparisons and boundaries.
  - [x] Review Form1, XpTrackingUtility, PartyHeroUtility, MyHeroBriefingUtility, TaggedNoteCipherUtility, hero roster loaders, import scripts, and generated manifests.
  - [x] Classify each name use as display/search text or authorization identity.
  - [x] Replace authorization comparisons that use first names, inferred aliases, or user-entered names; retain display/search behavior only where it cannot grant access.
  - [x] Document any intentional public search alias separately from protected account identity.

- [x] 8. Update tests and release/deployment contracts.
  - [x] Add unit/regression coverage for exact-ID success, same-first-name cross-password denial, ambiguous-alias load rejection, explicit unique-alias success, DM scope, party XP visibility, hero selection, briefing activity, encrypted-note access, and account switching.
  - [x] Add negative fixtures proving that a first-name-only input cannot authenticate or select a protected hero when multiple identities share that first name.
  - [x] Update custom regression-harness catalogs, sidecar schema validators, installer/runtime checks, migration tests, and release checklists.
  - [x] Verify that no protected lookup receives the originally entered name after authentication.

- [x] 9. Migrate, deploy, and verify the identity data atomically.
  - [x] Back up the live broker database, verify the rollback copy, and confirm the migrated password sidecar and canonical identity/role mapping have exact parity; no production data mutation was required.
  - [x] Verify local hashes, release contents, and generated roster/hero paths before deployment.
  - [x] Run authorized positive tests for all seven identities plus anonymous, wrong-password, unauthorized-origin, logout, and account-switch negative tests.
  - [x] Verify production behavior through the authenticated deployment contract without exposing passwords or protected response bodies.


## Prioritized codebase strengthening backlog

### P0 — Restore a fail-closed regression baseline

- [x] Prevent cross-account Magic Item disclosure in the PWA.
  - [x] Clear `magicItemSnapshot`, loading state, and errors on logout, session expiry, login, restore, and every account-generation transition.
  - [x] Bind Magic Item requests and decoded responses to the initiating account generation so a delayed prior-account response cannot repopulate current state.
  - [x] Add browser regressions that load an account-private item for player A, switch to player B, and prove neither cached nor delayed A data can render for B.
- [x] Make PWA deployment finalization durable before rollback evidence is removed.
  - [x] Persist a rollback-forbidden/finalized transaction state before deleting backups; recover a lost SSH response by querying transaction status rather than invoking rollback blindly.
  - [x] Fault-inject a disconnect after remote finalization succeeds and prove the live release remains intact and restartable cleanup is safe.
- [x] Restore executable remote PHP controllers for word-count deployment and verification.
  - [x] Prepend the PHP opening tag in both `Invoke-RemotePhp` writers and reject placeholder or non-PHP payloads before upload.
  - [x] Require a structured semantic success response and expected state mutation, not merely process exit zero.
  - [x] Keep `deploy-atomicity-tests.php` red-before/green-after coverage for the generated production payloads.
- [x] Make every native test invocation fail the workflow immediately.
  - [x] Check and throw on each PHP suite's exit code inside the `ForEach-Object` loop instead of allowing a later passing suite to mask an earlier failure.
  - [x] Apply the same fail-fast rule to sequential PowerShell/native verification commands and preserve the failing suite name in diagnostics.
  - [x] Add a CI-policy self-test with an intentional intermediate native-command failure followed by a passing command; the job must still fail.
- [x] Eliminate the current broker production/test contract drift.
  - [x] Make `broker-startup-tests.php` pass; request startup must not run `DatabaseMigrationService::migrate()` or mutate schema from service constructors.
  - [x] Make `message-pagination-tests.php` pass; production `MessageService` must match the checked-in pagination, retention, cursor, and snapshot-consistency contract.
  - [x] Run the complete PHP suite locally and in the required workflow after fixing exit-code propagation.
- [x] Make clean locked restores deterministic across supported .NET 10 SDK patches.
  - [x] Reconcile the launcher's self-contained `Microsoft.NET.ILLink.Tasks` dependency with `PlayerAssistant.Launcher/packages.lock.json`.
  - [x] Either pin the supported SDK with `global.json` or test the minimum and current `10.0.x` SDKs explicitly.
  - [x] Verify locked restore from a clean worktree with no pre-existing `obj` assets or NuGet cache assumptions.

### P1 — Protect authorization, state, and core operations

- [x] Bind RPOL credential submission to an exact trusted HTTPS origin and path.
  - [x] Cancel untrusted WebView navigation, revalidate the parsed URI immediately before autofill/submission, and replace substring-based Playwright page selection.
  - [x] Prove a matching form on an untrusted page and a URL such as `evil.example/?next=rpol.net` receive no credentials.
- [x] Replace derivable portable/settings encryption with operating-system secret protection.
  - [x] Portable settings now contain only public configuration in a versioned, base64-encoded public-settings-v1 envelope; RPOL credentials are always excluded.
  - [x] Local settings continue to use current-user DPAPI, and legacy local envelopes migrate to DPAPI on load.
  - [x] RPOL credentials remain provisioned through Windows Credential Manager rather than portable or hosted settings payloads.
  - [x] Installer and runtime validators accept the public settings envelope while retaining legacy migration compatibility.
  - [x] Focused settings, installer-package, PowerShell parse, and full 536-test regression gates pass.
- [x] Make the installed application tree immutable and correctly permissioned.
  - [x] Route diagnostics, caches, markers, generated manifests, and snapshots through `WritableRuntimeDirectory` or another user-data location.
  - [x] Remove inherited Users-Modify access from Program Files executables, DLLs, and the generated uninstaller.
  - [x] Launch from a read/execute-only published directory and prove startup succeeds while the publish-tree hash remains unchanged.
- [x] Make update and installer transitions cancellation-safe and transactional.
  - [x] Thread form-lifetime cancellation through update checks/downloads and gate every post-await dialog, UI mutation, launch-ticket write, and `Process.Start`.
  - [x] On ZIP-installer failures after promotion, quarantine/remove the candidate and restore the prior tree, ACLs, shortcuts, and uninstall registration exactly.
  - [x] Add cancellation and rollback regression coverage for update download, verified launch, and installer transaction safeguards.
- [x] Replace blind retry of mutating PWA installation with durable transaction recovery.
  - [x] Assign a transaction ID and support explicit status/resume/finalize/rollback operations after ambiguous SSH completion.
  - [x] Prove a connection loss after successful promotion does not rerun a non-idempotent installer or strand an unrecoverable mixed release.
- [x] Deploy `campaign-search.json` atomically.
  - [x] Upload to same-directory staging, verify the remote hash, retain rollback evidence, and atomically rename into place.
  - [x] Interrupt transfer and promotion independently and prove the previous production bytes remain available.
- [x] Fail closed on release-signing and SSH-host identity.
  - [x] On trusted pushes, require the configured production update-signing key and verify the emitted public-key fingerprint; never substitute an ephemeral key.
  - [x] Centralize the pinned DreamHost host key and require strict host-key checking for every deployment workflow and script.
  - [x] Correct the malformed Gitea mirror source credential expression and add semantic workflow validation plus a dry-run authenticated fetch.

- [x] Re-establish the broker startup and migration boundary.
  - [x] Keep ordered migrations in `migrate-broker.php` and deployment only; normal requests verify the expected `PRAGMA user_version` and fail closed on mismatch.
  - [x] Lazily construct only the service required by the selected route.
  - [x] Keep public `/v1/health` independent of SQLite, migrations, private files, and unrelated service constructors; the route now exits before HTTPS/config/private broker startup.
  - [x] Remove `ensureSchema()` writes from `BrokerService`, `CharacterAuthService`, `XpTrackingService`, `QuestService`, and `MessageService` after migration fixtures cover their schemas.
  - [x] Verified with broker startup, schema-guard, migration, and direct no-config health-route tests.
- [x] Restore missing broker routing and session-concurrency contracts.
  - [x] Wire authenticated `/v1/revisions` through the production entry point with account scoping, anonymous denial, and public HTTP coverage.
  - [x] Copy authorized identity state and release the PHP session lock before slow read-only XP, revision, message, and quest work.
  - [x] Prove logout/session inspection can complete while a same-cookie read request is blocked upstream.
- [x] Restore transactional, bounded message pagination and retention.
  - [x] Validate `limit`, composite keyset cursors, retention days, and per-recipient read-message caps without coercing malformed values.
  - [x] Read page rows and `unread_count` from one SQLite snapshot so concurrent inserts cannot produce mixed-generation metadata.
  - [x] Make acknowledgement plus recipient-scoped retention one transaction; never delete unread messages or another recipient's records.
  - [x] Pass the request query into `MessageService::forAccount` and preserve already-loaded browser pages when a continuation request fails.
- [x] Enforce mutation idempotency at the broker, not only in request headers.
  - [x] Persist a bounded account/method/route/idempotency-key ledger with a request-body hash and replay the original response transactionally.
  - [x] Reject reuse of one key with a different body and serialize concurrent duplicate submissions.
  - [x] Cover messages, quest requests/decisions, acknowledgements, and other authenticated mutations with replay and collision tests.
- [x] Make authenticated role and scope structurally unforgeable.
  - [x] Replace independently supplied `IsDungeonMaster` and `AccountScope` values with a validated role/scope derived by one identity factory from the canonical identity record.
  - [x] Reject impossible combinations such as a player canonical ID carrying Dungeon Master scope.
  - [x] Add negative tests at every protected desktop boundary that currently consumes `XpAuthenticatedIdentity`.
- [x] Restore source-aware login throttling without enabling cross-address account lockout.
  - [x] Scope progressive failures to account plus normalized source while retaining a separate address-wide abuse threshold.
  - [x] Preserve address abuse history across successful logins and restore deterministic IPv4/IPv6 normalization tests.
- [x] Make persistent multi-file refreshes crash-safe and monotonic.
  - [x] Inventory XP progression, award-history, word-count, lexicon, and runtime-cache collections that can expose mixed generations.
  - [x] Add per-collection locking, an explicit commit point or journal, durable promotion, and idempotent recovery where several files form one logical snapshot.
  - [x] Add fault injection for stale-but-valid sources, equal-date events, partial promotion, concurrent refresh, replay, reset/decrease, and recovery interruption.
- [x] Make the Windows keep-alive behavior truthful and observable.
  - [x] Stop relying on a hard-coded repository path in the hidden launcher; install a validated absolute path appropriate to the current machine.
  - [x] Treat failed `SetThreadExecutionState` or `SendInput` calls as task failures and write bounded diagnostic status.
  - [x] Ensure the refresh cadence is shorter than the effective display timeout, including battery policy, or use a supervised long-running assertion.
  - [x] Separate display, system-sleep, and disk-idle requirements; use `ES_SYSTEM_REQUIRED` or explicit power-policy changes only when those behaviors are intentionally requested and tested.

### P2 — Strengthen asynchronous and deployment edge cases

- [x] Expand authenticated PWA lifecycle fault injection.
  - [x] Prove empty, malformed, HTML, and stalled `401` responses invalidate the session before body parsing can block cleanup.
  - [x] Keep request timeout and cancellation active through response-body decoding, then recheck account generation before applying results.
  - [x] Prove late success or failure from a prior account generation cannot clear or repopulate a newly authenticated account.
  - [x] Keep observed and applied revision tokens separate through failed initial loads and sibling-resource partial failures.
  - [x] Test hidden/visible and offline/online transitions for exactly one immediate resume request and no duplicate polling intervals.
- [x] Restore optional packs to truly optional, reclaimable storage.
  - [x] Remove optional datasets from install-time general precache and store them exclusively in content-addressed optional caches after demand loading.
  - [x] Make failed manifest requests retryable and generation-guard load/remove so late completion cannot resurrect a removed pack.
  - [x] Add both optional-pack suites to canonical CI and replace source-regex assertions with cache-key, request-byte, and removal behavior checks.
- [x] Validate service-worker responses semantically before use or commit.
  - [x] Prefer a valid cached response over HTTP errors, wrong MIME types, captive-portal HTML, malformed JSON, or otherwise invalid network content.
  - [x] Validate mandatory precache MIME, nonempty content, and JSON/schema before committing the installation cache; clean partial version caches on failure.
  - [x] Bound navigation fetch time before falling back to the cached shell.
- [x] Make service-worker controller transitions deterministic in long-lived pages.
  - [x] Avoid reloading on first controller acquisition, but reload exactly once when a later worker takes control in the same page.
  - [x] Cover duplicate controllerchange events, explicit SKIP_WAITING update prompts, and online/offline lifecycle transitions in focused and browser smoke tests.
- [x] Prevent trusted-network redirects from connecting to untrusted targets.
  - [x] Disable automatic redirects and manually follow a bounded hop count, validating every parsed target against its purpose-specific allowlist before sending.
  - [x] Prove a trusted endpoint redirecting to localhost or another disallowed authority sends zero requests to that target.
- [x] Bound and rate-limit the public translator APIs.
  - [x] Enforce request-body byte limits before reading/parsing the complete body, cap decoded input and output, and apply an application-level per-source rate limit.
  - [x] Avoid rebuilding large lexicons for every request where safe while preserving translation correctness and bounded resource use.
  - [x] Add oversized-body, malformed-JSON, maximum-valid-input, concurrent-burst, rate-limit-recovery, and memory/CPU-boundary tests.
- [x] Make RPOL credential migration and WebView dispatch lifetime-safe.
  - [x] Store username/password as one versioned credential record or compensate on partial write/delete so migration and plaintext removal are all-or-nothing.
  - [x] Complete cancellation even when UI enqueue fails or the handle is destroyed; dispose registrations and recheck dialog viability after each await.
  - Release-candidate self-test fixture regenerated through the documented publish path; migration and dispatch regression coverage passes.
- [x] Prove RPOL authentication against the exact protected resource before capturing or reusing browser state.
  - [x] Define one canonical protected Dice Roller probe for game `80170` and use it for visible-browser verification, WebView verification, restored-state validation, and publisher preflight.
  - [x] Reject public campaign content, cookie presence, login redirects/forms, untrusted navigation, challenges, wrong paths, wrong game IDs, and unexpected protected-page shapes as authentication proof.
  - [x] Capture state only after the live probe succeeds, then restore it in a publisher-equivalent fresh browser process and repeat the probe before promotion.
  - [x] Add deterministic classifier, local browser-fixture, cancellation, timeout, and state-round-trip tests without storing credentials or cookie values.
- [x] Secure the external RPOL browser/CDP connection and verification lifetime.
  - [x] Eliminate available-port/release/rebind races and prevent unauthorized local processes from reading cookies or controlling the verification browser.
  - [x] Apply one end-to-end deadline across browser launch, CDP connection, verification, capture, publishing, disposal, and wrapper supervision; produce truthful timeout/crash results.
  - [x] Serialize verifier and publisher operations with an application-owned cross-process lock and clean every browser, profile, CDP, and temporary-state resource on every exit path.
- [ ] Remove distributed RPOL administrator credentials after protected-page coverage passes.
  - [ ] Verify every required approved RPOL page through the scheduled signed publisher and complete a clean-client run using only a revocable broker token.
  - [ ] Remove administrator credentials from hosted, local, publish-time, and end-user settings/Credential Manager entries; rotate the administrator password afterward.
  - [ ] Complete release verification for broker-only retrieval, credential-free client startup, diagnostics redaction, and Release/publish/installer parity.
  - [!] Blocked after retry: the launcher-rendezvous defect is fixed and pushed, but the scheduled signed publisher still timed out before producing protected-page coverage; no credentials were removed or rotated.

- [ ] Add a profile-bound RPOL state fallback only when publisher-equivalent round-trip testing proves storage-state reuse cannot work.
  - [ ] Use a dedicated user-only persistent profile with restrictive ACLs, a single-process lock, exact protected-probe validation, and explicit reset behavior.
  - [ ] Never copy the user’s normal browser profile or package, upload, log, or commit authenticated browser state.
- [x] Isolate network and update concurrency state.
  - [x] Scope circuit breakers by network purpose and endpoint family so unrelated services sharing one authority cannot suppress or reset one another.
  - [x] Serialize highest-trusted-version compare-and-write across processes and recompute the maximum while holding the lock.
  - [x] Require the x64 Windows Desktop Runtime for the win-x64 launcher; do not accept x86-only probes.
  - Implementation: purpose/family-scoped breakers, cross-process trusted-version locking, and x64-only launcher runtime probing; focused and full custom tests passed. The installer compatibility fallback for Windows PowerShell 5.1 also restored the required package/RC validation gates.
- [x] Harden inbox behavior against retries, concurrent mutation, and user-data loss.
  - [x] Preserve unsent drafts across retryable failures and account-safe navigation while clearing them on identity transition.
  - [x] Deduplicate accumulated pages by stable message ID and reset safely when server metadata shrinks after acknowledgements or retention.
  - [x] Add bounded per-account message-send throttling and abuse-oriented fixtures without weakening legitimate DM broadcasts.
- [x] Extend release and deployment transaction fault injection.
  - [x] Exercise interruption before and after each commit point for migrations, public-loader promotion, cron changes, private config, installer replacement, and final HTTPS verification.
  - [x] Verify rollback restores pre-existing files, removes newly introduced files, preserves mode-restricted recovery evidence on rollback failure, and never runs after finalization.
  - [x] Verify source and packaged installer templates remain byte-contract compatible and that runtime/deployment manifests reject drift.
- [x] Make broker operations and recovery observable under concurrency and partial platform failure.
  - [x] Claim alert thresholds and cooldowns transactionally so concurrent failures emit at most one notification.
  - [x] Fail recovery when required public health cannot execute or atomic status persistence fails; preserve explicit failure evidence.
  - [x] Reuse one validated XP snapshot for award enrichment rather than issuing redundant live refreshes.
  - [x] Isolate inline FTPS configuration fixtures from ambient `BACKUP_FTPS_*` variables and restore the environment in `finally`.

### P3 — Reduce future regression surface

- [x] Split remaining high-churn orchestration code behind testable boundaries.
  - [x] Continue extracting account/session, messages/activity, presence, and update lifecycle logic from `pwa/app.js` while preserving browser behavior and offline cache contracts.
  - [x] Continue reducing `Form1` event-handler orchestration into cancellable controllers with explicit single-flight and shutdown semantics.
  - [x] Generate duplicated installer/deployment payloads from one canonical source and fail verification on source/dist drift.
- [ ] Add measurable resource budgets.
  - [ ] Set upper bounds for broker query latency, message-table growth, cache/backup retention, startup work, PWA polling, optional-pack storage, and diagnostic/log growth.
  - [ ] Add representative large-fixture and slow-I/O tests so performance and storage regressions become release-gate failures rather than production surprises.
  - [!] Blocked 2026-08-30: implementation, focused/full regression, deployment parity, installer smoke, and non-signing RC gates pass. The remaining release acceptance prerequisite is an approved Authenticode signer subject/thumbprint and matching certificate; the local executable is unsigned. No signer value was guessed and no signing check was weakened.

## Completed

- [x] Schedule fail-closed full wiki and local IC/OOC recounts at 4:00 AM and 8:30 PM Central with authenticated publication verification.
- [x] Add private cron refresh observability and safe broker health fields.
- [x] Add publisher transaction and rollback tests.
- [x] Add idempotent private broker deployment automation.
- [x] Add production deployment drift detection.
- [x] Sign and verify the canonical word-count source with Ed25519.
- [x] Add exact-pattern production backup retention.

Completed work is preserved in [to-do-archive-2026-07-30.md](to-do-archive-2026-07-30.md).
