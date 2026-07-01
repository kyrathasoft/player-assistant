using PlayerAssistant;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

var requestedTestFilter = args.Length > 0 ? string.Join(" ", args).Trim() : string.Empty;

var tests = new (string Name, Action Test)[]
{
    ("orcish translator returns one-to-one english mapping", OrcishTranslatorReturnsOneToOneEnglishMapping),
    ("orcish translator returns several english matches for one orcish word", OrcishTranslatorReturnsSeveralEnglishMatchesForOneOrcishWord),
    ("orcish translator uses part of speech to disambiguate", OrcishTranslatorUsesPartOfSpeechToDisambiguate),
    ("orcish translator filters human terms by register", OrcishTranslatorFiltersHumanTermsByRegister),
    ("orcish translator supports reverse lookup for respectful human term", OrcishTranslatorSupportsReverseLookupForRespectfulHumanTerm),
    ("orcish translator generates plural possessives systematically", OrcishTranslatorGeneratesPluralPossessivesSystematically),
    ("orcish translator supports pale adjective registers", OrcishTranslatorSupportsPaleAdjectiveRegisters),
    ("orcish translator prefers vrak for default skin terms", OrcishTranslatorPrefersVrakForDefaultSkinTerms),
    ("orcish translator reserves drukh for reverent monster hide", OrcishTranslatorReservesDrukhForReverentMonsterHide),
    ("orcish translator supports oglur verb family", OrcishTranslatorSupportsOglurVerbFamily),
    ("orcish translator exposes both if variants", OrcishTranslatorExposesBothIfVariants),
    ("orcish translator alternates repeated if terms in sequence", OrcishTranslatorAlternatesRepeatedIfTermsInSequence),
    ("orcish translator exposes both but variants", OrcishTranslatorExposesBothButVariants),
    ("orcish translator supports sarcastic but variants", OrcishTranslatorSupportsSarcasticButVariants),
    ("orcish translator random but picker returns valid variant", OrcishTranslatorRandomButPickerReturnsValidVariant),
    ("startup manifest status distinguishes skipped and failed", StartupManifestStatusDistinguishesSkippedAndFailed),
    ("startup error log entry includes phase and exception", StartupErrorLogEntryIncludesPhaseAndException),
    ("show-all thread url preserves base query and adds show all", ShowAllThreadUrlPreservesBaseQueryAndAddsShowAll),
    ("die roll extraction keeps only saved-log lines", DieRollExtractionKeepsOnlySavedLogLines),
    ("die roll extraction handles live rpol paragraph markup", DieRollExtractionHandlesLiveRpolParagraphMarkup),
    ("die roll sync appends only unsaved rolls", DieRollSyncAppendsOnlyUnsavedRolls),
    ("regional map downloads when missing", RegionalMapDownloadsWhenMissing),
    ("regional map downloads when older than one hour", RegionalMapDownloadsWhenOlderThanOneHour),
    ("regional map skips when newer than one hour", RegionalMapSkipsWhenNewerThanOneHour),
    ("regional map downloads when newer but transparent", RegionalMapDownloadsWhenNewerButTransparent),
    ("startup status includes download count and size", StartupStatusIncludesDownloadCountAndSize),
    ("adjusted post tallies aggregate saved IC html", AdjustedPostTalliesAggregateSavedIcHtml),
    ("keyword search falls back to The-prefixed term", KeywordSearchFallsBackToThePrefixedTerm),
    ("keyword search keeps quoted phrases together", KeywordSearchKeepsQuotedPhrasesTogether),
    ("keyword search accepts url source metadata", KeywordSearchAcceptsUrlSourceMetadata),
    ("keyword search filters rpol hero metadata-only hits", KeywordSearchFiltersRpolHeroMetadataOnlyHits),
    ("search enter triggers click when enabled", SearchEnterTriggersClickWhenEnabled),
    ("keyword search offers online fallback on local miss", KeywordSearchOffersOnlineFallbackOnLocalMiss),
    ("keyword search rpol scope excludes obsidian-only whiteheart", KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheart),
    ("keyword search rpol scope excludes obsidian-only whiteheart stiffwhiskers", KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheartStiffwhiskers),
    ("hero image paths follow listing markdown table", HeroImagePathsFollowListingMarkdownTable),
    ("legacy local settings migrate to portable encryption", LegacyLocalSettingsMigrateToPortableEncryption),
    ("local settings are encrypted on load", LocalSettingsAreEncryptedOnLoad)
};

