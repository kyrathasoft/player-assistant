# Scrape for Orcish Translation Candidates

This is the durable runbook for repeating the random Obsidian sitemap-to-Orcish workflow after a context reset. Do not rely on remembered counts, prior chat messages, or an old `Release` assembly. Recalculate all state at the start of every batch.

## Outcome

A completed batch:

1. Selects the requested number of unused Scarlet Horizons sitemap URLs at random.
2. Resolves and downloads the Markdown endpoint for every selected page.
3. Extracts English word candidates while removing Markdown and web scaffolding.
4. Drops every English term already supported by the current Orcish lexicon.
5. Manually culls names, malformed tokens, implementation vocabulary, and other unsuitable candidates.
6. Derives plausible near-kin forms and filters them again against the lexicon.
7. Adds reviewed Orcish translations without violating collision or morphology policy.
8. Records the successfully scraped URLs in `dont-scrape-again.md`.
9. Updates regression counts and the Orcish history in `to-do.md`.
10. Builds and verifies the Release translator, refreshes Graphify, and, when requested, commits and pushes the batch.

## Authoritative files

- `AGENTS.md`: repository policy, including Orcish admission and Release verification rules.
- `dont-scrape-again.md`: the authoritative no-repeat URL ledger.
- `OrcishTranslatorUtility.cs`: lexicon, morphology, affixes, review entry point, and generated batch builders.
- `ToOrcish\Program.cs`: sentence-level context and sense selection.
- `PlayerAssistant.Tests\Program.cs`: translator regression registration, batch counts, and total English-term count.
- `to-do.md`: durable history of completed batches and their counts.
- `codex-scratch\candidates.txt`: current curated backlog; it must be empty when the entire batch has been verified and adopted.
- `codex-scratch\sample-wiki-orcish-candidates.ps1`: optional local scrape helper.
- `codex-scratch\derive-near-kin.py`: optional local Hunspell-family helper.

`codex-scratch` is intentionally untracked. It is a work area, not durable history. The tracked ledger, translator, tests, `to-do.md`, and this runbook must contain enough information to recover after scratch files or conversation context disappear.

## Current snapshot

Snapshot after the third 50-page batch on 2026-07-17:

- Sitemap URLs: 972
- Recorded used URLs: 352
- Remaining URLs: 620
- Unique supported English terms: 17,138
- Last batch commit: `edd7bc8`

These values are historical only. Always recalculate them before the next batch.

## Non-negotiable rules

- Work from `C:\repos\player-assistant` in PowerShell.
- Read `AGENTS.md`, `to-do.md`, and `dont-scrape-again.md` before selecting URLs or changing the lexicon.
- Do not scan `bin` or `obj`.
- Use RTK for noisy Git/search/test output when practical.
- Normalize URLs before comparing them. Treat `+` as a space, decode percent escapes, and ignore a trailing slash.
- Record only URLs whose Markdown endpoint was successfully downloaded. Replace empty pages so the requested batch size contains usable pages.
- Treat the running Release assembly as the authoritative existing lexicon. Source-text regex matching is only a preliminary filter because generated batch constants are not expressed as `new(...)` calls.
- Before adding a term, consult morphology and existing roots, then run it through `OrcishTranslatorUtility.ReviewProposedLexiconEntry()` or the equivalent internal `OrcishLexiconReviewUtility.EnsureCanAdd()` path.
- Do not add entries with unresolved review issues. Intentional exceptions require the validator's documented explicit tags.
- Prefer existing roots, compounds, and reusable morphology over unrelated forms.
- Do not hand-add a predictable plural, possessive, past, progressive, or present form when the morphology engine already covers it.
- Keep noun, verb, adjective, complement, singular, plural, and sense distinctions explicit when meaning depends on usage.
- Do not commit `Release`, `Release\publish`, scratch artifacts, installers, logs, diagnostics, or credential-bearing sidecars.
- Modify tracked files with `apply_patch`.

## Batch naming

Choose identifiers before starting. Examples:

