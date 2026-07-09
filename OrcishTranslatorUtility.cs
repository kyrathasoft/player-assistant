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
                new("wilderness", "vril-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["wild", "wilderness", "compound"]),
                new("hedge", "vrul", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["default", "growth"]),
                new("woods", "vril", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["default", "wilderness", "plural-mass"]),
                new("mountain", "ti-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["height", "compound"]),
                new("mountains", "ti-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["height", "plural", "compound"]),
                new("shadow", "burz-nak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["dark", "nearby", "compound"]),
                new("gloom", "burz-thog", PartOfSpeech: "noun", GrammarClass: "light", Tags: ["dark", "abstract", "compound"]),
                new("dusk", "naut-ik", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["evening", "darkening", "compound"]),
                new("gloom of dusk", "burz-thog uk naut-ik", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["gloom", "dusk", "fixed-phrase"]),
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
                new("crossroad", "dug-lag-mokru", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["junction", "compound"]),
                new("crossroads", "dug-lag-mokrui", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["junction", "plural", "compound"]),
                new("wilds", "vril-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["wild", "wilderness", "plural", "compound"]),
                new("nearby wilds", "nak-vril-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["nearby", "wild", "wilderness", "plural", "fixed-phrase"]),
                new("caravan route", "hrogar-lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["trade", "transport", "fixed-phrase"]),
                new("Kirkilston", "Kirkilston", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement"]),
                new("Kirkliston", "Kirkliston", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement", "variant-spelling"]),
                new("Kirkliston's", "Kirklistonuk", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement", "possessive", "variant-spelling"]),
                new("alternate spelling 'Kirkilston'", "agh-narg bibnak Kirkilston", PartOfSpeech: "noun", GrammarClass: "language", Tags: ["alternate", "spelling", "fixed-phrase"]),
                new("Blackpeak Mountains", "Burz-ti Ti-Daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "mountains", "compound"]),
                new("Darkforest", "Burz-gruul", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "forest", "compound"]),
                new("Darkwood Forest", "Burz-gruul", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "forest", "compound"]),
                new("Raven's Pass", "Ravenuk Lag", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "pass", "exonym", "fixed-phrase"]),
                new("Raven’s Pass", "Ravenuk Lag", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "pass", "exonym", "fixed-phrase"]),
                new("Eastdale", "Eastdale", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement"]),
                new("Westkeep", "Westkeep", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement"]),
                new("Middenmark", "Middenmark", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "region"]),
                new("St", "St", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "title", "exonym"]),
                new("Ygg", "Ygg", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
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
                new("centerpiece", "thrak-murk-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["central", "important", "compound"]),
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
                new("wall", "gor-hek", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["defense", "built", "compound"]),
                new("formal wall", "bib-darguk gor-hek", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["defense", "formal", "built", "fixed-phrase"]),
                new("structure", "hek-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["built", "compound"]),
                new("structures", "hek-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["built", "plural", "compound"]),
                new("defensive structures", "gor-hek-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["defense", "built", "plural", "compound"]),
                new("significant defensive structures", "thrak-gor-hek-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["important", "defense", "built", "plural", "fixed-phrase"]),
                new("cave", "burz-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["underground", "shelter", "compound"]),
                new("caves", "burz-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["underground", "shelter", "plural", "compound"]),
                new("Glittering Caves", "Glittering Caves", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "plural"]),
                new("Forge", "Forge", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement"]),
                new("Threshold", "Threshold", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement"]),
                new("base", "mokh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["operations", "compound"]),
                new("residence", "dakku-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["dwelling", "compound"]),
                new("booth", "dakku-burz", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["seat", "tavern", "compound"]),
                new("hearth", "rukh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["fire", "dwelling", "compound"]),
                new("dining area", "quum-dak-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["food", "area", "compound"]),
                new("common room", "mokh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["communal", "tavern", "compound"]),
                new("room", "dak-burz", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["interior", "compound"]),
                new("inn", "rukh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["tavern", "lodging", "compound"]),
                new("within", "ik-burz", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["interior", "compound"]),
                new("square", "murk-mokh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["central", "public", "compound"]),
                new("outside", "dok-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["exterior", "compound"]),
                new("market area", "drav-dak-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["trade", "area", "compound"]),
                new("small market area", "nik-drav-dak-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["trade", "small", "area", "fixed-phrase"]),
                new("map", "bibnak", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["default", "text", "directional"]),
                new("maps", "bibnaki", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["default", "text", "directional", "plural"]),
                new("book", "bib", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["default", "text"]),
                new("scroll", "bib", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["default", "text"]),
                new("book-man", "bib-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "scholar", "text"]),
                new("morsel", "quum-bit", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["small", "food", "compound"]),
                new("Morsels", "quum-biti", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["small", "food", "plural", "compound"]),
                new("tankard", "rukh-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["drink", "vessel", "compound"]),
                new("Tankards", "rukh-banti", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["drink", "vessel", "plural", "compound"]),
                new("Morgan's Morsels & Tankards", "Morganuk quum-biti agh rukh-banti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "tavern", "fixed-phrase"]),
                new("Morgan’s Morsels & Tankards", "Morganuk quum-biti agh rukh-banti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "tavern", "fixed-phrase"]),
                new("&", "agh", PartOfSpeech: "conjunction", GrammarClass: "addition", Tags: ["symbol"]),
                new("ale", "rukh-quum", PartOfSpeech: "noun", GrammarClass: "drink", Tags: ["fermented", "grain", "compound"]),
                new("soup", "rukh-quum", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["liquid", "food", "compound"]),
                new("bread", "hek-quum", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["baked", "grain", "compound"]),
                new("seat", "dakku-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["seat", "furniture", "compound"]),
                new("notice board", "narg-bib-dak", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["notice", "board", "compound"]),
                new("board", "bib-dak", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["notice", "board", "compound"]),
                new("blanket", "khal-grod", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["covering", "warmth", "compound"]),
                new("coin", "drav-zol", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["money", "metal", "compound"]),
                new("gold coin", "zol-ti-drav-zol", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["money", "gold", "compound"]),
                new("a single gold coin", "ash zol-ti-drav-zol", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["money", "gold", "single", "fixed-phrase"]),
                new("bench", "dakku-bant-ti", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["seat", "furniture", "compound"]),
                new("boot", "kruk-khal", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["footwear", "garb", "compound"]),
                new("boots", "kruk-khali", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["footwear", "garb", "plural", "compound"]),
                new("window", "oglar-dak-burz", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["opening", "sight", "compound"]),
                new("windows", "oglar-dak-burzi", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["opening", "sight", "plural", "compound"]),
                new("sign", "narg-bib", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["notice", "sign", "compound"]),
                new("tabard", "khal-bib", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["garb", "heraldic", "compound"]),
                new("chainmail hauberk", "zol-bant-khal", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["armor", "chainmail", "compound"]),
                new("hauberk", "zol-bant-khal", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["armor", "chainmail", "compound"]),
                new("kettle hat", "zol-mog-ti-khal", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["helmet", "armor", "compound"]),
                new("brim", "khal-nak", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["edge", "hat", "compound"]),
                new("mace", "zol-brak", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "bludgeon", "compound"]),
                new("haft", "zol-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "handle", "compound"]),
                new("brass ring", "zol-mauk-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["metal", "ring", "compound"]),
                new("ring", "bant-murk", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["ring", "compound"]),
                new("belt", "khal-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["garb", "strap", "compound"]),
                new("hardwood", "gruul-yank", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["wood", "hard", "compound"]),
                new("iron head", "zol-mog-ti", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["iron", "weapon", "compound"]),
                new("flange", "zol-rukh", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "edge", "compound"]),
                new("flanges", "zol-rukhi", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "edge", "plural", "compound"]),
                new("shield", "gor-zol", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["shield", "defense", "compound"]),
                new("boss", "gor-zol-murk", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["shield", "boss", "compound"]),
                new("pack", "hrowk-khal", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["pack", "carrying", "compound"]),
                new("fabric", "khal-thog", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["cloth", "abstract", "compound"]),
                new("leather strap", "vrak-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["leather", "strap", "compound"]),
                new("straps", "banti", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["strap", "plural"]),
                new("finger", "krub", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["hand"]),
                new("fingers", "krubi", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["hand", "plural"]),
                new("shoulder", "gash-bant", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["body", "compound"]),
                new("shoulders", "gash-banti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["body", "plural", "compound"]),
                new("chest", "grod-burz", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["body", "heart", "compound"]),
                new("head", "mog-ti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["body", "compound"]),
                new("arm", "yank-bant", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["body", "compound"]),
                new("arms", "yank-banti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["body", "plural", "compound"]),
                new("chin", "narg-bant", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["face", "compound"]),
                new("thumb", "krub-ti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["hand", "thumb", "compound"]),
                new("sore thumb", "morz-krub-ti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["sore", "thumb", "fixed-phrase"]),
                new("feet", "kruki", PartOfSpeech: "noun", GrammarClass: "measure", Tags: ["distance", "plural"]),
                new("flame", "rukh-tur", PartOfSpeech: "noun", GrammarClass: "fire", Tags: ["fire", "burning", "compound"]),
                new("flames", "rukh-turi", PartOfSpeech: "noun", GrammarClass: "fire", Tags: ["fire", "burning", "plural", "compound"]),
                new("hide", "vrak", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["neutral", "default"]),
                new("hides", "vraki", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["neutral", "default", "plural"]),
                new("hide", "drukh", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["reverent", "monster", "thick-hide"]),
                new("hides", "drukhi", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["reverent", "monster", "thick-hide", "plural"]),
                new("rope", "bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["neutral", "default"]),
                new("ropes", "banti", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["neutral", "default", "plural"]),
                new("rope's", "bantuk", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["neutral", "default", "possessive"]),
                new("braid", "bant-var", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["braid", "compound"]),
                new("cape", "khal", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["garb", "default"]),
                new("pommel", "zol-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "handle", "compound"]),
                new("sword", "zol-gash", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "iron", "compound"]),
                new("weapon", "zol-gash", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "default"]),
                new("armor", "zol-vrak", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["armor", "iron", "compound"]),
                new("finery", "ti-khal", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["garb", "expensive", "compound"]),
                new("beard", "drath-khal", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["hair", "face", "compound"]),
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
                new("merchants", "draviki", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["trade", "default", "plural"]),
                new("merchant wagon", "dravik-hrogar", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["transport", "trade", "compound"]),
                new("merchant wagons", "dravik-hrogarai", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["transport", "trade", "compound", "plural"]),
                new("miner", "hekfa", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "labor", "default"]),
                new("miners", "hekfai", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "labor", "default", "plural"]),
                new("priest", "mograth", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "default"]),
                new("priests", "mograthi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "default", "plural"]),
                new("wandering priests", "vagor-mograthi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "wandering", "plural"]),
                new("seasoned Priest", "drath-mograth", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "experienced", "compound"]),
                new("sage", "thogmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["learned", "default"]),
                new("sages", "thogmogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["learned", "default", "plural"]),
                new("thinker", "thogmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["thoughtful", "default"]),
                new("smith", "hekruhur", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["craft", "specialized"]),
                new("smiths", "hekruhuri", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["craft", "specialized", "plural"]),
                new("blacksmith", "zol-hekruhur", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["craft", "iron", "specialized", "compound"]),
                new("traveler", "fletragi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["outsider", "wayfarer", "default"]),
                new("travelers", "fletragith", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["outsider", "wayfarer", "default", "plural"]),
                new("traveller", "fletragi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["outsider", "wayfarer", "variant-spelling"]),
                new("settler", "dak-hekmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["settlement", "worker", "compound"]),
                new("settlers", "dak-hekmogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["settlement", "worker", "plural", "compound"]),
                new("innkeeper", "rukh-dak darg-dravik", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["tavern", "owner", "fixed-phrase"]),
                new("hedge-wizard", "gurmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "specialized"]),
                new("hedge-wizards", "gurmogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "specialized", "plural"]),
                new("wizard", "gurmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "specialized"]),
                new("wizards", "gurmogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "specialized", "plural"]),
                new("fighter", "gash", PartOfSpeech: "noun", GrammarClass: "person"),
                new("fighters", "gash", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["plural"]),
                new("warrior", "gash", PartOfSpeech: "noun", GrammarClass: "person"),
                new("farmer", "quum-hekmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["farming", "worker", "compound"]),
                new("farmers", "quum-hekmogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["farming", "worker", "plural", "compound"]),
                new("farmer's", "quum-hekmoguk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["farming", "worker", "possessive", "compound"]),
                new("farmer’s", "quum-hekmoguk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["farming", "worker", "possessive", "compound"]),
                new("lumberjack", "gruul-hek-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["wood", "worker", "compound"]),
                new("lumberjacks", "gruul-hek-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["wood", "worker", "plural", "compound"]),
                new("knight", "zol-gash-darg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["warrior", "noble", "compound"]),
                new("Slip", "Slip", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Morgan", "Morgan", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Morgan's", "Morganuk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym", "possessive"]),
                new("Kelpie", "Kelpie", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Demetra", "Demetra", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "deity", "exonym"]),
                new("Xavamros", "Xavamros", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Battlebeard", "Battlebeard", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Brand", "Brand", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Governor", "darg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["title", "ruler", "compound"]),
                new("Prince", "darg-ti-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["title", "ruler", "higher-than-governor", "compound"]),
                new("Xavin", "Xavin", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Petre", "Petre", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
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
                new("customer", "dravik-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["trade", "buyer", "compound"]),
                new("customers", "dravik-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["trade", "buyer", "plural", "compound"]),
                new("patron", "dravik-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["tavern", "customer", "compound"]),
                new("patrons", "dravik-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["tavern", "customer", "plural", "compound"]),
                new("proprietor", "darg-dravik", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["owner", "trade", "compound"]),
                new("local", "nak-dak-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["local", "resident", "compound"]),
                new("locals", "nak-dak-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["local", "resident", "plural", "compound"]),
                new("hireling", "dravik-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["paid", "helper"]),
                new("hirelings", "dravik-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["paid", "helper", "plural"]),
                new("option", "varg", PartOfSpeech: "noun", GrammarClass: "choice", Tags: ["abstract"]),
                new("choice", "varg-thog", PartOfSpeech: "noun", GrammarClass: "choice", Tags: ["abstract", "compound"]),
                new("way", "lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["route"]),
                new("figure", "mog-var", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["figure", "role"]),
                new("target", "narg-gash", PartOfSpeech: "noun", GrammarClass: "danger", Tags: ["target", "martial", "compound"]),
                new("someone", "varg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["indefinite", "person", "compound"]),
                new("need", "thruk", PartOfSpeech: "noun", GrammarClass: "requirement", Tags: ["abstract"]),
                new("needs", "thruki", PartOfSpeech: "noun", GrammarClass: "requirement", Tags: ["abstract", "plural"]),
                new("time", "dakur", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["abstract"]),
                new("times", "dakuri", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["abstract", "plural"]),
                new("day", "dakur", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["day"]),
                new("days", "dakuri", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["day", "plural"]),
                new("day's", "dakuruk", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["day", "possessive"]),
                new("day’s", "dakuruk", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["day", "possessive"]),
                new("year", "dakur-ti", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["year", "compound"]),
                new("years", "dakur-tiwi", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["year", "plural", "compound"]),
                new("pace", "lag-bit", PartOfSpeech: "noun", GrammarClass: "measure", Tags: ["step", "distance", "compound"]),
                new("paces", "lag-biti", PartOfSpeech: "noun", GrammarClass: "measure", Tags: ["step", "distance", "plural", "compound"]),
                new("a few paces", "nik lag-biti", PartOfSpeech: "noun", GrammarClass: "measure", Tags: ["step", "distance", "fixed-phrase"]),
                new("station", "darg-dak", PartOfSpeech: "noun", GrammarClass: "status", Tags: ["rank", "social", "compound"]),
                new("one of some station", "ash uk varg darg-dak", PartOfSpeech: "noun", GrammarClass: "status", Tags: ["rank", "social", "fixed-phrase"]),
                new("quality", "thrak-thog", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["quality", "abstract", "compound"]),
                new("high quality", "thrak-thog-ti", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["quality", "high", "compound"]),
                new("contrast", "mok-nu-thog", PartOfSpeech: "noun", GrammarClass: "comparison", Tags: ["contrast", "abstract", "compound"]),
                new("attention", "oglar-thog", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["attention", "perception", "compound"]),
                new("family", "mokh", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["group"]),
                new("family's", "mokhuk", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["group", "possessive"]),
                new("family’s", "mokhuk", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["group", "possessive"]),
                new("daughter", "nurik-mog", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["child", "family", "compound"]),
                new("farmer’s daughter", "quum-hekmoguk nurik-mog", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["farming", "family", "fixed-phrase"]),
                new("farmer's daughter", "quum-hekmoguk nurik-mog", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["farming", "family", "fixed-phrase"]),
                new("ruling family", "dargin mokh", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["ruling", "family", "fixed-phrase"]),
                new("century", "mur-dakur", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["long-span", "compound"]),
                new("centuries", "mur-dakuri", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["long-span", "plural", "compound"]),
                new("second century", "dug mur-dakur", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["long-span", "ordinal", "fixed-phrase"]),
                new("idea", "thog-var", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["abstract", "compound"]),
                new("nonsense", "nul-thog", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["foolish", "abstract", "compound"]),
                new("throne", "darg-thrak", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rulership", "seat", "compound"]),
                new("faith", "mograth-thog", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["belief", "compound"]),
                new("prayer", "mograth-narg", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["prayer", "speech", "compound"]),
                new("quiet prayer", "thrum-narg mograth-narg", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["prayer", "quiet", "fixed-phrase"]),
                new("belief", "mograth-thog", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["belief", "compound"]),
                new("beliefs", "mograth-thogi", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["belief", "plural", "compound"]),
                new("god", "mograth-darg-mog", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["deity", "compound"]),
                new("gods", "mograth-darg-mogi", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["deity", "plural", "compound"]),
                new("Ecclesiastical", "mograthuk", PartOfSpeech: "adjective", GrammarClass: "religion", Tags: ["church", "possessive-derived"]),
                new("Ecclesiastical Law", "mograthuk darg-bib", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["church", "law", "fixed-phrase"]),
                new("administration", "darg-bib", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["bureaucracy", "compound"]),
                new("administrative", "darg-bibuk", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["bureaucracy", "possessive-derived"]),
                new("governance", "darg-thog", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rule", "abstract", "compound"]),
                new("authority", "darg-thog", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rule", "abstract", "compound"]),
                new("administrative authority", "darg-bib darg-thog", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["bureaucracy", "rule", "fixed-phrase"]),
                new("religious authority", "mograth-darg", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["religious", "rule", "compound"]),
                new("religious and administrative authority", "mograth-darg agh darg-bib darg-thog", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["religious", "bureaucracy", "rule", "fixed-phrase"]),
                new("stance", "lag-thog", PartOfSpeech: "noun", GrammarClass: "position", Tags: ["viewpoint", "compound"]),
                new("Prelacy", "mograth-darg", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["religious", "rule", "compound"]),
                new("The Prelacy of Middenmark", "arhk mograth-darg uk Middenmark", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["proper-noun", "religious", "rule", "fixed-phrase"]),
                new("law", "darg-bib", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rule", "written", "compound"]),
                new("laws", "darg-bibi", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rule", "written", "plural", "compound"]),
                new("Red Laws", "rug-darg-bibi", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["proper-noun", "law", "red-law", "plural", "compound"]),
                new("cross", "mograth-bant", PartOfSpeech: "noun", GrammarClass: "symbol", Tags: ["heraldic", "religious", "symbol", "compound"]),
                new("red cross", "rug-mograth-bant", PartOfSpeech: "noun", GrammarClass: "symbol", Tags: ["heraldic", "religious", "red", "fixed-phrase"]),
                new("emblazoned cross", "nargash-ti mograth-bant", PartOfSpeech: "noun", GrammarClass: "symbol", Tags: ["heraldic", "emblazoned", "fixed-phrase"]),
                new("design", "narg-var", PartOfSpeech: "noun", GrammarClass: "symbol", Tags: ["design", "abstract", "compound"]),
                new("red cross design", "rug-mograth-bant narg-var", PartOfSpeech: "noun", GrammarClass: "symbol", Tags: ["heraldic", "design", "fixed-phrase"]),
                new("order", "darg-lag", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["order", "law", "compound"]),
                new("peace", "gor-thrum", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["peace", "protection", "compound"]),
                new("fellowship", "mokru-mokh", PartOfSpeech: "noun", GrammarClass: "association", Tags: ["companionship", "group", "compound"]),
                new("crowd", "mokh-mur", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["crowd", "group", "compound"]),
                new("handful", "krub-mokh", PartOfSpeech: "noun", GrammarClass: "quantity", Tags: ["small", "group", "compound"]),
                new("soul", "grod-thog", PartOfSpeech: "noun", GrammarClass: "spirit", Tags: ["soul", "spirit", "compound"]),
                new("souls", "grod-thogi", PartOfSpeech: "noun", GrammarClass: "spirit", Tags: ["soul", "spirit", "plural", "compound"]),
                new("strength", "yank-thog", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["strength", "abstract", "compound"]),
                new("cause", "thruk-thog", PartOfSpeech: "noun", GrammarClass: "purpose", Tags: ["cause", "purpose", "compound"]),
                new("purpose", "thruk-thog", PartOfSpeech: "noun", GrammarClass: "purpose", Tags: ["purpose", "abstract", "compound"]),
                new("presence", "dak-thog", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["presence", "abstract", "compound"]),
                new("height", "ti-thog", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["height", "abstract", "compound"]),
                new("above average height", "ti-thog-ti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["height", "above-average", "compound"]),
                new("build", "grod-vrak", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["build", "body", "compound"]),
                new("strapping build", "yank-grod-vrak", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["strong", "build", "compound"]),
                new("feature", "mogum-narg", PartOfSpeech: "noun", GrammarClass: "appearance", Tags: ["face", "trait", "compound"]),
                new("features", "mogum-nargi", PartOfSpeech: "noun", GrammarClass: "appearance", Tags: ["face", "trait", "plural", "compound"]),
                new("handsome features", "mauk-mogum-nargi", PartOfSpeech: "noun", GrammarClass: "appearance", Tags: ["handsome", "face", "fixed-phrase"]),
                new("hair", "mog-ti-khal", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["hair", "head", "compound"]),
                new("pale and beardless features", "kelnib agh nul-drath-khal mogum-nargi", PartOfSpeech: "noun", GrammarClass: "appearance", Tags: ["pale", "beardless", "fixed-phrase"]),
                new("smooth, fair features", "thrum-vrak agh drav-mauk mogum-nargi", PartOfSpeech: "noun", GrammarClass: "appearance", Tags: ["smooth", "fair", "fixed-phrase"]),
                new("downy blond hair", "thrum-khal surg mog-ti-khal", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["hair", "blond", "fixed-phrase"]),
                new("spark", "rukh-bit", PartOfSpeech: "noun", GrammarClass: "fire", Tags: ["spark", "small", "compound"]),
                new("light", "rukh-oglar", PartOfSpeech: "noun", GrammarClass: "light", Tags: ["light", "compound"]),
                new("sound", "narg-rukh", PartOfSpeech: "noun", GrammarClass: "sound", Tags: ["sound", "compound"]),
                new("sounds", "narg-rukhi", PartOfSpeech: "noun", GrammarClass: "sound", Tags: ["sound", "plural", "compound"]),
                new("minute", "dakur-bit", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["short-time", "compound"]),
                new("minutes", "dakur-biti", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["short-time", "plural", "compound"]),
                new("eaves", "hek-khal-nak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["roof", "edge", "plural", "compound"]),
                new("sun", "surg", PartOfSpeech: "noun", GrammarClass: "celestial", Tags: ["sun"]),
                new("warhorse", "gash-hrog", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["war", "mount", "compound"]),
                new("moth", "rukh-flit", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["insect", "light", "compound"]),
                new("bat", "naut-flit", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["night", "flying", "compound"]),
                new("arrival", "ik-lag", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["arrival", "compound"]),
                new("wave", "bant-narg", PartOfSpeech: "noun", GrammarClass: "gesture", Tags: ["wave", "gesture", "compound"]),
                new("talk", "narg-thog", PartOfSpeech: "noun", GrammarClass: "speech", Tags: ["talk", "abstract", "compound"]),
                new("warmth", "rukh-grod-thog", PartOfSpeech: "noun", GrammarClass: "temperature", Tags: ["warmth", "abstract", "compound"]),
                new("expression", "mogum-narg", PartOfSpeech: "noun", GrammarClass: "expression", Tags: ["face", "compound"]),
                new("gaze", "oglar-lag", PartOfSpeech: "noun", GrammarClass: "perception", Tags: ["gaze", "compound"]),
                new("girl", "nurik-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["young", "female", "compound"]),
                new("home", "dakku-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["dwelling", "home", "compound"]),
                new("folly", "nul-thog", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["foolish", "abstract", "compound"]),
                new("bravery", "yanki-thog", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["courage", "abstract", "compound"]),
                new("eyes", "oglar-krubi", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["sight", "plural", "compound"]),
                new("implementation", "hek-darg", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["execution", "law", "compound"]),
                new("doctrine", "mograth-bib", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["teaching", "written", "compound"]),
                new("religious doctrine", "mograth-bib", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["teaching", "religious", "compound"]),
                new("triad", "dug-agh-ash mokh", PartOfSpeech: "noun", GrammarClass: "group", Tags: ["three", "collective", "fixed-phrase"]),
                new("community", "mokh", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["group", "inhabited"]),
                new("hamlet", "thrum-mog-dak", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["small", "inhabited", "compound"]),
                new("hamlet's", "thrum-mog-dakuk", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["small", "inhabited", "possessive", "compound"]),
                new("communities", "mokhi", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["group", "inhabited", "plural"]),
                new("small group", "nikmokh", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["small", "group", "compound"]),
                new("Hamlet Watch", "thrum-mog-dak thrak", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["watch", "defense", "fixed-phrase"]),
                new("threat", "vark-thog", PartOfSpeech: "noun", GrammarClass: "danger", Tags: ["danger", "abstract", "compound"]),
                new("threats", "vark-thogi", PartOfSpeech: "noun", GrammarClass: "danger", Tags: ["danger", "abstract", "plural", "compound"]),
                new("danger", "vark-thog", PartOfSpeech: "noun", GrammarClass: "danger", Tags: ["danger", "abstract", "compound"]),
                new("dangers", "vark-thogi", PartOfSpeech: "noun", GrammarClass: "danger", Tags: ["danger", "abstract", "plural", "compound"]),
                new("force", "darg-gash", PartOfSpeech: "noun", GrammarClass: "power", Tags: ["power", "martial", "compound"]),
                new("forces", "darg-gashi", PartOfSpeech: "noun", GrammarClass: "power", Tags: ["power", "martial", "plural", "compound"]),
                new("chaos", "nul-darg-thog", PartOfSpeech: "noun", GrammarClass: "disorder", Tags: ["disorder", "abstract", "compound"]),
                new("forces of chaos", "nul-darg-thog darg-gashi", PartOfSpeech: "noun", GrammarClass: "power", Tags: ["chaos", "power", "plural", "fixed-phrase"]),
                new("bravery", "yanki-thog", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["courage", "abstract", "compound"]),
                new("testament", "thog-bib", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["evidence", "written", "compound"]),
                new("spirit", "grod-thog", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["spirit", "abstract", "compound"]),
                new("rugged and resilient spirit", "mur-grod agh grotash-nu grod-thog", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["rugged", "resilient", "spirit", "fixed-phrase"]),
                new("life", "dakur-thog", PartOfSpeech: "noun", GrammarClass: "life", Tags: ["daily", "abstract", "compound"]),
                new("daily life", "dakur-dakur-thog", PartOfSpeech: "noun", GrammarClass: "life", Tags: ["daily", "routine", "compound"]),
                new("ethos", "mokh-thog", PartOfSpeech: "noun", GrammarClass: "culture", Tags: ["values", "abstract", "compound"]),
                new("hardworking ethos", "mur-hekin mokh-thog", PartOfSpeech: "noun", GrammarClass: "culture", Tags: ["labor", "values", "fixed-phrase"]),
                new("farming", "quum-hekin", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["food", "farming", "compound"]),
                new("shepherding", "thrum-quum-mog-hekin", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["livestock", "sheep", "compound"]),
                new("trade", "drav", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["commerce", "default"]),
                new("trades", "dravi", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["commerce", "plural"]),
                new("local trades", "nak-dakuk dravi", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["commerce", "local", "plural", "fixed-phrase"]),
                new("amenity", "dakku-hek", PartOfSpeech: "noun", GrammarClass: "service", Tags: ["useful", "settlement", "compound"]),
                new("amenities", "dakku-heki", PartOfSpeech: "noun", GrammarClass: "service", Tags: ["useful", "settlement", "plural", "compound"]),
                new("essential amenities", "thruk-dakku-heki", PartOfSpeech: "noun", GrammarClass: "service", Tags: ["essential", "settlement", "plural", "fixed-phrase"]),
                new("tavern", "rukh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["drink", "social", "compound"]),
                new("social life", "mokh-dakur-thog", PartOfSpeech: "noun", GrammarClass: "society", Tags: ["social", "daily", "compound"]),
                new("communal gathering", "mokh-mokru", PartOfSpeech: "noun", GrammarClass: "society", Tags: ["communal", "gathering", "compound"]),
                new("communal gatherings", "mokh-mokrui", PartOfSpeech: "noun", GrammarClass: "society", Tags: ["communal", "gathering", "plural", "compound"]),
                new("festival", "mauk-mokh", PartOfSpeech: "noun", GrammarClass: "celebration", Tags: ["celebration", "public", "compound"]),
                new("festivals", "mauk-mokhi", PartOfSpeech: "noun", GrammarClass: "celebration", Tags: ["celebration", "public", "plural", "compound"]),
                new("fair", "drav-mauk", PartOfSpeech: "noun", GrammarClass: "celebration", Tags: ["trade", "celebration", "compound"]),
                new("fairs", "drav-mauki", PartOfSpeech: "noun", GrammarClass: "celebration", Tags: ["trade", "celebration", "plural", "compound"]),
                new("focal point", "murk-thrak-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["central", "important", "compound"]),
                new("focal points", "murk-thrak-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["central", "important", "plural", "compound"]),
                new("celebration", "mauk-thog", PartOfSpeech: "noun", GrammarClass: "celebration", Tags: ["joy", "abstract", "compound"]),
                new("social cohesion", "mokh-mokru-thog", PartOfSpeech: "noun", GrammarClass: "society", Tags: ["social", "unity", "compound"]),
                new("people", "mogi", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["person", "plural"]),
                new("hardship", "grot-thog", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["difficulty", "abstract", "compound"]),
                new("safe", "gor-grod", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["safety", "protected", "compound"]),
                new("sight", "oglar-thog", PartOfSpeech: "noun", GrammarClass: "perception", Tags: ["sight", "abstract", "compound"]),
                new("welcome sight", "mokra-dak oglar-thog", PartOfSpeech: "noun", GrammarClass: "perception", Tags: ["welcome", "sight", "fixed-phrase"]),
                new("march", "gash-lag", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["walking", "military", "compound"]),
                new("day's march", "dakuruk gash-lag", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["day", "walking", "fixed-phrase"]),
                new("day’s march", "dakuruk gash-lag", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["day", "walking", "fixed-phrase"]),
                new("morning", "dakur-sun", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["morning", "compound"]),
                new("guard duty", "gor-hek", PartOfSpeech: "noun", GrammarClass: "protection", Tags: ["guard", "duty", "compound"]),
                new("resilience", "grotash-nu-thog", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["resilient", "abstract", "compound"]),
                new("opportunity", "varg-dak", PartOfSpeech: "noun", GrammarClass: "choice", Tags: ["opportunity", "compound"]),
                new("opportunities", "varg-daki", PartOfSpeech: "noun", GrammarClass: "choice", Tags: ["opportunity", "plural", "compound"]),
                new("defense", "gor-thog", PartOfSpeech: "noun", GrammarClass: "protection", Tags: ["defense", "abstract", "compound"]),
                new("activity", "hek-var", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["action", "abstract", "compound"]),
                new("activities", "hek-vari", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["action", "abstract", "plural", "compound"]),
                new("trade activities", "drav hek-vari", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["commerce", "action", "plural", "fixed-phrase"]),
                new("jobs board", "hek-bib", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["work", "notice", "compound"]),
                new("posting", "narg-bib", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["notice", "written", "compound"]),
                new("postings", "narg-bibi", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["notice", "written", "plural", "compound"]),
                new("following postings", "ut-narg-bibi", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["following", "notice", "plural", "fixed-phrase"]),
                new("economy", "drav-thog", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["commerce", "abstract", "compound"]),
                new("local economy", "nak-dakuk drav-thog", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["commerce", "local", "fixed-phrase"]),
                new("livestock", "quum-mogi", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["food", "kept", "plural", "compound"]),
                new("sheep", "thrum-quum-mogi", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["food", "kept", "plural", "compound"]),
                new("agriculture", "quum-hekin", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["food", "farming", "compound"]),
                new("corn", "rug-quum", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["grain", "crop", "compound"]),
                new("crop", "quum-hek", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["farming", "product", "compound"]),
                new("chief crop", "thrak-quum-hek", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["farming", "primary", "fixed-phrase"]),
                new("production", "hekin-var", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["making", "process", "compound"]),
                new("lumber", "gruul-hek", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["wood", "processed", "compound"]),
                new("lumber production", "gruul-hek hekin-var", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["wood", "making", "fixed-phrase"]),
                new("sap", "gruul-rukh", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["wood", "liquid", "compound"]),
                new("sawdust", "gruul-thrum", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["wood", "dust", "compound"]),
                new("mud", "dak-rukh", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["earth", "wet", "compound"]),
                new("scent", "kaag-thog", PartOfSpeech: "noun", GrammarClass: "sense", Tags: ["smell", "abstract", "compound"]),
                new("scents", "kaag-thogi", PartOfSpeech: "noun", GrammarClass: "sense", Tags: ["smell", "plural", "compound"]),
                new("labour", "hek", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["british", "default"]),
                new("air", "hush", PartOfSpeech: "noun", GrammarClass: "element", Tags: ["air"]),
                new("open air", "lag-nu-gor hush", PartOfSpeech: "noun", GrammarClass: "element", Tags: ["air", "open", "fixed-phrase"]),
                new("export", "dok-dravin", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["outbound", "commerce", "compound"]),
                new("mead", "rukh-mauk", PartOfSpeech: "noun", GrammarClass: "drink", Tags: ["fermented", "honey", "compound"]),
                new("interaction", "mokru-thog", PartOfSpeech: "noun", GrammarClass: "association", Tags: ["contact", "compound"]),
                new("interactions", "mokru-thogi", PartOfSpeech: "noun", GrammarClass: "association", Tags: ["contact", "plural", "compound"]),
                new("key stop", "thrak-dak", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["important", "waypoint", "compound"]),
                new("work", "hek", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["default"]),
                new("hedgerow", "vrul-lag", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["hedge", "route", "compound"]),
                new("hedgerows", "vrul-lagi", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["hedge", "route", "plural", "compound"]),
                new("muttering", "thrum-narg", PartOfSpeech: "noun", GrammarClass: "speech", Tags: ["quiet", "speech", "compound"]),
                new("mutterings", "thrum-nargi", PartOfSpeech: "noun", GrammarClass: "speech", Tags: ["quiet", "speech", "plural", "compound"]),
                new("common folk", "mokh-mogi", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["common", "folk", "plural", "compound"]),
                new("eye", "oglar-krub", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["sight", "body", "compound"]),
                new("name", "mog-narg", PartOfSpeech: "noun", GrammarClass: "identity", Tags: ["name", "compound"]),
                new("glow", "rukh-oglar", PartOfSpeech: "noun", GrammarClass: "light", Tags: ["light", "warmth", "compound"]),
                new("smell", "kaag-thog", PartOfSpeech: "noun", GrammarClass: "sense", Tags: ["smell", "abstract", "compound"]),
                new("smells", "kaag-thogi", PartOfSpeech: "noun", GrammarClass: "sense", Tags: ["smell", "plural", "compound"]),
                new("grin", "mauk-narg", PartOfSpeech: "noun", GrammarClass: "expression", Tags: ["smile", "compound"]),
                new("watch", "gor", PartOfSpeech: "verb", GrammarClass: "action"),
                new("to be", "tar", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["infinitive"]),
                new("be", "tar", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["infinitive"]),
                new("is", "tur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present"]),
                new("am", "tur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present"]),
                new("are", "tur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present"]),
                new("was", "tash", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past"]),
                new("were", "tash", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past"]),
                new("had", "tukash", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["past"]),
                new("have been", "tuk", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["perfect"]),
                new("is being", "turin", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["progressive", "present"]),
                new("are being", "turin", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["progressive", "present"]),
                new("being", "turin", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["progressive", "present"]),
                new("will be", "taruk", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["future"]),
                new("may be", "mauk tar", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility", "permission", "state"]),
                new("may opt to be staying", "mauk vargu dakkin", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility", "choice", "location", "fixed-phrase"]),
                new("may be from", "mauk dok", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility", "origin", "fixed-phrase"]),
                new("is named after", "tur mog-nargash dok", PartOfSpeech: "verb", GrammarClass: "naming", Tags: ["present", "passive", "fixed-phrase"]),
                new("named after", "mog-nargash dok", PartOfSpeech: "verb", GrammarClass: "naming", Tags: ["past-participle", "fixed-phrase"]),
                new("is led by", "tur dargash fa", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["present", "passive", "authority", "fixed-phrase"]),
                new("is not", "notur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present", "negative"]),
                new("are not", "notur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present", "negative"]),
                new("was not", "notash", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past", "negative"]),
                new("were not", "notash", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past", "negative"]),
                new("may", "mauk", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility", "permission"]),
                new("might", "mauk", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility"]),
                new("could", "mauk", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility"]),
                new("could wait", "mauk grotash", PartOfSpeech: "verb", GrammarClass: "delay", Tags: ["possibility", "delay", "fixed-phrase"]),
                new("wouldn't", "nu mauk", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["negative", "conditional", "contraction"]),
                new("wouldn’t", "nu mauk", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["negative", "conditional", "contraction"]),
                new("will", "uk", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["future"]),
                new("opt", "vargu", PartOfSpeech: "verb", GrammarClass: "choice", Tags: ["infinitive"]),
                new("choose", "vargu", PartOfSpeech: "verb", GrammarClass: "choice", Tags: ["infinitive"]),
                new("use", "bruku", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["infinitive"]),
                new("uses", "brukur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["present"]),
                new("using", "brukin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["progressive", "present"]),
                new("lacking", "nul-tukrin", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["progressive", "negative"]),
                new("Lacking", "nul-tukrin", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["progressive", "negative"]),
                new("give", "draku", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["infinitive"]),
                new("made", "hekash", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["past"]),
                new("make", "heku", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["infinitive"]),
                new("making", "hekin", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["progressive", "making"]),
                new("make their way", "lagu ughatuk lag", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["travel", "fixed-phrase"]),
                new("dwarven made", "dwarfuk hekash", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["past", "dwarven", "fixed-phrase"]),
                new("provide", "dravku", PartOfSpeech: "verb", GrammarClass: "support", Tags: ["infinitive"]),
                new("provide", "dravur", PartOfSpeech: "verb", GrammarClass: "support", Tags: ["present", "plural-subject"]),
                new("retain", "dargu-tukra", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["authority", "infinitive", "compound"]),
                new("retains", "dargu-tukur", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["authority", "present", "compound"]),
                new("retains the throne", "dargu-tukur arhk darg-thrak", PartOfSpeech: "verb", GrammarClass: "authority", Tags: ["rulership", "possession", "fixed-phrase"]),
                new("rule", "dargu", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["infinitive", "authority"]),
                new("governed", "dargash", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["past-participle", "authority"]),
                new("Governed", "dargash", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["past-participle", "authority"]),
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
                new("contributes", "dravur", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["present", "support"]),
                new("benefits from", "dravur dok", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["present", "advantage", "fixed-phrase"]),
                new("form", "heku", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["infinitive"]),
                new("forms", "hekur", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["present"]),
                new("has", "tukur", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["present", "third-person"]),
                new("kept", "dargash-tuk", PartOfSpeech: "verb", GrammarClass: "authority", Tags: ["past", "maintained", "compound"]),
                new("brought", "dravash-ik", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["past", "brought", "compound"]),
                new("bring", "dravku-ik", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["infinitive", "brought", "compound"]),
                new("join", "mokru", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["infinitive"]),
                new("to join", "mokru", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["infinitive"]),
                new("joins", "mokrur", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["present"]),
                new("joined", "mokrash", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["past"]),
                new("joining", "mokrin", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["progressive", "present"]),
                new("escape", "varku", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive"]),
                new("escaped", "varkash", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past"]),
                new("escaping", "varkin", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "present"]),
                new("carry", "hrowku", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive", "carrying"]),
                new("carries", "hrowkur", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["present", "carrying"]),
                new("carried", "hrowkash", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past", "carrying"]),
                new("stepped inside", "lagash ik", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past", "inside", "fixed-phrase"]),
                new("drift", "varku-thrum", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive", "slow", "compound"]),
                new("drift into", "varku-thrum ik", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive", "slow", "fixed-phrase"]),
                new("filter into", "varku-thrum ik", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive", "slow", "fixed-phrase"]),
                new("wander", "vagoru", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive", "wandering"]),
                new("rush off", "varku-grak dok", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive", "hasty", "fixed-phrase"]),
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
                new("revolves around", "murk-dakur nak", PartOfSpeech: "verb", GrammarClass: "relation", Tags: ["present", "central", "fixed-phrase"]),
                new("rooted", "lag-hekash", PartOfSpeech: "verb", GrammarClass: "origin", Tags: ["past-participle", "rooted", "compound"]),
                new("respected", "dargash-thog", PartOfSpeech: "verb", GrammarClass: "respect", Tags: ["past", "respect", "compound"]),
                new("caught his eye", "nargash mogumuk oglar-krub", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["past", "attention", "fixed-phrase"]),
                new("throwing in with", "mokrin ogh", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["progressive", "joining", "fixed-phrase"]),
                new("left to his name", "ashdak ur mogumuk mog-narg", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["remaining", "fixed-phrase"]),
                new("left", "ashdak", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["remaining"]),
                new("paid", "dravash", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["past", "payment"]),
                new("knew", "thogash", PartOfSpeech: "verb", GrammarClass: "thought", Tags: ["past", "knowledge"]),
                new("needed", "thrukash", PartOfSpeech: "verb", GrammarClass: "requirement", Tags: ["past", "need"]),
                new("was needed", "tash thrukash", PartOfSpeech: "verb", GrammarClass: "requirement", Tags: ["past", "need", "passive", "fixed-phrase"]),
                new("What was needed", "mok tash thrukash", PartOfSpeech: "verb", GrammarClass: "requirement", Tags: ["question", "need", "fixed-phrase"]),
                new("he'd be sleeping", "mogum taruk dakkin-naut", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["future", "sleeping", "fixed-phrase"]),
                new("he’d be sleeping", "mogum taruk dakkin-naut", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["future", "sleeping", "fixed-phrase"]),
                new("allowed", "vargash-fa", PartOfSpeech: "verb", GrammarClass: "permission", Tags: ["past", "permission", "compound"]),
                new("wrapping around", "bantin nak", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "wrapping", "fixed-phrase"]),
                new("flashed", "oglash-grak", PartOfSpeech: "verb", GrammarClass: "display", Tags: ["past", "quick", "compound"]),
                new("raised", "ti-hekash", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past", "upward", "compound"]),
                new("settled", "dakash-grod", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["past", "comfortable", "compound"]),
                new("let", "vargash", PartOfSpeech: "verb", GrammarClass: "permission", Tags: ["past", "allow"]),
                new("loosen", "thrumku", PartOfSpeech: "verb", GrammarClass: "condition", Tags: ["infinitive", "relax"]),
                new("waited", "grotash", PartOfSpeech: "verb", GrammarClass: "delay", Tags: ["past", "wait"]),
                new("watched", "gorash", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["past", "watch"]),
                new("smelling", "kaag-thogin", PartOfSpeech: "verb", GrammarClass: "sense", Tags: ["progressive", "smell", "compound"]),
                new("carrying", "hrowkin", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "carrying"]),
                new("smiled", "mauk-nargash", PartOfSpeech: "verb", GrammarClass: "expression", Tags: ["past", "smile", "compound"]),
                new("offering", "dravin", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["progressive", "offer"]),
                new("glanced", "oglash-thrum", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["past", "brief", "compound"]),
                new("soured", "morzash", PartOfSpeech: "verb", GrammarClass: "condition", Tags: ["past", "sour"]),
                new("tightened", "mur-dargash", PartOfSpeech: "verb", GrammarClass: "condition", Tags: ["past", "tight", "compound"]),
                new("turned", "lagash-nak", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past", "turn", "compound"]),
                new("Bowing", "darg-thrumin", PartOfSpeech: "verb", GrammarClass: "gesture", Tags: ["progressive", "bow"]),
                new("murmured", "thrum-nargash", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["past", "quiet", "compound"]),
                new("asking", "skelin", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["progressive", "ask"]),
                new("found", "oglash-lag", PartOfSpeech: "verb", GrammarClass: "discovery", Tags: ["past", "found", "compound"]),
                new("returned", "ut-lagash", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past", "return"]),
                new("dressed up as", "khalash mok", PartOfSpeech: "verb", GrammarClass: "appearance", Tags: ["past", "disguised", "fixed-phrase"]),
                new("willing to stand", "varg-thog ur gor-dargu", PartOfSpeech: "verb", GrammarClass: "resolve", Tags: ["willing", "stand", "fixed-phrase"]),
                new("stand", "gor-dargu", PartOfSpeech: "verb", GrammarClass: "resolve", Tags: ["infinitive", "stand", "compound"]),
                new("lend", "dravku-dakur", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["infinitive", "lend", "compound"]),
                new("stroked", "brukash-thrum", PartOfSpeech: "verb", GrammarClass: "touch", Tags: ["past", "gentle", "compound"]),
                new("kindling", "rukh-hekin", PartOfSpeech: "verb", GrammarClass: "fire", Tags: ["progressive", "ignite", "compound"]),
                new("began", "ashdak-hekash", PartOfSpeech: "verb", GrammarClass: "sequence", Tags: ["past", "begin", "compound"]),
                new("began to", "ashdak-hekash ur", PartOfSpeech: "verb", GrammarClass: "sequence", Tags: ["past", "begin", "fixed-phrase"]),
                new("gather", "mokhu", PartOfSpeech: "verb", GrammarClass: "collection", Tags: ["infinitive", "gather"]),
                new("made his way", "lagash mogumuk lag", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past", "travel", "fixed-phrase"]),
                new("emanating", "rukh-oglarin", PartOfSpeech: "verb", GrammarClass: "light", Tags: ["progressive", "light", "compound"]),
                new("lingered", "grotash-dak", PartOfSpeech: "verb", GrammarClass: "delay", Tags: ["past", "linger", "compound"]),
                new("looking", "oglarin", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["progressive", "look"]),
                new("attracted", "dravash-nak", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past", "drawn", "compound"]),
                new("stood out", "turash-oglar", PartOfSpeech: "verb", GrammarClass: "appearance", Tags: ["past", "noticeable", "compound"]),
                new("given", "dravash", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["past-participle", "given"]),
                new("emblazoned", "nargash-ti", PartOfSpeech: "verb", GrammarClass: "symbol", Tags: ["past", "heraldic", "compound"]),
                new("wore", "khalash", PartOfSpeech: "verb", GrammarClass: "garb", Tags: ["past"]),
                new("had been", "tukash tuk", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past-perfect", "fixed-phrase"]),
                new("scrubbed", "rukh-vrakash", PartOfSpeech: "verb", GrammarClass: "cleaning", Tags: ["past", "clean", "compound"]),
                new("oiled", "rukh-thrumash", PartOfSpeech: "verb", GrammarClass: "maintenance", Tags: ["past", "oil", "compound"]),
                new("look", "oglar", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["infinitive"]),
                new("cast", "rukh-oglash", PartOfSpeech: "verb", GrammarClass: "light", Tags: ["past", "cast", "compound"]),
                new("stood in contrast to", "turash mok-nu ur", PartOfSpeech: "verb", GrammarClass: "comparison", Tags: ["past", "contrast", "fixed-phrase"]),
                new("hung haft-down", "zornash zol-bant-dok", PartOfSpeech: "verb", GrammarClass: "weapon", Tags: ["past", "haft-down", "fixed-phrase"]),
                new("hung", "zornash", PartOfSpeech: "verb", GrammarClass: "position", Tags: ["past", "hanging"]),
                new("capped", "mog-ti-hekash", PartOfSpeech: "verb", GrammarClass: "craft", Tags: ["past", "capped", "compound"]),
                new("centered over", "murkash dak-uk", PartOfSpeech: "verb", GrammarClass: "position", Tags: ["past", "centered", "fixed-phrase"]),
                new("angled", "lag-naksh", PartOfSpeech: "verb", GrammarClass: "position", Tags: ["past", "angled", "compound"]),
                new("bunched up", "mokhash-ti", PartOfSpeech: "verb", GrammarClass: "position", Tags: ["past", "gathered", "compound"]),
                new("spent", "dravash-dakur", PartOfSpeech: "verb", GrammarClass: "time", Tags: ["past", "spent", "compound"]),
                new("did draw", "dravash-oglar", PartOfSpeech: "verb", GrammarClass: "attention", Tags: ["past", "draw-attention", "fixed-phrase"]),
                new("waiting for", "grotin fa", PartOfSpeech: "verb", GrammarClass: "delay", Tags: ["progressive", "waiting", "fixed-phrase"]),
                new("passed", "lagash-dakur", PartOfSpeech: "verb", GrammarClass: "time", Tags: ["past", "passed", "compound"]),
                new("had come and gone again", "tukash ik-lagash agh dok-lagash ut", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past-perfect", "come-gone", "fixed-phrase"]),
                new("come", "ik-lagu", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive", "arrive", "compound"]),
                new("gone", "dok-lagash", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past", "departed", "compound"]),
                new("had been out there", "tukash dak dok-dak", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["past-perfect", "outside", "fixed-phrase"]),
                new("well set", "grod-dakkin dok", PartOfSpeech: "verb", GrammarClass: "celestial", Tags: ["sunset", "fixed-phrase"]),
                new("flitting about", "flitin nak", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "flying", "fixed-phrase"]),
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
                new("reflecting", "oglarin", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["progressive", "present", "figurative"]),
                new("relies on", "lag-tukur ak", PartOfSpeech: "verb", GrammarClass: "support", Tags: ["present", "dependence", "fixed-phrase"]),
                new("known as", "mog-oglar mok", PartOfSpeech: "verb", GrammarClass: "reputation", Tags: ["known", "fixed-phrase"]),
                new("responsible for safeguarding", "tukur-darg gorin", PartOfSpeech: "verb", GrammarClass: "protection", Tags: ["responsibility", "progressive", "fixed-phrase"]),
                new("emerging", "dok-varkin", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "origin", "compound"]),
                new("marked by", "nargash fa", PartOfSpeech: "verb", GrammarClass: "description", Tags: ["marked", "fixed-phrase"]),
                new("engaged in", "hekin ik", PartOfSpeech: "verb", GrammarClass: "labor", Tags: ["working", "fixed-phrase"]),
                new("living in the shadow", "dakkin k'ik arhk burz-nak", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["progressive", "shadow", "fixed-phrase"]),
                new("posed by", "nargash fa", PartOfSpeech: "verb", GrammarClass: "description", Tags: ["caused-by", "fixed-phrase"]),
                new("no strangers to", "noglar-nu ur", PartOfSpeech: "verb", GrammarClass: "experience", Tags: ["familiar", "fixed-phrase"]),
                new("presents", "dravur", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["present", "offers"]),
                new("aiding", "dravin", PartOfSpeech: "verb", GrammarClass: "support", Tags: ["progressive", "help"]),
                new("engaging in", "hekin ik", PartOfSpeech: "verb", GrammarClass: "labor", Tags: ["working", "fixed-phrase"]),
                new("slides into", "varkin ik", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["present", "entering", "fixed-phrase"]),
                new("pushing", "brukin", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "force"]),
                new("doesn't interfere with", "nu grotash ogh", PartOfSpeech: "verb", GrammarClass: "obstruction", Tags: ["negative", "fixed-phrase"]),
                new("interfere with", "grotash ogh", PartOfSpeech: "verb", GrammarClass: "obstruction", Tags: ["fixed-phrase"]),
                new("looks around", "oglur nak", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["present", "nearby", "fixed-phrase"]),
                new("marks", "nargur", PartOfSpeech: "verb", GrammarClass: "description", Tags: ["present", "marked"]),
                new("thinks", "thogur", PartOfSpeech: "verb", GrammarClass: "thought", Tags: ["present"]),
                new("sizing up", "oglar-thogin", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["assessing", "progressive", "compound"]),
                new("what do we have", "mok ughat tukra", PartOfSpeech: "verb", GrammarClass: "question", Tags: ["question", "fixed-phrase"]),
                new("What", "mok", PartOfSpeech: "pronoun", GrammarClass: "question", Tags: ["question"]),
                new("muses", "thogur-nak", PartOfSpeech: "verb", GrammarClass: "thought", Tags: ["present", "reflective", "compound"]),
                new("wearing", "khalin", PartOfSpeech: "verb", GrammarClass: "garb", Tags: ["progressive", "present"]),
                new("grunts", "grukhur", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["present", "rough"]),
                new("returns", "ut-lagur", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["present", "return"]),
                new("returns his attention to", "ut-oglarur mogumuk oglar-thog ur", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["present", "attention", "fixed-phrase"]),
                new("acknowledge", "nargu-thog", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["infinitive", "acknowledgement", "compound"]),
                new("acknowledges", "nargur-thog", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["present", "acknowledgement", "compound"]),
                new("acknowledged", "nargash-thog", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["past", "acknowledgement", "compound"]),
                new("moves", "lagur", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["present"]),
                new("moves to take", "lagur ur dravku", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["present", "taking", "fixed-phrase"]),
                new("take", "dravku", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["infinitive"]),
                new("stares into", "mur-oglur ik", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["present", "staring", "fixed-phrase"]),
                new("stares", "mur-oglur", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["present", "staring", "compound"]),
                new("bound for", "lagash fa", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past-participle", "destination", "fixed-phrase"]),
                new("have", "tukra", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["present"]),
                new("cripple", "kangsin", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["subject-complement"]),
                new("crippled", "kangsin", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["subject-complement", "past-participle"]),
                new("welcome", "mokra-dak", PartOfSpeech: "adjective", GrammarClass: "acceptance", Tags: ["friendly"]),
                new("well met", "mokra-narg", PartOfSpeech: "interjection", GrammarClass: "greeting", Tags: ["greeting", "fixed-phrase"]),
                new("Well met", "mokra-narg", PartOfSpeech: "interjection", GrammarClass: "greeting", Tags: ["greeting", "fixed-phrase"]),
                new("please", "mauk-drav", PartOfSpeech: "interjection", GrammarClass: "courtesy", Tags: ["request", "polite", "fixed-phrase"]),
                new("Obliged", "tukru-drav", PartOfSpeech: "interjection", GrammarClass: "courtesy", Tags: ["thanks", "debt", "fixed-phrase"]),
                new("abandoned", "nul-dakkin", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["deserted", "place"]),
                new("hidden", "noglar", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["concealed", "place"]),
                new("secret", "noglar", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["concealed"]),
                new("famous", "mur-oglar", PartOfSpeech: "adjective", GrammarClass: "reputation", Tags: ["known", "compound"]),
                new("expensive", "drav-ti", PartOfSpeech: "adjective", GrammarClass: "value", Tags: ["costly", "compound"]),
                new("braided", "bantin", PartOfSpeech: "adjective", GrammarClass: "appearance", Tags: ["braided"]),
                new("triple braided", "dug-agh-ash bantin", PartOfSpeech: "adjective", GrammarClass: "appearance", Tags: ["three", "braided", "fixed-phrase"]),
                new("hardworking", "mur-hekin", PartOfSpeech: "adjective", GrammarClass: "labor", Tags: ["labor", "intense", "compound"]),
                new("essential", "thruk", PartOfSpeech: "adjective", GrammarClass: "requirement", Tags: ["essential"]),
                new("communal", "mokhuk", PartOfSpeech: "adjective", GrammarClass: "society", Tags: ["communal", "possessive-derived"]),
                new("social", "mokhuk", PartOfSpeech: "adjective", GrammarClass: "society", Tags: ["social", "possessive-derived"]),
                new("modest", "thrum", PartOfSpeech: "adjective", GrammarClass: "degree", Tags: ["modest", "small"]),
                new("vital", "thruk-ti", PartOfSpeech: "adjective", GrammarClass: "requirement", Tags: ["vital", "essential", "intensified"]),
                new("following", "ut", PartOfSpeech: "adjective", GrammarClass: "sequence", Tags: ["following"]),
                new("lost", "nul-lag", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["missing", "place"]),
                new("dwarven", "dwarfuk", PartOfSpeech: "adjective", GrammarClass: "species", Tags: ["possessive-derived", "exonym"]),
                new("lanky", "thrum-yank", PartOfSpeech: "adjective", GrammarClass: "body", Tags: ["thin", "tall", "compound"]),
                new("comfortable", "grod-dakkin", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["comfortable", "compound"]),
                new("comforting", "grod-dakkin", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["comforting", "compound"]),
                new("further", "dok-ti", PartOfSpeech: "adjective", GrammarClass: "distance", Tags: ["farther", "compound"]),
                new("familiar", "noglar-nu", PartOfSpeech: "adjective", GrammarClass: "experience", Tags: ["familiar", "compound"]),
                new("evening", "naut-dakur", PartOfSpeech: "adjective", GrammarClass: "time", Tags: ["evening", "compound"]),
                new("honest", "grak-tur", PartOfSpeech: "adjective", GrammarClass: "virtue", Tags: ["honest", "compound"]),
                new("open", "lag-nu-gor", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["open", "compound"]),
                new("friendly", "mokra-grod", PartOfSpeech: "adjective", GrammarClass: "acceptance", Tags: ["friendly", "compound"]),
                new("missing", "nul-lag", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["missing", "place"]),
                new("capable", "grod-yank", PartOfSpeech: "adjective", GrammarClass: "skill", Tags: ["capable", "strong", "compound"]),
                new("better than", "grod-ti mok", PartOfSpeech: "adjective", GrammarClass: "comparison", Tags: ["comparative", "fixed-phrase"]),
                new("faintest", "thrum-mur", PartOfSpeech: "adjective", GrammarClass: "degree", Tags: ["faint", "superlative", "compound"]),
                new("uncertain", "nul-grak-thog", PartOfSpeech: "adjective", GrammarClass: "thought", Tags: ["uncertain", "negative", "compound"]),
                new("almost", "nak-grak", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["almost", "compound"]),
                new("sore", "morz", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["pain"]),
                new("white", "kelnib", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["white", "pale"]),
                new("recently", "dakur-nak", PartOfSpeech: "adverb", GrammarClass: "time", Tags: ["recent", "compound"]),
                new("cloying", "murgrom-kaag", PartOfSpeech: "adjective", GrammarClass: "sense", Tags: ["smell", "excess", "compound"]),
                new("round", "bant-murkuk", PartOfSpeech: "adjective", GrammarClass: "shape", Tags: ["round", "compound"]),
                new("a size too large", "ash thrum-dak murgrom-ti", PartOfSpeech: "adjective", GrammarClass: "size", Tags: ["oversized", "fixed-phrase"]),
                new("beardless", "nul-drath-khal", PartOfSpeech: "adjective", GrammarClass: "body", Tags: ["beardless", "negative", "compound"]),
                new("boyish", "nurik-margiuk", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["boyish", "possessive-derived"]),
                new("smooth", "thrum-vrak", PartOfSpeech: "adjective", GrammarClass: "appearance", Tags: ["smooth", "compound"]),
                new("fair", "drav-mauk", PartOfSpeech: "adjective", GrammarClass: "appearance", Tags: ["fair", "pleasant", "compound"]),
                new("downy", "thrum-khal", PartOfSpeech: "adjective", GrammarClass: "appearance", Tags: ["soft-hair", "compound"]),
                new("blond", "surg", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["blond", "sun"]),
                new("above average", "ti-grak", PartOfSpeech: "adjective", GrammarClass: "comparison", Tags: ["above-average", "compound"]),
                new("strapping", "yank-grod", PartOfSpeech: "adjective", GrammarClass: "body", Tags: ["strong", "compound"]),
                new("starker", "mur-oglar-ti", PartOfSpeech: "adjective", GrammarClass: "comparison", Tags: ["starker", "comparative", "compound"]),
                new("brass", "zol-mauk", PartOfSpeech: "adjective", GrammarClass: "material", Tags: ["brass", "metal", "compound"]),
                new("stout", "yank-grod", PartOfSpeech: "adjective", GrammarClass: "strength", Tags: ["stout", "strong", "compound"]),
                new("shallow", "thrum-burz", PartOfSpeech: "adjective", GrammarClass: "shape", Tags: ["shallow", "compound"]),
                new("same", "grak-mok", PartOfSpeech: "adjective", GrammarClass: "comparison", Tags: ["same", "compound"]),
                new("awkward", "lag-grot", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["awkward", "compound"]),
                new("awkwardly", "lag-grotin", PartOfSpeech: "adverb", GrammarClass: "condition", Tags: ["awkward", "compound"]),
                new("leather", "vrak", PartOfSpeech: "adjective", GrammarClass: "material", Tags: ["leather", "hide"]),
                new("laboring", "hekin", PartOfSpeech: "adjective", GrammarClass: "labor", Tags: ["labor", "progressive"]),
                new("many", "mur", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["many"]),
                new("handsome", "mauk-mogum", PartOfSpeech: "adjective", GrammarClass: "appearance", Tags: ["handsome", "compound"]),
                new("lingering", "grotin-dak", PartOfSpeech: "adjective", GrammarClass: "delay", Tags: ["lingering", "compound"]),
                new("occasional", "varg-dakur", PartOfSpeech: "adjective", GrammarClass: "time", Tags: ["occasional", "compound"]),
                new("truly", "grak-tur", PartOfSpeech: "adverb", GrammarClass: "certainty", Tags: ["truth", "compound"]),
                new("never", "nul-dakur", PartOfSpeech: "adverb", GrammarClass: "time", Tags: ["never", "negative", "compound"]),
                new("little", "thrum", PartOfSpeech: "adjective", GrammarClass: "quantity", Tags: ["small"]),
                new("small", "thrum", PartOfSpeech: "adjective", GrammarClass: "quantity", Tags: ["small"]),
                new("new", "nurik", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["new"]),
                new("older", "drath-ti", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["comparative"]),
                new("quiet", "thrum-narg", PartOfSpeech: "adjective", GrammarClass: "sound", Tags: ["quiet", "compound"]),
                new("common", "mokhuk", PartOfSpeech: "adjective", GrammarClass: "society", Tags: ["common", "possessive-derived"]),
                new("long", "mur-dakur", PartOfSpeech: "adjective", GrammarClass: "time", Tags: ["long", "compound"]),
                new("lone", "ash-mog", PartOfSpeech: "adjective", GrammarClass: "quantity", Tags: ["alone", "compound"]),
                new("dangerous", "vark-thoguk", PartOfSpeech: "adjective", GrammarClass: "danger", Tags: ["danger", "possessive-derived"]),
                new("warm", "rukh-grod", PartOfSpeech: "adjective", GrammarClass: "temperature", Tags: ["warm", "compound"]),
                new("single", "ash", PartOfSpeech: "adjective", GrammarClass: "quantity", Tags: ["single"]),
                new("toothless", "nul-togruk", PartOfSpeech: "adjective", GrammarClass: "body", Tags: ["toothless", "negative", "compound"]),
                new("hoary", "murdrath-kelnib", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["very-old", "pale", "compound"]),
                new("hoary with age", "murdrath-kelnib dakuruk", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["very-old", "fixed-phrase"]),
                new("well into his second century", "grak ik mogumuk dug mur-dakur", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["aged", "fixed-phrase"]),
                new("still youthful enough", "ashdak nurik grod", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["youthful", "sufficient", "fixed-phrase"]),
                new("youthful", "nurik-grod", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["young", "vigorous", "compound"]),
                new("red", "rug", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["default"]),
                new("Red", "rug", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["default"]),
                new("formal", "bib-darguk", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["formal", "written-law", "compound"]),
                new("significant", "thrak-grak", PartOfSpeech: "adjective", GrammarClass: "importance", Tags: ["important", "compound"]),
                new("defensive", "goruk", PartOfSpeech: "adjective", GrammarClass: "protection", Tags: ["defense", "possessive-derived"]),
                new("brave", "yanki-grod", PartOfSpeech: "adjective", GrammarClass: "virtue", Tags: ["courage", "compound"]),
                new("untrained", "nul-hekin", PartOfSpeech: "adjective", GrammarClass: "skill", Tags: ["untrained", "negative", "compound"]),
                new("responsible", "tukur-darg", PartOfSpeech: "adjective", GrammarClass: "duty", Tags: ["responsibility", "compound"]),
                new("nearby", "nak", PartOfSpeech: "adjective", GrammarClass: "location", Tags: ["nearby"]),
                new("rugged", "mur-grod", PartOfSpeech: "adjective", GrammarClass: "virtue", Tags: ["rugged", "compound"]),
                new("resilient", "grotash-nu", PartOfSpeech: "adjective", GrammarClass: "virtue", Tags: ["resilient", "negative-break", "compound"]),
                new("chief", "thrak", PartOfSpeech: "adjective", GrammarClass: "importance", Tags: ["primary", "important"]),
                new("local", "nak-dakuk", PartOfSpeech: "adjective", GrammarClass: "place", Tags: ["local", "possessive-derived"]),
                new("religious", "mograthuk", PartOfSpeech: "adjective", GrammarClass: "religion", Tags: ["possessive-derived"]),
                new("puritanical", "mur-mograth-darg", PartOfSpeech: "adjective", GrammarClass: "religion", Tags: ["strict", "religious", "compound"]),
                new("more puritanical", "mur-mograth-darg-ti", PartOfSpeech: "adjective", GrammarClass: "religion", Tags: ["strict", "comparative", "religious", "compound"]),
                new("relaxed", "thrum-darg", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["lenient", "law", "compound"]),
                new("more relaxed", "thrum-darg-ti", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["lenient", "comparative", "law", "compound"]),
                new("lenient", "thrum-darg", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["lenient", "law", "compound"]),
                new("more lenient", "thrum-darg-ti", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["lenient", "comparative", "law", "compound"]),
                new("rigid", "mur-darg", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["strict", "law", "compound"]),
                new("less rigid", "mur-darg-nu-ti", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["less", "strict", "comparative", "law", "compound"]),
                new("notably more lenient", "oglar-ti thrum-darg-ti", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["noticeable", "lenient", "comparative", "fixed-phrase"]),
                new("approach", "lag-thog", PartOfSpeech: "noun", GrammarClass: "position", Tags: ["viewpoint", "compound"]),
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
                new("He", "mogum", PartOfSpeech: "pronoun", GrammarClass: "third-person", Tags: ["masculine", "plain"]),
                new("him", "mogum", PartOfSpeech: "pronoun", GrammarClass: "third-person", Tags: ["masculine", "object"]),
                new("his", "mogumuk", PartOfSpeech: "pronoun", GrammarClass: "third-person", Tags: ["masculine", "possessive"]),
                new("her", "umuk", PartOfSpeech: "pronoun", GrammarClass: "third-person", Tags: ["feminine", "object"]),
                new("they", "ughat", PartOfSpeech: "pronoun", GrammarClass: "other", Tags: ["plural", "plain"]),
                new("them", "ughatum", PartOfSpeech: "pronoun", GrammarClass: "other", Tags: ["plural", "object"]),
                new("who", "lek", PartOfSpeech: "pronoun", GrammarClass: "relative", Tags: ["relative"]),
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
                new("notably", "oglar-ti", PartOfSpeech: "adverb", GrammarClass: "reputation", Tags: ["noticeable", "compound"]),
                new("primarily", "thrak-grak", PartOfSpeech: "adverb", GrammarClass: "importance", Tags: ["primary", "compound"]),
                new("significantly", "thrak-grak", PartOfSpeech: "adverb", GrammarClass: "importance", Tags: ["important", "compound"]),
                new("Additionally", "agh-agh", PartOfSpeech: "adverb", GrammarClass: "addition", Tags: ["additive", "compound"]),
                new("also", "agh-agh", PartOfSpeech: "adverb", GrammarClass: "addition", Tags: ["additive", "compound"]),
                new("albeit", "rokh", PartOfSpeech: "adverb", GrammarClass: "contrast", Tags: ["concession"]),
                new("particularly", "thrak-grak", PartOfSpeech: "adverb", GrammarClass: "importance", Tags: ["particular", "compound"]),
                new("often", "murdakur", PartOfSpeech: "adverb", GrammarClass: "time", Tags: ["frequent", "compound"]),
                new("only", "thrum-grak", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["only", "limiting", "compound"]),
                new("least", "thrum-grak", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["minimum", "compound"]),
                new("no further", "nu dok-ti", PartOfSpeech: "adverb", GrammarClass: "distance", Tags: ["negative", "fixed-phrase"]),
                new("at most", "ak mur", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["maximum", "fixed-phrase"]),
                new("out there", "dak dok-dak", PartOfSpeech: "adverb", GrammarClass: "location", Tags: ["outside", "fixed-phrase"]),
                new("however", "rokh-grak", PartOfSpeech: "adverb", GrammarClass: "contrast", Tags: ["contrastive", "compound"]),
                new("unconsciously", "nul-thogin", PartOfSpeech: "adverb", GrammarClass: "thought", Tags: ["unconscious", "negative", "compound"]),
                new("back", "dok", PartOfSpeech: "adverb", GrammarClass: "direction", Tags: ["back"]),
                new("not", "nu", PartOfSpeech: "adverb", GrammarClass: "negation", Tags: ["negative"]),
                new("still", "ashdak", PartOfSpeech: "adverb", GrammarClass: "continuity", Tags: ["continuing"]),
                new("Still", "ashdak", PartOfSpeech: "adverb", GrammarClass: "continuity", Tags: ["continuing"]),
                new("at least", "thrum-grak", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["minimum", "fixed-phrase"]),
                new("For now", "dakur-lek", PartOfSpeech: "adverb", GrammarClass: "time", Tags: ["present", "fixed-phrase"]),
                new("Comfortably", "grod-dakkin", PartOfSpeech: "adverb", GrammarClass: "condition", Tags: ["comfort", "compound"]),
                new("though", "rokh", PartOfSpeech: "adverb", GrammarClass: "contrast", Tags: ["concession"]),
                new("alone", "ash-mog", PartOfSpeech: "adverb", GrammarClass: "quantity", Tags: ["alone", "compound"]),
                new("thoughtfully", "thogin-grak", PartOfSpeech: "adverb", GrammarClass: "thought", Tags: ["thoughtful", "compound"]),
                new("even", "agh-grak", PartOfSpeech: "adverb", GrammarClass: "emphasis", Tags: ["inclusive", "compound"]),
                new("now", "dakur-lek", PartOfSpeech: "adverb", GrammarClass: "time", Tags: ["present", "compound"]),
                new("Although", "rokh-ut", PartOfSpeech: "adverb", GrammarClass: "contrast", Tags: ["concession", "compound"]),
                new("initially", "ashdak", PartOfSpeech: "adverb", GrammarClass: "sequence", Tags: ["initial"]),
                new("then", "ut-dakur", PartOfSpeech: "adverb", GrammarClass: "sequence", Tags: ["then", "sequence", "compound"]),
                new("then to", "ut-dakur ur", PartOfSpeech: "adverb", GrammarClass: "sequence", Tags: ["then", "direction", "fixed-phrase"]),
                new("of course", "grak-tur", PartOfSpeech: "adverb", GrammarClass: "certainty", Tags: ["fixed-phrase"]),
                new("too much", "murgrom", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["excess"]),
                new("much more", "mur-ti", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["comparative", "compound"]),
                new("much more than", "mur-ti mok", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["comparative", "fixed-phrase"]),
                new("those", "lek", PartOfSpeech: "determiner", GrammarClass: "demonstrative"),
                new("these", "lek", PartOfSpeech: "determiner", GrammarClass: "demonstrative"),
                new("this", "um-lek", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["singular", "near"]),
                new("other such", "agh-lek", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["similar", "additional", "fixed-phrase"]),
                new("three", "dug-agh-ash", PartOfSpeech: "numeral", GrammarClass: "cardinal"),
                new("ten", "gakh", PartOfSpeech: "numeral", GrammarClass: "cardinal"),
                new("dozen", "gakh-agh-dug", PartOfSpeech: "numeral", GrammarClass: "cardinal", Tags: ["twelve", "compound"]),
                new("few", "nik", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["small-quantity"]),
                new("most", "mur", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["majority"]),
                new("some", "varg", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["indefinite"]),
                new("both", "dug-grak", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["both", "two", "compound"]),
                new("Both", "dug-grak", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["both", "two", "compound"]),
                new("enough", "grod-grak", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["sufficient", "compound"]),
                new("all", "mur", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["all"]),
                new("each", "ash-ash", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["each", "compound"]),
                new("any", "varg", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["any"]),
                new("such", "lek-mok", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["such", "compound"]),
                new("first", "ash", PartOfSpeech: "numeral", GrammarClass: "ordinal", Tags: ["first"]),
                new("one", "ash", PartOfSpeech: "numeral", GrammarClass: "cardinal", Tags: ["one"]),
                new("twenty", "dug-gakh", PartOfSpeech: "numeral", GrammarClass: "cardinal", Tags: ["twenty", "compound"]),
                new("trio", "dug-agh-ash", PartOfSpeech: "numeral", GrammarClass: "cardinal", Tags: ["three"]),
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
                new("under", "dak-uk", PartOfSpeech: "preposition", GrammarClass: "authority", Tags: ["dominion", "compound"]),
                new("around", "nak", PartOfSpeech: "preposition", GrammarClass: "location", Tags: ["nearby"]),
                new("near", "nak", PartOfSpeech: "preposition", GrammarClass: "location", Tags: ["nearby"]),
                new("with", "ogh", PartOfSpeech: "preposition", GrammarClass: "association", Tags: ["default"]),
                new("for", "fa", PartOfSpeech: "preposition", GrammarClass: "purpose", Tags: ["purpose"]),
                new("access to", "lag ur", PartOfSpeech: "preposition", GrammarClass: "access", Tags: ["access", "fixed-phrase"]),
                new("against", "mok-nu", PartOfSpeech: "preposition", GrammarClass: "opposition", Tags: ["opposition", "compound"]),
                new("into", "ik", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["interior"]),
                new("inside", "ik", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["interior"]),
                new("toward", "ur", PartOfSpeech: "preposition", GrammarClass: "direction", Tags: ["direction"]),
                new("until", "ur-dakur", PartOfSpeech: "preposition", GrammarClass: "time", Tags: ["until", "compound"]),
                new("between", "murk", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["between"]),
                new("across", "dak-nak", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["across", "compound"]),
                new("beside", "mokru-nak", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["beside", "compound"]),
                new("by", "nak", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["nearby"]),
                new("behind", "dok", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["behind"]),
                new("about", "nak", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["nearby"]),
                new("than", "mok", PartOfSpeech: "preposition", GrammarClass: "comparison", Tags: ["comparative"]),
                new("through", "mokru-ik", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["through", "compound"]),
                new("upon", "dak-uk", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["upon"]),
                new("beneath", "burz-nak", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["beneath", "compound"]),
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
                new("as does", "mok-grak", PartOfSpeech: "conjunction", GrammarClass: "comparison", Tags: ["equivalence", "fixed-phrase"]),
                new("be it", "tar ut", PartOfSpeech: "conjunction", GrammarClass: "choice", Tags: ["alternative", "fixed-phrase"]),
                new("so that", "mok-ut", PartOfSpeech: "conjunction", GrammarClass: "purpose", Tags: ["purpose", "fixed-phrase"]),
                new("that", "ut", PartOfSpeech: "conjunction", GrammarClass: "relative", Tags: ["relative"]),
                new("while", "rokh-dakur", PartOfSpeech: "conjunction", GrammarClass: "time", Tags: ["while", "compound"]),
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
                new("inhabitant", "dak-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["inhabited", "resident", "compound"]),
                new("inhabitants", "dak-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["inhabited", "resident", "plural", "compound"]),
                new("resident", "dak-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["inhabited", "resident", "compound"]),
                new("residents", "dak-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["inhabited", "resident", "plural", "compound"]),
                new("adventurer", "vark-yank-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["danger", "wayfarer", "compound"]),
                new("adventurers", "vark-yank-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["danger", "wayfarer", "plural", "compound"]),
                new("dwarf", "dwarf", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["dwarven-race", "species", "exonym"]),
                new("noble", "darg-ti-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["ruler", "noble", "compound"]),
                new("member", "mokh-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["group", "compound"]),
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