if (!string.IsNullOrWhiteSpace(requestedTestFilter))
{
    tests = tests
        .Where(test => test.Name.Contains(requestedTestFilter, StringComparison.OrdinalIgnoreCase))
        .ToArray();
}

var failures = new List<string>();

foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        var rootException = ex is TargetInvocationException tie && tie.InnerException is not null
            ? tie.InnerException
            : ex;

        failures.Add($"{name}: {rootException}");
        Console.WriteLine($"FAIL {name}: {rootException}");
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Failures:");
    foreach (var failure in failures)
    {
        Console.WriteLine(failure);
    }

    return 1;
}

return 0;

static void OrcishTranslatorReturnsOneToOneEnglishMapping()
{
    var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("hello");

    AssertEqual(1, results.Count, "expected one translation for hello");
    AssertEqual("zug", results[0].Translation, "unexpected Orcish translation for hello");
}

static void OrcishTranslatorReturnsSeveralEnglishMatchesForOneOrcishWord()
{
    var results = OrcishTranslatorUtility.TranslateOrcishToEnglish("mokra");

    AssertEqual(2, results.Count, "expected two English translations for mokra");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "friend", StringComparison.OrdinalIgnoreCase)), "expected friend translation");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "ally", StringComparison.OrdinalIgnoreCase)), "expected ally translation");
}

static void OrcishTranslatorUsesPartOfSpeechToDisambiguate()
{
    var nounResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("watch", partOfSpeech: "noun");
    var verbResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("watch", partOfSpeech: "verb");
    var unfilteredResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("watch");

    AssertEqual(1, nounResults.Count, "expected one noun translation for watch");
    AssertEqual("thrak", nounResults[0].Translation, "unexpected noun translation for watch");
    AssertEqual(1, verbResults.Count, "expected one verb translation for watch");
    AssertEqual("gor", verbResults[0].Translation, "unexpected verb translation for watch");
    AssertEqual(2, unfilteredResults.Count, "expected both translations when no part of speech is supplied");
}

static void OrcishTranslatorFiltersHumanTermsByRegister()
{
    var neutralResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("human", partOfSpeech: "noun", requiredTags: ["neutral"]);
    var insultingResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("weak human", partOfSpeech: "noun", requiredTags: ["insulting"]);
    var respectfulResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("free human", partOfSpeech: "noun", requiredTags: ["respectful"]);
    var pluralResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("humans", partOfSpeech: "noun", requiredTags: ["neutral", "plural"]);

    AssertEqual(1, neutralResults.Count, "expected one neutral human translation");
    AssertEqual("marg", neutralResults[0].Translation, "unexpected neutral human translation");
    AssertEqual(1, insultingResults.Count, "expected one insulting human translation");
    AssertEqual("thrum-skin", insultingResults[0].Translation, "unexpected insulting human translation");
    AssertEqual(1, respectfulResults.Count, "expected one respectful human translation");
    AssertEqual("surgar", respectfulResults[0].Translation, "unexpected respectful human translation");
    AssertEqual(1, pluralResults.Count, "expected one plural neutral human translation");
    AssertEqual("margi", pluralResults[0].Translation, "unexpected plural human translation");
}

static void OrcishTranslatorSupportsReverseLookupForRespectfulHumanTerm()
{
    var results = OrcishTranslatorUtility.TranslateOrcishToEnglish("surgar", partOfSpeech: "noun", requiredTags: ["respectful"]);

    AssertEqual(2, results.Count, "expected respectful reverse lookup to surface both English glosses");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "sun-born", StringComparison.OrdinalIgnoreCase)), "expected sun-born reverse translation");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "free human", StringComparison.OrdinalIgnoreCase)), "expected free human reverse translation");
}