```text
Batch slug:       batch-50d
Ledger heading:   Fourth random 50-page translation batch
Method stem:      FourthFiftyPage
Source tag:       fourth-fifty-page-sample
Near-kin tag:     fourth-fifty-page-near-kin
Orcish prefix:    a new collision-free prefix
```

Search the translator before adopting a prefix:

```powershell
rtk rg '<prefix>-' OrcishTranslatorUtility.cs
```

Exit code 1 with no matches is the desired result. Also inspect existing affix meanings and nearby batch prefixes. A mechanically unused prefix is not sufficient if it conflicts with established Orcish semantics.

## Phase 1: preflight and current-state discovery

1. Confirm the branch and worktree scope.
2. If unrelated changes exist, stop and separate them before staging or editing overlapping files.
3. Build the current Release app so assembly-based filtering reads the latest lexicon.
4. Count the current lexicon dynamically.

```powershell
rtk git status --short
git branch --show-current
dotnet build 'player-assistant.csproj' -c Release -o 'Release' --verbosity minimal
```

Dynamic lexicon count:

```powershell
$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path 'Release\player-assistant.dll'))
$type = $assembly.GetType('PlayerAssistant.OrcishTranslatorUtility')
$method = $type.GetMethod('GetLexiconEntries', [System.Reflection.BindingFlags]'Public,Static')
$entries = $method.Invoke($null, @())
$english = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) { [void]$english.Add($entry.English) }
[pscustomobject]@{
    LexiconEntries = $entries.Count
    UniqueEnglish = $english.Count
}
```

Also count the current backlog:

```powershell
@(Get-Content -LiteralPath 'codex-scratch\candidates.txt' |
    Where-Object { $_.Trim() }).Count
```

Resolve any pre-existing backlog deliberately. Do not silently mix an unrelated backlog into a new scrape.

## Phase 2: fetch the sitemap and select unused URLs

The sitemap is:

```text
https://publish.obsidian.md/scarlethorizons/sitemap.xml
```

Use this normalization function for both sitemap and ledger URLs:

```powershell
$normalize = {
    param($url)
    [Uri]::UnescapeDataString(($url -replace '\+', ' ')).TrimEnd('/')
}
```

Selection template for a 50-page batch:

```powershell
$batchSize = 50
$base = 'https://publish.obsidian.md/scarlethorizons'
[xml]$sitemap = (Invoke-WebRequest -UseBasicParsing "$base/sitemap.xml" -TimeoutSec 30).Content

$urls = @(
    $sitemap.SelectNodes('//*[local-name()="loc"]') |
        ForEach-Object { $_.InnerText.Trim() } |
        Where-Object { $_ -like "$base/*" } |
        Sort-Object -Unique
)

$used = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$ledgerText = Get-Content -Raw -LiteralPath 'dont-scrape-again.md'
[regex]::Matches($ledgerText, 'https://publish\.obsidian\.md/scarlethorizons/[^\s]+') |
    ForEach-Object { [void]$used.Add((& $normalize $_.Value)) }

$remaining = @($urls | Where-Object { -not $used.Contains((& $normalize $_)) })
if ($remaining.Count -lt $batchSize) {
    throw "Only $($remaining.Count) unused URLs remain."
}

$selected = @($remaining | Get-Random -Count $batchSize)
```

Do not use raw string equality. The sitemap and ledger may encode spaces differently while still identifying the same page.

## Phase 3: resolve Markdown endpoints and scrape words

Preferred helper invocation:

```powershell
$json = & 'codex-scratch\sample-wiki-orcish-candidates.ps1' -SelectedUrls $selected
Set-Content -LiteralPath 'codex-scratch\BATCH_SLUG-scrape.json' -Value $json
$scrape = $json | ConvertFrom-Json
```

The helper's required behavior is documented here in case the untracked script is missing:

1. Fetch each public page URL as HTML.
2. Extract the Markdown access URL from this JavaScript preload pattern:

   ```regex
   window\.preloadPage=f\("(?<url>https://[^" ]+?\.md)"\)
   ```

