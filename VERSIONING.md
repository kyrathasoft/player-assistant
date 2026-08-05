# Version metadata

`version.props` is the canonical source for desktop, installer, release-artifact, PWA display, browser-resource, and service-worker cache versions.

- Change desktop and PWA release identities only in `version.props`.
- Increment `PlayerAssistantPwaAppRevision` when `pwa/app.js` changes.
- Increment `PlayerAssistantPwaStylesRevision` when `pwa/styles.css` changes.
- Increment `PlayerAssistantPwaMetadataRevision` when the metadata script contract changes.
- Increment `PlayerAssistantPwaCacheRevision` for every deployed PWA shell release.
- Run `python verify-version-metadata.py --write` to regenerate the checked-in PWA projections.
- Run `python verify-version-metadata.py` to reject drift without modifying files.

MSBuild imports `version.props` directly. PowerShell release tooling reads it through `version-metadata.ps1`. The packaged installer derives its displayed version from the verified payload executable. CI loads the same metadata before naming release artifacts.