static void OrcishTranslatorGeneratesPluralPossessivesSystematically()
{
    var neutralResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("humans'", partOfSpeech: "noun", requiredTags: ["neutral", "plural", "possessive"]);
    var insultingResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("softskins'", partOfSpeech: "noun", requiredTags: ["insulting", "plural", "possessive"]);
    var respectfulReverseResults = OrcishTranslatorUtility.TranslateOrcishToEnglish("surgariuk", partOfSpeech: "noun", requiredTags: ["respectful", "plural", "possessive"]);

    AssertEqual(1, neutralResults.Count, "expected neutral plural possessive");
    AssertEqual("margiuk", neutralResults[0].Translation, "unexpected neutral plural possessive");
    AssertEqual(1, insultingResults.Count, "expected insulting plural possessive");
    AssertEqual("thrum-skinaruk", insultingResults[0].Translation, "unexpected insulting plural possessive");
    AssertTrue(respectfulReverseResults.Any(result => string.Equals(result.Translation, "sun-born ones'", StringComparison.OrdinalIgnoreCase)), "expected respectful plural possessive reverse translation");
    AssertTrue(respectfulReverseResults.Any(result => string.Equals(result.Translation, "free humans'", StringComparison.OrdinalIgnoreCase)), "expected respectful plural possessive reverse translation for gloss");
}

static void OrcishTranslatorSupportsPaleAdjectiveRegisters()
{
    var neutralResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("pale", partOfSpeech: "adjective", requiredTags: ["neutral"]);
    var fearfulResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("pale with fear", partOfSpeech: "adjective", requiredTags: ["fear", "pejorative"]);
    var reverseResults = OrcishTranslatorUtility.TranslateOrcishToEnglish("kelnagak", partOfSpeech: "adjective", requiredTags: ["fear", "pejorative"]);

    AssertEqual(1, neutralResults.Count, "expected neutral pale adjective");
    AssertEqual("kelnib", neutralResults[0].Translation, "unexpected neutral pale adjective");
    AssertEqual(1, fearfulResults.Count, "expected fear-pale adjective");
    AssertEqual("kelnagak", fearfulResults[0].Translation, "unexpected fear-pale adjective");
    AssertTrue(reverseResults.Any(result => string.Equals(result.Translation, "pale with fear", StringComparison.OrdinalIgnoreCase)), "expected fear-pale reverse translation");
}

static void OrcishTranslatorPrefersVrakForDefaultSkinTerms()
{
    var skinResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("skin", partOfSpeech: "noun", requiredTags: ["default"]);
    var hidesResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("hides", partOfSpeech: "noun", requiredTags: ["default", "plural"]);
    var possessiveResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("skins'", partOfSpeech: "noun", requiredTags: ["default", "plural", "possessive"]);

    AssertEqual(1, skinResults.Count, "expected one default skin translation");
    AssertEqual("vrak", skinResults[0].Translation, "unexpected default skin translation");
    AssertEqual(1, hidesResults.Count, "expected one default hides translation");
    AssertEqual("vraki", hidesResults[0].Translation, "unexpected default hides translation");
    AssertEqual(1, possessiveResults.Count, "expected one default plural possessive skin translation");
    AssertEqual("vrakiuk", possessiveResults[0].Translation, "unexpected default plural possessive skin translation");
}

static void OrcishTranslatorReservesDrukhForReverentMonsterHide()
{
    var reverentResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("hide", partOfSpeech: "noun", requiredTags: ["reverent", "monster", "thick-hide"]);
    var reverseResults = OrcishTranslatorUtility.TranslateOrcishToEnglish("drukh", partOfSpeech: "noun", requiredTags: ["reverent", "monster", "thick-hide"]);
    var pluralPossessiveResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("hides'", partOfSpeech: "noun", requiredTags: ["reverent", "monster", "thick-hide", "plural", "possessive"]);

    AssertEqual(1, reverentResults.Count, "expected one reverent monster hide translation");
    AssertEqual("drukh", reverentResults[0].Translation, "unexpected reverent monster hide translation");
    AssertTrue(reverseResults.Any(result => string.Equals(result.Translation, "hide", StringComparison.OrdinalIgnoreCase)), "expected reverse hide translation for drukh");
    AssertEqual(1, pluralPossessiveResults.Count, "expected one reverent monster plural possessive hide translation");
    AssertEqual("drukhiuk", pluralPossessiveResults[0].Translation, "unexpected reverent monster plural possessive hide translation");
}