3. Decode `\u0026` to `&` and `\/` to `/` in the captured URL.
4. Fetch that URL with `Accept: text/markdown,text/plain`.
5. Record `PageUrl`, `MarkdownUrl`, and `MarkdownCharacters` for every page.
6. Remove or flatten the following before tokenization:
   - YAML frontmatter
   - fenced code blocks
   - Markdown reference definitions
   - embedded images and attachments
   - ordinary Markdown links while retaining their labels
   - wiki links while retaining label and target text
   - HTML tags
   - literal HTTP(S) URLs
   - HTML entities
7. Tokenize with:

   ```regex
   (?<![A-Za-z])[A-Za-z][A-Za-z'-]{2,}(?![A-Za-z])
   ```

8. Lowercase tokens, trim leading/trailing apostrophes and hyphens, and reduce contractions ending in `'s`, `'d`, `'ll`, `'re`, or `'ve` to their base token.
9. Exclude common English stop words and obvious web/Markdown terms such as `page`, `website`, `wiki`, `obsidian`, `publish`, `markdown`, `frontmatter`, `navigation`, `sidebar`, `footer`, `header`, `html`, `http`, `https`, `www`, `tags`, `aliases`, `attachment`, `canvas`, `stylesheet`, `nbsp`, `callout`, `toc`, and `backlinks`.
10. Return candidates with occurrence counts, sorted by descending frequency and then alphabetically.

### Empty-page replacement

An HTTP success can still yield empty Markdown. Replace every page whose `MarkdownCharacters` is zero and rerun the complete scrape with the retained pages plus new random replacements. Do not merely reduce the batch size.

```powershell
$empty = @($scrape.Pages | Where-Object MarkdownCharacters -eq 0)
```

For replacements, exclude:

- every normalized ledger URL;
- every normalized retained URL in the current batch; and
- every failed or empty URL already attempted during the current batch.

Repeat until the result contains the requested number of non-empty pages. Preserve only the final successful page set in the batch JSON and ledger.

## Phase 4: authoritative exact lexicon filtering

The scraper's source-regex filter is not authoritative. Generated constants are invisible to it. Filter again through the current Release assembly:

```powershell
$scrape = Get-Content -Raw -LiteralPath 'codex-scratch\BATCH_SLUG-scrape.json' | ConvertFrom-Json
$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path 'Release\player-assistant.dll'))
$type = $assembly.GetType('PlayerAssistant.OrcishTranslatorUtility')
$entries = $type.GetMethod('GetLexiconEntries', [System.Reflection.BindingFlags]'Public,Static').Invoke($null, @())

$existing = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) { [void]$existing.Add($entry.English) }

$raw = @($scrape.Candidates | ForEach-Object Word | Sort-Object -Unique)
$remaining = @(
    $scrape.Candidates |
        Where-Object { -not $existing.Contains($_.Word) } |
        Sort-Object @{ Expression = 'Occurrences'; Descending = $true }, Word
)

Set-Content -LiteralPath 'codex-scratch\BATCH_SLUG-raw-candidates.txt' -Value $raw
$remaining | ConvertTo-Json -Depth 3 |
    Set-Content -LiteralPath 'codex-scratch\BATCH_SLUG-exact-remaining.json'
```

Record the raw count and exact-untranslated count in the scratch manifest or working notes.

## Phase 5: manual source-candidate culling

Create `codex-scratch\BATCH_SLUG-source-candidates.txt` from the exact-untranslated list. This step requires judgment; frequency alone is not a quality signal.

Drop:

- public-page and Markdown scaffolding missed by the generic stop list;
- source-code, shell, tooling, and repository-instruction vocabulary when it appears only because a code/how-to page was sampled;
- stat abbreviations such as `str`, `dex`, `int`, `wis`, and `cha`;
- file extensions, command switches, package/tool names, URLs, and concatenated identifiers;
- dates and abbreviations used only as metadata;
- proper personal names, place names, campaign-specific identifiers, and phonetic spellings that are not genuinely useful lexical concepts;
- malformed concatenations, missing-apostrophe forms, broken fragments, and OCR/tokenization debris;
- duplicate singular/plural spelling variants already represented by the lexicon or morphology engine;
- established intentional culls, including `radiation`, `nuclear`, `science`, and the `archontos` exonym.

