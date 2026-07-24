# PC Login Dashboard on Programmer's Website

Status: character authentication and protected current-XP dashboard implemented.

## Implemented authentication

- PWA character-name/password dialog, session restoration, authenticated identity display, and explicit logout.
- `POST /v1/login`, `GET /v1/session`, protected `GET /v1/me`, and CSRF-protected `POST /v1/logout`.
- Private SQLite character accounts with random IDs, normalized names, stable character authorization keys, player/DM roles, enabled state, and password timestamps.
- Existing `xp-password-hashes-v1` PBKDF2-HMAC-SHA256 hashes accepted through an administrator-only import endpoint.
- Automatic migration to PHP `password_hash()` storage after the first successful legacy-password login.
- Strict server sessions with `Secure`, `HttpOnly`, `SameSite=Strict`, path-restricted cookies, session-ID regeneration, idle expiry, and absolute expiry.
- Generic authentication failures, account-and-address throttling, bounded lockout, and redacted authentication auditing.
- Exact-Origin validation for login/logout, CSRF validation for logout, JSON content-type enforcement, HTTPS/HSTS, no-store responses, and restrictive browser headers.
- Administrator-only account listing, creation, enable/disable, role/key changes, and password reset endpoints.
- PWA service-worker exclusion for all authentication API requests; protected responses are never cached for offline use.
- Focused PHP authentication tests under `web-deploy/tests/character-auth-tests.php`.

## Implemented protected XP dashboard

- Protected `GET /v1/xp` route requiring a current enabled character session.
- Fixed server-configured Obsidian Publish source; browsers cannot supply or receive the source URL.
- HTTPS-only fetches, redirect rejection, strict host/path allowlisting, short connection and request timeouts, response-size limits, and narrow content-type acceptance.
- Latest `As of` markdown-table validation before any snapshot is cached or returned.
- Player authorization by the server-stored character key, with exactly one matching total required.
- Fail-closed handling for missing, invalid, or ambiguous mappings.
- Dungeon Master role access to the validated current party totals.
- Last-known-good server cache with a bounded stale window; stale responses are labeled.
- PWA **Current XP** card, explicit refresh, authenticated-session restoration, safe text rendering, and DM party table.
- API-wide `Cache-Control: no-store`; service-worker exclusion for `/scarlethorizons/api/`.
- Focused service, routing, malformed-source, cache, stale-fallback, and authorization tests.

## Implemented files

- `web-deploy/player-assistant-broker/BrokerHttpException.php`
- `web-deploy/player-assistant-broker/CharacterAuthService.php`
- `web-deploy/player-assistant-broker/XpTrackingService.php`
- `web-deploy/player-assistant-broker/BrokerService.php`
- `web-deploy/bryanmiller.us/scarlethorizons/api/index.php`
- `web-deploy/import-character-accounts.ps1`
- `pwa/index.html`, `pwa/app.js`, `pwa/styles.css`, and `pwa/service-worker.js`

## Goal

Create a PHP application on the programmer's website where a player can sign in with a username and password and view only the offsite campaign data that the authenticated player is authorized to see.

The website must fetch the offsite data on the server. A player's browser must not receive the offsite URL, upstream credentials, unfiltered records, or records belonging to another player.

## Remaining protected-data decisions

- Accounts are administrator-created; self-registration is intentionally excluded.
- Decide which protected fields beyond current XP should be added.
- Decide whether password reset is administrator-assisted or email-based. Do not implement security questions.

## Protected dashboard architecture

Keep this feature in a separate web application directory rather than adding authentication concerns to `web-translator`.

Suggested components:

- `public/index.php`: login or authenticated dashboard entry point.
- `public/login.php`: login form and authentication POST handler.
- `public/logout.php`: POST-only logout handler.
- `src/AuthService.php`: password verification, account state, throttling, and session creation.
- `src/AuthorizationService.php`: maps the authenticated account to permitted player and character identifiers.
- `src/OffsiteDataClient.php`: retrieves only a fixed, configured upstream HTTPS endpoint.
- `src/DashboardService.php`: validates, filters, and shapes upstream data into a player-safe view model.
- `src/Database.php`: parameterized database access.
- `config/`: non-public configuration schema; production secrets must be injected outside the document root or through host-provided secret configuration.
- `templates/`: escaped server-rendered login, error, and dashboard views.
- `tests/`: authentication, authorization, upstream validation, and failure-path tests.

The normal request flow should be:

1. Verify the session and account state.
2. Resolve the authenticated account's permitted player or character identifiers on the server.
3. Fetch a fixed allowlisted upstream endpoint with strict time and size limits, or use a recent validated cache.
4. Validate the upstream content type and schema before using any values.
5. Filter the data by the server-side authorization mapping.
6. Render only the filtered view model with HTML escaping and restrictive private-data response headers.

## Account and password handling

