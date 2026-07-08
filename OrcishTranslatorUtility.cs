namespace PlayerAssistant
{
    using System.Text.RegularExpressions;

    internal enum OrcishLanguage
    {
        English,
        Orcish
    }

    internal sealed record OrcishLexiconEntry(
        string English,
        string Orcish,
        string? PartOfSpeech = null,
        string? GrammarClass = null,
        IReadOnlyList<string>? Tags = null);

    internal sealed record OrcishAffixEntry(
        string Affix,
        string AffixType,
        string Meaning,
        string? UsageNote = null,
        IReadOnlyList<string>? Tags = null);

    internal sealed record OrcishTranslationRequest(
        string Text,
        OrcishLanguage SourceLanguage,
        OrcishLanguage TargetLanguage,
        string? PartOfSpeech = null,
        IReadOnlyList<string>? RequiredTags = null);

    internal sealed record OrcishTranslationCandidate(
        string Source,
        string Translation,
        string? PartOfSpeech = null,
        string? GrammarClass = null,
        IReadOnlyList<string>? Tags = null);

    internal sealed record OrcishSequenceTranslation(
        string Source,
        string Translation,
        string? PartOfSpeech = null,
        string? GrammarClass = null,
        IReadOnlyList<string>? Tags = null);

    internal static class OrcishTranslatorUtility
    {
        private static readonly Regex EmphasizedFirstPersonPronounPattern =
            new(@"(?<!\S)I\s+\{emphasis\}(?!\S)", RegexOptions.Compiled);

        private static readonly Regex FirstPersonPronounPattern =
            new(@"(?<!\S)I(?!\S)", RegexOptions.Compiled);

        private static readonly OrcishLexiconEntry[] LexiconEntries =
            BuildLexiconEntries();

        private static readonly OrcishAffixEntry[] AffixEntries =
            BuildAffixEntries();

        private static readonly IReadOnlyDictionary<string, OrcishLexiconEntry[]> EnglishIndex =
            BuildIndex(LexiconEntries, static entry => entry.English);

        private static readonly IReadOnlyDictionary<string, OrcishLexiconEntry[]> OrcishIndex =
            BuildIndex(LexiconEntries, static entry => entry.Orcish);

        public static IReadOnlyList<OrcishTranslationCandidate> TranslateEnglishToOrcish(
            string englishText,
            string? partOfSpeech = null)
        {
            return Translate(new OrcishTranslationRequest(
                englishText,
                OrcishLanguage.English,
                OrcishLanguage.Orcish,
                partOfSpeech));
        }

        public static string TranslateEnglishTextToOrcishPronouns(string englishText)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(englishText);

            var translatedText = EmphasizedFirstPersonPronounPattern.Replace(englishText, "Grrt-Ugh");
            translatedText = FirstPersonPronounPattern.Replace(
                translatedText,
                static _ => RandomNumberUtility.GenerateInteger(0, 1) == 0 ? "Ugh" : "Grrt");

            return translatedText;
        }

        public static IReadOnlyList<OrcishTranslationCandidate> TranslateEnglishToOrcish(
            string englishText,
            string? partOfSpeech,
            IReadOnlyList<string>? requiredTags)
        {
            return Translate(new OrcishTranslationRequest(
                englishText,
                OrcishLanguage.English,
                OrcishLanguage.Orcish,
                partOfSpeech,
                requiredTags));
        }

        public static IReadOnlyList<OrcishTranslationCandidate> TranslateOrcishToEnglish(
            string orcishText,
            string? partOfSpeech,
            IReadOnlyList<string>? requiredTags)
        {
            return Translate(new OrcishTranslationRequest(
                orcishText,
                OrcishLanguage.Orcish,
                OrcishLanguage.English,
                partOfSpeech,
                requiredTags));
        }

        public static IReadOnlyList<OrcishTranslationCandidate> TranslateOrcishToEnglish(
            string orcishText,
            string? partOfSpeech = null)
        {
            return Translate(new OrcishTranslationRequest(
                orcishText,
                OrcishLanguage.Orcish,
                OrcishLanguage.English,
                partOfSpeech));
        }

        public static OrcishTranslationCandidate? TranslateEnglishToOrcishRandom(
            string englishText,
            string? partOfSpeech = null,
            IReadOnlyList<string>? requiredTags = null)
        {
            var candidates = TranslateEnglishToOrcish(englishText, partOfSpeech, requiredTags);
            return ChooseRandomCandidate(candidates);
        }

        public static OrcishTranslationCandidate? TranslateOrcishToEnglishRandom(
            string orcishText,
            string? partOfSpeech = null,
            IReadOnlyList<string>? requiredTags = null)
        {
            var candidates = TranslateOrcishToEnglish(orcishText, partOfSpeech, requiredTags);
            return ChooseRandomCandidate(candidates);
        }

        public static IReadOnlyList<OrcishSequenceTranslation> TranslateEnglishSequenceToOrcish(
            IReadOnlyList<string> englishTerms)
        {
            ArgumentNullException.ThrowIfNull(englishTerms);

            var alternationState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<OrcishSequenceTranslation>(englishTerms.Count);

            foreach (var englishTerm in englishTerms)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(englishTerm);

                var candidates = TranslateEnglishToOrcish(englishTerm);
                var selected = SelectSequenceCandidate(englishTerm, candidates, alternationState);

                results.Add(new OrcishSequenceTranslation(
                    englishTerm,
                    selected?.Translation ?? string.Empty,
                    selected?.PartOfSpeech,
                    selected?.GrammarClass,
                    selected?.Tags ?? Array.Empty<string>()));
            }

            return results;
        }

        public static int GetEnglishTermCount()
        {
            return EnglishIndex.Count;
        }

        public static IReadOnlyList<string> GetEnglishTerms()
        {
            return EnglishIndex.Keys
                .OrderBy(static term => term, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyList<OrcishAffixEntry> GetAffixEntries()
        {
            return AffixEntries;
        }

        private static OrcishLexiconEntry[] BuildLexiconEntries()
        {
            var entries = new List<OrcishLexiconEntry>
            {
                new("hello", "zug"),
                new("friend", "mokra", PartOfSpeech: "noun", GrammarClass: "kinship"),
                new("ally", "mokra", PartOfSpeech: "noun", GrammarClass: "kinship"),
                new("humanoid", "mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "species"]),
                new("human", "margi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["neutral", "species"]),
                new("man", "margi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["neutral", "species"]),
                new("inferior other", "margi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["pejorative", "outsider", "broad-gloss"]),
                new("humans", "margith", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["neutral", "species", "plural"]),
                new("men", "margith", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["neutral", "species", "plural"]),
                new("inferior others", "margith", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["pejorative", "outsider", "plural", "broad-gloss"]),
                new("obviously inferior others", "margith", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["pejorative", "outsider", "plural", "emphatic", "broad-gloss"]),
                new("human's", "margiuk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["neutral", "species", "possessive"]),
                new("man's", "margiuk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["neutral", "species", "possessive"]),
                new("softskin", "thrum-skin", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["insulting", "species"]),
                new("weak human", "thrum-skin", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["insulting", "species"]),
                new("softskins", "thrum-skinar", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["insulting", "species", "plural"]),
                new("weak humans", "thrum-skinar", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["insulting", "species", "plural"]),
                new("softskin's", "thrum-skinuk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["insulting", "species", "possessive"]),
                new("children of Gruumsh", "mogra", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["orc", "collective", "identity"]),
                new("favored children of Gruumsh", "mogra-ti", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["orc", "collective", "identity", "favored"]),
                new("superior children of Gruumsh", "mogra-ti", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["orc", "collective", "identity", "superior"]),
                new("githyanki", "githyanki", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["exonym", "historical", "orc-origin"]),
                new("one of unexpected strength", "yanki", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["historical", "unexpected-strength"]),
                new("sun-born", "surgar", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["respectful", "species"]),
                new("free human", "surgar", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["respectful", "species"]),
                new("sun-born ones", "surgari", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["respectful", "species", "plural"]),
                new("free humans", "surgari", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["respectful", "species", "plural"]),
                new("sun-born's", "surgaruk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["respectful", "species", "possessive"]),
                new("skin", "vrak", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["neutral", "default"]),
                new("skins", "vraki", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["neutral", "default", "plural"]),
                new("forest", "gruul", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["default", "wilderness"]),
                new("hedge", "vrul", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["default", "growth"]),
                new("woods", "vril", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["default", "wilderness", "plural-mass"]),
                new("path", "lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["default", "route"]),
                new("road", "lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["default", "route"]),
                new("trail", "lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["default", "route"]),
                new("paths", "lagi", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["default", "route", "plural"]),
                new("roads", "lagi", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["default", "route", "plural"]),
                new("trails", "lagi", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["default", "route", "plural"]),
                new("built road", "hek-lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["built", "route"]),
                new("built path", "hek-lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["built", "route"]),
                new("wild trail", "vril-lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["wild", "wilderness", "route"]),
                new("woods path", "vril-lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["wild", "wilderness", "route"]),
                new("Kirkilston", "Kirkilston", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement"]),
                new("Eastdale", "Eastdale", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement"]),
                new("Westkeep", "Westkeep", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement"]),
                new("Middenmark", "Middenmark", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "region"]),
                new("region", "dak-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["area", "compound"]),
                new("regional", "dak-mokhuk", PartOfSpeech: "adjective", GrammarClass: "place", Tags: ["area", "possessive-derived"]),
                new("area", "dak-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["area", "compound"]),
                new("surrounding area", "nak-dak-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["nearby", "area", "fixed-phrase"]),
                new("immediate surrounding area", "grak-nak-dak-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["nearby", "immediate", "area", "fixed-phrase"]),
                new("vicinity", "nak-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["nearby", "area", "compound"]),
                new("surface", "oglar-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["visible", "above", "compound"]),
                new("subterranean", "burz", PartOfSpeech: "adjective", GrammarClass: "place", Tags: ["underground"]),
                new("near-surface", "nak-oglar-dak", PartOfSpeech: "adjective", GrammarClass: "place", Tags: ["nearby", "surface", "compound"]),
                new("place", "dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["default"]),
                new("places", "daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["default", "plural"]),
                new("center", "murk-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["central", "compound"]),
                new("centers", "murk-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["central", "plural", "compound"]),
                new("key centers", "thrak-murk-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["central", "important", "plural", "compound"]),
                new("haunt", "darg-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["held", "territory", "compound"]),
                new("haunts", "darg-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["held", "territory", "plural", "compound"]),
                new("subterranean near-surface haunts", "burz nak-oglar-dak darg-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["underground", "surface", "territory", "plural", "fixed-phrase"]),
                new("surface communities", "oglar-dak mokhi", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["surface", "inhabited", "plural", "compound"]),
                new("settlement", "mog-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["inhabited", "compound"]),
                new("dwarven settlement", "dwarf-mog-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["dwarven", "inhabited", "compound"]),
                new("church", "mograth-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["religious", "compound"]),
                new("kirk", "mograth-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["religious", "synonym", "compound"]),
                new("temple", "mograth-dak-ti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["religious", "major", "compound"]),
                new("Red Temple", "rug-mograth-dak-ti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "religious", "red-law", "compound"]),
                new("Watchtower", "gor-ti-hek", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["watch", "built", "compound"]),
                new("tower", "ti-hek", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["built", "tall", "compound"]),
                new("cave", "burz-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["underground", "shelter", "compound"]),
                new("caves", "burz-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["underground", "shelter", "plural", "compound"]),
                new("Glittering Caves", "Glittering Caves", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "plural"]),
                new("Forge", "Forge", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement"]),
                new("Threshold", "Threshold", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement"]),
                new("base", "mokh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["operations", "compound"]),
                new("residence", "dakku-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["dwelling", "compound"]),
                new("map", "bibnak", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["default", "text", "directional"]),
                new("maps", "bibnaki", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["default", "text", "directional", "plural"]),
                new("book", "bib", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["default", "text"]),
                new("scroll", "bib", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["default", "text"]),
                new("book-man", "bib-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "scholar", "text"]),
                new("hide", "vrak", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["neutral", "default"]),
                new("hides", "vraki", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["neutral", "default", "plural"]),
                new("hide", "drukh", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["reverent", "monster", "thick-hide"]),
                new("hides", "drukhi", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["reverent", "monster", "thick-hide", "plural"]),
                new("rope", "bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["neutral", "default"]),
                new("ropes", "banti", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["neutral", "default", "plural"]),
                new("rope's", "bantuk", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["neutral", "default", "possessive"]),
                new("builder", "hekruh", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "default"]),
                new("builders", "hekruhi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "default", "plural"]),
                new("carter", "hrowga", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "transport", "default"]),
                new("carters", "hrowgai", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "transport", "default", "plural"]),
                new("carter's", "hrowgauk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "transport", "default", "possessive"]),
                new("conveyance", "hrog", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["broad-gloss", "transport", "default"]),
                new("wagon", "hrogar", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["transport", "default"]),
                new("carrier", "hrowka", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "transport", "default"]),
                new("carriers", "hrowkai", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "transport", "default", "plural"]),
                new("map-carrier", "bibnak-hrowka", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["transport", "directional", "compound"]),
                new("map-carriers", "bibnak-hrowkai", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["transport", "directional", "compound", "plural"]),
                new("company", "mokh", PartOfSpeech: "noun", GrammarClass: "organization", Tags: ["trade", "group", "default"]),
                new("merchant", "dravik", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["trade", "default"]),
                new("merchant wagon", "dravik-hrogar", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["transport", "trade", "compound"]),
                new("merchant wagons", "dravik-hrogarai", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["transport", "trade", "compound", "plural"]),
                new("miner", "hekfa", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "labor", "default"]),
                new("miners", "hekfai", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "labor", "default", "plural"]),
                new("priest", "mograth", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "default"]),
                new("priests", "mograthi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "default", "plural"]),
                new("wandering priests", "vagor-mograthi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "wandering", "plural"]),
                new("sage", "thogmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["learned", "default"]),
                new("sages", "thogmogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["learned", "default", "plural"]),
                new("thinker", "thogmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["thoughtful", "default"]),
                new("smith", "hekruhur", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["craft", "specialized"]),
                new("smiths", "hekruhuri", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["craft", "specialized", "plural"]),
                new("traveler", "fletragi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["outsider", "wayfarer", "default"]),
                new("travelers", "fletragith", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["outsider", "wayfarer", "default", "plural"]),
                new("hedge-wizard", "gurmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "specialized"]),
                new("hedge-wizards", "gurmogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "specialized", "plural"]),
                new("wizard", "gurmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "specialized"]),
                new("wizards", "gurmogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "specialized", "plural"]),
                new("fighter", "gash", PartOfSpeech: "noun", GrammarClass: "person"),
                new("fighters", "gash", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["plural"]),
                new("warrior", "gash", PartOfSpeech: "noun", GrammarClass: "person"),
                new("Slip", "Slip", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Xavamros", "Xavamros", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Battlebeard", "Battlebeard", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Governor", "darg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["title", "ruler", "compound"]),
                new("Prince", "darg-ti-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["title", "ruler", "higher-than-governor", "compound"]),
                new("Xavin", "Xavin", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("cleric", "mograth", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "default"]),
                new("clerics", "mograthi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "default", "plural"]),
                new("orc", "orukh", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["species", "orc"]),
                new("orcs", "orukhi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["species", "orc", "plural"]),
                new("goblin", "goblin", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["species", "exonym"]),
                new("goblins", "goblini", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["species", "exonym", "plural"]),
                new("kobold", "kobold", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["species", "exonym"]),
                new("kobolds", "koboldi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["species", "exonym", "plural"]),
                new("watch", "thrak", PartOfSpeech: "noun", GrammarClass: "object"),
                new("NPC", "nul-narg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["game-term", "non-player"]),
                new("NPCs", "nul-narg-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["game-term", "non-player", "plural"]),
                new("PC", "narg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["game-term", "player"]),
                new("PCs", "narg-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["game-term", "player", "plural"]),
                new("character", "mog-var", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["game-term", "role"]),
                new("hireling", "dravik-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["paid", "helper"]),
                new("hirelings", "dravik-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["paid", "helper", "plural"]),
                new("option", "varg", PartOfSpeech: "noun", GrammarClass: "choice", Tags: ["abstract"]),
                new("need", "thruk", PartOfSpeech: "noun", GrammarClass: "requirement", Tags: ["abstract"]),
                new("needs", "thruki", PartOfSpeech: "noun", GrammarClass: "requirement", Tags: ["abstract", "plural"]),
                new("time", "dakur", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["abstract"]),
                new("times", "dakuri", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["abstract", "plural"]),
                new("century", "mur-dakur", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["long-span", "compound"]),
                new("centuries", "mur-dakuri", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["long-span", "plural", "compound"]),
                new("second century", "dug mur-dakur", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["long-span", "ordinal", "fixed-phrase"]),
                new("idea", "thog-var", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["abstract", "compound"]),
                new("nonsense", "nul-thog", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["foolish", "abstract", "compound"]),
                new("throne", "darg-thrak", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rulership", "seat", "compound"]),
                new("faith", "mograth-thog", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["belief", "compound"]),
                new("administration", "darg-bib", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["bureaucracy", "compound"]),
                new("governance", "darg-thog", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rule", "abstract", "compound"]),
                new("stance", "lag-thog", PartOfSpeech: "noun", GrammarClass: "position", Tags: ["viewpoint", "compound"]),
                new("Prelacy", "mograth-darg", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["religious", "rule", "compound"]),
                new("The Prelacy of Middenmark", "arhk mograth-darg uk Middenmark", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["proper-noun", "religious", "rule", "fixed-phrase"]),
                new("law", "darg-bib", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rule", "written", "compound"]),
                new("laws", "darg-bibi", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rule", "written", "plural", "compound"]),
                new("Red Laws", "rug-darg-bibi", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["proper-noun", "law", "red-law", "plural", "compound"]),
                new("community", "mokh", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["group", "inhabited"]),
                new("communities", "mokhi", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["group", "inhabited", "plural"]),
                new("work", "hek", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["default"]),
                new("watch", "gor", PartOfSpeech: "verb", GrammarClass: "action"),
                new("to be", "tar", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["infinitive"]),
                new("be", "tar", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["infinitive"]),
                new("is", "tur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present"]),
                new("am", "tur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present"]),
                new("are", "tur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present"]),
                new("was", "tash", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past"]),
                new("were", "tash", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past"]),
                new("have been", "tuk", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["perfect"]),
                new("is being", "turin", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["progressive", "present"]),
                new("are being", "turin", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["progressive", "present"]),
                new("will be", "taruk", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["future"]),
                new("may be", "mauk tar", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility", "permission", "state"]),
                new("may opt to be staying", "mauk vargu dakkin", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility", "choice", "location", "fixed-phrase"]),
                new("may be from", "mauk dok", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility", "origin", "fixed-phrase"]),
                new("is named after", "tur mog-nargash dok", PartOfSpeech: "verb", GrammarClass: "naming", Tags: ["present", "passive", "fixed-phrase"]),
                new("named after", "mog-nargash dok", PartOfSpeech: "verb", GrammarClass: "naming", Tags: ["past-participle", "fixed-phrase"]),
                new("is not", "notur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present", "negative"]),
                new("are not", "notur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present", "negative"]),
                new("was not", "notash", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past", "negative"]),
                new("were not", "notash", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past", "negative"]),
                new("may", "mauk", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility", "permission"]),
                new("might", "mauk", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility"]),
                new("will", "uk", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["future"]),
                new("opt", "vargu", PartOfSpeech: "verb", GrammarClass: "choice", Tags: ["infinitive"]),
                new("choose", "vargu", PartOfSpeech: "verb", GrammarClass: "choice", Tags: ["infinitive"]),
                new("use", "bruku", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["infinitive"]),
                new("uses", "brukur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["present"]),
                new("using", "brukin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["progressive", "present"]),
                new("give", "draku", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["infinitive"]),
                new("made", "hekash", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["past"]),
                new("provide", "dravku", PartOfSpeech: "verb", GrammarClass: "support", Tags: ["infinitive"]),
                new("provide", "dravur", PartOfSpeech: "verb", GrammarClass: "support", Tags: ["present", "plural-subject"]),
                new("retain", "dargu-tukra", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["authority", "infinitive", "compound"]),
                new("retains", "dargu-tukur", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["authority", "present", "compound"]),
                new("retains the throne", "dargu-tukur arhk darg-thrak", PartOfSpeech: "verb", GrammarClass: "authority", Tags: ["rulership", "possession", "fixed-phrase"]),
                new("rule", "dargu", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["infinitive", "authority"]),
                new("ruling", "dargin", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["progressive", "present", "authority"]),
                new("ruling the subterranean near-surface haunts", "dargin arhk burz nak-oglar-dak darg-daki", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["authority", "territory", "fixed-phrase"]),
                new("hold", "dargu", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["infinitive", "authority"]),
                new("holds", "dargur", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["present", "authority"]),
                new("holds sway", "dargur-ti", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["present", "authority", "fixed-phrase"]),
                new("insist", "dargu-thog", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["infinitive", "stubborn", "compound"]),
                new("insist on", "dargu-thog ak", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["infinitive", "stubborn", "fixed-phrase"]),
                new("does not", "nu", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["negative"]),
                new("doesn't", "nu", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["negative", "contraction"]),
                new("to see", "oglar", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["infinitive"]),
                new("see", "oglar", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["infinitive"]),
                new("sees", "oglur", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["present"]),
                new("saw", "oglash", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["past"]),
                new("have seen", "ogluk", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["perfect"]),
                new("is seeing", "oglurin", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["progressive", "present"]),
                new("will see", "oglaruk", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["future"]),
                new("does not see", "noglur", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["present", "negative"]),
                new("did not see", "noglash", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["past", "negative"]),
                new("provide", "dravku", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["infinitive"]),
                new("to provide", "dravku", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["infinitive"]),
                new("obtain", "dravku", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["infinitive", "acquire"]),
                new("provides", "dravur", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["present"]),
                new("provided", "dravash", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["past"]),
                new("providing", "dravin", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["progressive", "present"]),
                new("obtaining", "dravin", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["progressive", "present", "acquire"]),
                new("has", "tukur", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["present", "third-person"]),
                new("join", "mokru", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["infinitive"]),
                new("to join", "mokru", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["infinitive"]),
                new("joins", "mokrur", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["present"]),
                new("joined", "mokrash", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["past"]),
                new("joining", "mokrin", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["progressive", "present"]),
                new("escape", "varku", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive"]),
                new("escaped", "varkash", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past"]),
                new("escaping", "varkin", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "present"]),
                new("exploring", "lag-oglarin", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "present", "seeking", "compound"]),
                new("adventuring", "vark-yankin", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "present", "danger", "compound"]),
                new("stay", "dakku", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["infinitive"]),
                new("stays", "dakur", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["present"]),
                new("staying", "dakkin", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["progressive", "present"]),
                new("stayed", "dakash", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["past"]),
                new("working", "hekin", PartOfSpeech: "verb", GrammarClass: "labor", Tags: ["progressive", "present"]),
                new("recover", "ut-dravku", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["infinitive", "reclaim"]),
                new("recovered", "ut-dravash", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["past", "reclaim"]),
                new("re-take", "ut-dravku", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["infinitive", "reclaim"]),
                new("retake", "ut-dravku", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["infinitive", "reclaim"]),
                new("went on", "lagash", PartOfSpeech: "verb", GrammarClass: "sequence", Tags: ["past", "continued"]),
                new("ask again", "utar-skel", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["imperative", "repeated"]),
                new("break legs", "brak-kruzi", PartOfSpeech: "verb", GrammarClass: "harm", Tags: ["imperative", "plural-object"]),
                new("leave no tracks", "nul-lagi", PartOfSpeech: "verb", GrammarClass: "stealth", Tags: ["imperative", "negative-trace"]),
                new("remove food", "rukh-quum", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["imperative"]),
                new("travel only by night", "naut-varku", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["instruction", "night-only"]),
                new("use iron", "bruk-zol", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["imperative"]),
                new("hate", "krugh", PartOfSpeech: "verb", GrammarClass: "emotion", Tags: ["infinitive"]),
                new("think", "thog", PartOfSpeech: "verb", GrammarClass: "thought", Tags: ["infinitive"]),
                new("hold off", "grotash", PartOfSpeech: "verb", GrammarClass: "delay", Tags: ["fixed-phrase"]),
                new("control", "dargu", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["infinitive"]),
                new("controlling", "dargin", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["progressive", "present"]),
                new("feel", "grodh", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["infinitive"]),
                new("plague", "morzku", PartOfSpeech: "verb", GrammarClass: "affliction", Tags: ["infinitive"]),
                new("plagues", "morzur", PartOfSpeech: "verb", GrammarClass: "affliction", Tags: ["present"]),
                new("have", "tukra", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["present"]),
                new("cripple", "kangsin", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["subject-complement"]),
                new("crippled", "kangsin", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["subject-complement", "past-participle"]),
                new("welcome", "mokra-dak", PartOfSpeech: "adjective", GrammarClass: "acceptance", Tags: ["friendly"]),
                new("abandoned", "nul-dakkin", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["deserted", "place"]),
                new("hidden", "noglar", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["concealed", "place"]),
                new("famous", "mur-oglar", PartOfSpeech: "adjective", GrammarClass: "reputation", Tags: ["known", "compound"]),
                new("lost", "nul-lag", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["missing", "place"]),
                new("dwarven", "dwarfuk", PartOfSpeech: "adjective", GrammarClass: "species", Tags: ["possessive-derived", "exonym"]),
                new("hoary", "murdrath-kelnib", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["very-old", "pale", "compound"]),
                new("hoary with age", "murdrath-kelnib dakuruk", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["very-old", "fixed-phrase"]),
                new("well into his second century", "grak ik mogumuk dug mur-dakur", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["aged", "fixed-phrase"]),
                new("still youthful enough", "ashdak nurik grod", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["youthful", "sufficient", "fixed-phrase"]),
                new("youthful", "nurik-grod", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["young", "vigorous", "compound"]),
                new("red", "rug", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["default"]),
                new("Red", "rug", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["default"]),
                new("puritanical", "mur-mograth-darg", PartOfSpeech: "adjective", GrammarClass: "religion", Tags: ["strict", "religious", "compound"]),
                new("more puritanical", "mur-mograth-darg-ti", PartOfSpeech: "adjective", GrammarClass: "religion", Tags: ["strict", "comparative", "religious", "compound"]),
                new("relaxed", "thrum-darg", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["lenient", "law", "compound"]),
                new("more relaxed", "thrum-darg-ti", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["lenient", "comparative", "law", "compound"]),
                new("immediate", "grak-nak", PartOfSpeech: "adjective", GrammarClass: "location", Tags: ["nearby", "emphatic", "compound"]),
                new("surrounding", "nak", PartOfSpeech: "adjective", GrammarClass: "location", Tags: ["nearby"]),
                new("cripple", "kangstuk", PartOfSpeech: "verb", GrammarClass: "harm", Tags: ["infinitive"]),
                new("crippled", "kangstash", PartOfSpeech: "verb", GrammarClass: "harm", Tags: ["past"]),
                new("I", "Ugh", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["variant-a", "plain"]),
                new("I", "Grrt", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["variant-b", "plain"]),
                new("myself", "Ughuk", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["variant-a", "intensive"]),
                new("myself", "Grrtuk", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["variant-b", "intensive"]),
                new("I'm", "Ugh tur", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["contraction", "first-person", "state"]),
                new("you", "narg", PartOfSpeech: "pronoun", GrammarClass: "second-person", Tags: ["plain"]),
                new("your", "narguk", PartOfSpeech: "pronoun", GrammarClass: "second-person", Tags: ["possessive"]),
                new("yours", "narguk", PartOfSpeech: "pronoun", GrammarClass: "second-person", Tags: ["possessive"]),
                new("he", "mogum", PartOfSpeech: "pronoun", GrammarClass: "third-person", Tags: ["masculine", "plain"]),
                new("his", "mogumuk", PartOfSpeech: "pronoun", GrammarClass: "third-person", Tags: ["masculine", "possessive"]),
                new("they", "ughat", PartOfSpeech: "pronoun", GrammarClass: "other", Tags: ["plural", "plain"]),
                new("them", "ughatum", PartOfSpeech: "pronoun", GrammarClass: "other", Tags: ["plural", "object"]),
                new("whose", "ughatuk", PartOfSpeech: "pronoun", GrammarClass: "relative", Tags: ["possessive", "relative"]),
                new("their", "ughatuk", PartOfSpeech: "pronoun", GrammarClass: "other", Tags: ["possessive", "plural"]),
                new("it", "um", PartOfSpeech: "pronoun", GrammarClass: "thing", Tags: ["plain"]),
                new("its", "umuk", PartOfSpeech: "pronoun", GrammarClass: "thing", Tags: ["possessive"]),
                new("really", "grak", PartOfSpeech: "adverb", GrammarClass: "emphasis", Tags: ["variant-a", "plain"]),
                new("really", "urkh", PartOfSpeech: "adverb", GrammarClass: "emphasis", Tags: ["variant-b", "plain"]),
                new("there", "dak", PartOfSpeech: "adverb", GrammarClass: "location", Tags: ["locative", "existential"]),
                new("somewhere", "varg-dak", PartOfSpeech: "adverb", GrammarClass: "location", Tags: ["indefinite", "compound"]),
                new("perhaps", "mauk-grak", PartOfSpeech: "adverb", GrammarClass: "possibility", Tags: ["uncertainty", "compound"]),
                new("ago", "dakur-ash", PartOfSpeech: "adverb", GrammarClass: "time", Tags: ["past", "compound"]),
                new("however", "rokh-grak", PartOfSpeech: "adverb", GrammarClass: "contrast", Tags: ["contrastive", "compound"]),
                new("However", "rokh-grak", PartOfSpeech: "adverb", GrammarClass: "contrast", Tags: ["contrastive", "compound"]),
                new("alongside", "mokru-nak", PartOfSpeech: "adverb", GrammarClass: "association", Tags: ["beside", "compound"]),
                new("initially", "ashdak", PartOfSpeech: "adverb", GrammarClass: "sequence", Tags: ["initial"]),
                new("of course", "grak-tur", PartOfSpeech: "adverb", GrammarClass: "certainty", Tags: ["fixed-phrase"]),
                new("too much", "murgrom", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["excess"]),
                new("those", "lek", PartOfSpeech: "determiner", GrammarClass: "demonstrative"),
                new("these", "lek", PartOfSpeech: "determiner", GrammarClass: "demonstrative"),
                new("this", "um-lek", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["singular", "near"]),
                new("other such", "agh-lek", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["similar", "additional", "fixed-phrase"]),
                new("three", "dug-agh-ash", PartOfSpeech: "numeral", GrammarClass: "cardinal"),
                new("key", "thrak", PartOfSpeech: "adjective", GrammarClass: "importance", Tags: ["important"]),
                new("the", "arhk", PartOfSpeech: "determiner", GrammarClass: "article", Tags: ["default", "before-consonant"]),
                new("the", "karnt", PartOfSpeech: "determiner", GrammarClass: "article", Tags: ["before-vowel"]),
                new("a", "ash", PartOfSpeech: "determiner", GrammarClass: "article", Tags: ["indefinite"]),
                new("an", "ash", PartOfSpeech: "determiner", GrammarClass: "article", Tags: ["indefinite"]),
                new("at", "ak", PartOfSpeech: "preposition", GrammarClass: "location", Tags: ["default", "before-consonant"]),
                new("at", "kaat", PartOfSpeech: "preposition", GrammarClass: "location", Tags: ["before-vowel"]),
                new("to", "ur", PartOfSpeech: "preposition", GrammarClass: "direction", Tags: ["default", "before-consonant"]),
                new("to", "kur", PartOfSpeech: "preposition", GrammarClass: "direction", Tags: ["before-vowel"]),
                new("in", "ik", PartOfSpeech: "preposition", GrammarClass: "location", Tags: ["default", "before-consonant"]),
                new("in", "k'ik", PartOfSpeech: "preposition", GrammarClass: "location", Tags: ["before-vowel"]),
                new("like", "mok", PartOfSpeech: "preposition", GrammarClass: "comparison", Tags: ["default"]),
                new("as", "mok", PartOfSpeech: "preposition", GrammarClass: "comparison", Tags: ["role"]),
                new("from", "dok", PartOfSpeech: "preposition", GrammarClass: "origin", Tags: ["source"]),
                new("of", "uk", PartOfSpeech: "preposition", GrammarClass: "possession", Tags: ["genitive"]),
                new("on", "ak", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["default"]),
                new("over", "dak-uk", PartOfSpeech: "preposition", GrammarClass: "authority", Tags: ["dominion", "compound"]),
                new("around", "nak", PartOfSpeech: "preposition", GrammarClass: "location", Tags: ["nearby"]),
                new("into", "ik", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["interior"]),
                new("after", "dok", PartOfSpeech: "preposition", GrammarClass: "origin", Tags: ["source"]),
                new("unlike", "mok-nu", PartOfSpeech: "preposition", GrammarClass: "comparison", Tags: ["contrast", "compound"]),
                new("depending on", "ut-lag", PartOfSpeech: "preposition", GrammarClass: "condition", Tags: ["fixed-phrase"]),
                new("those formidable ones", "lekyanki", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["formidable", "marked"]),
                new("these formidable ones", "lekyanki", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["formidable", "marked"]),
                new("two", "dug", PartOfSpeech: "numeral", GrammarClass: "cardinal"),
                new("second", "dug", PartOfSpeech: "numeral", GrammarClass: "ordinal"),
                new("III", "dug-agh-ash", PartOfSpeech: "numeral", GrammarClass: "ordinal", Tags: ["roman", "third"]),
                new("IV", "dug-agh-dug", PartOfSpeech: "numeral", GrammarClass: "ordinal", Tags: ["roman", "fourth"]),
                new("if", "ut", PartOfSpeech: "conjunction", GrammarClass: "condition", Tags: ["variant-a", "plain", "alternating"]),
                new("if", "ka", PartOfSpeech: "conjunction", GrammarClass: "condition", Tags: ["variant-b", "plain", "alternating"]),
                new("when", "dakur-ut", PartOfSpeech: "conjunction", GrammarClass: "time", Tags: ["temporal", "compound"]),
                new("just as", "mok-grak", PartOfSpeech: "conjunction", GrammarClass: "comparison", Tags: ["equivalence", "fixed-phrase"]),
                new("that", "ut", PartOfSpeech: "conjunction", GrammarClass: "relative", Tags: ["relative"]),
                new("and", "agh", PartOfSpeech: "conjunction", GrammarClass: "addition", Tags: ["plain"]),
                new("or", "ogh", PartOfSpeech: "conjunction", GrammarClass: "alternative", Tags: ["plain"]),
                new("but", "rokh", PartOfSpeech: "conjunction", GrammarClass: "contrast", Tags: ["variant-a", "plain"]),
                new("but", "nar", PartOfSpeech: "conjunction", GrammarClass: "contrast", Tags: ["variant-b", "plain"]),
                new("sarcastic but", "rokhki", PartOfSpeech: "conjunction", GrammarClass: "contrast", Tags: ["variant-a", "sarcastic"]),
                new("sarcastic but", "narki", PartOfSpeech: "conjunction", GrammarClass: "contrast", Tags: ["variant-b", "sarcastic"]),
                new("old", "drath", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["default"]),
                new("very old", "murdrath", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["intensified"]),
                new("young", "nurik", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["default"]),
                new("very young", "murnurik", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["intensified"]),
                new("healthy", "grod", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["default"]),
                new("young and healthy", "nurik-grod", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["compound"]),
                new("sickly", "morz", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["default"]),
                new("pale", "kelnib", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["neutral"]),
                new("pale with fear", "kelnagak", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["fear", "pejorative"]),
                new("fear-pale", "kelnagak", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["fear", "pejorative"]),
                new("readers of strange books", "zruk-bib-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["learned", "text", "plural"]),
                new("robe-wearers", "khal-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["garb", "plural"]),
                new("small groups", "nikmokhi", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["small", "plural"]),
                new("strong fighters", "yanki-gash", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["strong", "martial", "plural"]),
                new("strong ones", "yankith", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["strong", "plural"]),
                new("those carrying scrolls", "lek bib-hrowkai", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["demonstrative", "text", "plural"])
            };

            var baseEntries = entries.ToArray();
            entries.AddRange(BuildPluralPossessives(baseEntries));
            return entries.ToArray();
        }

        private static OrcishAffixEntry[] BuildAffixEntries()
        {
            return
            [
                new(
                    "mar",
                    "prefix",
                    "often marks inferiority",
                    "User guidance: many Orcish words beginning with 'mar-' carry an inferior or lesser sense. In forms like 'margi' and 'margith', the gloss can extend beyond humans to other non-orc humanoids Orcs view as inferior.",
                    ["connotation", "inferiority"]),
                new(
                    "ti",
                    "suffix",
                    "connotes grandeur or power",
                    "Seen in self-reference such as 'mogra-ti', indicating the superior or favored children of Gruumsh.",
                    ["connotation", "grandeur", "power", "favor"]),
                new(
                    "gi",
                    "suffix",
                    "marks something broadly foreign to Orcish experience or culture",
                    "Example: 'fletra-gi' can describe a flying thing that feels alien or outside Orcish experience.",
                    ["connotation", "foreign", "otherness", "cultural-distance"]),
                new(
                    "yanki",
                    "suffix",
                    "marks one of unexpected strength",
                    "Historical note: Orcs are said to have added 'yanki' after underestimating and then losing to the extraplanar humanoids later known as 'githyanki'.",
                    ["connotation", "unexpected-strength", "historical", "martial-respect"])
            ];
        }

        public static IReadOnlyList<OrcishTranslationCandidate> Translate(OrcishTranslationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);

            if (request.SourceLanguage == request.TargetLanguage)
            {
                throw new ArgumentException("Source and target languages must be different.", nameof(request));
            }

            var index = request.SourceLanguage == OrcishLanguage.English
                ? EnglishIndex
                : OrcishIndex;

            var normalizedText = request.Text.Trim();
            if (!index.TryGetValue(normalizedText, out var matchingEntries))
            {
                return Array.Empty<OrcishTranslationCandidate>();
            }

            var candidates = matchingEntries
                .Where(entry => MatchesPartOfSpeech(entry, request.PartOfSpeech))
                .Where(entry => MatchesRequiredTags(entry, request.RequiredTags))
                .Select(entry => CreateCandidate(entry, request.SourceLanguage))
                .Distinct()
                .ToArray();

            return candidates;
        }

        private static OrcishTranslationCandidate? ChooseRandomCandidate(
            IReadOnlyList<OrcishTranslationCandidate> candidates)
        {
            if (candidates.Count == 0)
            {
                return null;
            }

            var selectedIndex = RandomNumberUtility.GenerateInteger(0, candidates.Count - 1);
            return candidates[selectedIndex];
        }

        private static OrcishTranslationCandidate? SelectSequenceCandidate(
            string englishTerm,
            IReadOnlyList<OrcishTranslationCandidate> candidates,
            IDictionary<string, string> alternationState)
        {
            if (candidates.Count == 0)
            {
                return null;
            }

            var alternatingCandidates = candidates
                .Where(candidate => HasTag(candidate.Tags, "alternating"))
                .ToArray();

            if (alternatingCandidates.Length < 2)
            {
                return ChooseRandomCandidate(candidates);
            }

            if (!alternationState.TryGetValue(englishTerm, out var previousTranslation))
            {
                var firstChoice = ChooseRandomCandidate(alternatingCandidates)!;
                alternationState[englishTerm] = firstChoice.Translation;
                return firstChoice;
            }

            var nextChoice = alternatingCandidates.FirstOrDefault(candidate =>
                !string.Equals(candidate.Translation, previousTranslation, StringComparison.OrdinalIgnoreCase));

            if (nextChoice is null)
            {
                return ChooseRandomCandidate(alternatingCandidates);
            }

            alternationState[englishTerm] = nextChoice.Translation;
            return nextChoice;
        }

        private static bool MatchesPartOfSpeech(OrcishLexiconEntry entry, string? partOfSpeech)
        {
            return string.IsNullOrWhiteSpace(partOfSpeech)
                || string.Equals(entry.PartOfSpeech, partOfSpeech.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesRequiredTags(OrcishLexiconEntry entry, IReadOnlyList<string>? requiredTags)
        {
            if (requiredTags is null || requiredTags.Count == 0)
            {
                return true;
            }

            var entryTags = entry.Tags ?? Array.Empty<string>();
            return requiredTags.All(requiredTag =>
                entryTags.Any(entryTag => string.Equals(entryTag, requiredTag, StringComparison.OrdinalIgnoreCase)));
        }

        private static IEnumerable<OrcishLexiconEntry> BuildPluralPossessives(IEnumerable<OrcishLexiconEntry> entries)
        {
            foreach (var entry in entries.Where(entry =>
                         string.Equals(entry.PartOfSpeech, "noun", StringComparison.OrdinalIgnoreCase) &&
                         HasTag(entry, "plural") &&
                         !HasTag(entry, "possessive")))
            {
                yield return new OrcishLexiconEntry(
                    ToEnglishPossessive(entry.English),
                    ToOrcishPossessive(entry.Orcish),
                    entry.PartOfSpeech,
                    entry.GrammarClass,
                    AddTag(entry.Tags, "possessive"));
            }
        }

        private static bool HasTag(OrcishLexiconEntry entry, string tag)
        {
            return (entry.Tags ?? Array.Empty<string>())
                .Any(existingTag => string.Equals(existingTag, tag, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasTag(IReadOnlyList<string>? tags, string tag)
        {
            return (tags ?? Array.Empty<string>())
                .Any(existingTag => string.Equals(existingTag, tag, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<string> AddTag(IReadOnlyList<string>? tags, string tag)
        {
            var result = new List<string>(tags ?? Array.Empty<string>());
            if (!result.Any(existingTag => string.Equals(existingTag, tag, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(tag);
            }

            return result;
        }

        private static string ToEnglishPossessive(string english)
        {
            return english.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                ? $"{english}'"
                : $"{english}'s";
        }

        private static string ToOrcishPossessive(string orcish)
        {
            return $"{orcish}uk";
        }

        private static OrcishTranslationCandidate CreateCandidate(
            OrcishLexiconEntry entry,
            OrcishLanguage sourceLanguage)
        {
            return sourceLanguage == OrcishLanguage.English
                ? new OrcishTranslationCandidate(
                    entry.English,
                    entry.Orcish,
                    entry.PartOfSpeech,
                    entry.GrammarClass,
                    entry.Tags ?? Array.Empty<string>())
                : new OrcishTranslationCandidate(
                    entry.Orcish,
                    entry.English,
                    entry.PartOfSpeech,
                    entry.GrammarClass,
                    entry.Tags ?? Array.Empty<string>());
        }

        private static IReadOnlyDictionary<string, OrcishLexiconEntry[]> BuildIndex(
            IEnumerable<OrcishLexiconEntry> entries,
            Func<OrcishLexiconEntry, string> keySelector)
        {
            var index = new Dictionary<string, List<OrcishLexiconEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                var key = keySelector(entry).Trim();
                if (!index.TryGetValue(key, out var bucket))
                {
                    bucket = [];
                    index[key] = bucket;
                }

                bucket.Add(entry);
            }

            return index.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
