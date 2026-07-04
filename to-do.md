- [x] code app feature to search online vault by using the JSON library file 'sitemap-keyword-urls.json'
- [x] add menu item to display the regional map
- [x] build method GetSearchTerms() to extract search terms when btnSearch is clicked
- [x] string[] SearchTerms should accept the output of method GetSearchTerms()
- [x] code a method that, given a search term, will return a list of URLs from the JSON library file 'sitemap-keyword-urls.json' that match the search term
- [x] add terms from Locations index (on obisidian wiki) to game-posts-key-terms.md
- [x] Store the XP Tracking URL in encrypted local settings and add a parser/fetch helper for current PC XP totals.
- [x] Add `Show > XP` with encrypted per-PC password sidecar validation before displaying a character's XP date and total.
- [x] Allow the Dungeon Master XP credential to display dates and XP totals for all PCs.
- [x] Show player-safe XP Tracking failure messages that hide the unlisted URL and direct users to contact the DM.
- [x] Add a polished installer package workflow for `0.9.0-hardening.5` targeting `C:\Program Files\kyrathasoft\player-assistant`.
- [x] Add an Inno Setup installer that installs to Program Files and guides users to the .NET Desktop Runtime 10 x64 download when the required runtime is missing.
- [x] Switch publish output to framework-dependent multi-file now that the Inno installer checks for the .NET Desktop Runtime.

## Hardening completed in this session

- [x] Added centralized startup exception/log wrappers around required and optional startup phases.
- [x] Converted settings load, player-character refresh, regional-map preload, configuration validation, and runtime housekeeping into logged startup phases.
- [x] Added background-task supervision for startup work so duplicate phases are suppressed, cancellation is coordinated, and failures are logged.
- [x] Added retry/timeout handling for network requests and preserved caller cancellation behavior.
- [x] Added atomic file writes and replacement retry handling for runtime artifacts such as keyword indexes and generated sidecars.
- [x] Added malformed runtime-artifact quarantine handling for JSON/text loads, including keyword index, login info, and asset manifest paths.
- [x] Added startup configuration validation for required settings, optional RPOL credentials, and runtime sidecar warnings.
- [x] Hardened UI operation failures with centralized status/log/dialog reporting.
- [x] Hardened RPOL authentication failure caching and logging to avoid repeated noisy failures.
- [x] Hardened publish verification to reject diagnostic/runtime leak artifacts such as stale startup logs, temp folders, debug symbols, plaintext credentials, and browser auth state.
- [x] Added runtime housekeeping to remove stale temp files, orphaned atomic temp files, and old quarantined JSON files while rotating oversized startup logs.
- [x] Added focused regression tests for the above hardening paths in the Release test harness.
- [x] Added startup dependency failure-matrix coverage for bad config, missing/empty sidecars, corrupt optional local settings, locked runtime artifacts, keyword-index recovery, and terminal network failures.
- [x] Hardened RPOL authenticated-fetch failure classification for missing credentials, rejected login, expired auth state, RPOL blocking/rate limits, Playwright unavailability, and transient remote outages.
- [x] Added `startup-health.json` to record structured startup phase status, elapsed time, download counts, failure counts, and last exception summaries while keeping the diagnostic artifact out of publish output.
- [x] Expanded publish verification to parse and validate published `settings.json`, encrypted `settings.local.json`, keyword-index sidecars, keyword terms, `sitemap.xml`, and required Playwright runtime internals.
- [x] Added and ran a controlled Release startup smoke verification script that backs up selected runtime artifacts, launches `Release\player-assistant.exe --suppress-hero-images`, validates fresh `startup-health.json` phases, and restores prior artifacts.
- [x] Added release identity hardening with `0.9.0-hardening.5` project metadata, a `/version`/`--version` command path, executable version verification during publish checks, and regression coverage for version metadata.
- [x] Ran the full release rehearsal: Release build, publish, publish verification tests, Release startup smoke, published-folder startup smoke, and executable version checks for both output folders.
- [x] Hardened keyword terms startup handling so running from `Release\publish` no longer deletes the parent `Release\game-posts-key-terms.md` runtime artifact.

## Next hardening tasks

- [x] Add transactional RPOL thread export that writes to a temporary sibling folder, validates the manifest, and swaps into place without deleting the last good export first.
- [x] Add a shared diagnostic redaction utility used consistently by crash diagnostics, startup health, startup logs, and diagnostic bundle verification paths.
- [x] Add an RC commit/tag checklist script that verifies clean intended diffs, runs the focused hardening tests, confirms both executable versions, and prints the exact `v0.9.0-hardening.5-rc1` tagging commands without mutating Git state.
- [x] Add a published-folder runtime integrity check that verifies no startup run from `Release\publish` modifies or deletes parent `Release` artifacts, using before/after file manifests for tracked runtime files.
- [x] Add a diagnostic bundle script that collects `startup-health.json`, `startup-errors.log`, version metadata, publish verification output, and smoke verification output into a redacted timestamped zip for troubleshooting.
- [x] Add focused regression coverage for diagnostic bundle redaction, encrypted local-settings summarization, expected zip contents, and forbidden auth-state rejection.
- [x] Add `collect-diagnostics.ps1 -VerifyOnly` to inspect an existing diagnostics zip for forbidden auth-state files and unredacted credential markers.
- [x] Broaden diagnostics redaction for bearer tokens, cookie headers, credentialed URLs, and password/token/secret query values.
- [x] Add diagnostics bundle generation and verification to the RC checklist.
- [x] Add process-lock diagnostics for build/publish troubleshooting so running `player-assistant.exe` process paths and PIDs are reported after publish failure.
- [x] Validate `rpol-storage-state.json` before Playwright uses it, deleting stale, malformed, or non-RPOL auth state so authenticated fetches start from a clean login path.
- [x] Add retention limits for diagnostics, quarantines, and old scratch folders.
- [x] Add `startup-health.json` schema/versioning.
- [x] Add crash-path diagnostic capture like `last-crash.json`.
- [x] Add Release/publish parity checks.
- [x] Add config repair guidance for startup validation failures.
- [x] Add a network/auth circuit breaker for repeated terminal failures.
- [x] Add a release integrity hash manifest.

