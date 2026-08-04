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

internal static class TranslatorUiTests
{
    internal static void ShowMenuContainsXpItem()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
                ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
            var xpMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "xpToolStripMenuItem")
                ?? throw new InvalidOperationException("xpToolStripMenuItem was null."));

            AssertEqual("XP", xpMenuItem.Text ?? string.Empty, "unexpected XP menu item text");
            AssertTrue(
                showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(xpMenuItem),
                "Show menu should contain the XP item");
        });
    }

    internal static void ShowMenuContainsPartyItem()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
                ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
            var partyMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "partyToolStripMenuItem")
                ?? throw new InvalidOperationException("partyToolStripMenuItem was null."));

            AssertEqual("Party", partyMenuItem.Text ?? string.Empty, "unexpected Party menu item text");
            AssertTrue(
                showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(partyMenuItem),
                "Show menu should contain the Party item");
        });
    }

    internal static void ShowMenuContainsFormerPcsItem()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
                ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
            var partyMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "partyToolStripMenuItem")
                ?? throw new InvalidOperationException("partyToolStripMenuItem was null."));
            var formerPcsMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "formerPcsToolStripMenuItem")
                ?? throw new InvalidOperationException("formerPcsToolStripMenuItem was null."));

            AssertEqual("Former PCs", formerPcsMenuItem.Text ?? string.Empty, "unexpected Former PCs menu item text");
            AssertTrue(
                showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(formerPcsMenuItem),
                "Show menu should contain the Former PCs item");
            AssertEqual(
                showMenuItem.DropDownItems.IndexOf(partyMenuItem) + 1,
                showMenuItem.DropDownItems.IndexOf(formerPcsMenuItem),
                "Former PCs should appear immediately after Party");
        });
    }

    internal static void FormerPcsViewDisplaysTokenNameAndClass()
    {
        using var directory = TemporaryDirectory.Create();
        var tokenPath = Path.Combine(directory.Path, "urvan-token.png");
        using (var token = new Bitmap(8, 8))
        {
            token.SetPixel(0, 0, Color.DarkRed);
            token.Save(tokenPath, ImageFormat.Png);
        }

        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            InvokePrivateMethod(
                form,
                "ShowFormerPcs",
                (object)new[] { new FormerPcSummary("Urvan", "Paladin of St. Ygg", tokenPath) });

            var panel = (Panel)(GetPrivateField(form, "_partyPanel")
                ?? throw new InvalidOperationException("Former PCs panel was null."));
            var controls = panel.Controls
                .Cast<Control>()
                .SelectMany(control => control.Controls.Cast<Control>().Prepend(control))
                .ToArray();

            AssertTrue(controls.OfType<Label>().Any(label => label.Text == "Urvan"), "former PC name should be displayed");
            AssertTrue(controls.OfType<Label>().Any(label => label.Text == "Paladin of St. Ygg"), "former PC class should be displayed");
            AssertTrue(controls.OfType<PictureBox>().Any(pictureBox => pictureBox.Image is not null), "former PC token image should be displayed");
        });
    }

    internal static void ShowMenuContainsMyHeroBriefingItem()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
                ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
            var myHeroBriefingMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "myHeroBriefingToolStripMenuItem")
                ?? throw new InvalidOperationException("myHeroBriefingToolStripMenuItem was null."));

            AssertEqual("My Hero Briefing", myHeroBriefingMenuItem.Text ?? string.Empty, "unexpected My Hero Briefing menu item text");
            AssertTrue(
                showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(myHeroBriefingMenuItem),
                "Show menu should contain the My Hero Briefing item");
            AssertTrue(
                showMenuItem.DropDownItems.IndexOf(myHeroBriefingMenuItem) > showMenuItem.DropDownItems.IndexOf((ToolStripItem)(GetPrivateField(form, "partyToolStripMenuItem")
                    ?? throw new InvalidOperationException("partyToolStripMenuItem was null."))),
                "My Hero Briefing should appear after Party");
        });
    }

    internal static void ShowMenuContainsAdventureOutlineItem()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
                ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
            var adventureOutlineMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "adventureOutlineToolStripMenuItem")
                ?? throw new InvalidOperationException("adventureOutlineToolStripMenuItem was null."));

            AssertEqual("Adventure Outline", adventureOutlineMenuItem.Text ?? string.Empty, "unexpected Adventure Outline menu item text");
            AssertTrue(
                showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(adventureOutlineMenuItem),
                "Show menu should contain the Adventure Outline item");
        });
    }

    internal static void ShowMenuContainsTranslatorItem()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
                ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
            var translatorMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "translatorToolStripMenuItem")
                ?? throw new InvalidOperationException("translatorToolStripMenuItem was null."));
            var orcishMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "orcishTranslatorToolStripMenuItem")
                ?? throw new InvalidOperationException("orcishTranslatorToolStripMenuItem was null."));
            var elvenMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "elvenTranslatorToolStripMenuItem")
                ?? throw new InvalidOperationException("elvenTranslatorToolStripMenuItem was null."));

            AssertEqual("Translate", translatorMenuItem.Text ?? string.Empty, "unexpected Translate menu item text");
            AssertTrue(
                showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(translatorMenuItem),
                "Show menu should contain the Translate item");
            AssertEqual("Orcish", orcishMenuItem.Text ?? string.Empty, "unexpected Orcish translator menu text");
            AssertEqual("Elven", elvenMenuItem.Text ?? string.Empty, "unexpected Elven translator menu text");
            AssertTrue(translatorMenuItem.DropDownItems.Contains(orcishMenuItem), "Translate should contain Orcish");
            AssertTrue(translatorMenuItem.DropDownItems.Contains(elvenMenuItem), "Translate should contain Elven");
        });
    }

    internal static void ElvenTranslatorPrefersSindarinAndFallsBackToQuenya()
    {
        var friend = ElvenTranslatorUtility.TranslateEnglishToElven("friend");
        AssertTrue(friend.Count > 0, "friend should have an Elven translation");
        AssertEqual("mellon", friend[0].Translation, "friend should prefer the standard Sindarin form");
        AssertTrue(friend.All(candidate => candidate.Language == "Sindarin"), "friend should not expose Quenya when Sindarin exists");

        var abandon = ElvenTranslatorUtility.TranslateEnglishToElven("abandon");
        AssertTrue(abandon.Count > 0, "abandon should have a Quenya fallback");
        AssertEqual("Quenya", abandon[0].Language, "abandon should use Quenya only because Sindarin is unavailable");
        AssertEqual("hehta", abandon[0].Translation, "unexpected Quenya fallback for abandon");
        AssertTrue(ElvenTranslatorUtility.GetEnglishTermCount() > 9000, "embedded Elven lexicon should expose the generated vocabulary");
    }

    internal static void ElvenTranslatorPreservesTextAndPunctuation()
    {
        AssertEqual(
            "Mellon hehta untranslatedword.",
            ElvenTranslatorUtility.TranslateEnglishTextToElven("friend abandon untranslatedword."),
            "Elven text translation should translate known words and preserve unknown words");
        AssertEqual(
            "Friend.",
            ElvenTranslatorUtility.TranslateElvenTextToEnglish("mellon."),
            "Elven reverse translation should preserve punctuation");
    }

    internal static void GhukliakTranslatorLoadsSourceAndCompleteCoverage()
    {
        AssertEqual(81204, GhukliakTranslatorUtility.GetEnglishTermCount(), "unexpected complete Ghukliak English term count");
        AssertEqual("bikhouihg", GhukliakTranslatorUtility.TranslateEnglishToGhukliak("language")[0].Translation, "unexpected language translation");
        AssertTrue(
            GhukliakTranslatorUtility.TranslateGhukliakToEnglish("bikhouihg")
                .Any(candidate => candidate.English == "language"),
            "Ghukliak reverse lookup should include language");
    }

    internal static void GhukliakTranslatorPreservesTextAndPunctuation()
    {
        AssertEqual(
            "Bikhouihg unknownword.",
            GhukliakTranslatorUtility.TranslateEnglishTextToGhukliak("language unknownword."),
            "Ghukliak text translation should preserve unknown words");
        AssertEqual(
            "Tongue.",
            GhukliakTranslatorUtility.TranslateGhukliakTextToEnglish("bikhouihg."),
            "Ghukliak reverse translation should preserve punctuation");
        AssertTrue(
            GhukliakTranslatorUtility.TranslateEnglishTextToGhukliak("a single, gold coin.").Contains(','),
            "Ghukliak longest-phrase matching should not consume punctuation between source words");
    }

    internal static void GhukliakCompleteCoverageTranslatesEveryOrcishTerm()
    {
        AssertEqual(81204, GhukliakTranslatorUtility.GetEnglishTermCount(), "unexpected complete Ghukliak English term count");

        var missing = OrcishTranslatorUtility.GetEnglishTerms()
            .Where(term => GhukliakTranslatorUtility.TranslateEnglishToGhukliak(term).Count == 0)
            .Take(10)
            .ToArray();
        AssertEqual(0, missing.Length, $"Orcish English terms remain untranslated: {string.Join(", ", missing)}");

        var abacus = GhukliakTranslatorUtility.TranslateEnglishToGhukliak("abacus").Single();
        AssertFalse(
            string.Equals(abacus.English, abacus.Translation, StringComparison.OrdinalIgnoreCase),
            "generated Ghukliak forms should not pass English through unchanged");
        AssertTrue(
            GhukliakTranslatorUtility.TranslateGhukliakToEnglish(abacus.Translation)
                .Any(entry => entry.English == "abacus"),
            "complete-coverage forms should remain available to reverse translation");
        AssertTrue(
            GhukliakTranslatorUtility.TranslateEnglishToGhukliak("a single gold coin").Count == 1,
            "complete coverage should include remaining multiword English terms");
    }

    internal static void TranslatorViewSupportsGhukliakMode()
    {
        Form1.TranslatorTextOverrideForTests = static (_, _) => string.Empty;
        try
        {
            RunOnStaThread(() =>
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                var menuItem = (ToolStripMenuItem)(GetPrivateField(form, "ghukliakTranslatorToolStripMenuItem")
                    ?? throw new InvalidOperationException("ghukliakTranslatorToolStripMenuItem was null."));
                menuItem.PerformClick();

                var heading = (Label)(GetPrivateField(form, "_translatorHeadingLabel")
                    ?? throw new InvalidOperationException("_translatorHeadingLabel was null."));
                var direction = (CheckBox)(GetPrivateField(form, "_translatorDirectionCheckBox")
                    ?? throw new InvalidOperationException("_translatorDirectionCheckBox was null."));
                AssertEqual("English to Goblin (Ghukliak)", heading.Text, "Ghukliak menu should open English-to-Goblin mode");
                AssertEqual("Goblin (Ghukliak) to English", direction.Text, "Ghukliak direction toggle should identify its source language");
            });
        }
        finally
        {
            Form1.TranslatorTextOverrideForTests = null;
        }
    }

    internal static void TranslatorControllerUsesInjectedBackend()
    {
        var backend = new TestTranslatorBackend();
        var controller = new TranslatorController(backend);

        controller.SelectTarget(TranslatorTargetLanguage.Elven);

        AssertEqual(TranslatorTargetLanguage.Elven, controller.TargetLanguage, "controller should retain the selected target language");
        AssertEqual("Elven", controller.TargetName, "controller should expose the selected target name");
        AssertEqual("elvish", controller.ExportLanguageToken, "controller should expose the selected export token");
        AssertTrue(controller.IsReady, "controller should obtain readiness from the injected backend");
        AssertEqual(
            "translated:Elven:True:hello",
            controller.Translate("hello", targetToEnglish: true),
            "controller should route translation through the injected backend");
        AssertEqual(
            37,
            controller.StartPreloadingAsync().GetAwaiter().GetResult(),
            "controller should route preload requests through the injected backend");
        AssertEqual(
            42,
            controller.WaitUntilReadyAsync(CancellationToken.None).GetAwaiter().GetResult().EnglishTermCount,
            "controller should route readiness waits through the injected backend");
    }

    internal static void TranslatorViewDelegatesSelectionToInjectedController()
    {
        RunOnStaThread(() =>
        {
            var controller = new TranslatorController(new TestTranslatorBackend());
            using var form = new Form1(suppressHeroImagesForThisRun: true, controller);
            var menuItem = (ToolStripMenuItem)(GetPrivateField(form, "ghukliakTranslatorToolStripMenuItem")
                ?? throw new InvalidOperationException("ghukliakTranslatorToolStripMenuItem was null."));

            menuItem.PerformClick();

            AssertEqual(
                TranslatorTargetLanguage.Ghukliak,
                controller.TargetLanguage,
                "Form1 should delegate translator selection to its injected controller");
        });
    }

    internal static void ElvenTranslatorFinalizesEveryEnglishTerm()
    {
        var terms = ElvenTranslatorUtility.GetEnglishTerms();
        var entries = ElvenTranslatorUtility.GetLexiconEntries();
        AssertEqual(84460, terms.Count, "unexpected finalized English-to-Elven term count");
        AssertEqual(terms.Count, entries.Count, "every English term should have exactly one finalized translation");
        AssertTrue(
            terms.All(term => ElvenTranslatorUtility.TranslateEnglishToElven(term).Count == 1),
            "every English term should resolve to exactly one selected candidate");
        AssertTrue(
            entries.All(entry => !entry.Translation.Contains('(') &&
                                 !entry.Translation.Contains(')') &&
                                 !entry.Translation.Contains('/')),
            "finalized translations should not expose optional-form notation");
        AssertEqual(
            "emecima",
            ElvenTranslatorUtility.TranslateEnglishToElven("accurate")[0].Translation,
            "parenthetical letters should be expanded into a usable Quenya form");
        AssertEqual(
            "an quetta",
            ElvenTranslatorUtility.TranslateEnglishToElven("postscriptum")[0].Translation,
            "attested abbreviations should expand to their full Elvish phrase");
    }

    internal static void ElvenMorphologyDerivesConservativeForms()
    {
        AssertDerivedElvenForm("Sindarin", "adan", "plural", "edain");
        AssertDerivedElvenForm("Sindarin", "orch", "plural", "yrch");
        AssertDerivedElvenForm("Sindarin", "car", "present-active", "câr");
        AssertDerivedElvenForm("Sindarin", "gala", "active-participle", "galol");
        AssertDerivedElvenForm("Quenya", "atan", "plural", "atani");
        AssertDerivedElvenForm("Quenya", "lassë", "plural", "lassi");
        AssertDerivedElvenForm("Quenya", "mat", "present-active", "matë");
        AssertDerivedElvenForm("Quenya", "laita", "active-participle", "laitaila");
        AssertDerivedElvenForm("Sindarin", "gala", "gerund", "galad");
        AssertDerivedElvenForm("Quenya", "mat", "gerund", "matie");
        AssertDerivedElvenForm("Sindarin", "gala", "passive-participle", "galannen");
        AssertDerivedElvenForm("Quenya", "laita", "passive-participle", "laitaina");
        AssertDerivedElvenForm("Sindarin", "mellon", "possessive", "mellon");
        AssertDerivedElvenForm("Quenya", "atan", "possessive", "atanwa");
        AssertDerivedElvenForm("Sindarin", "tanc", "comparative", "athanc");
        AssertDerivedElvenForm("Sindarin", "tanc", "superlative", "rodanc");
        AssertDerivedElvenForm("Quenya", "calima", "comparative", "ancalima");
        AssertDerivedElvenForm("Quenya", "calima", "superlative", "aricalima");

        var mismatch = ElvenTranslatorUtility.ReviewProposedLexiconEntry(
            new ElvenLexiconEntry(
                "local invalid agent plural",
                "caroni",
                "Sindarin",
                PartOfSpeech: "noun",
                RootForms: ["caron"],
                Tags: ["derived-by-rule", "plural"]));
        AssertTrue(
            mismatch.Any(issue => issue.Code == "root-morphology-mismatch"),
            "a morphology-derived entry should be rejected when it does not match the declared root rule");
    }

    internal static void ElvenFirstIterationLoadsGeneratedTranslations()
    {
        AssertEqual("fuia", ElvenTranslatorUtility.TranslateEnglishToElven("abhors")[0].Translation, "unexpected translation for abhors");
        AssertEqual("itanqualër", ElvenTranslatorUtility.TranslateEnglishToElven("aconites")[0].Translation, "unexpected translation for aconites");
        AssertEqual("ceryn", ElvenTranslatorUtility.TranslateEnglishToElven("agents")[0].Translation, "unexpected translation for agents");
        AssertEqual("antacila", ElvenTranslatorUtility.TranslateEnglishToElven("applying")[0].Translation, "unexpected translation for applying");
        AssertEqual("pannol", ElvenTranslatorUtility.TranslateEnglishToElven("arranging")[0].Translation, "unexpected translation for arranging");
        AssertTrue(
            ElvenTranslatorUtility.GetLexiconEntries()
                .Where(entry => entry.SourceLanguage?.StartsWith("local-morphology", StringComparison.Ordinal) == true)
                .All(entry => !string.IsNullOrWhiteSpace(entry.Gloss)),
            "every first-iteration entry should retain its derivation note");
    }

    internal static void ElvenSecondIterationLoadsGeneratedTranslations()
    {
        AssertEqual("awarth", ElvenTranslatorUtility.TranslateEnglishToElven("abandonment's")[0].Translation, "unexpected translation for abandonment's");
        AssertEqual("cuiwed", ElvenTranslatorUtility.TranslateEnglishToElven("alerting")[0].Translation, "unexpected translation for alerting");
        AssertEqual("ovrannen", ElvenTranslatorUtility.TranslateEnglishToElven("abounded")[0].Translation, "unexpected translation for abounded");
        AssertEqual("húnalë", ElvenTranslatorUtility.TranslateEnglishToElven("accursedness")[0].Translation, "unexpected translation for accursedness");
        AssertEqual("trenarnui", ElvenTranslatorUtility.TranslateEnglishToElven("accountable")[0].Translation, "unexpected translation for accountable");
        AssertEqual(
            5000,
            ElvenTranslatorUtility.GetLexiconEntries().Count(entry =>
                entry.SourceLanguage == "local-morphology:second-iteration"),
            "the second iteration should contribute exactly 5,000 entries");
    }

    internal static void ElvenCompleteCoverageTranslatesEveryOrcishTerm()
    {
        var coverageEntries = ElvenTranslatorUtility.GetLexiconEntries()
            .Where(entry => entry.SourceLanguage == "local-neologism:complete-coverage")
            .ToArray();
        AssertEqual(69012, coverageEntries.Length, "complete coverage should add every remaining Orcish English term");
        AssertTrue(coverageEntries.All(entry => entry.Language == "Sindarin"), "invented fallback vocabulary should remain Sindarin-first");
        AssertTrue(coverageEntries.All(entry => entry.ReliabilityMark == "!"), "invented fallback vocabulary should be marked as pure neologism");

        var missing = OrcishTranslatorUtility.GetEnglishTerms()
            .Where(term => ElvenTranslatorUtility.TranslateEnglishToElven(term).Count == 0)
            .Take(10)
            .ToArray();
        AssertEqual(0, missing.Length, $"Orcish English terms remain untranslated: {string.Join(", ", missing)}");
        AssertTrue(
            ElvenTranslatorUtility.TranslateEnglishToElven("films'").Count == 1,
            "complete coverage should include the plural possessive film form");

        var abacus = ElvenTranslatorUtility.TranslateEnglishToElven("abacus").Single();
        AssertEqual("Sindarin", abacus.Language, "abacus should use the generated Sindarin fallback");
        AssertTrue(
            ElvenTranslatorUtility.TranslateElvenToEnglish(abacus.Translation).Any(entry => entry.English == "abacus"),
            "complete-coverage forms should remain available to reverse translation");
        AssertTrue(
            ElvenTranslatorUtility.TranslateEnglishToElven("a single gold coin").Count == 1,
            "complete coverage should include remaining multiword English terms");
    }

    internal static void ElvenLexiconValidatorAcceptsReviewedRootedAdditions()
    {
        var rooted = new ElvenLexiconEntry(
            "local fellowship test",
            "mellonath",
            "Sindarin",
            PartOfSpeech: "noun",
            RootForms: ["mellon"],
            Tags: ["phonotactics-reviewed", "close-form-reviewed"]);
        AssertEqual(
            0,
            ElvenTranslatorUtility.ReviewProposedLexiconEntry(rooted).Count,
            "a same-language rooted Sindarin addition should pass after exceptional sound patterns are reviewed");
        ElvenTranslatorUtility.EnsureProposedLexiconEntryCanBeAdded(rooted);

        var reviewedNewRoot = new ElvenLexiconEntry(
            "local Quenya test root",
            "závora",
            "Quenya",
            Tags: ["root-invention-reviewed", "phonotactics-reviewed", "close-form-reviewed"]);
        ElvenTranslatorUtility.EnsureProposedLexiconEntryCanBeAdded(reviewedNewRoot);
    }

    internal static void ElvenLexiconValidatorRejectsUnsupportedAdditions()
    {
        var missingProvenance = ElvenTranslatorUtility.ReviewProposedLexiconEntry(
            new ElvenLexiconEntry("local unsupported test", "mellonath", "Sindarin"));
        AssertTrue(
            missingProvenance.Any(issue => issue.Code == "root-provenance-required"),
            "local additions should declare established roots or explicit invented-root review");

        var crossLanguage = ElvenTranslatorUtility.ReviewProposedLexiconEntry(
            new ElvenLexiconEntry(
                "local cross-language test",
                "mellonion",
                "Quenya",
                RootForms: ["mellon"],
                Tags: ["phonotactics-reviewed", "close-form-reviewed"]));
        AssertTrue(
            crossLanguage.Any(issue => issue.Code == "cross-language-root"),
            "Quenya additions should not silently derive from a Sindarin root");

        var changedRoot = ElvenTranslatorUtility.ReviewProposedLexiconEntry(
            new ElvenLexiconEntry(
                "local changed-root test",
                "calad",
                "Sindarin",
                RootForms: ["mellon"],
                Tags: ["collision-reviewed", "phonotactics-reviewed", "close-form-reviewed"]));
        AssertTrue(
            changedRoot.Any(issue => issue.Code == "root-form-mismatch"),
            "unexplained root replacement should be rejected");

        var malformed = ElvenTranslatorUtility.ReviewProposedLexiconEntry(
            new ElvenLexiconEntry(
                "local malformed test",
                "mel@lon",
                "Sindarin",
                Tags: ["root-invention-reviewed", "phonotactics-reviewed", "close-form-reviewed"]));
        AssertTrue(
            malformed.Any(issue => issue.Code == "invalid-elvish-character"),
            "non-Elvish punctuation should be rejected");
    }

    internal static void ElvenLexiconValidatorPreservesSindarinPreference()
    {
        var quenyaFriend = ElvenTranslatorUtility.ReviewProposedLexiconEntry(
            new ElvenLexiconEntry(
                "friend",
                "málo",
                "Quenya",
                Tags: ["root-invention-reviewed", "phonotactics-reviewed", "close-form-reviewed", "collision-reviewed"]));
        AssertTrue(
            quenyaFriend.Any(issue => issue.Code == "quenya-shadowed-by-sindarin"),
            "Quenya should not be added when Sindarin already covers the English term");

        var closeForm = ElvenTranslatorUtility.ReviewProposedLexiconEntry(
            new ElvenLexiconEntry(
                "local close-form test",
                "mellom",
                "Sindarin",
                Tags: ["root-invention-reviewed", "phonotactics-reviewed"]));
        AssertTrue(
            closeForm.Any(issue => issue.Code == "close-form-conflict"),
            "near-colliding Elven forms should require explicit review");
    }

    internal static void TranslatorViewSupportsElvenMode()
    {
        Form1.TranslatorTextOverrideForTests = static (_, _) => string.Empty;
        try
        {
            RunOnStaThread(() =>
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                var elvenMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "elvenTranslatorToolStripMenuItem")
                    ?? throw new InvalidOperationException("elvenTranslatorToolStripMenuItem was null."));
                elvenMenuItem.PerformClick();

                var heading = (Label)(GetPrivateField(form, "_translatorHeadingLabel")
                    ?? throw new InvalidOperationException("_translatorHeadingLabel was null."));
                var direction = (CheckBox)(GetPrivateField(form, "_translatorDirectionCheckBox")
                    ?? throw new InvalidOperationException("_translatorDirectionCheckBox was null."));
                var output = (TextBox)(GetPrivateField(form, "_translatorOutputTextBox")
                    ?? throw new InvalidOperationException("_translatorOutputTextBox was null."));
                var exportButton = (Button)(GetPrivateField(form, "_translatorExportButton")
                    ?? throw new InvalidOperationException("_translatorExportButton was null."));

                AssertEqual("English to Elven", heading.Text, "Elven menu should open English-to-Elven mode");
                AssertEqual("Elven to English", direction.Text, "Elven direction toggle should identify its source language");
                output.Text = "mellon";
                AssertTrue(exportButton.Enabled, "export should be available for a non-empty English-to-Elven translation");

                direction.Checked = true;
                AssertEqual("Elven to English", heading.Text, "Elven reverse mode should update the heading");
                AssertFalse(exportButton.Enabled, "export should be unavailable in Elven-to-English mode");
            });
        }
        finally
        {
            Form1.TranslatorTextOverrideForTests = null;
        }
    }

    internal static void TranslatorViewTogglesDirectionWithoutWebLinks()
    {
        Form1.TranslatorTextOverrideForTests = static (_, _) => string.Empty;
        try
        {
            RunOnStaThread(() =>
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                InvokePrivateMethod(form, "ShowTranslatorPanel");

                var panel = (Panel)(GetPrivateField(form, "_translatorPanel")
                    ?? throw new InvalidOperationException("_translatorPanel was null."));
                var heading = (Label)(GetPrivateField(form, "_translatorHeadingLabel")
                    ?? throw new InvalidOperationException("_translatorHeadingLabel was null."));
                var direction = (CheckBox)(GetPrivateField(form, "_translatorDirectionCheckBox")
                    ?? throw new InvalidOperationException("_translatorDirectionCheckBox was null."));
                var inputLabel = (Label)(GetPrivateField(form, "_translatorInputLabel")
                    ?? throw new InvalidOperationException("_translatorInputLabel was null."));
                var input = (TextBox)(GetPrivateField(form, "_translatorInputTextBox")
                    ?? throw new InvalidOperationException("_translatorInputTextBox was null."));
                var output = (TextBox)(GetPrivateField(form, "_translatorOutputTextBox")
                    ?? throw new InvalidOperationException("_translatorOutputTextBox was null."));
                var exportButton = (Button)(GetPrivateField(form, "_translatorExportButton")
                    ?? throw new InvalidOperationException("_translatorExportButton was null."));

                AssertFalse(direction.Checked, "translator should default to English-to-Orcish mode");
                AssertEqual("English to Orcish", heading.Text, "unexpected default translator heading");
                AssertEqual("English text", inputLabel.Text, "unexpected default translator input label");
                AssertEqual(0, panel.Controls.OfType<LinkLabel>().Count(), "native translator should not expose web hyperlinks");
                AssertEqual("Export Translation", exportButton.Text, "unexpected translator export button text");
                AssertFalse(exportButton.Enabled, "export should be unavailable until an English-to-Orcish translation exists");

                input.Text = "x";
                output.Text = "stale translation";

                direction.Checked = true;

                AssertEqual("Orcish to English", heading.Text, "unexpected reverse translator heading");
                AssertEqual("Orcish text", inputLabel.Text, "unexpected reverse translator input label");
                AssertEqual(string.Empty, input.Text, "direction changes should clear translator input");
                AssertEqual(string.Empty, output.Text, "direction changes should clear translator output");
                AssertFalse(exportButton.Enabled, "export should remain unavailable in Orcish-to-English mode");
                AssertTrue(ReferenceEquals(form.ActiveControl, input), "direction changes should return focus to translator input");
            });
        }
        finally
        {
            Form1.TranslatorTextOverrideForTests = null;
        }
    }

    internal static void TranslatorViewExportsEnglishToOrcishTranslation()
    {
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"player-assistant-translator-{Guid.NewGuid():N}");
        var exportPath = Path.Combine(exportDirectory, "my-orcish-translation.txt");
        Directory.CreateDirectory(exportDirectory);
        Form1.TranslatorTextOverrideForTests = static (_, _) => string.Empty;
        Form1.TranslatorExportPathOverrideForTests = () => exportPath;
        try
        {
            RunOnStaThread(() =>
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                InvokePrivateMethod(form, "ShowTranslatorPanel");

                var direction = (CheckBox)(GetPrivateField(form, "_translatorDirectionCheckBox")
                    ?? throw new InvalidOperationException("_translatorDirectionCheckBox was null."));
                var input = (TextBox)(GetPrivateField(form, "_translatorInputTextBox")
                    ?? throw new InvalidOperationException("_translatorInputTextBox was null."));
                var output = (TextBox)(GetPrivateField(form, "_translatorOutputTextBox")
                    ?? throw new InvalidOperationException("_translatorOutputTextBox was null."));
                var exportButton = (Button)(GetPrivateField(form, "_translatorExportButton")
                    ?? throw new InvalidOperationException("_translatorExportButton was null."));

                input.Text = "Café";
                output.Text = "Grûk";
                AssertTrue(exportButton.Enabled, "export should become available for a non-empty English-to-Orcish translation");
                AssertEqual(
                    "english-5-bytes-to-orcish-5-bytes",
                    Form1.BuildTranslatorExportDefaultFileName(input.Text, output.Text),
                    "export filename should include the current English and Orcish UTF-8 byte counts");

                InvokePrivateMethod(form, "TranslatorExportButton_Click", exportButton, EventArgs.Empty);
                AssertEqual("Grûk", File.ReadAllText(exportPath), "exported translation content should match the output textbox");

                direction.Checked = true;
                output.Text = "Hello";
                AssertFalse(exportButton.Enabled, "export should be unavailable for Orcish-to-English output");
            });
        }
        finally
        {
            Form1.TranslatorTextOverrideForTests = null;
            Form1.TranslatorExportPathOverrideForTests = null;
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }

    internal static void TranslatorViewExportsEnglishToElvishTranslation()
    {
        var exportDirectory = Path.Combine(Path.GetTempPath(), $"player-assistant-elvish-translator-{Guid.NewGuid():N}");
        var exportPath = Path.Combine(exportDirectory, "my-elvish-translation.txt");
        Directory.CreateDirectory(exportDirectory);
        Form1.TranslatorTextOverrideForTests = static (_, _) => string.Empty;
        Form1.TranslatorExportPathOverrideForTests = () => exportPath;
        try
        {
            RunOnStaThread(() =>
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                var elvenMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "elvenTranslatorToolStripMenuItem")
                    ?? throw new InvalidOperationException("elvenTranslatorToolStripMenuItem was null."));
                elvenMenuItem.PerformClick();

                var direction = (CheckBox)(GetPrivateField(form, "_translatorDirectionCheckBox")
                    ?? throw new InvalidOperationException("_translatorDirectionCheckBox was null."));
                var input = (TextBox)(GetPrivateField(form, "_translatorInputTextBox")
                    ?? throw new InvalidOperationException("_translatorInputTextBox was null."));
                var output = (TextBox)(GetPrivateField(form, "_translatorOutputTextBox")
                    ?? throw new InvalidOperationException("_translatorOutputTextBox was null."));
                var exportButton = (Button)(GetPrivateField(form, "_translatorExportButton")
                    ?? throw new InvalidOperationException("_translatorExportButton was null."));

                input.Text = "Café";
                output.Text = "Mellon";
                AssertTrue(exportButton.Enabled, "export should become available for a non-empty English-to-Elven translation");
                AssertEqual(
                    "english-5-bytes-to-elvish-6-bytes",
                    Form1.BuildTranslatorExportDefaultFileName(input.Text, output.Text, "elvish"),
                    "Elvish export filename should include the current UTF-8 byte counts");

                InvokePrivateMethod(form, "TranslatorExportButton_Click", exportButton, EventArgs.Empty);
                AssertEqual("Mellon", File.ReadAllText(exportPath), "exported Elvish content should match the output textbox");

                direction.Checked = true;
                output.Text = "Friend";
                AssertFalse(exportButton.Enabled, "export should be unavailable for Elven-to-English output");
            });
        }
        finally
        {
            Form1.TranslatorTextOverrideForTests = null;
            Form1.TranslatorExportPathOverrideForTests = null;
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }

    internal static void TranslatorViewTranslatesWhileInputChanges()
    {
        RunOnStaThread(() =>
        {
            using var synchronizationContext = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            using var translationStarted = new ManualResetEventSlim();
            using var releaseTranslation = new ManualResetEventSlim();
            using var firstTranslationReturned = new ManualResetEventSlim();
            Form1.TranslatorTextOverrideForTests = (input, orcishToEnglish) =>
            {
                if (input == "hello")
                {
                    translationStarted.Set();
                    if (!releaseTranslation.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("test translation was not released");
                    }

                    firstTranslationReturned.Set();
                }

                return orcishToEnglish ? "Hello" : input == "hello" ? "Zug" : "Durb";
            };

            try
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                InvokePrivateMethod(form, "ShowTranslatorPanel");

                var input = (TextBox)(GetPrivateField(form, "_translatorInputTextBox")
                    ?? throw new InvalidOperationException("_translatorInputTextBox was null."));
                var output = (TextBox)(GetPrivateField(form, "_translatorOutputTextBox")
                    ?? throw new InvalidOperationException("_translatorOutputTextBox was null."));

                input.Text = "hello";
                WaitForWindowsFormsCondition(
                    () => translationStarted.IsSet,
                    "pasted translator input should begin translating promptly");
                WaitForWindowsFormsCondition(
                    () => form.UseWaitCursor,
                    "translator should show the wait cursor when translation takes noticeable time");

                input.Text = "goodbye";
                WaitForWindowsFormsCondition(
                    () => output.Text == "Durb",
                    "translator output should update automatically when input changes");
                AssertFalse(form.UseWaitCursor, "translator should restore the normal cursor after translation");

                releaseTranslation.Set();
                WaitForWindowsFormsCondition(
                    () => firstTranslationReturned.IsSet,
                    "canceled translator work should finish");
                Application.DoEvents();
                AssertEqual("Durb", output.Text, "stale translator work should not replace current output");
            }
            finally
            {
                releaseTranslation.Set();
                Form1.TranslatorTextOverrideForTests = null;
                SynchronizationContext.SetSynchronizationContext(null);
            }
        });
    }

    internal static void AdventureOutlineViewDisplaysGeneratedMarkdown()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            const string outline = """
            ---
            title: Adventure Outline
            aliases:
              - Scarlet Horizons Adventure Outline
            ---

            # Adventure Outline

            - Source files inspected:
              - `C:/repos/player-assistant/Release/Posts/IC/ch-1.html`

            ## Chapter 7 - The Gate Opens

            - Kelpie: Found the hidden key.

            -

            ## Ch 4 - Battle at Blightstone Pit

            - The party fights at the quarry.

            ## Ch 5 - A Betentacled Escape

            - The party escapes the pit.

            ## Ch 2 - Supper With Nuanda

            - Jelb weighs the party's options.
            - Dungeon Master frames the next choice.

            ## Ch 3 - Joining the Caravan to Raven's Pass

            - The party joins the caravan.
            """;

            InvokePrivateMethod(form, "ShowAdventureOutline", outline);

            var textBox = (RichTextBox)(GetPrivateField(form, "_adventureOutlineTextBox")
                ?? throw new InvalidOperationException("_adventureOutlineTextBox was null."));
            var adventureOutlineMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "adventureOutlineToolStripMenuItem")
                ?? throw new InvalidOperationException("adventureOutlineToolStripMenuItem was null."));

            AssertTrue(form.Controls.Contains(textBox), "adventure outline text box should be attached to the form");
            AssertTrue(textBox.ReadOnly, "adventure outline text box should be read-only");
            AssertContains(textBox.Text, "Chapter 7 - The Gate Opens");
            AssertContains(textBox.Text, "Kelpie: Found the hidden key.");
            AssertContains(textBox.Text, "Ch 2 - Supper With Nuanda");
            AssertContains(textBox.Text, "Jelb weighs the party's options.");
            AssertContains(textBox.Text, "Dungeon Master frames the next choice.");
            AssertTrue(
                textBox.Text.IndexOf("Chapter 7 - The Gate Opens", StringComparison.Ordinal)
                    < textBox.Text.IndexOf("Ch 4 - Battle at Blightstone Pit", StringComparison.Ordinal),
                "adventure outline display should keep chapter order from the markdown");
            AssertTrue(
                textBox.Text.IndexOf("Ch 4 - Battle at Blightstone Pit", StringComparison.Ordinal)
                    < textBox.Text.IndexOf("Ch 5 - A Betentacled Escape", StringComparison.Ordinal),
                "chapter 4 should display before chapter 5");
            AssertTrue(
                textBox.Text.IndexOf("Ch 5 - A Betentacled Escape", StringComparison.Ordinal)
                    < textBox.Text.IndexOf("Ch 2 - Supper With Nuanda", StringComparison.Ordinal),
                "chapter 5 should display before the later markdown chapter 2 entry in this fixture");
            AssertTrue(
                textBox.Text.IndexOf("Ch 2 - Supper With Nuanda", StringComparison.Ordinal)
                    < textBox.Text.IndexOf("Ch 3 - Joining the Caravan to Raven's Pass", StringComparison.Ordinal),
                "chapter 2 should display before chapter 3");
            AssertFalse(textBox.Text.Contains("title: Adventure Outline", StringComparison.Ordinal), "player-facing outline should hide YAML frontmatter");
            AssertFalse(textBox.Text.Contains("aliases:", StringComparison.Ordinal), "player-facing outline should hide YAML frontmatter keys");
            AssertFalse(textBox.Text.Contains("Scarlet Horizons Adventure Outline", StringComparison.Ordinal), "player-facing outline should hide YAML frontmatter values");
            AssertFalse(textBox.Lines.Any(line => line.Trim().Equals("-", StringComparison.Ordinal)), "player-facing outline should hide empty bullet marker lines");
            for (var lineIndex = 0; lineIndex < textBox.Lines.Length; lineIndex++)
            {
                if (textBox.Lines[lineIndex].Length == 0)
                {
                    var lineStart = textBox.GetFirstCharIndexFromLine(lineIndex);
                    if (lineStart < 0)
                    {
                        continue;
                    }

                    textBox.Select(lineStart, 0);
                    AssertFalse(textBox.SelectionBullet, "blank outline lines should not render as empty bullets");
                }
            }

            var chapterStart = textBox.Text.IndexOf("Ch 2 - Supper With Nuanda", StringComparison.Ordinal);
            textBox.Select(chapterStart, 1);
            AssertTrue(textBox.SelectionFont?.Bold == true, "chapter headings should be bold");
            AssertEqual(16f, textBox.SelectionFont?.Size ?? 0f, "chapter headings should use the enlarged adventure outline font");
            var jelbStart = textBox.Text.IndexOf("Jelb weighs the party's options.", StringComparison.Ordinal);
            textBox.Select(jelbStart, 1);
            AssertTrue(textBox.SelectionFont?.Bold == false, "summary bullet text should use regular font");
            AssertEqual(12f, textBox.SelectionFont?.Size ?? 0f, "summary bullet text should use the enlarged adventure outline font");
            var dungeonStart = textBox.Text.IndexOf("Dungeon Master frames the next choice.", StringComparison.Ordinal);
            textBox.Select(dungeonStart, 1);
            AssertTrue(textBox.SelectionFont?.Bold == false, "summary bullet text should not inherit heading bold");
            AssertEqual(12f, textBox.SelectionFont?.Size ?? 0f, "summary bullet text should keep the enlarged adventure outline font");
            AssertFalse(textBox.Text.Contains("Source files inspected", StringComparison.Ordinal), "player-facing outline should hide source file audit text");
            AssertFalse(textBox.Text.Contains("ch-1.html", StringComparison.Ordinal), "player-facing outline should hide source file paths");
            AssertFalse(adventureOutlineMenuItem.Enabled, "Adventure Outline menu item should be disabled while the outline is active");
        });
    }

    internal static void AboutMenuContainsAuthorAndUpdateItems()
    {
        RunOnStaThread(() =>
        {
            using var form = new Form1(suppressHeroImagesForThisRun: true);
            var menuStrip = (MenuStrip)(GetPrivateField(form, "menuStrip")
                ?? throw new InvalidOperationException("menuStrip was null."));
            var settingsMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "settingsToolStripMenuItem")
                ?? throw new InvalidOperationException("settingsToolStripMenuItem was null."));
            var aboutMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "aboutToolStripMenuItem")
                ?? throw new InvalidOperationException("aboutToolStripMenuItem was null."));
            var authorMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "authorToolStripMenuItem")
                ?? throw new InvalidOperationException("authorToolStripMenuItem was null."));
            var checkForUpdateMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "checkForUpdateToolStripMenuItem")
                ?? throw new InvalidOperationException("checkForUpdateToolStripMenuItem was null."));
            var versionMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "versionToolStripMenuItem")
                ?? throw new InvalidOperationException("versionToolStripMenuItem was null."));

            var topLevelItems = menuStrip.Items.Cast<ToolStripItem>().ToArray();
            AssertEqual("About", aboutMenuItem.Text ?? string.Empty, "unexpected About menu text");
            AssertEqual(
                Array.IndexOf(topLevelItems, settingsMenuItem) + 1,
                Array.IndexOf(topLevelItems, aboutMenuItem),
                "About menu should be immediately to the right of Settings");
            AssertEqual("Author", authorMenuItem.Text ?? string.Empty, "unexpected Author menu item text");
            AssertEqual("Check for Updates", checkForUpdateMenuItem.Text ?? string.Empty, "unexpected update menu item text");
            AssertEqual("Version", versionMenuItem.Text ?? string.Empty, "unexpected version menu item text");
            AssertTrue(
                aboutMenuItem.DropDownItems.Cast<ToolStripItem>().SequenceEqual([authorMenuItem, checkForUpdateMenuItem, versionMenuItem]),
                "About menu should contain Author, Check for Updates, then Version");
        });
    }

    internal static void AboutAuthorTextListsDeveloperInfo()
    {
        var authorText = (string)(InvokeStaticMethod(typeof(Form1), "GetAuthorInfoText")
            ?? throw new InvalidOperationException("GetAuthorInfoText returned null."));
        AssertEqual(
            string.Join(Environment.NewLine, "Bryan Miller", "kyrathasoft@gmail.com", "bryanmiller.us"),
            authorText,
            "author info text should list developer details on separate lines");
    }

    internal static void AboutVersionTextShowsAppVersion()
    {
        var versionText = (string)(InvokeStaticMethod(typeof(Form1), "GetAppVersionText")
            ?? throw new InvalidOperationException("GetAppVersionText returned null."));
        AssertEqual("RPOL Scarlet Horizon Campaign Assistant 0.9.5", versionText, "unexpected About Version text");
    }
}
