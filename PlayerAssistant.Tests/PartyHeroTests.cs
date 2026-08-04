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

internal static class PartyHeroTests
{
    internal static void SearchEnterTriggersClickWhenEnabled()
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
                    "entry": {
                      "total_occurrences": 1,
                      "matches": [
                        {
                          "url": "https://publish.obsidian.md/scarlethorizons/entry",
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
                    using var buttonHost = new Form();

                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var btnSearch = GetControl<Button>(form, "btnSearch");
                    buttonHost.Controls.Add(btnSearch);
                    buttonHost.Show();
                    Application.DoEvents();

                    var clickCount = 0;
                    btnSearch.Click += (_, _) => clickCount++;

                    txtSearch.Text = "entry";
                    AssertTrue(btnSearch.Enabled, "expected search button to be enabled for a valid search term");

                    InvokePrivateMethod(
                        form,
                        "TxtSearch_EnterPressed",
                        txtSearch,
                        EventArgs.Empty);

                    AssertEqual(1, clickCount, "expected Enter to trigger the existing search click path");
                    var completionTimeout = Stopwatch.StartNew();
                    while (!btnSearch.Enabled && completionTimeout.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }

                    AssertTrue(btnSearch.Enabled, "expected the Enter-triggered search to complete");
                });
        });
    }

    internal static void KeywordSearchUppercasesEncryptedIndexResultsWithoutChangingLaunchUrl()
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
                    "nimba": {
                      "total_occurrences": 2,
                      "matches": [
                        {
                          "url": "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                          "count": 1,
                          "last_indexed": "2026-07-07T00:00:00.0000000+00:00"
                        },
                        {
                          "url": "https://publish.obsidian.md/scarlethorizons/NPCs/Nuanda+Armstrong",
                          "count": 1,
                          "last_indexed": "2026-07-07T00:00:00.0000000+00:00"
                        }
                      ]
                    }
                  }
                }
                """,
                () => WithTemporaryEncryptedTextIndex(
                    """
                    [
                      {
                        "url": "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                        "encrypted_sections": 2,
                        "frontmatter_tags": ["npc", "spy"]
                      }
                    ]
                    """,
                    () =>
                    {
                        using var form = new Form1(suppressHeroImagesForThisRun: true);
                        var txtSearch = GetControl<TextBox>(form, "txtSearch");
                        var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                        txtSearch.Text = "nimba";
                        InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                        var results = lstSearchResults.Items.Cast<object>().ToArray();
                        AssertEqual(2, results.Length, "expected both keyword-index matches to be returned");
                        AssertEqual(
                            "HTTPS://PUBLISH.OBSIDIAN.MD/SCARLETHORIZONS/NPCS/NIMBA+ARMSTRONG",
                            results[0]?.ToString() ?? string.Empty,
                            "encrypted index result should display in uppercase");
                        AssertEqual(
                            "https://publish.obsidian.md/scarlethorizons/NPCs/Nuanda+Armstrong",
                            results[1]?.ToString() ?? string.Empty,
                            "non-encrypted index result should display normally");

                        var launchUrl = InvokePrivateMethod(form, "GetSearchResultLaunchUrl", results[0]);
                        AssertEqual(
                            "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                            launchUrl?.ToString() ?? string.Empty,
                            "uppercase display item should retain the original launch URL");
                    }));
        });
    }

    internal static void KeywordSearchUppercasesOnlineObsidianFallbackResults()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {}
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                    SetPrivateField(
                        form,
                        "_showLocalIndexMissPrompt",
                        (Func<string[], DialogResult>)(_ => DialogResult.Yes));
                    SetPrivateField(
                        form,
                        "_showOnlineSearchCompletedMessage",
                        (Action<string[], int>)((_, _) => { }));
                    SetPrivateField(
                        form,
                        "_onlineSearchProvider",
                        (Func<string[], CancellationToken, Task<string[]>>)((_, _) => Task.FromResult(new[]
                        {
                            "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                            "https://rpol.net/display.cgi?gi=80170&ti=12&msgpage=&show=all"
                        })));

                    txtSearch.Text = "not indexed locally";
                    InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().ToArray();
                    AssertEqual(2, results.Length, "expected online fallback to populate both provider results");
                    AssertEqual(
                        "HTTPS://PUBLISH.OBSIDIAN.MD/SCARLETHORIZONS/NPCS/NIMBA+ARMSTRONG",
                        results[0]?.ToString() ?? string.Empty,
                        "online Obsidian fallback result should display in uppercase");
                    AssertEqual(
                        "https://rpol.net/display.cgi?gi=80170&ti=12&msgpage=&show=all",
                        results[1]?.ToString() ?? string.Empty,
                        "non-Obsidian online fallback result should display normally");

                    var launchUrl = InvokePrivateMethod(form, "GetSearchResultLaunchUrl", results[0]);
                    AssertEqual(
                        "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                        launchUrl?.ToString() ?? string.Empty,
                        "uppercase online Obsidian item should retain the original launch URL");
                });
        });
    }

    internal static void KeywordSearchBackfillsOnlineHitsIntoKeywordIndex()
    {
        WithTemporaryKeywordIndex(
            """
            {
              "index_metadata": {
                "total_words_indexed": 0
              },
              "words": {}
            }
            """,
            () =>
            {
                Form1.BackfillKeywordIndexWithOnlineResultsAsync(
                    ["Nimba Armstrong"],
                    ["https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong"],
                    CancellationToken.None).GetAwaiter().GetResult();

                using var document = JsonDocument.Parse(File.ReadAllText(GetPlayerAssistantIndexPath()));
                var words = document.RootElement.GetProperty("words");
                AssertTrue(words.TryGetProperty("Nimba Armstrong", out var nimbaEntry), "online hit should add the missing search term to keyword-index.json");
                AssertEqual(1, nimbaEntry.GetProperty("total_occurrences").GetInt32(), "backfilled keyword should record one occurrence");

                var match = nimbaEntry.GetProperty("matches").EnumerateArray().Single();
                AssertEqual(
                    "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                    match.GetProperty("url").GetString() ?? string.Empty,
                    "backfilled keyword should store the online hit URL");
                AssertEqual(1, match.GetProperty("count").GetInt32(), "backfilled match should use a count of one");
                AssertFalse(
                    string.IsNullOrWhiteSpace(match.GetProperty("last_indexed").GetString()),
                    "backfilled match should record a last-indexed timestamp");
            });
    }

    internal static void KeywordSearchOffersOnlineFallbackOnLocalMiss()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {}
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");
                    var promptCallCount = 0;
                    var onlineSearchCallCount = 0;
                    var onlineSearchCompletedCallCount = 0;

                    SetPrivateField(
                        form,
                        "_showLocalIndexMissPrompt",
                        (Func<string[], DialogResult>)(terms =>
                        {
                            promptCallCount++;
                            AssertEqual("not indexed locally", terms[0], "unexpected prompt term");
                            return DialogResult.Yes;
                        }));
                    SetPrivateField(
                        form,
                        "_onlineSearchProvider",
                        (Func<string[], CancellationToken, Task<string[]>>)((terms, _) =>
                        {
                            onlineSearchCallCount++;
                            AssertEqual("not indexed locally", terms[0], "unexpected online search term");
                            return Task.FromResult(new[]
                            {
                                "https://example.test/online-result"
                            });
                        }));
                    SetPrivateField(
                        form,
                        "_showOnlineSearchCompletedMessage",
                        (Action<string[], int>)((terms, resultCount) =>
                        {
                            onlineSearchCompletedCallCount++;
                            AssertEqual("not indexed locally", terms[0], "unexpected completed-message term");
                            AssertEqual(1, resultCount, "unexpected completed-message result count");
                        }));

                    txtSearch.Text = "\"not indexed locally\"";
                    InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                    AssertEqual(1, promptCallCount, "expected the local-index miss prompt to be shown once");
                    AssertEqual(1, onlineSearchCallCount, "expected online search to run once");
                    AssertEqual(1, onlineSearchCompletedCallCount, "expected the online-search completion message to be shown once");
                    AssertEqual(1, results.Length, "expected online fallback to populate one result");
                    AssertContains(string.Join("\n", results), "https://example.test/online-result");
                });
        });
    }

    internal static void KeywordSearchCancelsPreviousOnlineFallback()
    {
        RunOnStaThread(() =>
        {
            WithTemporaryKeywordIndex(
                """
                {
                  "index_metadata": {
                    "total_words_indexed": 0
                  },
                  "words": {}
                }
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var btnSearch = GetControl<Button>(form, "btnSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");
                    var firstSearchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var secondSearchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    CancellationToken firstSearchToken = default;
                    var onlineSearchCallCount = 0;

                    SetPrivateField(
                        form,
                        "_showLocalIndexMissPrompt",
                        (Func<string[], DialogResult>)(_ => DialogResult.Yes));
                    SetPrivateField(
                        form,
                        "_showOnlineSearchCompletedMessage",
                        (Action<string[], int>)((_, _) => { }));
                    SetPrivateField(
                        form,
                        "_onlineSearchProvider",
                        (Func<string[], CancellationToken, Task<string[]>>)(async (terms, cancellationToken) =>
                        {
                            onlineSearchCallCount++;
                            if (onlineSearchCallCount == 1)
                            {
                                firstSearchToken = cancellationToken;
                                firstSearchStarted.SetResult();
                                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                            }

                            secondSearchStarted.SetResult();
                            cancellationToken.ThrowIfCancellationRequested();
                            return ["https://example.test/current-search"];
                        }));

                    txtSearch.Text = "\"first missing\"";
                    _ = InvokePrivateAsync(form, "PerformSearchAsync");
                    AssertTrue(firstSearchStarted.Task.Wait(TimeSpan.FromSeconds(2)), "first search did not reach online fallback");

                    txtSearch.Text = "\"second missing\"";
                    var secondSearch = InvokePrivateAsync(form, "PerformSearchAsync");
                    secondSearch.GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                    AssertTrue(firstSearchToken.IsCancellationRequested, "starting a second search should cancel the first search token");
                    AssertTrue(secondSearchStarted.Task.IsCompleted, "second search did not reach online fallback");
                    AssertEqual(2, onlineSearchCallCount, "expected both online search attempts to start");
                    AssertTrue(btnSearch.Enabled, "search button should be re-enabled after current search completes");
                    AssertEqual(1, results.Length, "only current search results should remain");
                    AssertContains(string.Join("\n", results), "https://example.test/current-search");
                });
        });
    }

    internal static void KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheart()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var txtSearch = GetControl<TextBox>(form, "txtSearch");
            var rdoRPOL = GetControl<RadioButton>(form, "rdoRPOL");
            var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

            rdoRPOL.Checked = true;
            txtSearch.Text = "whiteheart";
            InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

            var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
            AssertEqual(0, results.Length, "expected RPOL-only search to exclude the Obsidian-only whiteheart entry");
        });
    }

    internal static void KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheartStiffwhiskers()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var txtSearch = GetControl<TextBox>(form, "txtSearch");
            var rdoRPOL = GetControl<RadioButton>(form, "rdoRPOL");
            var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

            rdoRPOL.Checked = true;
            txtSearch.Text = "whiteheart stiffwhiskers";
            InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

            var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
            AssertEqual(0, results.Length, "expected RPOL-only search to exclude the Obsidian-only whiteheart stiffwhiskers entry");
        });
    }

    internal static void KeywordSearchExpandsHeroFirstAndFullNames()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            SetPrivateField(
                form,
                "_playerCharacterListingMarkdown",
                """
                | Name | Character | Notes | Hero |
                | ---- | --------- | ----- | ---- |
                | [[Kelpie Lawfuller]] | Fighter | active | ![[kelpie-token.webp]] |
                | [[Jelb Garrick]] | Illusionist | active | ![[jelb-token.webp]] |
                """);

            var kelpieAliases = ((string[]?)InvokePrivateMethod(form, "GetHeroSearchTermAliases", "Kelpie")
                ?? []).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            var jelbAliases = ((string[]?)InvokePrivateMethod(form, "GetHeroSearchTermAliases", "Jelb Garrick")
                ?? []).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

            AssertEqual(2, kelpieAliases.Length, "Kelpie first-name search should produce first and full-name aliases");
            AssertEqual("Kelpie", kelpieAliases[0], "unexpected Kelpie first-name alias");
            AssertEqual("Kelpie Lawfuller", kelpieAliases[1], "unexpected Kelpie full-name alias");
            AssertEqual(2, jelbAliases.Length, "Jelb full-name search should produce first and full-name aliases");
            AssertEqual("Jelb", jelbAliases[0], "unexpected Jelb first-name alias");
            AssertEqual("Jelb Garrick", jelbAliases[1], "unexpected Jelb full-name alias");
        });
    }

    internal static void PartyHeroSheetParserReadsSummaryAndHidesXpLines()
    {
        var hero = PartyHeroUtility.ParseHeroSheet(
            """
            ---
            dg-publish: true
            ---
            ![[jelb-token.webp]]

            Class: Illusionist
            HP: 4
            Level: 1
            XP: 0
            Intelligence 16 Language Native+2 Literacy Literate XP Bonus: 10%
            Attained level 03 Illusionist after XP was awarded.

            Name: Jelb Garrick
            """,
            "Jelb");

        AssertEqual("Jelb Garrick", hero.Name, "unexpected parsed hero name");
        AssertEqual("Illusionist", hero.CharacterClass, "unexpected parsed class");
        AssertEqual("3", hero.Level, "unexpected parsed level");
        AssertEqual("4", hero.HitPoints, "unexpected parsed hit points");
        AssertFalse(hero.CharacterSheetText.Contains("XP: 0", StringComparison.Ordinal), "XP total lines should be hidden from party sheet text");
        AssertContains(hero.CharacterSheetText, "XP Bonus: 10%");
    }

    internal static void MyHeroBriefingBuildsSelectedHeroSummaryBoundary()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Kelpie Lawfuller", "kelpie-token.webp", "3", "Fighter", "12", "Kelpie sheet"),
            new("Jelb Garrick", "jelb-token.webp", "3", "Illusionist", "8", "Jelb sheet")
        };
        var request = new MyHeroBriefingRequest(
            heroes,
            SelectedHeroName: "Jelb Garrick",
            AuthenticatedHeroName: "Jelb",
            XpTotals: [new PcXpTotal("Jelb", 8575)],
            ThreadPosts:
            [
                new MyHeroBriefingThreadPosts(
                    "Chapter 1",
                    "https://rpol.net/display.cgi?gi=80170&ti=7",
                    [])
            ],
            EncryptedTextIndex:
            [
                new EncryptedTextIndexEntry(
                    "https://publish.obsidian.md/scarlethorizons/Secrets",
                    1,
                    ["illusionist"])
            ],
            QuickLinks:
            [
                new MyHeroBriefingQuickLink("Party", "app://show/party")
            ]);

        var briefing = MyHeroBriefingUtility.Build(request);

        AssertFalse(briefing.NeedsHeroSelection, "selected hero should not require a picker");
        AssertTrue(briefing.Hero is not null, "selected hero should build a hero summary");
        AssertEqual("Jelb Garrick", briefing.Hero!.Name, "unexpected briefing hero");
        AssertEqual("Illusionist", briefing.Hero.CharacterClass, "unexpected briefing class");
        AssertEqual("3", briefing.Hero.Level, "unexpected briefing level");
        AssertEqual("8", briefing.Hero.HitPoints, "unexpected briefing hit points");
        AssertEqual(8575, briefing.Hero.XpTotal ?? -1, "XP should match first-name alias");
        AssertEqual("jelb-token.webp", briefing.Hero.TokenImagePath ?? string.Empty, "unexpected token path");
        AssertEqual("Jelb Garrick", briefing.Hero.AccessContext.CharacterName ?? string.Empty, "unexpected access context character");
        AssertTrue(briefing.HeroCard is not null, "selected hero should build a current hero card");
        AssertEqual("Jelb Garrick", briefing.HeroCard!.Name, "unexpected card hero");
        AssertEqual("Illusionist", briefing.HeroCard.CharacterClass, "unexpected card class");
        AssertEqual("3", briefing.HeroCard.Level, "unexpected card level");
        AssertEqual("8", briefing.HeroCard.HitPoints, "unexpected card hit points");
        AssertEqual("XP Total: 8,575", briefing.HeroCard.XpTotalLabel, "unexpected card XP label");
        AssertEqual("jelb-token.webp", briefing.HeroCard.TokenImagePath ?? string.Empty, "unexpected card token path");
        AssertEqual("Jelb sheet", briefing.HeroCard.CharacterSheetText, "unexpected card sheet text");
        AssertEqual(2, briefing.HeroChoices.Count, "unexpected hero choice count");
        AssertEqual(MyHeroBriefingHeroIdentitySource.AuthenticatedHero, briefing.HeroIdentitySource, "authenticated identity should win before selected hero");
        AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Full Sheet" && link.Target == "app://show/party"), "briefing should include a full-sheet quick link");
        AssertTrue(briefing.QuickLinks.Any(link => link.Label == "XP" && link.Target == "app://show/xp"), "briefing should include an XP quick link");
        AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Party" && link.Target == "app://show/party"), "briefing should include a Party quick link");
        AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Adventure Outline" && link.Target == "app://show/adventure-outline"), "briefing should include an Adventure Outline quick link");
        AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Chapter 1" && link.Target == "https://rpol.net/display.cgi?gi=80170&ti=7"), "briefing should include RPOL thread quick links");
        AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Party" && link.Target == "app://show/party"), "provided quick links should be retained");
        AssertEqual(briefing.QuickLinks.Count, briefing.HeroCard.QuickLinks.Count, "card quick links should mirror briefing quick links");
        AssertEqual(0, briefing.RecentActivity.Count, "activity should be left for the later backlog step");
        AssertEqual(0, briefing.LikelyResponseItems.Count, "response items should be left for the later backlog step");
        AssertEqual(1, briefing.UnlockedNotes.Count, "encrypted index input should surface unlocked notes");
        AssertEqual("Secrets", briefing.UnlockedNotes[0].Title, "unexpected unlocked note title");
    }

    internal static void MyHeroBriefingPrefersAuthenticatedHeroIdentity()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet"),
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
        };

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            SelectedHeroName: "Kelpie Lawfuller",
            AuthenticatedHeroName: "Jelb"));

        AssertTrue(briefing.Hero is not null, "authenticated hero should resolve a briefing hero");
        AssertEqual("Jelb Garrick", briefing.Hero!.Name, "authenticated first-name identity should select Jelb");
        AssertEqual(MyHeroBriefingHeroIdentitySource.AuthenticatedHero, briefing.HeroIdentitySource, "unexpected identity source");
        AssertFalse(briefing.NeedsHeroSelection, "resolved authenticated hero should not need a picker");
    }

    internal static void MyHeroBriefingRequiresExplicitDungeonMasterHeroSelection()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet"),
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
        };
        var unresolved = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            AuthenticatedHeroName: "Dungeon Master",
            IsDungeonMaster: true));
        var selected = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            SelectedHeroName: "Kelpie",
            AuthenticatedHeroName: "Dungeon Master",
            IsDungeonMaster: true));

        AssertTrue(unresolved.Hero is null, "DM briefing should not infer a hero from Dungeon Master identity");
        AssertTrue(unresolved.NeedsHeroSelection, "DM briefing should request explicit hero selection");
        AssertEqual(MyHeroBriefingHeroIdentitySource.None, unresolved.HeroIdentitySource, "unexpected unresolved DM identity source");
        AssertEqual("Choose a hero to build My Hero Briefing for Dungeon Master view.", unresolved.StatusMessage, "unexpected DM picker status");
        AssertTrue(selected.Hero is not null, "explicit DM selection should resolve a hero");
        AssertEqual("Kelpie Lawfuller", selected.Hero!.Name, "unexpected selected DM hero");
        AssertEqual(MyHeroBriefingHeroIdentitySource.SelectedHero, selected.HeroIdentitySource, "unexpected selected DM identity source");
    }

    internal static void MyHeroBriefingLeavesAmbiguousFirstNameUnresolved()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Max North", null, "1", "Fighter", "5", "Max North sheet"),
            new("Max Stone", null, "2", "Thief", "7", "Max Stone sheet")
        };

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            AuthenticatedHeroName: "Max"));

        AssertTrue(briefing.Hero is null, "ambiguous first-name identity should remain unresolved");
        AssertTrue(briefing.NeedsHeroSelection, "ambiguous identity should request explicit selection");
        AssertEqual(MyHeroBriefingHeroIdentitySource.None, briefing.HeroIdentitySource, "unexpected ambiguous identity source");
    }

    internal static void MyHeroBriefingHidesXpForUnauthenticatedSelectedHeroCard()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet"),
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
        };

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            SelectedHeroName: "Kelpie Lawfuller",
            XpTotals: [new PcXpTotal("Kelpie Lawfuller", 7062)]));

        AssertTrue(briefing.HeroCard is not null, "selected hero should build a current hero card");
        AssertTrue(briefing.HeroCard!.XpTotal is null, "unauthenticated selected hero should not receive raw XP totals");
        AssertEqual("XP Total: hidden", briefing.HeroCard.XpTotalLabel, "unexpected hidden XP label");
        AssertEqual(MyHeroBriefingHeroIdentitySource.SelectedHero, briefing.HeroIdentitySource, "unexpected selected identity source");
    }

    internal static void MyHeroBriefingBuildsRecentHeroActivity()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
        };
        var matchingPosts = Enumerable.Range(1, 12)
            .Select(index => new RpolThreadPost(
                index,
                index % 2 == 0 ? "Dungeon Master" : "Kelpie",
                string.Empty,
                "Mon 1 Jan 2026",
                $"{index:00}:00",
                $"{index:000}.html",
                "<div></div>",
                "<p></p>",
                index == 12
                    ? "Jelb Garrick considers the long corridor. " + new string('x', 220)
                    : $"Jelb studies clue {index}."))
            .Concat(
            [
                new RpolThreadPost(
                    13,
                    "Dungeon Master",
                    string.Empty,
                    "Mon 1 Jan 2026",
                    "13:00",
                    "013.html",
                    "<div></div>",
                    "<p></p>",
                    "A jelbian carving is unrelated."),
                new RpolThreadPost(
                    14,
                    "Dungeon Master",
                    string.Empty,
                    "Mon 1 Jan 2026",
                    "14:00",
                    "014.html",
                    "<div></div>",
                    "<p></p>",
                    "Kelpie studies the same clue."),
                new RpolThreadPost(
                    15,
                    "Jelb",
                    string.Empty,
                    "Mon 1 Jan 2026",
                    "15:00",
                    "015.html",
                    "<div></div>",
                    "<p></p>",
                    "I check the stonework for hidden catches.")
            ])
            .ToArray();
        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            AuthenticatedHeroName: "Jelb",
            ThreadPosts:
            [
                new MyHeroBriefingThreadPosts(
                    "Chapter 1",
                    "https://rpol.net/display.cgi?gi=80170&ti=7",
                    matchingPosts)
            ]));

        AssertEqual(10, briefing.RecentActivity.Count, "recent activity should be capped at ten matching posts");
        AssertEqual(15, briefing.RecentActivity[0].MessageNumber, "latest hero-authored post should appear first");
        AssertEqual(4, briefing.RecentActivity[^1].MessageNumber, "oldest retained matching post should be message 4");
        AssertTrue(
            briefing.RecentActivity.All(item => item.ThreadTitle == "Chapter 1"
                && item.ThreadUrl == "https://rpol.net/display.cgi?gi=80170&ti=7"),
            "activity items should retain thread context");
        AssertTrue(
            briefing.RecentActivity.All(item => item.MessageNumber != 13 && item.MessageNumber != 14),
            "activity should exclude substring matches and unrelated hero posts");
        AssertTrue(briefing.RecentActivity.Any(item => item.MessageNumber == 15), "hero-authored posts should count as recent activity");
        AssertTrue(briefing.RecentActivity[1].Excerpt.EndsWith("...", StringComparison.Ordinal), "long excerpts should be shortened");
        AssertTrue(briefing.RecentActivity[1].Excerpt.Length <= 183, "shortened excerpts should stay bounded");
    }

    internal static void MyHeroBriefingBuildsLikelyOpenResponseItems()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
        };
        var chapterPosts = new RpolThreadPost[]
        {
            CreateRpolThreadPost(1, "Dungeon Master", "Before Jelb posts."),
            CreateRpolThreadPost(2, "Jelb", "Jelb watches the door."),
            CreateRpolThreadPost(3, "Kelpie", "Should we open it?"),
            CreateRpolThreadPost(4, "Dungeon Master", "Jelb hears a faint click."),
            CreateRpolThreadPost(5, "Nuanda", "The corridor stays quiet."),
            CreateRpolThreadPost(6, "Jelb", "Jelb studies the lock."),
            CreateRpolThreadPost(7, "Dungeon Master", "The lock gives way."),
            CreateRpolThreadPost(8, "Kelpie", "Jelb, do you want the lantern?")
        };
        var noHeroPostThread = new RpolThreadPost[]
        {
            CreateRpolThreadPost(1, "Kelpie", "Jelb might know this."),
            CreateRpolThreadPost(2, "Dungeon Master", "What happens next?")
        };

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            AuthenticatedHeroName: "Jelb",
            ThreadPosts:
            [
                new MyHeroBriefingThreadPosts(
                    "Chapter 1",
                    "https://rpol.net/display.cgi?gi=80170&ti=7",
                    chapterPosts),
                new MyHeroBriefingThreadPosts(
                    "Chapter 2",
                    "https://rpol.net/display.cgi?gi=80170&ti=8",
                    noHeroPostThread)
            ]));

        AssertEqual(2, briefing.LikelyResponseItems.Count, "only posts after the hero's latest post should be response candidates");
        AssertEqual(8, briefing.LikelyResponseItems[0].MessageNumber, "direct mention should rank first");
        AssertEqual("Direct mention after your last post", briefing.LikelyResponseItems[0].Reason, "unexpected direct-mention reason");
        AssertEqual(7, briefing.LikelyResponseItems[1].MessageNumber, "neutral follow-up should remain after direct mentions and questions");
        AssertEqual("Recent post after your last post", briefing.LikelyResponseItems[1].Reason, "weak evidence should stay neutral");
        AssertTrue(
            briefing.LikelyResponseItems.All(item => item.ThreadTitle == "Chapter 1"
                && item.ThreadUrl == "https://rpol.net/display.cgi?gi=80170&ti=7"),
            "response items should be grouped by retaining thread context and ignore threads without a hero post");
    }

    internal static void MyHeroBriefingSurfacesRelevantUnlockedNotes()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
        };
        var encryptedIndex = new EncryptedTextIndexEntry[]
        {
            new(
                "https://publish.obsidian.md/scarlethorizons/Secrets/Illusionist+Clue",
                2,
                ["Class Illusionist"]),
            new(
                "https://publish.obsidian.md/scarlethorizons/Secrets/Jelb+Only",
                1,
                ["Hero Jelb"]),
            new(
                "https://publish.obsidian.md/scarlethorizons/Secrets/High+Level",
                1,
                ["Level 4"]),
            new(
                "https://publish.obsidian.md/scarlethorizons/Secrets/Fighter+Only",
                1,
                ["Class Fighter"]),
            new(
                "https://publish.obsidian.md/scarlethorizons/Secrets/Public",
                0,
                ["Class Illusionist"])
        };

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
            heroes,
            AuthenticatedHeroName: "Jelb",
            EncryptedTextIndex: encryptedIndex));

        AssertEqual(2, briefing.UnlockedNotes.Count, "only notes unlocked by hero tags should be surfaced");
        AssertTrue(
            briefing.UnlockedNotes.Any(note =>
                note.Title == "Illusionist Clue"
                && note.Url == "https://publish.obsidian.md/scarlethorizons/Secrets/Illusionist+Clue"
                && note.Excerpt == "2 unlocked encrypted sections may be relevant."),
            "class-matched encrypted note should be included");
        AssertTrue(
            briefing.UnlockedNotes.Any(note =>
                note.Title == "Jelb Only"
                && note.Excerpt == "1 unlocked encrypted section may be relevant."),
            "hero-name matched encrypted note should be included");
        AssertFalse(
            briefing.UnlockedNotes.Any(note => note.Title is "High Level" or "Fighter Only" or "Public"),
            "locked notes and entries without encrypted sections should remain hidden");
    }

    internal static void MyHeroBriefingRequestsHeroSelectionWhenNoHeroSelected()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet"),
            new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
        };

        var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(heroes));

        AssertTrue(briefing.Hero is null, "briefing should not choose a hero before identity resolution exists");
        AssertTrue(briefing.NeedsHeroSelection, "briefing should request a hero selection");
        AssertEqual(2, briefing.HeroChoices.Count, "unexpected hero choice count");
        AssertEqual(MyHeroBriefingHeroIdentitySource.None, briefing.HeroIdentitySource, "unexpected unresolved identity source");
        AssertEqual("Choose a hero to build My Hero Briefing.", briefing.StatusMessage, "unexpected picker status");
    }

    internal static void MyHeroBriefingDisplayTextIncludesFocusedSections()
    {
        var briefing = CreateMyHeroBriefingDisplayFixture();
        var text = (string)(InvokeStaticMethod(typeof(Form1), "FormatMyHeroBriefingForDisplay", briefing)
            ?? throw new InvalidOperationException("briefing display text was null."));

        AssertContains(text, "My Hero Briefing");
        AssertContains(text, "Current Hero");
        AssertContains(text, "Jelb Garrick");
        AssertContains(text, "Class: Illusionist");
        AssertContains(text, "Level: 3");
        AssertContains(text, "HP: 8");
        AssertContains(text, "XP: 1,234 XP");
        AssertContains(text, "Likely Open Response Items");
        AssertContains(text, "*First, the app finds the hero's latest authored post in each thread.*");
        AssertContains(text, "*Then it looks at later posts in that same thread by other authors.*");
        AssertContains(text, "*Those later posts are ranked as:*");
        AssertContains(text, "*- Direct mention after your last post when the post mentions the hero by name or first name.*");
        AssertContains(text, "*- Question-like post after your last post when the post contains a ?.*");
        AssertContains(text, "*- Recent post after your last post when it is simply a later post in that thread.*");
        AssertContains(text, "Direct mention after your last post");
        AssertContains(text, "Recent Hero Activity");
        AssertContains(text, "Relevant Unlocked Notes");
        AssertContains(text, "Jelb Only");
        AssertContains(text, "Quick Links");
    }

    internal static void MyHeroBriefingStylesLikelyResponseKey()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            InvokePrivateMethod(form, "ShowMyHeroBriefing", CreateMyHeroBriefingDisplayFixture());
            var textBox = (RichTextBox)(GetPrivateField(form, "_myHeroBriefingTextBox")
                ?? throw new InvalidOperationException("my hero briefing text box was null."));
            const string keyLine = "*First, the app finds the hero's latest authored post in each thread.*";
            var start = textBox.Text.IndexOf(keyLine, StringComparison.Ordinal);

            AssertTrue(start >= 0, "expected likely response key line to be present");
            textBox.Select(start, 1);
            AssertEqual(Color.FromArgb(246, 241, 222), textBox.SelectionBackColor, "unexpected likely response key background color");
        });
    }

    internal static void MyHeroBriefingLoadsCachedThreadPostsFromRuntimeArtifacts()
    {
        using var directory = TemporaryDirectory.Create();
        var threadDirectory = Path.Combine(directory.Path, "ch-2");
        Directory.CreateDirectory(threadDirectory);
        File.WriteAllText(
            Path.Combine(threadDirectory, "_source-show-all.html"),
            CreateRpolSourceHtml(
                (1, "Jelb", "Mon 1 Jan 2026", "01:00", "Jelb checks the door."),
                (2, "Dungeon Master", "Mon 1 Jan 2026", "01:05", "The lock clicks.")));
        var manifest = new RpolThreadSplitResult(
            "Chapter 2 - Supper With Nuanda",
            "https://rpol.net/display.cgi?gi=80170&ti=8&show=all",
            threadDirectory,
            2,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Dungeon Master"] = 1,
                ["Jelb"] = 1
            },
            []);
        File.WriteAllText(Path.Combine(threadDirectory, "manifest.json"), JsonSerializer.Serialize(manifest));

        Form1.MyHeroBriefingPostsDirectoryOverride = directory.Path;
        try
        {
            var threadPosts = (IReadOnlyList<MyHeroBriefingThreadPosts>)(InvokeStaticMethod(typeof(Form1), "LoadMyHeroBriefingThreadPosts")
                ?? throw new InvalidOperationException("thread posts were null."));

            AssertEqual(1, threadPosts.Count, "expected one cached thread to load");
            AssertEqual("Chapter 2 - Supper With Nuanda", threadPosts[0].ThreadTitle, "unexpected thread title");
            AssertEqual("https://rpol.net/display.cgi?gi=80170&ti=8&show=all", threadPosts[0].ThreadUrl, "unexpected thread URL");
            AssertEqual(2, threadPosts[0].Posts.Count, "unexpected cached post count");
            AssertEqual("Jelb", threadPosts[0].Posts[0].Author, "unexpected first cached post author");
            AssertEqual("The lock clicks.", threadPosts[0].Posts[1].BodyText, "unexpected second cached post body");
        }
        finally
        {
            Form1.MyHeroBriefingPostsDirectoryOverride = null;
        }
    }

    internal static void MyHeroBriefingLoadsFlatCachedThreadFilesFromRuntimeArtifacts()
    {
        using var directory = TemporaryDirectory.Create();
        var asideDirectory = Path.Combine(directory.Path, "Aside");
        Directory.CreateDirectory(asideDirectory);
        File.WriteAllText(
            Path.Combine(directory.Path, "ch-5.html"),
            CreateRpolSourceHtml(
                (1, "Jelb", "Mon 1 Jan 2026", "01:00", "I watch the passage."),
                (2, "Dungeon Master", "Mon 1 Jan 2026", "01:05", "Jelb hears footsteps.")));
        File.WriteAllText(
            Path.Combine(directory.Path, "ch-5.bak-20260713-191558-767.html"),
            CreateRpolSourceHtml((99, "Dungeon Master", "Mon 1 Jan 2026", "01:10", "Stale backup content.")));
        File.WriteAllText(
            Path.Combine(asideDirectory, "Aside - Searching the woods.html"),
            CreateRpolSourceHtml((3, "Kelpie", "Mon 1 Jan 2026", "01:15", "Kelpie searches the woods.")));

        Form1.MyHeroBriefingPostsDirectoryOverride = directory.Path;
        try
        {
            var threadPosts = (IReadOnlyList<MyHeroBriefingThreadPosts>)(InvokeStaticMethod(typeof(Form1), "LoadMyHeroBriefingThreadPosts")
                ?? throw new InvalidOperationException("thread posts were null."));

            AssertEqual(2, threadPosts.Count, "expected current flat chapter and aside files to load");
            AssertTrue(threadPosts.Any(thread => thread.ThreadTitle == "ch-5" && thread.Posts.Count == 2), "current chapter file should load");
            AssertTrue(threadPosts.Any(thread => thread.ThreadTitle == "Aside - Searching the woods" && thread.Posts.Count == 1), "aside file should load");
            AssertFalse(threadPosts.Any(thread => thread.Posts.Any(post => post.MessageNumber == 99)), "backup files should be ignored");
        }
        finally
        {
            Form1.MyHeroBriefingPostsDirectoryOverride = null;
        }
    }

    internal static void MyHeroBriefingEncryptedIndexLoaderToleratesMalformedJson()
    {
        WithTemporaryEncryptedTextIndex(
            "{ not-json",
            () =>
            {
                var entries = (IReadOnlyList<EncryptedTextIndexEntry>)(InvokeStaticMethod(typeof(Form1), "LoadMyHeroBriefingEncryptedTextIndex")
                    ?? throw new InvalidOperationException("encrypted index entries were null."));

                AssertEqual(0, entries.Count, "malformed encrypted index should be ignored");
            });
    }

    internal static void PartyHeroListingSummaryOverridesStaleCachedSheet()
    {
        using var directory = TemporaryDirectory.Create();
        var activeDirectory = Path.Combine(directory.Path, "active");
        Directory.CreateDirectory(activeDirectory);
        File.WriteAllText(
            PlayerCharacterAssetUtility.GetPlayerCharactersListingMarkdownCachePath(directory.Path),
            """
            | Name | Class | Level | Token | HP | Race | AC |
            | ---- | ----- | ----- | ----- | -- | ---- | -- |
            | [[Jelb Garrick, Illusionist\|Jelb]] | Illusionist | 3 | ![[jelb-token.webp\|70]] | 8 | Human | 7[12] |
            """);
        File.WriteAllText(
            Path.Combine(activeDirectory, "jelb.md"),
            """
            Class: Illusionist
            HP: 4
            Level: 1

            Name: Jelb Garrick
            """);

        var heroes = PartyHeroUtility.LoadActiveParty(directory.Path);

        AssertEqual(1, heroes.Count, "unexpected active party count");
        AssertEqual("Jelb Garrick", heroes[0].Name, "sheet name should remain the displayed party name");
        AssertEqual("Illusionist", heroes[0].CharacterClass, "listing class should be used");
        AssertEqual("3", heroes[0].Level, "listing level should override stale sheet level");
        AssertEqual("8", heroes[0].HitPoints, "listing HP should override stale sheet HP");
        AssertContains(heroes[0].CharacterSheetText, "HP: 4");
    }

    internal static void PartyHeroXpVisibilityFollowsAuthenticatedCharacter()
    {
        var heroes = new PartyHeroSheet[]
        {
            new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet"),
            new("Jelb Garrick", null, "1", "Illusionist", "4", "Jelb sheet")
        };
        var xpTotals = new PcXpTotal[]
        {
            new("Kelpie Lawfuller", 7062),
            new("Jelb Garrick", 8575)
        };

        var kelpieView = PartyHeroUtility.WithVisibleXpTotals(
            heroes,
            xpTotals,
            "Kelpie",
            isDungeonMaster: false);
        var dmView = PartyHeroUtility.WithVisibleXpTotals(
            heroes,
            xpTotals,
            "Dungeon Master",
            isDungeonMaster: true);

        AssertEqual(7062, kelpieView[0].XpTotal ?? -1, "authenticated hero should see their own XP");
        AssertTrue(kelpieView[1].XpTotal is null, "authenticated hero should not see another hero's XP");
        AssertEqual(7062, dmView[0].XpTotal ?? -1, "DM should see Kelpie XP");
        AssertEqual(8575, dmView[1].XpTotal ?? -1, "DM should see Jelb XP");
    }

    internal static void TaggedNoteCipherDecryptsForMatchingLevelTag()
    {
        var hero = new HeroAccessContext(
            Level: 8,
            CharacterClass: "Paladin",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Wis"] = 12
            });

        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Level 8}The shrine door opens at moonrise.{Level 8}",
            TaggedNoteCipherMode.Encrypt);
        var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: hero);

        AssertEqual("{Level 8}The shrine door opens at moonrise.{Level 8}", decrypted, "matching level tag should decrypt note text");
        AssertTrue(encrypted.StartsWith("{Level 8}", StringComparison.Ordinal), "encrypted note should preserve opening tags as plaintext");
        AssertTrue(encrypted.EndsWith("{Level 8}", StringComparison.Ordinal), "encrypted note should preserve closing tags as plaintext");
        AssertFalse(encrypted.Contains("The shrine door opens", StringComparison.Ordinal), "encrypted note should hide wrapped plaintext");
    }

    internal static void TaggedNoteCipherDecryptsForMatchingCharacterTag()
    {
        var jelbHero = HeroAccessContext.FromPartyHeroSheet(new PartyHeroSheet(
            Name: "Jelb Stonehand",
            TokenImagePath: null,
            Level: "3",
            CharacterClass: "Fighter",
            HitPoints: "20",
            CharacterSheetText: "Name: Jelb Stonehand"));
        var otherHero = HeroAccessContext.FromPartyHeroSheet(new PartyHeroSheet(
            Name: "Kelpie Lawfuller",
            TokenImagePath: null,
            Level: "8",
            CharacterClass: "Paladin",
            HitPoints: "42",
            CharacterSheetText: "Name: Kelpie Lawfuller"));
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Character Jelb}sample text{Character Jelb}",
            TaggedNoteCipherMode.Encrypt);

        var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: jelbHero);

        AssertEqual("{Character Jelb}sample text{Character Jelb}", decrypted, "matching character tag should decrypt note text");
        AssertThrows<UnauthorizedAccessException>(
            () => TaggedNoteCipherUtility.TransformTaggedText(encrypted, TaggedNoteCipherMode.Decrypt, hero: otherHero));
    }

    internal static void TaggedNoteCipherRejectsUnmetClassTag()
    {
        var hero = new HeroAccessContext(
            Level: 12,
            CharacterClass: "Illusionist",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Class paladin}Only paladins may read this vow.{Class paladin}",
            TaggedNoteCipherMode.Encrypt);

        AssertThrows<UnauthorizedAccessException>(
            () => TaggedNoteCipherUtility.TransformTaggedText(encrypted, TaggedNoteCipherMode.Decrypt, hero: hero));
    }

    internal static void TaggedNoteCipherAcceptsEitherOrAbilityTag()
    {
        var hero = new HeroAccessContext(
            Level: 4,
            CharacterClass: "Cleric",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Wisdom"] = 15
            });
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Level 6|Wis 15}The omen points east.{Level 6|Wis 15}",
            TaggedNoteCipherMode.Encrypt);
        var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: hero);

        AssertEqual("{Level 6|Wis 15}The omen points east.{Level 6|Wis 15}", decrypted, "either-or wisdom tag should decrypt note text");
    }

    internal static void TaggedNoteCipherAcceptsBareClassAlternative()
    {
        var hero = new HeroAccessContext(
            Level: 1,
            CharacterClass: "Wizard",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Wizard|Level 5}The sigil means danger.{Wizard|Level 5}",
            TaggedNoteCipherMode.Encrypt);
        var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: hero);

        AssertTrue(encrypted.StartsWith("{Wizard|Level 5}", StringComparison.Ordinal), "encrypted note should preserve bare class opening tag");
        AssertTrue(encrypted.EndsWith("{Wizard|Level 5}", StringComparison.Ordinal), "encrypted note should preserve bare class closing tag");
        AssertEqual("{Wizard|Level 5}The sigil means danger.{Wizard|Level 5}", decrypted, "bare class alternative should decrypt note text");
    }

    internal static void TaggedNoteCipherAcceptsClassLevelShorthandAndFactionTag()
    {
        var spyHero = new HeroAccessContext(
            Level: 4,
            CharacterClass: "Spy",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var factionHero = new HeroAccessContext(
            Level: 1,
            CharacterClass: "Fighter",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Faction"] = "Scyntarn"
            });
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Spy 4|Faction Scyntarn}Nimba is actually a witch, like her sister Nuanda.{Spy 4|Faction Scyntarn}",
            TaggedNoteCipherMode.Encrypt);

        var spyDecrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: spyHero);
        var factionDecrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: factionHero);

        AssertEqual(
            "{Spy 4|Faction Scyntarn}Nimba is actually a witch, like her sister Nuanda.{Spy 4|Faction Scyntarn}",
            spyDecrypted,
            "class level shorthand should decrypt note text");
        AssertEqual(spyDecrypted, factionDecrypted, "faction tag alternative should decrypt the same note text");
    }

    internal static void TaggedNoteCipherAcceptsGroupedAndExpressionTag()
    {
        const string taggedPlaintext = "{(Level 6 && Spy 3)|Scyntarn 9}The sealed paragraph opens.{(Level 6 && Spy 3)|Scyntarn 9}";
        var spyHero = new HeroAccessContext(
            Level: 6,
            CharacterClass: "Spy",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var scyntarnHero = new HeroAccessContext(
            Level: 1,
            CharacterClass: "Fighter",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            RankedMemberships: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Scyntarn"] = 9
            });
        var deniedHero = new HeroAccessContext(
            Level: 6,
            CharacterClass: "Fighter",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            RankedMemberships: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Scyntarn"] = 8
            });
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            taggedPlaintext,
            TaggedNoteCipherMode.Encrypt);

        var spyDecrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: spyHero);
        var scyntarnDecrypted = TaggedNoteCipherUtility.TransformTaggedText(
            encrypted,
            TaggedNoteCipherMode.Decrypt,
            hero: scyntarnHero);

        AssertEqual(taggedPlaintext, spyDecrypted, "level and spy class-level branch should decrypt note text");
        AssertEqual(taggedPlaintext, scyntarnDecrypted, "ranked Scyntarn branch should decrypt note text");
        AssertThrows<UnauthorizedAccessException>(
            () => TaggedNoteCipherUtility.TransformTaggedText(encrypted, TaggedNoteCipherMode.Decrypt, hero: deniedHero));
    }

    internal static void TaggedNoteCipherReportsMismatchedDecryptTags()
    {
        var hero = new HeroAccessContext(
            Level: 8,
            CharacterClass: "Spy",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Level 8}The ward is real.{Level 8}",
            TaggedNoteCipherMode.Encrypt);
        var mismatched = encrypted[..^"{Level 8}".Length] + "{Level 9}";
        var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
            mismatched,
            TaggedNoteCipherMode.Decrypt,
            hero: hero);

        AssertEqual(
            "unable to decrypt due to non-matching opening and closing tags",
            decrypted,
            "mismatched opening and closing tags should return the player-safe decrypt failure text");
    }

    internal static void TaggedNoteCipherReportsEncryptedMarkdownBlockCounts()
    {
        var validBlock = TaggedNoteCipherUtility.TransformTaggedText(
            "{Level 8}The ward is real.{Level 8}",
            TaggedNoteCipherMode.Encrypt);
        var secondValidBlock = TaggedNoteCipherUtility.TransformTaggedText(
            "{(Level 6 && Spy 3)|Scyntarn 9}The sealed paragraph opens.{(Level 6 && Spy 3)|Scyntarn 9}",
            TaggedNoteCipherMode.Encrypt);
        var mismatchedBlock = secondValidBlock[..^"{(Level 6 && Spy 3)|Scyntarn 9}".Length] + "{Scyntarn 9}";
        var wrappedValidBlock = validBlock.Replace("PAN1:", $"{Environment.NewLine}  PAN1:", StringComparison.Ordinal);
        var markdown = string.Join(
            Environment.NewLine + Environment.NewLine,
            "Plain markdown before encrypted text.",
            wrappedValidBlock,
            mismatchedBlock,
            secondValidBlock);

        var report = TaggedNoteCipherUtility.EncryptedTextReportFromMarkdown(markdown);

        AssertEqual(
            "valid encrypted blocks: 2, mismatched tags: 1",
            report,
            "encrypted markdown report should count matching and mismatched tag wrappers");
    }

    internal static void TaggedNoteCipherIndexesEncryptedMarkdownFrontmatterTags()
    {
        var encryptedBlock = TaggedNoteCipherUtility.TransformTaggedText(
            "{Level 8}The ward is real.{Level 8}",
            TaggedNoteCipherMode.Encrypt);
        var secondEncryptedBlock = TaggedNoteCipherUtility.TransformTaggedText(
            "{Spy 4|Faction Scyntarn}The innkeeper knows the pass.{Spy 4|Faction Scyntarn}",
            TaggedNoteCipherMode.Encrypt);
        var markdown = string.Join(
            Environment.NewLine,
            "---",
            "tags:",
            "  - npc",
            "  - spy",
            "  - \"Scyntarn\"",
            "---",
            "Plain text.",
            encryptedBlock,
            secondEncryptedBlock);

        var entry = TaggedNoteCipherUtility.CreateEncryptedTextIndexEntry(
            "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
            markdown);

        if (entry is null)
        {
            throw new InvalidOperationException("encrypted markdown should produce an index entry");
        }

        AssertEqual(2, entry.EncryptedSections, "encrypted text index should count encrypted markdown blocks");
        AssertEqual(
            "npc,spy,Scyntarn",
            string.Join(",", entry.FrontmatterTags),
            "encrypted text index should preserve frontmatter tags");
        AssertEqual(
            TaggedNoteCipherUtility.EncryptedTextIndexFileName,
            "encrypted-text-index.json",
            "encrypted text index filename should match the proposed JSON artifact name");
    }

    internal static void TaggedNoteCipherAuthenticatesVisibleTags()
    {
        var originalHero = new HeroAccessContext(
            Level: 8,
            CharacterClass: "Fighter",
            AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var lowerLevelHero = originalHero with { Level = 7 };
        var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
            "{Level 8}The ward is real.{Level 8}",
            TaggedNoteCipherMode.Encrypt);
        var tampered = encrypted.Replace("{Level 8}", "{Level 7}", StringComparison.Ordinal);

        AssertThrows<InvalidOperationException>(
            () => TaggedNoteCipherUtility.TransformTaggedText(tampered, TaggedNoteCipherMode.Decrypt, hero: lowerLevelHero));
    }

    internal static void XpDisplayRecognizesDungeonMasterAccess()
    {
        AssertTrue(
            (bool)(InvokeStaticMethod(typeof(Form1), "IsDungeonMasterXpAccess", "Dungeon Master") ?? false),
            "Dungeon Master should unlock all XP totals");
        AssertTrue(
            (bool)(InvokeStaticMethod(typeof(Form1), "IsDungeonMasterXpAccess", "dungeon master") ?? false),
            "Dungeon Master XP access should be case-insensitive");
        AssertFalse(
            (bool)(InvokeStaticMethod(typeof(Form1), "IsDungeonMasterXpAccess", "Kelpie") ?? true),
            "ordinary PCs should not unlock all XP totals");
    }

    internal static void XpDisplayFindsTotalsByFirstAndFullCharacterNames()
    {
        var totals = new PcXpTotal[]
        {
            new("Kelpie Lawfuller", 7062),
            new("Jelb", 8575)
        };

        var kelpieTotal = (PcXpTotal?)InvokeStaticMethod(
            typeof(Form1),
            "FindXpTotalForCharacter",
            totals,
            "Kelpie");
        var jelbTotal = (PcXpTotal?)InvokeStaticMethod(
            typeof(Form1),
            "FindXpTotalForCharacter",
            totals,
            "Jelb Garrick");

        if (kelpieTotal is null)
        {
            throw new InvalidOperationException("first-name Kelpie lookup should find full-name XP row");
        }

        if (jelbTotal is null)
        {
            throw new InvalidOperationException("full-name Jelb lookup should find first-name XP row");
        }

        AssertEqual(new PcXpTotal("Kelpie Lawfuller", 7062), kelpieTotal!, "unexpected Kelpie XP row");
        AssertEqual(new PcXpTotal("Jelb", 8575), jelbTotal!, "unexpected Jelb XP row");
    }

    internal static void XpDisplayStoresMultipleTotalsForDungeonMaster()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var totals = new PcXpTotal[]
            {
                new("Kelpie", 7062),
                new("Jelb", 8575)
            };

            InvokePrivateMethod(form, "ShowXpTotals", "As of 7.04.2026", totals);

            var storedTotals = (IReadOnlyList<PcXpTotal>)(GetPrivateField(form, "_xpTotals")
                ?? throw new InvalidOperationException("_xpTotals was null."));
            AssertEqual(2, storedTotals.Count, "Dungeon Master XP display should retain all requested totals");
            AssertEqual(new PcXpTotal("Kelpie", 7062), storedTotals[0], "unexpected first stored XP total");
            AssertTrue((bool)(GetPrivateField(form, "_showXpTotal") ?? false), "XP display should be active");
        });
    }

    internal static void XpTrackingParserReadsLatestTableTotals()
    {
        const string markdown =
            """
            ---
            status: XP
            ---
            As of 7.04.2026

            | Name     | Class       | Level | XP Total |
            | -------- | ----------- | ----- | -------- |
            | Kelpie   | Fighter     | 3     | 7,062    |
            | Jelb     | Illusionist | 2     | 8,575    |
            | Max      | Theurge     | 1     | 3,175    |
            | Geoffroy | Cleric      | 2     | 2,950    |

            As of 7.01.2026

            | Name     | Class       | Level | XP Total |
            | -------- | ----------- | ----- | -------- |
            | Kelpie   | Fighter     | 3     | 6,562    |
            | Jelb     | Illusionist | 2     | 8,075    |
            """;

        var totals = XpTrackingUtility.ParseCurrentXpTotals(markdown).ToArray();

        AssertEqual(4, totals.Length, "expected latest XP table to contain four current PCs");
        AssertEqual(new PcXpTotal("Kelpie", 7062), totals[0], "unexpected Kelpie XP total");
        AssertEqual(new PcXpTotal("Jelb", 8575), totals[1], "unexpected Jelb XP total");
        AssertEqual(new PcXpTotal("Max", 3175), totals[2], "unexpected Max XP total");
        AssertEqual(new PcXpTotal("Geoffroy", 2950), totals[3], "unexpected Geoffroy XP total");
    }

    internal static void XpTrackingParserRejectsMissingLatestTable()
    {
        var exception = AssertThrows<InvalidOperationException>(() =>
            XpTrackingUtility.ParseCurrentXpTotals(
                """
                As of 7.04.2026

                No table today.
                """));

        AssertContains(exception.Message, "latest XP tracking date does not have a markdown table");
    }

    internal static void XpTrackingFailureMessageHidesUrlAndDirectsPlayersToDm()
    {
        const string trackingUrl = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking";
        var message = XpTrackingUtility.FormatUserFacingFailureMessage(
            new InvalidOperationException($"XP tracking markdown could not be fetched from {trackingUrl}."));

        AssertContains(message, "XP totals could not be loaded from the XP Tracking page.");
        AssertContains(message, "Please contact the DM");
        AssertContains(message, "Technical detail:");
        AssertFalse(message.Contains(trackingUrl, StringComparison.Ordinal), "XP failure dialog should not expose the unlisted tracking URL");
        AssertFalse(message.Contains("https://", StringComparison.OrdinalIgnoreCase), "XP failure dialog should not expose URL-shaped text");
    }

    internal static void XpTrackingMissingPcMessageDirectsPlayersToDm()
    {
        var message = XpTrackingUtility.FormatMissingPcFailureMessage("Kelpie");

        AssertContains(message, "No XP total was found for 'Kelpie'.");
        AssertContains(message, "Please contact the DM");
        AssertFalse(message.Contains("https://", StringComparison.OrdinalIgnoreCase), "missing-PC message should not expose URL-shaped text");
    }

    internal static void ExternalUrlLaunchPolicyAcceptsHttpsAndRejectsHttp()
    {
        var http = ExternalUrlLaunchUtility.Validate(" http://rpol.net/path?q=one ");
        var https = ExternalUrlLaunchUtility.Validate("https://rpol.net/game.php?gi=80170");

        AssertFalse(http.IsAllowed, "HTTP URLs should be rejected");
        AssertTrue(https.IsAllowed, "HTTPS URLs should be allowed");
    }

    internal static void IllusionistProgressionDataExposesXpThresholds()
    {
        var path = Path.Combine(GetRepositoryRoot(), "class-progression.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        AssertEqual(1, root.GetProperty("schema_version").GetInt32(), "unexpected class progression schema");

        var illusionist = root
            .GetProperty("classes")
            .GetProperty("illusionist");
        AssertEqual("Illusionist", illusionist.GetProperty("name").GetString() ?? string.Empty, "unexpected class name");
        AssertEqual(36, illusionist.GetProperty("maximum_level").GetInt32(), "unexpected Illusionist maximum level");
        AssertEqual(14, illusionist.GetProperty("published_maximum_level").GetInt32(), "unexpected published Illusionist maximum level");

        var extension = illusionist.GetProperty("extended_progression");
        AssertEqual(14, extension.GetProperty("starts_after_level").GetInt32(), "unexpected Illusionist extension starting level");
        AssertEqual(150000, extension.GetProperty("xp_per_additional_level").GetInt32(), "unexpected Illusionist extended XP increment");
        AssertFalse(extension.GetProperty("mechanical_statistics_available").GetBoolean(), "extended Illusionist mechanics should not be presented as published");

        var progression = illusionist.GetProperty("level_progression").EnumerateArray().ToArray();
        AssertEqual(36, progression.Length, "expected all Illusionist levels");
        var expectedThresholds = new[]
        {
            0, 2500, 5000, 10000, 20000, 40000, 80000,
            150000, 300000, 450000, 600000, 750000, 900000, 1050000,
            1200000, 1350000, 1500000, 1650000, 1800000, 1950000, 2100000,
            2250000, 2400000, 2550000, 2700000, 2850000, 3000000, 3150000,
            3300000, 3450000, 3600000, 3750000, 3900000, 4050000, 4200000,
            4350000
        };
        for (var index = 0; index < progression.Length; index++)
        {
            AssertEqual(index + 1, progression[index].GetProperty("level").GetInt32(), "unexpected Illusionist level");
            AssertEqual(
                expectedThresholds[index],
                progression[index].GetProperty("minimum_xp").GetInt32(),
                $"unexpected XP threshold for Illusionist level {index + 1}");
            if (index < 14)
            {
                AssertEqual(
                    6,
                    progression[index].GetProperty("spell_slots").GetArrayLength(),
                    $"unexpected spell-slot columns for Illusionist level {index + 1}");
            }
            else
            {
                AssertTrue(
                    progression[index].GetProperty("extrapolated").GetBoolean(),
                    $"Illusionist level {index + 1} should be marked as extrapolated");
                AssertFalse(
                    progression[index].TryGetProperty("spell_slots", out _),
                    $"Illusionist level {index + 1} should not invent unpublished spell slots");
                AssertFalse(
                    progression[index].TryGetProperty("hit_dice", out _),
                    $"Illusionist level {index + 1} should not invent unpublished hit dice");
                AssertFalse(
                    progression[index].TryGetProperty("thac0", out _),
                    $"Illusionist level {index + 1} should not invent unpublished THAC0");
                AssertFalse(
                    progression[index].TryGetProperty("saving_throws", out _),
                    $"Illusionist level {index + 1} should not invent unpublished saving throws");
            }
        }
    }

    internal static void ExternalUrlLaunchPolicyRejectsUnsafeInputs()
    {
        var relative = ExternalUrlLaunchUtility.Validate("/relative/path");
        var file = ExternalUrlLaunchUtility.Validate("file:///C:/temp/report.html");
        var credentialed = ExternalUrlLaunchUtility.Validate("https://user:pass@example.test/private");
        var disallowed = ExternalUrlLaunchUtility.Validate("https://unexpected.example.test/private");

        AssertFalse(relative.IsAllowed, "relative URLs should not be opened externally");
        AssertContains(relative.RejectionReason ?? string.Empty, "absolute URL");
        AssertFalse(file.IsAllowed, "file URLs should not be opened from search results");
        AssertContains(file.RejectionReason ?? string.Empty, "HTTP and HTTPS");
        AssertFalse(credentialed.IsAllowed, "credentialed URLs should not be opened externally");
        AssertContains(credentialed.RejectionReason ?? string.Empty, "credentials");
        AssertFalse(disallowed.IsAllowed, "non-allowlisted URLs should not be opened externally");
        AssertContains(disallowed.RejectionReason ?? string.Empty, "allowlist");
    }

    internal static void HeroImagePathsFollowListingMarkdownTable()
    {
        using var directory = TemporaryDirectory.Create();
        var pcsDirectory = Path.Combine(directory.Path, "PCs");
        var activeDirectory = Path.Combine(pcsDirectory, "active");
        Directory.CreateDirectory(activeDirectory);

        var listedImagePath = Path.Combine(activeDirectory, "alice-token.webp");
        var strayImagePath = Path.Combine(activeDirectory, "stray-token.webp");
        File.WriteAllText(listedImagePath, "listed");
        File.WriteAllText(strayImagePath, "stray");

        var listingMarkdown = """
            | Name | Character | Notes | Hero |
            | --- | --- | --- | --- |
            | Alice | [[Alice]] | active | ![[alice-token.webp]] |
            | Bob | [[Bob]] | active | ![[bob-token.webp]] |
            """;

        var result = InvokeStaticMethod(
            typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.PlayerCharacterAssetUtility")
                ?? throw new InvalidOperationException("Unable to find PlayerCharacterAssetUtility type."),
            "GetListedActiveHeroImagePaths",
            listingMarkdown,
            pcsDirectory);

        var paths = ((string[])result!)
            .Select(path => Path.GetFileName(path) ?? string.Empty)
            .ToArray();

        AssertEqual(1, paths.Length, "expected only heroes listed in the markdown table to be selected");
        AssertEqual("alice-token.webp", paths[0], "unexpected hero image selected from active directory");
        AssertFalse(paths.Contains("stray-token.webp", StringComparer.OrdinalIgnoreCase), "unlisted hero image should not be selected");
    }

    internal static void HeroAssetPathsRejectEscapedTargets()
    {
        using var directory = TemporaryDirectory.Create();
        var activeDirectory = Path.Combine(directory.Path, "PCs", "active");
        var utilityType = typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.PlayerCharacterAssetUtility")
            ?? throw new InvalidOperationException("Unable to find PlayerCharacterAssetUtility type.");

        var safePath = (string)(InvokeStaticMethod(
            utilityType,
            "GetHeroAssetPath",
            activeDirectory,
            "alice-token.webp") ?? throw new InvalidOperationException("GetHeroAssetPath returned null."));

        AssertTrue(
            safePath.StartsWith(activeDirectory, StringComparison.OrdinalIgnoreCase),
            "safe hero asset path should remain under the active PCs directory");

        AssertThrows<InvalidOperationException>(() =>
            InvokeStaticMethod(utilityType, "GetHeroAssetPath", activeDirectory, "..\\escape.webp"));
        AssertThrows<InvalidOperationException>(() =>
            InvokeStaticMethod(utilityType, "GetHeroAssetPath", activeDirectory, "/escape.webp"));
    }
}
