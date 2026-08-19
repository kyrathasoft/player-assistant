# Identity release checklist

Use this checklist for every desktop release that contains identity, XP, party, briefing, or password-sidecar changes.

## Tests and authorization boundaries

- [ ] Run the full custom regression harness and confirm the identity cases pass.
- [ ] Confirm exact canonical-ID success and same-first-name cross-password denial.
- [ ] Confirm ambiguous aliases and first-name-only login or hero-selection inputs fail closed.
- [ ] Confirm explicit unique aliases, Dungeon Master scope, party XP visibility, hero selection, briefing activity, encrypted-note access, and account switching.
- [ ] Confirm protected calls carry the authenticated canonical identity rather than the originally entered name.

## Sidecar and migration contracts

- [ ] Validate `xp-passwords.json` schema version 2, salted PBKDF2-HMAC-SHA256 hashes, unique canonical IDs, unique canonical names, and collision-free explicit aliases.
- [ ] If migrating a legacy encrypted sidecar, create and retain a rollback copy until the migrated sidecar has passed validation and an authenticated smoke test.
- [ ] Never infer a canonical ID, role, or authorization scope from a display name, first name, filename, or generated slug.

## Release and installer verification

- [ ] Build Release and publish sequentially to `Release\\` and `Release\\publish\\`.
- [ ] Run publish verification, runtime-sidecar verification, installer/package verification, and release-manifest/hash checks.
- [ ] Confirm the installer payload includes the validated `xp-passwords.json` sidecar without plaintext passwords.
- [ ] Confirm generated roster paths prefer canonical IDs and legacy filename handling remains migration-only.
- [ ] Record the commit, artifact hashes, and verification results before deployment.
