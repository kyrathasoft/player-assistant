# Identity boundaries

## Protected account identity

Protected data must use the authenticated stable canonical ID or character key with ordinal/exact matching. Display names, first names, user-entered login text, generated slugs, and undeclared aliases must not grant access.

- Desktop XP rows require a unique `Canonical ID` and are matched only to `XpAuthenticatedIdentity.CanonicalId`.
- Party and My Hero Briefing resolution use canonical IDs. Character-tagged notes use the authenticated registry's canonical name plus explicitly declared aliases.
- Dungeon Master scope is assigned only to canonical ID `dungeon-master`; the display text `Dungeon Master` does not grant scope.
- Broker accounts require explicit `character_key` and aliases. New imported accounts also require an explicit role; role-less legacy reimports preserve the existing stable account role. Imports update by an unambiguous `character_key`; display-name and alias namespace collisions fail closed.
- Broker XP source labels resolve only through the explicit `xp.character_key_aliases` mapping. Unmapped labels fail closed.
- PWA magic-item viewer tokens are exact canonical character keys. Substring and inferred-first-name matching are forbidden.

## Public display and search aliases

These aliases affect only public presentation or search results and must never be reused as protected account identity:

- `Form1.GetHeroSearchTermAliases` expands public RPOL and Obsidian searches with full and first names.
- Adventure-outline author matching summarizes public campaign prose.
- `pwa/data/heroes.json` names and aliases choose public hero token artwork and wiki links.
- My Hero Briefing explicit aliases classify already-authorized public thread activity and quick links after canonical hero resolution.

## Migration-only compatibility

`PartyHeroUtility` may inspect full-name or collision-free legacy first-name markdown filenames only when the parsed full character name equals the roster's full name. Canonical-ID filenames take precedence. Legacy encrypted-sidecar conversion creates generated IDs only as migration input; production migration must replace those with reviewed stable IDs before deployment.

## Audited identity-use classification

The remaining name-bearing paths were reviewed against the canonical-ID boundary:

| Area | Protected identity rule | Permitted name use |
| --- | --- | --- |
| `Form1` | Authentication returns `XpAuthenticatedIdentity`; XP, party, and briefing calls carry that result or a canonical hero selection ID. Dungeon Master scope is checked on the returned identity. | Credential text is accepted only at the login boundary. `GetHeroSearchTermAliases` expands public corpus searches only. |
| `XpTrackingUtility` | Protected XP selection requires exactly one ordinal canonical-ID match. Missing or duplicate IDs fail closed. | Names are display text and user-facing error context only. |
| `PartyHeroUtility` | Player and Dungeon Master XP joins require unique ordinal canonical IDs. | Names render party sheets. Full-name and collision-free first-name filenames are migration-only fallbacks whose parsed full name must match the roster. |
| `MyHeroBriefingUtility` | Authenticated and Dungeon Master-selected heroes, XP totals, and identity-registry aliases resolve only by unique ordinal canonical ID. Mutable roster display names do not participate in the registry join. | Canonical names and explicitly declared aliases classify already-authorized public activity, quick links, and character-tagged note metadata. |
| `TaggedNoteCipherUtility` | Character-tag access receives only the canonical name and explicit aliases attached after canonical-ID resolution. It does not infer first names or authorize from raw login text. | Exact character-name and explicit-alias comparisons interpret authored note tags after authorization. |
| Hero roster and generated assets | New roster-backed markdown paths prefer canonical IDs; protected consumers reject missing IDs. | Full names, token names, and `pwa/data/heroes.json` aliases are presentation/search data. A full-name path is a non-authorizing compatibility fallback when no ID exists. |
| Account import and broker services | Schema-v2 imports carry explicit canonical IDs/`character_key` values and aliases unchanged; broker upserts and protected service joins use the stable account ID or exact character key. | Canonical names and aliases are login/display namespace entries only. The import script does not derive authorization IDs from names. |
| XP source-label mapping | Published labels must resolve through the explicit `xp.character_key_aliases` map; unmapped or ambiguous values fail closed. | Labels remain source presentation text and never become implicit account aliases. |

Adventure-outline author matching and public RPOL/Obsidian search continue to use names because they summarize or discover public campaign prose; those results never grant protected access.
