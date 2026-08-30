# Player Assistant Security and Edge-Case Implementation Backlog

**Scope:** Read-only review of `C:\repos\player-assistant`, including the C# desktop application, launcher/update path, PWA, PHP broker, deployment scripts, installer, CI, and existing `to-do.md`.

**Repository state:** Review performed with unrelated local changes present in `pwa/README.md`, `web-deploy/tests/broker-ftps-transport-tests.php`, and untracked `NUL` and `repo-cleanup.py`. Do not overwrite or mix those changes while implementing this backlog.

**Priority:** P0 blocks release or creates a credible security boundary failure; P1 is high-risk reliability/security hardening; P2 reduces exploitable edge cases and operational failure modes; P3 reduces regression surface.

## P0 — implement first

1. **Bind RPOL credential submission to an exact trusted origin and path.**
   - Files: `RpolAuthUtility.cs`, `RpolWebViewVerificationDialog.cs`, `NetworkUrlAllowlistUtility.cs`.
   - Reject navigation and form submission unless the parsed scheme, host, effective port, and expected path match the RPOL contract. Replace substring page selection such as `Url.Contains("rpol.net")` with exact URI validation.
   - Test: an attacker-controlled page containing a matching login form and a URL such as `evil.example/?next=rpol.net` must receive zero credentials.

2. **Remove derivable portable/settings encryption for credentials.**
   - Files: `LocalSettingsUtility.cs`, `RuntimeSecretStoreUtility.cs`, `AppSettingsUtility.cs`, packaging scripts.
   - Keep portable payloads secret-free; use DPAPI/Credential Manager or explicit user provisioning for RPOL credentials and storage state. Define a one-way migration with no plaintext credential residue.
   - Test: a copied settings fixture must not decrypt under another Windows identity; migration must be atomic.

3. **[x] Make the installed application tree read/execute-only.**
   - Files: `RuntimePathUtility.cs`, `AppConfigurationValidationUtility.cs`, `Installer/install-player-assistant.ps1`, `Installer/player-assistant.iss`, runtime housekeeping utilities.
   - Move diagnostics, caches, markers, manifests, and snapshots to user data; remove inherited Users-Modify access from Program Files binaries and scripts.
   - Test: startup and normal operation succeed with a read-only publish tree and the publish-tree hash remains unchanged. Verified by the full regression suite and read-only published-health run.

4. **[x] Make installer/update transitions cancellation-safe and transactional.**
   - Files: `PlayerAssistantUpdateUtility.cs`, `VerifiedInstallerUpdateUtility.cs`, `VerifiedInstallerLaunchUtility.cs`, `Installer/install-player-assistant.ps1`.
   - Propagate form-lifetime cancellation through download, verification, promotion, dialogs, launch-ticket writes, and process launch. On post-promotion failure, restore the previous tree, ACLs, shortcuts, and uninstall registration.
   - Test: cancellation is covered at update download and verified launch boundaries; installer transaction state records intent before promotion and rollback removes candidates and restores the prior tree, ACLs, shortcuts, and uninstall registration on failure.

5. **[x] Replace blind retry after ambiguous PWA deployment completion with durable recovery.**
   - Files: `web-deploy/deploy-pwa-files.ps1`, `web-deploy/bryanmiller.us/scarlethorizons/api/index.php`, deployment tests.
   - Persist a transaction ID and explicit status/resume/finalize/rollback state. Query status after SSH disconnect instead of rerunning a non-idempotent operation.
   - Test: disconnect after remote promotion and after finalization; recovery must converge without rollback-after-finalization.

6. **[x] Deploy generated PWA data atomically.**
   - Files: `pwa/refresh-campaign-search.ps1`, `web-deploy/deploy-pwa-files.ps1`, `pwa/campaign-search.json` packaging/verification.
   - Stage in the same directory, verify exact remote hash and schema, then rename atomically while retaining rollback evidence.
   - Test: interrupt transfer and promotion independently; old production bytes must remain served.

7. **[x] Fail closed on release signing and deployment host identity.**
   - Files: `.github/workflows/*.yml`, `AuthenticodeSignatureUtility.cs`, `web-deploy/*.ps1`.
   - Require the configured production signing key on trusted release paths, verify the emitted public-key fingerprint, and centralize strict DreamHost host-key checking. Reject missing, malformed, or ephemeral keys.
   - Test: absent key, wrong signer, changed host key, and malformed mirror credential expressions all fail before deployment.

