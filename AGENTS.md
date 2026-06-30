# Repository instructions

- Operating system: Windows 11
- Language: C#
- Framework: .NET
- This is a C#/.NET repository.
- The user is on Windows using PowerShell.
- Do not assume macOS, Linux, Xcode, Swift, or iOS unless files prove it.
- Prefer PowerShell commands.
- Inspect `.sln`, `.csproj`, `.props`, `.targets`, and `.cs` files before making language/framework assumptions.
- Build the executable and related output files under the repository root `Release` folder instead of under `Debug`.

# graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, invoke the `skill` tool with `skill: "graphify"` before doing anything else.
When the user types '/build', build the executable app in both the \Release directory and the \publish directory; publish sequentially, so there isn't a race to to access a DLL file

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
- When I type `run app briefly`, run the app with hero images suppressed only for that next app execution, then restore normal behavior immediately afterward. Do not terminate the app automatically; leave the GUI running until the user closes it.
- When I type `run app`, run the app normally with hero images enabled.
- When you run the app to test a code change, you should skip displaying the hero images

# Project Constraints
- Do not read or scan the /bin or /obj directories.
- Focus strictly on source files inside /src or specific .cs files mentioned.
