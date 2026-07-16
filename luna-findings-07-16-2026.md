# Source Review Findings

## Findings

- **High: TLS validation is disabled for RPOL browser authentication.** `RpolAuthUtility.cs:327` sets `IgnoreHTTPSErrors = true`, allowing credentials and session state to pass through an untrusted certificate. Set it to `false` and handle certificate failures explicitly.

- **High: Portable settings encryption uses keys embedded in the executable.** `LocalSettingsUtility.cs:19` and `LocalSettingsUtility.cs:444` derive fixed AES/HMAC keys from a public constant. Anyone with the application can decrypt `settings.local.json`. Use DPAPI, Windows Credential Manager, or a per-install secret unavailable to other users.

- **Medium: HTML response limits are applied after buffering.** `NetworkRequestUtility.cs:74` defaults to `ResponseContentRead`, while `HtmlUtility.cs:41` relies on that default. A server can therefore cause large memory allocation before the 5 MB limit is checked. Use `ResponseHeadersRead` and stream through the bounded copy helper.

- **Medium: Raw exception messages can reach users.** `Program.cs:39` and `UiOperationFailureReporter.cs:37` display unredacted exception text. Use `SensitiveTextRedactionUtility.Redact(...)` before status-bar or dialog output.

- **Medium: Keyword-index updates are not transactional with their integrity sidecar.** `KeywordIndexCrawler.cs:867` promotes the index before writing its sidecar at line 868. A failure between those operations leaves mismatched index and integrity metadata. Stage both files and commit them together, or add startup recovery for incomplete transactions.

- **Medium: The test harness is not reproducible from a normal build.** The full runner compiled successfully but exited with 20 failures, including missing `Release\\to-orcish.exe`, missing publish/update artifacts, stale expected version `0.9.1` versus current `0.9.4`, outdated network fixtures, and a search test invoking the wrong event path. Split unit tests from artifact/integration tests and make required setup explicit.

## Conclusion

The most important corrections are restoring TLS validation, replacing the embedded portable encryption key, and making network limits genuinely streaming. The application builds cleanly, but the current regression suite cannot serve as a reliable release gate until its artifact setup and stale expectations are corrected.