8. **Restore the broker migration/startup boundary.** [x]
   - Files: `web-deploy/player-assistant-broker/BrokerService.php`, `CharacterAuthService.php`, `XpTrackingService.php`, `QuestService.php`, `MessageService.php`, `migrate-broker.php`.
   - Keep migrations deployment-only; normal requests verify `PRAGMA user_version` and fail closed on mismatch. Lazily instantiate only the selected service; keep public health independent of SQLite and private subsystems. The public health route now exits before HTTPS/config/private broker startup.
   - Test: every route against current, old, and missing schema versions; assert that GET health performs no migration or write. Verified by broker startup, schema-guard, migration, and direct no-config health-route tests.

9. **Restore authenticated revision routing and release PHP session locks before slow reads.**
   - Files: `web-deploy/player-assistant-broker/BrokerService.php`, public `api/index.php`, `CharacterAuthService.php`, revision tests.
   - Route `/v1/revisions` through the production entry point with account scoping. Copy validated identity state, call `session_write_close()`, then perform XP/message/quest/revision reads.
   - Test: a blocked upstream read must not block logout or another same-cookie session request.

10. **Enforce broker-side mutation idempotency.**
    - Files: `BrokerService.php`, `MessageService.php`, `QuestService.php`, schema migrations, PWA request client.
    - Persist a bounded account/method/route/key ledger with request-body hash and original response. Reject key reuse with a different body and serialize concurrent duplicates.
    - Test: retry and race duplicate message, quest request/decision, acknowledgement, and other authenticated mutations.

## P1 — high-risk hardening

11. **Make role and account scope structurally unforgeable.**
    - Files: `CharacterAuthService.php`, `BrokerService.php`, desktop identity boundary classes, protected-route tests.
    - Derive role and scope once from the canonical account record; remove independently supplied `IsDungeonMaster` and `AccountScope` inputs; reject impossible combinations.
    - Test: player identity plus DM scope, mismatched account IDs, and stale-session scope all fail closed.

12. **Restore source-aware login throttling without cross-address lockout.**
    - Files: `CharacterAuthService.php`, login schema/migration, login hardening tests.
    - Scope progressive failure state to account plus normalized source, retain an independent address abuse threshold, and preserve abuse history across successful login.
    - Test: IPv4/IPv6 normalization, account isolation, expiry, distributed attempts, and successful-login behavior.

13. **Make multi-file refreshes crash-safe and monotonic.**
    - Files: XP progression/award refresh utilities, word-count publishing, lexicon refresh and runtime-cache writers.
    - Inventory logical snapshots, add per-collection locking/journal and explicit commit points, and make recovery idempotent.
    - Test: stale-valid source, equal-date events, partial promotion, concurrent refresh, replay, reset/decrease, and interrupted recovery.

14. **Secure the external RPOL browser's CDP connection.**
    - Files: `RpolAuthUtility.cs:406-419,476-492,651-686,781-793`.
    - Eliminate the available-port/release/rebind race and unauthenticated loopback debugging endpoint. Reserve the listener, use an unguessable authenticated endpoint, or use a controlled browser-launch mechanism that prevents another local process from reading cookies or controlling the verification browser.
    - Test: port collision, competing local CDP client, cancellation, browser crash, and cleanup of the temporary profile.

15. **Make authenticated PWA lifecycle and controller transitions deterministic.**
    - Files: `pwa/app.js`, request helpers, browser smoke tests.
    - Test malformed/HTML/stalled 401 responses, cancellation during body decoding, generation changes before result application, partial sibling loads, hidden/offline resume transitions, first controller acquisition, later worker takeover, repeated `controllerchange`, and multiple tabs.
    - Acceptance: prior-account responses cannot repopulate or clear the current account; first acquisition does not reload; later takeover reloads exactly once; resume schedules exactly one request.

16. **Keep optional PWA packs demand-loaded, content-addressed, and reclaimable.**
    - Files: `pwa/service-worker.js`, `pwa/optional-pack-loader.js`, translator/search workers, generated manifest, verifier.
    - Remove optional data from general install precache; generation-guard load/remove, preserve last-known-good packs, and clean obsolete hashes only after validation.
    - Test: failed manifest, truncated/wrong-MIME/stale-hash pack, quota failure, removal racing with download, offline reuse, and cache-key isolation.

17. **Validate service-worker responses semantically before use or commit.**
    - Files: `pwa/service-worker.js`, `pwa/verify-pwa.ps1`, service-worker tests.
    - Reject error pages, wrong MIME, captive portals, malformed JSON, empty mandatory assets, and corrupt cache entries; prefer valid cached content and bound navigation timeout.
    - Test: 404/503/HTML/wrong-MIME/timeout/corrupt JSON and partial install cleanup.