static void OrcishTranslatorSupportsOglurVerbFamily()
{
    AssertEqual("oglar", OrcishTranslatorUtility.TranslateEnglishToOrcish("to see", partOfSpeech: "verb", requiredTags: ["infinitive"])[0].Translation, "unexpected infinitive see translation");
    AssertEqual("oglur", OrcishTranslatorUtility.TranslateEnglishToOrcish("sees", partOfSpeech: "verb", requiredTags: ["present"])[0].Translation, "unexpected present see translation");
    AssertEqual("oglash", OrcishTranslatorUtility.TranslateEnglishToOrcish("saw", partOfSpeech: "verb", requiredTags: ["past"])[0].Translation, "unexpected past see translation");
    AssertEqual("ogluk", OrcishTranslatorUtility.TranslateEnglishToOrcish("have seen", partOfSpeech: "verb", requiredTags: ["perfect"])[0].Translation, "unexpected perfect see translation");
    AssertEqual("oglurin", OrcishTranslatorUtility.TranslateEnglishToOrcish("is seeing", partOfSpeech: "verb", requiredTags: ["progressive"])[0].Translation, "unexpected progressive see translation");
    AssertEqual("oglaruk", OrcishTranslatorUtility.TranslateEnglishToOrcish("will see", partOfSpeech: "verb", requiredTags: ["future"])[0].Translation, "unexpected future see translation");
    AssertEqual("noglur", OrcishTranslatorUtility.TranslateEnglishToOrcish("does not see", partOfSpeech: "verb", requiredTags: ["present", "negative"])[0].Translation, "unexpected negative present see translation");
    AssertEqual("noglash", OrcishTranslatorUtility.TranslateEnglishToOrcish("did not see", partOfSpeech: "verb", requiredTags: ["past", "negative"])[0].Translation, "unexpected negative past see translation");
}

static void OrcishTranslatorExposesBothIfVariants()
{
    var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("if", partOfSpeech: "conjunction");

    AssertEqual(2, results.Count, "expected two plain if variants");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "ut", StringComparison.OrdinalIgnoreCase)), "expected ut variant");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "ka", StringComparison.OrdinalIgnoreCase)), "expected ka variant");
}

static void OrcishTranslatorAlternatesRepeatedIfTermsInSequence()
{
    var results = OrcishTranslatorUtility.TranslateEnglishSequenceToOrcish(["if", "if", "if", "if"]);

    AssertEqual(4, results.Count, "expected four translated terms");
    AssertFalse(string.IsNullOrWhiteSpace(results[0].Translation), "expected first if translation");
    AssertFalse(string.Equals(results[0].Translation, results[1].Translation, StringComparison.OrdinalIgnoreCase), "expected second if to alternate");
    AssertEqual(results[0].Translation, results[2].Translation, "expected third if to alternate back to the first choice");
    AssertEqual(results[1].Translation, results[3].Translation, "expected fourth if to alternate back to the second choice");
}

static void OrcishTranslatorExposesBothButVariants()
{
    var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("but", partOfSpeech: "conjunction");

    AssertEqual(2, results.Count, "expected two plain but variants");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "rokh", StringComparison.OrdinalIgnoreCase)), "expected rokh variant");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "nar", StringComparison.OrdinalIgnoreCase)), "expected nar variant");
}

static void OrcishTranslatorSupportsSarcasticButVariants()
{
    var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("sarcastic but", partOfSpeech: "conjunction");

    AssertEqual(2, results.Count, "expected two sarcastic but variants");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "rokhki", StringComparison.OrdinalIgnoreCase)), "expected sarcastic rokh variant");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "narki", StringComparison.OrdinalIgnoreCase)), "expected sarcastic nar variant");
}