## Follow-up hardening backlog

- [x] Add secret scanning to the RC checklist for tracked files and reachable history.
- [x] Add a dependency/runtime version inventory to publish output and diagnostics.
- [x] Strengthen `settings.local.json` key separation with a per-machine or per-install derivation path.
- [x] Add network allowlist validation for configured and fetched RPOL/Obsidian URLs.
- [x] Add RC checklist self-tests for secret scan, health failure, manifest mismatch, and expected-path handling.

## Additional hardening backlog

- [x] Add signed release provenance with commit, tag, manifest, runtime inventory, script hash, and executable signature metadata.
- [x] Add config/schema versioning for `settings.json` and `settings.local.json`.
  - [x] Add `schema_version: 1` metadata to checked-in `settings.json`.
  - [x] Treat missing schema versions as legacy-compatible version `0`.
  - [x] Reject invalid or future schema versions in app startup settings load.
  - [x] Emit `schema_version: 1` for encrypted `settings.local.json` envelopes.
  - [x] Migrate legacy/plaintext local settings to the current encrypted schema envelope.
  - [x] Validate config schema versions during publish verification.
  - [x] Include local-settings schema metadata in diagnostic bundle shape output.
  - [x] Add focused regression tests for current/future settings schemas, local settings schemas, publish verification, and diagnostics.

## Future hardening implementation tasks

- [x] Add network response content limits for HTML, markdown, JSON cache, and image downloads.
  - [x] Define per-content-type maximum byte limits and sensible defaults.
  - [x] Enforce limits while streaming HTTP responses instead of after full buffering.
  - [x] Apply limits to RPOL HTML fetches, Obsidian markdown fetches, keyword/sitemap JSON cache downloads, and hero/image downloads.
  - [x] Preserve last known good files by failing bounded downloads before atomic promotion.
  - [x] Add tests for oversized HTML, markdown, JSON, and image responses.
- [x] Validate sitemap and keyword-index URL entries against the network allowlist before storing them.
  - [x] Validate every sitemap URL before writing `sitemap.xml`.
  - [x] Validate keyword-index URL lists before writing `sitemap-keyword-urls.json` or `keyword-index.json`.
  - [x] Reject credentialed URLs, non-HTTP(S) schemes, escaped hosts, and non-allowlisted RPOL/Obsidian hosts.
  - [x] Preserve the previous good index when fetched data contains rejected URLs.
  - [x] Add regression coverage for poisoned sitemap and keyword-index entries.
- [x] Add a full RC dry-run mode that writes a structured JSON summary and exits nonzero on any failure.
  - [x] Add a `-DryRunJson` or equivalent mode to the RC checklist script.
  - [x] Capture each checklist step with status, elapsed time, command, artifact paths, and failure summary.
  - [x] Exit nonzero when any required check fails while still writing the summary file.
  - [x] Add tests for passing and failing dry-run summaries.
- [x] Add dependency freshness and vulnerability checks to the RC checklist.
  - [x] Inventory NuGet package versions, .NET SDK/runtime version, Playwright package/browser versions, and bundled Node runtime versions.
  - [x] Run `dotnet list package --vulnerable` or equivalent vulnerability checks during RC verification.
  - [x] Record dependency check results in diagnostics and RC dry-run JSON output.
  - [x] Add self-tests or fixture tests that prove stale/vulnerable dependency output fails the RC checklist.

## Remaining hardening backlog

- [ ] Add release code-signing enforcement and Authenticode verification to publish verification, installer builds, and the RC checklist.
  - [ ] Fail RC verification when the release executable or installer is unsigned or signed with an unexpected certificate subject/thumbprint.
  - [ ] Record signature metadata in release provenance and diagnostics.
- [x] Add installer/runtime sidecar ACL validation.
  - [x] Verify encrypted runtime sidecars are installed read-only for normal users where appropriate.
  - [x] Verify writable runtime directories live under the approved per-user or ProgramData fallback locations.
  - [x] Add installer verification that rejects missing encrypted XP/settings sidecars.
- [ ] Add automated dependency freshness policy checks beyond vulnerability scanning.
  - [ ] Compare NuGet and Playwright versions against the latest available package metadata.
  - [ ] Warn or fail RC verification when dependencies exceed an approved age threshold.
  - [ ] Record stale dependency findings in dependency inventory JSON.
- [ ] Add authenticated-source tamper detection for fetched Obsidian/RPOL content.
  - [ ] Persist source hashes for last-known-good downloaded markdown, sitemap, keyword, and RPOL export inputs.
  - [ ] Detect unexpected structural changes and show player-safe recovery guidance.
  - [ ] Keep previous good content available when newly fetched content fails integrity or shape checks.
- [ ] Add backup/restore hardening for user-writable runtime data.
  - [ ] Create bounded rotating backups before modifying user settings, indexes, exports, and encrypted sidecars.
  - [ ] Add startup recovery that can restore the newest valid backup after corruption or interrupted writes.
  - [ ] Add focused tests for backup selection, rollback, and retention limits.
- [ ] Add CI/release pipeline enforcement for the local hardening scripts.
  - [ ] Run publish verification, RC self-tests, secret scan, dependency checks, and diagnostics verification in CI.
  - [ ] Upload redacted verification artifacts for failed CI runs.
  - [ ] Block release tags unless the RC checklist passes.
