---
name: obsidian-publish-word-count
description: Crawl an Obsidian Publish sitemap and produce reproducible per-page and site-wide content word counts, while also counting current local IC and OOC post HTML totals, always classifying game-intro.html and the-cast.html as OOC, recording all three totals in a dated history file, and publishing successful refreshed totals to the protected PWA broker. Use when asked to count words across an entire Obsidian Publish wiki, audit page-level word counts, refresh a site word-count report, or compare the wiki total with local Posts/IC and Posts/OOC content.
---

# Obsidian Publish Word Count

Use the bundled PowerShell script for deterministic counting:

```powershell
& "<skill-directory>\scripts\count-obsidian-publish-words.ps1" `
  -SiteUrl 'https://publish.obsidian.md/example-site' `
  -OutputCsv 'C:\path\site-word-counts.csv' `
  -HistoryFile 'C:\Users\Bryan\Documents\do-not-delete-repositories\player-assistant\obsidian-wiki-word-count.md' `
  -LocalPostsRoot 'C:\Users\Bryan\Documents\do-not-delete-repositories\player-assistant\Release\Posts'
```

## Workflow

1. Run the script against the site's root URL, not an individual page.
2. Let it fetch the live `sitemap.xml`, discover the site's current Publish host and UID, and retrieve every sitemap page as Markdown.
3. Require `FailedPages` to be `0`. Retry the run with a lower `-ThrottleLimit` if the service returns transient failures.
4. Read every current `*.html` file beneath `Release\Posts\IC` and `Release\Posts\OOC`, calculate one visible-content total for each directory, and exclude `.bak-*` duplicates plus non-HTML state files. Always include `game-intro.html` and `the-cast.html` in the OOC total and in OOC comparisons; never exclude them because they are not thread pages. Reject an IC file containing numbered `msgpage` navigation because it is a paginated subset; refresh that thread from its RPOL `show=all` URL before counting.
5. For IC and OOC thread pages, count only visible `messagebody` content. For non-thread OOC pages, fall back to the visible page `content` section, then to the document body for locally exported standalone HTML. Exclude surrounding RPOL chrome, HTML markup, and RPOL edit/update notices.
6. After a zero-failure run, append exactly one new bullet to `C:\Users\Bryan\Documents\do-not-delete-repositories\player-assistant\obsidian-wiki-word-count.md` matching the existing format: `- As of M/d/yyyy, the wiki contained N words; total IC words: I; total OOC words: O`. The bundled script does this automatically through `-HistoryFile`; never overwrite or rewrite existing entries.
7. After a zero-failure run and history append, publish the refreshed snapshot to the protected Player Assistant broker using its configured administrator-authenticated word-count uploader. Include schema version, observation time, wiki page count and total, IC file count and total, OOC file count and total, and the counting-rule version. Never place this snapshot in the public PWA directory and never expose, log, save, or embed the broker administrator credential.
8. Verify that the logged-in PWA read endpoint returns the newly published snapshot with the exact totals and observation time. If the uploader or broker routes are not implemented or configured, report that deployment prerequisite and leave the prior broker snapshot untouched. If upload or verification fails, keep the locally completed count and history entry, report that the PWA was not updated, and never replace the broker's last known good snapshot with partial data.
9. Report the page count, successful page count, wiki total, IC total, OOC total, absolute CSV path, appended history entry, broker publication result, and PWA read-back result.
10. Explain the counting rule briefly:
   - Count Unicode letter/number tokens.
   - Treat internal apostrophes and hyphens as part of one word.
   - Preserve visible link labels.
   - Exclude Publish chrome by counting source Markdown rather than rendered shell HTML.
   - Exclude YAML frontmatter, fenced code blocks, Markdown/HTML syntax, comments, embeds, images, URLs, imported forum headers, timestamps, and edit notices.
11. Do not append a history entry, publish a broker snapshot, or claim complete totals if any sitemap page failed or either local post directory cannot be counted completely.

The CSV is sorted in sitemap order and contains `PagePath`, `Url`, `WordCount`, and `Status`.
