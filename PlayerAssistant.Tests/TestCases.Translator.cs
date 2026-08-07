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

namespace PlayerAssistant.Tests;

internal static partial class TestCases
{
    internal static void OrcishTranslatorReturnsOneToOneEnglishMapping()
    {
        var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("hello");

        AssertEqual(1, results.Count, "expected one translation for hello");
        AssertEqual("zug", results[0].Translation, "unexpected Orcish translation for hello");
    }

    internal static void OrcishTranslatorReturnsSeveralEnglishMatchesForOneOrcishWord()
    {
        var results = OrcishTranslatorUtility.TranslateOrcishToEnglish("mokra");

        AssertEqual(2, results.Count, "expected two English translations for mokra");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "friend", StringComparison.OrdinalIgnoreCase)), "expected friend translation");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "ally", StringComparison.OrdinalIgnoreCase)), "expected ally translation");
    }

    internal static void OrcishTranslatorUsesPartOfSpeechToDisambiguate()
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

    internal static void OrcishTranslatorFiltersHumanTermsByRegister()
    {
        var neutralResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("human", partOfSpeech: "noun", requiredTags: ["neutral"]);
        var insultingResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("weak human", partOfSpeech: "noun", requiredTags: ["insulting"]);
        var respectfulResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("free human", partOfSpeech: "noun", requiredTags: ["respectful"]);
        var pluralResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("humans", partOfSpeech: "noun", requiredTags: ["neutral", "plural"]);

        AssertEqual(1, neutralResults.Count, "expected one neutral human translation");
        AssertEqual("margi", neutralResults[0].Translation, "unexpected neutral human translation");
        AssertEqual(1, insultingResults.Count, "expected one insulting human translation");
        AssertEqual("thrum-skin", insultingResults[0].Translation, "unexpected insulting human translation");
        AssertEqual(1, respectfulResults.Count, "expected one respectful human translation");
        AssertEqual("surgar", respectfulResults[0].Translation, "unexpected respectful human translation");
        AssertEqual(1, pluralResults.Count, "expected one plural neutral human translation");
        AssertEqual("margith", pluralResults[0].Translation, "unexpected plural human translation");
    }

    internal static void OrcishTranslatorSupportsReverseLookupForRespectfulHumanTerm()
    {
        var results = OrcishTranslatorUtility.TranslateOrcishToEnglish("surgar", partOfSpeech: "noun", requiredTags: ["respectful"]);

        AssertEqual(2, results.Count, "expected respectful reverse lookup to surface both English glosses");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "sun-born", StringComparison.OrdinalIgnoreCase)), "expected sun-born reverse translation");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "free human", StringComparison.OrdinalIgnoreCase)), "expected free human reverse translation");
    }

    internal static void OrcishTranslatorGeneratesPluralPossessivesSystematically()
    {
        var neutralResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("humans'", partOfSpeech: "noun", requiredTags: ["neutral", "plural", "possessive"]);
        var insultingResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("softskins'", partOfSpeech: "noun", requiredTags: ["insulting", "plural", "possessive"]);
        var respectfulReverseResults = OrcishTranslatorUtility.TranslateOrcishToEnglish("surgariuk", partOfSpeech: "noun", requiredTags: ["respectful", "plural", "possessive"]);

        AssertEqual(1, neutralResults.Count, "expected neutral plural possessive");
        AssertEqual("margithuk", neutralResults[0].Translation, "unexpected neutral plural possessive");
        AssertEqual(1, insultingResults.Count, "expected insulting plural possessive");
        AssertEqual("thrum-skinaruk", insultingResults[0].Translation, "unexpected insulting plural possessive");
        AssertTrue(respectfulReverseResults.Any(result => string.Equals(result.Translation, "sun-born ones'", StringComparison.OrdinalIgnoreCase)), "expected respectful plural possessive reverse translation");
        AssertTrue(respectfulReverseResults.Any(result => string.Equals(result.Translation, "free humans'", StringComparison.OrdinalIgnoreCase)), "expected respectful plural possessive reverse translation for gloss");
    }

    internal static void OrcishTranslatorSupportsPaleAdjectiveRegisters()
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

    internal static void OrcishTranslatorPrefersVrakForDefaultSkinTerms()
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

    internal static void OrcishTranslatorReservesDrukhForReverentMonsterHide()
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

    internal static void OrcishTranslatorSupportsOglurVerbFamily()
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

    internal static void OrcishTranslatorExposesBothIPronounVariants()
    {
        var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("I", partOfSpeech: "pronoun");

        AssertEqual(2, results.Count, "expected two plain I variants");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "Ugh", StringComparison.OrdinalIgnoreCase)), "expected Ugh variant");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "Grrt", StringComparison.OrdinalIgnoreCase)), "expected Grrt variant");
    }

    internal static void OrcishTranslatorRandomIPickerReturnsValidVariant()
    {
        var result = OrcishTranslatorUtility.TranslateEnglishToOrcishRandom("I", partOfSpeech: "pronoun");

        AssertTrue(result is not null, "expected random I picker to return a variant");
        AssertTrue(
            string.Equals(result!.Translation, "Ugh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.Translation, "Grrt", StringComparison.OrdinalIgnoreCase),
            "expected random I picker to return one of the plain variants");
    }

    internal static void OrcishTranslatorReplacesEmphasizedIInEnglishText()
    {
        var translated = OrcishTranslatorUtility.TranslateEnglishTextToOrcishPronouns("if I {emphasis} see");

        AssertEqual("if Grrt-Ugh see", translated, "expected emphasized I to become Grrt-Ugh");
    }

    internal static void OrcishTranslatorExposesBothReallyAdverbVariants()
    {
        var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("really", partOfSpeech: "adverb");

        AssertEqual(2, results.Count, "expected two really adverb variants");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "grak", StringComparison.OrdinalIgnoreCase)), "expected grak variant");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "urkh", StringComparison.OrdinalIgnoreCase)), "expected urkh variant");
    }

    internal static void OrcishTranslatorRandomReallyPickerReturnsValidVariant()
    {
        var result = OrcishTranslatorUtility.TranslateEnglishToOrcishRandom("really", partOfSpeech: "adverb");

        AssertTrue(result is not null, "expected random really picker to return a variant");
        AssertTrue(
            string.Equals(result!.Translation, "grak", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.Translation, "urkh", StringComparison.OrdinalIgnoreCase),
            "expected random really picker to return one of the adverb variants");
    }

    internal static void OrcishTranslatorExposesBothIfVariants()
    {
        var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("if", partOfSpeech: "conjunction");

        AssertEqual(2, results.Count, "expected two plain if variants");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "ut", StringComparison.OrdinalIgnoreCase)), "expected ut variant");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "ka", StringComparison.OrdinalIgnoreCase)), "expected ka variant");
    }

    internal static void OrcishTranslatorAlternatesRepeatedIfTermsInSequence()
    {
        var results = OrcishTranslatorUtility.TranslateEnglishSequenceToOrcish(["if", "if", "if", "if"]);

        AssertEqual(4, results.Count, "expected four translated terms");
        AssertFalse(string.IsNullOrWhiteSpace(results[0].Translation), "expected first if translation");
        AssertFalse(string.Equals(results[0].Translation, results[1].Translation, StringComparison.OrdinalIgnoreCase), "expected second if to alternate");
        AssertEqual(results[0].Translation, results[2].Translation, "expected third if to alternate back to the first choice");
        AssertEqual(results[1].Translation, results[3].Translation, "expected fourth if to alternate back to the second choice");
    }

    internal static void OrcishTranslatorExposesBothButVariants()
    {
        var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("but", partOfSpeech: "conjunction");

        AssertEqual(2, results.Count, "expected two plain but variants");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "rokh", StringComparison.OrdinalIgnoreCase)), "expected rokh variant");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "nar", StringComparison.OrdinalIgnoreCase)), "expected nar variant");
    }

    internal static void OrcishTranslatorSupportsSarcasticButVariants()
    {
        var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("sarcastic but", partOfSpeech: "conjunction");

        AssertEqual(2, results.Count, "expected two sarcastic but variants");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "rokhki", StringComparison.OrdinalIgnoreCase)), "expected sarcastic rokh variant");
        AssertTrue(results.Any(result => string.Equals(result.Translation, "narki", StringComparison.OrdinalIgnoreCase)), "expected sarcastic nar variant");
    }

    internal static void OrcishTranslatorRandomButPickerReturnsValidVariant()
    {
        var result = OrcishTranslatorUtility.TranslateEnglishToOrcishRandom("but", partOfSpeech: "conjunction");

        AssertTrue(result is not null, "expected random but picker to return a variant");
        AssertTrue(
            string.Equals(result!.Translation, "rokh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.Translation, "nar", StringComparison.OrdinalIgnoreCase),
            "expected random but picker to return one of the plain variants");
    }

    internal static void OrcishTranslatorSupportsKirkilstonRefugePhraseVocabulary()
    {
        AssertEqual("Kirkilston", OrcishTranslatorUtility.TranslateEnglishToOrcish("Kirkilston", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "unexpected Kirkilston proper noun translation");
        AssertEqual("mauk vargu dakkin", OrcishTranslatorUtility.TranslateEnglishToOrcish("may opt to be staying", partOfSpeech: "verb", requiredTags: ["fixed-phrase"])[0].Translation, "unexpected staying choice phrase translation");
        AssertEqual("nak-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("vicinity", partOfSpeech: "noun", requiredTags: ["nearby"])[0].Translation, "unexpected vicinity translation");
        AssertEqual("nul-dakkin", OrcishTranslatorUtility.TranslateEnglishToOrcish("abandoned", partOfSpeech: "adjective", requiredTags: ["deserted"])[0].Translation, "unexpected abandoned translation");
        AssertEqual("ti-hek", OrcishTranslatorUtility.TranslateEnglishToOrcish("tower", partOfSpeech: "noun", requiredTags: ["built"])[0].Translation, "unexpected tower translation");
        AssertEqual("noglar", OrcishTranslatorUtility.TranslateEnglishToOrcish("hidden", partOfSpeech: "adjective", requiredTags: ["concealed"])[0].Translation, "unexpected hidden translation");
        AssertEqual("burz-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("cave", partOfSpeech: "noun", requiredTags: ["underground"])[0].Translation, "unexpected cave translation");
        AssertEqual("mokh-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("base", partOfSpeech: "noun", requiredTags: ["operations"])[0].Translation, "unexpected base translation");
        AssertEqual("mauk dok", OrcishTranslatorUtility.TranslateEnglishToOrcish("may be from", partOfSpeech: "verb", requiredTags: ["origin"])[0].Translation, "unexpected origin modal translation");
        AssertEqual("dakku-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("residence", partOfSpeech: "noun", requiredTags: ["dwelling"])[0].Translation, "unexpected residence translation");
    }

    internal static void OrcishTranslatorSupportsRegionalMapHistoryVocabulary()
    {
        AssertEqual("dak-mokhuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("regional", partOfSpeech: "adjective", requiredTags: ["area"])[0].Translation, "unexpected regional translation");
        AssertEqual("thog-var", OrcishTranslatorUtility.TranslateEnglishToOrcish("idea", partOfSpeech: "noun", requiredTags: ["abstract"])[0].Translation, "unexpected idea translation");
        AssertEqual("mur-dakuri", OrcishTranslatorUtility.TranslateEnglishToOrcish("centuries", partOfSpeech: "noun", requiredTags: ["long-span", "plural"])[0].Translation, "unexpected centuries translation");
        AssertEqual("mur-oglar", OrcishTranslatorUtility.TranslateEnglishToOrcish("famous", partOfSpeech: "adjective", requiredTags: ["known"])[0].Translation, "unexpected famous translation");
        AssertEqual("ut-dravash", OrcishTranslatorUtility.TranslateEnglishToOrcish("recovered", partOfSpeech: "verb", requiredTags: ["reclaim"])[0].Translation, "unexpected recovered translation");
        AssertEqual("dwarf-mog-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("dwarven settlement", partOfSpeech: "noun", requiredTags: ["dwarven"])[0].Translation, "unexpected dwarven settlement translation");
        AssertEqual("thrum-quum-mog-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("thorpes", partOfSpeech: "noun", requiredTags: ["rural", "plural"])[0].Translation, "unexpected thorpes translation");
        AssertEqual("thrum-mog-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("hamlets", partOfSpeech: "noun", requiredTags: ["small", "plural"])[0].Translation, "unexpected hamlets translation");
        AssertEqual("mog-dak-muri", OrcishTranslatorUtility.TranslateEnglishToOrcish("towns", partOfSpeech: "noun", requiredTags: ["settlement", "plural"])[0].Translation, "unexpected towns translation");
        AssertEqual("mog-dak-tii", OrcishTranslatorUtility.TranslateEnglishToOrcish("cities", partOfSpeech: "noun", requiredTags: ["settlement", "plural"])[0].Translation, "unexpected cities translation");
        AssertEqual("Glittering Caves", OrcishTranslatorUtility.TranslateEnglishToOrcish("Glittering Caves", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "unexpected Glittering Caves proper noun translation");
        AssertEqual("ut-dravku", OrcishTranslatorUtility.TranslateEnglishToOrcish("re-take", partOfSpeech: "verb", requiredTags: ["reclaim"])[0].Translation, "unexpected re-take translation");
        AssertEqual("orukhi", OrcishTranslatorUtility.TranslateEnglishToOrcish("orcs", partOfSpeech: "noun", requiredTags: ["orc", "plural"])[0].Translation, "unexpected orcs translation");
        AssertEqual("koboldi", OrcishTranslatorUtility.TranslateEnglishToOrcish("kobolds", partOfSpeech: "noun", requiredTags: ["plural"])[0].Translation, "unexpected kobolds translation");
    }

    internal static void OrcishTranslatorSupportsXavamrosRulershipVocabulary()
    {
        AssertEqual("dug-agh-dug", OrcishTranslatorUtility.TranslateEnglishToOrcish("IV", partOfSpeech: "numeral", requiredTags: ["fourth"])[0].Translation, "unexpected IV translation");
        AssertEqual("murdrath-kelnib dakuruk", OrcishTranslatorUtility.TranslateEnglishToOrcish("hoary with age", partOfSpeech: "adjective", requiredTags: ["very-old"])[0].Translation, "unexpected hoary with age translation");
        AssertEqual("dargu-tukur arhk darg-thrak", OrcishTranslatorUtility.TranslateEnglishToOrcish("retains the throne", partOfSpeech: "verb", requiredTags: ["rulership"])[0].Translation, "unexpected retains the throne translation");
        AssertEqual("dargin arhk burz nak-oglar-dak darg-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("ruling the subterranean near-surface haunts", partOfSpeech: "verb", requiredTags: ["territory"])[0].Translation, "unexpected ruling the subterranean near-surface haunts translation");
        AssertEqual("mok-grak", OrcishTranslatorUtility.TranslateEnglishToOrcish("just as", partOfSpeech: "conjunction", requiredTags: ["equivalence"])[0].Translation, "unexpected just as translation");
        AssertEqual("darg-mog", OrcishTranslatorUtility.TranslateEnglishToOrcish("Governor", partOfSpeech: "noun", requiredTags: ["ruler"])[0].Translation, "unexpected Governor translation");
        AssertEqual("dargur-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("holds sway", partOfSpeech: "verb", requiredTags: ["authority"])[0].Translation, "unexpected holds sway translation");
        AssertEqual("oglar-dak mokhi", OrcishTranslatorUtility.TranslateEnglishToOrcish("surface communities", partOfSpeech: "noun", requiredTags: ["surface"])[0].Translation, "unexpected surface communities translation");
    }

    internal static void OrcishTranslatorSupportsPrinceXavinYouthfulAdventureVocabulary()
    {
        AssertEqual("darg-ti-mog", OrcishTranslatorUtility.TranslateEnglishToOrcish("Prince", partOfSpeech: "noun", requiredTags: ["higher-than-governor"])[0].Translation, "unexpected Prince translation");
        AssertEqual("Xavin", OrcishTranslatorUtility.TranslateEnglishToOrcish("Xavin", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "unexpected Xavin proper noun translation");
        AssertEqual("grak ik mogumuk dug mur-dakur", OrcishTranslatorUtility.TranslateEnglishToOrcish("well into his second century", partOfSpeech: "adjective", requiredTags: ["aged"])[0].Translation, "unexpected age phrase translation");
        AssertEqual("ashdak nurik grod", OrcishTranslatorUtility.TranslateEnglishToOrcish("still youthful enough", partOfSpeech: "adjective", requiredTags: ["sufficient"])[0].Translation, "unexpected youthful enough translation");
        AssertEqual("dargu-thog ak", OrcishTranslatorUtility.TranslateEnglishToOrcish("insist on", partOfSpeech: "verb", requiredTags: ["stubborn"])[0].Translation, "unexpected insist on translation");
        AssertEqual("lag-oglarin", OrcishTranslatorUtility.TranslateEnglishToOrcish("exploring", partOfSpeech: "verb", requiredTags: ["seeking"])[0].Translation, "unexpected exploring translation");
        AssertEqual("vark-yankin", OrcishTranslatorUtility.TranslateEnglishToOrcish("adventuring", partOfSpeech: "verb", requiredTags: ["danger"])[0].Translation, "unexpected adventuring translation");
        AssertEqual("nul-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("nonsense", partOfSpeech: "noun", requiredTags: ["foolish"])[0].Translation, "unexpected nonsense translation");
        AssertEqual("morzur", OrcishTranslatorUtility.TranslateEnglishToOrcish("plagues", partOfSpeech: "verb", requiredTags: ["present"])[0].Translation, "unexpected plagues translation");
    }

    internal static void OrcishTranslatorSupportsKirkilstonChurchAndRedLawsVocabulary()
    {
        AssertEqual("tur mog-nargash dok", OrcishTranslatorUtility.TranslateEnglishToOrcish("is named after", partOfSpeech: "verb", requiredTags: ["passive"])[0].Translation, "unexpected named after translation");
        AssertEqual("mograth-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("church", partOfSpeech: "noun", requiredTags: ["religious"])[0].Translation, "unexpected church translation");
        AssertEqual("mograth-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("kirk", partOfSpeech: "noun", requiredTags: ["synonym"])[0].Translation, "unexpected kirk translation");
        AssertEqual("murk-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("center", partOfSpeech: "noun", requiredTags: ["central"])[0].Translation, "unexpected center translation");
        AssertEqual("thrak-murk-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("centerpiece", partOfSpeech: "noun", requiredTags: ["important"])[0].Translation, "unexpected centerpiece translation");
        AssertEqual("rug-mograth-dak-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("Red Temple", partOfSpeech: "noun", requiredTags: ["red-law"])[0].Translation, "unexpected Red Temple translation");
        AssertEqual("gor-ti-hek", OrcishTranslatorUtility.TranslateEnglishToOrcish("Watchtower", partOfSpeech: "noun", requiredTags: ["watch"])[0].Translation, "unexpected Watchtower translation");
        AssertEqual("thrak-murk-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("key centers", partOfSpeech: "noun", requiredTags: ["important"])[0].Translation, "unexpected key centers translation");
        AssertEqual("mograth-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("faith", partOfSpeech: "noun", requiredTags: ["belief"])[0].Translation, "unexpected faith translation");
        AssertEqual("mograthuk darg-bib", OrcishTranslatorUtility.TranslateEnglishToOrcish("Ecclesiastical Law", partOfSpeech: "noun", requiredTags: ["church"])[0].Translation, "unexpected Ecclesiastical Law translation");
        AssertEqual("dargash", OrcishTranslatorUtility.TranslateEnglishToOrcish("Governed", partOfSpeech: "verb", requiredTags: ["past-participle"])[0].Translation, "unexpected governed translation");
        AssertEqual("tur dargash fa", OrcishTranslatorUtility.TranslateEnglishToOrcish("is led by", partOfSpeech: "verb", requiredTags: ["passive"])[0].Translation, "unexpected passive led translation");
        AssertEqual("drath-mograth", OrcishTranslatorUtility.TranslateEnglishToOrcish("seasoned Priest", partOfSpeech: "noun", requiredTags: ["experienced"])[0].Translation, "unexpected seasoned Priest translation");
        AssertEqual("mograth-darg", OrcishTranslatorUtility.TranslateEnglishToOrcish("Prelacy", partOfSpeech: "noun", requiredTags: ["religious"])[0].Translation, "unexpected Prelacy translation");
        AssertEqual("rug-darg-bibi", OrcishTranslatorUtility.TranslateEnglishToOrcish("Red Laws", partOfSpeech: "noun", requiredTags: ["red-law"])[0].Translation, "unexpected Red Laws translation");
        AssertEqual("hek-darg", OrcishTranslatorUtility.TranslateEnglishToOrcish("implementation", partOfSpeech: "noun", requiredTags: ["execution"])[0].Translation, "unexpected implementation translation");
        AssertEqual("dug-agh-ash mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("triad", partOfSpeech: "noun", requiredTags: ["three"])[0].Translation, "unexpected triad translation");
        AssertEqual("mograth-darg agh darg-bib darg-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("religious and administrative authority", partOfSpeech: "noun", requiredTags: ["bureaucracy"])[0].Translation, "unexpected religious and administrative authority translation");
        AssertEqual("thrum-darg-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("more relaxed", partOfSpeech: "adjective", requiredTags: ["lenient"])[0].Translation, "unexpected more relaxed translation");
        AssertEqual("oglar-ti thrum-darg-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("notably more lenient", partOfSpeech: "adjective", requiredTags: ["noticeable"])[0].Translation, "unexpected notably more lenient translation");
        AssertEqual("mur-darg-nu-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("less rigid", partOfSpeech: "adjective", requiredTags: ["less"])[0].Translation, "unexpected less rigid translation");
        AssertEqual("mograth-bib", OrcishTranslatorUtility.TranslateEnglishToOrcish("religious doctrine", partOfSpeech: "noun", requiredTags: ["teaching"])[0].Translation, "unexpected religious doctrine translation");
        AssertEqual("grak-nak-dak-mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("immediate surrounding area", partOfSpeech: "noun", requiredTags: ["immediate"])[0].Translation, "unexpected immediate surrounding area translation");
    }

    internal static void OrcishTranslatorSupportsKirklistonEconomyVocabulary()
    {
        AssertEqual("drav-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("economy", partOfSpeech: "noun", requiredTags: ["commerce"])[0].Translation, "unexpected economy translation");
        AssertEqual("murk-dakur nak", OrcishTranslatorUtility.TranslateEnglishToOrcish("revolves around", partOfSpeech: "verb", requiredTags: ["central"])[0].Translation, "unexpected revolves around translation");
        AssertEqual("quum-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("livestock", partOfSpeech: "noun", requiredTags: ["kept"])[0].Translation, "unexpected livestock translation");
        AssertEqual("thrum-quum-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("sheep", partOfSpeech: "noun", requiredTags: ["kept"])[0].Translation, "unexpected sheep translation");
        AssertEqual("quum-hekin", OrcishTranslatorUtility.TranslateEnglishToOrcish("agriculture", partOfSpeech: "noun", requiredTags: ["farming"])[0].Translation, "unexpected agriculture translation");
        AssertEqual("thrak-quum-hek", OrcishTranslatorUtility.TranslateEnglishToOrcish("chief crop", partOfSpeech: "noun", requiredTags: ["primary"])[0].Translation, "unexpected chief crop translation");
        AssertEqual("dok-dravin", OrcishTranslatorUtility.TranslateEnglishToOrcish("export", partOfSpeech: "noun", requiredTags: ["outbound"])[0].Translation, "unexpected export translation");
        AssertEqual("rukh-mauk", OrcishTranslatorUtility.TranslateEnglishToOrcish("mead", partOfSpeech: "noun", requiredTags: ["fermented"])[0].Translation, "unexpected mead translation");
        AssertEqual("gruul-hek hekin-var", OrcishTranslatorUtility.TranslateEnglishToOrcish("lumber production", partOfSpeech: "noun", requiredTags: ["wood"])[0].Translation, "unexpected lumber production translation");
        AssertEqual("hrogar-lag", OrcishTranslatorUtility.TranslateEnglishToOrcish("caravan route", partOfSpeech: "noun", requiredTags: ["transport"])[0].Translation, "unexpected caravan route translation");
        AssertEqual("hrogar-mokhi", OrcishTranslatorUtility.TranslateEnglishToOrcish("caravans", partOfSpeech: "noun", requiredTags: ["transport", "plural"])[0].Translation, "unexpected caravans translation");
        AssertEqual("mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("company", partOfSpeech: "noun", requiredTags: ["trade"])[0].Translation, "unexpected company translation");
        AssertEqual("dravur dok", OrcishTranslatorUtility.TranslateEnglishToOrcish("benefits from", partOfSpeech: "verb", requiredTags: ["advantage"])[0].Translation, "unexpected benefits from translation");
        AssertEqual("mokru-thogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("interactions", partOfSpeech: "noun", requiredTags: ["contact"])[0].Translation, "unexpected interactions translation");
        AssertEqual("draviki", OrcishTranslatorUtility.TranslateEnglishToOrcish("merchants", partOfSpeech: "noun", requiredTags: ["trade"])[0].Translation, "unexpected merchants translation");
        AssertEqual("hrowk-dravi drav-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("exchange of goods", partOfSpeech: "noun", requiredTags: ["exchange"])[0].Translation, "unexpected exchange of goods translation");
        AssertEqual("hrowku mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("transport persons", partOfSpeech: "verb", requiredTags: ["transport"])[0].Translation, "unexpected transport persons translation");
        AssertEqual("hrowk-drav", OrcishTranslatorUtility.TranslateEnglishToOrcish("cargo", partOfSpeech: "noun", requiredTags: ["carried-goods"])[0].Translation, "unexpected cargo translation");
        AssertEqual("thrak-grak", OrcishTranslatorUtility.TranslateEnglishToOrcish("of import", partOfSpeech: "adjective", requiredTags: ["important"])[0].Translation, "unexpected of import translation");
        AssertEqual("quum-drav zorn-dakur", OrcishTranslatorUtility.TranslateEnglishToOrcish("rate of pay", partOfSpeech: "noun", requiredTags: ["payment", "rate"])[0].Translation, "unexpected rate of pay translation");
        AssertEqual("drav-biti", OrcishTranslatorUtility.TranslateEnglishToOrcish("shares", partOfSpeech: "noun", requiredTags: ["ownership", "plural"])[0].Translation, "unexpected shares translation");
    }

    internal static void OrcishTranslatorSupportsKirklistonWatchVocabulary()
    {
        AssertEqual("nul-tukrin", OrcishTranslatorUtility.TranslateEnglishToOrcish("Lacking", partOfSpeech: "verb", requiredTags: ["negative"])[0].Translation, "unexpected lacking translation");
        AssertEqual("bib-darguk gor-hek", OrcishTranslatorUtility.TranslateEnglishToOrcish("formal wall", partOfSpeech: "noun", requiredTags: ["formal"])[0].Translation, "unexpected formal wall translation");
        AssertEqual("thrak-gor-hek-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("significant defensive structures", partOfSpeech: "noun", requiredTags: ["defense"])[0].Translation, "unexpected significant defensive structures translation");
        AssertEqual("lag-tukur ak", OrcishTranslatorUtility.TranslateEnglishToOrcish("relies on", partOfSpeech: "verb", requiredTags: ["dependence"])[0].Translation, "unexpected relies on translation");
        AssertEqual("nikmokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("small group", partOfSpeech: "noun", requiredTags: ["small"])[0].Translation, "unexpected small group translation");
        AssertEqual("yanki-grod", OrcishTranslatorUtility.TranslateEnglishToOrcish("brave", partOfSpeech: "adjective", requiredTags: ["courage"])[0].Translation, "unexpected brave translation");
        AssertEqual("nul-hekin", OrcishTranslatorUtility.TranslateEnglishToOrcish("untrained", partOfSpeech: "adjective", requiredTags: ["untrained"])[0].Translation, "unexpected untrained translation");
        AssertEqual("mog-oglar mok", OrcishTranslatorUtility.TranslateEnglishToOrcish("known as", partOfSpeech: "verb", requiredTags: ["known"])[0].Translation, "unexpected known as translation");
        AssertEqual("thrum-mog-dak thrak", OrcishTranslatorUtility.TranslateEnglishToOrcish("Hamlet Watch", partOfSpeech: "noun", requiredTags: ["watch"])[0].Translation, "unexpected Hamlet Watch translation");
        AssertEqual("gor-thog-in", OrcishTranslatorUtility.TranslateEnglishToOrcish("protecting", partOfSpeech: "verb", requiredTags: ["protection", "progressive"])[0].Translation, "unexpected protecting translation");
        AssertEqual("lag-ti-bit gor-thog-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("additional protection", partOfSpeech: "noun", requiredTags: ["additional", "protection"])[0].Translation, "unexpected additional protection translation");
        AssertEqual("tukur-darg gorin", OrcishTranslatorUtility.TranslateEnglishToOrcish("responsible for safeguarding", partOfSpeech: "verb", requiredTags: ["responsibility"])[0].Translation, "unexpected responsible for safeguarding translation");
        AssertEqual("vark-thogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("threats", partOfSpeech: "noun", requiredTags: ["danger"])[0].Translation, "unexpected threats translation");
        AssertEqual("nul-darg-thog darg-gashi", OrcishTranslatorUtility.TranslateEnglishToOrcish("forces of chaos", partOfSpeech: "noun", requiredTags: ["chaos"])[0].Translation, "unexpected forces of chaos translation");
        AssertEqual("nak-vril-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("nearby wilds", partOfSpeech: "noun", requiredTags: ["nearby"])[0].Translation, "unexpected nearby wilds translation");
        AssertEqual("yanki-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("bravery", partOfSpeech: "noun", requiredTags: ["courage"])[0].Translation, "unexpected bravery translation");
        AssertEqual("mur-grod agh grotash-nu grod-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("rugged and resilient spirit", partOfSpeech: "noun", requiredTags: ["resilient"])[0].Translation, "unexpected rugged and resilient spirit translation");
        AssertEqual("Kirklistonuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("Kirkliston's", partOfSpeech: "noun", requiredTags: ["possessive"])[0].Translation, "unexpected Kirkliston's translation");
        AssertEqual("dak-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("inhabitants", partOfSpeech: "noun", requiredTags: ["resident"])[0].Translation, "unexpected inhabitants translation");
    }

    internal static void OrcishTranslatorSupportsKirklistonDailyLifeVocabulary()
    {
        AssertEqual("dakur-dakur-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("daily life", partOfSpeech: "noun", requiredTags: ["routine"])[0].Translation, "unexpected daily life translation");
        AssertEqual("agh-narg bibnak Kirkilston", OrcishTranslatorUtility.TranslateEnglishToOrcish("alternate spelling 'Kirkilston'", partOfSpeech: "noun", requiredTags: ["alternate"])[0].Translation, "unexpected alternate spelling translation");
        AssertEqual("nargash fa", OrcishTranslatorUtility.TranslateEnglishToOrcish("marked by", partOfSpeech: "verb", requiredTags: ["marked"])[0].Translation, "unexpected marked by translation");
        AssertEqual("mur-hekin mokh-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("hardworking ethos", partOfSpeech: "noun", requiredTags: ["values"])[0].Translation, "unexpected hardworking ethos translation");
        AssertEqual("dak-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("residents", partOfSpeech: "noun", requiredTags: ["resident"])[0].Translation, "unexpected residents translation");
        AssertEqual("hekin ik", OrcishTranslatorUtility.TranslateEnglishToOrcish("engaged in", partOfSpeech: "verb", requiredTags: ["working"])[0].Translation, "unexpected engaged in translation");
        AssertEqual("thrum-quum-mog-hekin", OrcishTranslatorUtility.TranslateEnglishToOrcish("shepherding", partOfSpeech: "noun", requiredTags: ["sheep"])[0].Translation, "unexpected shepherding translation");
        AssertEqual("nak-dakuk dravi", OrcishTranslatorUtility.TranslateEnglishToOrcish("local trades", partOfSpeech: "noun", requiredTags: ["local"])[0].Translation, "unexpected local trades translation");
        AssertEqual("thruk-dakku-heki", OrcishTranslatorUtility.TranslateEnglishToOrcish("essential amenities", partOfSpeech: "noun", requiredTags: ["essential"])[0].Translation, "unexpected essential amenities translation");
        AssertEqual("zol-hekruhur", OrcishTranslatorUtility.TranslateEnglishToOrcish("blacksmith", partOfSpeech: "noun", requiredTags: ["iron"])[0].Translation, "unexpected blacksmith translation");
        AssertEqual("rukh-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("tavern", partOfSpeech: "noun", requiredTags: ["drink"])[0].Translation, "unexpected tavern translation");
        AssertEqual("nik-drav-dak-mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("small market area", partOfSpeech: "noun", requiredTags: ["small"])[0].Translation, "unexpected small market area translation");
        AssertEqual("mokh-dakur-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("social life", partOfSpeech: "noun", requiredTags: ["social"])[0].Translation, "unexpected social life translation");
        AssertEqual("mokh-mokrui", OrcishTranslatorUtility.TranslateEnglishToOrcish("communal gatherings", partOfSpeech: "noun", requiredTags: ["communal"])[0].Translation, "unexpected communal gatherings translation");
        AssertEqual("mauk-mokhi", OrcishTranslatorUtility.TranslateEnglishToOrcish("festivals", partOfSpeech: "noun", requiredTags: ["celebration"])[0].Translation, "unexpected festivals translation");
        AssertEqual("drav-mauki", OrcishTranslatorUtility.TranslateEnglishToOrcish("fairs", partOfSpeech: "noun", requiredTags: ["trade"])[0].Translation, "unexpected fairs translation");
        AssertEqual("murk-thrak-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("focal points", partOfSpeech: "noun", requiredTags: ["central"])[0].Translation, "unexpected focal points translation");
        AssertEqual("mokh-mokru-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("social cohesion", partOfSpeech: "noun", requiredTags: ["unity"])[0].Translation, "unexpected social cohesion translation");
    }

    internal static void OrcishTranslatorSupportsKirklistonWildernessOpportunityVocabulary()
    {
        AssertEqual("dakkin k'ik arhk burz-nak", OrcishTranslatorUtility.TranslateEnglishToOrcish("living in the shadow", partOfSpeech: "verb", requiredTags: ["shadow"])[0].Translation, "unexpected living in the shadow translation");
        AssertEqual("Burz-ti Ti-Daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("Blackpeak Mountains", partOfSpeech: "noun", requiredTags: ["mountains"])[0].Translation, "unexpected Blackpeak Mountains translation");
        AssertEqual("Burz-gruul", OrcishTranslatorUtility.TranslateEnglishToOrcish("Darkforest", partOfSpeech: "noun", requiredTags: ["forest"])[0].Translation, "unexpected Darkforest translation");
        AssertEqual("mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("people", partOfSpeech: "noun", requiredTags: ["plural"])[0].Translation, "unexpected people translation");
        AssertEqual("noglar-nu ur", OrcishTranslatorUtility.TranslateEnglishToOrcish("no strangers to", partOfSpeech: "verb", requiredTags: ["familiar"])[0].Translation, "unexpected no strangers to translation");
        AssertEqual("grot-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("hardship", partOfSpeech: "noun", requiredTags: ["difficulty"])[0].Translation, "unexpected hardship translation");
        AssertEqual("vark-thogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("dangers", partOfSpeech: "noun", requiredTags: ["danger"])[0].Translation, "unexpected dangers translation");
        AssertEqual("nargash fa", OrcishTranslatorUtility.TranslateEnglishToOrcish("posed by", partOfSpeech: "verb", requiredTags: ["caused-by"])[0].Translation, "unexpected posed by translation");
        AssertEqual("vril-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("wilderness", partOfSpeech: "noun", requiredTags: ["wild"])[0].Translation, "unexpected wilderness translation");
        AssertEqual("grotash-nu-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("resilience", partOfSpeech: "noun", requiredTags: ["resilient"])[0].Translation, "unexpected resilience translation");
        AssertEqual("varg-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("opportunities", partOfSpeech: "noun", requiredTags: ["opportunity"])[0].Translation, "unexpected opportunities translation");
        AssertEqual("vark-yank-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("adventurers", partOfSpeech: "noun", requiredTags: ["danger"])[0].Translation, "unexpected adventurers translation");
        AssertEqual("tar ut", OrcishTranslatorUtility.TranslateEnglishToOrcish("be it", partOfSpeech: "conjunction", requiredTags: ["alternative"])[0].Translation, "unexpected be it translation");
        AssertEqual("dravin", OrcishTranslatorUtility.TranslateEnglishToOrcish("aiding", partOfSpeech: "verb", requiredTags: ["help"])[0].Translation, "unexpected aiding translation");
        AssertEqual("gor-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("defense", partOfSpeech: "noun", requiredTags: ["defense"])[0].Translation, "unexpected defense translation");
        AssertEqual("hekin ik", OrcishTranslatorUtility.TranslateEnglishToOrcish("engaging in", partOfSpeech: "verb", requiredTags: ["working"])[0].Translation, "unexpected engaging in translation");
        AssertEqual("drav hek-vari", OrcishTranslatorUtility.TranslateEnglishToOrcish("trade activities", partOfSpeech: "noun", requiredTags: ["commerce"])[0].Translation, "unexpected trade activities translation");
        AssertEqual("hek-bib", OrcishTranslatorUtility.TranslateEnglishToOrcish("jobs board", partOfSpeech: "noun", requiredTags: ["work"])[0].Translation, "unexpected jobs board translation");
        AssertEqual("ut-narg-bibi", OrcishTranslatorUtility.TranslateEnglishToOrcish("following postings", partOfSpeech: "noun", requiredTags: ["following"])[0].Translation, "unexpected following postings translation");
    }

    internal static void OrcishTranslatorSupportsMorganTavernObservationVocabulary()
    {
        AssertEqual("Brand", OrcishTranslatorUtility.TranslateEnglishToOrcish("Brand", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "unexpected Brand translation");
        AssertEqual("varkin ik", OrcishTranslatorUtility.TranslateEnglishToOrcish("slides into", partOfSpeech: "verb", requiredTags: ["entering"])[0].Translation, "unexpected slides into translation");
        AssertEqual("Morganuk quum-biti agh rukh-banti", OrcishTranslatorUtility.TranslateEnglishToOrcish("Morgan's Morsels & Tankards", partOfSpeech: "noun", requiredTags: ["tavern"])[0].Translation, "unexpected tavern name translation");
        AssertEqual("quum-biti", OrcishTranslatorUtility.TranslateEnglishToOrcish("Morsels", partOfSpeech: "noun", requiredTags: ["food"])[0].Translation, "unexpected Morsels translation");
        AssertEqual("rukh-banti", OrcishTranslatorUtility.TranslateEnglishToOrcish("Tankards", partOfSpeech: "noun", requiredTags: ["vessel"])[0].Translation, "unexpected Tankards translation");
        AssertEqual("nul-thogin", OrcishTranslatorUtility.TranslateEnglishToOrcish("unconsciously", partOfSpeech: "adverb", requiredTags: ["unconscious"])[0].Translation, "unexpected unconsciously translation");
        AssertEqual("nu grotash ogh", OrcishTranslatorUtility.TranslateEnglishToOrcish("doesn't interfere with", partOfSpeech: "verb", requiredTags: ["negative"])[0].Translation, "unexpected doesn't interfere with translation");
        AssertEqual("lag ur", OrcishTranslatorUtility.TranslateEnglishToOrcish("access to", partOfSpeech: "preposition", requiredTags: ["access"])[0].Translation, "unexpected access to translation");
        AssertEqual("zol-bant", OrcishTranslatorUtility.TranslateEnglishToOrcish("pommel", partOfSpeech: "noun", requiredTags: ["handle"])[0].Translation, "unexpected pommel translation");
        AssertEqual("zol-gash", OrcishTranslatorUtility.TranslateEnglishToOrcish("sword", partOfSpeech: "noun", requiredTags: ["weapon"])[0].Translation, "unexpected sword translation");
        AssertEqual("oglur nak", OrcishTranslatorUtility.TranslateEnglishToOrcish("looks around", partOfSpeech: "verb", requiredTags: ["nearby"])[0].Translation, "unexpected looks around translation");
        AssertEqual("dravik-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("customers", partOfSpeech: "noun", requiredTags: ["buyer"])[0].Translation, "unexpected customers translation");
        AssertEqual("nak-dak-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("locals", partOfSpeech: "noun", requiredTags: ["local"])[0].Translation, "unexpected locals translation");
        AssertEqual("oglar-thogin", OrcishTranslatorUtility.TranslateEnglishToOrcish("sizing up", partOfSpeech: "verb", requiredTags: ["assessing"])[0].Translation, "unexpected sizing up translation");
        AssertEqual("mok ughat tukra", OrcishTranslatorUtility.TranslateEnglishToOrcish("what do we have", partOfSpeech: "verb", requiredTags: ["question"])[0].Translation, "unexpected what do we have translation");
        AssertEqual("darg-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("station", partOfSpeech: "noun", requiredTags: ["rank"])[0].Translation, "unexpected station translation");
        AssertEqual("dwarfuk hekash", OrcishTranslatorUtility.TranslateEnglishToOrcish("dwarven made", partOfSpeech: "verb", requiredTags: ["dwarven"])[0].Translation, "unexpected dwarven made translation");
        AssertEqual("thrak-thog-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("high quality", partOfSpeech: "noun", requiredTags: ["high"])[0].Translation, "unexpected high quality translation");
        AssertEqual("dug-agh-ash bantin", OrcishTranslatorUtility.TranslateEnglishToOrcish("triple braided", partOfSpeech: "adjective", requiredTags: ["braided"])[0].Translation, "unexpected triple braided translation");
        AssertEqual("dug-agh-ash", OrcishTranslatorUtility.TranslateEnglishToOrcish("triple", partOfSpeech: "adjective", requiredTags: ["multiplier"])[0].Translation, "unexpected triple translation");
        AssertEqual("darg-ti-mog", OrcishTranslatorUtility.TranslateEnglishToOrcish("noble", partOfSpeech: "noun", requiredTags: ["noble"])[0].Translation, "unexpected noble translation");
        AssertEqual("dargin mokh-zog", OrcishTranslatorUtility.TranslateEnglishToOrcish("ruling family", partOfSpeech: "noun", requiredTags: ["ruling"])[0].Translation, "unexpected ruling family translation");
        AssertEqual("mokh-zog", OrcishTranslatorUtility.TranslateEnglishToOrcish("family", partOfSpeech: "noun", requiredTags: ["root-repaired"])[0].Translation, "unexpected family translation");
        AssertEqual("dakku-dak mokh-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("home base", partOfSpeech: "noun", requiredTags: ["operations"])[0].Translation, "unexpected home base translation");
    }

    internal static void OrcishTranslatorTreatsDwarfOnlyAsDwarvenRace()
    {
        var dwarf = OrcishTranslatorUtility.TranslateEnglishToOrcish("dwarf", partOfSpeech: "noun", requiredTags: ["dwarven-race"]);

        AssertEqual(1, dwarf.Count, "dwarf should have a Dwarven race translation");
        AssertEqual("dwarf", dwarf[0].Translation, "unexpected Dwarven race translation");
        AssertEqual("species", dwarf[0].GrammarClass ?? string.Empty, "dwarf should be classified as a species");
        AssertTrue(dwarf[0].Tags?.Contains("dwarven-race", StringComparer.OrdinalIgnoreCase) == true, "dwarf should carry the Dwarven race sense");
        AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("dwarf", partOfSpeech: "noun", requiredTags: ["diminutive"]).Count, "dwarf should not carry a diminutive sense");
        AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("dwarf", partOfSpeech: "noun", requiredTags: ["midget"]).Count, "dwarf should not carry a midget sense");
        AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("midget", partOfSpeech: "noun").Count, "Orcish should not have a midget term");
    }

    internal static void OrcishTranslatorSupportsMorganDiningAcknowledgementVocabulary()
    {
        AssertEqual("grukhur", OrcishTranslatorUtility.TranslateEnglishToOrcish("grunts", partOfSpeech: "verb", requiredTags: ["rough"])[0].Translation, "unexpected grunts translation");
        AssertEqual("ut-oglarur mogumuk oglar-thog ur", OrcishTranslatorUtility.TranslateEnglishToOrcish("returns his attention to", partOfSpeech: "verb", requiredTags: ["attention"])[0].Translation, "unexpected returns his attention to translation");
        AssertEqual("darg-dravik", OrcishTranslatorUtility.TranslateEnglishToOrcish("proprietor", partOfSpeech: "noun", requiredTags: ["owner"])[0].Translation, "unexpected proprietor translation");
        AssertEqual("mokra-narg", OrcishTranslatorUtility.TranslateEnglishToOrcish("Well met", partOfSpeech: "interjection", requiredTags: ["greeting"])[0].Translation, "unexpected Well met translation");
        AssertEqual("rukh-quum", OrcishTranslatorUtility.TranslateEnglishToOrcish("ale", partOfSpeech: "noun", requiredTags: ["fermented"])[0].Translation, "unexpected ale translation");
        AssertEqual("rukh-quum", OrcishTranslatorUtility.TranslateEnglishToOrcish("soup", partOfSpeech: "noun", requiredTags: ["liquid"])[0].Translation, "unexpected soup translation");
        AssertEqual("hek-quum", OrcishTranslatorUtility.TranslateEnglishToOrcish("bread", partOfSpeech: "noun", requiredTags: ["baked"])[0].Translation, "unexpected bread translation");
        AssertEqual("mauk-drav", OrcishTranslatorUtility.TranslateEnglishToOrcish("please", partOfSpeech: "interjection", requiredTags: ["request"])[0].Translation, "unexpected please translation");
        AssertEqual("tukru-drav", OrcishTranslatorUtility.TranslateEnglishToOrcish("Obliged", partOfSpeech: "interjection", requiredTags: ["thanks"])[0].Translation, "unexpected Obliged translation");
        AssertEqual("nargu-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("acknowledge", partOfSpeech: "verb", requiredTags: ["acknowledgement"])[0].Translation, "unexpected acknowledge translation");
        AssertEqual("nargur-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("acknowledges", partOfSpeech: "verb", requiredTags: ["acknowledgement"])[0].Translation, "unexpected acknowledges translation");
        AssertEqual("nargash-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("acknowledged", partOfSpeech: "verb", requiredTags: ["acknowledgement"])[0].Translation, "unexpected acknowledged translation");
        AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("acknnowledges", partOfSpeech: "verb").Count, "misspelled acknnowledges should not be a lexicon entry");
        AssertEqual("quum-dak-mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("dining area", partOfSpeech: "noun", requiredTags: ["food"])[0].Translation, "unexpected dining area translation");
        AssertEqual("thrum-yank", OrcishTranslatorUtility.TranslateEnglishToOrcish("lanky", partOfSpeech: "adjective", requiredTags: ["thin"])[0].Translation, "unexpected lanky translation");
        AssertEqual("mur-oglur ik", OrcishTranslatorUtility.TranslateEnglishToOrcish("stares into", partOfSpeech: "verb", requiredTags: ["staring"])[0].Translation, "unexpected stares into translation");
        AssertEqual("rukh-turi", OrcishTranslatorUtility.TranslateEnglishToOrcish("flames", partOfSpeech: "noun", requiredTags: ["fire"])[0].Translation, "unexpected flames translation");
    }

    internal static void OrcishTranslatorSupportsKelpieRoadAndInnVocabulary()
    {
        AssertEqual("Kelpie", OrcishTranslatorUtility.TranslateEnglishToOrcish("Kelpie", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "unexpected Kelpie translation");
        AssertEqual("Burz-gruul", OrcishTranslatorUtility.TranslateEnglishToOrcish("Darkwood Forest", partOfSpeech: "noun", requiredTags: ["forest"])[0].Translation, "unexpected Darkwood Forest translation");
        AssertEqual("Ravenuk Lag", OrcishTranslatorUtility.TranslateEnglishToOrcish("Raven’s Pass", partOfSpeech: "noun", requiredTags: ["pass"])[0].Translation, "unexpected Raven's Pass translation");
        AssertEqual("fletragi", OrcishTranslatorUtility.TranslateEnglishToOrcish("traveller", partOfSpeech: "noun", requiredTags: ["wayfarer"])[0].Translation, "unexpected traveller translation");
        AssertEqual("lagu ughatuk lag", OrcishTranslatorUtility.TranslateEnglishToOrcish("make their way", partOfSpeech: "verb", requiredTags: ["travel"])[0].Translation, "unexpected make their way translation");
        AssertEqual("vrul-lagi", OrcishTranslatorUtility.TranslateEnglishToOrcish("hedgerows", partOfSpeech: "noun", requiredTags: ["hedge"])[0].Translation, "unexpected hedgerows translation");
        AssertEqual("dug-lag-mokrui", OrcishTranslatorUtility.TranslateEnglishToOrcish("crossroads", partOfSpeech: "noun", requiredTags: ["junction"])[0].Translation, "unexpected crossroads translation");
        AssertEqual("mokh-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("common folk", partOfSpeech: "noun", requiredTags: ["folk"])[0].Translation, "unexpected common folk translation");
        AssertEqual("narg-bib-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("notice board", partOfSpeech: "noun", requiredTags: ["notice"])[0].Translation, "unexpected notice board translation");
        AssertEqual("nargash mogumuk oglar-krub", OrcishTranslatorUtility.TranslateEnglishToOrcish("caught his eye", partOfSpeech: "verb", requiredTags: ["attention"])[0].Translation, "unexpected caught his eye translation");
        AssertEqual("dak-hekmogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("settlers", partOfSpeech: "noun", requiredTags: ["settlement"])[0].Translation, "unexpected settlers translation");
        AssertEqual("rukh-dak darg-dravik", OrcishTranslatorUtility.TranslateEnglishToOrcish("innkeeper", partOfSpeech: "noun", requiredTags: ["tavern"])[0].Translation, "unexpected innkeeper translation");
        AssertEqual("ash zol-ti-drav-zol", OrcishTranslatorUtility.TranslateEnglishToOrcish("a single gold coin", partOfSpeech: "noun", requiredTags: ["gold"])[0].Translation, "unexpected single gold coin translation");
        AssertEqual("ashdak ur mogumuk mog-narg", OrcishTranslatorUtility.TranslateEnglishToOrcish("left to his name", partOfSpeech: "verb", requiredTags: ["remaining"])[0].Translation, "unexpected left to his name translation");
        AssertEqual("mogum taruk dakkin-naut", OrcishTranslatorUtility.TranslateEnglishToOrcish("he’d be sleeping", partOfSpeech: "verb", requiredTags: ["sleeping"])[0].Translation, "unexpected he'd be sleeping translation");
        AssertEqual("nul-togruk", OrcishTranslatorUtility.TranslateEnglishToOrcish("toothless", partOfSpeech: "adjective", requiredTags: ["toothless"])[0].Translation, "unexpected toothless translation");
    }

    internal static void OrcishTranslatorSupportsKelpieFellowshipPrayerVocabulary()
    {
        AssertEqual("dakku-bant-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("bench", partOfSpeech: "noun", requiredTags: ["seat"])[0].Translation, "unexpected bench translation");
        AssertEqual("hrowku", OrcishTranslatorUtility.TranslateEnglishToOrcish("carry", partOfSpeech: "verb", requiredTags: ["carrying"])[0].Translation, "unexpected carry translation");
        AssertEqual("hrowkur", OrcishTranslatorUtility.TranslateEnglishToOrcish("carries", partOfSpeech: "verb", requiredTags: ["carrying"])[0].Translation, "unexpected carries translation");
        AssertEqual("hrowkash", OrcishTranslatorUtility.TranslateEnglishToOrcish("carried", partOfSpeech: "verb", requiredTags: ["carrying"])[0].Translation, "unexpected carried translation");
        AssertEqual("hrowkin", OrcishTranslatorUtility.TranslateEnglishToOrcish("carrying", partOfSpeech: "verb", requiredTags: ["carrying"])[0].Translation, "unexpected carrying translation");
        AssertEqual("gruul-hek-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("lumberjacks", partOfSpeech: "noun", requiredTags: ["wood"])[0].Translation, "unexpected lumberjacks translation");
        AssertEqual("quum-hekmoguk nurik-mog", OrcishTranslatorUtility.TranslateEnglishToOrcish("farmer’s daughter", partOfSpeech: "noun", requiredTags: ["family"])[0].Translation, "unexpected farmer's daughter translation");
        AssertEqual("thrum-narg mograth-narg", OrcishTranslatorUtility.TranslateEnglishToOrcish("quiet prayer", partOfSpeech: "noun", requiredTags: ["prayer"])[0].Translation, "unexpected quiet prayer translation");
        AssertEqual("Demetra", OrcishTranslatorUtility.TranslateEnglishToOrcish("Demetra", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "unexpected Demetra translation");
        AssertEqual("mokru-mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("fellowship", partOfSpeech: "noun", requiredTags: ["companionship"])[0].Translation, "unexpected fellowship translation");
        AssertEqual("varg-thog ur gor-dargu", OrcishTranslatorUtility.TranslateEnglishToOrcish("willing to stand", partOfSpeech: "verb", requiredTags: ["willing"])[0].Translation, "unexpected willing to stand translation");
        AssertEqual("zol-gash-darg-mog", OrcishTranslatorUtility.TranslateEnglishToOrcish("knight", partOfSpeech: "noun", requiredTags: ["noble"])[0].Translation, "unexpected knight translation");
        AssertEqual("gash-hrog", OrcishTranslatorUtility.TranslateEnglishToOrcish("warhorse", partOfSpeech: "noun", requiredTags: ["mount"])[0].Translation, "unexpected warhorse translation");
        AssertEqual("rukh-hekin", OrcishTranslatorUtility.TranslateEnglishToOrcish("kindling", partOfSpeech: "verb", requiredTags: ["ignite"])[0].Translation, "unexpected kindling translation");
    }

    internal static void OrcishTranslatorSupportsHeraldicStrangerEquipmentVocabulary()
    {
        AssertEqual("rug-mograth-bant", OrcishTranslatorUtility.TranslateEnglishToOrcish("red cross", partOfSpeech: "noun", requiredTags: ["heraldic"])[0].Translation, "unexpected red cross translation");
        AssertEqual("khal-bib", OrcishTranslatorUtility.TranslateEnglishToOrcish("tabard", partOfSpeech: "noun", requiredTags: ["heraldic"])[0].Translation, "unexpected tabard translation");
        AssertEqual("zol-bant-khal", OrcishTranslatorUtility.TranslateEnglishToOrcish("chainmail hauberk", partOfSpeech: "noun", requiredTags: ["chainmail"])[0].Translation, "unexpected chainmail hauberk translation");
        AssertEqual("zol-mog-ti-khal", OrcishTranslatorUtility.TranslateEnglishToOrcish("kettle hat", partOfSpeech: "noun", requiredTags: ["helmet"])[0].Translation, "unexpected kettle hat translation");
        AssertEqual("zornash zol-bant-dok", OrcishTranslatorUtility.TranslateEnglishToOrcish("hung haft-down", partOfSpeech: "verb", requiredTags: ["haft-down"])[0].Translation, "unexpected hung haft-down translation");
        AssertEqual("gor-zol-murk", OrcishTranslatorUtility.TranslateEnglishToOrcish("boss", partOfSpeech: "noun", requiredTags: ["shield"])[0].Translation, "unexpected shield boss translation");
        AssertEqual("dravik-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("patrons", partOfSpeech: "noun", requiredTags: ["tavern"])[0].Translation, "unexpected tavern patrons translation");
        AssertEqual("morz-krub-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("sore thumb", partOfSpeech: "noun", requiredTags: ["thumb"])[0].Translation, "unexpected sore thumb translation");
        AssertEqual("narg-gash", OrcishTranslatorUtility.TranslateEnglishToOrcish("target", partOfSpeech: "noun", requiredTags: ["target"])[0].Translation, "unexpected target translation");
        AssertEqual("rug-mograth-bant narg-var", OrcishTranslatorUtility.TranslateEnglishToOrcish("red cross design", partOfSpeech: "noun", requiredTags: ["design"])[0].Translation, "unexpected red cross design translation");
        AssertEqual("ut-dakur ur", OrcishTranslatorUtility.TranslateEnglishToOrcish("then to", partOfSpeech: "adverb", requiredTags: ["then"])[0].Translation, "unexpected then to translation");
    }

    internal static void OrcishTranslatorSupportsHistoricalWikiFodderVocabulary()
    {
        AssertEqual("murk-dak-mur-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("intercontinental", partOfSpeech: "adjective")[0].Translation, "unexpected intercontinental translation");
        AssertEqual("morz-vrak-rukh", OrcishTranslatorUtility.TranslateEnglishToOrcish("sickness", partOfSpeech: "noun")[0].Translation, "unexpected sickness translation");
        AssertEqual("brak-grod-nu-hek-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("violence", partOfSpeech: "noun")[0].Translation, "unexpected violence translation");
        AssertEqual("mokh-ash-flit", OrcishTranslatorUtility.TranslateEnglishToOrcish("society", partOfSpeech: "noun")[0].Translation, "unexpected society translation");
        AssertEqual("nul-darg-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("anarchy", partOfSpeech: "noun")[0].Translation, "unexpected anarchy translation");
        AssertEqual("margith", OrcishTranslatorUtility.TranslateEnglishToOrcish("humanity", partOfSpeech: "noun")[0].Translation, "unexpected humanity translation");
        AssertEqual("dak-mur-bant-murkuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("planet", partOfSpeech: "noun")[0].Translation, "unexpected planet translation");
        AssertEqual("grak-laguk", OrcishTranslatorUtility.TranslateEnglishToOrcish("conventional", partOfSpeech: "adjective")[0].Translation, "unexpected conventional translation");
        AssertEqual("gash-dakur-hek-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("warfare", partOfSpeech: "noun")[0].Translation, "unexpected warfare translation");
        AssertEqual("dakur-thogu-mog", OrcishTranslatorUtility.TranslateEnglishToOrcish("survivor", partOfSpeech: "noun")[0].Translation, "unexpected survivor translation");
        AssertEqual("brak-thog-ti-morz-dakur", OrcishTranslatorUtility.TranslateEnglishToOrcish("holocaust", partOfSpeech: "noun")[0].Translation, "unexpected holocaust translation");
        AssertEqual("dak-mogi-zorn", OrcishTranslatorUtility.TranslateEnglishToOrcish("population", partOfSpeech: "noun")[0].Translation, "unexpected population translation");
        AssertEqual("dak-mur-ti-mur-kaag-tuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("global", partOfSpeech: "adjective")[0].Translation, "unexpected global translation");
        AssertEqual("dok-ka-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("relation", partOfSpeech: "noun")[0].Translation, "unexpected relation translation");
        AssertEqual("heku-yankuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("harden", partOfSpeech: "verb")[0].Translation, "unexpected harden translation");
        AssertEqual("dak-burzuk-gor-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("bunker", partOfSpeech: "noun")[0].Translation, "unexpected bunker translation");
        AssertEqual("darg-gash-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("leadership", partOfSpeech: "noun")[0].Translation, "unexpected leadership translation");
        AssertEqual("drav-zol-mokhuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("affluent", partOfSpeech: "adjective")[0].Translation, "unexpected affluent translation");
        AssertEqual("darg-thog-laguk", OrcishTranslatorUtility.TranslateEnglishToOrcish("influential", partOfSpeech: "adjective")[0].Translation, "unexpected influential translation");
        AssertEqual("thrum-zorn-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("decline", partOfSpeech: "noun")[0].Translation, "unexpected decline translation");
        AssertEqual("gakh-dakur-tiwi", OrcishTranslatorUtility.TranslateEnglishToOrcish("decade", partOfSpeech: "noun")[0].Translation, "unexpected decade translation");
        AssertEqual("murk-dak-muri", OrcishTranslatorUtility.TranslateEnglishToOrcish("international", partOfSpeech: "adjective")[0].Translation, "unexpected international translation");
        AssertEqual("grot-lag-lagu-zorn", OrcishTranslatorUtility.TranslateEnglishToOrcish("crawl", partOfSpeech: "verb")[0].Translation, "unexpected crawl translation");
        AssertEqual("disasdok-dok-lag-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("mothball", partOfSpeech: "verb")[0].Translation, "unexpected mothball translation");
        AssertEqual("morz-dok-hekinuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("medical", partOfSpeech: "adjective")[0].Translation, "unexpected medical translation");
        AssertEqual("gor-thog-ti-drav-thruk", OrcishTranslatorUtility.TranslateEnglishToOrcish("care", partOfSpeech: "noun")[0].Translation, "unexpected care translation");
        AssertEqual("lagu-zorn-dok", OrcishTranslatorUtility.TranslateEnglishToOrcish("revert", partOfSpeech: "verb")[0].Translation, "unexpected revert translation");
        AssertEqual("nu-zorn", OrcishTranslatorUtility.TranslateEnglishToOrcish("shortage", partOfSpeech: "noun")[0].Translation, "unexpected shortage translation");
        AssertEqual("burz-hek-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("infrastructure", partOfSpeech: "noun")[0].Translation, "unexpected infrastructure translation");
        AssertEqual("rukh-gurmog", OrcishTranslatorUtility.TranslateEnglishToOrcish("drug", partOfSpeech: "noun")[0].Translation, "unexpected drug translation");
        AssertEqual("rukh-gurmog", OrcishTranslatorUtility.TranslateEnglishToOrcish("pharmaceutical", partOfSpeech: "noun")[0].Translation, "unexpected pharmaceutical translation");
        AssertEqual("ash-ash-dakur-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("annual", partOfSpeech: "adjective")[0].Translation, "unexpected annual translation");
        AssertEqual("mograth-hek", OrcishTranslatorUtility.TranslateEnglishToOrcish("observance", partOfSpeech: "noun")[0].Translation, "unexpected observance translation");
        AssertEqual("ukin", OrcishTranslatorUtility.TranslateEnglishToOrcish("voluntary", partOfSpeech: "adjective")[0].Translation, "unexpected voluntary translation");
        AssertEqual("dok-lag-ti-dakku-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("exile", partOfSpeech: "noun")[0].Translation, "unexpected exile translation");
        AssertEqual("thrum-zorn-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("decadence", partOfSpeech: "noun")[0].Translation, "unexpected decadence translation");
        AssertEqual("nargu-grod-zog-dorn", OrcishTranslatorUtility.TranslateEnglishToOrcish("praise", partOfSpeech: "verb")[0].Translation, "unexpected praise translation");
        AssertEqual("mauk-hek", OrcishTranslatorUtility.TranslateEnglishToOrcish("skill", partOfSpeech: "noun")[0].Translation, "unexpected skill translation");
        AssertEqual("brak-thog-morz-dok", OrcishTranslatorUtility.TranslateEnglishToOrcish("vengeance", partOfSpeech: "noun")[0].Translation, "unexpected vengeance translation");
        AssertEqual("nargu-morz-thog-nu", OrcishTranslatorUtility.TranslateEnglishToOrcish("insult", partOfSpeech: "verb")[0].Translation, "unexpected insult translation");
        AssertEqual("nargash-morz-thog-nu", OrcishTranslatorUtility.TranslateEnglishToOrcish("insulted", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "past"])[0].Translation, "unexpected rule-derived insulted translation");

        var diseaseMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("morz-vrak-rukh", partOfSpeech: "noun");
        AssertTrue(diseaseMeanings.Any(static candidate => candidate.Translation == "sickness"), "reverse disease form should include sickness");
        var communityMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("mokh-ash-flit", partOfSpeech: "noun");
        AssertTrue(communityMeanings.Any(static candidate => candidate.Translation == "community"), "reverse community form should retain community");
        AssertTrue(communityMeanings.Any(static candidate => candidate.Translation == "society"), "reverse community form should include society");
        var chaosMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("nul-darg-thog", partOfSpeech: "noun");
        AssertTrue(chaosMeanings.Any(static candidate => candidate.Translation == "chaos"), "reverse chaos form should retain chaos");
        AssertTrue(chaosMeanings.Any(static candidate => candidate.Translation == "anarchy"), "reverse chaos form should include anarchy");
        var humanityMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("margith", partOfSpeech: "noun");
        AssertTrue(humanityMeanings.Any(static candidate => candidate.Translation == "humans"), "reverse humanity form should retain humans");
        AssertTrue(humanityMeanings.Any(static candidate => candidate.Translation == "humanity"), "reverse humanity form should include humanity");
        var leadershipMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("darg-gash-mogi", partOfSpeech: "noun");
        AssertTrue(leadershipMeanings.Any(static candidate => candidate.Translation == "leaders"), "reverse leadership form should retain leaders");
        AssertTrue(leadershipMeanings.Any(static candidate => candidate.Translation == "leadership"), "reverse leadership form should include leadership");
        var drugMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("rukh-gurmog", partOfSpeech: "noun");
        AssertTrue(drugMeanings.Any(static candidate => candidate.Translation == "drug"), "reverse potion form should include drug");
        AssertTrue(drugMeanings.Any(static candidate => candidate.Translation == "pharmaceutical"), "reverse potion form should include pharmaceutical");
        var declineMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("thrum-zorn-thog", partOfSpeech: "noun");
        AssertTrue(declineMeanings.Any(static candidate => candidate.Translation == "decline"), "reverse decline form should retain decline");
        AssertTrue(declineMeanings.Any(static candidate => candidate.Translation == "decadence"), "reverse decline form should include decadence");
        var skillMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("mauk-hek", partOfSpeech: "noun");
        AssertTrue(skillMeanings.Any(static candidate => candidate.Translation == "ability"), "reverse skill form should retain ability");
        AssertTrue(skillMeanings.Any(static candidate => candidate.Translation == "skill"), "reverse skill form should include skill");
        AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("radiation").Count, "dropped radiation candidate should remain absent");
        AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("nuclear").Count, "dropped nuclear candidate should remain absent");
        AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("science").Count, "dropped science candidate should remain absent");
        AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("program").Count, "dropped program candidate should remain absent");
    }

    internal static void OrcishTranslatorSupportsTwelvePageWikiScrapeVocabulary()
    {
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["veteran"] = "drath-gash",
            ["discipline"] = "hekin-gash-darg-lag",
            ["reliability"] = "gor-laguk-thog",
            ["incursion"] = "gash-narg-ik-lagu-dak",
            ["schism"] = "mokh-zorn-dug",
            ["captivity"] = "darg-varkum-thog",
            ["reverence"] = "grak-tur-ti-mograth-thog",
            ["pantheon"] = "mograth-darg-mogi-mokh-zorn",
            ["convert"] = "varu-mograth-thog",
            ["layover"] = "nul-vrak-dakur-lagu-dok",
            ["militia"] = "nak-dakuk-gash-darg-morz",
            ["craftsman"] = "mauk-hek-heku-mog",
            ["restriction"] = "gor-dak-dargu-mokh-mokh",
            ["felony"] = "darg-bib-grum-brak-morz-bibuk",
            ["judge"] = "darg-bib-grum-brak-mog",
            ["censorship"] = "dargu-mokh-mokh-narg-bib",
            ["taxation"] = "darg-thog-quum-drav",
            ["imprisonment"] = "darg-varkum-thog",
            ["privilege"] = "var-tiuk-grak-nak-ti",
            ["decree"] = "darg-narg-darg-bib-grum-brak",
            ["selflessness"] = "drakuin-mur-kaag-tuk",
            ["phenomenon"] = "var-tiuk-dakur-hek-ti",
            ["madness"] = "thog-vrak-nul-darg-thog",
            ["aberrant"] = "morz-bibuk-var-tiuk",
            ["resistant"] = "goru-varkuk",
            ["emanation"] = "gurmog-thog-dok-darg-krag",
            ["industrial"] = "hek-zol-hek-grum-morzuk",
            ["machine"] = "hek-grum-morz-hek-zol",
            ["steam"] = "rukh-ash-dak-rukh-ti-hush",
            ["dissipation"] = "thrum-zorn-thog"
        };

        foreach (var pair in expected)
        {
            var results = OrcishTranslatorUtility.TranslateEnglishToOrcish(pair.Key);
            AssertTrue(results.Any(candidate => candidate.Translation == pair.Value), $"unexpected translation for {pair.Key}");
        }

        var captivityMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("darg-varkum-thog", partOfSpeech: "noun");
        AssertTrue(captivityMeanings.Any(static candidate => candidate.Translation == "captivity"), "reverse captivity form should include captivity");
        AssertTrue(captivityMeanings.Any(static candidate => candidate.Translation == "imprisonment"), "reverse captivity form should include imprisonment");
    }

    internal static void OrcishTranslatorSupportsRecoveredScrollVocabulary()
    {
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dedicate"] = "draku-mur-kaag-tuk",
            ["curb"] = "dargu-mokh-mokh-gor-dak",
            ["spread"] = "lagu-zorn-mur-kaag-tuk",
            ["spore"] = "gruul-rukh-bit",
            ["epicenter"] = "murk-dak-dakur-hek-ti",
            ["pay"] = "quum-dravu",
            ["weak"] = "nu-brak-burz-yankuk",
            ["third"] = "dug-agh-ash-darg-lag",
            ["fourth"] = "dug-dug-darg-lag",
            ["break"] = "braku",
            ["leg"] = "vrak-lag",
            ["possible"] = "mauk-grrt-ashuk",
            ["seize"] = "dravku-krag-flit-darg-gash",
            ["track"] = "lag-narg-bib",
            ["source"] = "dok-darg-krag-dak",
            ["labor"] = "hek-grum-morz",
            ["prefer"] = "vargu-dak-zog-ti",
            ["waste"] = "flitu-dok-lag-ti",
            ["desire"] = "thruk-thog-var",
            ["reader"] = "bib-oglaru-mog",
            ["strange"] = "var-tiuk-gi",
            ["question"] = "narg-bib-thog-var",
            ["lie"] = "nargu-morz-bibuk",
            ["remove"] = "dravku-krag-flit-dok-lag-ti",
            ["deliver"] = "dravku-ik-draku",
            ["instruction"] = "darg-narg-narg-bib",
            ["follow"] = "lagu-dok-dak-tuk",
            ["watcher-mark"] = "thrak-narg-bib",
            ["thorn"] = "grodu-vrak-zorn-bit-dak",
            ["hill"] = "thrum-brak-grrt-ti-dak",
            ["split"] = "heku-dug-dak",
            ["root"] = "grodu-vrak-mokh-dak",
            ["collect"] = "mokhu-mur-kaag-tuk",
            ["hunger"] = "quum-thruk",
            ["crude"] = "nu-brak-burz-mauk-hek",
            ["depict"] = "heku-narg-bib",
            ["jagged"] = "brakuk-zol-nak",
            ["sinkhole"] = "defuh-burz-dak-ti",
            ["maw"] = "narg-ik-ti",
            ["northward"] = "ur-doku-goth-surg-lag",
            ["rough"] = "brakuk-dak-thrum-ti",
            ["sketch"] = "nu-brak-burz-mauk-hek-narg-bib",
            ["route"] = "lag",
            ["label"] = "mog-narg-narg-bib",
            ["precise"] = "grak-nak-ti-zorn",
            ["suggest"] = "nargu-thog-var",
            ["rendezvous"] = "mokru-dak",
            ["script"] = "bib-narg",
            ["text"] = "bib-narg"
        };

        foreach (var pair in expected)
        {
            var results = OrcishTranslatorUtility.TranslateEnglishToOrcish(pair.Key);
            AssertTrue(results.Any(candidate => candidate.Translation == pair.Value), $"unexpected translation for {pair.Key}");
        }

        var sharedForms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["braku"] = "break",
            ["vrak-lag"] = "leg",
            ["hek-grum-morz"] = "labor",
            ["lag"] = "route",
            ["mokru-dak"] = "rendezvous"
        };

        foreach (var pair in sharedForms)
        {
            var meanings = OrcishTranslatorUtility.TranslateOrcishToEnglish(pair.Key);
            AssertTrue(meanings.Any(candidate => candidate.Translation == pair.Value), $"reverse {pair.Key} form should include {pair.Value}");
        }

        var writtenMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("bib-narg", partOfSpeech: "noun");
        AssertTrue(writtenMeanings.Any(static candidate => candidate.Translation == "script"), "reverse bib-narg form should include script");
        AssertTrue(writtenMeanings.Any(static candidate => candidate.Translation == "text"), "reverse bib-narg form should include text");

        var textEntry = OrcishTranslatorUtility.GetLexiconEntries()
            .Single(static entry => string.Equals(entry.English, "text", StringComparison.OrdinalIgnoreCase));
        var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(textEntry)
            .Where(static issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        AssertEqual(0, reviewIssues.Length, "the intentional text/script shared form should remain admissible");
    }

    internal static void OrcishTranslatorPropagatesRepairedRootsThroughDerivedFamilies()
    {
        AssertEqual("dakur-hrowkuk gash-lag", OrcishTranslatorUtility.TranslateEnglishToOrcish("day's march", partOfSpeech: "noun", requiredTags: ["root-repaired"])[0].Translation, "unexpected repaired day's march translation");
        AssertEqual("ut-dravku", OrcishTranslatorUtility.TranslateEnglishToOrcish("retake", partOfSpeech: "verb", requiredTags: ["reclaim", "root-repaired"])[0].Translation, "unexpected repaired retake translation");
        AssertEqual("mokrai", OrcishTranslatorUtility.TranslateEnglishToOrcish("allies", partOfSpeech: "noun", requiredTags: ["base-ally", "plural", "root-repaired"])[0].Translation, "unexpected repaired allies translation");
        AssertEqual("mokh-zogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("families", partOfSpeech: "noun", requiredTags: ["base-family", "plural", "root-repaired"])[0].Translation, "unexpected repaired families translation");
        AssertEqual("noglar-grak", OrcishTranslatorUtility.TranslateEnglishToOrcish("secretly", partOfSpeech: "adverb", requiredTags: ["base-secret", "root-repaired"])[0].Translation, "unexpected repaired secretly translation");
        AssertEqual("darg-dakuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("station's", partOfSpeech: "noun", requiredTags: ["base-station", "possessive", "root-repaired"])[0].Translation, "unexpected repaired station possessive translation");
        AssertEqual("darg-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("stations", partOfSpeech: "noun", requiredTags: ["base-station", "s-form", "root-repaired"])[0].Translation, "unexpected repaired stations translation");
        AssertEqual("kelnib-in", OrcishTranslatorUtility.TranslateEnglishToOrcish("paling", partOfSpeech: "verb", requiredTags: ["base-pale", "progressive", "root-repaired"])[0].Translation, "unexpected repaired paling translation");
        AssertEqual("darg-dakash", OrcishTranslatorUtility.TranslateEnglishToOrcish("stationed", partOfSpeech: "verb", requiredTags: ["base-station", "past", "root-repaired"])[0].Translation, "unexpected repaired stationed translation");
    }

    internal static void OrcishTranslatorAuditsReviewPromotedDerivedFamilies()
    {
        AssertEqual("vargur", OrcishTranslatorUtility.TranslateEnglishToOrcish("lets", partOfSpeech: "verb", requiredTags: ["base-let", "derived-audited"])[0].Translation, "unexpected audited lets translation");
        AssertEqual("margiuk-grod-krag", OrcishTranslatorUtility.TranslateEnglishToOrcish("man’s", partOfSpeech: "noun", requiredTags: ["base-man", "derived-audited"])[0].Translation, "unexpected audited man's translation");
        AssertEqual("oglurin", OrcishTranslatorUtility.TranslateEnglishToOrcish("seeing", partOfSpeech: "verb", requiredTags: ["base-see", "derived-audited"])[0].Translation, "unexpected audited seeing translation");
        AssertEqual("gorin", OrcishTranslatorUtility.TranslateEnglishToOrcish("watching", partOfSpeech: "verb", requiredTags: ["base-watch", "derived-audited"])[0].Translation, "unexpected audited watching translation");
        AssertEqual("hrowk-khal-thrumuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("bag's", partOfSpeech: "noun", requiredTags: ["base-bag", "derived-audited"])[0].Translation, "unexpected audited bag possessive translation");
        AssertEqual("oglar-krubuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("eye's", partOfSpeech: "noun", requiredTags: ["base-eye", "derived-audited"])[0].Translation, "unexpected audited eye possessive translation");
        AssertEqual("dok-ka-burz-bantuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("tether's", partOfSpeech: "noun", requiredTags: ["base-tether", "derived-audited"])[0].Translation, "unexpected audited tether possessive translation");
        AssertEqual("dug-agh-ash-ash-dokuuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("trio's", partOfSpeech: "noun", requiredTags: ["base-trio", "derived-audited"])[0].Translation, "unexpected audited trio possessive translation");
        AssertEqual("dornukikash", OrcishTranslatorUtility.TranslateEnglishToOrcish("hushed", partOfSpeech: "verb", requiredTags: ["base-hush", "derived-audited"])[0].Translation, "unexpected audited hushed translation");
    }

    internal static void OrcishTranslatorShortensMechanicallyLengthenedForms()
    {
        AssertEqual("dak-mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("area", partOfSpeech: "noun", requiredTags: ["area", "shortened"])[0].Translation, "unexpected shortened area translation");
        AssertEqual("dak-mokhi", OrcishTranslatorUtility.TranslateEnglishToOrcish("areas", partOfSpeech: "noun", requiredTags: ["base-area", "shortened"])[0].Translation, "unexpected shortened areas translation");
        AssertEqual("kaag-thogash", OrcishTranslatorUtility.TranslateEnglishToOrcish("smelled", partOfSpeech: "verb", requiredTags: ["base-smell", "shortened"])[0].Translation, "unexpected shortened smelled translation");
        AssertEqual("oglar-gashash", OrcishTranslatorUtility.TranslateEnglishToOrcish("aimed", partOfSpeech: "verb", requiredTags: ["base-aim", "shortened"])[0].Translation, "unexpected shortened aimed translation");
        AssertEqual("oglar-gashin", OrcishTranslatorUtility.TranslateEnglishToOrcish("aiming", partOfSpeech: "verb", requiredTags: ["base-aim", "shortened"])[0].Translation, "unexpected shortened aiming translation");
        AssertEqual("narg-bib-zol", OrcishTranslatorUtility.TranslateEnglishToOrcish("stylus", partOfSpeech: "noun", requiredTags: ["writing", "shortened"])[0].Translation, "unexpected shortened stylus translation");

        var huntResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("hunt", partOfSpeech: "verb", requiredTags: ["hunt"]);
        AssertEqual(1, huntResults.Count, "hunt should not retain a generated duplicate translation");
        AssertEqual("gash-lag-mokh", huntResults[0].Translation, "unexpected hunt translation");
    }

    internal static void OrcishTranslatorDerivesPredictableMorphologyByRule()
    {
        AssertEqual("dak-mokhi", OrcishTranslatorUtility.TranslateEnglishToOrcish("areas", partOfSpeech: "noun", requiredTags: ["derived-by-rule", "plural"])[0].Translation, "areas should be generated from the area root");
        AssertEqual("hrowkur", OrcishTranslatorUtility.TranslateEnglishToOrcish("carries", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "present"])[0].Translation, "carries should be generated from the carry root");
        AssertEqual("hrowkash", OrcishTranslatorUtility.TranslateEnglishToOrcish("carried", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "past"])[0].Translation, "carried should be generated from the carry root");
        AssertEqual("hrowkin", OrcishTranslatorUtility.TranslateEnglishToOrcish("carrying", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "progressive"])[0].Translation, "carrying should be generated from the carry root");
        AssertEqual("nargur-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("acknowledges", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "present"])[0].Translation, "acknowledges should inflect the first root in its compound");
        AssertEqual("nargash-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("acknowledged", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "past"])[0].Translation, "acknowledged should inflect the first root in its compound");
        AssertEqual("oglar-gashash", OrcishTranslatorUtility.TranslateEnglishToOrcish("aimed", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "past"])[0].Translation, "aimed should be generated from the aim verb root");
        AssertEqual("oglar-gashin", OrcishTranslatorUtility.TranslateEnglishToOrcish("aiming", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "progressive"])[0].Translation, "aiming should be generated from the aim verb root");
    }

    internal static void OrcishTranslatorCullsLowValueExonymPassThroughs()
    {
        foreach (var culled in new[] { "aac", "abby", "archontos", "atk", "lexie", "rosk", "sulla", "vul's" })
        {
            AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish(culled).Count, $"low-value exonym '{culled}' should be culled");
        }

        AssertEqual("Kirkilston", OrcishTranslatorUtility.TranslateEnglishToOrcish("Kirkilston", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "intentional Kirkilston exonym should remain");
        AssertEqual("Xavin", OrcishTranslatorUtility.TranslateEnglishToOrcish("Xavin", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "intentional Xavin exonym should remain");
        AssertEqual("Kelpie", OrcishTranslatorUtility.TranslateEnglishToOrcish("Kelpie", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "intentional Kelpie exonym should remain");
    }

    internal static void OrcishTranslatorEnforcesLexiconQualityInvariants()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries();
        var duplicateSignatures = entries
            .GroupBy(
                static entry => $"{entry.English}\u001F{entry.Orcish}\u001F{entry.PartOfSpeech}",
                StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => FormatLexiconEntry(group.First()))
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AssertEqual(string.Empty, string.Join("; ", duplicateSignatures), "lexicon should not contain exact duplicate English/Orcish/part-of-speech entries");

        var singleWordEnglishWithOrcishSpaces = entries
            .Where(static entry => IsSingleWord(entry.English))
            .Where(static entry => entry.Orcish.Contains(' '))
            .Where(static entry => !HasAnyTag(entry, "fixed-phrase", "proper-noun", "exonym"))
            .Select(static entry => FormatLexiconEntry(entry))
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AssertEqual(string.Empty, string.Join("; ", singleWordEnglishWithOrcishSpaces), "single-word English entries should not translate to Orcish phrases without an explicit phrase/name tag");

        var entriesWithDigits = entries
            .Where(static entry => entry.Orcish.Any(char.IsDigit))
            .Select(static entry => FormatLexiconEntry(entry))
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AssertEqual(string.Empty, string.Join("; ", entriesWithDigits), "Orcish translations should not contain digits");

        var placeholderSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bbc",
            "dbe",
            "dca",
            "dcbi",
            "fcb",
            "fcd",
            "fce"
        };
        var entriesWithPlaceholderSegments = entries
            .Where(entry => SplitOrcishSegments(entry.Orcish).Any(placeholderSegments.Contains))
            .Select(static entry => FormatLexiconEntry(entry))
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AssertEqual(string.Empty, string.Join("; ", entriesWithPlaceholderSegments), "Orcish translations should not contain placeholder-looking generated segments");

        var unapprovedPassThroughs = entries
            .Where(static entry => string.Equals(entry.English, entry.Orcish, StringComparison.OrdinalIgnoreCase))
            .Where(static entry => !HasAnyTag(entry, "proper-noun", "exonym", "game-term"))
            .Select(static entry => FormatLexiconEntry(entry))
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AssertEqual(string.Empty, string.Join("; ", unapprovedPassThroughs), "direct pass-through translations should be approved as proper nouns, exonyms, or game terms");

        var generatedPassThroughs = entries
            .Where(static entry => string.Equals(entry.English, entry.Orcish, StringComparison.OrdinalIgnoreCase))
            .Where(static entry => HasAnyTag(entry, "generated"))
            .Where(static entry => !HasAnyTag(entry, "keep-exonym", "keep-lore-term", "orc-origin"))
            .Select(static entry => FormatLexiconEntry(entry))
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AssertEqual(string.Empty, string.Join("; ", generatedPassThroughs), "generated direct pass-through translations should be explicitly kept or removed");
    }

    internal static void OrcishTranslatorReviewsProposedLexiconAdditions()
    {
        var existingEntries = new OrcishLexiconEntry[]
        {
            new("hello", "zug", PartOfSpeech: "interjection"),
            new("carry", "hrowku", PartOfSpeech: "verb", Tags: ["infinitive"]),
            new("stone", "krag", PartOfSpeech: "noun")
        };

        var reverseCollisionIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
            new OrcishLexiconEntry("greeting", "zug", PartOfSpeech: "noun"),
            existingEntries);
        AssertTrue(
            reverseCollisionIssues.Any(static issue => issue.Code == "orcish-form-collision"),
            "a proposed Orcish form already used by another English term should be rejected");

        var closeFormIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
            new OrcishLexiconEntry("shout", "zugg", PartOfSpeech: "verb"),
            existingEntries);
        AssertTrue(
            closeFormIssues.Any(static issue => issue.Code == "close-form-conflict"),
            "an easily confused Orcish form should require explicit review");

        var wrongRootIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
            new OrcishLexiconEntry(
                "carried",
                "murkash",
                PartOfSpeech: "verb",
                Tags: ["root-derived", "base-carry", "past"]),
            existingEntries);
        AssertTrue(
            wrongRootIssues.Any(static issue => issue.Code == "root-morphology-mismatch"),
            "a derived form that abandons its declared root should be rejected");

        var faithfulRootIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
            new OrcishLexiconEntry(
                "carried",
                "hrowkash",
                PartOfSpeech: "verb",
                Tags: ["root-derived", "base-carry", "past"]),
            existingEntries);
        AssertEqual(0, faithfulRootIssues.Count, "a faithful rule-derived root form should pass review");

        var compoundIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
            new OrcishLexiconEntry("stone road", "krag-lag", PartOfSpeech: "noun", Tags: ["compound"]),
            existingEntries);
        AssertTrue(
            compoundIssues.Any(static issue => issue.Code == "compound-root-review-required"),
            "a compound should identify a source root or record explicit manual review");

        var reviewedSharedFormIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
            new OrcishLexiconEntry("greeting", "zug", PartOfSpeech: "noun", Tags: ["shared-form"]),
            existingEntries);
        AssertFalse(
            reviewedSharedFormIssues.Any(static issue => issue.Code == "orcish-form-collision"),
            "an intentional shared reverse form should be accepted only with an explicit review tag");

        var repeatedCharacterIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
            new OrcishLexiconEntry("coool", "grod", PartOfSpeech: "adjective"),
            existingEntries);
        AssertTrue(
            repeatedCharacterIssues.Any(static issue => issue.Code == "repeated-character-approval-required"),
            "three consecutive copies of one character should require explicit user approval");

        var approvedRepeatedCharacterIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
            new OrcishLexiconEntry(
                "coool",
                "grod",
                PartOfSpeech: "adjective",
                Tags: ["repeated-character-user-approved"]),
            existingEntries);
        AssertFalse(
            approvedRepeatedCharacterIssues.Any(static issue => issue.Code == "repeated-character-approval-required"),
            "the explicit user-approval tag should admit a repeated-character candidate");

        AssertThrows<InvalidOperationException>(() =>
            OrcishLexiconReviewUtility.EnsureCanAdd(
                new OrcishLexiconEntry("greeting", "zug", PartOfSpeech: "noun"),
                existingEntries));
    }

    private static IEnumerable<string> SplitOrcishSegments(string orcish)
    {
        return orcish
            .Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    internal static void OrcishTranslatorSupportsTenPageWikiSampleVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "ten-page-sample"))
            .ToArray();

        AssertEqual(230, entries.Length, "expected every candidate from the ten-page wiki sample");
        foreach (var entry in entries)
        {
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsNearKinMorphologyFamilies()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "near-kin"))
            .Where(entry => !HasAnyTag(entry, "fifteen-page-near-kin", "twenty-page-near-kin", "thirty-page-near-kin", "thirty-page-followup-near-kin", "sixty-seven-page-near-kin", "second-thirty-page-near-kin", "fifty-page-near-kin", "second-fifty-page-near-kin", "third-fifty-page-near-kin", "all-remaining-page-near-kin", "shadowdim-blog-near-kin", "blog-followup-near-kin", "blog-high-yield-near-kin", "blog-mixed-high-yield-near-kin", "blog-random-hundred-near-kin", "blog-final-sitemap-near-kin", "gutenberg-corpus-near-kin", "gutenberg-second-corpus-near-kin", "gutenberg-third-corpus-near-kin", "gutenberg-fourth-corpus-near-kin", "gutenberg-fifth-5000-near-kin", "gutenberg-sixth-5500-near-kin", "standard-ebooks-corpus-near-kin", "various-ebooks-1000-near-kin", "various-ebooks-second-1000-near-kin", "various-ebooks-third-1500-near-kin", "various-ebooks-fourth-5100-near-kin"))
            .ToArray();

        AssertEqual(302, entries.Length, "expected every candidate from the 139 near-kin families");
        AssertEqual(
            139,
            entries.SelectMany(entry => entry.Tags ?? Array.Empty<string>())
                .Where(tag => tag.StartsWith("family-", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            "expected all near-kin source families");

        foreach (var entry in entries)
        {
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }

        var urgedRoot = OrcishTranslatorUtility.TranslateEnglishToOrcish("urged").Single().Translation;
        AssertTrue(
            OrcishTranslatorUtility.TranslateEnglishToOrcish("urge").Single().Translation.StartsWith(urgedRoot, StringComparison.OrdinalIgnoreCase),
            "urge should retain the urged family root");
        AssertTrue(
            OrcishTranslatorUtility.TranslateEnglishToOrcish("urging").Single().Translation.StartsWith(urgedRoot, StringComparison.OrdinalIgnoreCase),
            "urging should retain the urged family root");

        var sinkingRoot = OrcishTranslatorUtility.TranslateEnglishToOrcish("sinking").Single().Translation;
        foreach (var form in new[] { "sink", "sank", "sunk" })
        {
            AssertTrue(
                OrcishTranslatorUtility.TranslateEnglishToOrcish(form).Single().Translation.StartsWith(sinkingRoot, StringComparison.OrdinalIgnoreCase),
                $"{form} should retain the sinking family root");
        }
    }

    internal static void OrcishTranslatorSupportsFifteenPageSampleVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "fifteen-page-sample", "fifteen-page-near-kin"))
            .ToArray();

        AssertEqual(932, entries.Length, "expected every candidate from the fifteen-page sample expansion");
        AssertEqual(329, entries.Count(entry => HasAnyTag(entry, "fifteen-page-sample")), "expected the scraped source candidates");
        AssertEqual(603, entries.Count(entry => HasAnyTag(entry, "fifteen-page-near-kin")), "expected the near-kin candidates");
        AssertEqual(
            308,
            entries.SelectMany(entry => entry.Tags ?? Array.Empty<string>())
                .Where(tag => tag.StartsWith("family-", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            "expected every reconstructed word family");

        foreach (var entry in entries)
        {
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }

        AssertTrue(
            new[] { "taken", "took" }.Select(term => entries.Single(entry => string.Equals(entry.English, term, StringComparison.OrdinalIgnoreCase)))
                .All(entry => HasAnyTag(entry, "family-taken")),
            "taken and took should preserve one Orcish family");
        AssertTrue(
            new[] { "tragedy", "tragedies" }.Select(term => entries.Single(entry => string.Equals(entry.English, term, StringComparison.OrdinalIgnoreCase)))
                .All(entry => HasAnyTag(entry, "family-tragedy")),
            "tragedy and tragedies should preserve one Orcish family");
    }

    internal static void OrcishTranslatorSupportsTwentyPageSampleVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "twenty-page-sample", "twenty-page-near-kin"))
            .ToArray();

        AssertEqual(505, entries.Length, "expected every candidate from the twenty-page sample expansion");
        AssertEqual(200, entries.Count(entry => HasAnyTag(entry, "twenty-page-sample")), "expected the scraped source candidates");
        AssertEqual(305, entries.Count(entry => HasAnyTag(entry, "twenty-page-near-kin")), "expected the near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }

        var abstinenceRoot = entries.Single(entry => string.Equals(entry.English, "abstinence", StringComparison.OrdinalIgnoreCase)).Orcish;
        foreach (var form in new[] { "abstain", "abstained", "abstaining", "abstains", "abstinences" })
        {
            AssertTrue(
                entries.Single(entry => string.Equals(entry.English, form, StringComparison.OrdinalIgnoreCase))
                    .Orcish.StartsWith(abstinenceRoot, StringComparison.OrdinalIgnoreCase),
                $"{form} should retain the abstinence family root");
        }

        var springRoot = entries.Single(entry => string.Equals(entry.English, "spring", StringComparison.OrdinalIgnoreCase)).Orcish;
        foreach (var form in new[] { "sprang", "springing", "springs", "sprung" })
        {
            AssertTrue(
                entries.Single(entry => string.Equals(entry.English, form, StringComparison.OrdinalIgnoreCase))
                    .Orcish.StartsWith(springRoot, StringComparison.OrdinalIgnoreCase),
                $"{form} should retain the spring family root");
        }
    }

    internal static void OrcishTranslatorSupportsThirtyPageSampleVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "thirty-page-sample", "thirty-page-near-kin"))
            .ToArray();

        AssertEqual(1593, entries.Length, "expected every candidate from the thirty-page sample expansion");
        AssertEqual(701, entries.Count(entry => HasAnyTag(entry, "thirty-page-sample")), "expected the scraped source candidates");
        AssertEqual(892, entries.Count(entry => HasAnyTag(entry, "thirty-page-near-kin")), "expected the near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }

        AssertThirtyPageFamilyRoot(entries, "alloys", "alloy", "alloyed", "alloying");
        AssertThirtyPageFamilyRoot(entries, "fed", "feed", "feeding", "feeds");
        AssertThirtyPageFamilyRoot(entries, "struck", "strikes");
        AssertThirtyPageFamilyRoot(entries, "zone", "zoned", "zones", "zoning");
    }

    internal static void OrcishTranslatorSupportsThirtyPageFollowupVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "thirty-page-followup", "thirty-page-followup-near-kin"))
            .ToArray();

        AssertEqual(862, entries.Length, "expected every candidate from the thirty page followup expansion");
        AssertEqual(253, entries.Count(entry => HasAnyTag(entry, "thirty-page-followup")), "expected the scraped source candidates");
        AssertEqual(609, entries.Count(entry => HasAnyTag(entry, "thirty-page-followup-near-kin")), "expected the near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsSixtySevenPageSampleVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "sixty-seven-page-sample", "sixty-seven-page-near-kin"))
            .ToArray();

        AssertEqual(2257, entries.Length, "expected every candidate from the sixty seven page sample expansion");
        AssertEqual(760, entries.Count(entry => HasAnyTag(entry, "sixty-seven-page-sample")), "expected the scraped source candidates");
        AssertEqual(1497, entries.Count(entry => HasAnyTag(entry, "sixty-seven-page-near-kin")), "expected the near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }

    }

    internal static void OrcishTranslatorSupportsSecondThirtyPageSampleVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "second-thirty-page-sample", "second-thirty-page-near-kin"))
            .ToArray();

        AssertEqual(2672, entries.Length, "expected every candidate from the second thirty page sample expansion");
        AssertEqual(875, entries.Count(entry => HasAnyTag(entry, "second-thirty-page-sample")), "expected the scraped source candidates");
        AssertEqual(1797, entries.Count(entry => HasAnyTag(entry, "second-thirty-page-near-kin")), "expected the near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsFiftyPageSampleVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "fifty-page-sample", "fifty-page-near-kin"))
            .ToArray();

        AssertEqual(1715, entries.Length, "expected every candidate from the fifty page sample expansion");
        AssertEqual(599, entries.Count(entry => HasAnyTag(entry, "fifty-page-sample")), "expected the scraped source candidates");
        AssertEqual(1116, entries.Count(entry => HasAnyTag(entry, "fifty-page-near-kin")), "expected the near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsSecondFiftyPageSampleVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "second-fifty-page-sample", "second-fifty-page-near-kin"))
            .ToArray();

        AssertEqual(1751, entries.Length, "expected every retained candidate from the second fifty page sample expansion");
        AssertEqual(611, entries.Count(entry => HasAnyTag(entry, "second-fifty-page-sample")), "expected the retained scraped source candidates");
        AssertEqual(1140, entries.Count(entry => HasAnyTag(entry, "second-fifty-page-near-kin")), "expected the retained near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsThirdFiftyPageSampleVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "third-fifty-page-sample", "third-fifty-page-near-kin"))
            .ToArray();

        AssertEqual(551, entries.Length, "expected every candidate from the third fifty page sample expansion");
        AssertEqual(219, entries.Count(entry => HasAnyTag(entry, "third-fifty-page-sample")), "expected the scraped source candidates");
        AssertEqual(332, entries.Count(entry => HasAnyTag(entry, "third-fifty-page-near-kin")), "expected the near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsAllRemainingPageSampleVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "all-remaining-page-sample", "all-remaining-page-near-kin"))
            .ToArray();

        AssertEqual(7681, entries.Length, "expected every retained candidate from the all remaining page sample expansion");
        AssertEqual(2830, entries.Count(entry => HasAnyTag(entry, "all-remaining-page-sample")), "expected the retained scraped source candidates");
        AssertEqual(4851, entries.Count(entry => HasAnyTag(entry, "all-remaining-page-near-kin")), "expected the retained near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsShadowdimBlogCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "shadowdim-blog-candidate-batch", "shadowdim-blog-near-kin"))
            .ToArray();

        AssertEqual(1006, entries.Length, "expected every candidate from the Shadowdim blog batch");
        AssertEqual(395, entries.Count(entry => HasAnyTag(entry, "shadowdim-blog-candidate-batch")), "expected the scraped source candidates");
        AssertEqual(611, entries.Count(entry => HasAnyTag(entry, "shadowdim-blog-near-kin")), "expected the near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsBlogFollowupCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "blog-followup-candidate-batch", "blog-followup-near-kin"))
            .ToArray();

        AssertEqual(1128, entries.Length, "expected every candidate from the blog follow-up batch");
        AssertEqual(437, entries.Count(entry => HasAnyTag(entry, "blog-followup-candidate-batch")), "expected the scraped source candidates");
        AssertEqual(691, entries.Count(entry => HasAnyTag(entry, "blog-followup-near-kin")), "expected the near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsBlogHighYieldCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "blog-high-yield-candidate-batch", "blog-high-yield-near-kin"))
            .ToArray();

        AssertEqual(1771, entries.Length, "expected every retained candidate from the blog high-yield batch");
        AssertEqual(793, entries.Count(entry => HasAnyTag(entry, "blog-high-yield-candidate-batch")), "expected the retained scraped source candidates");
        AssertEqual(978, entries.Count(entry => HasAnyTag(entry, "blog-high-yield-near-kin")), "expected the retained near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsBlogMixedHighYieldCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "blog-mixed-high-yield-candidate-batch", "blog-mixed-high-yield-near-kin"))
            .ToArray();

        AssertEqual(785, entries.Length, "expected every candidate from the blog mixed high-yield batch");
        AssertEqual(373, entries.Count(entry => HasAnyTag(entry, "blog-mixed-high-yield-candidate-batch")), "expected the scraped source candidates");
        AssertEqual(412, entries.Count(entry => HasAnyTag(entry, "blog-mixed-high-yield-near-kin")), "expected the near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsBlogRandomHundredCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "blog-random-hundred-candidate-batch", "blog-random-hundred-near-kin"))
            .ToArray();

        AssertEqual(310, entries.Length, "expected every candidate from the random hundred-page blog batch");
        AssertEqual(142, entries.Count(entry => HasAnyTag(entry, "blog-random-hundred-candidate-batch")), "expected the scraped source candidates");
        AssertEqual(168, entries.Count(entry => HasAnyTag(entry, "blog-random-hundred-near-kin")), "expected the near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsBlogFinalSitemapCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "blog-final-sitemap-candidate-batch", "blog-final-sitemap-near-kin"))
            .ToArray();

        AssertEqual(45, entries.Length, "expected every candidate from the final blog sitemap batch");
        AssertEqual(22, entries.Count(entry => HasAnyTag(entry, "blog-final-sitemap-candidate-batch")), "expected the scraped source candidates");
        AssertEqual(23, entries.Count(entry => HasAnyTag(entry, "blog-final-sitemap-near-kin")), "expected the near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)), $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsGutenbergCorpusCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "gutenberg-corpus-candidate-batch", "gutenberg-corpus-near-kin"))
            .ToArray();

        AssertEqual(2446, entries.Length, "expected every retained candidate from the Gutenberg corpus batch");
        AssertEqual(885, entries.Count(entry => HasAnyTag(entry, "gutenberg-corpus-candidate-batch")), "expected the retained corpus source candidates");
        AssertEqual(1561, entries.Count(entry => HasAnyTag(entry, "gutenberg-corpus-near-kin")), "expected the retained near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)), $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsGutenbergCorpusSecondCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "gutenberg-second-corpus-candidate-batch", "gutenberg-second-corpus-near-kin"))
            .ToArray();

        AssertEqual(456, entries.Length, "expected every retained candidate from the second Gutenberg corpus batch");
        AssertEqual(153, entries.Count(entry => HasAnyTag(entry, "gutenberg-second-corpus-candidate-batch")), "expected the retained second corpus source candidates");
        AssertEqual(303, entries.Count(entry => HasAnyTag(entry, "gutenberg-second-corpus-near-kin")), "expected the retained second corpus near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)), $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsGutenbergCorpusThirdCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "gutenberg-third-corpus-candidate-batch", "gutenberg-third-corpus-near-kin"))
            .ToArray();

        AssertEqual(73, entries.Length, "expected every candidate from the third Gutenberg corpus batch");
        AssertEqual(25, entries.Count(entry => HasAnyTag(entry, "gutenberg-third-corpus-candidate-batch")), "expected the third corpus source candidates");
        AssertEqual(48, entries.Count(entry => HasAnyTag(entry, "gutenberg-third-corpus-near-kin")), "expected the third corpus near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)), $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsGutenbergCorpusFourthCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "gutenberg-fourth-corpus-candidate-batch", "gutenberg-fourth-corpus-near-kin"))
            .ToArray();

        AssertEqual(3476, entries.Length, "expected every retained candidate from the fourth Gutenberg corpus batch");
        AssertEqual(1278, entries.Count(entry => HasAnyTag(entry, "gutenberg-fourth-corpus-candidate-batch")), "expected the retained fourth corpus source candidates");
        AssertEqual(2198, entries.Count(entry => HasAnyTag(entry, "gutenberg-fourth-corpus-near-kin")), "expected the retained fourth corpus near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)), $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsStandardEbooksCorpusCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "standard-ebooks-corpus-candidate-batch", "standard-ebooks-corpus-near-kin"))
            .ToArray();

        AssertEqual(13, entries.Length, "expected every candidate from the Standard Ebooks corpus batch");
        AssertEqual(5, entries.Count(entry => HasAnyTag(entry, "standard-ebooks-corpus-candidate-batch")), "expected the Standard Ebooks source candidates");
        AssertEqual(8, entries.Count(entry => HasAnyTag(entry, "standard-ebooks-corpus-near-kin")), "expected the Standard Ebooks near-kin candidates");

        foreach (var entry in entries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)), $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsVariousEbooksCorpusCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "various-ebooks-1000-candidate-batch", "various-ebooks-1000-near-kin"))
            .ToArray();

        AssertEqual(8684, entries.Length, "expected every candidate from the various-ebooks corpus batch");
        AssertEqual(3602, entries.Count(entry => HasAnyTag(entry, "various-ebooks-1000-candidate-batch")), "expected the various-ebooks source candidates");
        AssertEqual(5082, entries.Count(entry => HasAnyTag(entry, "various-ebooks-1000-near-kin")), "expected the various-ebooks near-kin candidates");

        foreach (var english in new[] { "aback", "zoo", "abbesses", "zoos" })
        {
            var entry = entries.Single(candidate => string.Equals(candidate.English, english, StringComparison.OrdinalIgnoreCase));
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)), $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsVariousEbooksCorpusSecondCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "various-ebooks-second-1000-candidate-batch", "various-ebooks-second-1000-near-kin"))
            .ToArray();

        AssertEqual(692, entries.Length, "expected every candidate from the second various-ebooks corpus batch");
        AssertEqual(267, entries.Count(entry => HasAnyTag(entry, "various-ebooks-second-1000-candidate-batch")), "expected the second various-ebooks source candidates");
        AssertEqual(425, entries.Count(entry => HasAnyTag(entry, "various-ebooks-second-1000-near-kin")), "expected the second various-ebooks near-kin candidates");

        foreach (var english in new[] { "abridged", "wordless", "abridge", "wordlessly" })
        {
            var entry = entries.Single(candidate => string.Equals(candidate.English, english, StringComparison.OrdinalIgnoreCase));
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)), $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsVariousEbooksCorpusThirdCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "various-ebooks-third-1500-candidate-batch", "various-ebooks-third-1500-near-kin"))
            .ToArray();

        AssertEqual(1771, entries.Length, "expected every candidate from the third various-ebooks corpus batch");
        AssertEqual(765, entries.Count(entry => HasAnyTag(entry, "various-ebooks-third-1500-candidate-batch")), "expected the third various-ebooks source candidates");
        AssertEqual(1006, entries.Count(entry => HasAnyTag(entry, "various-ebooks-third-1500-near-kin")), "expected the third various-ebooks near-kin candidates");

        foreach (var english in new[] { "abaft", "zodiac", "abdicate", "zodiacs" })
        {
            var entry = entries.Single(candidate => string.Equals(candidate.English, english, StringComparison.OrdinalIgnoreCase));
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)), $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsVariousEbooksCorpusFourthCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "various-ebooks-fourth-5100-candidate-batch", "various-ebooks-fourth-5100-near-kin"))
            .ToArray();

        AssertEqual(12452, entries.Length, "expected every candidate from the fourth various-ebooks corpus batch");
        AssertEqual(6257, entries.Count(entry => HasAnyTag(entry, "various-ebooks-fourth-5100-candidate-batch")), "expected the fourth various-ebooks source candidates");
        AssertEqual(6195, entries.Count(entry => HasAnyTag(entry, "various-ebooks-fourth-5100-near-kin")), "expected the fourth various-ebooks near-kin candidates");

        foreach (var english in new[] { "abacus", "zounds", "abacuses", "zebra's" })
        {
            var entry = entries.Single(candidate => string.Equals(candidate.English, english, StringComparison.OrdinalIgnoreCase));
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)), $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsEarthItCaresNotVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "earth-it-cares-not"))
            .ToArray();

        AssertEqual(73, entries.Length, "expected the curated source and morphology entries from The Earth It Cares Not");

        var sourceEntries = entries
            .Where(entry => !HasAnyTag(entry, "s-form", "present", "past", "progressive"))
            .ToArray();
        AssertEqual(57, sourceEntries.Length, "expected every curated source candidate from The Earth It Cares Not");

        foreach (var entry in sourceEntries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");
        }

        foreach (var english in new[]
                 {
                     "alcohol-fueled", "backswing", "darkflame", "hasiko", "magister", "neshralk",
                     "plast", "querma", "reconstitute", "upperdark", "cuemess", "cuumess",
                     "halfling's", "dungeoneer's"
                 })
        {
            var entry = entries.Single(candidate => string.Equals(candidate.English, english, StringComparison.OrdinalIgnoreCase));
            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }

        AssertEqual(
            entries.Single(entry => string.Equals(entry.English, "cuemess", StringComparison.OrdinalIgnoreCase)).Orcish,
            entries.Single(entry => string.Equals(entry.English, "cuumess", StringComparison.OrdinalIgnoreCase)).Orcish,
            "expected the documented cuemess spelling variants to share one Orcish form");

        foreach (var derivedEnglish in new[]
                 {
                     "texts", "fine-tunes", "fine-tuned", "fine-tuning", "over-exerts", "over-exerted",
                     "over-exerting", "reappears", "reappeared", "reappearing", "reconstitutes",
                     "reconstituted", "reconstituting", "resecures", "resecured", "resecuring", "resources"
                 })
        {
            AssertTrue(
                OrcishTranslatorUtility.TranslateEnglishToOrcish(derivedEnglish).Count > 0,
                $"expected morphology to cover source form '{derivedEnglish}'");
        }
    }

    internal static void OrcishTranslatorSupportsLocalShadowdimVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "local-shadowdim"))
            .ToArray();

        AssertEqual(32, entries.Length, "expected the curated source and morphology entries from local Shadowdim Markdown");

        var sourceEnglish = new[]
        {
            "world", "section", "tools", "cultist's", "operations", "site", "snap-crack",
            "document", "dungeon's", "fighter-thief", "instructions", "lapis-lazuli-tiled",
            "logothete's", "orojiam", "over-muscled", "passphrase", "beastman's",
            "beastmen's", "caprine's", "colossai", "curation", "sources", "trapdoor's"
        };
        AssertEqual(23, sourceEnglish.Length, "expected every curated local Shadowdim source candidate");

        foreach (var english in sourceEnglish)
        {
            var entry = entries.Single(candidate => string.Equals(candidate.English, english, StringComparison.OrdinalIgnoreCase));
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }

        foreach (var derivedEnglish in new[]
                 {
                     "sections", "sites", "documents", "passphrases", "tools'", "operations'",
                     "instructions'", "colossai's", "sources'"
                 })
        {
            AssertTrue(
                entries.Any(entry => string.Equals(entry.English, derivedEnglish, StringComparison.OrdinalIgnoreCase)),
                $"expected morphology to cover local Shadowdim family form '{derivedEnglish}'");
        }

        AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("clink-clink-clink").Count, "dropped sound effect should remain untranslated");
    }

    internal static void OrcishTranslatorSupportsSomethingFoundIIVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "something-found-ii"))
            .ToArray();
        var sourceEnglish = """
    aftereffects admittedly all-encompassing ambiance arguably bearhug bloodbath bone-chilling boxy catty-corner cerebral cityscape clamshell cloudscape confounded decades-long disappointment dollop eye-catching gaggle gawking goosebumps gothic grey-black heart-reader heart-racing heart-sinking heart-stopping incomparable invasive landmass lavender-white life-ending liminal liver-spotted metamorphosis mid-stride midship mind-boggling nexus-point nonstop off-guard off-yellow otherness pencil-thin petrichor piggyback reconsider resemblance riptide roiling seabed smaller-framed soul-sinking splat squish starstruck still-standing stone-faced sulfurous thunderheads toasty toothy topsy-turvy unabated unadulterated unbearable unbelievable unbelievably unblemished unbreathable unbridled unearthly unfathomable unfazed unfiltered unheard unhinged uninhabited uninvited unleash unnaturally unorthodox unquenchable unreality unresolvable unruffled unsettlingly unspool unthreatening unwarranted veiny vegetal vertiginous vibrancy viewpoint war-ravaged warzone wasp-like water-whip wild-haired wind-rushing wine-colored world-changing world-ending worrisome yellow-gold
    """.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        AssertEqual(107, sourceEnglish.Length, "expected every curated Something Found II source candidate except dropped terms");
        foreach (var english in sourceEnglish)
        {
            AssertTrue(
                OrcishTranslatorUtility.TranslateEnglishToOrcish(english).Count > 0,
                $"expected source candidate '{english}' to translate");
        }

        var acceptedEntries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => !HasAnyTag(entry, "something-found-ii"))
            .ToList();
        foreach (var entry in entries.Where(entry => !HasAnyTag(entry, "derived-by-rule")))
        {
            var reviewIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(entry, acceptedEntries);
            AssertEqual(0, reviewIssues.Count, $"expected reviewed entry '{entry.English}' to remain admissible");
            acceptedEntries.Add(entry);
        }

        AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("zaffre").Count, "dropped zaffre candidate should remain untranslated");
        AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("white-purple").Count, "dropped white-purple candidate should remain untranslated");
        AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("youth-spirited").Count, "dropped youth-spirited candidate should remain untranslated");
    }

    internal static void OrcishTranslatorSupportsReviewedMegadungeonVocabulary()
    {
        var sourceEnglish = OrcishTranslatorUtility.GetMegadungeonsSourceCandidates();
        AssertEqual(332, sourceEnglish.Count, "expected every megadungeon candidate that passed the adoption gates");
        AssertEqual(
            sourceEnglish.Count,
            sourceEnglish.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "megadungeon source candidates should be unique");

        foreach (var english in sourceEnglish)
        {
            AssertTrue(
                OrcishTranslatorUtility.TranslateEnglishToOrcish(english).Count > 0,
                $"expected reviewed megadungeon source candidate '{english}' to translate");
        }

        foreach (var dropped in new[] { "co-adaptability", "no-face", "pornographic" })
        {
            AssertEqual(
                0,
                OrcishTranslatorUtility.TranslateEnglishToOrcish(dropped).Count,
                $"expected dropped megadungeon candidate '{dropped}' to remain untranslated");
        }

        foreach (var retainedBase in new[] { "adaptability", "alpha", "werewolf" })
        {
            AssertTrue(
                OrcishTranslatorUtility.TranslateEnglishToOrcish(retainedBase).Count > 0,
                $"expected established base term '{retainedBase}' to remain translated");
        }
    }

    internal static void OrcishTranslatorSupportsGutenbergCorpusFifthCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "gutenberg-fifth-5000-candidate-batch", "gutenberg-fifth-5000-near-kin"))
            .ToArray();

        AssertEqual(11289, entries.Length, "expected every candidate from the fifth Gutenberg corpus batch after repeated-character culling");
        AssertEqual(7940, entries.Count(entry => HasAnyTag(entry, "gutenberg-fifth-5000-candidate-batch")), "expected the fifth Gutenberg source candidates after repeated-character culling");
        AssertEqual(3349, entries.Count(entry => HasAnyTag(entry, "gutenberg-fifth-5000-near-kin")), "expected the fifth Gutenberg near-kin candidates");

        foreach (var english in new[] { "aba", "zoophytes", "abalone's", "zoophyte's" })
        {
            var entry = entries.Single(candidate => string.Equals(candidate.English, english, StringComparison.OrdinalIgnoreCase));
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)), $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorSupportsGutenbergCorpusSixthCandidateVocabulary()
    {
        var entries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => HasAnyTag(entry, "gutenberg-sixth-5500-candidate-batch", "gutenberg-sixth-5500-near-kin"))
            .ToArray();

        AssertEqual(9092, entries.Length, "expected every candidate from the sixth Gutenberg corpus batch after repeated-character culling");
        AssertEqual(7082, entries.Count(entry => HasAnyTag(entry, "gutenberg-sixth-5500-candidate-batch")), "expected the sixth Gutenberg source candidates after repeated-character culling");
        AssertEqual(2010, entries.Count(entry => HasAnyTag(entry, "gutenberg-sixth-5500-near-kin")), "expected the sixth Gutenberg near-kin candidates");

        foreach (var english in new[] { "abbey-church", "zircon", "abjection's", "zircons" })
        {
            var entry = entries.Single(candidate => string.Equals(candidate.English, english, StringComparison.OrdinalIgnoreCase));
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
            AssertTrue(translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)), $"expected '{entry.English}' to translate as '{entry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorExcludesApprovedAnachronisticFamilies()
    {
        var discarded = new[]
        {
            "academic", "academic's", "academics",
            "battery's",
            "chemical", "chemical's", "chemically", "chemicals", "chemistry", "chemistry's",
            "college", "college's", "colleges",
            "dictionaries", "dictionary", "dictionary's",
            "editor", "editor's", "editors",
            "engine", "engine's", "engines",
            "evolution", "evolution's",
            "footnote", "footnote's", "footnoted", "footnotes", "footnoting",
            "geological", "geologically",
            "gravity-chute", "gravity-chutes", "gravity-train", "moon-gravity",
            "hypothesis",
            "motor", "motor's", "motored", "motoring", "motors",
            "oxygen", "oxygen's",
            "photograph", "photograph's", "photographed", "photographer", "photographers", "photographing",
            "plastic", "plastic's", "plastics",
            "psychological", "psychologically", "psychologies", "psychology", "psychology's",
            "radiations",
            "radio", "radio's", "radioed", "radioing", "radios",
            "railroad", "railroad's", "railroaded", "railroader", "railroaders", "railroading", "railroads",
            "railway", "railway's", "railways",
            "sciences", "scientific", "scientist", "scientist's", "scientists",
            "statistic", "statistic's", "statistics",
            "telegraph", "telegraph's", "telegraphed", "telegrapher", "telegraphers", "telegraphing",
            "telephone", "telephone's", "telephoned", "telephoner", "telephoners", "telephones", "telephoning",
            "telescope", "telescope's", "telescoped", "telescopes", "telescoping",
            "automobile", "automobile's", "automobiled", "automobiles", "automobiling",
            "camera", "camera's", "cameras",
            "gasoline", "gasoline's",
            "rocket", "rocket's", "rocketed", "rocketing", "rockets",
            "thermometer", "thermometer's", "thermometers",
            "electric", "electrics", "electrical", "electrically", "electricity", "electricity's",
            "earphone", "earphones", "electrician", "gramophone", "gyroscope", "kaleidoscope", "megaphone",
            "motorboat", "motorcycle", "motorcycles", "motorist", "periscope", "periscopes", "radioactive", "telegraphy",
            "non-computerized",
            "submarine", "submarine's", "submariner", "submariners", "submarines",
            "pornography", "pornography's", "pornographic", "pornographer", "pornographers",
            "zeppelin", "zeppelins",
            "anthropologist", "anthropologists", "biologist", "ethnologist", "ethnologists", "geologists",
            "mythologists", "philologist", "philologists", "psychologist", "psychologists",
            "zoological", "zoologically", "zoologist", "zoologists", "zoology"
        };
        var terms = OrcishTranslatorUtility.GetEnglishTerms();

        foreach (var english in discarded)
        {
            AssertTrue(!terms.Contains(english, StringComparer.OrdinalIgnoreCase), $"expected discarded anachronistic term '{english}' to be absent");
            AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish(english).Count, $"expected discarded anachronistic term '{english}' not to translate");
        }
    }

    internal static void OrcishTranslatorRetainsApprovedKnowledgeVocabularyAndNounFilmSense()
    {
        var terms = OrcishTranslatorUtility.GetEnglishTerms();
        foreach (var english in new[]
        {
            "essay", "film", "frequency", "journal", "laboratory",
            "lecture", "professor", "publication", "research"
        })
        {
            AssertTrue(terms.Contains(english, StringComparer.OrdinalIgnoreCase), $"expected approved Orcish knowledge term '{english}'");
            AssertTrue(OrcishTranslatorUtility.TranslateEnglishToOrcish(english).Count > 0, $"expected approved term '{english}' to translate");
        }

        var filmEntries = OrcishTranslatorUtility.GetLexiconEntries()
            .Where(entry => new[] { "film", "film's", "films" }.Contains(entry.English, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        AssertEqual(3, filmEntries.Length, "expected only noun film forms");
        AssertTrue(filmEntries.All(entry => string.Equals(entry.PartOfSpeech, "noun", StringComparison.OrdinalIgnoreCase)), "film forms should be nouns");
        AssertTrue(filmEntries.All(entry => string.Equals(entry.GrammarClass, "substance", StringComparison.OrdinalIgnoreCase)), "film forms should mean a smear or layer");

        foreach (var verbForm in new[] { "filmed", "filming" })
        {
            AssertTrue(!terms.Contains(verbForm, StringComparer.OrdinalIgnoreCase), $"expected verb film form '{verbForm}' to be absent");
            AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish(verbForm).Count, $"expected verb film form '{verbForm}' not to translate");
        }
    }

    internal static void OrcishTranslatorSupportsSoftwareArtifactVocabulary()
    {
        var expectedEntries = new[]
        {
            new OrcishLexiconEntry("release", "dakur-nar-grod-vrak", "noun", "artifact", ["software", "compound", "compound-reviewed", "ooc"]),
            new OrcishLexiconEntry("assembly", "mokh-zorn-grod-vrak", "noun", "artifact", ["software", "compound", "compound-reviewed", "ooc"])
        };

        foreach (var expectedEntry in expectedEntries)
        {
            var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(expectedEntry)
                .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AssertEqual(0, reviewIssues.Length, $"expected reviewed software artifact entry '{expectedEntry.English}' to remain admissible");

            var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(expectedEntry.English, "noun");
            AssertTrue(
                translations.Any(candidate => string.Equals(candidate.Translation, expectedEntry.Orcish, StringComparison.OrdinalIgnoreCase)),
                $"expected '{expectedEntry.English}' to translate as '{expectedEntry.Orcish}'");
        }
    }

    internal static void OrcishTranslatorTranslatesFullTextInBothDirections()
    {
        AssertEqual(
            "Zug untranslatedword.",
            OrcishTranslatorUtility.TranslateEnglishTextToOrcish("hello untranslatedword."),
            "English text translation should translate known words and preserve unknown words");
        AssertEqual(
            "Hello untranslatedword.",
            OrcishTranslatorUtility.TranslateOrcishTextToEnglish("zug untranslatedword."),
            "Orcish text translation should translate known words and preserve unknown words");
        AssertEqual(
            "\"Zug\" ...",
            OrcishTranslatorUtility.TranslateEnglishTextToOrcish("\"hello\" ..."),
            "full-text translation should preserve punctuation-only tokens");
    }

    internal static void OrcishTranslatorWarmupIsShared()
    {
        OrcishTranslatorWarmupUtility.ResetForTests();
        using var warmupStarted = new ManualResetEventSlim();
        using var releaseWarmup = new ManualResetEventSlim();
        var warmupCount = 0;
        OrcishTranslatorWarmupUtility.WarmupOverrideForTests = () =>
        {
            Interlocked.Increment(ref warmupCount);
            warmupStarted.Set();
            if (!releaseWarmup.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("test warmup was not released");
            }

            return 80874;
        };

        try
        {
            var first = OrcishTranslatorWarmupUtility.StartPreloading();
            var second = OrcishTranslatorWarmupUtility.StartPreloading();

            AssertTrue(ReferenceEquals(first, second), "translator warmup should be shared");
            WaitForCondition(() => warmupStarted.IsSet, "translator warmup did not start");
            AssertFalse(OrcishTranslatorWarmupUtility.IsReady, "blocked translator warmup should not report ready");

            releaseWarmup.Set();
            var result = first.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

            AssertEqual(80874, result.EnglishTermCount, "unexpected warmed English term count");
            AssertEqual(1, Volatile.Read(ref warmupCount), "translator warmup should run once");
            AssertTrue(OrcishTranslatorWarmupUtility.IsReady, "completed translator warmup should report ready");
        }
        finally
        {
            releaseWarmup.Set();
            try
            {
                OrcishTranslatorWarmupUtility.StartPreloading()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
            }

            OrcishTranslatorWarmupUtility.ResetForTests();
        }
    }

    internal static void OrcishTranslatorLoadsEmbeddedSnapshot()
    {
        AssertEqual(80874, OrcishTranslatorUtility.GetEnglishTermCount(), "unexpected embedded snapshot term count");
        AssertTrue(
            OrcishLexiconSnapshotUtility.WasEmbeddedSnapshotLoaded,
            "translator should load the embedded lexicon snapshot instead of cold-JITing generated builders");
    }

    internal static void OrcishTranslatorExposesUniqueEnglishTermCount()
    {
        var terms = OrcishTranslatorUtility.GetEnglishTerms();

        AssertEqual(80874, OrcishTranslatorUtility.GetEnglishTermCount(), "unexpected total English term count");
        AssertEqual(OrcishTranslatorUtility.GetEnglishTermCount(), terms.Count, "term list and count should agree");
        AssertEqual(1, terms.Count(term => string.Equals(term, "I", StringComparison.OrdinalIgnoreCase)), "I should be counted once despite multiple variants");
        AssertEqual(1, terms.Count(term => string.Equals(term, "really", StringComparison.OrdinalIgnoreCase)), "really should be counted once despite multiple variants");
        AssertEqual(1, terms.Count(term => string.Equals(term, "watch", StringComparison.OrdinalIgnoreCase)), "watch should be counted once despite multiple parts of speech");
        AssertTrue(terms.Contains("humans'", StringComparer.OrdinalIgnoreCase), "expected generated plural possessive term");
    }

    internal static void ToOrcishTranslatesTermsBeforeTrailingPunctuation()
    {
        var result = RunToOrcish("yours,");

        AssertEqual(0, result.ExitCode, "to-orcish should exit successfully");
        AssertEqual("Narguk,", result.Output.Trim(), "expected yours to translate before comma restoration");
    }

    internal static void ToOrcishTranslatesDottedAbbreviationTerms()
    {
        var result = RunToOrcish("p.m.");

        AssertEqual(0, result.ExitCode, "to-orcish should exit successfully");
        AssertEqual("Exenda", result.Output.Trim(), "expected dotted abbreviation to translate before punctuation stripping");
    }

    internal static void ToOrcishTranslatesTermsInsideParentheses()
    {
        var result = RunToOrcish("(secret)");

        AssertEqual(0, result.ExitCode, "to-orcish should exit successfully");
        AssertEqual("(noglar)", result.Output.Trim(), "expected parenthesized terms to translate without treating parentheses as word characters");
    }

    internal static void ToOrcishTranslatesTermsInsideQuotes()
    {
        var result = RunToOrcish("\"Well met. Please.\" \"Obliged,\"");

        AssertEqual(0, result.ExitCode, "to-orcish should exit successfully");
        AssertEqual("\"Mokra-narg. Mauk-drav.\" \"Tukru-drav,\"", result.Output.Trim(), "expected quoted terms to translate without treating quotes as word characters");
    }

    internal static void ToOrcishTranslatesWordsAfterNewlines()
    {
        var result = RunToOrcish("The roads\nThe notice board");

        AssertEqual(0, result.ExitCode, "to-orcish should exit successfully");
        AssertEqual("Arhk lagi arhk narg-bib-dak", result.Output.Trim(), "expected words after newlines to translate as separate terms");
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

    private static void AssertDerivedElvenForm(string language, string root, string tag, string expected)
    {
        AssertTrue(
            ElvenMorphologyUtility.TryCreateDerivedForm(language, root, [tag], out var actual),
            $"{language} {tag} should be supported for '{root}'");
        AssertEqual(expected, actual, $"unexpected {language} {tag} for '{root}'");
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

    internal static void TranslatorControllerCancelsSupersededInjectedServiceWork()
    {
        using var service = new BlockingTranslatorService();
        var completedTranslations = new List<string>();
        using var controller = new TranslatorController(
            service,
            _ => { },
            _ => { },
            (_, _) => Task.CompletedTask,
            completedTranslations.Add);

        controller.Activate(TranslatorTargetLanguage.Orcish);
        var firstTranslation = controller.TranslateInputAsync("hello", targetToEnglish: false, inputLengthChange: 5);
        AssertTrue(service.FirstTranslationStarted.Wait(TimeSpan.FromSeconds(5)), "first injected translation did not start");

        var secondTranslation = controller.TranslateInputAsync("world", targetToEnglish: false, inputLengthChange: 5);
        Task.WhenAll(firstTranslation, secondTranslation).GetAwaiter().GetResult();

        AssertTrue(service.FirstTranslationCanceled.IsSet, "superseded injected translation was not canceled");
        AssertEqual(1, completedTranslations.Count, "only the latest translation should reach the view");
        AssertEqual("translated:world", completedTranslations[0], "latest translation was not displayed");
        AssertEqual(TranslatorTargetLanguage.Orcish, service.LastTargetLanguage, "controller did not pass its active language to the injected service");
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

}
