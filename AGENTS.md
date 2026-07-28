# Repository

## Environment
- OS: Windows 11
- Language: C#
- Framework: .NET
- Shell: PowerShell
- Prefer PowerShell commands and PowerShell-safe quoting.
- Use single quotes for literal arguments (e.g. git commit messages).
- Do not assume macOS, Linux, Swift, Xcode, or iOS.
- Inspect `.sln`, `.csproj`, `.props`, `.targets`, and `.cs` files before making framework assumptions.

## Build
- Build Release only.
- Output executables and artifacts under `Release\`.
- `/build`:
  1. Build `Release\`
  2. Publish `Release\publish\`
  3. Run sequentially (avoid DLL lock/race issues).

## Startup
- Read `to-do.md` at the start of every coding session.
- Use it for priorities, hardening status, and backlog context.
- If a backlog task updates `to-do.md`, include it in the commit.
- If a user question includes the word `Jelenneth`, read `jelenneth.md` before answering.
- Read `magic-item-tracking.md` before changing magic-item data or packaging. `pwa\magic-items.json` is the canonical offline fallback and must be copied beside the executable in `Release\` and included with other critical installer files.

## Commits
- Never commit installer files unless explicitly requested.
- Never commit incidental generated artifacts.
- Commit only intentional source or tracked data changes.
- Work from and push directly to `master`, bypassing the branch/PR workflow unless the user instructs otherwise.

## Response Style
- Be concise.
- Default verbosity: low.
- Use bullets, not prose.
- Do not explain reasoning unless asked.
- Do not restate the request.
- Avoid introductions, conclusions, and filler.
- Keep code-change summaries to 3–5 bullets.
- If one line is enough, use one line.
- For code explanations, use bullet points only.
- Before acting, do not describe intended actions.
- After acting, provide only a brief summary.
- Do not display commands.
- Do not display shell invocations.
- Do not display tool calls.
- Report only results. 

## Reporting
Do not show:
- Commands
- Command output
- Grep/search results
- File-inspection transcripts
- Line lookups

Never start sections with:
- Ran
- Executed
- Command

Report only:
- Files modified
- Files inspected (if relevant)
- Results / conclusions

# Graphify

Knowledge graph location: `graphify-out/`

## Commands
- `/graphify` → invoke Graphify before anything else.
- If `graphify-out/graph.json` exists:
  - Use `graphify query "<question>"` first for codebase questions.
  - Use `graphify path "<A>" "<B>"` for relationships.
  - Use `graphify explain "<concept>"` for focused concepts.
- Prefer Graphify over broad source scans.

## Graph Files
- Dirty `graphify-out/` files are expected.
- Do not skip Graphify because graph files are dirty.
- Skip only when:
  - Working on graph correctness/staleness.
  - User explicitly says not to use Graphify.

## Navigation
- Prefer `graphify-out/wiki/index.md` for discovery.
- Use `graphify-out/GRAPH_REPORT.md` only for architecture review or when Graphify queries are insufficient.

## Updates
After code changes:
```text
graphify update . --no-cluster
