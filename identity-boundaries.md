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
