using PlayerAssistant;
using Microsoft.Playwright;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Windows.Forms;
using System.Xml.Linq;

using static TestSupport;

internal static class AdventureSearchTests
{
    internal static void RegionalMapDownloadsWhenMissing()
    {
        var filePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "Images", "Maps", "northernreaches.png");

        AssertTrue(GameForumUtility.ShouldDownloadRegionalMap(filePath), "missing regional map should be downloaded");
    }

    internal static void RegionalMapDownloadsWhenOlderThanOneHour()
    {
        using var directory = TemporaryDirectory.Create();
        var filePath = Path.Combine(directory.Path, "Images", "Maps", "northernreaches.png");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        WriteVisiblePng(filePath);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow - TimeSpan.FromMinutes(61));

        AssertTrue(GameForumUtility.ShouldDownloadRegionalMap(filePath), "regional map older than one hour should be downloaded");
    }

    internal static void RegionalMapSkipsWhenNewerThanOneHour()
    {
        using var directory = TemporaryDirectory.Create();
        var filePath = Path.Combine(directory.Path, "Images", "Maps", "northernreaches.png");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        WriteVisiblePng(filePath);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow - TimeSpan.FromMinutes(59));

        AssertFalse(GameForumUtility.ShouldDownloadRegionalMap(filePath), "regional map newer than one hour should not be downloaded");
    }

    internal static void RegionalMapDownloadsWhenNewerButTransparent()
    {
        using var directory = TemporaryDirectory.Create();
        var filePath = Path.Combine(directory.Path, "Images", "Maps", "northernreaches.png");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        WriteTransparentPng(filePath);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow - TimeSpan.FromMinutes(1));

        AssertTrue(GameForumUtility.ShouldDownloadRegionalMap(filePath), "transparent regional map should be downloaded");
    }

    internal static void StartupStatusIncludesDownloadCountAndSize()
    {
        using var directory = TemporaryDirectory.Create();
        var firstPath = Path.Combine(directory.Path, "first.bin");
        var secondPath = Path.Combine(directory.Path, "second.bin");
        File.WriteAllBytes(firstPath, new byte[1024]);
        File.WriteAllBytes(secondPath, new byte[512]);

        FileDownloadCounters.Reset();
        FileDownloadCounters.AddCompletedDownload(firstPath);
        FileDownloadCounters.AddCompletedDownload(secondPath);

        var summary = Form1.GetStartupDownloadSummary();

        AssertContains(summary, "2 files");
        AssertContains(summary, "1.5 KB");
        AssertContains(summary, "0 MB");
    }

    internal static void AdventureOutlineBuildsFromSavedIcHtml()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);

        File.WriteAllText(
            Path.Combine(icDirectory, "ch-10.html"),
            """
            <html><body>
            <h1>Ch 10 - Later Trouble.</h1>
            <span class="messageauthor">Kelpie Lawfuller</span>
            <div class="messagebody" id="msg2">Kelpie keeps watch.<br>Then moves on.</div>
            </body></html>
            """);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-2.html"),
            """
            <html><body>
            <h1>Ch 2 - Supper With Nuanda.</h1>
            <span class="messageauthor you"><a href="/gm">Dungeon Master</a></span>
            <div class="messagebody" id="msg1">Nuanda offers stew &amp; hard biscuits.</div>
            </body></html>
            """);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-2.bak-20260707.html"),
            """
            <html><body>
            <h1>Ch 2 - Old Backup.</h1>
            <span class="messageauthor">Backup</span>
            <div class="messagebody" id="msg1">This should not appear.</div>
            </body></html>
            """);

        var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
            .GetAwaiter()
            .GetResult();

        AssertContains(outline, "# Adventure Outline");
        AssertContains(outline, "## Ch 2 - Supper With Nuanda");
        AssertContains(outline, "- Dungeon Master introduces Nuanda's supper.");
        AssertContains(outline, "## Ch 10 - Later Trouble");
        AssertContains(outline, "- Kelpie keeps watch as the party moves on.");
        AssertFalse(outline.Contains("Old Backup", StringComparison.Ordinal), "backup chapter files should be ignored");
        AssertTrue(
            outline.IndexOf("## Ch 2", StringComparison.Ordinal) < outline.IndexOf("## Ch 10", StringComparison.Ordinal),
            "chapter files should sort by numeric chapter number");
    }

    internal static void AdventureOutlineParsesRpolLinkedAuthorExports()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);

        File.WriteAllText(
            Path.Combine(icDirectory, "ch-1.html"),
            """
            <html><body>
            <div class="threadheader">
                <h1>Ch 1 - Kirkilston.</h1>
            </div>
            <div class="message">
                <span class="messageauthor you"><a href="/gameinfo.php?action=viewdescription&amp;ci=396686">Dungeon Master</a></span>
                <div class="messagebody" id="msg37">Mapper: Slip?<br>Caller: Kelpie?</div>
            </div>
            <div class="message">
                <span class="messageauthor"><a href="/gameinfo.php?action=viewdescription&amp;ci=396648">Kelpie Lawfuller</a></span>
                <div class="messagebody" id="msg38">
                Kelpie was already prepared.<br>
                <span class="blue">I will take the fore</span>
                </div>
            </div>
            </body></html>
            """);

        var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
            .GetAwaiter()
            .GetResult();

        AssertContains(outline, "## Ch 1 - Kirkilston");
        AssertContains(outline, "- Dungeon Master asks a question that narrows the party's next choice.");
        AssertContains(outline, "- Kelpie takes the lead as the party sets out toward Nuanda.");
        AssertFalse(
            outline.Contains("No in-character posts were found", StringComparison.Ordinal),
            "linked author RPOL exports should produce post summaries");
    }

    internal static void AdventureOutlineSummarizesTableRolesConcisely()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-1.html"),
            """
            <html><body>
            <h1>Ch 1 - Kirkilston.</h1>
            <span class="messageauthor you"><a href="/gm">Dungeon Master</a></span>
            <div class="messagebody" id="msg1">
            • Mapper: Slip?<br>
            • Caller: Kelpie?<br>
            • Quartermaster: Urvan<br>
            • Chronicler: Jelb?<br>
            Whoever is acting as the Caller can let me know where the party heads off to.
            </div>
            </body></html>
            """);

        var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
        File.WriteAllText(
            outlinePath,
            """
            # Adventure Outline

            ## Ch 1 - Kirkilston

            - Dungeon Master: • Mapper: Slip? • Caller: Kelpie? • Quartermaster: Urvan • Chronicler: Jelb? Whoever is acting as the Caller can let me know where the party heads off to, any preparation they make, etc. If you are seeking Nuanda, you can get there in under an hour, and I just need a d6 roll from...
            """);

        var updated = AdventureOutlineUtility.UpdateAdventureOutlineAsync(icDirectory, outlinePath)
            .GetAwaiter()
            .GetResult();
        var outline = File.ReadAllText(outlinePath);

        AssertTrue(updated, "role-assignment outline should replace stale overlong bullets");
        AssertContains(outline, "- Dungeon Master asked players to assume the roles of Caller, Quartermaster, Mapper, and Chronicler.");
        AssertFalse(outline.Contains("Whoever is acting as the Caller", StringComparison.Ordinal), "role-assignment outline should not retain the long excerpt");
    }

    internal static void AdventureOutlineSkipsEmptyBulletMarkerPosts()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-1.html"),
            """
            <html><body>
            <h1>Ch 1 - Kirkilston.</h1>
            <span class="messageauthor">Kelpie Lawfuller</span>
            <div class="messagebody" id="msg1">•<br>-<br></div>
            <span class="messageauthor">Dungeon Master</span>
            <div class="messagebody" id="msg2">The party leaves town.</div>
            </body></html>
            """);

        var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
            .GetAwaiter()
            .GetResult();

        AssertContains(outline, "- Dungeon Master moves the party out of town.");
        AssertFalse(outline.Contains("Kelpie", StringComparison.Ordinal), "empty bullet marker posts should not produce outline bullets");
        AssertFalse(outline.Contains("advances the scene", StringComparison.OrdinalIgnoreCase), "outline summaries should explain how the scene advanced");
    }

    internal static void AdventureOutlineRejectsWeakGeneratedSummaries()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-2.html"),
            """
            <html><body>
            <h1>Ch 2 - Supper With Nuanda.</h1>
            <span class="messageauthor">Jelb Garrick</span>
            <div class="messagebody" id="msg1">Jelb agreed to check on the bread, bringing it out to cool when it was ready. I do have something of hers. Her brooch.</div>
            <span class="messageauthor">Nuanda</span>
            <div class="messagebody" id="msg2">The girl, Jelenneth. She was picking berries when bandits abducted her.</div>
            <span class="messageauthor">Urvan Hall</span>
            <div class="messagebody" id="msg3">That is...remarkable. With this information we could look for her.</div>
            </body></html>
            """);

        var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
        File.WriteAllText(
            outlinePath,
            """
            # Adventure Outline

            ## Ch 2 - Supper With Nuanda

            - Jelb advances the scene.
            - Nuanda contributes a new development to the scene.
            - Urvan presses for answers or a decision.
            - Nuanda reassures Kelpie that Morrow and her own magic protect her.
            """);

        AdventureOutlineUtility.UpdateAdventureOutlineAsync(icDirectory, outlinePath)
            .GetAwaiter()
            .GetResult();
        var outline = File.ReadAllText(outlinePath);

        AssertContains(outline, "- Jelb helps with the bread and offers Jelenneth's brooch as a focus.");
        AssertContains(outline, "- Nuanda recounts Jelenneth's abduction by bandits.");
        AssertContains(outline, "- Urvan recognizes that Nuanda's divination gives the party a lead.");

        foreach (var weakSummary in GetWeakAdventureOutlineSummaryPhrases())
        {
            AssertFalse(
                outline.Contains(weakSummary, StringComparison.OrdinalIgnoreCase),
                $"outline should not contain weak generated summary '{weakSummary}'");
        }
    }

    internal static void AdventureOutlineFallbackSummariesPreserveSceneSpecifics()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-5.html"),
            """
            <html><body>
            <h1>Ch 5 - A Betentacled Escape.</h1>
            <span class="messageauthor">Maximilian</span>
            <div class="messagebody" id="msg1">Maximilian studies the recovered scroll and says it names Red Tusk and the Deep Friends.</div>
            <span class="messageauthor">Algorn Druff</span>
            <div class="messagebody" id="msg2">Algorn says the Raven's Pass trail should lead them to Nimba at The Mason's Apron.</div>
            <span class="messageauthor">Billworth Turgen</span>
            <div class="messagebody" id="msg3">Billworth checks the caravan wagons, mules, and remaining cargo before they move again.</div>
            </body></html>
            """);

        var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
            .GetAwaiter()
            .GetResult();

        AssertContains(outline, "- Maximilian connects the current threat to Red Tusk, the Deep Friends, or the Toothbreakers.");
        AssertContains(outline, "- Algorn points the party toward Raven's Pass contacts and support.");
        AssertContains(outline, "- Billworth focuses the scene on the caravan, its route, or its cargo.");

        foreach (var weakSummary in GetWeakAdventureOutlineSummaryPhrases())
        {
            AssertFalse(
                outline.Contains(weakSummary, StringComparison.OrdinalIgnoreCase),
                $"fallback outline should not contain weak generated summary '{weakSummary}'");
        }
    }

    internal static void AdventureOutlineMergesNewSavedIcBullets()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-1.html"),
            """
            <html><body>
            <h1>Ch 1 - Kirkilston.</h1>
            <span class="messageauthor">Dungeon Master</span>
            <div class="messagebody" id="msg1">The party leaves town.</div>
            <span class="messageauthor">Kelpie Lawfuller</span>
            <div class="messagebody" id="msg2">Kelpie takes the lead.</div>
            </body></html>
            """);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-2.html"),
            """
            <html><body>
            <h1>Ch 2 - Supper With Nuanda.</h1>
            <span class="messageauthor">Nuanda</span>
            <div class="messagebody" id="msg3">Nuanda shares what she learned.</div>
            </body></html>
            """);

        var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
        File.WriteAllText(
            outlinePath,
            """
            # Adventure Outline

            ## Ch 1 - Kirkilston

            - Dungeon Master: The party leaves town.
            """);

        var updated = AdventureOutlineUtility.UpdateAdventureOutlineAsync(icDirectory, outlinePath)
            .GetAwaiter()
            .GetResult();
        var outline = File.ReadAllText(outlinePath);

        AssertTrue(updated, "existing adventure outline should be updated with missing material");
        AssertContains(outline, "- Dungeon Master moves the party out of town.");
        AssertFalse(outline.Contains("- Dungeon Master: The party leaves town.", StringComparison.Ordinal), "stale author-prefixed excerpts should be replaced");
        AssertContains(outline, "- Kelpie takes the lead.");
        AssertContains(outline, "## Ch 2 - Supper With Nuanda");
        AssertContains(outline, "- Nuanda briefs the party.");
        AssertTrue(
            outline.IndexOf("- Kelpie takes the lead.", StringComparison.Ordinal)
                < outline.IndexOf("## Ch 2 - Supper With Nuanda", StringComparison.Ordinal),
            "missing chapter 1 bullet should remain before chapter 2");
    }

    internal static void AdventureOutlineFallsBackToObsidianMarkdown()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
        var requestedUrl = string.Empty;

        var updated = AdventureOutlineUtility.UpdateAdventureOutlineAsync(
            icDirectory,
            outlinePath,
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/Adventure+Outline",
            (url, _) =>
            {
                requestedUrl = url;
                return Task.FromResult(
                    """
                    # Adventure Outline

                    ## Ch 1 - Kirkilston

                    - The party seeks Nuanda.
                    """);
            }).GetAwaiter().GetResult();

        AssertTrue(updated, "fallback markdown should write adventure outline when saved IC files are unavailable");
        AssertEqual(
            "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/Adventure+Outline",
            requestedUrl,
            "unexpected fallback markdown URL");
        AssertContains(File.ReadAllText(outlinePath), "- The party seeks Nuanda.");
    }

    internal static void AdventureOutlinePrefersSavedIcHtmlOverFallback()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        Directory.CreateDirectory(icDirectory);
        File.WriteAllText(
            Path.Combine(icDirectory, "ch-1.html"),
            """
            <html><body>
            <h1>Ch 1 - Kirkilston.</h1>
            <span class="messageauthor">Dungeon Master</span>
            <div class="messagebody" id="msg1">Local chapter material.</div>
            </body></html>
            """);
        var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
        var fallbackFetchCount = 0;

        AdventureOutlineUtility.UpdateAdventureOutlineAsync(
            icDirectory,
            outlinePath,
            AdventureOutlineUtility.FallbackMarkdownUrl,
            (_, _) =>
            {
                fallbackFetchCount++;
                return Task.FromResult("# Adventure Outline\n\n- Fallback material.");
            }).GetAwaiter().GetResult();

        var outline = File.ReadAllText(outlinePath);
        AssertEqual(0, fallbackFetchCount, "fallback markdown should not be fetched when saved IC HTML builds an outline");
        AssertContains(outline, "- Dungeon Master adds a concrete detail that changes the party's situation.");
        AssertFalse(outline.Contains("Fallback material", StringComparison.Ordinal), "fallback content should not replace local IC outline");
    }

    internal static void AdventureOutlineIgnoresFailedFallbackMarkdownFetch()
    {
        using var directory = TemporaryDirectory.Create();
        var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
        var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);

        var updated = AdventureOutlineUtility.UpdateAdventureOutlineAsync(
            icDirectory,
            outlinePath,
            AdventureOutlineUtility.FallbackMarkdownUrl,
            (_, _) => Task.FromResult($"{MarkdownUtility.UnresolvedUrlMessage}: fallback"))
            .GetAwaiter()
            .GetResult();

        AssertFalse(updated, "failed fallback markdown fetch should not update adventure outline");
        AssertFalse(File.Exists(outlinePath), "failed fallback markdown fetch should not write an outline file");
    }

    internal static void AdjustedPostTalliesAggregateSavedIcHtml()
    {
        using var directory = TemporaryDirectory.Create();
        var postsDirectory = Path.Combine(directory.Path, "Posts", "IC");
        var asideDirectory = Path.Combine(postsDirectory, "Aside");
        var outOfCharacterDirectory = Path.Combine(directory.Path, "Posts", "OOC");
        Directory.CreateDirectory(postsDirectory);
        Directory.CreateDirectory(asideDirectory);
        Directory.CreateDirectory(outOfCharacterDirectory);

        File.WriteAllText(
            Path.Combine(postsDirectory, "chapter.html"),
            CreateRpolSourceHtml(
                (1, RpolThreadPostUtility.DungeonMasterAuthor, "Mon 1 Jan 2026", "01:00", "The party arrives."),
                (2, RpolThreadPostUtility.NuandaAuthor, "Mon 1 Jan 2026", "01:05", "Nuanda answers."),
                (3, "Jelb Garrick", "Mon 1 Jan 2026", "01:10", "Jelb listens.")));
        File.WriteAllText(
            Path.Combine(asideDirectory, "aside.html"),
            CreateRpolSourceHtml(
                (4, RpolThreadPostUtility.NuandaNemereAuthor, "Mon 1 Jan 2026", "01:15", "Nemere answers."),
                (5, RpolThreadPostUtility.BillworthTurgenAuthor, "Mon 1 Jan 2026", "01:20", "Billworth watches.")));
        File.WriteAllText(
            Path.Combine(outOfCharacterDirectory, "ooc.html"),
            CreateRpolSourceHtml(
                (6, RpolThreadPostUtility.ThurganNewlAuthor, "Mon 1 Jan 2026", "01:25", "Thurgan comments."),
                (7, RpolThreadPostUtility.TheArchonAuthor, "Mon 1 Jan 2026", "01:30", "The Archon comments."),
                (8, "Kelpie Lawfuller", "Mon 1 Jan 2026", "01:35", "Kelpie comments.")));

        var counts = RpolThreadPostUtility.GetAdjustedPostTalliesFromSavedHtmlDirectories(
            postsDirectory,
            asideDirectory,
            outOfCharacterDirectory);

        AssertEqual(8, counts.Count, "expected adjusted author count");
        AssertEqual(6, counts[RpolThreadPostUtility.DungeonMasterAuthor], "unexpected Dungeon Master count");
        AssertEqual(1, counts[RpolThreadPostUtility.BillworthTurgenAuthor], "unexpected Billworth count");
        AssertEqual(1, counts["Jelb Garrick"], "unexpected Jelb count");
        AssertEqual(1, counts["Kelpie Lawfuller"], "unexpected Kelpie count");
        AssertEqual(1, counts[RpolThreadPostUtility.NuandaAuthor], "unexpected Nuanda count");
        AssertEqual(2, counts[RpolThreadPostUtility.NuandaNemereAuthor], "unexpected Nuanda Nemere count");
        AssertEqual(1, counts[RpolThreadPostUtility.TheArchonAuthor], "unexpected The-Archon count");
        AssertEqual(1, counts[RpolThreadPostUtility.ThurganNewlAuthor], "unexpected Thurgan count");
    }

    internal static void KeywordSearchFallsBackToThePrefixedTerm()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {
                    "The": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/the",
                          "count": 1,
                          "last_indexed": "2026-06-28T00:00:00.0000000+00:00"
                        }
                      ]
                    },
                    "The Coal": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/the-coal",
                          "count": 1,
                          "last_indexed": "2026-06-28T00:00:00.0000000+00:00"
                        }
                      ]
                    },
                    "The Hills": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/the-hills",
                          "count": 1,
                          "last_indexed": "2026-06-28T00:00:00.0000000+00:00"
                        }
                      ]
                    }
                  }
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                    txtSearch.Text = "The Coal Hills";
                    InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                    AssertEqual(3, results.Length, "expected three search results from the exact and fallback lookups");
                    AssertContains(string.Join("\n", results), "https://example.test/the");
                    AssertContains(string.Join("\n", results), "https://example.test/the-coal");
                    AssertContains(string.Join("\n", results), "https://example.test/the-hills");
                });
        });
    }

    internal static void KeywordSearchKeepsQuotedPhrasesTogether()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {
                    "one": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/one",
                          "count": 1,
                          "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                        }
                      ]
                    },
                    "two": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/two",
                          "count": 1,
                          "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                        }
                      ]
                    },
                    "one two": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/one-two",
                          "count": 1,
                          "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                        }
                      ]
                    },
                    "three": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://example.test/three",
                          "count": 1,
                          "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                        }
                      ]
                    }
                  }
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                    txtSearch.Text = "\"one two\" three";
                    InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                    AssertEqual(2, results.Length, "expected one quoted-phrase result plus one standalone result");
                    AssertContains(string.Join("\n", results), "https://example.test/one-two");
                    AssertContains(string.Join("\n", results), "https://example.test/three");
                    AssertFalse(results.Contains("https://example.test/one", StringComparer.Ordinal), "quoted phrase should not be split into a standalone 'one' lookup");
                    AssertFalse(results.Contains("https://example.test/two", StringComparer.Ordinal), "quoted phrase should not be split into a standalone 'two' lookup");
                });
        });
    }

    internal static void KeywordSearchAcceptsUrlSourceMetadata()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "urls": {
                    "https://example.test/rpol-entry": {
                      "source": "RPOL"
                    },
                    "https://example.test/obsidian-entry": {
                      "source": "Obsidian wiki"
                    }
                  },
                  "words": {
                    "entry": {
                      "total_occurrences": 2,
                      "matches": [
                        {
                          "url": "https://example.test/rpol-entry",
                          "count": 1,
                          "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                        },
                        {
                          "url": "https://example.test/obsidian-entry",
                          "count": 1,
                          "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                        }
                      ]
                    }
                  }
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                    txtSearch.Text = "entry";
                    InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                    AssertEqual(2, results.Length, "expected both matches to be returned when url source metadata is present");
                    AssertContains(string.Join("\n", results), "https://example.test/rpol-entry");
                    AssertContains(string.Join("\n", results), "https://example.test/obsidian-entry");
                });
        });
    }

    internal static void KeywordSearchFiltersRpolHeroMetadataOnlyHits()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {
                    "Kelpie Lawfuller": {
                      "total_occurrences": 3,
                      "matches": [
                        {
                          "url": "https://rpol.net/display.cgi?gi=80170&ti=11",
                          "count": 1,
                          "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                        },
                        {
                          "url": "https://rpol.net/display.cgi?gi=80170&ti=12",
                          "count": 1,
                          "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                        },
                        {
                          "url": "https://publish.obsidian.md/scarlethorizons/PCs/Kelpie+Lawfuller",
                          "count": 1,
                          "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                        }
                      ]
                    }
                  }
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");
                    var bodyCheckCount = 0;

                    SetPrivateField(
                        form,
                        "_playerCharacterListingMarkdown",
                        """
                        | Name | Character | Notes | Hero |
                        | --- | --- | --- | --- |
                        | Kelpie Lawfuller | [[Kelpie Lawfuller]] | active | ![[kelpie-token.webp]] |
                        """);
                    SetPrivateField(
                        form,
                        "_rpolHeroNameBodyMatchProvider",
                        (Func<string, string, CancellationToken, Task<bool>>)((url, term, _) =>
                        {
                            bodyCheckCount++;
                            AssertEqual("Kelpie Lawfuller", term, "unexpected hero term passed to RPOL body filter");
                            return Task.FromResult(url.Contains("ti=12", StringComparison.Ordinal));
                        }));

                    txtSearch.Text = "\"Kelpie Lawfuller\"";
                    InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                    AssertEqual(2, results.Length, "expected one RPOL body hit and one Obsidian hit");
                    AssertEqual(2, bodyCheckCount, "expected both RPOL matches to be checked against post bodies");
                    AssertContains(string.Join("\n", results), "https://rpol.net/display.cgi?gi=80170&ti=12&msgpage=&show=all");
                    AssertContains(string.Join("\n", results), "https://publish.obsidian.md/scarlethorizons/PCs/Kelpie+Lawfuller");
                    AssertFalse(
                        results.Contains("https://rpol.net/display.cgi?gi=80170&ti=11&msgpage=&show=all", StringComparer.Ordinal),
                        "metadata-only RPOL hit should be excluded for hero-name searches");
                });
        });
    }
}
