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

## Step 7 audit — remaining name uses

The remaining name-based operations were audited and classified as follows:

| Location | Name-based operation | Classification and boundary |
|---|---|---|
| `Form1.GetHeroSearchTermAliases` | Expands full-name and first-name search terms | Public search only; it does not select protected records or grant access. |
| `AdventureOutlineUtility` | Uses author full names and first names while generating campaign summaries | Public campaign-prose summarization only; no authorization or protected-data selection. |
| `PostTotalsUtility` | Groups saved post counts and login rows by display name | Reporting/presentation only; it does not authorize XP, notes, briefing data, or account access. |
| `PartyHeroUtility.FindHeroMarkdownPath` | Checks canonical, full-name, and legacy first-name filenames | Migration-only compatibility; canonical IDs take precedence, legacy paths are collision-checked and parsed-name validated. |
| `MyHeroBriefingUtility` | Uses resolved hero names and explicit aliases for thread activity, response detection, and tagged-note metadata | Runs only after canonical identity resolution; aliases classify already-authorized public activity and note metadata. Name-only authenticated or Dungeon Master selection is rejected. |
| `TaggedNoteCipherUtility` | Matches `Character`/`Hero`/`Name` tags against the resolved hero name and explicit aliases | Post-resolution authorization context only; inferred first-name aliases are not created. |
| `Form1`, `PartyHeroUtility`, `XpTrackingUtility`, and `MyHeroBriefingUtility` | Protected XP, hero, and briefing selection | Uses `XpAuthenticatedIdentity.CanonicalId` and exact canonical-ID joins; user-entered or display names are not carried into protected lookups. |

No remaining authorization boundary uses a first name, fuzzy name, generated slug, or unvalidated display name. The regression catalog includes same-first-name fixtures, name-only rejection, ambiguous-alias rejection, canonical-ID joins, stale-display-name protection, and name-only Dungeon Master selection rejection.