Usually keep:

- ordinary English nouns, verbs, adjectives, and adverbs even when rare;
- useful fantasy or game-domain common nouns such as creature types, magical practices, professions, equipment, and conditions;
- clear compounds and fixed phrases that need explicit translation;
- culturally specific adjectives or nouns when they are real English concepts rather than a sampled character's name.

Inspect the complete curated list before proceeding. A short automated drop list is only a first pass.

## Phase 6: derive and curate near-kin

The local helper uses the MiKTeX Hunspell English dictionary and affix rules:

```text
C:\Users\Bryan\AppData\Local\Programs\MiKTeX\hunspell\dicts\en_US.aff
C:\Users\Bryan\AppData\Local\Programs\MiKTeX\hunspell\dicts\en_US.dic
```

Confirm both files exist. If the installation path changed, locate the active `en_US.aff` and `en_US.dic` and update only the scratch helper.

Run:

```powershell
python 'codex-scratch\derive-near-kin.py' `
    'codex-scratch\BATCH_SLUG-source-candidates.txt' `
    'codex-scratch\BATCH_SLUG-near-raw.json'
```

The helper:

- parses Hunspell suffix rules;
- reconstructs families for dictionary stems;
- chooses the longest plausible stem for ambiguous matches;
- adds a maintained set of irregular families such as `bought`/`buy`, `chosen`/`choose`, `forgot`/`forget`, `shaken`/`shake`/`shook`, `sworn`/`swear`/`swore`, `written`/`write`/`wrote`, and `worse`/`bad`/`worst`.

Filter near-kin candidates against:

1. the current Release lexicon;
2. the curated source-candidate set; and
3. a reviewed false-family denylist.

Write two files:

```text
codex-scratch\BATCH_SLUG-near-candidates.txt
codex-scratch\BATCH_SLUG-near-families.txt
```

The family file format is:

```text
candidate|source-candidate
```

### Mandatory near-kin review

Inspect the complete family mapping, not only the candidate list. Hunspell stemming can create unrelated lookalikes. Known bad examples include:

- `fibers` producing `fib` or `fiber` through the wrong family;
- `lemme` producing `lemming`;
- `mist` producing `mister`;
- `cubs` producing `cubed` or `cuber`;
- `din` producing `diner` or `dining`;
- `mop` producing `moped` or `moping`;
- `peers` producing `pee`;
- `seams` producing `seamen`;
- `stag` producing `staged` or `staging`;
- a plural source producing invalid possessives such as `worms's`.

At minimum, separately inspect every family whose source is five letters or shorter, then scan the full mapping for semantic drift. Re-run the filter after updating the denylist.

Run the authoritative lexicon filter again after near-kin generation. The final source and near-kin lists must be mutually unique and absent from the current lexicon.

## Phase 7: record the successful URLs

After all selected pages have non-empty Markdown, insert a new section near the top of `dont-scrape-again.md` using `apply_patch`:

```markdown
## BATCH_LEDGER_HEADING

- https://publish.obsidian.md/scarlethorizons/...
```

Record the public `PageUrl`, not the access Markdown endpoint. Preserve every URL exactly as returned in the successful scrape manifest.

Verify:

- ledger line count equals unique normalized ledger count;
- the new section contains exactly the requested batch size;
- no new URL appeared in an earlier section.

## Phase 8: add the Orcish lexicon batch

### First reuse existing language structure

Before generating a new root for a source term:

1. Search exact English and close English forms in `OrcishTranslatorUtility.cs` and the current Release lexicon.
2. Search Orcish roots and affix meanings for an established semantic fit.
3. Use or extend reusable morphology when the form is predictable.
4. Use an explicit full phrase or compound when separate CLI token translation would lose the intended meaning.
5. Preserve part-of-speech and sense distinctions where necessary.