static void OrcishTranslatorRandomButPickerReturnsValidVariant()
{
    var result = OrcishTranslatorUtility.TranslateEnglishToOrcishRandom("but", partOfSpeech: "conjunction");

    AssertTrue(result is not null, "expected random but picker to return a variant");
    AssertTrue(
        string.Equals(result!.Translation, "rokh", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(result.Translation, "nar", StringComparison.OrdinalIgnoreCase),
        "expected random but picker to return one of the plain variants");
}

static void StartupManifestStatusDistinguishesSkippedAndFailed()
{
    AssertEqual("downloaded", Form1.GetManifestStatus(downloaded: true, errorMessage: null), "unexpected downloaded status");
    AssertEqual("skipped", Form1.GetManifestStatus(downloaded: false, errorMessage: null), "unexpected skipped status");
    AssertEqual("failed", Form1.GetManifestStatus(downloaded: false, errorMessage: "boom"), "unexpected failed status");
}

static void StartupErrorLogEntryIncludesPhaseAndException()
{
    var entry = Form1.FormatStartupErrorLogEntry("ooc thread downloads", new InvalidOperationException("Missing RPoL credentials."));

    AssertContains(entry, "ooc thread downloads");
    AssertContains(entry, "InvalidOperationException");
    AssertContains(entry, "Missing RPoL credentials.");
}

static void ShowAllThreadUrlPreservesBaseQueryAndAddsShowAll()
{
    const string threadUrl = "https://rpol.net/display.cgi?gi=80170&ti=17&date=1779581880";

    var showAllUrl = RpolThreadPostUtility.GetShowAllThreadUrl(threadUrl);

    AssertEqual(
        "https://rpol.net/display.cgi?gi=80170&ti=17&date=1779581880&msgpage=&show=all",
        showAllUrl,
        "unexpected show-all thread url");
}

static void DieRollExtractionKeepsOnlySavedLogLines()
{
    const string html = """
        <html><body>
        <div>18:37, Today: Dungeon Master rolled 4 using 1d6.  orcs' init for rnd 2 abandoned rock quarry. – [roll=1782257860.18744.396686]</div>
        <div>18:05, Today: Jelb Garrick rolled 6 using 2d4.  Fire Damage. – [roll=1782255916.99298.396653]</div>
        <div>15:41, Today: Kelpie Lawfuller rolled 17,8 using d20+2,d8+2.  Sword attack (held action). – [roll=1782247280.40694.396648]</div>
        <div>18:44, Today: Maximilian Yragerne rolled 16 using 1d20. dex.</div>
        <div>Dice are fun.</div>
        </body></html>
        """;

    var entries = GameForumUtility.ExtractDieRollEntries(html);

    AssertEqual(3, entries.Length, "unexpected die roll entry count");
    AssertEqual("1782257860.18744.396686", entries[0].RollId, "unexpected first roll id");
    AssertContains(entries[1].Line, "Jelb Garrick rolled 6 using 2d4.");
    AssertContains(entries[2].Line, "[roll=1782247280.40694.396648]");
}

static void DieRollExtractionHandlesLiveRpolParagraphMarkup()
{
    const string html = """
        <div class="info_box">
        <p style="margin-left: 2em; text-indent: -2em;">18:37, Today: Dungeon Master rolled 4 using 1d6.&nbsp; orcs' init for rnd 2 abandoned rock quarry.
         –&nbsp;<span class="link-colour">[roll=1782257860.18744.396686]</span></p>
        <p style="margin-left: 2em; text-indent: -2em;">18:05, Today: Jelb Garrick rolled 6 using 2d4.&nbsp; Fire Damage.
         –&nbsp;<span class="link-colour">[roll=1782255916.99298.396653]</span></p>
        <p style="margin-left: 2em; text-indent: -2em;">15:41, Today: Kelpie Lawfuller rolled 17,8 using d20+2,d8+2.&nbsp; Sword attack (held action).
         –&nbsp;<span class="link-colour">[roll=1782247280.40694.396648]</span></p>
        </div>
        """;

    var entries = GameForumUtility.ExtractDieRollEntries(html);

    AssertEqual(3, entries.Length, "unexpected die roll entry count from paragraph markup");
    AssertEqual("1782257860.18744.396686", entries[0].RollId, "unexpected first roll id from paragraph markup");
    AssertContains(entries[0].Line, "abandoned rock quarry. – [roll=1782257860.18744.396686]");
    AssertContains(entries[2].Line, "Kelpie Lawfuller rolled 17,8 using d20+2,d8+2.");
}

static void DieRollSyncAppendsOnlyUnsavedRolls()
{
    using var directory = TemporaryDirectory.Create();
    var filePath = Path.Combine(directory.Path, "Posts", "OOC", "dice-rolls.html");
    const string initialHtml = """
        <div>18:37, Today: Dungeon Master rolled 4 using 1d6.  orcs' init for rnd 2 abandoned rock quarry. – [roll=1782257860.18744.396686]</div>
        <div>18:05, Today: Jelb Garrick rolled 6 using 2d4.  Fire Damage. – [roll=1782255916.99298.396653]</div>
        """;
    const string nextHtml = """
        <div>18:05, Today: Jelb Garrick rolled 6 using 2d4.  Fire Damage. – [roll=1782255916.99298.396653]</div>
        <div>15:41, Today: Kelpie Lawfuller rolled 17,8 using d20+2,d8+2.  Sword attack (held action). – [roll=1782247280.40694.396648]</div>
        """;

    var firstAppendCount = GameForumUtility.AppendNewDieRollEntriesAsync(initialHtml, filePath).GetAwaiter().GetResult();
    var secondAppendCount = GameForumUtility.AppendNewDieRollEntriesAsync(nextHtml, filePath).GetAwaiter().GetResult();
    var savedHtml = File.ReadAllText(filePath);
    var savedEntries = GameForumUtility.ExtractDieRollEntries(savedHtml);

    AssertEqual(2, firstAppendCount, "unexpected initial append count");
    AssertEqual(1, secondAppendCount, "unexpected incremental append count");
    AssertEqual(3, savedEntries.Length, "unexpected saved die roll count");
    AssertEqual("1782257860.18744.396686", savedEntries[0].RollId, "unexpected first saved roll id");
    AssertEqual("1782247280.40694.396648", savedEntries[2].RollId, "unexpected final saved roll id");
}

static void RegionalMapDownloadsWhenMissing()
{
    var filePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "Images", "Maps", "northernreaches.png");

    AssertTrue(GameForumUtility.ShouldDownloadRegionalMap(filePath), "missing regional map should be downloaded");
}

