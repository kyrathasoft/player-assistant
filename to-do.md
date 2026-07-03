- [x] code app feature to search online vault by using the JSON library file 'sitemap-keyword-urls.json'
- [x] add menu item to display the regional map
- [x] build method GetSearchTerms() to extract search terms when btnSearch is clicked
- [x] string[] SearchTerms should accept the output of method GetSearchTerms()
- [x] code a method that, given a search term, will return a list of URLs from the JSON library file 'sitemap-keyword-urls.json' that match the search term
- [x] add terms from Locations index (on obisidian wiki) to game-posts-key-terms.md

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

- [ ] Add an RC commit/tag checklist script that verifies clean intended diffs, runs the focused hardening tests, confirms both executable versions, and prints the exact `v0.9.0-hardening.5-rc1` tagging commands without mutating Git state.
- [ ] Add a published-folder runtime integrity check that verifies no startup run from `Release\publish` modifies or deletes parent `Release` artifacts, using before/after file manifests for tracked runtime files.
- [ ] Add a diagnostic bundle script that collects `startup-health.json`, `startup-errors.log`, version metadata, publish verification output, and smoke verification output into a redacted timestamped zip for troubleshooting.