Only terms that genuinely need a new batch root should use the batch prefix.

### Batch data shape

The established large-batch pattern uses two raw string constants and a builder:

```csharp
private const string MethodStemSourceCandidateData = """
source-one
source-two
""";

private const string MethodStemNearKinCandidateData = """
near-form|source-one
other-form|source-two
""";
```

The builder must:

- accept the existing entry sequence;
- maintain `acceptedEntries` as each proposal is admitted;
- assign each source a collision-free Orcish root;
- tag source entries with `wiki-fodder`, the batch source tag, `generated`, `review-promoted`, `close-form-reviewed`, and `family-<source>`;
- create each near-kin form from its source root, currently through `CreateThirtyPageNearKinForm(...)` where an explicit derived form is still necessary;
- tag near-kin entries with `wiki-fodder`, the batch near-kin tag, `near-kin`, `derived-by-rule`, `review-promoted`, `close-form-reviewed`, and `family-<source>`;
- call `OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries)` before adding or yielding every entry.

Add the builder call in `BuildLexiconEntries()` before `BuildDerivedMorphologyEntries(baseEntries)`.

Do not copy old counts, prefixes, tags, or method names. Choose batch-specific values and calculate counts from the final files.

## Phase 9: add regression coverage

In `PlayerAssistant.Tests\Program.cs`:

1. Register a new named batch test.
2. Exclude the new near-kin tag from the legacy near-kin-only count test.
3. Add a batch test that verifies:
   - total batch entries;
   - source-tag count;
   - near-kin-tag count;
   - every entry has no review issue except its expected exact duplicate against the now-built lexicon;
   - every English term translates to the entry's Orcish form.
4. Recalculate the unique English-term assertion. If every final candidate was absent and unique, the new expected total is:

   ```text
   previous unique English total + final source count + final near-kin count
   ```

5. Confirm the runtime count instead of trusting the arithmetic.

## Phase 10: update durable history

Append one bullet under the Orcish work section in `to-do.md` containing:

- total adopted candidates;
- scraped source count;
- near-kin count;
- no-repeat and exact-lexicon filtering confirmation;
- major culling categories;
- ledger update confirmation; and
- backlog synchronization confirmation.

Do not record temporary or pre-cull counts as the completed result.

## Phase 11: build and verify

Build sequentially into the root `Release` folder:

```powershell
dotnet build 'PlayerAssistant.Tests\PlayerAssistant.Tests.csproj' -c Release -o 'Release'
& 'Release\PlayerAssistant.Tests.exe' 'NEW BATCH TEST NAME'
& 'Release\PlayerAssistant.Tests.exe' 'orcish translator exposes unique english term count'
& 'Release\PlayerAssistant.Tests.exe' 'orcish translator'

dotnet build 'player-assistant.csproj' -c Release -o 'Release' --verbosity minimal
dotnet build 'ToOrcish\to-orcish.csproj' -c Release -o 'Release' --verbosity minimal
```

Publishing/building sequentially avoids competing writes to shared DLLs.

Verify several representative source and near-kin terms with the real CLI:

```powershell
& 'Release\to-orcish.exe' 'SOURCE TERM'
& 'Release\to-orcish.exe' 'NEAR-KIN TERM'
```

If an executable is locked, do not fall back to an unverified Debug output. Stop the running process if appropriate, rebuild Release, or use:

```powershell
dotnet exec 'Release\to-orcish.dll' 'TERM'
```

### Final assembly verification

Reload the newly built Release assembly and verify:

- every final source candidate is present;
- every final near-kin candidate is present;
- missing candidate count is zero;
- runtime unique-English count matches the regression assertion;
- `codex-scratch\candidates.txt` is empty when the whole batch is covered.

### Final URL verification

Refetch the current sitemap and compare normalized sets:

```powershell
$ledgerUrls = @(
    [regex]::Matches(
        (Get-Content -Raw -LiteralPath 'dont-scrape-again.md'),
        'https://publish\.obsidian\.md/scarlethorizons/[^\s]+'
    ) | ForEach-Object Value
)
$ledgerKeys = @($ledgerUrls | ForEach-Object { & $normalize $_ } | Sort-Object -Unique)
$sitemapKeys = @($urls | ForEach-Object { & $normalize $_ } | Sort-Object -Unique)
$remainingKeys = @($sitemapKeys | Where-Object { $_ -notin $ledgerKeys })
```

Require:

- ledger URL lines equal unique ledger URLs;
- the new ledger total increased by exactly the batch size;
- remaining count equals unique sitemap count minus unique ledger count.

## Phase 12: refresh Graphify

After changing code:

```powershell
graphify update . --no-cluster
```

Dirty Graphify working files are expected during an update, but only the intended tracked project files should remain in the final Git diff.

## Phase 13: review, commit, and push when authorized

Expected tracked files for a normal completed batch:

```text
OrcishTranslatorUtility.cs
PlayerAssistant.Tests/Program.cs
dont-scrape-again.md
to-do.md
```

This runbook itself appears only when it is created or intentionally updated.

Review:

```powershell
rtk git status --short
rtk git diff --stat
rtk git diff --check
rtk git diff --name-only
```

Stage only intended files. Do not use `git add -A` in a mixed worktree.

```powershell
git add -- 'OrcishTranslatorUtility.cs' 'PlayerAssistant.Tests/Program.cs' 'dont-scrape-again.md' 'to-do.md'
git commit -m 'Add next 50-page Orcish lexicon batch'
git push
```

Before pushing, confirm `gh --version`, `gh auth status`, the current branch, and `origin`. A repeated workflow does not automatically authorize opening a PR; create one only when requested.

## Reset recovery

After a context reset:

1. Read `AGENTS.md`, this file, `to-do.md`, and `dont-scrape-again.md`.
2. Run `rtk git status --short` and inspect the current branch and recent commits.
3. Build the current Release assembly and recalculate lexicon counts.
4. Recalculate normalized sitemap, used, and remaining URL counts.
5. Search `OrcishTranslatorUtility.cs` for the most recent `*PageSampleEntries` builder, tags, prefix, and test registration.
6. Inspect the latest ledger section to identify whether URL selection was already recorded.
7. Inspect `codex-scratch` if it still exists, but never treat scratch files as authoritative without comparing them to tracked code and the ledger.
8. If code contains a batch that the ledger or tests do not, reconcile the partial batch before selecting new URLs.
9. If the ledger contains a batch with no matching lexicon builder, re-scrape those exact URLs and resume that batch instead of consuming new URLs.
10. If no partial work exists, start a new batch with a new slug, heading, method stem, tags, and Orcish prefix.

## Completion checklist

- [ ] Requested number of random URLs selected from the live sitemap.
- [ ] Every URL absent from all earlier normalized ledger entries.
- [ ] Every selected page produced non-empty Markdown.
- [ ] Final successful public URLs recorded in `dont-scrape-again.md`.
- [ ] Raw words extracted after Markdown/web cleanup.
- [ ] Existing lexicon terms removed using the current Release assembly.
- [ ] Source candidates manually culled.
- [ ] Near-kin generated, manually reviewed, and re-filtered.
- [ ] Existing roots and morphology reused where appropriate.
- [ ] Every explicit entry admitted through the review validator.
- [ ] Batch and unique-count regressions updated.
- [ ] `to-do.md` updated with final counts.
- [ ] Release test harness built.
- [ ] Focused batch test passed.
- [ ] Unique-count test passed.
- [ ] All Orcish translator tests passed.
- [ ] Release app and CLI built sequentially.
- [ ] Representative source and near-kin CLI translations verified.
- [ ] Final assembly missing-candidate count is zero.
- [ ] Candidate backlog synchronized.
- [ ] Ledger URLs are unique after normalization.
- [ ] Remaining sitemap count recalculated.
- [ ] Graphify refreshed.
- [ ] Diff contains only intended tracked files.
- [ ] Commit and push completed if authorized.