static void RegionalMapDownloadsWhenOlderThanOneHour()
{
    using var directory = TemporaryDirectory.Create();
    var filePath = Path.Combine(directory.Path, "Images", "Maps", "northernreaches.png");
    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    WriteVisiblePng(filePath);
    File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow - TimeSpan.FromMinutes(61));

    AssertTrue(GameForumUtility.ShouldDownloadRegionalMap(filePath), "regional map older than one hour should be downloaded");
}

static void RegionalMapSkipsWhenNewerThanOneHour()
{
    using var directory = TemporaryDirectory.Create();
    var filePath = Path.Combine(directory.Path, "Images", "Maps", "northernreaches.png");
    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    WriteVisiblePng(filePath);
    File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow - TimeSpan.FromMinutes(59));

    AssertFalse(GameForumUtility.ShouldDownloadRegionalMap(filePath), "regional map newer than one hour should not be downloaded");
}

static void RegionalMapDownloadsWhenNewerButTransparent()
{
    using var directory = TemporaryDirectory.Create();
    var filePath = Path.Combine(directory.Path, "Images", "Maps", "northernreaches.png");
    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    WriteTransparentPng(filePath);
    File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow - TimeSpan.FromMinutes(1));

    AssertTrue(GameForumUtility.ShouldDownloadRegionalMap(filePath), "transparent regional map should be downloaded");
}

static void StartupStatusIncludesDownloadCountAndSize()
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

