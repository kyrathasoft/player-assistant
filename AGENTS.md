# Repository instructions

- Operating system: Windows 11
- Language: C#
- Framework: .NET
- This is a C#/.NET repository.
- The user is on Windows using PowerShell.
- Always use PowerShell-safe quoting for shell commands; prefer single-quoted arguments for literal strings such as Git commit messages.
- Do not assume macOS, Linux, Xcode, Swift, or iOS unless files prove it.
- Prefer PowerShell commands.
- Inspect `.sln`, `.csproj`, `.props`, `.targets`, and `.cs` files before making language/framework assumptions.
- Build the executable and related output files under the repository root `Release` folder instead of under `Debug`.
- At the beginning of each coding session, read `to-do.md` so current completed hardening work, active backlog notes, and project priorities are in context before making changes.
- When committing work from the to-do.md backlog, always include `to-do.md` in the commit if it has been updated.
- Never commit installer files unless the user specifically tells you to commit installer files.
- When explaining code you have written, just give me bullet points, not full sentences unless I ask you to expand the text.

# graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, invoke the `skill` tool with `skill: "graphify"` before doing anything else.
When the user types '/build', build the executable app in both the \Release directory and the \publish directory; publish sequentially, so there isn't a race to to access a DLL file

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update . --no-cluster` to keep the graph current quickly (AST-only, no API cost). Run `graphify cluster-only . --no-viz --no-label` only when refreshed communities, report analysis, or labels are explicitly needed.
- When I type `run app briefly`, run the app with hero images suppressed only for that next app execution, then restore normal behavior immediately afterward. Do not terminate the app automatically; leave the GUI running until the user closes it.
- When I type `run app`, run the app normally with hero images enabled.
- When you run the app to test a code change, you should skip displaying the hero images

# Project Constraints
- Do not read or scan the /bin or /obj directories.
- Focus strictly on source files inside /src or specific .cs files mentioned.
- Before adopting a new Orcish lexicon term, first check for exact collisions and close-form conflicts against existing translator entries and affix patterns.
- Run the proposed entry through `OrcishTranslatorUtility.ReviewProposedLexiconEntry()` before adding it. Do not add entries with review issues; intentional shared forms, close forms, exceptional roots, or compounds require the corresponding explicit review tag documented by the validator.
- Reject any candidate whose English term contains three or more consecutive copies of the same character unless the user explicitly approves that exact term; record that approval with the validator's `repeated-character-user-approved` tag.
- Reject the `pornography` English family from Orcish translation; Orcish has no pornography vocabulary.
- Before adding a new explicit Orcish lexicon word, first consult the morphology rules engine in `OrcishTranslatorUtility.cs`; use or extend a reusable rule for predictable plural, possessive, past, progressive, or present forms instead of hand-adding a derived entry.
- For Orcish lexicon additions, prefer extending existing roots, compounds, plural patterns, and affix meanings instead of inventing unrelated forms when an established pattern already fits.
- When an English term is better represented as a fixed Orcish phrase or compound, add the full entry explicitly rather than assuming the CLI translator will compose it from separate words.
- Keep part of speech and sense distinctions explicit in the lexicon when meaning depends on usage, such as noun vs. verb vs. adjective/complement or singular vs. plural vs. possessive.
- When a new term has multiple plausible senses, preserve nuance with tags and grammar classes that support context-aware selection in `ToOrcish/Program.cs` rather than collapsing distinct meanings into one entry.
- Use `codex-scratch\candidates.txt` as the current curated backlog for Orcish lexicon work; remove an item only when the exact remaining candidate has been covered, not merely a related root word.
- Whenever Orcish translation work is being pursued, check root `dont-scrape-again.md` before selecting or scraping wiki URLs, and add every newly selected URL there so previously used pages are not selected again.
- After lexicon edits, verify representative terms with `to-orcish`; if Debug outputs are locked, use a Release build artifact for confirmation instead of assuming the change worked.
- The native app embeds `web-translator\orcish-lexicon.json` for fast translator startup. After lexicon edits, run `web-translator\export-lexicon.ps1` before the final app build so the embedded snapshot stays synchronized with `OrcishTranslatorUtility`.

# Generated artifact update policy
- Treat root `keyword-index.json` and `sitemap-keyword-urls.json` as the tracked campaign search snapshots. They may be committed only after an intentional crawl/index refresh, schema change, or release-data refresh.
- Do not commit incidental changes to generated runtime output under `Release`, `Release\publish`, `publish`, `publish-msbuild`, installer payloads, diagnostics bundles, startup health/log files, crash files, or outbound network diagnostics unless the user explicitly asks for a release artifact commit.
- When an intentional app run refreshes `Release\keyword-index.json` or `Release\sitemap-keyword-urls.json`, copy the reviewed Release copy back to the matching root tracked snapshot before committing so Git preserves the current reproducible search data.
- Before committing refreshed generated snapshots, verify the JSON parses, confirm the change is expected from the crawl/index source data, and run the relevant release checks that consume the files: publish verification, Release/publish parity, or published-folder runtime integrity as appropriate.
- Keep `settings.local.json`, `xp-passwords*.json`, and other credential-bearing sidecars ignored and local. The publish script may stage encrypted copies into `Release\publish`, but those staged copies remain generated distribution output.

Do not display executed commands, command output, grep results, line-number lookups, or file-inspection transcripts.

Never include blocks beginning with:
- Ran
- Executed
- Command

Report only:
- Files modified
- Files inspected (if relevant)
- Results and conclusions