- Store a normalized unique username and a PHP password hash; never store plaintext or reversibly encrypted passwords.
- Use `password_hash()` with `PASSWORD_DEFAULT`, `password_verify()`, and `password_needs_rehash()`.
- Allow enough database space for password hashes to grow as PHP changes the default algorithm.
- Use parameterized SQL for every database operation.
- Return the same generic login failure response for unknown usernames, incorrect passwords, disabled accounts, and other authentication failures.
- Add server-side throttling keyed conservatively by account and request source, with bounded temporary lockout and security logging.
- Do not log passwords, session identifiers, upstream credentials, or complete private dashboard records.
- Require a deliberate administrator workflow to create, disable, and reset accounts.

Suggested minimum account fields:

- Random internal user ID.
- Unique normalized username.
- Password hash.
- Role, initially `player` or `dm`.
- Stable permitted-player or permitted-character key.
- Enabled or disabled state.
- Password-changed timestamp.
- Last successful login timestamp.
- Failed-attempt and temporary-lockout state, if not stored in a separate throttling table.

## Session and request security

- Require HTTPS for the entire application and enable HSTS after HTTPS deployment is confirmed.
- Use PHP strict session mode and a non-default generic session-cookie name.
- Set session cookies with `Secure`, `HttpOnly`, and an appropriate `SameSite` policy; restrict cookie path and omit a broad cookie domain.
- Regenerate the session ID immediately after successful login and after any privilege change.
- Enforce both idle and absolute session timeouts on the server.
- Make logout a CSRF-protected POST action, invalidate the server-side session, expire the cookie, and return `Cache-Control: no-store`.
- Require CSRF tokens for every state-changing form.
- Apply authorization on every protected request; never rely on a hidden field, URL parameter, or unguessable record identifier.
- Deny access by default when the account-to-player mapping is missing, ambiguous, disabled, or inconsistent with upstream data.
- Escape output for its HTML context and use a restrictive Content Security Policy, frame protection, referrer policy, and MIME-sniffing protection.

## Offsite data boundary

- Configure the upstream URL on the server; do not accept a URL or hostname from a player request.
- Require HTTPS and an exact hostname, port, and path allowlist.
- Do not automatically follow redirects. If redirects are ever required, validate every redirect destination against the same allowlist.
- Block loopback, link-local, private-network, metadata-service, and unexpected resolved addresses unless a specifically reviewed private endpoint is required.
- Use short connection and overall timeouts, a strict maximum response size, and a narrow accepted content type.
- Validate the complete response schema before replacing cached data or rendering any value.
- Preserve the last known good validated cache when the upstream service is unavailable or returns malformed data.
- Keep upstream credentials outside the document root and out of Git. Send them only from the server-side client.
- Never proxy arbitrary upstream responses directly to the browser.
- Record safe operational telemetry such as fetch success, duration, cache age, and a redacted failure category.

## Player-facing behavior

- Mobile-responsive login and dashboard pages.
- Clear generic login errors that do not reveal whether an account exists.
- Clear session-expired and upstream-unavailable messages.
- Dashboard identity heading so the player can recognize whose data is displayed.
- Last-refreshed and cache-age information without exposing the upstream URL.
- Explicit logout control.
- No self-registration, account enumeration, upstream debugging details, or unfiltered data in the first release.

## Acceptance criteria

- A valid enabled account can log in and log out.
- Passwords are stored only as modern PHP password hashes.
- Failed login responses do not disclose whether a username exists.
- Session fixation, missing-CSRF, expired-session, and disabled-account tests fail safely.
- A player can see the intended record or records and cannot retrieve another player's data by changing any request value.
- A Dungeon Master view, if implemented, is explicit and separately authorized.
- The browser never receives the upstream URL, upstream credentials, or unfiltered upstream response.
- Unallowlisted URLs, redirects, resolved addresses, oversized responses, unexpected content types, invalid schemas, and timeouts are rejected.
- A validated last-known-good cache remains available during a temporary upstream outage when permitted by the chosen cache policy.
- Private responses use `Cache-Control: no-store` and appropriate security headers.
- Logs and error pages contain no passwords, tokens, session IDs, upstream secrets, or private payloads.
- The login and dashboard remain usable on phones, tablets, and computers.

## Verification plan

- Unit tests for username normalization, password verification, rehash decisions, account state, authorization mappings, schema validation, and redaction.
- Integration tests using a fixture database and controlled upstream server.
- Negative tests for brute-force throttling, session fixation, CSRF, IDOR, SQL injection, XSS, SSRF, redirect bypass, DNS/address bypass, oversized payloads, malformed data, and upstream outages.
- Manual tests for account provisioning, password reset, logout, session expiration, responsive layout, keyboard navigation, and screen-reader labels.
- Deployment verification confirming HTTPS, cookie flags, secret location and permissions, database permissions, PHP configuration, private caching headers, and disabled production error display.

## Security references

- [PHP password hashing API](https://www.php.net/manual/en/book.password.php)
- [PHP sessions](https://www.php.net/manual/en/book.session.php)
- [OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- [OWASP Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html)
- [OWASP Authorization Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html)
- [OWASP CSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html)
- [OWASP SSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html)