static void AdjustedPostTalliesAggregateSavedIcHtml()
{
    var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    var postsDirectory = Path.Combine(repositoryRoot, "Release", "Posts", "IC");
    var asideDirectory = Path.Combine(postsDirectory, "Aside");

    var outOfCharacterDirectory = Path.Combine(repositoryRoot, "Release", "Posts", "OOC");

    var counts = RpolThreadPostUtility.GetAdjustedPostTalliesFromSavedHtmlDirectories(
        postsDirectory,
        asideDirectory,
        outOfCharacterDirectory);

    AssertEqual(12, counts.Count, "expected adjusted author count");
    AssertEqual(62, counts[RpolThreadPostUtility.DungeonMasterAuthor], "unexpected Dungeon Master count");
    AssertEqual(0, counts.GetValueOrDefault(RpolThreadPostUtility.BillworthTurgenAuthor, 0), "unexpected Billworth count");
    AssertEqual(6, counts["Geoffroy Morin"], "unexpected Geoffroy count");
    AssertEqual(19, counts["Jelb Garrick"], "unexpected Jelb count");
    AssertEqual(28, counts["Kelpie Lawfuller"], "unexpected Kelpie count");
    AssertEqual(10, counts["Maximilian Yragerne"], "unexpected Maximilian count");
    AssertEqual(5, counts[RpolThreadPostUtility.NuandaAuthor], "unexpected Nuanda count");
    AssertEqual(6, counts[RpolThreadPostUtility.NuandaNemereAuthor], "unexpected Nuanda Nemere count");
    AssertEqual(1, counts["temp-name"], "unexpected temp-name count");
    AssertEqual(1, counts["The-Archon"], "unexpected The-Archon count");
    AssertEqual(3, counts[RpolThreadPostUtility.ThurganNewlAuthor], "unexpected Thurgan count");
    AssertEqual(17, counts["Urvan Hall"], "unexpected Urvan count");
}

static void KeywordSearchFallsBackToThePrefixedTerm()
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

static void KeywordSearchKeepsQuotedPhrasesTogether()
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

static void KeywordSearchAcceptsUrlSourceMetadata()
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

static void KeywordSearchFiltersRpolHeroMetadataOnlyHits()
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

static void SearchEnterTriggersClickWhenEnabled()
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
                      "url": "https://example.test/entry",
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
                var btnSearch = GetControl<Button>(form, "btnSearch");
                var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                txtSearch.Text = "entry";
                AssertTrue(btnSearch.Enabled, "expected search button to be enabled for a valid search term");

                InvokePrivateMethod(
                    form,
                    "TxtSearch_KeyDown",
                    txtSearch,
                    new KeyEventArgs(Keys.Enter));

                var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                AssertEqual(1, results.Length, "expected Enter to trigger the existing search click path");
                AssertContains(string.Join("\n", results), "https://example.test/entry");
            });
    });
}

static void KeywordSearchOffersOnlineFallbackOnLocalMiss()
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

static void KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheart()
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

static void KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheartStiffwhiskers()
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

static void HeroImagePathsFollowListingMarkdownTable()
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

static void LocalSettingsAreEncryptedOnLoad()
{
    using var directory = TemporaryDirectory.Create();
    var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
    var plaintext = """
        {
          "RPOL user name": "example-user",
          "RPOL password": "example-password"
        }
        """;
    File.WriteAllText(localSettingsPath, plaintext);

    var settings = (Dictionary<string, string>)InvokeStaticMethod(
        typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.LocalSettingsUtility")
            ?? throw new InvalidOperationException("Unable to find LocalSettingsUtility type."),
        "LoadSettings",
        localSettingsPath)!;

    AssertEqual("example-user", settings["RPOL user name"], "unexpected user name after load");
    AssertEqual("example-password", settings["RPOL password"], "unexpected password after load");
    AssertFalse(File.ReadAllText(localSettingsPath).Contains("example-password", StringComparison.Ordinal), "plaintext password should not remain on disk");
    AssertTrue(
        (bool)InvokeStaticMethod(
            typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.LocalSettingsUtility")
                ?? throw new InvalidOperationException("Unable to find LocalSettingsUtility type."),
            "IsEncryptedSettingsFile",
            localSettingsPath)!,
        "expected the local settings file to be encrypted after load");
}