18. **Bound and rate-limit the public translator APIs.**
    - Files: `web-translator/api.php:18-56`, `web-translator/elven-api.php:18-56`, `OrcishTranslator.php`, `ElvenTranslator.php`.
    - Enforce a request-body byte limit before reading/parsing the complete body, cap decoded input and output, add an application-level per-source rate limit, and avoid rebuilding large lexicons for every request where safe.
    - Test: oversized non-word bodies, malformed JSON, maximum valid input, concurrent bursts, rate-limit recovery, and memory/CPU bounds.

19. **Harden every trusted-network redirect.**
    - Files: `NetworkRequestUtility.cs`, `NetworkUrlAllowlistUtility.cs`, `RpolClient.php`, all HTTP fetchers.
    - Disable automatic redirects where possible; manually follow a bounded number of hops and validate every parsed target against the purpose-specific allowlist before sending.
    - Test: trusted endpoint redirecting to localhost, private IP, alternate port, userinfo host, encoded host, or disallowed subdomain must send zero follow-up requests.

20. **Make RPOL credential migration and WebView dispatch lifetime-safe.**
    - Files: `RpolAuthUtility.cs`, `RpolWebViewVerificationDialog.cs`, `RuntimeSecretStoreUtility.cs`, `Form1.cs`.
    - Store username/password as one versioned credential record or compensate on partial writes/deletes; complete cancellation even if UI enqueue fails or the dialog is destroyed; dispose registrations and recheck dialog viability after every await. Do not persist or email raw exception details; map failures to stable redacted operational codes.
    - Test: cancellation during navigation, login submission, storage-state save, dialog close, process exit, and partial migration failure.

## Validation gates before implementation is considered complete

- Preserve and isolate the existing dirty worktree changes.
- Add a red regression test before each security or edge-case fix.
- Run the full Release desktop test suite, PHP broker suites, PWA static verification, browser smoke tests, and deployment-contract tests.
- Verify no credentials, tokens, cookies, protected response bodies, or production database copies enter diagnostics, build artifacts, `Release`, or Git history.
- Re-run repository hygiene, secret scanning, package-lock, signing, and release-parity checks.

## Additional concrete review corrections folded into the implementation details

- `AuthenticodeSignatureUtility.cs:165-189`: do not locate the signature-verification PowerShell executable through a user-controlled `PATH`; use a trusted system path or verify the verifier executable.
- `PlayerAssistantUpdateUtility.cs:247-260,331-344`: serialize highest-trusted-version compare-and-write across processes and recompute the maximum under the lock.
- `CharacterAuthService.php:527-590`: bound or coalesce read-triggered `last_seen_at`/presence writes to prevent authenticated read amplification against SQLite.
- `web-deploy/bryanmiller.us/scarlethorizons/api/index.php:257-279`: constrain the session cookie path to the API boundary rather than accepting broad configuration values.
- `BrokerService.php` admin routes: add independent workload/rate guards for signed health, account, snapshot, token, and import operations.
- `web-deploy/bryanmiller.us/scarlethorizons/api/index.php:105-117,125-140` and `BrokerAlertService.php:34-42,86-106`: persist stable redacted error codes instead of raw exception messages in alerts and email.
- `.github/workflows/hardening.yml:447-449`: trusted pushes must fail when the production signing key is absent; never fall back to an ephemeral key for release artifacts.
- `web-deploy/deploy-pwa-files.ps1:167-173`: check rollback exit status and retain rollback evidence until recovery is verified.
- `Installer/player-assistant.iss:68-115`: require the compatible x64 Desktop Runtime rather than accepting any `10.*` directory.
- `PlayerAssistant.Tests/Program.cs:21-23,32-38`: a zero-match focused test filter must fail instead of returning success.
- `verify-test-harness-structure.ps1:45-53`: validate an exact authoritative catalog and resolve every registered target, not merely a lower-bound count.
- `.github/workflows/hardening.yml:219-220`: pin and integrity-verify the Inno Setup compiler/toolchain.
- `.github/workflows/pwa-campaign-word-count-deploy.yml:4-8,23-46`: run canonical generated-data/schema/parity verification before deploying tracked campaign data.

## 2026-08-30 follow-up review — twenty prioritized findings

This follow-up reviewed `origin/master` at `8b46c50c25eedcff49aea68296429d4864f13033` across desktop/launcher, PWA/browser, PHP broker/API, and deployment/recovery boundaries. The canonical implementation order is the matching section in `to-do.md`; this plan preserves review evidence and acceptance detail.

### P1 — security and integrity boundaries

