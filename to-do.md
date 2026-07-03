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