static void LegacyLocalSettingsMigrateToPortableEncryption()
{
    using var directory = TemporaryDirectory.Create();
    var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
    var plaintext = """
        {
          "RPOL user name": "example-user",
          "RPOL password": "example-password"
        }
        """;
    var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
    var protectedBytes = ProtectedData.Protect(plaintextBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
    var legacyEnvelope = """
        {
          "format": "dpapi-current-user",
          "payload": "__PAYLOAD__"
        }
        """.Replace("__PAYLOAD__", Convert.ToBase64String(protectedBytes), StringComparison.Ordinal);

    File.WriteAllText(localSettingsPath, legacyEnvelope);

    var settings = (Dictionary<string, string>)InvokeStaticMethod(
        typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.LocalSettingsUtility")
            ?? throw new InvalidOperationException("Unable to find LocalSettingsUtility type."),
        "LoadSettings",
        localSettingsPath)!;

    AssertEqual("example-user", settings["RPOL user name"], "unexpected user name after migrating legacy encrypted settings");
    AssertEqual("example-password", settings["RPOL password"], "unexpected password after migrating legacy encrypted settings");
    AssertTrue(
        File.ReadAllText(localSettingsPath).Contains("\"format\": \"app-protected-v1\"", StringComparison.Ordinal),
        "expected legacy encrypted settings to be rewritten using the portable app-protected-v1 format");
}

static void AssertTrue(bool actual, string message)
{
    if (!actual)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertFalse(bool actual, string message)
{
    if (actual)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertContains(string value, string expected)
{
    if (!value.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{value}' to contain '{expected}'.");
    }
}

static void AssertEqual<T>(T expected, T actual, string message) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}. Expected '{expected}' but was '{actual}'.");
    }
}

static void RunOnStaThread(Action action)
{
    Exception? capturedException = null;
    var thread = new Thread(() =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            capturedException = ex;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (capturedException is not null)
    {
        throw capturedException;
    }
}

static T GetControl<T>(Form form, string fieldName) where T : Control
{
    var field = typeof(Form1).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
    if (field?.GetValue(form) is T control)
    {
        return control;
    }

    throw new InvalidOperationException($"Unable to find control field '{fieldName}'.");
}

static Task InvokePrivateAsync(object instance, string methodName, params object[] args)
{
    var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
    if (method is null)
    {
        throw new InvalidOperationException($"Unable to find method '{methodName}'.");
    }

    if (method.Invoke(instance, args) is Task task)
    {
        return task;
    }

    throw new InvalidOperationException($"Method '{methodName}' did not return a Task.");
}

static object? InvokeStaticMethod(Type type, string methodName, params object[] args)
{
    var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    if (method is null)
    {
        throw new InvalidOperationException($"Unable to find static method '{methodName}'.");
    }

    return method.Invoke(null, args);
}

static object? InvokePrivateMethod(object instance, string methodName, params object[] args)
{
    var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
    if (method is null)
    {
        throw new InvalidOperationException($"Unable to find method '{methodName}'.");
    }

    return method.Invoke(instance, args);
}

static void SetPrivateField(object instance, string fieldName, object? value)
{
    var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
    if (field is null)
    {
        throw new InvalidOperationException($"Unable to find field '{fieldName}'.");
    }

    field.SetValue(instance, value);
}

static void WithTemporaryKeywordIndex(string json, Action action)
{
    var indexPath = GetPlayerAssistantIndexPath();
    var backupPath = indexPath + ".test-backup";
    var hadOriginalIndex = File.Exists(indexPath);

    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        if (hadOriginalIndex)
        {
            File.Copy(indexPath, backupPath, overwrite: true);
        }

        File.WriteAllText(indexPath, json);
        action();
    }
    finally
    {
        if (File.Exists(indexPath))
        {
            File.Delete(indexPath);
        }

        if (hadOriginalIndex)
        {
            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException($"Expected backup file '{backupPath}' to exist for restore.", backupPath);
            }

            File.Move(backupPath, indexPath, overwrite: true);
        }
    }
}

static string GetPlayerAssistantIndexPath()
{
    var assemblyDirectory = Path.GetDirectoryName(typeof(Form1).Assembly.Location);
    if (string.IsNullOrWhiteSpace(assemblyDirectory))
    {
        throw new InvalidOperationException("Unable to resolve the player-assistant assembly directory.");
    }

    return Path.Combine(assemblyDirectory, "keyword-index.json");
}

static void WriteVisiblePng(string filePath)
{
    using var bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
    bitmap.SetPixel(0, 0, Color.Black);
    bitmap.Save(filePath, ImageFormat.Png);
}

static void WriteTransparentPng(string filePath)
{
    using var bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
    bitmap.Save(filePath, ImageFormat.Png);
}

internal sealed class TemporaryDirectory : IDisposable
{
    private TemporaryDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TemporaryDirectory Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return new TemporaryDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