1. **Exact broker HTTPS authority.** `NetworkUrlAllowlistUtility.cs:98-176` permits HTTP, custom ports, and subdomains while `RpolSnapshotUtility.cs:820-1025` sends HMAC/bearer material. Pin exact HTTPS authority/path and prove forbidden targets receive zero authenticated traffic.
2. **Verified-installer launch TOCTOU.** `VerifiedInstallerLaunchUtility.cs:103-123` returns a path after closing verification handles and `Form1.cs:2147-2149` launches later. Bind verification to stable file identity through launch and fault-inject a post-verification swap.
3. **Reparse-safe RPOL cleanup.** `RpolExternalProfileCleanup.cs:65-111` and `RpolCleanupUtility.cs:73-109` recursively delete stale profiles without rejecting junctions/symlinks. Never traverse reparse targets; prove outside bytes survive root and nested link fixtures.
4. **Backup filename confinement.** `web-deploy/backup-broker-database.ps1:30-63` trusts remote `recovery.backup_file` for SCP and local joins. Enforce the exact backup basename grammar and canonical descendants before any I/O.
5. **Mandatory mutation idempotency keys.** `BrokerService::mutation()` at `BrokerService.php:770-787` directly executes when the header is absent. Reject missing keys and prove all protected mutations replay one durable result.
6. **Atomic/recoverable idempotency finalization.** `IdempotencyLedger.php:54-88` separates pending-row, effect, and response transactions and deletes evidence after exceptions. Eliminate duplicate/stranded outcomes under commit-boundary fault injection.
7. **Recoverable signed-admin mutations.** `BrokerService.php:239-286,608-650` consumes nonces before account/token/word-count/snapshot responses are safely replayable. Persist signed operation IDs, request hashes, and terminal responses.
8. **Optional-pack URL confinement.** `pwa/optional-pack-loader.js:36-49,91-127` resolves manifest URLs without origin/path confinement and uses default credentials. Allow only ID-specific static paths with `credentials: 'omit'` and prove invalid paths cause no fetch/cache write.
9. **Complete v7 schema guard.** `DatabaseMigrationService.php:305-313` creates `message_send_rate_limits`, but `BrokerService.php:679-713` omits it from required objects while `MessageService.php:329-344` queries it. Partial v7 databases must fail at startup.
10. **Cross-tab protected-state invalidation.** `pwa/app.js:2457-2494,2935-2990` clears only the current document and has no cross-client signal. Broadcast account transitions and prove hidden pages clear immediately.
11. **BFCache session revalidation.** `pwa/app.js` restores authentication only at startup and lacks `pageshow` handling. Hide/clear protected state and revalidate `/session` before rendering a restored page.
12. **Serialized PWA release writers.** Deployment workflows lack a shared concurrency group and `web-deploy/deploy-pwa-files.ps1:26-151` lacks a host-wide lock. Serialize all release writers and prove rollback cannot overwrite another transaction.
13. **Correct finalized-transaction discovery.** The installer retains clean `finalized` manifests (`install-player-assistant-web.php:1031-1058`), while `restore_dreamhost_pwa.py:382-386` treats them as unknown. Accept only finalized plus rollback-forbidden and cleanup-complete.

### P2 — resilience, configuration, and supply chain

14. **Response-body deadlines.** `NetworkRequestUtility.cs:93-111,235-312` ends policy timeout after headers while body reads use an infinite client timeout. Carry deadline through decode/copy and test headers-then-stall cleanup.
15. **Canonical shell-cache promotion.** `pwa/service-worker.js:256-284` stores any valid in-scope navigation response under `index.html`. Promote only root/index navigations and prove `offline.html` cannot poison the shell.
16. **Worker crash recovery.** `pwa/modules/translator.js` and `search.js` lack `error`/`messageerror` recovery. End loading state, terminate/recreate one worker, reject stale replies, and verify retry succeeds.
17. **Hosted-settings downgrade serialization.** `HostedSettingsTrustUtility.cs:172-195` performs an unlocked read/compare/write. Use a cross-process max-version transaction and prove reverse completion cannot lower trust.
18. **Production message-retention wiring.** `MessageService.php:12-23` accepts retention config, but `BrokerService.php:732-735` constructs it without `config['messages']`. Test non-default pruning through the broker boundary.
19. **Pinned/attested Inno Setup.** `.github/workflows/hardening.yml:236-238` installs mutable Chocolatey `innosetup`. Pin and verify version/hash/signer before `ISCC`, and record provenance.
20. **Atomic release-update generations.** `build-release-update-artifacts.ps1:196-244` replaces archive, manifest, signature, and key independently. Stage/verify/promote one recoverable generation and fault-inject every boundary.
