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

        public static IReadOnlyList<OrcishLexiconEntry> GetLexiconEntries()
        {
            return LexiconEntries;
        }

        public static IReadOnlyList<OrcishAffixEntry> GetAffixEntries()
        {
            return AffixEntries;
        }

        public static IReadOnlyList<OrcishLexiconReviewIssue> ReviewProposedLexiconEntry(
            OrcishLexiconEntry proposedEntry)
        {
            return OrcishLexiconReviewUtility.ReviewProposedEntry(proposedEntry, LexiconEntries);
        }

        public static void EnsureProposedLexiconEntryCanBeAdded(OrcishLexiconEntry proposedEntry)
        {
            OrcishLexiconReviewUtility.EnsureCanAdd(proposedEntry, LexiconEntries);
        }

        private static OrcishLexiconEntry[] BuildLexiconEntries()
        {
            // Review every proposed addition with ReviewProposedLexiconEntry before placing it here.
            var entries = new List<OrcishLexiconEntry>
            {
                new("hello", "zug"),
                new("friend", "mokra", PartOfSpeech: "noun", GrammarClass: "kinship"),
                new("ally", "mokra", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["root-repaired"]),
                new("humanoid", "mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "species"]),
                new("human", "margi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["neutral", "species"]),
                new("man", "margi-ash-rukh", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["neutral", "species"]),
                new("inferior other", "margi-flit-vrak", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["pejorative", "outsider", "broad-gloss"]),
                new("humans", "margith", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["neutral", "species", "plural"]),
                new("humanity", "margith", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["shared-form", "human", "collective", "species", "wiki-fodder"]),
                new("men", "margith", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["neutral", "species", "plural", "root-repaired"]),
                new("inferior others", "margith-vrak-zog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["pejorative", "outsider", "plural", "broad-gloss"]),
                new("obviously inferior others", "margith-grrt-gash", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["pejorative", "outsider", "plural", "emphatic", "broad-gloss"]),
                new("human's", "margiuk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["neutral", "species", "possessive"]),
                new("man's", "margiuk-grod-krag", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["neutral", "species", "possessive"]),
                new("softskin", "thrum-skin", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["insulting", "species"]),
                new("weak human", "thrum-skin", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["insulting", "species", "root-repaired"]),
                new("softskins", "thrum-skinar", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["insulting", "species", "plural"]),
                new("weak humans", "thrum-skinar", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["insulting", "species", "plural", "root-repaired"]),
                new("softskin's", "thrum-skinuk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["insulting", "species", "possessive"]),
                new("children of Gruumsh", "mogra", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["orc", "collective", "identity"]),
                new("favored children of Gruumsh", "mogra-ti", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["orc", "collective", "identity", "favored"]),
                new("superior children of Gruumsh", "mogra-ti", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["orc", "collective", "identity", "superior", "root-repaired"]),
                new("githyanki", "githyanki", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["exonym", "historical", "orc-origin"]),
                new("one of unexpected strength", "yanki", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["historical", "unexpected-strength"]),
                new("sun-born", "surgar", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["respectful", "species"]),
                new("free human", "surgar", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["respectful", "species", "root-repaired"]),
                new("sun-born ones", "surgari", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["respectful", "species", "plural"]),
                new("free humans", "surgari", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["respectful", "species", "plural", "root-repaired"]),
                new("sun-born's", "surgaruk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["respectful", "species", "possessive"]),
                new("skin", "vrak", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["neutral", "default"]),
                new("skins", "vraki", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["neutral", "default", "plural"]),
                new("forest", "gruul", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["default", "wilderness"]),
                new("wilderness", "vril-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["wild", "wilderness", "compound"]),
                new("hedge", "vrul", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["default", "growth"]),
                new("woods", "vril", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["default", "wilderness", "plural-mass"]),
                new("Darkwood", "Burz-vril", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "woods", "compound"]),
                new("mountain", "ti-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["height", "compound"]),
                new("mountains", "ti-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["height", "plural", "compound"]),
                new("shadow", "burz-nak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["dark", "nearby", "compound"]),
                new("gloom", "burz-thog", PartOfSpeech: "noun", GrammarClass: "light", Tags: ["dark", "abstract", "compound"]),
                new("dusk", "naut-ik", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["evening", "darkening", "compound"]),
                new("gloom of dusk", "burz-thog uk naut-ik", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["gloom", "dusk", "fixed-phrase"]),
                new("path", "lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["default", "route"]),
                new("road", "lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["default", "route", "made-path", "root-repaired"]),
                new("trail", "lag-vril", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["default", "route", "wild-path", "root-repaired"]),
                new("paths", "lagi", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["default", "route", "plural"]),
                new("roads", "lagi", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["default", "route", "plural", "root-repaired"]),
                new("trails", "lag-vrili", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["default", "route", "plural", "root-repaired"]),
                new("built road", "hek-lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["built", "route"]),
                new("built path", "hek-lag-thog", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["built", "route", "root-repaired"]),
                new("wild trail", "vril-lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["wild", "wilderness", "route"]),
                new("woods path", "gruul-lag-dak", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["woods", "route", "root-repaired"]),
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
                new("Darkwood Forest", "Burz-gruul", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "forest", "compound", "root-repaired"]),
                new("Raven's Pass", "Ravenuk Lag", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "pass", "exonym", "fixed-phrase"]),
                new("Raven’s Pass", "Ravenuk Lag", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "pass", "exonym", "fixed-phrase", "root-repaired"]),
                new("Eastdale", "Eastdale", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement"]),
                new("Westkeep", "Westkeep", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "settlement"]),
                new("Middenmark", "Middenmark", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "region"]),
                new("St", "St", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "title", "exonym"]),
                new("Ygg", "Ygg", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("region", "dak-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["area", "compound"]),
                new("regional", "dak-mokhuk", PartOfSpeech: "adjective", GrammarClass: "place", Tags: ["area", "possessive-derived"]),
                new("area", "dak-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["area", "compound", "root-repaired", "shortened", "derive-plural", "base-area"]),
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
                new("thorpes", "thrum-quum-mog-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["small", "rural", "inhabited", "plural", "compound"]),
                new("towns", "mog-dak-muri", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["settlement", "medium", "plural", "compound"]),
                new("church", "mograth-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["religious", "compound"]),
                new("kirk", "mograth-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["religious", "synonym", "small-church", "compound", "root-repaired"]),
                new("temple", "mograth-dak-ti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["religious", "major", "compound"]),
                new("Red Temple", "rug-mograth-dak-ti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "religious", "red-law", "compound"]),
                new("Watchtower", "gor-ti-hek", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["watch", "built", "compound"]),
                new("tower", "ti-hek", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["built", "tall", "compound"]),
                new("wall", "gor-hek", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["defense", "built", "compound"]),
                new("formal wall", "bib-darguk gor-hek", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["defense", "formal", "built", "fixed-phrase"]),
                new("structure", "hek-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["built", "compound"]),
                new("infrastructure", "burz-hek-dak", PartOfSpeech: "noun", GrammarClass: "structure", Tags: ["compound", "compound-reviewed", "under", "structure", "wiki-fodder"]),
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
                new("cottage", "dakku-dak-thrum", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["dwelling", "small", "compound"]),
                new("cottage's", "dakku-dak-thrumuk", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["dwelling", "small", "possessive", "compound"]),
                new("booth", "dakku-burz", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["seat", "tavern", "compound"]),
                new("hearth", "rukh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["fire", "dwelling", "compound"]),
                new("dining area", "quum-dak-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["food", "area", "compound"]),
                new("kitchen", "rukh-quum-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["food", "cooking", "compound"]),
                new("common room", "mokh-rukh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["communal", "tavern", "compound", "root-repaired"]),
                new("room", "dak-burz", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["interior", "compound"]),
                new("inn", "rukh-dakku-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["tavern", "lodging", "compound", "root-repaired"]),
                new("well", "dak-rukh-burz", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["water", "deep", "compound"]),
                new("within", "ik-burz", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["interior", "compound"]),
                new("square", "murk-mokh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["central", "public", "compound"]),
                new("outside", "dok-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["exterior", "compound"]),
                new("market area", "drav-dak-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["trade", "area", "compound"]),
                new("small market area", "nik-drav-dak-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["trade", "small", "area", "fixed-phrase"]),
                new("map", "bibnak", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["default", "text", "directional"]),
                new("maps", "bibnaki", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["default", "text", "directional", "plural"]),
                new("book", "bib", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["default", "text"]),
                new("scroll", "bib-khal", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["default", "text", "rolled-cloth", "root-repaired"]),
                new("book-man", "bib-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["broad-gloss", "scholar", "text"]),
                new("morsel", "quum-bit", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["small", "food", "compound"]),
                new("Morsels", "quum-biti", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["small", "food", "plural", "compound"]),
                new("tankard", "rukh-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["drink", "vessel", "compound"]),
                new("Tankards", "rukh-banti", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["drink", "vessel", "plural", "compound"]),
                new("Morgan's Morsels & Tankards", "Morganuk quum-biti agh rukh-banti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "tavern", "fixed-phrase"]),
                new("Morgan’s Morsels & Tankards", "Morganuk quum-biti agh rukh-banti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "tavern", "fixed-phrase", "root-repaired"]),
                new("&", "agh", PartOfSpeech: "conjunction", GrammarClass: "addition", Tags: ["symbol"]),
                new("ale", "rukh-quum", PartOfSpeech: "noun", GrammarClass: "drink", Tags: ["fermented", "grain", "compound"]),
                new("soup", "rukh-quum", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["liquid", "food", "thin", "compound", "root-repaired"]),
                new("stew", "rukh-quum-ti", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["liquid", "food", "hearty", "compound"]),
                new("bread", "hek-quum", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["baked", "grain", "compound"]),
                new("dough", "hek-quum-thrum", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["baked", "unbaked", "soft", "compound"]),
                new("potato", "dak-quum", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["root", "earth", "compound"]),
                new("potatoes", "dak-quumi", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["root", "earth", "plural", "compound"]),
                new("meat", "quum-vrak", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["flesh", "food", "compound"]),
                new("pork", "vril-quum-vrak", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["boar", "flesh", "compound"]),
                new("carcass", "nul-dakur-vrak", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["dead", "flesh", "compound"]),
                new("seat", "dok-gash", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["seat", "furniture", "compound"]),
                new("counter", "rukh-quum-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["food", "surface", "compound"]),
                new("bucket", "rukh-bant-ti", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["water", "vessel", "compound"]),
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
                new("hauberk", "zol-bant-khal-flit-drak", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["armor", "chainmail", "compound"]),
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
                new("blade", "zol-rukh-vark", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "edge", "danger", "compound", "root-repaired"]),
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
                new("chest", "grod-burz-ik-burzuk", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["body", "heart", "inner", "compound"]),
                new("head", "mog-ti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["body", "compound"]),
                new("arm", "yank-bant", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["body", "compound"]),
                new("arms", "yank-banti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["body", "plural", "compound"]),
                new("chin", "narg-bant", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["face", "compound"]),
                new("thumb", "krub-ti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["hand", "thumb", "compound"]),
                new("sore thumb", "morz-krub-ti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["sore", "thumb", "fixed-phrase"]),
                new("feet", "kruki", PartOfSpeech: "noun", GrammarClass: "measure", Tags: ["distance", "plural"]),
                new("flame", "rukh-tur", PartOfSpeech: "noun", GrammarClass: "fire", Tags: ["fire", "burning", "compound"]),
                new("flames", "rukh-turi", PartOfSpeech: "noun", GrammarClass: "fire", Tags: ["fire", "burning", "plural", "compound"]),
                new("hide", "vrak-drukh", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["neutral", "skin", "hide", "compound", "root-repaired"]),
                new("hides", "vraki", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["neutral", "skin", "hide", "default", "plural", "compound", "root-repaired"]),
                new("hide", "drukh", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["reverent", "monster", "thick-hide"]),
                new("hides", "drukhi", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["reverent", "monster", "thick-hide", "plural"]),
                new("rope", "bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["neutral", "default"]),
                new("ropes", "bant-mokhi", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["neutral", "default", "plural", "bundled", "root-repaired"]),
                new("rope's", "bantuk", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["neutral", "default", "possessive"]),
                new("braid", "bant-var", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["braid", "compound"]),
                new("cape", "khal", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["garb", "default"]),
                new("pommel", "zol-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "handle", "small-handle", "compound", "root-repaired"]),
                new("sword", "zol-gash", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "iron", "compound"]),
                new("broadsword", "zol-gash-ti", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "sword", "large", "compound"]),
                new("dagger", "zol-bit", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "small", "compound"]),
                new("weapon", "zol-gash-dak-ash", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "default"]),
                new("armor", "zol-vrak", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["armor", "iron", "compound"]),
                new("armour", "zol-vrak-khal", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["armor", "iron", "british", "covering", "compound", "root-repaired"]),
                new("sallet", "zol-mog-ti-khal-bit", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["helmet", "armor", "small", "compound"]),
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
                new("thinker", "thog-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["thoughtful", "default", "root-repaired"]),
                new("smith", "hekruhur", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["craft", "specialized"]),
                new("smiths", "hekruhuri", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["craft", "specialized", "plural"]),
                new("blacksmith", "zol-hekruhur", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["craft", "iron", "specialized", "compound"]),
                new("traveler", "fletragi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["outsider", "wayfarer", "default"]),
                new("travelers", "fletragith", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["outsider", "wayfarer", "default", "plural"]),
                new("traveller", "fletragi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["outsider", "wayfarer", "variant-spelling", "root-repaired"]),
                new("settler", "dak-hekmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["settlement", "worker", "compound"]),
                new("settlers", "dak-hekmogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["settlement", "worker", "plural", "compound"]),
                new("innkeeper", "rukh-dak darg-dravik", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["tavern", "owner", "fixed-phrase", "root-repaired"]),
                new("caller", "darg-narg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["leader", "speech", "compound"]),
                new("chronicler", "bib-mog-bant-doku", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["record", "text", "compound"]),
                new("quartermaster", "hrowk-darg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["supplies", "authority", "compound"]),
                new("mapper", "bibnak-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["map", "directional", "compound"]),
                new("bannerman", "narg-zol-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["banner", "martial", "compound"]),
                new("squire", "gash-darg-thrum-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["warrior", "junior", "compound"]),
                new("hedge-wizard", "gurmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "specialized"]),
                new("hedge-wizards", "gurmogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "specialized", "plural"]),
                new("wizard", "gurmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "specialized", "root-repaired"]),
                new("wizards", "gurmogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "specialized", "plural", "root-repaired"]),
                new("witch", "gurmog-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "specialized", "compound"]),
                new("good witch", "grod-gurmog-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "helpful", "fixed-phrase"]),
                new("healer", "morz-mograth-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["healing", "religious", "compound"]),
                new("enchantress", "mauk-mograth-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "female", "compound"]),
                new("sorceress", "krug-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "female", "compound"]),
                new("blood sorceress", "pukh-krug-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "blood", "fixed-phrase"]),
                new("fighter", "gash", PartOfSpeech: "noun", GrammarClass: "person"),
                new("fighters", "gash-darg-morz", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["plural"]),
                new("warrior", "gash", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["root-repaired"]),
                new("farmer", "quum-hekmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["farming", "worker", "compound"]),
                new("farmers", "quum-hekmogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["farming", "worker", "plural", "compound"]),
                new("farmer's", "quum-hekmoguk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["farming", "worker", "possessive", "compound"]),
                new("farmer’s", "quum-hekmoguk-lag-thog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["farming", "worker", "possessive", "compound"]),
                new("lumberjack", "gruul-hek-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["wood", "worker", "compound"]),
                new("lumberjacks", "gruul-hek-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["wood", "worker", "plural", "compound"]),
                new("knight", "zol-gash-darg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["warrior", "noble", "compound"]),
                new("Slip", "Slip", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Morgan", "Morgan", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Morgan's", "Morganuk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym", "possessive"]),
                new("Kelpie", "Kelpie", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym", "root-repaired"]),
                new("Demetra", "Demetra", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "deity", "exonym"]),
                new("Xavamros", "Xavamros", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Battlebeard", "Battlebeard", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Brand", "Brand", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Governor", "darg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["title", "ruler", "compound"]),
                new("Prince", "darg-ti-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["title", "ruler", "higher-than-governor", "compound"]),
                new("Xavin", "Xavin", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("Petre", "Petre", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("cleric", "mograth", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "default", "root-repaired"]),
                new("clerics", "mograthi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "default", "plural", "root-repaired"]),
                new("orc", "orukh", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["species", "orc"]),
                new("orcs", "orukhi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["species", "orc", "plural"]),
                new("goblin", "goblin", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["species", "exonym"]),
                new("goblins", "goblini", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["species", "exonym", "plural"]),
                new("kobold", "kobold", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["species", "exonym"]),
                new("kobolds", "koboldi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["species", "exonym", "plural"]),
                new("watch", "thrak", PartOfSpeech: "noun", GrammarClass: "object"),
                new("torch-bearer", "rukh-tur-hrowka", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["fire", "carrier", "compound"]),
                new("torch-bearers", "rukh-tur-hrowkai", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["fire", "carrier", "plural", "compound"]),
                new("NPC", "nul-narg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["game-term", "non-player"]),
                new("NPCs", "nul-narg-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["game-term", "non-player", "plural"]),
                new("PC", "narg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["game-term", "player"]),
                new("PCs", "narg-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["game-term", "player", "plural"]),
                new("character", "mog-var", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["game-term", "role"]),
                new("customer", "dravik-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["trade", "buyer", "compound"]),
                new("customers", "dravik-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["trade", "buyer", "plural", "compound"]),
                new("patron", "dravik-mog-bant-flit", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["tavern", "customer", "compound"]),
                new("patrons", "dravik-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["tavern", "customer", "plural", "compound", "root-repaired"]),
                new("proprietor", "darg-dravik", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["owner", "trade", "compound"]),
                new("local", "nak-dak-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["local", "resident", "compound"]),
                new("locals", "nak-dak-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["local", "resident", "plural", "compound"]),
                new("hireling", "dravik-mog-karn-heku", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["paid", "helper"]),
                new("hirelings", "dravik-mogi-karn-vrak", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["paid", "helper", "plural"]),
                new("option", "varg", PartOfSpeech: "noun", GrammarClass: "choice", Tags: ["abstract"]),
                new("choice", "varg-thog", PartOfSpeech: "noun", GrammarClass: "choice", Tags: ["abstract", "compound"]),
                new("way", "lag-lag-gash", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["route"]),
                new("figure", "mog-var-morz-brak", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["figure", "role"]),
                new("target", "narg-gash", PartOfSpeech: "noun", GrammarClass: "danger", Tags: ["target", "martial", "compound"]),
                new("someone", "varg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["indefinite", "person", "compound"]),
                new("need", "thruk", PartOfSpeech: "noun", GrammarClass: "requirement", Tags: ["abstract"]),
                new("needs", "thruki", PartOfSpeech: "noun", GrammarClass: "requirement", Tags: ["abstract", "plural"]),
                new("time", "dakur", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["abstract"]),
                new("times", "dakuri", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["abstract", "plural"]),
                new("day", "dakur-hrowk-narg", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["day"]),
                new("days", "dakur-hrowki", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["day", "plural", "root-repaired"]),
                new("day's", "dakur-hrowkuk", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["day", "possessive", "root-repaired"]),
                new("day’s", "dakur-hrowkuk", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["day", "possessive", "root-repaired"]),
                new("year", "dakur-ti", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["year", "compound"]),
                new("annual", "ash-ash-dakur-ti", PartOfSpeech: "adjective", GrammarClass: "time", Tags: ["compound", "compound-reviewed", "each", "year", "wiki-fodder"]),
                new("years", "dakur-tiwi", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["year", "plural", "compound"]),
                new("pace", "lag-bit", PartOfSpeech: "noun", GrammarClass: "measure", Tags: ["step", "distance", "compound"]),
                new("paces", "lag-biti", PartOfSpeech: "noun", GrammarClass: "measure", Tags: ["step", "distance", "plural", "compound"]),
                new("a few paces", "dakukash heku brakash", PartOfSpeech: "noun", GrammarClass: "measure", Tags: ["step", "distance", "fixed-phrase"]),
                new("station", "darg-dak", PartOfSpeech: "noun", GrammarClass: "status", Tags: ["rank", "social", "compound", "root-repaired"]),
                new("one of some station", "ash uk varg darg-dak", PartOfSpeech: "noun", GrammarClass: "status", Tags: ["rank", "social", "fixed-phrase"]),
                new("quality", "thrak-thog", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["quality", "abstract", "compound"]),
                new("high quality", "thrak-thog-ti", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["quality", "high", "compound"]),
                new("gift", "drav-thog", PartOfSpeech: "noun", GrammarClass: "transfer", Tags: ["offering", "abstract", "compound"]),
                new("payment", "quum-drav", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["food", "exchange", "compound"]),
                new("aid", "drav-thruk", PartOfSpeech: "noun", GrammarClass: "support", Tags: ["help", "need", "compound"]),
                new("care", "gor-thog-ti-drav-thruk", PartOfSpeech: "noun", GrammarClass: "support", Tags: ["compound", "compound-reviewed", "protection", "aid", "wiki-fodder"]),
                new("wisdom", "thog-ti", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["knowledge", "honored", "compound"]),
                new("contrast", "mok-nu-thog", PartOfSpeech: "noun", GrammarClass: "comparison", Tags: ["contrast", "abstract", "compound"]),
                new("attention", "oglar-thog", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["attention", "perception", "compound"]),
                new("family", "mokh-zog", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["group", "root-repaired"]),
                new("family's", "mokh-zoguk", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["group", "possessive", "root-repaired"]),
                new("family’s", "mokh-zoguk", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["group", "possessive", "root-repaired"]),
                new("family know", "mokh-zog thogur", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["family", "knowledge", "fixed-phrase", "root-repaired"]),
                new("daughter", "nurik-mog", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["child", "family", "compound"]),
                new("farmer’s daughter", "quum-hekmoguk nurik-mog", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["farming", "family", "fixed-phrase"]),
                new("farmer's daughter", "quum-hekmoguk nurik-mog", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["farming", "family", "fixed-phrase", "root-repaired"]),
                new("ruling family", "dargin mokh-zog", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["ruling", "family", "fixed-phrase", "root-repaired"]),
                new("century", "mur-dakur", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["long-span", "compound"]),
                new("centuries", "mur-dakuri", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["long-span", "plural", "compound"]),
                new("second century", "dug mur-dakur", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["long-span", "ordinal", "fixed-phrase"]),
                new("idea", "thog-var", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["abstract", "compound"]),
                new("nonsense", "nul-thog", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["foolish", "abstract", "compound"]),
                new("throne", "darg-thrak", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rulership", "seat", "compound"]),
                new("faith", "mograth-thog", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["belief", "compound"]),
                new("prayer", "mograth-narg", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["prayer", "speech", "compound"]),
                new("quiet prayer", "thrum-narg mograth-narg", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["prayer", "quiet", "fixed-phrase"]),
                new("belief", "mograth-thog-burz-grum", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["belief", "compound"]),
                new("beliefs", "mograth-thogi", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["belief", "plural", "compound"]),
                new("god", "mograth-darg-mog", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["deity", "compound"]),
                new("gods", "mograth-darg-mogi", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["deity", "plural", "compound"]),
                new("Ecclesiastical", "mograthuk", PartOfSpeech: "adjective", GrammarClass: "religion", Tags: ["church", "possessive-derived"]),
                new("Ecclesiastical Law", "mograthuk darg-bib", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["church", "law", "fixed-phrase"]),
                new("administration", "darg-bib", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["bureaucracy", "compound"]),
                new("administrative", "darg-bibuk", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["bureaucracy", "possessive-derived"]),
                new("governance", "darg-thog", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rule", "abstract", "compound"]),
                new("authority", "darg-thog", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rule", "abstract", "compound", "root-repaired"]),
                new("administrative authority", "darg-bib darg-thog", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["bureaucracy", "rule", "fixed-phrase"]),
                new("religious authority", "mograth-darg", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["religious", "rule", "compound"]),
                new("religious and administrative authority", "mograth-darg agh darg-bib darg-thog", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["religious", "bureaucracy", "rule", "fixed-phrase"]),
                new("stance", "lag-thog", PartOfSpeech: "noun", GrammarClass: "position", Tags: ["viewpoint", "compound"]),
                new("Prelacy", "mograth-darg", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["religious", "rule", "compound", "root-repaired"]),
                new("The Prelacy of Middenmark", "arhk mograth-darg uk Middenmark", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["proper-noun", "religious", "rule", "fixed-phrase"]),
                new("law", "darg-bib-grum-brak", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rule", "written", "compound"]),
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
                new("purpose", "thruk-thog-mokh-bant", PartOfSpeech: "noun", GrammarClass: "purpose", Tags: ["purpose", "abstract", "compound"]),
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
                new("branch", "gruul-krub", PartOfSpeech: "noun", GrammarClass: "plant", Tags: ["wood", "finger", "compound"]),
                new("branches", "gruul-krubi", PartOfSpeech: "noun", GrammarClass: "plant", Tags: ["wood", "finger", "plural", "compound"]),
                new("trunk", "gruul-bant", PartOfSpeech: "noun", GrammarClass: "plant", Tags: ["wood", "body", "compound"]),
                new("trunks", "gruul-banti", PartOfSpeech: "noun", GrammarClass: "plant", Tags: ["wood", "body", "plural", "compound"]),
                new("brush", "vril-thrum-dak", PartOfSpeech: "noun", GrammarClass: "plant", Tags: ["woods", "low-growth", "compound"]),
                new("undergrowth", "vril-thrum", PartOfSpeech: "noun", GrammarClass: "plant", Tags: ["woods", "low-growth", "compound"]),
                new("warhorse", "gash-hrog", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["war", "mount", "compound"]),
                new("boar", "vril-quum-mog", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["wild", "food", "compound"]),
                new("boar's", "vril-quum-moguk", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["wild", "food", "possessive", "compound"]),
                new("tusk", "kruk-zol", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["tooth", "weapon", "compound"]),
                new("tusks", "kruk-zoli", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["tooth", "weapon", "plural", "compound"]),
                new("black cat", "burz-kaag-mog", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["black", "scent", "fixed-phrase"]),
                new("dog", "gor-mogra", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["guard", "companion", "compound"]),
                new("dogs", "gor-mograi", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["guard", "companion", "plural", "compound"]),
                new("hound", "vark-gor-mogra", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["hunt", "guard", "compound"]),
                new("beast", "vark-mog", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["danger", "creature", "compound"]),
                new("moth", "rukh-flit", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["insect", "light", "compound"]),
                new("bat", "naut-flit", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["night", "flying", "compound"]),
                new("arrival", "ik-lag", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["arrival", "compound"]),
                new("wave", "bant-narg", PartOfSpeech: "noun", GrammarClass: "gesture", Tags: ["wave", "gesture", "compound"]),
                new("talk", "narg-thog", PartOfSpeech: "noun", GrammarClass: "speech", Tags: ["talk", "abstract", "compound"]),
                new("warmth", "rukh-grod-thog", PartOfSpeech: "noun", GrammarClass: "temperature", Tags: ["warmth", "abstract", "compound"]),
                new("expression", "mogum-narg-thog", PartOfSpeech: "noun", GrammarClass: "expression", Tags: ["face", "compound", "root-repaired"]),
                new("gaze", "oglar-lag", PartOfSpeech: "noun", GrammarClass: "perception", Tags: ["gaze", "compound"]),
                new("fore", "lag-ti", PartOfSpeech: "noun", GrammarClass: "position", Tags: ["front", "route", "compound"]),
                new("girl", "nurik-mog-lag-dak", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["young", "female", "compound"]),
                new("missing girl", "nul-lag nurik-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["missing", "young", "female", "fixed-phrase"]),
                new("home", "dakku-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["dwelling", "home", "compound", "root-repaired"]),
                new("home base", "dakku-dak mokh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["dwelling", "operations", "fixed-phrase", "root-repaired"]),
                new("folly", "nul-thog", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["foolish", "abstract", "compound", "root-repaired"]),
                new("bravery", "yanki-thog", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["courage", "abstract", "compound"]),
                new("eyes", "oglar-krubi", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["sight", "plural", "compound"]),
                new("implementation", "hek-darg", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["execution", "law", "compound"]),
                new("doctrine", "mograth-bib", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["teaching", "written", "compound"]),
                new("religious doctrine", "mograth-bib", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["teaching", "religious", "compound", "root-repaired"]),
                new("triad", "dug-agh-ash mokh", PartOfSpeech: "noun", GrammarClass: "group", Tags: ["three", "collective", "fixed-phrase", "root-repaired"]),
                new("community", "mokh-ash-flit", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["group", "inhabited"]),
                new("society", "mokh-ash-flit", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["shared-form", "community", "collective", "wiki-fodder"]),
                new("hamlet", "thrum-mog-dak", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["small", "inhabited", "compound"]),
                new("hamlets", "thrum-mog-daki", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["small", "inhabited", "plural", "compound"]),
                new("hamlet's", "thrum-mog-dakuk", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["small", "inhabited", "possessive", "compound"]),
                new("communities", "mokhi", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["group", "inhabited", "plural"]),
                new("small group", "nikmokh", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["small", "group", "compound"]),
                new("Hamlet Watch", "thrum-mog-dak thrak", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["watch", "defense", "fixed-phrase"]),
                new("threat", "vark-thog", PartOfSpeech: "noun", GrammarClass: "danger", Tags: ["danger", "abstract", "compound"]),
                new("threats", "vark-thogi", PartOfSpeech: "noun", GrammarClass: "danger", Tags: ["danger", "abstract", "plural", "compound"]),
                new("danger", "vark-thog-thog-burz", PartOfSpeech: "noun", GrammarClass: "danger", Tags: ["danger", "abstract", "compound"]),
                new("dangers", "vark-thogi", PartOfSpeech: "noun", GrammarClass: "danger", Tags: ["danger", "abstract", "plural", "compound", "root-repaired"]),
                new("peril", "vark-thog-ti", PartOfSpeech: "noun", GrammarClass: "danger", Tags: ["danger", "intensified", "compound"]),
                new("initiative", "ashdak-gash-thog", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["first", "combat", "compound"]),
                new("attack", "gash-narg", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["combat", "action", "compound"]),
                new("damage", "brak-thog", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["harm", "abstract", "compound"]),
                new("roll", "zorn-bib", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["dice", "written", "compound"]),
                new("force", "darg-gash", PartOfSpeech: "noun", GrammarClass: "power", Tags: ["power", "martial", "compound"]),
                new("forces", "darg-gashi", PartOfSpeech: "noun", GrammarClass: "power", Tags: ["power", "martial", "plural", "compound"]),
                new("chaos", "nul-darg-thog", PartOfSpeech: "noun", GrammarClass: "disorder", Tags: ["disorder", "abstract", "compound"]),
                new("anarchy", "nul-darg-thog", PartOfSpeech: "noun", GrammarClass: "disorder", Tags: ["shared-form", "chaos", "abstract", "wiki-fodder"]),
                new("forces of chaos", "nul-darg-thog darg-gashi", PartOfSpeech: "noun", GrammarClass: "power", Tags: ["chaos", "power", "plural", "fixed-phrase"]),
                new("bravery", "yanki-thog-lag-bant", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["courage", "abstract", "compound"]),
                new("testament", "thog-bib", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["evidence", "written", "compound"]),
                new("spirit", "grod-thog-tuk-narg", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["spirit", "abstract", "compound"]),
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
                new("tavern", "rukh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["drink", "social", "compound", "root-repaired"]),
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
                new("sight", "oglar-thog", PartOfSpeech: "noun", GrammarClass: "perception", Tags: ["sight", "abstract", "compound", "root-repaired"]),
                new("welcome sight", "mokra-dak oglar-thog", PartOfSpeech: "noun", GrammarClass: "perception", Tags: ["welcome", "sight", "fixed-phrase"]),
                new("march", "gash-lag", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["walking", "military", "compound"]),
                new("day's march", "dakur-hrowkuk gash-lag", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["day", "walking", "fixed-phrase", "root-repaired"]),
                new("day’s march", "dakur-hrowkuk gash-lag", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["day", "walking", "fixed-phrase", "root-repaired"]),
                new("morning", "dakur-sun", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["morning", "compound"]),
                new("guard duty", "gor-hek-thog", PartOfSpeech: "noun", GrammarClass: "protection", Tags: ["guard", "duty", "compound", "root-repaired"]),
                new("resilience", "grotash-nu-thog", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["resilient", "abstract", "compound"]),
                new("opportunity", "varg-dak", PartOfSpeech: "noun", GrammarClass: "choice", Tags: ["opportunity", "compound"]),
                new("opportunities", "varg-daki", PartOfSpeech: "noun", GrammarClass: "choice", Tags: ["opportunity", "plural", "compound"]),
                new("defense", "gor-thog", PartOfSpeech: "noun", GrammarClass: "protection", Tags: ["defense", "abstract", "compound"]),
                new("protecting", "gor-thog-in", PartOfSpeech: "verb", GrammarClass: "protection", Tags: ["protection", "progressive", "compound"]),
                new("additional protection", "lag-ti-bit gor-thog-ti", PartOfSpeech: "noun", GrammarClass: "protection", Tags: ["additional", "protection", "fixed-phrase"]),
                new("activity", "hek-var", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["action", "abstract", "compound"]),
                new("activities", "hek-vari", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["action", "abstract", "plural", "compound"]),
                new("trade activities", "drav hek-vari", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["commerce", "action", "plural", "fixed-phrase"]),
                new("jobs board", "hek-bib", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["work", "notice", "compound"]),
                new("posting", "narg-bib-hrowk-grum", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["notice", "written", "compound"]),
                new("postings", "narg-bibi", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["notice", "written", "plural", "compound"]),
                new("following postings", "ut-narg-bibi", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["following", "notice", "plural", "fixed-phrase"]),
                new("economy", "drav-thog", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["commerce", "abstract", "compound", "root-repaired"]),
                new("local economy", "nak-dakuk drav-thog", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["commerce", "local", "fixed-phrase"]),
                new("livestock", "quum-mogi", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["food", "kept", "plural", "compound"]),
                new("sheep", "thrum-quum-mogi", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["food", "kept", "plural", "compound"]),
                new("agriculture", "quum-hekin", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["food", "farming", "compound", "root-repaired"]),
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
                new("work", "hek-grum-morz", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["default"]),
                new("farm", "quum-hek-dak", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["food", "work", "place", "compound"]),
                new("hedgerow", "vrul-lag", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["hedge", "route", "compound"]),
                new("hedgerows", "vrul-lagi", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["hedge", "route", "plural", "compound"]),
                new("muttering", "thrum-narg", PartOfSpeech: "noun", GrammarClass: "speech", Tags: ["quiet", "speech", "compound"]),
                new("mutterings", "thrum-nargi", PartOfSpeech: "noun", GrammarClass: "speech", Tags: ["quiet", "speech", "plural", "compound"]),
                new("common folk", "mokh-mogi", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["common", "folk", "plural", "compound"]),
                new("eye", "oglar-krub", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["sight", "body", "compound"]),
                new("name", "mog-narg", PartOfSpeech: "noun", GrammarClass: "identity", Tags: ["name", "compound"]),
                new("glow", "rukh-oglar-vrak-tuk", PartOfSpeech: "noun", GrammarClass: "light", Tags: ["light", "warmth", "compound"]),
                new("smell", "kaag-thog-thog-krag", PartOfSpeech: "noun", GrammarClass: "sense", Tags: ["smell", "abstract", "compound"]),
                new("smells", "kaag-thogi-grod-morz", PartOfSpeech: "noun", GrammarClass: "sense", Tags: ["smell", "plural", "compound"]),
                new("grin", "mauk-narg", PartOfSpeech: "noun", GrammarClass: "expression", Tags: ["smile", "compound"]),
                new("watch", "gor", PartOfSpeech: "verb", GrammarClass: "action"),
                new("to be", "tar", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["infinitive"]),
                new("be", "taru", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["infinitive", "root-repaired"]),
                new("is", "tur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present"]),
                new("am", "tur-gash-drak", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present"]),
                new("are", "tur-mokh", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present", "plural", "root-repaired"]),
                new("was", "tash", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past"]),
                new("were", "tash-mokh", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past", "plural", "root-repaired"]),
                new("had", "tukash", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["past"]),
                new("have been", "tuk", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["perfect"]),
                new("is being", "turin", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["progressive", "present"]),
                new("are being", "turin-ash-rukh", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["progressive", "present"]),
                new("being", "turin-vrak-hrowk", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["progressive", "present"]),
                new("will be", "taruk", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["future"]),
                new("may be", "mauk tar", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility", "permission", "state"]),
                new("may opt to be staying", "mauk vargu dakkin", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility", "choice", "location", "fixed-phrase"]),
                new("may be from", "mauk dok", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility", "origin", "fixed-phrase"]),
                new("is named after", "tur mog-nargash dok", PartOfSpeech: "verb", GrammarClass: "naming", Tags: ["present", "passive", "fixed-phrase"]),
                new("named after", "mog-nargash dok", PartOfSpeech: "verb", GrammarClass: "naming", Tags: ["past-participle", "fixed-phrase"]),
                new("is led by", "tur dargash fa", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["present", "passive", "authority", "fixed-phrase"]),
                new("is not", "notur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present", "negative"]),
                new("are not", "notur-mokh", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present", "negative", "plural", "root-repaired"]),
                new("was not", "notash", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past", "negative"]),
                new("were not", "notash-mokh", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past", "negative", "plural", "root-repaired"]),
                new("may", "mauk", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility", "permission"]),
                new("might", "mauk-thog-darg", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility"]),
                new("could", "mauk-drak-grod", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility"]),
                new("could wait", "mauk grotash", PartOfSpeech: "verb", GrammarClass: "delay", Tags: ["possibility", "delay", "fixed-phrase"]),
                new("wouldn't", "nu-mauk", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["negative", "conditional", "contraction", "compound"]),
                new("wouldn’t", "nu-mauk-doku-krag", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["negative", "conditional", "contraction", "compound"]),
                new("will", "uk", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["future"]),
                new("opt", "vargu", PartOfSpeech: "verb", GrammarClass: "choice", Tags: ["infinitive"]),
                new("choose", "vargu-dak-zog", PartOfSpeech: "verb", GrammarClass: "choice", Tags: ["infinitive"]),
                new("use", "bruku", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["infinitive"]),
                new("uses", "brukur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["present"]),
                new("using", "brukin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["progressive", "present"]),
                new("lacking", "nul-tukrin", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["progressive", "negative"]),
                new("Lacking", "nul-tukrin-kaag-darg", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["progressive", "negative"]),
                new("give", "draku", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["infinitive"]),
                new("made", "hekash", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["past"]),
                new("make", "heku", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["infinitive"]),
                new("harden", "heku-yankuk", PartOfSpeech: "verb", GrammarClass: "change", Tags: ["compound", "compound-reviewed", "make", "strong", "wiki-fodder"]),
                new("making", "hekin", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["progressive", "making"]),
                new("make their way", "lagu ughatuk lag", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["travel", "fixed-phrase"]),
                new("field dress", "quum-vrak heku", PartOfSpeech: "verb", GrammarClass: "food", Tags: ["butcher", "prepare", "fixed-phrase"]),
                new("butcher", "quum-vrak-heku", PartOfSpeech: "verb", GrammarClass: "food", Tags: ["prepare", "meat", "compound"]),
                new("butchery", "quum-vrak-hekin", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["prepare", "meat", "compound"]),
                new("dwarven made", "dwarfuk hekash", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["past", "dwarven", "fixed-phrase"]),
                new("provide", "dravku", PartOfSpeech: "verb", GrammarClass: "support", Tags: ["infinitive"]),
                new("provide", "dravur", PartOfSpeech: "verb", GrammarClass: "support", Tags: ["present", "plural-subject"]),
                new("retain", "dargu-tukra", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["authority", "infinitive", "compound"]),
                new("retains", "dargu-tukur", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["authority", "present", "compound"]),
                new("retains the throne", "dargu-tukur arhk darg-thrak", PartOfSpeech: "verb", GrammarClass: "authority", Tags: ["rulership", "possession", "fixed-phrase"]),
                new("rule", "dargu", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["infinitive", "authority"]),
                new("governed", "dargash", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["past-participle", "authority"]),
                new("Governed", "dargash-karn-grod", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["past-participle", "authority"]),
                new("ruling", "dargin", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["progressive", "present", "authority"]),
                new("ruling the subterranean near-surface haunts", "dargin arhk burz nak-oglar-dak darg-daki", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["authority", "territory", "fixed-phrase"]),
                new("hold", "dargu-grum-bant", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["infinitive", "authority"]),
                new("holds", "dargur", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["present", "authority"]),
                new("holds sway", "dargur-ti", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["present", "authority", "fixed-phrase"]),
                new("insist", "dargu-thog", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["infinitive", "stubborn", "compound"]),
                new("insist on", "dargu-thog ak", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["infinitive", "stubborn", "fixed-phrase"]),
                new("does not", "nu", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["negative"]),
                new("doesn't", "nu-darg-bant", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["negative", "contraction"]),
                new("to see", "oglar", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["infinitive"]),
                new("see", "oglar-karn-darg", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["infinitive"]),
                new("sees", "oglur", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["present"]),
                new("saw", "oglash", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["past"]),
                new("have seen", "ogluk", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["perfect"]),
                new("is seeing", "oglurin", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["progressive", "present"]),
                new("will see", "oglaruk", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["future"]),
                new("does not see", "noglur", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["present", "negative"]),
                new("did not see", "noglash", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["past", "negative"]),
                new("provide", "dravku-drav", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["infinitive", "exchange", "root-repaired"]),
                new("to provide", "dravku-thruk", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["infinitive", "purpose", "root-repaired"]),
                new("obtain", "tukur-drav", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["infinitive", "acquire", "possession", "root-repaired"]),
                new("provides", "dravur-drav", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["present", "exchange", "root-repaired"]),
                new("provided", "dravash", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["past"]),
                new("providing", "dravin", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["progressive", "present"]),
                new("obtaining", "tukrin-drav", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["progressive", "present", "acquire", "possession", "root-repaired"]),
                new("contributes", "dravur-mokh", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["present", "support", "group", "root-repaired"]),
                new("benefits from", "dravur dok", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["present", "advantage", "fixed-phrase"]),
                new("form", "heku-var", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["infinitive", "shape", "root-repaired"]),
                new("forms", "hekur", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["present"]),
                new("has", "tukur", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["present", "third-person"]),
                new("kept", "dargash-tuk", PartOfSpeech: "verb", GrammarClass: "authority", Tags: ["past", "maintained", "compound"]),
                new("brought", "dravash-ik", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["past", "brought", "compound"]),
                new("bring", "dravku-ik", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["infinitive", "brought", "compound"]),
                new("join", "mokru", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["infinitive"]),
                new("to join", "mokru-thruk", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["infinitive", "purpose", "root-repaired"]),
                new("joins", "mokrur", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["present"]),
                new("joined", "mokrash", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["past"]),
                new("joining", "mokrin", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["progressive", "present"]),
                new("escape", "varku", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive"]),
                new("escaped", "varkash", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past"]),
                new("escaping", "varkin", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "present"]),
                new("carry", "hrowku", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive", "carrying", "derive-present", "derive-past", "derive-progressive"]),
                new("transport persons", "hrowku mogi", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["transport", "people", "fixed-phrase"]),
                new("stepped inside", "lagash ik", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past", "inside", "fixed-phrase"]),
                new("drift", "varku-thrum", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive", "slow", "compound"]),
                new("drift into", "varku-thrum ik", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive", "slow", "fixed-phrase"]),
                new("filter into", "varku-thrum ik-grum-brak", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive", "slow", "fixed-phrase"]),
                new("wander", "vagoru", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive", "wandering"]),
                new("rush off", "varku-grak dok", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["infinitive", "hasty", "fixed-phrase"]),
                new("exploring", "lag-oglarin", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "present", "seeking", "compound"]),
                new("adventuring", "vark-yankin", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "present", "danger", "compound"]),
                new("stay", "dakku", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["infinitive"]),
                new("stays", "dakur-darg-tuk", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["present"]),
                new("staying", "dakkin", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["progressive", "present"]),
                new("stayed", "dakash", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["past"]),
                new("working", "hekin-darg-hush", PartOfSpeech: "verb", GrammarClass: "labor", Tags: ["progressive", "present"]),
                new("recover", "ut-dravku", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["infinitive", "reclaim"]),
                new("recovered", "ut-dravash", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["past", "reclaim"]),
                new("re-take", "ut-dravku", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["infinitive", "reclaim", "root-repaired"]),
                new("retake", "ut-dravku", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["infinitive", "reclaim", "root-repaired"]),
                new("went on", "lagash", PartOfSpeech: "verb", GrammarClass: "sequence", Tags: ["past", "continued"]),
                new("revolves around", "murk-dakur nak", PartOfSpeech: "verb", GrammarClass: "relation", Tags: ["present", "central", "fixed-phrase"]),
                new("rooted", "lag-hekash", PartOfSpeech: "verb", GrammarClass: "origin", Tags: ["past-participle", "rooted", "compound"]),
                new("respected", "dargash-thog", PartOfSpeech: "verb", GrammarClass: "respect", Tags: ["past", "respect", "compound"]),
                new("caught his eye", "nargash mogumuk oglar-krub", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["past", "attention", "fixed-phrase"]),
                new("throwing in with", "mokrin ogh", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["progressive", "joining", "fixed-phrase"]),
                new("left to his name", "ashdak ur mogumuk mog-narg", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["remaining", "fixed-phrase"]),
                new("left", "ashdak", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["remaining"]),
                new("paid", "dravash-thog-zog", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["past", "payment"]),
                new("knew", "thogash", PartOfSpeech: "verb", GrammarClass: "thought", Tags: ["past", "knowledge"]),
                new("needed", "thrukash", PartOfSpeech: "verb", GrammarClass: "requirement", Tags: ["past", "need"]),
                new("was needed", "tash thrukash", PartOfSpeech: "verb", GrammarClass: "requirement", Tags: ["past", "need", "passive", "fixed-phrase"]),
                new("What was needed", "mok tash thrukash", PartOfSpeech: "verb", GrammarClass: "requirement", Tags: ["question", "need", "fixed-phrase"]),
                new("he'd be sleeping", "mogum taruk dakkin-naut", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["future", "sleeping", "fixed-phrase"]),
                new("he’d be sleeping", "mogum taruk dakkin-naut", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["future", "sleeping", "fixed-phrase", "root-repaired"]),
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
                new("smiled", "mauk-nargash", PartOfSpeech: "verb", GrammarClass: "expression", Tags: ["past", "smile", "compound"]),
                new("offering", "dravin-hush-narg", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["progressive", "offer"]),
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
                new("given", "dravash-hush-morz", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["past-participle", "given"]),
                new("emblazoned", "nargash-ti", PartOfSpeech: "verb", GrammarClass: "symbol", Tags: ["past", "heraldic", "compound"]),
                new("wore", "khalash", PartOfSpeech: "verb", GrammarClass: "garb", Tags: ["past"]),
                new("had been", "tukash tuk", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past-perfect", "fixed-phrase"]),
                new("scrubbed", "rukh-vrakash", PartOfSpeech: "verb", GrammarClass: "cleaning", Tags: ["past", "clean", "compound"]),
                new("oiled", "rukh-thrumash", PartOfSpeech: "verb", GrammarClass: "maintenance", Tags: ["past", "oil", "compound"]),
                new("look", "oglar-bant-ash", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["infinitive"]),
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
                new("remove food", "rukh-quum-goru-drak", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["imperative"]),
                new("travel only by night", "naut-varku", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["instruction", "night-only"]),
                new("use iron", "bruk-zol", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["imperative"]),
                new("hate", "krugh", PartOfSpeech: "verb", GrammarClass: "emotion", Tags: ["infinitive"]),
                new("think", "thog", PartOfSpeech: "verb", GrammarClass: "thought", Tags: ["infinitive"]),
                new("hold off", "grotash-bant-drak", PartOfSpeech: "verb", GrammarClass: "delay", Tags: ["fixed-phrase"]),
                new("control", "dargu-mokh-mokh", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["infinitive"]),
                new("controlling", "dargin-kaag-drak", PartOfSpeech: "verb", GrammarClass: "command", Tags: ["progressive", "present"]),
                new("feel", "grodh", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["infinitive"]),
                new("plague", "morzku", PartOfSpeech: "verb", GrammarClass: "affliction", Tags: ["infinitive"]),
                new("plagues", "morzur", PartOfSpeech: "verb", GrammarClass: "affliction", Tags: ["present"]),
                new("ensure", "growtin", PartOfSpeech: "verb", GrammarClass: "certainty", Tags: ["infinitive"]),
                new("ensures", "growtinur", PartOfSpeech: "verb", GrammarClass: "certainty", Tags: ["present"]),
                new("ensured", "growtinash", PartOfSpeech: "verb", GrammarClass: "certainty", Tags: ["past"]),
                new("ensuring", "growtinin", PartOfSpeech: "verb", GrammarClass: "certainty", Tags: ["progressive", "present"]),
                new("function", "gashin-grrtuk-lag", PartOfSpeech: "noun", GrammarClass: "operation", Tags: ["working", "compound"]),
                new("functions", "gashin-grrtuk-lagi", PartOfSpeech: "noun", GrammarClass: "operation", Tags: ["working", "plural", "compound"]),
                new("functioning", "gashin-grrtuk-lagin", PartOfSpeech: "verb", GrammarClass: "operation", Tags: ["working", "progressive", "present", "compound"]),
                new("functionality", "gashin-grrtuk-lag-thog", PartOfSpeech: "noun", GrammarClass: "operation", Tags: ["working", "abstract", "compound"]),
                new("fund", "drav-zol-thrukku", PartOfSpeech: "verb", GrammarClass: "support", Tags: ["money", "support", "infinitive", "compound"]),
                new("funds", "drav-zol-thrukur", PartOfSpeech: "verb", GrammarClass: "support", Tags: ["money", "support", "present", "compound"]),
                new("funding", "drav-zol-thrukin", PartOfSpeech: "verb", GrammarClass: "support", Tags: ["money", "support", "progressive", "present", "compound"]),
                new("haste", "grak-lag-thog", PartOfSpeech: "noun", GrammarClass: "speed", Tags: ["fast", "motion", "abstract", "compound"]),
                new("hurry", "grak-lagu", PartOfSpeech: "verb", GrammarClass: "speed", Tags: ["fast", "motion", "infinitive", "compound"]),
                new("horn", "narg-rukh-ti-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["signal", "sound", "instrument", "compound"]),
                new("horn-blower", "narg-rukh-ti-bant gash-rukh-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["signal", "sound", "instrument", "fixed-phrase"]),
                new("inject", "rukh-vrak-iku", PartOfSpeech: "verb", GrammarClass: "body", Tags: ["liquid", "skin", "inside", "infinitive", "compound"]),
                new("injects", "rukh-vrak-ikur", PartOfSpeech: "verb", GrammarClass: "body", Tags: ["liquid", "skin", "inside", "present", "compound"]),
                new("injected", "rukh-vrak-ikash", PartOfSpeech: "verb", GrammarClass: "body", Tags: ["liquid", "skin", "inside", "past", "compound"]),
                new("injecting", "rukh-vrak-ikin", PartOfSpeech: "verb", GrammarClass: "body", Tags: ["liquid", "skin", "inside", "progressive", "present", "compound"]),
                new("injection", "rukh-vrak-ik-thog", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["liquid", "skin", "inside", "abstract", "compound"]),
                new("insert", "bruk-iku", PartOfSpeech: "verb", GrammarClass: "placement", Tags: ["push", "inside", "infinitive", "compound"]),
                new("inserts", "bruk-ikur", PartOfSpeech: "verb", GrammarClass: "placement", Tags: ["push", "inside", "present", "compound"]),
                new("inserted", "bruk-ikash", PartOfSpeech: "verb", GrammarClass: "placement", Tags: ["push", "inside", "past", "compound"]),
                new("inserting", "bruk-ikin", PartOfSpeech: "verb", GrammarClass: "placement", Tags: ["push", "inside", "progressive", "present", "compound"]),
                new("insertion", "bruk-ik-thog", PartOfSpeech: "noun", GrammarClass: "placement", Tags: ["push", "inside", "abstract", "compound"]),
                new("ironclad", "zol-vrakuk", PartOfSpeech: "adjective", GrammarClass: "armor", Tags: ["iron", "armor", "possessive-derived", "compound"]),
                new("jasper", "braku-brakari", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["stone", "gem", "compound"]),
                new("macabre", "biti-forge", PartOfSpeech: "adjective", GrammarClass: "tone", Tags: ["death", "dread", "compound"]),
                new("molding", "hekur-krag", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["formed", "edge", "compound"]),
                new("mortgage", "gashin-fentrest", PartOfSpeech: "noun", GrammarClass: "finance", Tags: ["debt", "property", "compound"]),
                new("lever", "banktuk", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["tool", "mechanism"]),
                new("moses", "Moses", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("nearly", "goth-cab", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["almost", "compound"]),
                new("nineteen", "grow-hrowkai", PartOfSpeech: "number", GrammarClass: "count", Tags: ["number", "compound"]),
                new("non-surprise", "nul-noglar-thog-ti", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["negative", "surprise", "compound"]),
                new("obtained", "dravkuash", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["past", "acquire"]),
                new("omen", "noglar-narg-bib", PartOfSpeech: "noun", GrammarClass: "divination", Tags: ["hidden", "sign", "compound"]),
                new("orgasm", "grod-vrak-mauk-thog", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["pleasure", "body", "abstract", "compound"]),
                new("palaver", "narg-mokru-thog", PartOfSpeech: "noun", GrammarClass: "speech", Tags: ["talk", "debate", "abstract", "compound"]),
                new("palpable", "krub-oglaruk", PartOfSpeech: "adjective", GrammarClass: "sense", Tags: ["touch", "perceivable", "possessive-derived", "compound"]),
                new("printer", "bib-hekur-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["text", "maker", "compound"]),
                new("profitable", "drav-thog-tiuk", PartOfSpeech: "adjective", GrammarClass: "value", Tags: ["profit", "high-value", "possessive-derived", "compound"]),
                new("receive", "drav-doku", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["infinitive", "inbound", "compound"]),
                new("receives", "drav-dokur", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["present", "inbound", "compound"]),
                new("relative", "mokh-moguk", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["family", "member", "possessive-derived", "compound"]),
                new("sacrifice", "darg-morz-heku", PartOfSpeech: "verb", GrammarClass: "religion", Tags: ["death", "ritual", "infinitive", "compound"]),
                new("sacrificed", "darg-morz-hekuash", PartOfSpeech: "verb", GrammarClass: "religion", Tags: ["death", "ritual", "past", "compound"]),
                new("sake", "thruk-thoguk", PartOfSpeech: "noun", GrammarClass: "purpose", Tags: ["purpose", "benefit", "possessive-derived", "compound"]),
                new("sell", "dravu-dok", PartOfSpeech: "verb", GrammarClass: "trade", Tags: ["commerce", "outbound", "infinitive", "compound"]),
                new("signature", "mog-narg-bib", PartOfSpeech: "noun", GrammarClass: "identity", Tags: ["name", "mark", "written", "compound"]),
                new("signatures", "mog-narg-bibi", PartOfSpeech: "noun", GrammarClass: "identity", Tags: ["name", "mark", "written", "plural", "compound"]),
                new("slough", "rukh-dak-morz", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["wetland", "water", "ill", "compound"]),
                new("sunrise", "surg-lag-ti-thog", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["sun", "rising", "abstract", "compound"]),
                new("thunderously", "grot-narg-rukhuk-uk", PartOfSpeech: "adverb", GrammarClass: "sound", Tags: ["storm", "loud", "adverbial", "compound"]),
                new("title", "darg-mog-narg", PartOfSpeech: "noun", GrammarClass: "identity", Tags: ["rank", "name", "authority", "compound"]),
                new("virgin", "nul-brukash-vrak", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["untouched", "body", "negative", "compound"]),
                new("vision", "oglar-naut-thog", PartOfSpeech: "noun", GrammarClass: "perception", Tags: ["sight", "dream", "abstract", "compound"]),
                new("visions", "oglar-naut-thogi", PartOfSpeech: "noun", GrammarClass: "perception", Tags: ["sight", "dream", "abstract", "plural", "compound"]),
                new("washing", "rukh-vrakin", PartOfSpeech: "verb", GrammarClass: "cleaning", Tags: ["water", "clean", "progressive", "present", "compound"]),
                new("waved", "bant-nargash", PartOfSpeech: "verb", GrammarClass: "gesture", Tags: ["wave", "past", "compound"]),
                new("yank", "dravu-grak", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["pull", "sudden", "infinitive", "compound"]),
                new("yanks", "dravur-grak", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["pull", "sudden", "present", "compound"]),
                new("yellow", "surg-khal", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["sun", "color", "compound"]),
                new("yellowed", "surg-khalash", PartOfSpeech: "verb", GrammarClass: "color", Tags: ["sun", "color", "past", "compound"]),
                new("jerks", "hroguk-gori", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["sudden", "plural", "compound"]),
                new("commonwealth", "dravi-ender-dwimmer", PartOfSpeech: "noun", GrammarClass: "politics", Tags: ["community", "wealth", "compound"]),
                new("drive", "doki-dorn", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["force", "movement", "compound"]),
                new("advertising", "grum-hrowku-daku-burzuk", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["public", "notice", "progressive", "compound"]),
                new("flailing", "bantuk-brukur-gruuluk", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["uncontrolled", "limb", "progressive", "compound"]),
                new("grease-like", "rukh-rukh-mok", PartOfSpeech: "adjective", GrammarClass: "texture", Tags: ["oil", "slippery", "simile", "compound"]),
                new("roast", "dakku-grodin", PartOfSpeech: "verb", GrammarClass: "food", Tags: ["cook", "heat", "compound"]),
                new("jams", "hekfa-bitu", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["preserve", "plural", "compound"]),
                new("bankrupt", "drav-zol-morz", PartOfSpeech: "adjective", GrammarClass: "finance", Tags: ["debt", "ruin", "money", "compound"]),
                new("topic", "narg-thog-var", PartOfSpeech: "noun", GrammarClass: "speech", Tags: ["subject", "discussion", "thought", "compound"]),
                new("disturbed", "thog-brakash", PartOfSpeech: "verb", GrammarClass: "condition", Tags: ["unsettled", "past", "thought", "compound"]),
                new("beds", "dakku-hekmog", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["sleep", "plural", "compound"]),
                new("solomon", "eastdale-dornuk-dorn", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym", "compound"]),
                new("renaissance", "brakin-drath-gor-gruuli", PartOfSpeech: "noun", GrammarClass: "culture", Tags: ["rebirth", "revival", "compound"]),
                new("salts", "dak-zol-thrumi", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["mineral", "stone", "plural", "compound"]),
                new("persuaded", "bibnaki-hrowkash-kal", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["convinced", "past", "compound"]),
                new("visceral", "grrtuk-dornuk-koboldi", PartOfSpeech: "adjective", GrammarClass: "body", Tags: ["gut", "instinct", "compound"]),
                new("beloved", "mokra-thog-ti", PartOfSpeech: "adjective", GrammarClass: "emotion", Tags: ["love", "valued", "honored", "compound"]),
                new("pronounces", "grum-dak-demand", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["declares", "present", "compound"]),
                new("anything", "hrogarai-ashuk", PartOfSpeech: "pronoun", GrammarClass: "scope", Tags: ["indefinite", "compound"]),
                new("armadillo", "brukin-gori", PartOfSpeech: "noun", GrammarClass: "creature", Tags: ["animal", "armored", "compound"]),
                new("arrangements", "grimuk-hekruh-gashash", PartOfSpeech: "noun", GrammarClass: "organization", Tags: ["planned", "plural", "compound"]),
                new("audible", "bebec-flit", PartOfSpeech: "adjective", GrammarClass: "sound", Tags: ["hearable", "compound"]),
                new("auditions", "colossal-heku", PartOfSpeech: "noun", GrammarClass: "performance", Tags: ["trial", "plural", "compound"]),
                new("australia", "grodhi-khal", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "compound"]),
                new("ax", "zol-rukh-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "blade", "haft", "compound"]),
                new("baloney", "ik-dakkin", PartOfSpeech: "noun", GrammarClass: "speech", Tags: ["nonsense", "falsehood", "compound"]),
                new("battering", "gabh-dargin", PartOfSpeech: "verb", GrammarClass: "combat", Tags: ["striking", "progressive", "compound"]),
                new("battles", "giodon-bibnakin", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["conflict", "plural", "compound"]),
                new("boobs", "grod-burz-vraki", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["chest", "flesh", "plural", "compound"]),
                new("briefcase", "decob-decobi", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["case", "document", "compound"]),
                new("buckets", "rukh-bant-tii", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["water", "vessel", "plural", "compound"]),
                new("camelot", "groduk-dakuru", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "exonym", "compound"]),
                new("capitol", "daki-gashuri", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["government", "building", "compound"]),
                // Promoted from TSV cleanup: root-derived touched translations.
                new("afternoons", "dakur-surg-doki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-afternoon"]),
                new("alarms", "vark-narg-rukhi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-alarm"]),
                new("allies", "mokrai", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "plural", "base-ally", "root-repaired"]),
                new("approaches", "lag-thog-krag-burzi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-approach"]),
                new("arrow's", "flit-zoluk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-arrow"]),
                new("assignments", "darg-heki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-assignment"]),
                new("awards", "grod-dravi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-award"]),
                new("bandits", "drav-vark-mogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-bandit"]),
                new("bases", "mokh-daki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-base"]),
                new("beasts", "vark-mogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-beast"]),
                new("beings", "turin-vrak-hrowkur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-being"]),
                new("belts", "khal-banti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-belt"]),
                new("biscuits", "hek-quum-biti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-biscuit"]),
                new("blade’s", "zol-rukh-varkuk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-blade", "root-repaired"]),
                new("blades", "zol-rukh-varki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-blade", "root-repaired"]),
                new("blankets", "khal-grodi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-blanket"]),
                new("blockades", "gor-hek-lagi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-blockade"]),
                new("boards", "bib-daki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-board"]),
                new("bonuses", "agh-dravi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-bonus"]),
                new("books", "bibi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-book"]),
                new("bosses", "gor-zol-murki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-boss"]),
                new("bottles", "rukh-bant-burzi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-bottle"]),
                new("bounties", "mur-dravi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "plural", "base-bounty"]),
                new("bowls", "quum-banti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-bowl"]),
                new("breeding", "vrak-mokh-heku-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-breed"]),
                new("bridges", "ashdak-gorui", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-bridge"]),
                new("bringing", "dravku-ik-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-bring"]),
                new("bruises", "morz-vrak-burzi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-bruise"]),
                new("brushes", "vril-thrum-daki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-brush"]),
                new("builds", "grod-vraki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-build"]),
                new("campaigns", "vark-lag-mokhi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-campaign"]),
                new("camps", "dakku-thrumi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-camp"]),
                new("carcasses", "nul-dakur-vraki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-carcass"]),
                new("casters", "gur-narg-mogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-caster"]),
                new("casts", "rukh-oglashur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-cast"]),
                new("causes", "thruk-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-cause"]),
                new("characters", "mog-vari", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-character"]),
                new("chests", "grod-burz-ik-burzuki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-chest"]),
                new("chooses", "vargu-dak-zogur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-choose"]),
                new("choosing", "vargu-dak-zog-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-choose"]),
                new("churches", "mograth-daki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-church"]),
                new("cinches", "darg-bantur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-cinch"]),
                new("cliffs", "ti-zol-daki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-cliff"]),
                new("coals", "burz-rukh-zoli", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-coal"]),
                new("components", "hek-biti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-component"]),
                new("contrasts", "mok-nu-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-contrast"]),
                new("controls", "dargu-mokh-mokhur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-control"]),
                new("cords", "bant-thrumi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-cord"]),
                new("counts", "darg-mog-tii", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-count"]),
                new("crops", "quum-heki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-crop"]),
                new("crowds", "mokh-muri", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-crowd"]),
                new("dagger's", "zol-bituk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-dagger"]),
                new("damages", "brak-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-damage"]),
                new("dangerously", "vark-thoguk-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-dangerous"]),
                new("daughters", "nurik-mogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-daughter"]),
                new("defenses", "gor-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-defense"]),
                new("designs", "narg-vari", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-design"]),
                new("drifting", "varku-thrum-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-drift"]),
                new("drifts", "varku-thrumur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-drift"]),
                new("drives", "doki-dornur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-drive"]),
                new("driving", "doki-dorn-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-drive"]),
                new("drowning", "dak-rukh-morzku-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-drown"]),
                new("dwells", "dakku-doku-kragur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-dwell"]),
                new("escapes", "varkuur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-escape"]),
                new("essentially", "thruk-dorn-zog-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-essential"]),
                new("evenings", "exendai", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-evening"]),
                new("families", "mokh-zogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "plural", "base-family", "root-repaired"]),
                new("fantasies", "thog-nauti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "plural", "base-fantasy"]),
                new("fathers", "flitu-hrowkuri", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-father"]),
                new("feeling", "grodhin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-feel"]),
                new("fields", "quum-hek-mokhi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-field"]),
                new("fiends", "vark-morz-mogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-fiend"]),
                new("fighter's", "gashuk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-fighter"]),
                new("figure's", "mog-var-morz-brakuk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-figure"]),
                new("figures", "mog-var-morz-braki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-figure"]),
                new("flights", "vark-lag-graki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-flight"]),
                new("formally", "bib-darguk-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-formal"]),
                new("formed", "heku-varash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-form", "root-repaired"]),
                new("forming", "heku-var-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-form", "root-repaired"]),
                new("founded", "oglash-lagash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-found"]),
                new("fragments", "bib-braki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-fragment"]),
                new("funding", "drav-zol-thrukku-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-fund"]),
                new("funds", "drav-zol-thrukkuur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-fund"]),
                new("gains", "dravuur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-gain"]),
                new("gallons", "rukh-bant-muri", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-gallon"]),
                new("gathered", "mokhuash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-gather"]),
                new("gathering", "mokhuin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-gather"]),
                new("gathers", "mokhuur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-gather"]),
                new("gazes", "oglar-lagi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-gaze"]),
                new("generally", "arandowkuri-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-general"]),
                new("gifts", "drav-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-gift"]),
                new("girls", "nurik-mog-lag-daki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-girl"]),
                new("gives", "drakuur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-give"]),
                new("giving", "drakuin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-give"]),
                new("gloves", "krub-khali", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-glove"]),
                new("glows", "rukh-oglar-vrak-tuki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-glow"]),
                new("goblin’s", "goblinuk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-goblin"]),
                new("group's", "mokh-zornuk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-group"]),
                new("guard's", "gor-moguk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-guard"]),
                new("guides", "lag-oglar-mogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-guide"]),
                new("hairs", "mog-ti-khali", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-hair"]),
                new("hated", "krughash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-hate"]),
                new("hates", "krughur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-hate"]),
                new("hating", "krughin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-hate"]),
                new("having", "tukrain", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-have"]),
                new("heartbeats", "grod-burz-biti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-heartbeat"]),
                new("hearts", "grod-burzi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-heart"]),
                new("hired", "dravu-mogash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-hire"]),
                new("hiring", "dravu-mog-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-hire"]),
                new("homes", "dakku-daki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-home", "root-repaired"]),
                new("honestly", "grak-tur-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-honest"]),
                new("hopes", "mauk-thruk-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-hope"]),
                new("horn's", "narg-rukh-ti-bantuk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-horn"]),
                new("horns", "narg-rukh-ti-banti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-horn"]),
                new("horrors", "vark-thog-mogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-horror"]),
                new("hunting", "gash-lag-mokh-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-hunt"]),
                new("ideas", "thog-vari", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-idea"]),
                new("illusions", "noglar-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-illusion"]),
                new("immediately", "grak-nak-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-immediate"]),
                new("increased", "ti-hekuash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-increase"]),
                new("increases", "ti-hekuur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-increase"]),
                new("increasing", "ti-heku-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-increase"]),
                new("injected", "rukh-vrak-ikuash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-inject"]),
                new("insects", "thrum-fliti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-insect"]),
                new("inserted", "bruk-ikuash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-insert"]),
                new("inserts", "bruk-ikuur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-insert"]),
                new("insists", "dargu-thogur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-insist"]),
                new("instincts", "vrak-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-instinct"]),
                new("knights", "zol-gash-darg-mogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-knight"]),
                new("leaders", "darg-gash-mogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-leader"]),
                new("leadership", "darg-gash-mogi", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["shared-form", "close-form-reviewed", "leader", "collective", "wiki-fodder"]),
                new("lineages", "mokh-lagi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-lineage"]),
                new("lonely", "ash-mog-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-lone"]),
                new("looked", "oglar-bant-ashash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-look"]),
                new("looting", "drav-varku-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-loot"]),
                new("lunches", "murk-dakur-quumi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-lunch"]),
                new("makes", "hekuur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-make"]),
                new("marches", "gash-lagi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-march"]),
                new("massively", "mur-vrak-mur-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-massive"]),
                new("miracles", "mograth-mauk-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-miracle"]),
                new("mornings", "dakur-suni", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-morning"]),
                new("mothers", "mokh-umi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-mother"]),
                new("muscles", "yank-vraki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-muscle"]),
                new("names", "mog-nargi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-name"]),
                new("nights", "exenda-flit-morzi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-night"]),
                new("obtained", "tukur-dravash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-obtain", "root-repaired"]),
                new("offerings", "dravin-hush-nargur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-offering"]),
                new("openly", "lag-nu-gor-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-open"]),
                new("orders", "darg-lagi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-order"]),
                new("pains", "morz-vrak-thog-tii", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-pain"]),
                new("passes", "laguur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-pass"]),
                new("passing", "laguin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-pass"]),
                new("payments", "quum-dravi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-payment"]),
                new("peoples", "mogii", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-people"]),
                new("plates", "quum-bant-thrumi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-plate"]),
                new("politically", "darg-thoguk-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-political"]),
                new("prayers", "mograth-nargi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-prayer"]),
                new("princes", "darg-ti-mogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-prince"]),
                new("prospects", "mauk-daki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-prospect"]),
                new("purposes", "thruk-thog-mokh-banti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-purpose"]),
                new("pursued", "vark-laguash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-pursue"]),
                new("pursuing", "vark-lagu-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-pursue"]),
                new("qualities", "thrak-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "plural", "base-quality"]),
                new("quietly", "thrum-narg-rukh-ash-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-quiet"]),
                new("received", "drav-dokuash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-receive"]),
                new("receives", "drav-dokuur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-receive"]),
                new("receiving", "drav-doku-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-receive"]),
                new("recovers", "ut-dravkuur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-recover"]),
                new("relatives", "mokh-moguki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-relative"]),
                new("relocated", "ut-dakku-lagash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-relocate"]),
                new("reports", "narg-bib-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-report"]),
                new("rescued", "ut-varkuash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-rescue"]),
                new("rescuing", "ut-varku-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-rescue"]),
                new("reserves", "disasdokur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-reserve"]),
                new("resets", "ut-dakkuur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-reset"]),
                new("retreats", "dok-laguur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-retreat"]),
                new("retrieved", "daki-grodash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-retrieve"]),
                new("roasting", "dakku-grodin-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-roast"]),
                new("robes", "khal-tii", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-robe"]),
                new("rooms", "dak-burzi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-room"]),
                new("ruled", "darguash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-rule"]),
                new("rules", "darguur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-rule"]),
                new("sacrifices", "darg-morz-hekuur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-sacrifice"]),
                new("sacrificing", "darg-morz-heku-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-sacrifice"]),
                new("sakes", "thruk-thoguki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-sake"]),
                new("sausages", "quum-vrak-banti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-sausage"]),
                new("savings", "ut-varkinur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-saving"]),
                new("scrapes", "thrum-biti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-scrap"]),
                new("scripts", "bib-nargi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-script"]),
                new("scroll's", "bib-khaluk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-scroll", "root-repaired"]),
                new("scrolls", "bib-khali", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-scroll", "root-repaired"]),
                new("secretly", "noglar-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-secret", "root-repaired"]),
                new("secured", "dok-ka-grod-morzash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-secure"]),
                new("secures", "dok-ka-grod-morzur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-secure"]),
                new("securing", "dok-ka-grod-morz-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-secure"]),
                new("selling", "dravu-dok-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-sell"]),
                new("sells", "dravu-dokur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-sell"]),
                new("sheets", "bib-vari", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-sheet"]),
                new("shields", "gor-zoli", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-shield"]),
                new("sincerely", "grak-tur-goru-goru-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-sincere"]),
                new("slabs", "hek-quum-banti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-slab"]),
                new("slings", "bant-zol-biti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-sling"]),
                new("slips", "Slipi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-slip"]),
                new("smoothly", "thrum-vrak-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-smooth"]),
                new("socially", "mokhuk-drak-heku-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-social"]),
                new("sparks", "rukh-biti", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-spark"]),
                new("spirits", "grod-thog-tuk-nargi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-spirit"]),
                new("spits", "narg-rukh-thrumi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-spit"]),
                new("spoons", "quum-krubi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-spoon"]),
                new("squares", "murk-mokh-daki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-square"]),
                new("standing", "gor-dargu-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-stand"]),
                new("stands", "gor-darguur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-stand"]),
                new("station's", "darg-dakuk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-station", "root-repaired"]),
                new("stations", "darg-daki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-station", "root-repaired"]),
                new("strains", "grot-thog-vraki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-strain"]),
                new("surfaces", "oglar-daki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-surface"]),
                new("survives", "dakur-thoguur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-survive"]),
                new("tables", "bib-zorn-mokhi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-table"]),
                new("taking", "dravku-krag-flit-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-take"]),
                new("talks", "narg-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-talk"]),
                new("temples", "mograth-dak-tii", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-temple"]),
                new("theirs", "ughatuk-dak-nargi", PartOfSpeech: "pronoun", GrammarClass: "pronoun", Tags: ["review-promoted", "root-derived", "s-form", "base-their"]),
                new("thief's", "drav-varkuk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-thief"]),
                new("thinking", "thogin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-think"]),
                new("throats", "narg-burzi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-throat"]),
                new("thumbs", "krub-tii", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-thumb"]),
                new("tiredly", "grot-vrakuk-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-tired"]),
                new("titles", "darg-mog-nargi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-title"]),
                new("towers", "ti-heki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-tower"]),
                new("treasures", "drav-zol-tii", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-treasure"]),
                new("trophies", "gash-dravi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "plural", "base-trophy"]),
                new("virgins", "nul-brukash-vraki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-virgin"]),
                new("vitally", "thruk-ti-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-vital"]),
                new("wagons", "hrogari", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-wagon"]),
                new("wandered", "vagoruash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-wander"]),
                new("wandering", "vagoruin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-wander"]),
                new("wards", "gor-nargi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-ward"]),
                new("warrior's", "gashuk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-warrior", "root-repaired"]),
                new("warriors", "gashi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-warrior", "root-repaired"]),
                new("waves", "bant-nargi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-wave"]),
                new("weapons", "zol-gash-dak-ashi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-weapon"]),
                new("well's", "dak-rukh-burzuk", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "possessive", "base-well"]),
                new("wells", "dak-rukh-burzi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-well"]),
                new("what's", "mokuk", PartOfSpeech: "pronoun", GrammarClass: "pronoun", Tags: ["review-promoted", "root-derived", "possessive", "base-what"]),
                new("whats", "moki", PartOfSpeech: "pronoun", GrammarClass: "pronoun", Tags: ["review-promoted", "root-derived", "s-form", "base-what"]),
                new("willing", "ukin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-will"]),
                new("voluntary", "ukin", PartOfSpeech: "adjective", GrammarClass: "choice", Tags: ["shared-form", "willing", "choice", "wiki-fodder"]),
                new("wills", "ukur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-will"]),
                new("witches", "gurmog-mogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-witch"]),
                new("witnesses", "oglar-mogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-witness"]),
                new("works", "hek-grum-morzi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-work"]),
                new("yanks", "dravu-grakur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-yank"]),
                // Promoted from TSV cleanup: exodore root repairs.
                new("cobra", "vril-gash-venom", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["review-promoted", "exodore-root-repair", "serpent", "venom"]),
                new("cured", "vrak-groduash", PartOfSpeech: "verb", GrammarClass: "healing", Tags: ["review-promoted", "exodore-root-repair", "past", "healed"]),
                new("deli", "quum-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["review-promoted", "exodore-root-repair", "food", "shop"]),
                new("elbow", "yank-bant-dok", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["review-promoted", "exodore-root-repair", "arm", "joint"]),
                new("halls", "dak-mokh-ti-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["review-promoted", "exodore-root-repair", "hall", "plural"]),
                new("handy", "krub-vrak-thog", PartOfSpeech: "adjective", GrammarClass: "body", Tags: ["review-promoted", "exodore-root-repair", "hand", "useful"]),
                new("hog", "vril-quum-mog-dak", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["review-promoted", "exodore-root-repair", "boar", "pig"]),
                new("huh", "narg-rukh-ashuk", PartOfSpeech: "interjection", GrammarClass: "sound", Tags: ["review-promoted", "exodore-root-repair", "question", "sound"]),
                new("intensity", "grukh-thog-ti", PartOfSpeech: "noun", GrammarClass: "degree", Tags: ["review-promoted", "exodore-root-repair", "force", "abstract"]),
                new("lays", "dak-hekash", PartOfSpeech: "verb", GrammarClass: "placement", Tags: ["review-promoted", "exodore-root-repair", "present"]),
                new("loft", "dak-burz-dok", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["review-promoted", "exodore-root-repair", "upper", "room"]),
                new("momma", "nurik-dakuk", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["review-promoted", "exodore-root-repair", "mother", "familiar"]),
                new("plant", "grodu-vrak", PartOfSpeech: "noun", GrammarClass: "plant", Tags: ["review-promoted", "exodore-root-repair", "growth"]),
                new("scalp", "mog-ti-vrak", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["review-promoted", "exodore-root-repair", "head", "skin"]),
                new("spank", "gash-brak-bant", PartOfSpeech: "verb", GrammarClass: "impact", Tags: ["review-promoted", "exodore-root-repair", "strike", "hand"]),
                new("spike", "zorn-bit-dak", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["review-promoted", "exodore-root-repair", "point"]),
                new("stood", "gor-darguash", PartOfSpeech: "verb", GrammarClass: "posture", Tags: ["review-promoted", "exodore-root-repair", "past", "stand"]),
                new("sub-vault", "burz-dak-burz", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["review-promoted", "exodore-root-repair", "under", "vault"]),
                new("taped", "banti-dargash", PartOfSpeech: "verb", GrammarClass: "binding", Tags: ["review-promoted", "exodore-root-repair", "past", "bound"]),
                new("vocal", "narg-rukhuk", PartOfSpeech: "adjective", GrammarClass: "sound", Tags: ["review-promoted", "exodore-root-repair", "voice"]),
                new("witty", "thog-grak", PartOfSpeech: "adjective", GrammarClass: "thought", Tags: ["review-promoted", "exodore-root-repair", "clever"]),
                new("blow", "gash-rukh", PartOfSpeech: "verb", GrammarClass: "air", Tags: ["breath", "infinitive", "compound"]),
                new("blows", "gash-rukhur", PartOfSpeech: "verb", GrammarClass: "air", Tags: ["breath", "present", "compound"]),
                new("blowing", "gash-rukh-in", PartOfSpeech: "verb", GrammarClass: "air", Tags: ["breath", "progressive", "present", "compound"]),
                new("blowed", "gash-rukhash", PartOfSpeech: "verb", GrammarClass: "air", Tags: ["breath", "past", "nonstandard", "compound"]),
                new("reflecting", "oglarin-brak-brak", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["progressive", "present", "figurative"]),
                new("hunt", "gash-lag-mokh", PartOfSpeech: "verb", GrammarClass: "pursuit", Tags: ["hunt", "infinitive", "compound"]),
                new("hunts", "gash-lag-mokhur", PartOfSpeech: "verb", GrammarClass: "pursuit", Tags: ["hunt", "present", "compound"]),
                new("hunting", "gash-lag-mokhin", PartOfSpeech: "verb", GrammarClass: "pursuit", Tags: ["hunt", "progressive", "present", "compound"]),
                new("hunted", "gash-lag-mokhash", PartOfSpeech: "verb", GrammarClass: "pursuit", Tags: ["hunt", "past", "compound"]),
                new("drown", "dak-rukh-morzku", PartOfSpeech: "verb", GrammarClass: "affliction", Tags: ["water", "death", "infinitive", "compound"]),
                new("drowned", "dak-rukh-morzkuash", PartOfSpeech: "verb", GrammarClass: "affliction", Tags: ["water", "death", "past", "compound"]),
                new("drowning", "dak-rukh-morzkuin", PartOfSpeech: "verb", GrammarClass: "affliction", Tags: ["water", "death", "progressive", "present", "compound"]),
                new("gambler", "akh-hrowkai", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["chance", "risk", "compound"]),
                new("gamblers", "akh-hrowkaii", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["chance", "risk", "plural", "compound"]),
                new("gambles", "akh-hrowkaiur", PartOfSpeech: "verb", GrammarClass: "risk", Tags: ["chance", "present", "compound"]),
                new("gambling", "akh-hrowkaiin", PartOfSpeech: "verb", GrammarClass: "risk", Tags: ["chance", "progressive", "present", "compound"]),
                new("episode", "gahb-goru", PartOfSpeech: "noun", GrammarClass: "story", Tags: ["event", "narrative", "compound"]),
                new("episodes", "gahb-gorui", PartOfSpeech: "noun", GrammarClass: "story", Tags: ["event", "narrative", "plural", "compound"]),
                new("episodic", "gahb-goruuk", PartOfSpeech: "adjective", GrammarClass: "story", Tags: ["event", "narrative", "possessive-derived", "compound"]),
                new("father", "flitu-hrowkur", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["family", "parent", "compound"]),
                new("father's", "flitu-hrowkuruk", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["family", "parent", "possessive", "compound"]),
                new("general", "arandowkuri", PartOfSpeech: "adjective", GrammarClass: "scope", Tags: ["broad", "common"]),
                new("grove", "gruul-mokh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["trees", "small-group", "place", "compound"]),
                new("groves", "gruul-mokh-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["trees", "small-group", "place", "plural", "compound"]),

                new("relies on", "lag-tukur ak", PartOfSpeech: "verb", GrammarClass: "support", Tags: ["present", "dependence", "fixed-phrase"]),
                new("known as", "mog-oglar mok", PartOfSpeech: "verb", GrammarClass: "reputation", Tags: ["known", "fixed-phrase"]),
                new("responsible for safeguarding", "tukur-darg gorin", PartOfSpeech: "verb", GrammarClass: "protection", Tags: ["responsibility", "progressive", "fixed-phrase"]),
                new("emerging", "dok-varkin", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "origin", "compound"]),
                new("marked by", "nargash fa", PartOfSpeech: "verb", GrammarClass: "description", Tags: ["marked", "fixed-phrase"]),
                new("engaged in", "hekin ik", PartOfSpeech: "verb", GrammarClass: "labor", Tags: ["working", "fixed-phrase"]),
                new("living in the shadow", "dakkin k'ik arhk burz-nak", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["progressive", "shadow", "fixed-phrase"]),
                new("posed by", "nargash fa", PartOfSpeech: "verb", GrammarClass: "description", Tags: ["caused-by", "fixed-phrase", "root-repaired"]),
                new("no strangers to", "noglar-nu ur", PartOfSpeech: "verb", GrammarClass: "experience", Tags: ["familiar", "fixed-phrase"]),
                new("presents", "dravur-goth-rukh", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["present", "offers"]),
                new("aiding", "dravin", PartOfSpeech: "verb", GrammarClass: "support", Tags: ["progressive", "help", "root-repaired"]),
                new("engaging in", "hekin ik", PartOfSpeech: "verb", GrammarClass: "labor", Tags: ["working", "fixed-phrase", "root-repaired"]),
                new("slides into", "varkin ik", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["present", "entering", "fixed-phrase"]),
                new("pushing", "brukin-zog-brak", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["progressive", "force"]),
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
                new("acknowledge", "nargu-thog", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["infinitive", "acknowledgement", "compound", "derive-present", "derive-past"]),
                new("moves", "lagur", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["present"]),
                new("moves to take", "lagur ur dravku", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["present", "taking", "fixed-phrase"]),
                new("take", "dravku-krag-flit", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["infinitive"]),
                new("stares into", "mur-oglur ik", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["present", "staring", "fixed-phrase"]),
                new("stares", "mur-oglur", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["present", "staring", "compound"]),
                new("bound for", "lagash fa", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["past-participle", "destination", "fixed-phrase"]),
                new("have", "tukra", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["present"]),
                new("cripple", "kangsin", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["subject-complement"]),
                new("crippled", "kangsin-vrak-zog", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["subject-complement", "past-participle"]),
                new("welcome", "mokra-dak", PartOfSpeech: "adjective", GrammarClass: "acceptance", Tags: ["friendly"]),
                new("Well met", "mokra-narg", PartOfSpeech: "interjection", GrammarClass: "greeting", Tags: ["greeting", "fixed-phrase", "root-repaired"]),
                new("please", "mauk-drav", PartOfSpeech: "interjection", GrammarClass: "courtesy", Tags: ["request", "polite", "fixed-phrase"]),
                new("Obliged", "tukru-drav", PartOfSpeech: "interjection", GrammarClass: "courtesy", Tags: ["thanks", "debt", "fixed-phrase"]),
                new("abandoned", "nul-dakkin", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["deserted", "place"]),
                new("hidden", "noglar", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["concealed", "place"]),
                new("secret", "noglar", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["concealed", "root-repaired"]),
                new("famous", "mur-oglar", PartOfSpeech: "adjective", GrammarClass: "reputation", Tags: ["known", "compound"]),
                new("expensive", "drav-ti", PartOfSpeech: "adjective", GrammarClass: "value", Tags: ["costly", "compound"]),
                new("braided", "bantin", PartOfSpeech: "adjective", GrammarClass: "appearance", Tags: ["braided"]),
                new("triple braided", "dug-agh-ash bantin", PartOfSpeech: "adjective", GrammarClass: "appearance", Tags: ["three", "braided", "fixed-phrase"]),
                new("triple", "dug-agh-ash", PartOfSpeech: "adjective", GrammarClass: "quantity", Tags: ["three", "multiplier"]),
                new("hardworking", "mur-hekin", PartOfSpeech: "adjective", GrammarClass: "labor", Tags: ["labor", "intense", "compound"]),
                new("essential", "thruk-dorn-zog", PartOfSpeech: "adjective", GrammarClass: "requirement", Tags: ["essential"]),
                new("communal", "mokhuk-drak-grod", PartOfSpeech: "adjective", GrammarClass: "society", Tags: ["communal", "possessive-derived"]),
                new("social", "mokhuk-drak-heku", PartOfSpeech: "adjective", GrammarClass: "society", Tags: ["social", "possessive-derived"]),
                new("modest", "thrum", PartOfSpeech: "adjective", GrammarClass: "degree", Tags: ["modest", "small"]),
                new("vital", "thruk-ti", PartOfSpeech: "adjective", GrammarClass: "requirement", Tags: ["vital", "essential", "intensified"]),
                new("following", "ut", PartOfSpeech: "adjective", GrammarClass: "sequence", Tags: ["following"]),
                new("lost", "nul-lag", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["missing", "place"]),
                new("dwarven", "dwarfuk", PartOfSpeech: "adjective", GrammarClass: "species", Tags: ["possessive-derived", "exonym"]),
                new("lanky", "thrum-yank", PartOfSpeech: "adjective", GrammarClass: "body", Tags: ["thin", "tall", "compound"]),
                new("comfortable", "grod-dakkin", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["comfortable", "compound"]),
                new("comforting", "grod-dakkin-goru-drak", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["comforting", "compound"]),
                new("further", "dok-ti", PartOfSpeech: "adjective", GrammarClass: "distance", Tags: ["farther", "compound"]),
                new("vulnerable", "gor-nu-grod", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["unprotected", "compound"]),
                new("penniless", "nul-drav-zol", PartOfSpeech: "adjective", GrammarClass: "poverty", Tags: ["money", "negative", "compound"]),
                new("familiar", "noglar-nu", PartOfSpeech: "adjective", GrammarClass: "experience", Tags: ["familiar", "compound"]),
                new("evening", "exenda", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["evening", "night"]),
                new("honest", "grak-tur", PartOfSpeech: "adjective", GrammarClass: "virtue", Tags: ["honest", "compound"]),
                new("open", "lag-nu-gor", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["open", "compound"]),
                new("friendly", "mokra-grod", PartOfSpeech: "adjective", GrammarClass: "acceptance", Tags: ["friendly", "compound"]),
                new("missing", "nul-lag-dak-hrowk", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["missing", "place"]),
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
                new("fair", "drav-mauk-brak-mokh", PartOfSpeech: "adjective", GrammarClass: "appearance", Tags: ["fair", "pleasant", "compound"]),
                new("downy", "thrum-khal", PartOfSpeech: "adjective", GrammarClass: "appearance", Tags: ["soft-hair", "compound"]),
                new("blond", "surg-narg-bibi", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["blond", "sun"]),
                new("above average", "ti-grak", PartOfSpeech: "adjective", GrammarClass: "comparison", Tags: ["above-average", "compound"]),
                new("strapping", "yank-grod", PartOfSpeech: "adjective", GrammarClass: "body", Tags: ["strong", "compound"]),
                new("starker", "mur-oglar-ti", PartOfSpeech: "adjective", GrammarClass: "comparison", Tags: ["starker", "comparative", "compound"]),
                new("brass", "zol-mauk", PartOfSpeech: "adjective", GrammarClass: "material", Tags: ["brass", "metal", "compound"]),
                new("stout", "yank-grod-thog-narg", PartOfSpeech: "adjective", GrammarClass: "strength", Tags: ["stout", "strong", "compound"]),
                new("shallow", "thrum-burz", PartOfSpeech: "adjective", GrammarClass: "shape", Tags: ["shallow", "compound"]),
                new("same", "grak-mok", PartOfSpeech: "adjective", GrammarClass: "comparison", Tags: ["same", "compound"]),
                new("awkward", "lag-grot", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["awkward", "compound"]),
                new("awkwardly", "lag-grotin", PartOfSpeech: "adverb", GrammarClass: "condition", Tags: ["awkward", "compound"]),
                new("leather", "vrak-zog-drak", PartOfSpeech: "adjective", GrammarClass: "material", Tags: ["leather", "hide"]),
                new("laboring", "hekin-mokh-narg", PartOfSpeech: "adjective", GrammarClass: "labor", Tags: ["labor", "progressive"]),
                new("many", "mur", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["many"]),
                new("handsome", "mauk-mogum", PartOfSpeech: "adjective", GrammarClass: "appearance", Tags: ["handsome", "compound"]),
                new("lingering", "grotin-dak", PartOfSpeech: "adjective", GrammarClass: "delay", Tags: ["lingering", "compound"]),
                new("occasional", "varg-dakur", PartOfSpeech: "adjective", GrammarClass: "time", Tags: ["occasional", "compound"]),
                new("truly", "grak-tur-gash-goru", PartOfSpeech: "adverb", GrammarClass: "certainty", Tags: ["truth", "compound"]),
                new("never", "nul-dakur", PartOfSpeech: "adverb", GrammarClass: "time", Tags: ["never", "negative", "compound"]),
                new("little", "thrum-kaag-hrowk", PartOfSpeech: "adjective", GrammarClass: "quantity", Tags: ["small"]),
                new("small", "thrum-brak-grrt", PartOfSpeech: "adjective", GrammarClass: "quantity", Tags: ["small"]),
                new("new", "nurik", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["new"]),
                new("older", "drath-ti", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["comparative"]),
                new("quiet", "thrum-narg-rukh-ash", PartOfSpeech: "adjective", GrammarClass: "sound", Tags: ["quiet", "compound"]),
                new("common", "mokhuk-grrt-karn", PartOfSpeech: "adjective", GrammarClass: "society", Tags: ["common", "possessive-derived"]),
                new("long", "mur-dakur-grum-narg", PartOfSpeech: "adjective", GrammarClass: "time", Tags: ["long", "compound"]),
                new("lone", "ash-mog", PartOfSpeech: "adjective", GrammarClass: "quantity", Tags: ["alone", "compound"]),
                new("dangerous", "vark-thoguk", PartOfSpeech: "adjective", GrammarClass: "danger", Tags: ["danger", "possessive-derived"]),
                new("dark", "burzuk", PartOfSpeech: "adjective", GrammarClass: "light", Tags: ["dark", "possessive-derived"]),
                new("neighbourly", "nak-dak-mokhuk", PartOfSpeech: "adjective", GrammarClass: "society", Tags: ["local", "helpful", "british", "compound"]),
                new("warm", "rukh-grod", PartOfSpeech: "adjective", GrammarClass: "temperature", Tags: ["warm", "compound"]),
                new("single", "ash", PartOfSpeech: "adjective", GrammarClass: "quantity", Tags: ["single"]),
                new("toothless", "nul-togruk", PartOfSpeech: "adjective", GrammarClass: "body", Tags: ["toothless", "negative", "compound"]),
                new("toothless smile", "nul-togruk mauk-narg", PartOfSpeech: "noun", GrammarClass: "expression", Tags: ["toothless", "smile", "fixed-phrase"]),
                new("hoary", "murdrath-kelnib", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["very-old", "pale", "compound"]),
                new("hoary with age", "murdrath-kelnib dakuruk", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["very-old", "fixed-phrase"]),
                new("well into his second century", "grak ik mogumuk dug mur-dakur", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["aged", "fixed-phrase"]),
                new("still youthful enough", "ashdak nurik grod", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["youthful", "sufficient", "fixed-phrase"]),
                new("youthful", "nurik-grod", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["young", "vigorous", "compound"]),
                new("red", "rug", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["default"]),
                new("Red", "rug-grum-lag", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["default"]),
                new("formal", "bib-darguk", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["formal", "written-law", "compound"]),
                new("significant", "thrak-grak", PartOfSpeech: "adjective", GrammarClass: "importance", Tags: ["important", "compound"]),
                new("defensive", "goruk", PartOfSpeech: "adjective", GrammarClass: "protection", Tags: ["defense", "possessive-derived"]),
                new("brave", "yanki-grod", PartOfSpeech: "adjective", GrammarClass: "virtue", Tags: ["courage", "compound"]),
                new("untrained", "nul-hekin", PartOfSpeech: "adjective", GrammarClass: "skill", Tags: ["untrained", "negative", "compound"]),
                new("responsible", "tukur-darg", PartOfSpeech: "adjective", GrammarClass: "duty", Tags: ["responsibility", "compound"]),
                new("nearby", "nak", PartOfSpeech: "adjective", GrammarClass: "location", Tags: ["nearby"]),
                new("rugged", "mur-grod", PartOfSpeech: "adjective", GrammarClass: "virtue", Tags: ["rugged", "compound"]),
                new("resilient", "grotash-nu", PartOfSpeech: "adjective", GrammarClass: "virtue", Tags: ["resilient", "negative-break", "compound"]),
                new("chief", "thrak-darg-zog", PartOfSpeech: "adjective", GrammarClass: "importance", Tags: ["primary", "important"]),
                new("local", "nak-dakuk", PartOfSpeech: "adjective", GrammarClass: "place", Tags: ["local", "possessive-derived"]),
                new("religious", "mograthuk-gash-flit", PartOfSpeech: "adjective", GrammarClass: "religion", Tags: ["possessive-derived"]),
                new("puritanical", "mur-mograth-darg", PartOfSpeech: "adjective", GrammarClass: "religion", Tags: ["strict", "religious", "compound"]),
                new("more puritanical", "mur-mograth-darg-ti", PartOfSpeech: "adjective", GrammarClass: "religion", Tags: ["strict", "comparative", "religious", "compound"]),
                new("relaxed", "thrum-darg", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["lenient", "law", "compound"]),
                new("more relaxed", "thrum-darg-ti", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["lenient", "comparative", "law", "compound"]),
                new("lenient", "thrum-darg-lag-lag", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["lenient", "law", "compound"]),
                new("more lenient", "thrum-darg-ti-goth-brak", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["lenient", "comparative", "law", "compound"]),
                new("rigid", "mur-darg", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["strict", "law", "compound"]),
                new("less rigid", "mur-darg-nu-ti", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["less", "strict", "comparative", "law", "compound"]),
                new("notably more lenient", "oglar-ti thrum-darg-ti", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["noticeable", "lenient", "comparative", "fixed-phrase"]),
                new("approach", "lag-thog-krag-burz", PartOfSpeech: "noun", GrammarClass: "position", Tags: ["viewpoint", "compound"]),
                new("immediate", "grak-nak", PartOfSpeech: "adjective", GrammarClass: "location", Tags: ["nearby", "emphatic", "compound"]),
                new("surrounding", "nak-krag-dak", PartOfSpeech: "adjective", GrammarClass: "location", Tags: ["nearby"]),
                new("cripple", "kangstuk", PartOfSpeech: "verb", GrammarClass: "harm", Tags: ["infinitive"]),
                new("crippled", "kangstash", PartOfSpeech: "verb", GrammarClass: "harm", Tags: ["past"]),
                new("I", "Ugh", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["variant-a", "plain"]),
                new("I", "Grrt", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["variant-b", "plain"]),
                new("myself", "Ughuk", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["variant-a", "intensive"]),
                new("myself", "Grrtuk", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["variant-b", "intensive"]),
                new("I'm", "Ughma", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["contraction", "first-person", "state"]),
                new("you", "narg", PartOfSpeech: "pronoun", GrammarClass: "second-person", Tags: ["plain"]),
                new("your", "narguk", PartOfSpeech: "pronoun", GrammarClass: "second-person", Tags: ["possessive"]),
                new("yours", "narguk", PartOfSpeech: "pronoun", GrammarClass: "second-person", Tags: ["possessive", "root-repaired"]),
                new("he", "mogum", PartOfSpeech: "pronoun", GrammarClass: "third-person", Tags: ["masculine", "plain"]),
                new("He", "mogum-brak-krag", PartOfSpeech: "pronoun", GrammarClass: "third-person", Tags: ["masculine", "plain"]),
                new("him", "mogum-karn-kaag", PartOfSpeech: "pronoun", GrammarClass: "third-person", Tags: ["masculine", "object"]),
                new("his", "mogumuk", PartOfSpeech: "pronoun", GrammarClass: "third-person", Tags: ["masculine", "possessive"]),
                new("her", "umuk", PartOfSpeech: "pronoun", GrammarClass: "third-person", Tags: ["feminine", "object"]),
                new("they", "ughat", PartOfSpeech: "pronoun", GrammarClass: "other", Tags: ["plural", "plain"]),
                new("them", "ughatum", PartOfSpeech: "pronoun", GrammarClass: "other", Tags: ["plural", "object"]),
                new("who", "lek", PartOfSpeech: "pronoun", GrammarClass: "relative", Tags: ["relative"]),
                new("whose", "ughatuk", PartOfSpeech: "pronoun", GrammarClass: "relative", Tags: ["possessive", "relative"]),
                new("their", "ughatuk-dak-narg", PartOfSpeech: "pronoun", GrammarClass: "other", Tags: ["possessive", "plural"]),
                new("it", "um", PartOfSpeech: "pronoun", GrammarClass: "thing", Tags: ["plain"]),
                new("its", "umuk-zog-thog", PartOfSpeech: "pronoun", GrammarClass: "thing", Tags: ["possessive"]),
                new("really", "grak", PartOfSpeech: "adverb", GrammarClass: "emphasis", Tags: ["variant-a", "plain"]),
                new("really", "urkh", PartOfSpeech: "adverb", GrammarClass: "emphasis", Tags: ["variant-b", "plain"]),
                new("there", "dak-doku-darg", PartOfSpeech: "adverb", GrammarClass: "location", Tags: ["locative", "existential"]),
                new("somewhere", "varg-dak-grod-karn", PartOfSpeech: "adverb", GrammarClass: "location", Tags: ["indefinite", "compound"]),
                new("perhaps", "mauk-grak", PartOfSpeech: "adverb", GrammarClass: "possibility", Tags: ["uncertainty", "compound"]),
                new("ago", "dakur-ash", PartOfSpeech: "adverb", GrammarClass: "time", Tags: ["past", "compound"]),
                new("however", "rokh-grak", PartOfSpeech: "adverb", GrammarClass: "contrast", Tags: ["contrastive", "compound"]),
                new("However", "rokh-grak-bant-zog", PartOfSpeech: "adverb", GrammarClass: "contrast", Tags: ["contrastive", "compound"]),
                new("alongside", "mokru-nak", PartOfSpeech: "adverb", GrammarClass: "association", Tags: ["beside", "compound"]),
                new("notably", "oglar-ti", PartOfSpeech: "adverb", GrammarClass: "reputation", Tags: ["noticeable", "compound"]),
                new("primarily", "thrak-grak-lag-krag", PartOfSpeech: "adverb", GrammarClass: "importance", Tags: ["primary", "compound"]),
                new("significantly", "thrak-grak-flit-tuk", PartOfSpeech: "adverb", GrammarClass: "importance", Tags: ["important", "compound"]),
                new("Additionally", "agh-agh", PartOfSpeech: "adverb", GrammarClass: "addition", Tags: ["additive", "compound"]),
                new("also", "agh-agh-grrt-darg", PartOfSpeech: "adverb", GrammarClass: "addition", Tags: ["additive", "compound"]),
                new("albeit", "rokh", PartOfSpeech: "adverb", GrammarClass: "contrast", Tags: ["concession"]),
                new("particularly", "thrak-grak-dak-dak", PartOfSpeech: "adverb", GrammarClass: "importance", Tags: ["particular", "compound"]),
                new("often", "murdakur", PartOfSpeech: "adverb", GrammarClass: "time", Tags: ["frequent", "compound"]),
                new("only", "thrum-grak", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["only", "limiting", "compound"]),
                new("least", "thrum-grak-krag-morz", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["minimum", "compound"]),
                new("no further", "nu dok-ti", PartOfSpeech: "adverb", GrammarClass: "distance", Tags: ["negative", "fixed-phrase"]),
                new("at most", "ak mur", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["maximum", "fixed-phrase"]),
                new("out there", "dak dok-dak", PartOfSpeech: "adverb", GrammarClass: "location", Tags: ["outside", "fixed-phrase"]),
                new("however", "rokh-grak-grrt-hrowk", PartOfSpeech: "adverb", GrammarClass: "contrast", Tags: ["contrastive", "compound"]),
                new("unconsciously", "nul-thogin", PartOfSpeech: "adverb", GrammarClass: "thought", Tags: ["unconscious", "negative", "compound"]),
                new("back", "dok", PartOfSpeech: "adverb", GrammarClass: "direction", Tags: ["back"]),
                new("not", "nu-brak-burz", PartOfSpeech: "adverb", GrammarClass: "negation", Tags: ["negative"]),
                new("still", "ashdak-darg-gash", PartOfSpeech: "adverb", GrammarClass: "continuity", Tags: ["continuing"]),
                new("Still", "ashdak-narg-darg", PartOfSpeech: "adverb", GrammarClass: "continuity", Tags: ["continuing"]),
                new("at least", "thrum-grak-doku-dorn", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["minimum", "fixed-phrase"]),
                new("For now", "dakur-lek", PartOfSpeech: "adverb", GrammarClass: "time", Tags: ["present", "fixed-phrase"]),
                new("Comfortably", "grod-dakkin-rukh-zog", PartOfSpeech: "adverb", GrammarClass: "condition", Tags: ["comfort", "compound"]),
                new("though", "rokh-thog-hush", PartOfSpeech: "adverb", GrammarClass: "contrast", Tags: ["concession"]),
                new("alone", "ash-mog-vrak-grrt", PartOfSpeech: "adverb", GrammarClass: "quantity", Tags: ["alone", "compound"]),
                new("thoughtfully", "thogin-grak", PartOfSpeech: "adverb", GrammarClass: "thought", Tags: ["thoughtful", "compound"]),
                new("even", "agh-grak", PartOfSpeech: "adverb", GrammarClass: "emphasis", Tags: ["inclusive", "compound"]),
                new("now", "dakur-lek-krag-brak", PartOfSpeech: "adverb", GrammarClass: "time", Tags: ["present", "compound"]),
                new("Although", "rokh-ut", PartOfSpeech: "adverb", GrammarClass: "contrast", Tags: ["concession", "compound"]),
                new("initially", "ashdak-dorn-rukh", PartOfSpeech: "adverb", GrammarClass: "sequence", Tags: ["initial"]),
                new("then", "ut-dakur", PartOfSpeech: "adverb", GrammarClass: "sequence", Tags: ["then", "sequence", "compound"]),
                new("then to", "ut-dakur ur", PartOfSpeech: "adverb", GrammarClass: "sequence", Tags: ["then", "direction", "fixed-phrase"]),
                new("of course", "grak-tur-kaag-goru", PartOfSpeech: "adverb", GrammarClass: "certainty", Tags: ["fixed-phrase"]),
                new("too much", "murgrom", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["excess"]),
                new("much more", "mur-ti", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["comparative", "compound"]),
                new("much more than", "mur-ti mok", PartOfSpeech: "adverb", GrammarClass: "degree", Tags: ["comparative", "fixed-phrase"]),
                new("those", "lek-dak-tuk", PartOfSpeech: "determiner", GrammarClass: "demonstrative"),
                new("these", "lek-doku-drak", PartOfSpeech: "determiner", GrammarClass: "demonstrative"),
                new("this", "um-lek", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["singular", "near"]),
                new("other such", "agh-lek", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["similar", "additional", "fixed-phrase"]),
                new("three", "dug-agh-ash", PartOfSpeech: "numeral", GrammarClass: "cardinal"),
                new("ten", "gakh", PartOfSpeech: "numeral", GrammarClass: "cardinal"),
                new("decade", "gakh-dakur-tiwi", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["compound", "compound-reviewed", "ten", "years", "wiki-fodder"]),
                new("dozen", "gakh-agh-dug", PartOfSpeech: "numeral", GrammarClass: "cardinal", Tags: ["twelve", "compound"]),
                new("few", "nik", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["small-quantity"]),
                new("most", "mur-flit-mokh", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["majority"]),
                new("some", "varg-burz-bant", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["indefinite"]),
                new("both", "dug-grak", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["both", "two", "compound"]),
                new("Both", "dug-grak-goru-dorn", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["both", "two", "compound"]),
                new("enough", "grod-grak", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["sufficient", "compound"]),
                new("all", "mur-kaag-tuk", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["all"]),
                new("each", "ash-ash", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["each", "compound"]),
                new("any", "varg-lag-narg", PartOfSpeech: "determiner", GrammarClass: "quantity", Tags: ["any"]),
                new("such", "lek-mok", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["such", "compound"]),
                new("first", "ash-drak-doku", PartOfSpeech: "numeral", GrammarClass: "ordinal", Tags: ["first"]),
                new("one", "ash-dorn-bibi", PartOfSpeech: "numeral", GrammarClass: "cardinal", Tags: ["one"]),
                new("twenty", "dug-gakh", PartOfSpeech: "numeral", GrammarClass: "cardinal", Tags: ["twenty", "compound"]),
                new("trio", "dug-agh-ash-ash-doku", PartOfSpeech: "numeral", GrammarClass: "cardinal", Tags: ["three"]),
                new("key", "thrak-hrowk-darg", PartOfSpeech: "adjective", GrammarClass: "importance", Tags: ["important"]),
                new("the", "arhk", PartOfSpeech: "determiner", GrammarClass: "article", Tags: ["default", "before-consonant"]),
                new("the", "karnt", PartOfSpeech: "determiner", GrammarClass: "article", Tags: ["before-vowel"]),
                new("a", "ash-hush-grrt", PartOfSpeech: "determiner", GrammarClass: "article", Tags: ["indefinite"]),
                new("an", "ash-brak-dorn", PartOfSpeech: "determiner", GrammarClass: "article", Tags: ["indefinite"]),
                new("at", "ak", PartOfSpeech: "preposition", GrammarClass: "location", Tags: ["default", "before-consonant"]),
                new("at", "kaat", PartOfSpeech: "preposition", GrammarClass: "location", Tags: ["before-vowel"]),
                new("to", "ur", PartOfSpeech: "preposition", GrammarClass: "direction", Tags: ["default", "before-consonant"]),
                new("to", "kur", PartOfSpeech: "preposition", GrammarClass: "direction", Tags: ["before-vowel"]),
                new("in", "ik", PartOfSpeech: "preposition", GrammarClass: "location", Tags: ["default", "before-consonant"]),
                new("in", "k'ik", PartOfSpeech: "preposition", GrammarClass: "location", Tags: ["before-vowel"]),
                new("like", "mok-lag-darg", PartOfSpeech: "preposition", GrammarClass: "comparison", Tags: ["default"]),
                new("as", "mok-vrak-flit", PartOfSpeech: "preposition", GrammarClass: "comparison", Tags: ["role"]),
                new("from", "dok-darg-krag", PartOfSpeech: "preposition", GrammarClass: "origin", Tags: ["source"]),
                new("of", "uk-ash-morz", PartOfSpeech: "preposition", GrammarClass: "possession", Tags: ["genitive"]),
                new("on", "ak-dak-vrak", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["default"]),
                new("over", "dak-uk", PartOfSpeech: "preposition", GrammarClass: "authority", Tags: ["dominion", "compound"]),
                new("under", "dak-uk-karn-flit", PartOfSpeech: "preposition", GrammarClass: "authority", Tags: ["dominion", "compound"]),
                new("around", "nak-goru-bant", PartOfSpeech: "preposition", GrammarClass: "location", Tags: ["nearby"]),
                new("near", "nak-ash-grod", PartOfSpeech: "preposition", GrammarClass: "location", Tags: ["nearby"]),
                new("with", "ogh", PartOfSpeech: "preposition", GrammarClass: "association", Tags: ["default"]),
                new("for", "fa", PartOfSpeech: "preposition", GrammarClass: "purpose", Tags: ["purpose"]),
                new("access to", "lag ur", PartOfSpeech: "preposition", GrammarClass: "access", Tags: ["access", "fixed-phrase"]),
                new("against", "mok-nu", PartOfSpeech: "preposition", GrammarClass: "opposition", Tags: ["opposition", "compound"]),
                new("into", "ik-hush-hrowk", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["interior"]),
                new("inside", "ik-kaag-doku", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["interior"]),
                new("toward", "ur-doku-goth", PartOfSpeech: "preposition", GrammarClass: "direction", Tags: ["direction"]),
                new("until", "ur-dakur", PartOfSpeech: "preposition", GrammarClass: "time", Tags: ["until", "compound"]),
                new("between", "murk", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["between"]),
                new("intercontinental", "murk-dak-mur-ti", PartOfSpeech: "adjective", GrammarClass: "scope", Tags: ["compound", "compound-reviewed", "between", "land", "wiki-fodder"]),
                new("international", "murk-dak-muri", PartOfSpeech: "adjective", GrammarClass: "scope", Tags: ["compound", "compound-reviewed", "close-form-reviewed", "between", "land", "plural", "wiki-fodder"]),
                new("across", "dak-nak", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["across", "compound"]),
                new("beside", "mokru-nak-narg-ash", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["beside", "compound"]),
                new("by", "nak-narg-grrt", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["nearby"]),
                new("behind", "dok-krag-morz", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["behind"]),
                new("about", "nak-mokh-goru", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["nearby"]),
                new("than", "mok-kaag-zog", PartOfSpeech: "preposition", GrammarClass: "comparison", Tags: ["comparative"]),
                new("through", "mokru-ik", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["through", "compound"]),
                new("upon", "dak-uk-goth-karn", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["upon"]),
                new("beneath", "burz-nak-hush-morz", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["beneath", "compound"]),
                new("after", "dok-dak-tuk", PartOfSpeech: "preposition", GrammarClass: "origin", Tags: ["source"]),
                new("unlike", "mok-nu-doku-heku", PartOfSpeech: "preposition", GrammarClass: "comparison", Tags: ["contrast", "compound"]),
                new("depending on", "ut-lag", PartOfSpeech: "preposition", GrammarClass: "condition", Tags: ["fixed-phrase"]),
                new("those formidable ones", "lekyanki", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["formidable", "marked"]),
                new("these formidable ones", "lekyanki-doku-vrak", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["formidable", "marked"]),
                new("two", "dug", PartOfSpeech: "numeral", GrammarClass: "cardinal"),
                new("second", "dug-lag-hrowk", PartOfSpeech: "numeral", GrammarClass: "ordinal"),
                new("III", "dug-agh-ash-mokh-burz", PartOfSpeech: "numeral", GrammarClass: "ordinal", Tags: ["roman", "third"]),
                new("IV", "dug-agh-dug", PartOfSpeech: "numeral", GrammarClass: "ordinal", Tags: ["roman", "fourth"]),
                new("if", "ut", PartOfSpeech: "conjunction", GrammarClass: "condition", Tags: ["variant-a", "plain", "alternating", "root-repaired"]),
                new("if", "ka", PartOfSpeech: "conjunction", GrammarClass: "condition", Tags: ["variant-b", "plain", "alternating"]),
                new("when", "dakur-ut", PartOfSpeech: "conjunction", GrammarClass: "time", Tags: ["temporal", "compound"]),
                new("just as", "mok-grak", PartOfSpeech: "conjunction", GrammarClass: "comparison", Tags: ["equivalence", "fixed-phrase"]),
                new("as does", "mok-grak-bibi-bibi", PartOfSpeech: "conjunction", GrammarClass: "comparison", Tags: ["equivalence", "fixed-phrase"]),
                new("be it", "tar ut", PartOfSpeech: "conjunction", GrammarClass: "choice", Tags: ["alternative", "fixed-phrase"]),
                new("so that", "mok-ut", PartOfSpeech: "conjunction", GrammarClass: "purpose", Tags: ["purpose", "fixed-phrase"]),
                new("that", "ut-karn-morz", PartOfSpeech: "conjunction", GrammarClass: "relative", Tags: ["relative"]),
                new("while", "rokh-dakur", PartOfSpeech: "conjunction", GrammarClass: "time", Tags: ["while", "compound"]),
                new("and", "agh", PartOfSpeech: "conjunction", GrammarClass: "addition", Tags: ["plain", "root-repaired"]),
                new("or", "ogh", PartOfSpeech: "conjunction", GrammarClass: "alternative", Tags: ["plain", "root-repaired"]),
                new("but", "rokh", PartOfSpeech: "conjunction", GrammarClass: "contrast", Tags: ["variant-a", "plain", "root-repaired"]),
                new("but", "nar", PartOfSpeech: "conjunction", GrammarClass: "contrast", Tags: ["variant-b", "plain"]),
                new("sarcastic but", "rokhki", PartOfSpeech: "conjunction", GrammarClass: "contrast", Tags: ["variant-a", "sarcastic"]),
                new("sarcastic but", "narki", PartOfSpeech: "conjunction", GrammarClass: "contrast", Tags: ["variant-b", "sarcastic"]),
                new("old", "drath", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["default"]),
                new("very old", "murdrath", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["intensified"]),
                new("young", "nurik-vrak-vrak", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["default"]),
                new("very young", "murnurik", PartOfSpeech: "adjective", GrammarClass: "age", Tags: ["intensified"]),
                new("healthy", "grod", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["default"]),
                new("young and healthy", "nurik-grod-doku-flit", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["compound"]),
                new("sickly", "morz-grod-vrak", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["default"]),
                new("pale", "kelnib", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["neutral", "root-repaired"]),
                new("pale with fear", "kelnagak", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["fear", "pejorative"]),
                new("fear-pale", "kelnagak-rukh-karn", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["fear", "pejorative"]),
                new("readers of strange books", "zruk-bib-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["learned", "text", "plural"]),
                new("robe-wearers", "khal-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["garb", "plural"]),
                new("small groups", "nikmokhi", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["small", "plural"]),
                new("strong fighters", "yanki-gash", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["strong", "martial", "plural"]),
                new("strong ones", "yankith", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["strong", "plural"]),
                new("inhabitant", "dak-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["inhabited", "resident", "compound"]),
                new("inhabitants", "dak-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["inhabited", "resident", "plural", "compound"]),
                new("population", "dak-mogi-zorn", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["compound", "compound-reviewed", "inhabitants", "number", "wiki-fodder"]),
                new("resident", "dak-mog-hrowk-morz", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["inhabited", "resident", "compound"]),
                new("residents", "dak-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["inhabited", "resident", "plural", "compound", "root-repaired"]),
                new("adventurer", "vark-yank-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["danger", "wayfarer", "compound"]),
                new("adventurers", "vark-yank-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["danger", "wayfarer", "plural", "compound"]),
                new("dwarf", "dwarf", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["dwarven-race", "species", "exonym"]),
                new("noble", "darg-ti-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["ruler", "noble", "compound", "root-repaired"]),
                new("member", "mokh-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["group", "compound"]),
                new("those carrying scrolls", "lek bib-hrowkai", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["demonstrative", "text", "plural"])
            };

            entries.AddRange([
                new("she", "umra", PartOfSpeech: "pronoun", GrammarClass: "third-person", Tags: ["feminine", "plain"]),
                new("we", "ugh-mokh", PartOfSpeech: "pronoun", GrammarClass: "first-person", Tags: ["plural", "plain"]),
                new("me", "ughum", PartOfSpeech: "pronoun", GrammarClass: "first-person", Tags: ["object"]),
                new("my", "ugh-uk", PartOfSpeech: "pronoun", GrammarClass: "first-person", Tags: ["possessive"]),
                new("us", "ugh-mokhum", PartOfSpeech: "pronoun", GrammarClass: "first-person", Tags: ["plural", "object"]),
                new("can", "mauk-grrt-ash", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["possibility", "ability"]),
                new("would", "mauk-heku-grum", PartOfSpeech: "verb", GrammarClass: "modal", Tags: ["conditional"]),
                new("good", "grod-zog-dorn", PartOfSpeech: "adjective", GrammarClass: "virtue", Tags: ["positive"]),
                new("night", "exenda-flit-morz", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["night", "evening"]),
                new("glove", "krub-khal", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["hand", "garb", "compound"]),
                new("tracking", "lag-nargin", PartOfSpeech: "noun", GrammarClass: "stealth", Tags: ["trail", "following", "compound"]),
                new("wine", "rukh-mauk-rug", PartOfSpeech: "noun", GrammarClass: "drink", Tags: ["fermented", "red", "compound"]),
                new("hospitality", "mokra-dak-thog", PartOfSpeech: "noun", GrammarClass: "society", Tags: ["welcome", "dwelling", "compound"]),
                new("fiend", "vark-morz-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["evil", "danger", "compound"]),
                new("sincere", "grak-tur-goru-goru", PartOfSpeech: "adjective", GrammarClass: "virtue", Tags: ["truth", "compound"]),
                new("courage", "yanki-thog-rukh-morz", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["bravery", "abstract", "compound"]),
                new("Dream World", "naut-thog-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["dream", "spirit", "fixed-phrase"]),
                new("tonight", "um-naut", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["this-night", "compound"]),
                new("wooden", "gruuluk", PartOfSpeech: "adjective", GrammarClass: "material", Tags: ["wood", "possessive-derived"]),
                new("bowl", "quum-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["food", "vessel", "compound"]),
                new("spoon", "quum-krub", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["food", "hand", "compound"]),
                new("aroma", "kaag-thog-hrowk-tuk", PartOfSpeech: "noun", GrammarClass: "sense", Tags: ["smell", "abstract", "compound"]),
                new("garlic", "kaag-quum-ti", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["strong-smell", "compound"]),
                new("loaf", "hek-quum-ti", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["bread", "whole", "compound"]),
                new("slab", "hek-quum-bant", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["bread", "slice", "compound"]),
                new("hope", "mauk-thruk-thog", PartOfSpeech: "noun", GrammarClass: "emotion", Tags: ["desire", "need", "compound"]),
                new("aspiration", "ti-mauk-thog", PartOfSpeech: "noun", GrammarClass: "emotion", Tags: ["ambition", "abstract", "compound"]),
                new("outside world", "dok-dak mur-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["outside", "world", "fixed-phrase"]),
                new("bottle", "rukh-bant-burz", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["drink", "vessel", "compound"]),
                new("formality", "bib-darguk-thog", PartOfSpeech: "noun", GrammarClass: "society", Tags: ["formal", "abstract", "compound"]),
                new("cordial", "mokra-grod-flit-flit", PartOfSpeech: "adjective", GrammarClass: "acceptance", Tags: ["friendly", "compound"]),
                new("foresight", "oglar-dakur-ti", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["future", "perception", "compound"]),
                new("fortune", "mauk-drav-thog", PartOfSpeech: "noun", GrammarClass: "fate", Tags: ["luck", "abstract", "compound"]),
                new("sir", "gash-darg", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["title", "warrior", "compound"]),
                new("knighthood", "zol-gash-darg-thog", PartOfSpeech: "noun", GrammarClass: "status", Tags: ["knight", "abstract", "compound"]),
                new("tournament", "gash-mauk-dak", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["contest", "martial", "compound"]),
                new("tax collection", "darg-drav-mokhin", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["tax", "collection", "fixed-phrase"]),
                new("theocratic", "mograth-darguk", PartOfSpeech: "adjective", GrammarClass: "religion", Tags: ["religious-rule", "possessive-derived"]),
                new("paladin", "mograth-gash", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "warrior", "compound"]),
                new("novitiate", "nurik-mograth", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "new", "compound"]),
                new("report", "narg-bib-thog", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["written", "account", "compound"]),
                new("youth", "nurik-mog-krag-mokh", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["young", "person", "compound"]),
                new("dark arts", "burzuk gurmogi", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["dark", "magic", "fixed-phrase"]),
                new("peer", "mokru-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["equal", "companion", "compound"]),
                new("witness", "oglar-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["sight", "person", "compound"]),
                new("training", "hekin-gash", PartOfSpeech: "noun", GrammarClass: "skill", Tags: ["learning", "martial", "compound"]),
                new("wariness", "vark-oglar-thog", PartOfSpeech: "noun", GrammarClass: "emotion", Tags: ["danger", "watchfulness", "compound"]),
                new("white witch", "kelnib-gurmog-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "white", "fixed-phrase"]),
                new("black witch", "burz-gurmog-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "black", "fixed-phrase"]),
                new("nature", "vril-dak-thog", PartOfSpeech: "noun", GrammarClass: "world", Tags: ["wild", "abstract", "compound"]),
                new("destruction", "brak-thog-ti", PartOfSpeech: "noun", GrammarClass: "harm", Tags: ["breaking", "intensified", "compound"]),
                new("holocaust", "brak-thog-ti-morz-dakur", PartOfSpeech: "noun", GrammarClass: "destruction", Tags: ["compound", "compound-reviewed", "destruction", "death", "wiki-fodder"]),
                new("domination", "dargu-thog-ti", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["control", "intensified", "compound"]),
                new("prosperity", "grod-drav-thog", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["good", "wealth", "compound"]),
                new("priesthood", "mograth-mokh", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["priest", "group", "compound"]),
                new("prelate", "mograth-darg-ti", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["religious", "ruler", "compound"]),
                new("good-aligned religion", "grod-mograth-thog", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["good", "belief", "fixed-phrase"]),
                new("sin", "morz-thog", PartOfSpeech: "noun", GrammarClass: "morality", Tags: ["bad", "abstract", "compound"]),
                new("mother", "mokh-um", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["family", "female", "compound"]),
                new("liege", "darg-ti-mog-zog-karn", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["ruler", "noble", "compound"]),
                new("honorably", "grak-tur-ti", PartOfSpeech: "adverb", GrammarClass: "virtue", Tags: ["honor", "truth", "compound"]),
                new("wicked", "morz-thoguk", PartOfSpeech: "adjective", GrammarClass: "morality", Tags: ["bad", "possessive-derived"]),
                new("hidebound", "mur-darg-nu", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["rigid", "negative", "compound"]),
                new("bleakness", "burz-thog-drak-kaag", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["gloom", "abstract", "compound"]),
                new("applecart", "quum-hrogar", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["food", "cart", "compound"]),
                new("stupor", "nul-thogin-gash-brak", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["unconscious", "compound"]),
                new("righteous", "grod-darguk", PartOfSpeech: "adjective", GrammarClass: "morality", Tags: ["good", "law", "compound"]),
                new("innocent", "nul-brak-thoguk", PartOfSpeech: "adjective", GrammarClass: "morality", Tags: ["not-guilty", "unharmed", "compound"]),
                new("wakefulness", "oglar-dakur-thog", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["awake", "abstract", "compound"]),
                new("harvest", "quum-hek-drav", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["farming", "yield", "compound"]),
                new("bootsteps", "kruk-khali-lag-biti", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["boots", "steps", "compound"]),
                new("coal", "burz-rukh-zol", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["black", "fire", "compound"]),
                new("cot", "dakku-bant-thrum", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["bed", "small", "compound"]),
                new("oak", "gruul-yank-ti", PartOfSpeech: "noun", GrammarClass: "plant", Tags: ["wood", "hard", "compound"]),
                new("canvas", "khal-thog-ti", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["cloth", "heavy", "compound"]),
                new("cord", "bant-thrum", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["rope", "small", "compound"]),
                new("ward", "gor-narg", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["protection", "spoken", "compound"]),
                new("alarum", "gor-narg-rukh", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["warning", "sound", "compound"]),
                new("lineage", "mokh-lag", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["family", "path", "compound"]),
                new("assignment", "darg-hek", PartOfSpeech: "noun", GrammarClass: "duty", Tags: ["ordered", "task", "compound"]),
                new("pensive", "thogin-grot", PartOfSpeech: "adjective", GrammarClass: "thought", Tags: ["thinking", "delayed", "compound"]),
                new("pet", "mokra-mog", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["friend", "companion", "compound"]),
                new("jar", "oglar-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["clear", "vessel", "compound"]),
                new("robe", "khal-ti", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["garb", "large", "compound"]),
                new("revelation", "oglar-thog-ti", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["revealed", "knowledge", "compound"]),
                new("spell", "gur-narg", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["magic", "speech", "compound"]),
                new("phonetics", "narg-rukh-thog", PartOfSpeech: "noun", GrammarClass: "language", Tags: ["speech", "sound", "compound"]),
                new("arcane", "gurmoguk", PartOfSpeech: "adjective", GrammarClass: "magic", Tags: ["magic", "possessive-derived"]),
                new("roof", "hek-khal", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["built", "covering", "compound"]),
                new("overhead", "mog-ti-dak-uk", PartOfSpeech: "adverb", GrammarClass: "location", Tags: ["above", "compound"]),
                new("insect", "thrum-flit", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["small", "flying", "compound"]),
                new("spellbook", "gur-bib", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["magic", "book", "compound"]),
                new("biscuit", "hek-quum-bit", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["baked", "small", "compound"]),
                new("gravy", "rukh-quum-thuk", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["liquid", "thick", "compound"]),
                new("sausage", "quum-vrak-bant", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["meat", "bound", "compound"]),
                new("plate", "quum-bant-thrum", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["food", "flat", "compound"]),
                new("blueberry", "burz-oglar-quum", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["blue", "berry", "compound"]),
                new("cider", "surg-quum-rukh", PartOfSpeech: "noun", GrammarClass: "drink", Tags: ["fruit", "liquid", "compound"]),
                new("bounty", "mur-drav", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["abundance", "gift", "compound"]),
                new("mouse", "thrum-kaag-mog", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["small", "scent", "compound"]),
                new("quest", "thruk-lag", PartOfSpeech: "noun", GrammarClass: "purpose", Tags: ["need", "path", "compound"]),
                new("property", "darg-dakuk", PartOfSpeech: "noun", GrammarClass: "possession", Tags: ["held-place", "possessive-derived"]),
                new("bandit", "drav-vark-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["theft", "danger", "compound"]),
                new("ranger", "vril-gor-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["wilds", "watcher", "compound"]),
                new("plantation", "quum-hek-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["farming", "plural-place", "compound"]),
                new("mining camp", "hekfa-mokh-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["miners", "camp", "fixed-phrase"]),
                new("captured", "dargash-vark", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["past", "seized", "compound"]),
                new("spirit-walk", "grod-thog-lag", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["spirit", "path", "compound"]),
                new("homing enchantment", "dakku-lag gur-narg", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["home", "spell", "fixed-phrase"]),
                new("timeframe", "dakur-mokh", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["time", "group", "compound"]),
                new("fate", "darg-dakur", PartOfSpeech: "noun", GrammarClass: "fate", Tags: ["ruled-time", "compound"]),
                new("tragic", "morz-thog-ti", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["bad", "intensified", "compound"]),
                new("captor", "darg-vark-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["seizer", "danger", "compound"]),
                new("count", "darg-mog-ti", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["title", "ruler", "compound"]),
                new("lordship", "darg-thog-ti", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["rule", "intensified", "compound"]),
                new("letter of introduction", "mokra-narg-bib", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["friendly", "written", "fixed-phrase"]),
                new("component", "hek-bit", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["part", "making", "compound"]),
                new("amethyst", "mauk-oglar-zol", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["gem", "purple", "compound"]),
                new("rare herb", "varg-thrum-quum", PartOfSpeech: "noun", GrammarClass: "plant", Tags: ["rare", "herb", "fixed-phrase"]),
                new("sprig", "gruul-krub-bit", PartOfSpeech: "noun", GrammarClass: "plant", Tags: ["branch", "small", "compound"]),
                new("slain orc", "brakash orukh", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["dead", "orc", "fixed-phrase"]),
                new("resolve", "darg-thog-grod", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["determination", "compound"]),
                new("eagerness", "lag-mauk-thog", PartOfSpeech: "noun", GrammarClass: "emotion", Tags: ["desire", "motion", "compound"]),
                new("miracle", "mograth-mauk-thog", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["divine", "wonder", "compound"]),
                new("cinch", "darg-bant", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["tighten", "strap", "compound"]),
                new("bag", "hrowk-khal-thrum", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["carrying", "cloth", "compound"]),
                new("gallon", "rukh-bant-mur", PartOfSpeech: "noun", GrammarClass: "measure", Tags: ["liquid", "large", "compound"]),
                new("honey", "rukh-mauk-quum", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["sweet", "liquid", "compound"]),
                new("corked jug", "gruul-darg rukh-bant-ti", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["sealed", "vessel", "fixed-phrase"]),
                new("furlong", "mur-lag-bit", PartOfSpeech: "noun", GrammarClass: "measure", Tags: ["distance", "long", "compound"]),
                new("overnight", "naut-mur-dakur", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["night", "duration", "compound"]),
                new("guide", "lag-oglar-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["path", "sight", "compound"]),
                new("druid", "vril-mograth", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["wilds", "priest", "compound"]),
                new("blister", "morz-vrak-bit", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["sore", "skin", "compound"]),
                new("purse", "drav-zol-khal", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["money", "container", "compound"]),
                new("prospect", "mauk-dak", PartOfSpeech: "noun", GrammarClass: "possibility", Tags: ["possible", "place", "compound"]),
                new("gratitude", "drav-thog-grod", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["thanks", "gift", "compound"])
            ]);

            entries.AddRange([
                new("orcspawn", "orukh-vark-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["orc", "danger", "plural", "compound"]),
                new("quarry", "hek-zol-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["stone", "work", "compound"]),
                new("pit", "burz-dak-ti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["deep", "hole", "compound"]),
                new("Blightstone Pit", "Morz-zol Burz-dak-ti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["proper-noun", "quarry", "fixed-phrase"]),
                new("caravan", "hrogar-mokh", PartOfSpeech: "noun", GrammarClass: "transport", Tags: ["wagon", "group", "compound"]),
                new("caravans", "hrogar-mokhi", PartOfSpeech: "noun", GrammarClass: "transport", Tags: ["transport", "wagon", "group", "plural", "compound"]),
                new("wagon train", "hrogar-mokh-lag", PartOfSpeech: "noun", GrammarClass: "transport", Tags: ["wagon", "route", "fixed-phrase"]),
                new("mule", "hrog-mog", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["pack-animal", "compound"]),
                new("mules", "hrog-mogi", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["pack-animal", "plural", "compound"]),
                new("guard", "gor-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["protector", "compound"]),
                new("guards", "gor-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["protector", "plural", "compound"]),
                new("woodsman", "vril-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["woods", "compound"]),
                new("woodsmen", "vril-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["woods", "plural", "compound"]),
                new("ranger's", "vril-gor-moguk", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["ranger", "possessive", "compound"]),
                new("rangers", "vril-gor-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["wilds", "watcher", "plural", "compound"]),
                new("stream", "dak-rukh-lag", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["water", "path", "compound"]),
                new("rendezvous point", "mokru-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["meeting", "fixed-phrase"]),
                new("reinforcements", "agh-gash-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["additional", "fighters", "plural", "compound"]),
                new("horn call", "narg-rukh-ti", PartOfSpeech: "noun", GrammarClass: "sound", Tags: ["signal", "loud", "compound"]),
                new("wood-wise", "vril-thoguk", PartOfSpeech: "adjective", GrammarClass: "skill", Tags: ["woods", "knowledge", "compound"]),
                new("critter", "thrum-mog", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["small", "creature", "compound"]),
                new("critters", "thrum-mogi", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["small", "creature", "plural", "compound"]),
                new("sense", "kaag-oglar-thog", PartOfSpeech: "noun", GrammarClass: "perception", Tags: ["smell", "sight", "compound"]),
                new("senses", "kaag-oglar-thogi", PartOfSpeech: "noun", GrammarClass: "perception", Tags: ["smell", "sight", "plural", "compound"]),
                new("scant gain", "thrum-drav", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["small", "gain", "fixed-phrase"]),
                new("urgency", "dakur-thruk", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["need", "time", "compound"]),
                new("swiftest", "lag-grak-ti", PartOfSpeech: "adjective", GrammarClass: "speed", Tags: ["fast", "superlative", "compound"]),
                new("unseen", "noglar-darg-bant", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["hidden"]),
                new("lope", "lagu-thrum", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["easy-run", "compound"]),
                new("arrow", "flit-zol", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["missile", "weapon", "compound"]),
                new("arrows", "flit-zoli", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["missile", "weapon", "plural", "compound"]),
                new("bolt", "crossbow-flit-zol", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["crossbow", "missile", "compound"]),
                new("bolts", "crossbow-flit-zoli", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["crossbow", "missile", "plural", "compound"]),
                new("crossbow", "dug-bant-zol", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "bow", "compound"]),
                new("crossbowman", "dug-bant-zol-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["weapon", "fighter", "compound"]),
                new("longbow", "bant-zol-ti", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "bow", "large", "compound"]),
                new("dexterity", "krub-lag-thog", PartOfSpeech: "noun", GrammarClass: "skill", Tags: ["agility", "abstract", "compound"]),
                new("chase", "vark-lag", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["pursuit", "compound"]),
                new("combat range", "gash-dak-mokh", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["fighting", "distance", "fixed-phrase"]),
                new("stratagem", "gash-thog-var", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["battle", "plan", "compound"]),
                new("bonus", "agh-drav", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["additional", "gain", "compound"]),
                new("backtrail", "dok-lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["back", "trail", "compound"]),
                new("orcish", "orukhuk", PartOfSpeech: "adjective", GrammarClass: "species", Tags: ["orc", "possessive-derived"]),
                new("treeline", "gruul-lag", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["forest", "edge", "compound"]),
                new("snarling tide", "narg-vark rukh-lag", PartOfSpeech: "noun", GrammarClass: "danger", Tags: ["sound", "flood", "fixed-phrase"]),
                new("desperation", "nul-mauk-thog", PartOfSpeech: "noun", GrammarClass: "emotion", Tags: ["hopeless", "abstract", "compound"]),
                new("grim", "morz-oglar", PartOfSpeech: "adjective", GrammarClass: "emotion", Tags: ["dark", "look", "compound"]),
                new("spoils", "drav-hrowk", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["loot", "carried", "compound"]),
                new("brutal", "brak-grod-nu", PartOfSpeech: "adjective", GrammarClass: "violence", Tags: ["breaking", "not-good", "compound"]),
                new("violence", "brak-grod-nu-hek-ti", PartOfSpeech: "noun", GrammarClass: "action", Tags: ["compound", "compound-reviewed", "brutal", "action", "wiki-fodder"]),
                new("heartbeat", "grod-burz-bit", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["heart", "small", "compound"]),
                new("instinct", "vrak-thog", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["body", "thought", "compound"]),
                new("fighting chance", "gash-mauk-thog", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["fight", "possibility", "fixed-phrase"]),
                new("sheer rock face", "grak-zol-mogum", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["rock", "face", "fixed-phrase"]),
                new("choke point", "thrum-lag-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["narrow", "route", "fixed-phrase"]),
                new("illusionary rock slide", "noglar-zol lag-dak", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["illusion", "rockslide", "fixed-phrase"]),
                new("rockslide", "zol-lag-dak", PartOfSpeech: "noun", GrammarClass: "hazard", Tags: ["rock", "fall", "compound"]),
                new("weak minded", "thrum-thoguk", PartOfSpeech: "adjective", GrammarClass: "thought", Tags: ["weak", "mind", "compound"]),
                new("unconscious", "nul-thogin-morz-doku", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["unconscious"]),
                new("shatters", "brakur-ti", PartOfSpeech: "verb", GrammarClass: "harm", Tags: ["present", "break", "intensified"]),
                new("stone", "zol-dak", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["rock", "compound"]),
                new("stones", "zol-daki", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["rock", "plural", "compound"]),
                new("landslide", "dak-zol-lag", PartOfSpeech: "noun", GrammarClass: "hazard", Tags: ["earth", "rock", "compound"]),
                new("dust", "dak-thrum", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["earth", "small", "compound"]),
                new("sapling", "nurik-gruul", PartOfSpeech: "noun", GrammarClass: "plant", Tags: ["young", "tree", "compound"]),
                new("jeering", "morz-nargin", PartOfSpeech: "noun", GrammarClass: "speech", Tags: ["mocking", "compound"]),
                new("alarm", "vark-narg-rukh", PartOfSpeech: "noun", GrammarClass: "sound", Tags: ["danger", "warning", "compound"]),
                new("silenced", "nul-nargash", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["past", "negative", "compound"]),
                new("eighth", "gakh-bit", PartOfSpeech: "numeral", GrammarClass: "fraction", Tags: ["fraction", "compound"]),
                new("pursue", "vark-lagu", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["chase", "infinitive", "compound"]),
                new("fellows", "mokru-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["companions", "plural", "compound"]),
                new("loot", "drav-varku", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["steal", "infinitive", "compound"]),
                new("bastard", "morz-margi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["insult", "singular", "compound"]),
                new("bastards", "morz-margith", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["insult", "plural", "compound"]),
                new("rear", "dok-lag-dak", PartOfSpeech: "noun", GrammarClass: "position", Tags: ["back", "place", "compound"]),
                new("halter", "mog-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["animal", "rope", "compound"]),
                new("caster", "gur-narg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "speaker", "compound"]),
                new("illusion", "noglar-thog", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["hidden", "thought", "compound"]),
                new("horde", "mur-vark-mokh", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["many", "danger", "compound"]),
                new("sleeping", "dakkin-naut", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["sleep", "night", "compound"]),
                new("surviving", "dakur-thogin", PartOfSpeech: "adjective", GrammarClass: "life", Tags: ["living", "progressive", "compound"]),
                new("panicked", "vark-thogash", PartOfSpeech: "adjective", GrammarClass: "emotion", Tags: ["fear", "past", "compound"]),
                new("pursuit", "vark-lag-thog", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["chase", "abstract", "compound"]),
                new("elevation", "ti-dak-thog", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["height", "abstract", "compound"]),
                new("drow", "drow", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["exonym", "species"]),
                new("played-out quarry", "nul-hek-zol-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["exhausted", "quarry", "fixed-phrase"]),
                new("bend", "lag-mokru", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["turn", "compound"]),
                new("pell-mell", "lag-grotin-ti", PartOfSpeech: "adverb", GrammarClass: "motion", Tags: ["chaotic", "compound"]),
                new("bursting", "dok-varkin-grak", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["emerging", "sudden", "compound"]),
                new("snatched", "dravash-grak", PartOfSpeech: "verb", GrammarClass: "taking", Tags: ["past", "sudden", "compound"]),
                new("sling", "bant-zol-bit", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "small", "compound"]),
                new("shot", "flit-zol-narg", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["missile", "attack", "compound"]),
                new("shots", "flit-zol-nargi", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["missile", "attack", "plural", "compound"]),
                new("skittering", "lagin-grot", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["awkward", "progressive", "compound"]),
                new("harmlessly", "nul-brakkin", PartOfSpeech: "adverb", GrammarClass: "harm", Tags: ["without-harm", "compound"]),
                new("aim", "oglar-gash-thog", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["sight", "attack", "compound"]),
                new("aim", "oglar-gash", PartOfSpeech: "verb", GrammarClass: "combat", Tags: ["sight", "attack", "compound", "root-repaired", "shortened", "base-aim", "derive-past", "derive-progressive"]),
                new("ragged", "brak-khaluk", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["torn", "possessive-derived"]),
                new("bloodied", "pukh-vrakuk", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["blood", "skin", "compound"]),
                new("outnumbered", "mur-nu-mokh", PartOfSpeech: "adjective", GrammarClass: "quantity", Tags: ["outnumbered", "compound"]),
                new("battered", "brakash-thrum", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["beaten", "compound"]),
                new("bleeding", "pukh-rukhin", PartOfSpeech: "verb", GrammarClass: "body", Tags: ["blood", "progressive", "compound"]),
                new("axe", "brak-zol", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["weapon", "cutting", "compound"]),
                new("fiery line", "rukh-tur-lag", PartOfSpeech: "noun", GrammarClass: "fire", Tags: ["fire", "fixed-phrase"]),
                new("roundabout", "lag-nak-mur", PartOfSpeech: "adjective", GrammarClass: "motion", Tags: ["indirect", "compound"]),
                new("forcefully", "darg-nargin", PartOfSpeech: "adverb", GrammarClass: "speech", Tags: ["commanding", "compound"]),
                new("rear-attack", "dok-gash-narg", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["rear", "attack", "compound"]),
                new("hamstringing", "brak-kruzin", PartOfSpeech: "verb", GrammarClass: "harm", Tags: ["leg", "progressive", "compound"]),
                new("throat", "narg-burz", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["voice", "interior", "compound"]),
                new("rapid approach", "grak-lag-nak", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["fast", "near", "fixed-phrase"]),
                new("plate-sized", "quum-bant-thrumuk-mur", PartOfSpeech: "adjective", GrammarClass: "size", Tags: ["plate", "sized", "compound"]),
                new("vertically-slitted", "ti-brak-oglar", PartOfSpeech: "adjective", GrammarClass: "appearance", Tags: ["eye", "slit", "compound"]),
                new("surveying", "oglarin-mur", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["looking", "broad", "compound"]),
                new("tentacle", "krub-bant-vark", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["limb", "monster", "compound"]),
                new("tentacles", "krub-bant-varki", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["limb", "monster", "plural", "compound"]),
                new("antennae", "kaag-banti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["sensing", "plural", "compound"]),
                new("bite", "kruk-gash", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["tooth", "attack", "compound"]),
                new("filthy", "morz-vrakuk", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["dirty", "skin", "compound"]),
                new("leader", "darg-gash-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["commander", "martial", "compound"]),
                new("blockade", "gor-hek-lag", PartOfSpeech: "noun", GrammarClass: "route", Tags: ["blocked", "defense", "compound"]),
                new("dodge", "vark-lag-thrum", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["avoid", "infinitive", "compound"]),
                new("looms", "nak-oglarur", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["near", "seen", "present"]),
                new("fighting withdrawal", "gash-dok-lag", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["retreat", "fighting", "fixed-phrase"]),
                new("spit", "narg-rukh-thrum", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["mouth", "small", "compound"]),
                new("pinch of sand", "krub-bit dak-thrum", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["sand", "small", "fixed-phrase"]),
                new("arcane gesture", "gurmoguk bant-narg", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["gesture", "fixed-phrase"]),
                new("rainbows", "rug-oglar-banti", PartOfSpeech: "noun", GrammarClass: "light", Tags: ["color", "plural", "compound"]),
                new("palms", "krub-burzi", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["hand", "plural", "compound"]),
                new("magical power", "gurmog darg-gash", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["power", "fixed-phrase"]),
                new("Color Spray", "rug-oglar rukh", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["spell", "proper-noun", "fixed-phrase"]),
                new("flank", "gash-nak", PartOfSpeech: "noun", GrammarClass: "position", Tags: ["side", "combat", "compound"]),
                new("exploit", "vark-bruku", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["use", "danger", "compound"]),
                new("strain", "grot-thog-vrak", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["stress", "body", "compound"]),
                new("betrayed", "mokra-nu-hekash", PartOfSpeech: "verb", GrammarClass: "trust", Tags: ["past", "betrayal", "compound"]),
                new("reset", "ut-dakku", PartOfSpeech: "verb", GrammarClass: "position", Tags: ["return", "set", "compound"]),
                new("squared shoulders", "gash-banti gor-dargash", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["shoulders", "braced", "fixed-phrase"]),
                new("battlefield", "gash-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["battle", "compound"]),
                new("atop", "ti-dak-uk", PartOfSpeech: "preposition", GrammarClass: "relation", Tags: ["above", "compound"]),
                new("cliff", "ti-zol-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["high", "stone", "compound"]),
                new("partial cover", "thrum-gor-dak", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["cover", "partial", "fixed-phrase"]),
                new("bluff", "ti-dak-nak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["high", "edge", "compound"]),
                new("headshot", "mog-ti-gash", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["head", "attack", "compound"]),
                new("immense", "mur-vrak-ti", PartOfSpeech: "adjective", GrammarClass: "size", Tags: ["large", "body", "compound"]),
                new("commanding tone", "darg-narg-rukh", PartOfSpeech: "noun", GrammarClass: "speech", Tags: ["command", "tone", "fixed-phrase"]),
                new("mentally", "thogin-burz", PartOfSpeech: "adverb", GrammarClass: "thought", Tags: ["inside-mind", "compound"]),
                new("huge", "mur-vrak-ti-bibi-grum", PartOfSpeech: "adjective", GrammarClass: "size", Tags: ["large", "body", "compound"]),
                new("relocate", "ut-dakku-lag", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["move-again", "compound"]),
                new("allowance", "vargash-dak", PartOfSpeech: "noun", GrammarClass: "permission", Tags: ["allowed", "amount", "compound"]),
                new("massive", "mur-vrak-mur", PartOfSpeech: "adjective", GrammarClass: "size", Tags: ["large", "body", "compound"]),
                new("retreat", "dok-lagu", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["move-back", "compound"]),
                new("Deep Friends", "Burz Mokrath", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "monster-cult", "fixed-phrase"]),
                new("floor", "dak-burz-thrum", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["interior", "low", "compound"]),
                new("flight", "vark-lag-grak", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["escape", "speed", "compound"]),
                new("outrun", "lagu-ti", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["run-faster", "compound"]),
                new("fumble", "lag-grot-thog", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["awkward", "mistake", "compound"]),
                new("scrambles", "lagur-grot", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["awkward", "present", "compound"]),
                new("fleeing", "varkin-dok", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["escape", "progressive", "compound"]),
                new("horror", "vark-thog-mog", PartOfSpeech: "noun", GrammarClass: "danger", Tags: ["fear", "creature", "compound"]),
                new("iron grip", "zol-darg-krub", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["iron", "grip", "fixed-phrase"]),
                new("wagon frame", "hrogar-hek-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["wagon", "frame", "fixed-phrase"])
            ]);

            entries.AddRange([
                new("Toothbreakers", "Kruk-brakari", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["proper-noun", "orc", "clan"]),
                new("clan", "mokh-vrak", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["group", "blood", "compound"]),
                new("dwell", "dakku-doku-krag", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["infinitive", "reside"]),
                new("northwest", "surg-dok-naut", PartOfSpeech: "noun", GrammarClass: "direction", Tags: ["north", "west", "compound"]),
                new("Red Tusk", "Rug Kruk-zol", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "title", "fixed-phrase"]),
                new("core", "murk-burz", PartOfSpeech: "noun", GrammarClass: "position", Tags: ["center", "interior", "compound"]),
                new("vicious", "vark-grod-nu", PartOfSpeech: "adjective", GrammarClass: "danger", Tags: ["danger", "not-good", "compound"]),
                new("thieves' guild", "drav-vark-mokh", PartOfSpeech: "noun", GrammarClass: "organization", Tags: ["theft", "group", "fixed-phrase"]),
                new("lookout", "gor-oglar-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["watch", "sight", "compound"]),
                new("ambush", "noglar-gash", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["hidden", "attack", "compound"]),
                new("political", "darg-thoguk", PartOfSpeech: "adjective", GrammarClass: "authority", Tags: ["authority", "possessive-derived"]),
                new("absence", "nul-dak-thog", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["not-present", "compound"]),
                new("scrutiny", "mur-oglar-thog", PartOfSpeech: "noun", GrammarClass: "perception", Tags: ["close-looking", "compound"]),
                new("trophy", "gash-drav", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["combat", "prize", "compound"]),
                new("colluding", "mokrin-vark", PartOfSpeech: "verb", GrammarClass: "association", Tags: ["danger", "progressive", "compound"]),
                new("Elves", "elfi", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["exonym", "plural"]),
                new("surface ones", "oglar-dak mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["surface", "plural", "fixed-phrase"]),
                new("birdcall", "flit-narg-rukh", PartOfSpeech: "noun", GrammarClass: "sound", Tags: ["bird", "call", "compound"]),
                new("thicket finch", "vril-thrum flit", PartOfSpeech: "noun", GrammarClass: "animal", Tags: ["bird", "thicket", "fixed-phrase"]),
                new("Fentrest", "Fentrest", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["proper-noun", "exonym"]),
                new("glade", "gruul-oglar-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["forest", "open", "compound"]),
                new("witch-hunter", "gurmog-vark-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["witch", "hunter", "compound"]),
                new("scrying", "gurmog-oglarin", PartOfSpeech: "verb", GrammarClass: "magic", Tags: ["sight", "progressive", "compound"]),
                new("bowstring", "bant-zol-bant", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["bow", "string", "compound"]),
                new("sunset", "surg-dok", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["sun", "west", "compound"]),
                new("willingness", "varg-thog-grod", PartOfSpeech: "noun", GrammarClass: "resolve", Tags: ["choice", "good", "compound"]),
                new("afternoon", "dakur-surg-dok", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["day", "later", "compound"]),
                new("pressing on", "lagin-ti", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["continuing", "progressive", "compound"]),
                new("camp", "dakku-thrum", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["temporary", "dwelling", "compound"]),
                new("magicked", "gurmogash", PartOfSpeech: "verb", GrammarClass: "magic", Tags: ["past", "affected"]),
                new("intervening miles", "murk-lag-biti", PartOfSpeech: "noun", GrammarClass: "measure", Tags: ["between", "distance", "fixed-phrase"]),
                new("author", "bib-hek-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["writer", "compound"]),
                new("authors", "bib-hek-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["writer", "plural", "compound"]),
                new("captive", "darg-varkum", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["taken", "prisoner", "compound"]),
                new("captives", "darg-varkumi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["taken", "prisoner", "plural", "compound"]),
                new("brokenly", "brak-nargin", PartOfSpeech: "adverb", GrammarClass: "speech", Tags: ["broken", "compound"]),
                new("gruffly", "grukh-nargin", PartOfSpeech: "adverb", GrammarClass: "speech", Tags: ["rough", "compound"]),
                new("script", "bib-narg", PartOfSpeech: "noun", GrammarClass: "language", Tags: ["writing", "compound"]),
                new("scrawl", "bib-narg-grot", PartOfSpeech: "noun", GrammarClass: "language", Tags: ["rough-writing", "compound"]),
                new("scrutinized", "mur-oglash", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["past", "close-looking", "compound"]),
                new("signs", "narg-oglari", PartOfSpeech: "noun", GrammarClass: "evidence", Tags: ["indications", "plural", "compound"]),
                new("discerned", "oglash-thog", PartOfSpeech: "verb", GrammarClass: "thought", Tags: ["past", "understood", "compound"]),
                new("variation", "agh-var-thog", PartOfSpeech: "noun", GrammarClass: "difference", Tags: ["different", "abstract", "compound"]),
                new("writing implement", "bib-hek-zol", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["writing", "tool", "fixed-phrase"]),
                new("ink", "burz-rukh", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["black", "liquid", "compound"]),
                new("symbol", "narg-var-bib", PartOfSpeech: "noun", GrammarClass: "symbol", Tags: ["mark", "written", "compound"]),
                new("symbols", "narg-var-bibi", PartOfSpeech: "noun", GrammarClass: "symbol", Tags: ["mark", "written", "plural", "compound"]),
                new("composed", "hekash-mokru", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["past", "assembled", "compound"]),
                new("disappearance", "nul-oglar-thog", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["missing", "unseen", "compound"]),
                new("perilous", "vark-thog-tiuk", PartOfSpeech: "adjective", GrammarClass: "danger", Tags: ["peril", "possessive-derived"]),
                new("wounded", "brakash-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["injured", "plural", "compound"]),
                new("underway", "lagin", PartOfSpeech: "adverb", GrammarClass: "motion", Tags: ["moving", "progressive"]),
                new("ache", "morz-vrak-thog", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["pain", "body", "compound"]),
                new("treasure", "drav-zol-ti", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["wealth", "great", "compound"]),
                new("lair", "vark-dakku-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["monster", "dwelling", "compound"]),
                new("laired", "vark-dakash", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["past", "monster", "compound"]),
                new("terrorized", "vark-thogash-ti", PartOfSpeech: "verb", GrammarClass: "fear", Tags: ["past", "intensified", "compound"]),
                new("victim", "brak-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["harmed", "compound"]),
                new("victims", "brak-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["harmed", "plural", "compound"]),
                new("gold mine", "zol-ti-hekfa-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["gold", "mine", "fixed-phrase"]),
                new("waning", "thrumin-dakur", PartOfSpeech: "verb", GrammarClass: "time", Tags: ["diminishing", "progressive", "compound"]),
                new("daylight", "surg-oglar", PartOfSpeech: "noun", GrammarClass: "light", Tags: ["sun", "light", "compound"]),
                new("clearing", "gruul-oglar-dak-bant-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["forest", "open", "compound"]),
                new("copse", "gruul-mokh-thrum", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["trees", "small-group", "compound"]),
                new("picketed", "bant-dargash", PartOfSpeech: "verb", GrammarClass: "position", Tags: ["tied", "past", "compound"]),
                new("windbreak", "hush-gor-hek", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["air", "protection", "compound"]),
                new("campfire", "dakku-thrum-rukh-ash", PartOfSpeech: "noun", GrammarClass: "fire", Tags: ["camp", "fire", "compound"]),
                new("pain", "morz-vrak-thog-ti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["hurt", "intensified", "compound"]),
                new("sewn shut", "bantin-dargash", PartOfSpeech: "verb", GrammarClass: "healing", Tags: ["closed", "past", "fixed-phrase"]),
                new("honeyed", "rukh-mauk-quumuk", PartOfSpeech: "adjective", GrammarClass: "food", Tags: ["honey", "possessive-derived"]),
                new("throbbed", "morz-rukhur", PartOfSpeech: "verb", GrammarClass: "body", Tags: ["pain", "past", "compound"]),
                new("clue", "oglar-thog-bit", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["evidence", "small", "compound"]),
                new("clues", "oglar-thog-biti", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["evidence", "plural", "compound"]),
                new("rescue", "ut-varku", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["save", "infinitive", "compound"]),
                new("survive", "dakur-thogu", PartOfSpeech: "verb", GrammarClass: "life", Tags: ["live-through", "infinitive", "compound"]),
                new("survivor", "dakur-thogu-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["compound", "compound-reviewed", "survive", "person", "wiki-fodder"]),
                new("bruise", "morz-vrak-burz", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["injury", "dark", "compound"]),
                new("ribs", "grod-burz-banti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["chest", "plural", "compound"]),
                new("exhaled", "hush-dokash", PartOfSpeech: "verb", GrammarClass: "body", Tags: ["breath", "past", "compound"]),
                new("scrap", "thrum-bit", PartOfSpeech: "noun", GrammarClass: "quantity", Tags: ["small-piece", "compound"]),
                new("earthenware", "dak-rukh-bantuk", PartOfSpeech: "adjective", GrammarClass: "material", Tags: ["clay", "vessel", "compound"]),
                new("two-thirds", "dug-uk-dug-agh-ash", PartOfSpeech: "numeral", GrammarClass: "fraction", Tags: ["fraction", "fixed-phrase"]),
                new("authorship", "bib-hek-thog", PartOfSpeech: "noun", GrammarClass: "language", Tags: ["writing", "origin", "compound"]),
                new("faction", "mokh-darg", PartOfSpeech: "noun", GrammarClass: "organization", Tags: ["group", "power", "compound"]),
                new("leveraging", "brukin-darg", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["using", "power", "compound"]),
                new("muscle", "yank-vrak", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["strength", "body", "compound"]),
                new("fodder", "quum-vark", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["expendable", "food", "compound"]),
                new("Basilisk's Claw", "Basiliskuk Kruk", PartOfSpeech: "noun", GrammarClass: "organization", Tags: ["proper-noun", "exonym", "fixed-phrase"]),
                new("Cult of the Dragon", "Dragonuk Mograth-mokh", PartOfSpeech: "noun", GrammarClass: "organization", Tags: ["proper-noun", "cult", "fixed-phrase"]),
                new("Zhentarim", "Zhentarim", PartOfSpeech: "noun", GrammarClass: "organization", Tags: ["proper-noun", "exonym"]),
                new("flind", "flind", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["exonym", "species"]),
                new("flinds", "flindi", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["exonym", "species", "plural"]),
                new("hobgoblin", "hobgoblin", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["exonym", "species"]),
                new("hobgoblins", "hobgoblini", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["exonym", "species", "plural"]),
                new("ogre", "ogre", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["exonym", "species"]),
                new("ogres", "ogri", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["exonym", "species", "plural"]),
                new("machination", "noglar-darg-thog", PartOfSpeech: "noun", GrammarClass: "scheme", Tags: ["hidden", "power", "compound"]),
                new("machinations", "noglar-darg-thogi", PartOfSpeech: "noun", GrammarClass: "scheme", Tags: ["hidden", "power", "plural", "compound"]),
                new("coven", "gurmog-mokh", PartOfSpeech: "noun", GrammarClass: "organization", Tags: ["witch", "group", "compound"]),
                new("vibration", "rukh-bant-thog", PartOfSpeech: "noun", GrammarClass: "sense", Tags: ["tremor", "abstract", "compound"]),
                new("vibrations", "rukh-bant-thogi", PartOfSpeech: "noun", GrammarClass: "sense", Tags: ["tremor", "plural", "compound"]),
                new("web", "bant-vril", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["net", "compound"]),
                new("surveil", "noglar-oglar", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["watch-secretly", "compound"]),
                new("surveiling", "noglar-oglarin", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["watch-secretly", "progressive", "compound"]),
                new("Duchy", "darg-ti-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["political", "proper-noun", "compound"]),
                new("dawn", "surg-ash", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["sun", "first", "compound"]),
                new("farmstead", "quum-hek-dak-thrum", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["farm", "dwelling", "compound"]),
                new("farmsteads", "quum-hek-dak-thrumi", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["farm", "dwelling", "plural", "compound"]),
                new("outlie", "dok-dakku", PartOfSpeech: "verb", GrammarClass: "location", Tags: ["outside", "settled", "compound"]),
                new("creek crossing", "dak-rukh-lag mokru", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["water", "crossing", "fixed-phrase"]),
                new("field", "quum-hek-mokh", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["farm", "area", "compound"]),
                new("militiaman", "mokh-gor-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["community", "guard", "compound"]),
                new("militiamen", "mokh-gor-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["community", "guard", "plural", "compound"]),
                new("mountain pass", "ti-dak-lag", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["mountain", "route", "fixed-phrase"]),
                new("lunch", "murk-dakur-quum", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["midday", "meal", "compound"]),
                new("dusty", "dak-thrumuk", PartOfSpeech: "adjective", GrammarClass: "material", Tags: ["dust", "possessive-derived"]),
                new("tired", "grot-vrakuk", PartOfSpeech: "adjective", GrammarClass: "condition", Tags: ["weary", "body", "compound"]),
                new("hot meal", "rukh-grod-quum", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["hot", "meal", "fixed-phrase"]),
                new("treats", "dravur-grod", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["hosts", "present", "compound"]),
                new("goods", "hrowk-dravi", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["cargo", "plural", "compound"]),
                new("exchange of goods", "hrowk-dravi drav-thog", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["cargo", "commerce", "exchange", "fixed-phrase"]),
                new("cargo", "hrowk-drav", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["cargo", "carried-goods", "compound"]),
                new("of import", "thrak-grak", PartOfSpeech: "adjective", GrammarClass: "importance", Tags: ["important", "fixed-phrase"]),
                new("rate of pay", "quum-drav zorn-dakur", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["payment", "rate", "fixed-phrase"]),
                new("shares", "drav-biti", PartOfSpeech: "noun", GrammarClass: "trade", Tags: ["ownership", "portion", "plural", "compound"]),
                new("hearty meal", "grod-quum-ti", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["large", "meal", "fixed-phrase"]),
                new("eating establishment", "quum-dak-ti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["food", "business", "fixed-phrase"]),
                new("blood, sweat, and tears", "pukh hush-rukh agh oglar-rukh", PartOfSpeech: "noun", GrammarClass: "labor", Tags: ["effort", "fixed-phrase"]),
                new("sweat", "hush-rukh", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["labor", "liquid", "compound"]),
                new("tears", "oglar-rukh", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["eyes", "liquid", "compound"]),
                new("eventful", "hek-varuk", PartOfSpeech: "adjective", GrammarClass: "activity", Tags: ["events", "possessive-derived"]),
                new("trouble", "grot-var", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["difficulty", "compound"]),
                new("troubles", "grot-vari", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["difficulty", "plural", "compound"]),
                new("future trip", "dakur-ti lag", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["future", "travel", "fixed-phrase"]),
                new("future trips", "dakur-ti lagi", PartOfSpeech: "noun", GrammarClass: "motion", Tags: ["future", "travel", "plural", "fixed-phrase"]),
                new("party time", "mauk-mokh-dakur", PartOfSpeech: "noun", GrammarClass: "celebration", Tags: ["party", "time", "fixed-phrase"]),
                new("ale flow", "rukh-quum rukh-lag", PartOfSpeech: "noun", GrammarClass: "drink", Tags: ["ale", "flow", "fixed-phrase"]),
                new("mouthful", "narg-burz-quum", PartOfSpeech: "noun", GrammarClass: "food", Tags: ["mouth", "food", "compound"]),
                new("hum", "thrum-narg-rukh", PartOfSpeech: "noun", GrammarClass: "sound", Tags: ["quiet", "sound", "compound"]),
                new("favour", "drav-thog-bit", PartOfSpeech: "noun", GrammarClass: "transfer", Tags: ["favor", "small", "british", "compound"]),
                new("favours", "drav-thog-biti", PartOfSpeech: "noun", GrammarClass: "transfer", Tags: ["favor", "small", "plural", "british", "compound"]),
                new("adventurous", "vark-yankuk", PartOfSpeech: "adjective", GrammarClass: "motion", Tags: ["adventure", "possessive-derived"]),
                new("contemplation", "thogin-thog", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["thinking", "abstract", "compound"])
            ]);

            entries.AddRange([
                new("abilities", "mauk-heki", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["capability", "plural", "ooc"]),
                new("ability", "mauk-hek", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["capability", "ooc"]),
                new("skill", "mauk-hek", PartOfSpeech: "noun", GrammarClass: "ability", Tags: ["shared-form", "close-form-reviewed", "ability", "capability", "wiki-fodder"]),
                new("ability score", "mauk-hek bib-zorn", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["ability", "score", "fixed-phrase", "ooc"]),
                new("ability scores", "mauk-hek bib-zorni", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["ability", "score", "plural", "fixed-phrase", "ooc"]),
                new("additional", "lag-ti-bit", PartOfSpeech: "adjective", GrammarClass: "quantity", Tags: ["more", "ooc"]),
                new("additional hit points", "vrak-brak zorn lag-ti-bit", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["health", "points", "fixed-phrase", "ooc"]),
                new("advance", "ti-lagu", PartOfSpeech: "verb", GrammarClass: "progress", Tags: ["increase", "infinitive", "ooc"]),
                new("advancement", "ti-lagu-thog", PartOfSpeech: "noun", GrammarClass: "progress", Tags: ["increase", "process", "compound", "ooc"]),
                new("advances", "ti-lagui", PartOfSpeech: "verb", GrammarClass: "progress", Tags: ["increase", "present", "ooc"]),
                new("advancing", "ti-laguin", PartOfSpeech: "verb", GrammarClass: "progress", Tags: ["increase", "progressive", "present", "ooc"]),
                new("advanced", "ti-lagash", PartOfSpeech: "adjective", GrammarClass: "progress", Tags: ["higher", "ooc"]),
                new("adventure", "vark-yank", PartOfSpeech: "noun", GrammarClass: "story", Tags: ["danger", "path", "compound", "ooc"]),
                new("allowable", "darg-bib-maukuk", PartOfSpeech: "adjective", GrammarClass: "rule", Tags: ["permitted", "compound", "ooc"]),
                new("ancestor", "mokh-dakur-mog", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["family", "past", "compound", "ooc"]),
                new("ancestors", "mokh-dakur-mogi", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["family", "past", "plural", "compound", "ooc"]),
                new("ancestry", "mokh-dakur", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["family", "past", "compound", "ooc"]),
                new("artifact", "dakur-drav", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["old", "valuable", "compound", "ooc"]),
                new("artifacts", "dakur-dravi", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["old", "valuable", "plural", "compound", "ooc"]),
                new("attack roll", "gash-narg zorn-bib", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["combat", "dice", "fixed-phrase", "ooc"]),
                new("award", "grod-drav", PartOfSpeech: "noun", GrammarClass: "reward", Tags: ["prize", "compound", "ooc"]),
                new("awarded", "grod-dravash", PartOfSpeech: "verb", GrammarClass: "reward", Tags: ["past", "compound", "ooc"]),
                new("balance", "gor-thog-mokh", PartOfSpeech: "noun", GrammarClass: "quality", Tags: ["evenness", "compound", "ooc"]),
                new("balanced", "gor-thog-mokhuk", PartOfSpeech: "adjective", GrammarClass: "quality", Tags: ["even", "possessive-derived", "ooc"]),
                new("basic rules", "ash-darg-bibi", PartOfSpeech: "noun", GrammarClass: "rule", Tags: ["simple", "plural", "fixed-phrase", "ooc"]),
                new("bonus experience points", "grod-drav thog-lag zorni", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["reward", "experience", "points", "fixed-phrase", "ooc"]),
                new("building", "hekin-dak", PartOfSpeech: "noun", GrammarClass: "creation", Tags: ["construction", "compound", "ooc"]),
                new("calamity", "morz-var-ti", PartOfSpeech: "noun", GrammarClass: "disaster", Tags: ["death", "trouble", "intensified", "compound", "ooc"]),
                new("campaign", "vark-lag-mokh", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["adventure", "series", "compound", "ooc"]),
                new("casting", "gur-nargin", PartOfSpeech: "verb", GrammarClass: "magic", Tags: ["spell", "progressive", "compound", "ooc"]),
                new("character creation", "mog-var hekin", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["character", "creation", "fixed-phrase", "ooc"]),
                new("character sheet", "mog-var bib", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["character", "document", "fixed-phrase", "ooc"]),
                new("class", "mog-var-lag", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["role", "path", "compound", "ooc"]),
                new("classes", "mog-var-lagi", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["role", "path", "plural", "compound", "ooc"]),
                new("combat", "gash-mokh", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["battle", "compound", "ooc"]),
                new("contributing", "dravin-ti", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["adding", "progressive", "compound", "ooc"]),
                new("cost", "drav-gash", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["price", "compound", "ooc"]),
                new("costs", "drav-gashi", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["price", "plural", "compound", "ooc"]),
                new("culture", "mokh-thog-dakur", PartOfSpeech: "noun", GrammarClass: "society", Tags: ["people", "custom", "compound", "ooc"]),
                new("cultures", "mokh-thog-dakuri", PartOfSpeech: "noun", GrammarClass: "society", Tags: ["people", "custom", "plural", "compound", "ooc"]),
                new("current", "dakur-nar", PartOfSpeech: "adjective", GrammarClass: "time", Tags: ["now", "compound", "ooc"]),
                new("damage roll", "brak-thog zorn-bib", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["damage", "dice", "fixed-phrase", "ooc"]),
                new("dark god", "burzuk mograth-darg-mog", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["deity", "dark", "fixed-phrase", "ooc"]),
                new("death door", "morz-dak kruk", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["death", "threshold", "fixed-phrase", "ooc"]),
                new("death's door", "morzuk dak-kruk", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["death", "threshold", "possessive", "fixed-phrase", "ooc"]),
                new("demi-human", "thrum-margi", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["partial-human", "compound", "ooc"]),
                new("disappeared", "noglarash", PartOfSpeech: "verb", GrammarClass: "condition", Tags: ["unseen", "past", "ooc"]),
                new("discovering", "oglarin-thog", PartOfSpeech: "verb", GrammarClass: "thought", Tags: ["finding", "progressive", "compound", "ooc"]),
                new("dwarven clan", "dwarf-mokh-vrak", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["dwarf", "clan", "fixed-phrase", "ooc"]),
                new("elf", "elf", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["exonym", "ooc"]),
                new("exceptional ability score", "mauk-hek bib-zorn-ti", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["ability", "score", "high", "fixed-phrase", "ooc"]),
                new("exceptional ability scores", "mauk-hek bib-zorni-ti", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["ability", "score", "high", "plural", "fixed-phrase", "ooc"]),
                new("expanding", "ti-hekin", PartOfSpeech: "verb", GrammarClass: "growth", Tags: ["growing", "progressive", "compound", "ooc"]),
                new("expertise", "hek-thog-ti", PartOfSpeech: "noun", GrammarClass: "skill", Tags: ["skill", "intensified", "compound", "ooc"]),
                new("extra", "lag-bit-ti", PartOfSpeech: "adjective", GrammarClass: "quantity", Tags: ["additional", "compound", "ooc"]),
                new("fantasy", "thog-naut", PartOfSpeech: "noun", GrammarClass: "genre", Tags: ["dream", "thought", "compound", "ooc"]),
                new("fire damage", "rukh-tur brak-thog", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["fire", "damage", "fixed-phrase", "ooc"]),
                new("foe", "gash-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["enemy", "compound", "ooc"]),
                new("foes", "gash-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["enemy", "plural", "compound", "ooc"]),
                new("founding", "ash-hekin", PartOfSpeech: "verb", GrammarClass: "creation", Tags: ["first", "making", "progressive", "compound", "ooc"]),
                new("fragment", "bib-brak", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["broken", "text", "compound", "ooc"]),
                new("free resource", "surgar drav", PartOfSpeech: "noun", GrammarClass: "resource", Tags: ["free", "valuable", "fixed-phrase", "ooc"]),
                new("frequency", "dakur-zorn", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["rate", "count", "compound", "ooc"]),
                new("gain", "dravu", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["obtain", "infinitive", "ooc"]),
                new("gaining", "dravin-hrowk", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["obtaining", "progressive", "compound", "ooc"]),
                new("gold", "zol-ti", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["valuable-metal", "compound", "ooc"]),
                new("group", "mokh-zorn", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["collection", "compound", "ooc"]),
                new("groups", "mokh-zorni", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["collection", "plural", "compound", "ooc"]),
                new("hand-written", "krub-bibash", PartOfSpeech: "adjective", GrammarClass: "text", Tags: ["written-by-hand", "compound", "ooc"]),
                new("held action", "tukurash hek", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["reserved", "action", "fixed-phrase", "ooc"]),
                new("hex crawling", "dug-agh-dug-lagin", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["travel", "hex", "fixed-phrase", "ooc"]),
                new("hexcrawling", "dug-agh-dug-lag-thog", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["travel", "hex", "compound", "ooc"]),
                new("higher", "ti-ti", PartOfSpeech: "adjective", GrammarClass: "degree", Tags: ["greater", "intensified", "ooc"]),
                new("hire", "dravu-mog", PartOfSpeech: "verb", GrammarClass: "transaction", Tags: ["employ", "infinitive", "compound", "ooc"]),
                new("hit dice", "vrak-brak zorn-bibi", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["health", "dice", "plural", "fixed-phrase", "ooc"]),
                new("hit point", "vrak-brak zorn", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["health", "point", "fixed-phrase", "ooc"]),
                new("hit points", "vrak-brak zorni", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["health", "points", "plural", "fixed-phrase", "ooc"]),
                new("hovering", "thrum-lagin", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["near", "progressive", "compound", "ooc"]),
                new("illusionist", "noglar-thog-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["illusion", "magic", "compound", "ooc"]),
                new("increase", "ti-heku", PartOfSpeech: "verb", GrammarClass: "growth", Tags: ["raise", "infinitive", "compound", "ooc"]),
                new("influence", "darg-thog-lag", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["power", "path", "compound", "ooc"]),
                new("influential", "darg-thog-laguk", PartOfSpeech: "adjective", GrammarClass: "power", Tags: ["compound", "compound-reviewed", "influence", "possessive-derived", "wiki-fodder"]),
                new("journal", "dakur-bib", PartOfSpeech: "noun", GrammarClass: "text", Tags: ["record", "compound", "ooc"]),
                new("knowledge", "thog-hek", PartOfSpeech: "noun", GrammarClass: "thought", Tags: ["knowing", "compound", "ooc"]),
                new("large clan", "mokh-vrak-ti", PartOfSpeech: "noun", GrammarClass: "kinship", Tags: ["clan", "large", "fixed-phrase", "ooc"]),
                new("level", "ti-lag", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["rank", "compound", "ooc"]),
                new("level cap", "ti-lag gor-ti", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["rank", "limit", "fixed-phrase", "ooc"]),
                new("level limit", "ti-lag gor-dak", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["rank", "limit", "fixed-phrase", "ooc"]),
                new("level limits", "ti-lag gor-daki", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["rank", "limit", "plural", "fixed-phrase", "ooc"]),
                new("levels", "ti-lagi", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["rank", "plural", "compound", "ooc"]),
                new("limit", "gor-dak", PartOfSpeech: "noun", GrammarClass: "boundary", Tags: ["boundary", "compound", "ooc"]),
                new("limits", "gor-daki", PartOfSpeech: "noun", GrammarClass: "boundary", Tags: ["boundary", "plural", "compound", "ooc"]),
                new("lore", "dakur-bib-thog", PartOfSpeech: "noun", GrammarClass: "knowledge", Tags: ["old-knowledge", "compound", "ooc"]),
                new("lost artifact", "noglarash dakur-drav", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["artifact", "lost", "fixed-phrase", "ooc"]),
                new("lost artifacts", "noglarash dakur-dravi", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["artifact", "lost", "plural", "fixed-phrase", "ooc"]),
                new("mage", "gur-narg-mog-ti", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["magic", "speaker", "compound", "ooc"]),
                new("magic", "gurmog-thog", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["arcane", "compound", "ooc"]),
                new("major story arc", "var-bib-lag-ti", PartOfSpeech: "noun", GrammarClass: "story", Tags: ["story", "large", "fixed-phrase", "ooc"]),
                new("major story arcs", "var-bib-lagi-ti", PartOfSpeech: "noun", GrammarClass: "story", Tags: ["story", "large", "plural", "fixed-phrase", "ooc"]),
                new("mapping", "bibnakin", PartOfSpeech: "verb", GrammarClass: "text", Tags: ["map", "progressive", "ooc"]),
                new("mature content", "ti-mog narg", PartOfSpeech: "noun", GrammarClass: "rating", Tags: ["adult", "content", "fixed-phrase", "ooc"]),
                new("minor story arc", "var-bib-lag-bit", PartOfSpeech: "noun", GrammarClass: "story", Tags: ["story", "small", "fixed-phrase", "ooc"]),
                new("minor story arcs", "var-bib-lagi-bit", PartOfSpeech: "noun", GrammarClass: "story", Tags: ["story", "small", "plural", "fixed-phrase", "ooc"]),
                new("multiclass", "dug-lag-mog-var", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["multiple", "classes", "compound", "ooc"]),
                new("mystic", "mograth-gurmog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["spiritual", "magic", "compound", "ooc"]),
                new("non-computerized", "nul-zol-thoguk", PartOfSpeech: "adjective", GrammarClass: "tool", Tags: ["not-machine", "compound", "ooc"]),
                new("outsmarting", "thog-gash-ti", PartOfSpeech: "verb", GrammarClass: "thought", Tags: ["clever-victory", "compound", "ooc"]),
                new("paralysis", "gor-vrak", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["body", "stilled", "compound", "ooc"]),
                new("pass", "lagu", PartOfSpeech: "verb", GrammarClass: "motion", Tags: ["go-through", "infinitive", "ooc"]),
                new("phantasmal", "noglar-nautuk", PartOfSpeech: "adjective", GrammarClass: "magic", Tags: ["illusion", "dream", "compound", "ooc"]),
                new("player", "narg-mog-var", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["game-term", "player", "compound", "ooc"]),
                new("players", "narg-mog-vari", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["game-term", "player", "plural", "compound", "ooc"]),
                new("political influence", "darg-thoguk darg-thog-lag", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["political", "influence", "fixed-phrase", "ooc"]),
                new("progression", "lag-ti-thog", PartOfSpeech: "noun", GrammarClass: "progress", Tags: ["advancement", "compound", "ooc"]),
                new("race", "vrak-mokh", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["lineage", "compound", "ooc"]),
                new("races", "vrak-mokhi", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["lineage", "plural", "compound", "ooc"]),
                new("racial level limits", "vrak-mokh ti-lag gor-daki", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["race", "level", "limit", "plural", "fixed-phrase", "ooc"]),
                new("reclaimed", "tukurash-dok", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["taken-back", "past", "compound", "ooc"]),
                new("recovering", "tukurin-dok", PartOfSpeech: "verb", GrammarClass: "possession", Tags: ["taking-back", "progressive", "compound", "ooc"]),
                new("reputation", "narg-mokh-thog", PartOfSpeech: "noun", GrammarClass: "social", Tags: ["public-opinion", "compound", "ooc"]),
                new("retainer", "tukur-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["follower", "compound", "ooc"]),
                new("retainers", "tukur-mogi", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["follower", "plural", "compound", "ooc"]),
                new("rolled", "zorn-bibash", PartOfSpeech: "verb", GrammarClass: "game-term", Tags: ["dice", "past", "compound", "ooc"]),
                new("rolls", "zorn-bibi", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["dice", "plural", "compound", "ooc"]),
                new("rules set", "darg-bib mokh", PartOfSpeech: "noun", GrammarClass: "rule", Tags: ["rules", "collection", "fixed-phrase", "ooc"]),
                new("rumored", "nargash-noglar", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["hidden-speech", "past", "compound", "ooc"]),
                new("save", "ut-vark", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["resistance", "compound", "ooc"]),
                new("saves", "ut-varki", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["resistance", "plural", "compound", "ooc"]),
                new("saving", "ut-varkin", PartOfSpeech: "verb", GrammarClass: "protection", Tags: ["resisting", "progressive", "compound", "ooc"]),
                new("saving throw", "ut-vark zorn-bib", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["save", "dice", "fixed-phrase", "ooc"]),
                new("saving throws", "ut-vark zorn-bibi", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["save", "dice", "plural", "fixed-phrase", "ooc"]),
                new("score", "dakururi-brakk", PartOfSpeech: "verb", GrammarClass: "achievement", Tags: ["action", "tally", "extrapolated"]),
                new("score", "bib-zorn", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["number", "compound", "ooc"]),
                new("scores", "bib-zorni", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["number", "plural", "compound", "ooc"]),
                new("settlements", "mog-daki", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["inhabited", "plural", "compound", "ooc"]),
                new("sheet", "bib-var", PartOfSpeech: "noun", GrammarClass: "text", Tags: ["document", "compound", "ooc"]),
                new("slot", "dak-bit", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["small-place", "compound", "ooc"]),
                new("slots", "dak-biti", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["small-place", "plural", "compound", "ooc"]),
                new("spells", "gur-nargi", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["spell", "plural", "compound", "ooc"]),
                new("starting", "ash-dakuk", PartOfSpeech: "adjective", GrammarClass: "time", Tags: ["initial", "compound", "ooc"]),
                new("stat", "mauk-zorn", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["ability", "score", "compound", "ooc"]),
                new("stats", "mauk-zorni", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["ability", "score", "plural", "compound", "ooc"]),
                new("story", "var-bib", PartOfSpeech: "noun", GrammarClass: "story", Tags: ["account", "compound", "ooc"]),
                new("sword attack", "zol-gash gash-narg", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["sword", "attack", "fixed-phrase", "ooc"]),
                new("table", "bib-zorn-mokh", PartOfSpeech: "noun", GrammarClass: "reference", Tags: ["data", "compound", "ooc"]),
                new("template", "bib-nak-ti", PartOfSpeech: "noun", GrammarClass: "text", Tags: ["pattern", "compound", "ooc"]),
                new("theurge", "mograth-gur-narg-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["divine", "magic", "compound", "ooc"]),
                new("thief", "drav-vark", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["stealer", "compound", "ooc"]),
                new("throw club", "krug-zol flit", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["club", "throw", "fixed-phrase", "ooc"]),
                new("to hit", "gashu", PartOfSpeech: "verb", GrammarClass: "game-term", Tags: ["attack", "infinitive", "ooc"]),
                new("turn", "dakur-hek", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["action-time", "compound", "ooc"]),
                new("turns", "dakur-heki", PartOfSpeech: "noun", GrammarClass: "game-term", Tags: ["action-time", "plural", "compound", "ooc"]),
                new("undead", "nul-morzuk", PartOfSpeech: "noun", GrammarClass: "creature", Tags: ["not-dead", "compound", "ooc"]),
                new("updating", "nar-hekin", PartOfSpeech: "verb", GrammarClass: "revision", Tags: ["making-current", "progressive", "compound", "ooc"]),
                new("wealth", "drav-zol-mokh", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["riches", "compound", "ooc"]),
                new("affluent", "drav-zol-mokhuk", PartOfSpeech: "adjective", GrammarClass: "wealth", Tags: ["compound", "compound-reviewed", "wealth", "possessive-derived", "wiki-fodder"]),
                new("weekly", "dakur-mokhuk", PartOfSpeech: "adjective", GrammarClass: "time", Tags: ["week", "possessive-derived", "ooc"]),
                new("world lore", "mur-dak dakur-bib-thog", PartOfSpeech: "noun", GrammarClass: "knowledge", Tags: ["world", "lore", "fixed-phrase", "ooc"]),
                new("world-building", "mur-dak-hekin", PartOfSpeech: "noun", GrammarClass: "creation", Tags: ["world", "building", "compound", "ooc"]),
                new("world-building awards", "mur-dak hekin grod-drav", PartOfSpeech: "noun", GrammarClass: "reward", Tags: ["world-building", "award", "fixed-phrase", "ooc"])
            ]);

            entries.AddRange([
                new("hit", "gash-brak", Tags: ["sitemap"]),
                new("movement", "lag-thog-zorn", Tags: ["sitemap"]),
                new("alignment", "darg-lag-thog", Tags: ["sitemap"]),
                new("type", "var-mokh", Tags: ["sitemap"]),
                new("morale", "yanki-thog-mokh", Tags: ["sitemap"]),
                new("throws", "flit-bibi", Tags: ["sitemap"]),
                new("number", "zorn", Tags: ["sitemap"]),
                new("appearing", "oglar-lagin", Tags: ["sitemap"]),
                new("range", "dak-mokh-zorn", Tags: ["sitemap"]),
                new("magical", "gurmog-thoguk", Tags: ["sitemap"]),
                new("neutral", "murk-laguk", Tags: ["sitemap"]),
                new("creatures", "vark-mog-biti", Tags: ["sitemap"]),
                new("monsters", "vark-mog-ti-i", Tags: ["sitemap"]),
                new("creature", "vark-mog-bit", Tags: ["sitemap"]),
                new("referee", "darg-narg-mog-ti", Tags: ["sitemap"]),
                new("water", "dak-rukh-ti", Tags: ["sitemap"]),
                new("duration", "dakur-lag-thog", Tags: ["sitemap"]),
                new("points", "zorni", Tags: ["sitemap"]),
                new("fire", "rukh-ash", Tags: ["sitemap"]),
                new("dungeon", "burz-dak-mur", Tags: ["sitemap"]),
                new("giant", "ti-mog-ti", Tags: ["sitemap"]),
                new("chance", "mauk-thog-bit", Tags: ["sitemap"]),
                new("total", "mokh-zorn-ti", Tags: ["sitemap"]),
                new("items", "drav-biti", Tags: ["sitemap"]),
                new("locations", "dak-zorni", Tags: ["sitemap"]),
                new("versus", "gash-nak-thog", Tags: ["sitemap"]),
                new("death", "morz-dakur", Tags: ["sitemap"]),
                new("power", "darg-gash-ti", Tags: ["sitemap"]),
                new("normal", "grak-laguk", Tags: ["sitemap"]),
                new("conventional", "grak-laguk", PartOfSpeech: "adjective", GrammarClass: "quality", Tags: ["shared-form", "close-form-reviewed", "normal", "standard", "wiki-fodder"]),
                new("magic-user", "gur-narg-mog-bit", Tags: ["sitemap"]),
                new("melee", "nak-gash", Tags: ["sitemap"]),
                new("monster", "vark-mog-ti", Tags: ["sitemap"]),
                new("brother", "mokh-gash", Tags: ["sitemap"]),
                new("large", "ti-mur", Tags: ["sitemap"]),
                new("cannot", "nul-mauk", Tags: ["sitemap"]),
                new("poison", "morz-rukh", Tags: ["sitemap"]),
                new("standard", "darg-bib-laguk", Tags: ["sitemap"]),
                new("battle", "gash-mokh-ti", Tags: ["sitemap"]),
                new("move", "lagu-zorn", Tags: ["sitemap"]),
                new("revert", "lagu-zorn-dok", PartOfSpeech: "verb", GrammarClass: "movement", Tags: ["compound", "compound-reviewed", "move", "back", "wiki-fodder"]),
                new("die", "zorn-bib-bit", Tags: ["sitemap"]),
                new("heroes", "yanki-mogi", Tags: ["sitemap"]),
                new("ancient", "dakur-muruk", Tags: ["sitemap"]),
                new("silver", "kelnib-zol", Tags: ["sitemap"]),
                new("dwarves", "dwarfi", Tags: ["sitemap"]),
                new("miles", "lag-zorni", Tags: ["sitemap"]),
                new("door", "dak-kruk", Tags: ["sitemap"]),
                new("effects", "hek-thogi", Tags: ["sitemap"]),
                new("evil", "morz-thog-nu", Tags: ["sitemap"]),
                new("high", "ti-dakuk", Tags: ["sitemap"]),
                new("away", "dok-lag-ti", Tags: ["sitemap"]),
                new("exile", "dok-lag-ti-dakku-dak", PartOfSpeech: "noun", GrammarClass: "separation", Tags: ["compound", "compound-reviewed", "away", "home", "wiki-fodder"]),
                new("breath", "hush-vrak", Tags: ["sitemap"]),
                new("point", "zorn-bit", Tags: ["sitemap"]),
                new("deep", "burz-dakuk", Tags: ["sitemap"]),
                new("town", "mog-dak-mur", Tags: ["sitemap"]),
                new("half", "thrum-murk", Tags: ["sitemap"]),
                new("hand", "krub-vrak", Tags: ["sitemap"]),
                new("special", "var-tiuk", Tags: ["sitemap"]),
                new("hall", "dak-mokh-ti", Tags: ["sitemap"]),
                new("asks", "nargur-thruk", Tags: ["sitemap"]),
                new("effect", "hek-thog-bit", Tags: ["sitemap"]),
                new("black", "burz-rug", Tags: ["sitemap"]),
                new("item", "drav-bit", Tags: ["sitemap"]),
                new("above", "ti-dak-oglar", Tags: ["sitemap"]),
                new("below", "thrum-dak", Tags: ["sitemap"]),
                new("powerful", "darg-gash-tiuk", Tags: ["sitemap"]),
                new("empty", "nul-hrowk", Tags: ["sitemap"]),
                new("far", "lag-dok", Tags: ["sitemap"]),
                new("next", "nak-dakur", Tags: ["sitemap"]),
                new("living", "dakuruk-ti", Tags: ["sitemap"]),
                new("potion", "rukh-gurmog", Tags: ["sitemap"]),
                new("drug", "rukh-gurmog", PartOfSpeech: "noun", GrammarClass: "medicine", Tags: ["shared-form", "potion", "medicine", "wiki-fodder"]),
                new("pharmaceutical", "rukh-gurmog", PartOfSpeech: "noun", GrammarClass: "medicine", Tags: ["shared-form", "drug", "potion", "wiki-fodder"]),
                new("throw", "flitu", Tags: ["sitemap"]),
                new("find", "oglaru-dok", Tags: ["sitemap"]),
                new("flying", "flitin-ti", Tags: ["sitemap"]),
                new("live", "dakuru-ti", Tags: ["sitemap"]),
                new("right", "grak-nak-ti", Tags: ["sitemap"]),
                new("weight", "bant-zorn", Tags: ["sitemap"]),
                new("size", "bant-mokh", Tags: ["sitemap"]),
                new("travel", "lagu-dok", Tags: ["sitemap"]),
                new("rate", "zorn-dakur", Tags: ["sitemap"]),
                new("tall", "ti-bantuk", Tags: ["sitemap"]),
                new("animals", "vril-mog-zorni", Tags: ["sitemap"]),
                new("protection", "gor-thog-ti", Tags: ["sitemap"]),
                new("energy", "rukh-darg", Tags: ["sitemap"]),
                new("less", "thrum-zorn", Tags: ["sitemap"]),
                new("decline", "thrum-zorn-thog", PartOfSpeech: "noun", GrammarClass: "change", Tags: ["compound", "compound-reviewed", "less", "abstract", "wiki-fodder"]),
                new("decadence", "thrum-zorn-thog", PartOfSpeech: "noun", GrammarClass: "decline", Tags: ["shared-form", "decline", "moral", "wiki-fodder"]),
                new("charm", "mauk-narg-gur", Tags: ["sitemap"]),
                new("immunity", "gor-ti-thog", Tags: ["sitemap"]),
                new("called", "nargash-var", Tags: ["sitemap"]),
                new("end", "dok-dakur", Tags: ["sitemap"]),
                new("lawful", "darg-bibuk-ti", Tags: ["sitemap"]),
                new("penalty", "morz-drav", Tags: ["sitemap"]),
                new("staff", "bant-zol-mog", Tags: ["sitemap"]),
                new("value", "drav-thog-ti", Tags: ["sitemap"]),
                new("able", "maukuk-ti", Tags: ["sitemap"]),
                new("beastman", "vark-margi-ti", Tags: ["sitemap"]),
                new("beastmen", "vark-margith-ti", Tags: ["sitemap"]),
                new("city", "mog-dak-ti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["settlement", "large", "compound"]),
                new("dragon", "rukh-vark-mog", Tags: ["sitemap"]),
                new("wands", "gur-banti", Tags: ["sitemap"]),
                new("remaining", "tukurash-bit", Tags: ["sitemap"]),
                new("halfling", "thrum-marg-bit", Tags: ["sitemap"]),
                new("speed", "lag-rukh", Tags: ["sitemap"]),
                new("affected", "hekash-var", Tags: ["sitemap"]),
                new("cold", "nul-rukhuk", Tags: ["sitemap"]),
                new("heavy", "bant-tiuk", Tags: ["sitemap"]),
                new("mind", "thog-vrak", Tags: ["sitemap"]),
                new("missile", "flit-zol-ti", Tags: ["sitemap"]),
                new("object", "hrowk-dak", Tags: ["sitemap"]),
                new("suffer", "morz-thogu", Tags: ["sitemap"]),
                new("concentration", "mur-thogin", Tags: ["sitemap"]),
                new("invisible", "nul-oglaruk", Tags: ["sitemap"]),
                new("north", "surg-lag", Tags: ["sitemap"]),
                new("begins", "ashur-dak", Tags: ["sitemap"]),
                new("detail", "bib-bit-thog", Tags: ["sitemap"]),
                new("doors", "dak-kruki", Tags: ["sitemap"]),
                new("experience", "thog-lag", Tags: ["sitemap"]),
                new("killed", "morz-gashash", Tags: ["sitemap"]),
                new("slain", "gash-morzash", Tags: ["sitemap"]),
                new("pieces", "brak-biti", Tags: ["sitemap"]),
                new("specific", "ash-varuk", Tags: ["sitemap"]),
                new("become", "varu", Tags: ["sitemap"]),
                new("chamber", "burz-dak-mokh", Tags: ["sitemap"]),
                new("entry", "ik-lag-bib", Tags: ["sitemap"]),
                new("lantern", "rukh-oglar-bant", Tags: ["sitemap"]),
                new("traps", "noglar-varki", Tags: ["sitemap"]),
                new("broken", "brakuk", Tags: ["sitemap"]),
                new("curse", "morz-gur-narg", Tags: ["sitemap"]),
                new("father", "mokh-darg-mog", Tags: ["sitemap"]),
                new("located", "dakukash", Tags: ["sitemap"]),
                new("main", "murk-tiuk", Tags: ["sitemap"]),
                new("powers", "darg-gash-tii", Tags: ["sitemap"]),
                new("wearer", "khal-mog", Tags: ["sitemap"]),
                new("animal", "vril-mog-bit", Tags: ["sitemap"]),
                new("body", "vrak-mog", Tags: ["sitemap"]),
                new("chainmail", "zol-bant-vrak", Tags: ["sitemap"]),
                new("encounter", "mokru-gash", Tags: ["sitemap"]),
                new("information", "thog-bibi", Tags: ["sitemap"]),
                new("iron", "zol", Tags: ["sitemap"]),
                new("rock", "dak-zol", Tags: ["sitemap"]),
                new("short", "thrum-bantuk", Tags: ["sitemap"]),
                new("earth", "dak-mur-ti", Tags: ["sitemap"]),
                new("global", "dak-mur-ti-mur-kaag-tuk", PartOfSpeech: "adjective", GrammarClass: "scope", Tags: ["compound", "compound-reviewed", "earth", "all", "wiki-fodder"]),
                new("planet", "dak-mur-bant-murkuk", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["compound", "compound-reviewed", "land", "round", "world", "wiki-fodder"]),
                new("elemental", "dak-rukh-rukhuk", Tags: ["sitemap"]),
                new("greater", "ti-muruk", Tags: ["sitemap"]),
                new("holy", "mograth-groduk", Tags: ["sitemap"]),
                new("house", "dakku-dak-ti", Tags: ["sitemap"]),
                new("war", "gash-dakur", Tags: ["sitemap"]),
                new("warfare", "gash-dakur-hek-ti", PartOfSpeech: "noun", GrammarClass: "combat", Tags: ["compound", "compound-reviewed", "war", "action", "wiki-fodder"]),
                new("claw", "kruk-vrak", Tags: ["sitemap"]),
                new("different", "agh-varuk", Tags: ["sitemap"]),
                new("fight", "gashu-mokh", Tags: ["sitemap"]),
                new("hands", "krub-vraki", Tags: ["sitemap"]),
                new("healing", "morz-dok-hekin", Tags: ["sitemap"]),
                new("medical", "morz-dok-hekinuk", PartOfSpeech: "adjective", GrammarClass: "healing", Tags: ["compound", "compound-reviewed", "healing", "possessive-derived", "wiki-fodder"]),
                new("radius", "murk-dak-zorn", Tags: ["sitemap"]),
                new("successful", "grod-lagash", Tags: ["sitemap"]),
                new("copper", "rug-zol", Tags: ["sitemap"]),
                new("detect", "oglaru-noglar", Tags: ["sitemap"]),
                new("green", "gruul-rug", Tags: ["sitemap"]),
                new("intelligent", "thog-tiuk", Tags: ["sitemap"]),
                new("lizardmen", "lizard-margith", Tags: ["sitemap"]),
                new("location", "dak-zorn", Tags: ["sitemap"]),
                new("objects", "hrowk-daki", Tags: ["sitemap"]),
                new("skeleton", "vrak-bant-morz", Tags: ["sitemap"]),
                new("swift", "lag-rukhuk", Tags: ["sitemap"]),
                new("village", "mog-dak-thrum", Tags: ["sitemap"]),
                new("wand", "gur-bant", Tags: ["sitemap"]),
                new("west", "naut-lag", Tags: ["sitemap"]),
                new("natural", "vril-dakuk", Tags: ["sitemap"]),
                new("reduced", "thrumash-zorn", Tags: ["sitemap"]),
                new("south", "rukh-lag", Tags: ["sitemap"]),
                new("darkness", "burz-thog-ti", Tags: ["sitemap"]),
                new("dead", "morzuk", Tags: ["sitemap"]),
                new("ground", "dak-thrum-ti", Tags: ["sitemap"]),
                new("languages", "narg-mokhi", Tags: ["sitemap"]),
                new("maximum", "zorn-ti", Tags: ["sitemap"]),
                new("distance", "lag-dak-zorn", Tags: ["sitemap"]),
                new("food", "quum", Tags: ["sitemap"]),
                new("grants", "dravur-ti", Tags: ["sitemap"]),
                new("intelligence", "thog-ti-zorn", Tags: ["sitemap"]),
                new("language", "narg-mokh", Tags: ["sitemap"]),
                new("speak", "nargu", Tags: ["sitemap"]),
                new("praise", "nargu-grod-zog-dorn", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["compound", "compound-reviewed", "speak", "good", "wiki-fodder"]),
                new("insult", "nargu-morz-thog-nu", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["compound", "compound-reviewed", "speak", "evil", "derive-past", "wiki-fodder"]),
                new("appears", "oglarur", Tags: ["sitemap"]),
                new("dragons", "rukh-vark-mogi", Tags: ["sitemap"]),
                new("torches", "rukh-tur-banti", Tags: ["sitemap"]),
                new("trees", "gruuli", Tags: ["sitemap"]),
                new("destroyed", "brakash-ti", Tags: ["sitemap"]),
                new("direction", "lag-oglar", Tags: ["sitemap"]),
                new("inflicts", "brakur", Tags: ["sitemap"]),
                new("metal", "zol-mokh", Tags: ["sitemap"]),
                new("ruins", "brak-daki", Tags: ["sitemap"]),
                new("surprise", "noglar-thog-ti", Tags: ["sitemap"]),
                new("terrain", "dak-var", Tags: ["sitemap"]),
                new("wood", "gruul-thog", Tags: ["sitemap"]),
                new("blow", "gash-rukh-grum-narg", Tags: ["sitemap"]),
                new("blue", "burz-oglar", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["color", "default"]),
                new("equipment", "hrowk-zoli", Tags: ["sitemap"]),
                new("gems", "zol-oglari", Tags: ["sitemap"]),
                new("mercenaries", "drav-gash-mogi", Tags: ["sitemap"]),
                new("recovery", "tukur-dok-thog", Tags: ["sitemap"]),
                new("research", "thog-oglarin", Tags: ["sitemap"]),
                new("enchanted", "gur-nargashuk", Tags: ["sitemap"]),
                new("fear", "vark-thog-burz", Tags: ["sitemap"]),
                new("infravision", "burz-oglar-thog", Tags: ["sitemap"]),
                new("wild", "vriluk", Tags: ["sitemap"]),
                new("worth", "dravkui-ashuk", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["value", "abstract", "compound"]),
                new("deity", "mograth-darg-zorn", Tags: ["sitemap"]),
                new("edge", "zol-nak", Tags: ["sitemap"]),
                new("gear", "hrowk-zol", Tags: ["sitemap"]),
                new("golem", "hek-mog-zol", Tags: ["sitemap"]),
                new("hills", "ti-dak-thrumi", Tags: ["sitemap"]),
                new("plane", "dak-thog-mur", Tags: ["sitemap"]),
                new("restrictions", "gor-dak-bibi", Tags: ["sitemap"]),
                new("statue", "zol-mog-bant", Tags: ["sitemap"]),
                new("wolf", "vark-gor-mogra-ti", Tags: ["sitemap"]),
                new("age", "dakur-mur", Tags: ["sitemap"]),
                new("command", "darg-narg", Tags: ["sitemap"]),
                new("cursed", "morz-gur-narguk", Tags: ["sitemap"]),
                new("disease", "morz-vrak-rukh", Tags: ["sitemap"]),
                new("sickness", "morz-vrak-rukh", PartOfSpeech: "noun", GrammarClass: "condition", Tags: ["shared-form", "disease", "condition", "wiki-fodder"]),
                new("harm", "brak-thog-morz", Tags: ["sitemap"]),
                new("vengeance", "brak-thog-morz-dok", PartOfSpeech: "noun", GrammarClass: "retaliation", Tags: ["compound", "compound-reviewed", "harm", "back", "wiki-fodder"]),
                new("ritual", "mograth-hek", Tags: ["sitemap"]),
                new("observance", "mograth-hek", PartOfSpeech: "noun", GrammarClass: "ritual", Tags: ["shared-form", "close-form-reviewed", "ritual", "ceremony", "wiki-fodder"]),
                new("sleep", "naut-thog", Tags: ["sitemap"]),
                new("to-hit", "gashu-zorn", Tags: ["sitemap"]),
                new("actions", "heki", Tags: ["sitemap"]),
                new("bear", "yank-vrak-mog", Tags: ["sitemap"]),
                new("daily", "dakur-ashuk", Tags: ["sitemap"]),
                new("double", "dug-ti", Tags: ["sitemap"]),
                new("east", "surg-lag-ti", Tags: ["sitemap"]),
                new("hoard", "drav-zol-mokh-ti", Tags: ["sitemap"]),
                new("horse", "hrog-ti", Tags: ["sitemap"]),
                new("land", "dak-mur", Tags: ["sitemap"]),
                new("oil", "rukh-rukh", Tags: ["sitemap"]),
                new("remains", "tukur-morzi", Tags: ["sitemap"]),
                new("strong", "yankuk", Tags: ["sitemap"]),
                new("underground", "dak-burzuk", Tags: ["sitemap"]),
                new("bunker", "dak-burzuk-gor-thog", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["compound", "compound-reviewed", "underground", "defense", "wiki-fodder"]),
                new("voice", "narg-rukh-mog", Tags: ["sitemap"]),
                new("week", "dakur-mokh-ti", Tags: ["sitemap"]),
                new("act", "heku-ti", Tags: ["sitemap"]),
                new("action", "hek-ti", Tags: ["sitemap"]),
                new("checks", "oglar-bib-biti", Tags: ["sitemap"]),
                new("cups", "rukh-bant-biti", Tags: ["sitemap"]),
                new("determine", "thog-oglaru", Tags: ["sitemap"]),
                new("hits", "gash-braki", Tags: ["sitemap"]),
                new("mounted", "hroguk", Tags: ["sitemap"]),
                new("moving", "lagin-zorn", Tags: ["sitemap"]),
                new("require", "thruku", Tags: ["sitemap"]),
                new("resist", "goru-vark", Tags: ["sitemap"]),
                new("slow", "grot-lag", Tags: ["sitemap"]),
                new("crawl", "grot-lag-lagu-zorn", PartOfSpeech: "verb", GrammarClass: "movement", Tags: ["compound", "compound-reviewed", "slow", "move", "wiki-fodder"]),
                new("southern", "rukh-laguk", Tags: ["sitemap"]),
                new("alcohol", "rukh-quum-darg", Tags: ["sitemap"]),
                new("cultist", "mograth-mokh-mog", Tags: ["sitemap"]),
                new("dies", "morzur-ti", Tags: ["sitemap"]),
                new("follows", "lagur-dok", Tags: ["sitemap"]),
                new("halflings", "thrum-marg-biti", Tags: ["sitemap"]),
                new("hero", "yanki-mog", Tags: ["sitemap"]),
                new("present", "nar-dakur", Tags: ["sitemap"]),
                new("reach", "krubu-lag", Tags: ["sitemap"]),
                new("recent", "nar-dakuruk", Tags: ["sitemap"])
            ]);

            entries.AddRange([
                new("scarab", "zol-kruk-bit", Tags: ["blog"]),
                new("heart-seed", "pukh-dakur-bit", Tags: ["blog"]),
                new("archive", "dakur-bib-dak", Tags: ["blog"]),
                new("records", "dakur-bibi", Tags: ["blog"]),
                new("sovereign", "darg-ti-mog-zorn", Tags: ["blog"]),
                new("fungal", "gruul-thrum-rukhuk", Tags: ["blog"]),
                new("warden", "gor-darg-mog", Tags: ["blog"]),
                new("violet", "burz-rug-oglar", Tags: ["blog"]),
                new("cylinder", "bant-rukh-mur", Tags: ["blog"]),
                new("cylinders", "bant-rukh-muri", Tags: ["blog"]),
                new("pump", "rukh-lag-hek", Tags: ["blog"]),
                new("bronze", "rug-zol-thrum", Tags: ["blog"]),
                new("resonance", "rukh-bant-mokh", Tags: ["blog"]),
                new("silence", "nul-rukh-thog", Tags: ["blog"]),
                new("vault", "gor-drav-dak", Tags: ["blog"]),
                new("rhythmic", "rukh-dakuruk", Tags: ["blog"]),
                new("strike", "gash-rukh-ti", Tags: ["blog"]),
                new("amber", "surg-rukh-zol", Tags: ["blog"]),
                new("golden", "zol-tiuk", Tags: ["blog"]),
                new("seal", "gor-bib", Tags: ["blog"]),
                new("mechanical", "zol-hekuk", Tags: ["blog"]),
                new("shelf", "ded", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["storage"]),
                new("silent", "nul-narg-rukhuk", Tags: ["blog"]),
                new("faint", "thrum-oglaruk", Tags: ["blog"]),
                new("advantage", "grod-nak", Tags: ["blog"]),
                new("steady", "gor-laguk", Tags: ["blog"]),
                new("whispers", "thrum-narg-biti", Tags: ["blog"]),
                new("circular", "murk-laguk-ti", Tags: ["blog"]),
                new("comfort", "grod-vrak-thog", Tags: ["blog"]),
                new("champion", "gash-ti-mog", Tags: ["blog"]),
                new("shaft", "burz-lag-dak", Tags: ["blog"]),
                new("focus", "murk-thog", Tags: ["blog"]),
                new("scribe", "bib-hek-mog-ti", Tags: ["blog"]),
                new("surges", "rukh-darg-lagi", Tags: ["blog"]),
                new("wizard-priest", "gurmog-mograth-mog", Tags: ["blog"]),
                new("pressure", "bant-darg-thog", Tags: ["blog"]),
                new("sharp", "zol-nakuk", Tags: ["blog"]),
                new("hallway", "dak-lag-burz", Tags: ["blog"]),
                new("physical", "vrakuk-ti", Tags: ["blog"]),
                new("drop", "thrum-lagu", Tags: ["blog"]),
                new("elephant", "ti-hrog-mog", Tags: ["blog"]),
                new("glass", "oglar-zol", Tags: ["blog"]),
                new("line", "lag-bib", Tags: ["blog"]),
                new("signet", "darg-bib-zol", Tags: ["blog"]),
                new("pool", "dak-rukh-murk", Tags: ["blog"]),
                new("stairs", "ti-lagi-dak", Tags: ["blog"]),
                new("central", "murkuk-ti", Tags: ["blog"]),
                new("lift", "ti-hrowku", Tags: ["blog"]),
                new("tongue", "narg-vrak", Tags: ["blog"]),
                new("downtime", "thrum-dakur", Tags: ["blog"]),
                new("leads", "lagur-ti", Tags: ["blog"]),
                new("scorpions", "kruk-vark-biti", Tags: ["blog"]),
                new("silk", "thrum-khal-ti", Tags: ["blog"]),
                new("surge", "rukh-darg-lag", Tags: ["blog"]),
                new("metallic", "zoluk-ti", Tags: ["blog"]),
                new("radiant", "oglar-rukh-tiuk", Tags: ["blog"]),
                new("overseer", "gor-darg-oglar-mog", Tags: ["blog"]),
                new("shimmering", "oglar-rukhin", Tags: ["blog"]),
                new("active", "nar-gabh", Tags: ["blog"]),
                new("basalt", "burz-dak-zol", Tags: ["blog"]),
                new("knack", "hek-thog-nak", Tags: ["blog"]),
                new("magistrate", "darg-bib-mog", Tags: ["blog"]),
                new("oracle", "naut-thog-narg", Tags: ["blog"]),
                new("slides", "lagur-thrum", Tags: ["blog"]),
                new("translucent", "oglar-thrumuk", Tags: ["blog"]),
                new("prism", "oglar-var-zol", Tags: ["blog"]),
                new("psionic", "thog-darguk", Tags: ["blog"]),
                new("pulsing", "rukh-bant-in", Tags: ["blog"]),
                new("soft", "thrum-vrakuk", Tags: ["blog"]),
                new("stress", "bant-thog-morz", Tags: ["blog"]),
                new("tactical", "gash-thoguk", Tags: ["blog"]),
                new("ceiling", "ti-khal-dak", Tags: ["blog"]),
                new("chisel", "zol-hek-bit", Tags: ["blog"]),
                new("climb", "ti-lagu-vrak", Tags: ["blog"]),
                new("engine", "zol-hek-mokh", Tags: ["blog"]),
                new("archontean", "archon-thoguk", Tags: ["blog"]),
                new("blood-bond", "pukh-bant-mokh", Tags: ["blog"]),
                new("cistern", "dak-rukh-gor", Tags: ["blog"]),
                new("crystal", "oglar-zol-ti", Tags: ["blog"]),
                new("frenzy", "gash-rukh-morz", Tags: ["blog"]),
                new("library", "bib-mokh-dak", Tags: ["blog"]),
                new("maintenance", "gor-hekin", Tags: ["blog"]),
                new("mushroom", "gruul-thrum-rukh", Tags: ["blog"]),
                new("narrow", "thrum-nakuk", Tags: ["blog"]),
                new("pulse", "rukh-bant-bit", Tags: ["blog"]),
                new("success", "grod-lag-thog", Tags: ["blog"]),
                new("wet", "rukhuk-ti", Tags: ["blog"]),
                new("attribute", "mauk-hek-var", Tags: ["blog"]),
                new("cabal", "noglar-mokh", Tags: ["blog"]),
                new("constitution", "vrak-yank-thog", Tags: ["blog"]),
                new("flickering", "rukh-oglar-bitin", Tags: ["blog"]),
                new("longsword", "zol-gash-lag", Tags: ["blog"]),
                new("mechanism", "zol-hek-thog", Tags: ["blog"]),
                new("shifts", "var-lagi", Tags: ["blog"]),
                new("sun-disk", "surg-zol-mur", Tags: ["blog"]),
                new("tunnel", "burz-lag", Tags: ["blog"]),
                new("archer", "flit-zol-mog", Tags: ["blog"]),
                new("aura", "rukh-oglar-mokh", Tags: ["blog"]),
                new("catches", "tukurur-rukh", Tags: ["blog"]),
                new("damp", "rukh-thrumuk", Tags: ["blog"]),
                new("empire", "darg-dak-mokh-ti", Tags: ["blog"]),
                new("falling", "thrum-lagin-ti", Tags: ["blog"]),
                new("lock", "gor-kruk-zol", Tags: ["blog"]),
                new("plot", "noglar-var-bib", Tags: ["blog"]),
                new("rot", "morz-quum-thog", Tags: ["blog"]),
                new("drops", "thrum-lagur", Tags: ["blog"]),
                new("enter", "ik-lagu-dak", Tags: ["blog"]),
                new("fine", "thrum-tiuk", Tags: ["blog"]),
                new("heretic", "mograth-nu-mog", Tags: ["blog"]),
                new("internal", "ik-burzuk-ti", Tags: ["blog"]),
                new("acolyte", "mograth-bit-mog", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("acolytes", "mograth-bit-mogi", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("arcanist", "gurmog-bib-mog", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("balm", "morz-grod-rukh", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("bard", "narg-rukh-mog-ti", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("bioluminescence", "dakur-rukh-oglar", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("bioluminescent", "dakur-rukh-oglaruk", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("bone", "vrak-zol", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("bones", "vrak-zoli", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("bookstore", "bib-drav-dak", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("bovine", "hrog-vark-ti", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("brain", "mog-ti-thog-vrak", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("brains", "mog-ti-thog-vraki", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("breakthrough", "brak-lag-ti", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("broken head", "brak-mog-ti", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("burial", "morz-dak-hek", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("burrow", "burz-lag-bit", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("cabbage", "vril-nar-mog-ti", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("cabbages", "vril-nar-mog-tii", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("calcified", "zol-vrakash", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("caprine", "ti-vark-bit", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("carnelian", "rug-zol-bit", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("caverns", "burz-dak-murki", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("cemetery", "morz-dak-mokh", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("chime", "thrum-narg-zol", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("chimes", "thrum-narg-zoli", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("compost", "morz-vril-quum", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("constabulary", "darg-gor-mokh", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("dawnstriders", "surg-lag-mogi", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("donation", "drav-thog-mograth", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("economic", "drav-thoguk", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("elixir", "dakur-rukh-quum", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("enclave", "gor-mokh-dak", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("ephemeris", "surg-naut-lag-bib", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("eulogy", "morz-narg-ti", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("heartstone", "grod-burz-zol-dak", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("necromancer", "morz-gurmog", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("notebook", "bib-thrum", Tags: ["blog", "shadowdim-ap-6-10", "generated"]),
                new("syncretic", "mokru-mograthuk", Tags: ["blog", "shadowdim-ap-6-10", "generated"])
            ]);


            // Generated from codex-scratch/freq-xls-lt100-recommended/missing-lexicon-words.txt.
            // Deterministic recommended-word forms are namespaced to avoid collisions with hand-built roots.
            entries.AddRange([
                new("bolted", "crossbow-flit-zolash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-bolt"]),
            ]);

            // Generated from codex-scratch/freq-xls-lt100-prepositions/prepositions-minus-qua-missing-lexicon.txt.
            // Deterministic preposition forms are namespaced to avoid collisions with hand-built roots.
            entries.AddRange([
                new("abreast", "aproakh-dakuk-bibi", Tags: ["frequency", "freq-xls-lt100-prepositions", "preposition", "generated"]),
            ]);

            // Generated from codex-scratch/orcish-freq-xls-ge100/missing-lexicon-words.txt.
            // Deterministic batch forms are namespaced to avoid collisions with hand-built roots.
            entries.AddRange([
                new("aah", "ata-hekmoguk", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("abbey", "grotin-bibi", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("abbott", "hrowkuk-exieuk-bibnak", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("abdomen", "bae-grotin-bibash", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("abdominal", "gur-ardenuk-dravin", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("abducted", "gabh-burzuk-dakururi", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("abduction", "cee-dravash-hekruh", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("abide", "dakku-grak", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("abnormal", "dakkin-brand-grodin", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("aboard", "dakash-grimuk-bibnaki", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("abortion", "ata-brakkin-dargi", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("abroad", "dok-dak-lag", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("absent", "darg-hekruhi-flitu", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("absolutely", "gashur-dravkui-kar", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("absorb", "drav-doku-ik-rukh", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("abstract", "grodhi-gashu-imla", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("absurd", "dargi-gori-hekash", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("abuse", "brak-bruku", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("abused", "flindi-banti-hrowkash", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("academic", "gor-gord-flitin", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("academy", "heku-kangstuk-dakur", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("accent", "flit-gor-narg-rukh", Tags: ["frequency", "freq-xls-ge100", "generated", "name-substring-cleanup"]),
                new("accepted", "mokra-dravkuash", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("accepting", "mokra-dravkuin", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("accessory", "ashur-dakuru-bibuk", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("accidents", "brakin-dakuri-darguk", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("accommodate", "dwarf-grodhi-falsta-karnu", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("accompany", "grot-drath-bruku", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("accomplish", "grod-lag-heku", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("accomplished", "doku-imla-kal-dakururi", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("accountant", "drav-bib-mog", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("accounted", "drav-bib-thogash", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("accounting", "bibnaki-burzi-grukh", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("accounts", "gorin-dorn-grodhi", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("accusation", "brakin-grukh-fa", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("accusations", "darg-narg-morzi", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("accuse", "dargur-dakash-hekruh", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("accused", "darg-narg-morzash", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("accusing", "darg-narg-morzin", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("ace", "bibnaki-bae", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("aces", "bibnaki-baei", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("achieve", "bae-draviki-ecd", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("achieved", "bae-draviki-ecdash", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("aching", "arhk-burzuk-hekmog", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("acquaintance", "mokra-thog-mog", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("acquainted", "bibnakin-dug-khalash", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("acres", "cbd-devis", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("actress", "ebb-hekfa-mog", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("actresses", "ebb-hekfa-hekmog", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("acute", "hrogar-grimuk", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adapt", "hrowgai-kaat", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adapted", "hrowgai-kaat-ash", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adapting", "hrowgai-kaat-in", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adapts", "hrowgai-kaat-i", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("addict", "gruuli-hekui-klap", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("addicted", "gruuli-hekui-klap-ash", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("addiction", "gruuli-hekui-klap-thog", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("addicts", "dakuri-chesire-fa", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("addressed", "gurmogi-bant-goru-ash", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adios", "gurmogi-daku", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adjourn", "bib-hrowgauk-krit", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adjourned", "bib-hrowgauk-krizh", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adjourning", "bib-hrowgauk-krit-in", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adjust", "var-thog-mokhu", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("admirable", "heku-hekmog-krubi", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("admiral", "archon-cbd-dakuk", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("admiration", "arden-jorun-brand-thog", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("admire", "arden-jorun-brand", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("admired", "arden-jorun-brand-ash", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("admirer", "arden-jorun-brand-mog", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("admiring", "arden-jorun-brand-in", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("admission", "grak-narg-thog", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("admissions", "grak-narg-thogi", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("admitted", "grak-nargash", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("admitting", "grak-nargin", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("adopt", "flitin-brukur-ghen", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adopted", "flitin-brukur-ghen-ash", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adoption", "flitin-brukur-ghen-thog", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adorable", "mokra-thog-bit", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("adoration", "mokra-thog", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("adore", "mokra-thogu", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("adored", "mokra-thogash", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("adoring", "mokra-thogin", Tags: ["frequency", "freq-xls-ge100", "generated", "review-repaired", "root-derived"]),
                new("adult", "dravkui-hrogar", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adults", "dravkui-hrogar-i", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("advantages", "grod-nak-i", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("adventures", "vark-yank-i", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("advise", "aba-hek-goru-dag", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("advised", "aba-hek-goru-dag-ash", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("advising", "aba-hek-goru-dag-in", Tags: ["frequency", "freq-xls-ge100", "generated"]),
                new("advisor", "aba-hek-goru-dag-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["advice", "counsel", "compound"]),
                new("affairs", "dragonuk-heki", PartOfSpeech: "noun", GrammarClass: "matter", Tags: ["matter", "plural", "compound"]),
                new("affecting", "dug-brakur-bibashin", PartOfSpeech: "verb", GrammarClass: "influence", Tags: ["progressive", "root-dug-brakur-bibash", "compound"]),
                new("affection", "dug-brakur-bibash-thog", PartOfSpeech: "noun", GrammarClass: "feeling", Tags: ["fondness", "abstract", "root-dug-brakur-bibash", "compound"]),
                new("affectionate", "dug-brakur-bibashuk", PartOfSpeech: "adjective", GrammarClass: "feeling", Tags: ["fondness", "possessive-derived", "root-dug-brakur-bibash", "compound"]),
                new("alarmed", "vark-narg-rukhash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-alarm"]),
                new("ambushed", "noglar-gashash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-ambush"]),
                new("ares", "gash-mograth-darg-mog", Tags: ["review", "root-candidate-repaired", "generated", "war", "deity", "root-derived"]),
                new("babbling", "grotash-hekui", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["noisy", "progressive", "compound"]),
                new("backing", "dokin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-candidate-final", "root-derived", "progressive", "base-back"]),
                new("blues", "burz-oglari", PartOfSpeech: "noun", GrammarClass: "color", Tags: ["blue", "plural"]),
                new("bluffing", "ti-dak-nak-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-bluff"]),
                new("boarded", "bib-dakash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-board"]),
                new("boarding", "bib-dak-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-board"]),
                new("booked", "bibash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-book"]),
                new("booking", "bibin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-book"]),
                new("booster", "hrog-fice", PartOfSpeech: "noun", GrammarClass: "support", Tags: ["increase", "support", "compound"]),
                new("bottled", "rukh-bant-burzash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-bottle"]),
                new("bowling", "quum-bant-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-bowl"]),
                new("bred", "vrak-mokh-hekash", PartOfSpeech: "verb", GrammarClass: "lineage", Tags: ["species", "lineage", "past", "compound"]),
                new("breed", "vrak-mokh-heku", PartOfSpeech: "verb", GrammarClass: "lineage", Tags: ["species", "lineage", "infinitive", "compound"]),
                new("brushed", "vril-thrum-dakash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-brush"]),
                new("brushing", "vril-thrum-dak-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-brush"]),
                new("camping", "dakku-thrum-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-camp"]),
                new("certificate", "darg-bib-zol-bibi-flit", PartOfSpeech: "noun", GrammarClass: "authority", Tags: ["official", "document", "sealed", "compound"]),
                new("chased", "vark-lagash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-chase"]),
                new("cities", "mog-dak-tii", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["settlement", "large", "plural", "compound"]),
                // No noun entry for "clam"; Orcish currently preserves only the idiom.
                new("clam up", "nul-nargu", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["idiom", "refuse", "silence", "fixed-phrase"]),
                new("clause", "foce-forge", PartOfSpeech: "noun", GrammarClass: "language", Tags: ["formal", "written", "compound"]),
                new("compassion", "ghash-doku", PartOfSpeech: "noun", GrammarClass: "emotion", Tags: ["mercy", "care", "compound"]),
                new("costing", "drav-gash-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-cost"]),
                new("counted", "darg-mog-tiash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-count"]),
                new("crucify", "dragonuk-deme", PartOfSpeech: "verb", GrammarClass: "punishment", Tags: ["execution", "violence", "compound"]),
                new("exhausting", "grot-vrak-kain", PartOfSpeech: "verb", GrammarClass: "fatigue", Tags: ["weary", "body", "progressive", "compound"]),
                new("expecting", "drivetin", PartOfSpeech: "verb", GrammarClass: "anticipation", Tags: ["anticipate", "progressive"]),
                new("expects", "drivetur", PartOfSpeech: "verb", GrammarClass: "anticipation", Tags: ["anticipate", "present"]),
                new("fielding", "quum-hek-mokh-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-field"]),
                new("figuring", "mog-var-morz-brak-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-figure"]),
                new("fortunes", "mauk-dakash", PartOfSpeech: "noun", GrammarClass: "fate", Tags: ["luck", "plural", "compound"]),
                new("guided", "lag-oglar-mogash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-guide"]),
                new("haunting", "darg-dak-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-haunt"]),
                new("headed", "mog-tiash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-head"]),
                new("hers", "umuki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-candidate-final", "root-derived", "s-form", "base-her"]),
                new("hoped", "mauk-thruk-thogash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-hope"]),
                new("hush", "dornukik", PartOfSpeech: "interjection", GrammarClass: "command", Tags: ["quiet", "imperative"]),
                new("influenced", "darg-thog-lagash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-influence"]),
                new("inning", "gash-dakur-thrum", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["review-promoted", "game", "period", "compound", "root-repaired"]),
                new("lieutenant", "drukh-darg", PartOfSpeech: "noun", GrammarClass: "rank", Tags: ["military", "rank", "compound"]),
                new("lightly", "rukh-oglar-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-light"]),
                new("liked", "mok-lag-dargash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-candidate-final", "root-derived", "past", "base-like"]),
                new("liking", "mok-lag-darg-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-candidate-final", "root-derived", "progressive", "base-like"]),
                new("longing", "mur-dakur-grum-narg-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-long"]),
                new("lunchtime", "dwarf-dak", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["meal", "midday", "compound"]),
                new("manly", "margi-ash-rukh-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-man"]),
                new("marched", "gash-lagash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-march"]),
                new("marching", "gash-lag-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-march"]),
                new("naming", "mog-narg-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-name"]),
                new("ordering", "darg-lag-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-order"]),
                new("overly", "dak-uk-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "problem-proposal-repair", "root-derived", "adverbial", "base-overly"]),
                new("phoenix", "ash-morz-gori", PartOfSpeech: "noun", GrammarClass: "creature", Tags: ["review-promoted", "name-substring-cleanup", "fire", "death", "rebirth", "compound"]),
                new("plaster", "dargi-ka", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["surface", "compound"]),
                new("plasters", "dargi-kai", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["surface", "plural", "compound"]),
                new("plastered", "dargi-kash", PartOfSpeech: "verb", GrammarClass: "surface", Tags: ["past", "compound"]),
                new("plastering", "dargi-kain", PartOfSpeech: "verb", GrammarClass: "surface", Tags: ["progressive", "compound"]),
                new("pm", "exenda-brak-grod", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["evening", "abbreviation"]),
                new("p.m.", "Exenda", PartOfSpeech: "noun", GrammarClass: "time", Tags: ["evening", "abbreviation", "root-repaired"]),
                new("racing", "vrak-mokh-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-race"]),
                new("relatively", "mokh-moguk-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-relative"]),
                new("reported", "narg-bib-thogash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-report"]),
                new("reservation", "disasdok-thog", PartOfSpeech: "noun", GrammarClass: "claim", Tags: ["abstract", "compound"]),
                new("reservations", "disasdok-thogi", PartOfSpeech: "noun", GrammarClass: "claim", Tags: ["abstract", "plural", "compound"]),
                new("reserve", "disasdok", PartOfSpeech: "verb", GrammarClass: "claim", Tags: ["infinitive"]),
                new("mothball", "disasdok-dok-lag-ti", PartOfSpeech: "verb", GrammarClass: "storage", Tags: ["compound", "compound-reviewed", "reserve", "away", "wiki-fodder"]),
                new("reserving", "disasdokin", PartOfSpeech: "verb", GrammarClass: "claim", Tags: ["progressive"]),
                new("resolved", "darg-thog-grodash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-resolve"]),
                new("scored", "dakururi-brakkuk", PartOfSpeech: "verb", GrammarClass: "achievement", Tags: ["past", "tally", "compound"]),
                new("seating", "dok-gashin", PartOfSpeech: "verb", GrammarClass: "posture", Tags: ["progressive", "compound"]),
                new("secondly", "dug-lag-hrowk-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "problem-proposal-repair", "root-derived", "adverbial", "base-secondly"]),
                new("signing", "narg-bib-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-sign"]),
                new("sins", "morz-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-sin"]),
                new("sirs", "gash-dargi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-sir"]),
                new("sites", "dok-gashuur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-sit"]),
                new("smell", "kaag-thog", PartOfSpeech: "verb", GrammarClass: "sense", Tags: ["smell", "root-repaired", "shortened", "base-smell", "derive-past"]),
                new("sneeze", "dornuk-goruk", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["breath", "compound"]),
                new("spelled", "gur-nargash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-spell"]),
                new("spelling", "gur-narg-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-spell"]),
                new("squared", "murk-mokh-dakash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-square"]),
                new("stated", "mauk-zornash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-stat"]),
                new("stoned", "zol-dakash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-stone"]),
                new("talked", "narg-thogash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-talk"]),
                new("targeted", "narg-gashash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-target"]),
                new("tie", "dok-ka", PartOfSpeech: "verb", GrammarClass: "binding", Tags: ["bind", "secure", "verb"]),
                new("relation", "dok-ka-thog", PartOfSpeech: "noun", GrammarClass: "association", Tags: ["compound", "compound-reviewed", "tie", "abstract", "wiki-fodder"]),
                new("troubled", "grot-varash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-trouble"]),
                new("troubling", "grot-var-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-trouble"]),
                new("warmed", "rukh-grodash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-warm"]),
                new("waving", "bant-narg-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-wave"]),
                new("welcomed", "mokra-dakash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-welcome"]),
                new("worthwhile", "dravashuk", PartOfSpeech: "adjective", GrammarClass: "value", Tags: ["value", "beneficial", "compound"]),
                new("worthy", "dravkui-ashuki", PartOfSpeech: "adjective", GrammarClass: "value", Tags: ["value", "deserving", "compound"]),
            ]);

            // Generated from codex-scratch/orcish-shadowdim-ap-31-36/missing-lexicon-words.txt.
            // Deterministic batch forms are namespaced to avoid collisions with hand-built roots.
            entries.AddRange([
                new("abyss", "aproakh-ashdak", Tags: ["blog", "shadowdim-ap-31-36", "generated"]),
                new("accelerate", "doki-dakukash-barrow", Tags: ["blog", "shadowdim-ap-31-36", "generated"]),
                new("accelerating", "aele-gurmogi-ik-khaluk", Tags: ["blog", "shadowdim-ap-31-36", "generated"]),
                new("accustomed", "hekmoguk-dokash-gashur", Tags: ["blog", "shadowdim-ap-31-36", "generated"]),
                new("acid", "gashu-dokash", Tags: ["blog", "shadowdim-ap-31-36", "generated"]),
                new("acidic", "morz-rukhuk", Tags: ["blog", "shadowdim-ap-31-36", "generated", "review-repaired", "root-derived"]),
                new("acquires", "draviki-hekash-hek", Tags: ["blog", "shadowdim-ap-31-36", "generated"]),
                new("adjacent", "gor-ata-hrowka", Tags: ["blog", "shadowdim-ap-31-36", "generated"]),
                new("adjustment", "var-thog-mokh", Tags: ["blog", "shadowdim-ap-31-36", "generated", "review-repaired", "root-derived"]),
                new("affair", "dragonuk-hek", PartOfSpeech: "noun", GrammarClass: "matter", Tags: ["matter", "compound"]),
                new("backed", "dokash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-candidate-final", "root-derived", "past", "base-back"]),
                new("bending", "lag-mokru-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-bend"]),
                new("bruising", "morz-vrak-burz-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-bruise"]),
                new("exhausted", "grot-vrak-kash", PartOfSpeech: "verb", GrammarClass: "fatigue", Tags: ["weary", "body", "past", "compound"]),
                new("guarded", "gor-mogash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-guard"]),
                new("hasted", "grak-lag-thogash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-haste"]),
                new("needing", "thrukin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-need"]),
                new("paling", "kelnib-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-pale", "root-repaired"]),
                new("placing", "dakin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-place"]),
                new("priestly", "mograth-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-priest"]),
                new("seated", "dok-gashash", PartOfSpeech: "verb", GrammarClass: "posture", Tags: ["past", "compound"]),
                new("seats", "dok-gashi", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["seat", "furniture", "plural", "compound"]),
                new("tethered", "dok-kash", PartOfSpeech: "verb", GrammarClass: "binding", Tags: ["bind", "secure", "past"]),
                new("timing", "dakurin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-time"]),
                new("warded", "gor-nargash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-ward"]),
                new("witnessed", "oglar-mogash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-witness"]),
                new("worthiness", "dravkui-ashuk-thog", PartOfSpeech: "noun", GrammarClass: "value", Tags: ["value", "abstract", "compound"]),
            ]);

            // Generated from codex-scratch/orcish-shadowdim-ap-26-30/missing-lexicon-words.txt.
            // Deterministic batch forms are namespaced to avoid collisions with hand-built roots.
            entries.AddRange([
                new("abjurations", "gio-ghash-dargi-kruk", Tags: ["blog", "shadowdim-ap-26-30", "generated"]),
                new("actor", "daki-hek", Tags: ["blog", "shadowdim-ap-26-30", "generated"]),
                new("addressing", "gurmogi-bant-goru-in", Tags: ["blog", "shadowdim-ap-26-30", "generated"]),
                new("adhesive", "ashi-hrowku-gog", Tags: ["blog", "shadowdim-ap-26-30", "generated"]),
                new("adjusting", "var-thog-mokhin", Tags: ["blog", "shadowdim-ap-26-30", "generated", "review-repaired", "root-derived"]),
                new("admit", "grak-nargu", Tags: ["blog", "shadowdim-ap-26-30", "generated", "review-repaired", "root-derived"]),
                new("adopts", "flitin-brukur-ghent", Tags: ["blog", "shadowdim-ap-26-30", "generated"]),
                new("advice", "aba-hek-goru", Tags: ["blog", "shadowdim-ap-26-30", "generated"]),
                new("aided", "drav-thrukash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-aid"]),
                new("biting", "kruk-gash-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-bite"]),
                new("causing", "thruk-thog-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-cause"]),
                new("contrasting", "mok-nu-thog-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-contrast"]),
                new("crowded", "mokh-murash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-crowd"]),
                new("damaged", "brak-thogash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-damage"]),
                new("deduce", "ashur-gashu", PartOfSpeech: "verb", GrammarClass: "reasoning", Tags: ["infinitive", "compound"]),
                new("deducting", "ashur-gashi", PartOfSpeech: "verb", GrammarClass: "reasoning", Tags: ["progressive", "compound"]),
                new("deduction", "ashur-gash-thog", PartOfSpeech: "noun", GrammarClass: "reasoning", Tags: ["abstract", "compound"]),
                new("expect", "drivet", PartOfSpeech: "verb", GrammarClass: "anticipation", Tags: ["anticipate", "verb"]),
                new("expected", "drivetash", PartOfSpeech: "verb", GrammarClass: "anticipation", Tags: ["anticipate", "past"]),
                new("gloved", "krub-khalash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-glove"]),
                new("inner", "ik-burzuk", PartOfSpeech: "adjective", GrammarClass: "position", Tags: ["interior", "possessive-derived", "compound"]),
                new("mentioning", "dikultin", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["cite", "progressive"]),
                new("newly", "nurik-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-new"]),
                new("orderly", "darg-lag-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-order"]),
                new("pained", "morz-vrak-thog-tiash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-pain"]),
                new("pets", "mokra-mogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-pet"]),
                new("plating", "quum-bant-thrum-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-plate"]),
                new("quieted", "thrum-narg-rukh-ashash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-quiet"]),
                new("reserved", "disasdokash", PartOfSpeech: "verb", GrammarClass: "claim", Tags: ["past"]),
                new("scented", "kaag-thogash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-scent"]),
                new("sensed", "kaag-oglar-thogash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-sense"]),
                new("shielding", "gor-zol-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-shield"]),
                new("signed", "narg-bibash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-sign"]),
                new("slightly-too-loud", "dornukik-lag-gash", PartOfSpeech: "interjection", GrammarClass: "command", Tags: ["quiet", "imperative", "fixed-phrase"]),
                new("sub-level", "daki-dwarfuk", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["underground", "level", "compound"]),
                new("sub-levels", "daki-dwarfuk-gash-drak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["underground", "level", "plural", "compound"]),
                new("talking", "narg-thog-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-talk"]),
                new("trailing", "lag-vril-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-trail", "root-repaired"]),
                new("unprotected", "nul-gor-thog", PartOfSpeech: "adjective", GrammarClass: "protection", Tags: ["review-promoted", "name-substring-cleanup", "negative", "defense", "compound"]),
                new("ways", "lagi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-way", "root-repaired"]),
            ]);

            // Generated from codex-scratch/orcish-shadowdim-ap-21-25/missing-lexicon-words.txt.
            // Deterministic batch forms are namespaced to avoid collisions with hand-built roots.
            entries.AddRange([
                new("abandons", "hek-ata-hrowkuk", Tags: ["blog", "shadowdim-ap-21-25", "generated"]),
                new("absolute", "dornuk-drukhi-drath", Tags: ["blog", "shadowdim-ap-21-25", "generated"]),
                new("absorbs", "drav-doku-ik-rukhur", Tags: ["blog", "shadowdim-ap-21-25", "generated", "review-repaired", "root-derived"]),
                new("accessible", "dwarfuk-gurmog-flitu", Tags: ["blog", "shadowdim-ap-21-25", "generated"]),
                new("acoustic", "bitin-cec-gashuri", Tags: ["blog", "shadowdim-ap-21-25", "generated"]),
                new("acting", "exie-issendin", Tags: ["blog", "shadowdim-ap-21-25", "generated"]),
                new("activates", "bantin-bant-fletragii", Tags: ["blog", "shadowdim-ap-21-25", "generated"]),
                new("activating", "bantin-bant-fletragiin", Tags: ["blog", "shadowdim-ap-21-25", "generated"]),
                new("actual", "hekruhur-gurmog-krom", Tags: ["blog", "shadowdim-ap-21-25", "generated"]),
                new("address", "gurmogi-bant-goru", Tags: ["blog", "shadowdim-ap-21-25", "generated"]),
                new("addresses", "gurmogi-bant-goru-i", Tags: ["blog", "shadowdim-ap-21-25", "generated"]),
                new("adept", "grodu-baku", Tags: ["blog", "shadowdim-ap-21-25", "generated"]),
                new("aims", "oglar-gash-thogi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-aim"]),
                new("armed", "yank-bantash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-arm"]),
                new("bag's", "hrowk-khal-thrumuk", PartOfSpeech: "noun", GrammarClass: "possession", Tags: ["review-promoted", "problem-proposal-repair", "root-derived", "possessive", "base-bag", "derived-audited"]),
                new("based", "mokh-dakash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-base"]),
                new("blistering", "morz-vrak-bit-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-blister"]),
                new("centered", "murk-dakash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-center"]),
                new("chasing", "vark-lag-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-chase"]),
                new("cuts", "ecod-enderur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-cut"]),
                new("damaging", "brak-thog-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-damage"]),
                new("fairly", "drav-mauk-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-fair"]),
                new("flanking", "gash-nak-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-flank"]),
                new("functioned", "gashin-grrtuk-lagash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-function"]),
                new("guiding", "lag-oglar-mog-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-guide"]),
                new("hushed", "dornukikash", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["review-promoted", "problem-proposal-repair", "root-derived", "past", "base-hush", "derived-audited"]),
                new("jade", "oglar-zol-grod", PartOfSpeech: "noun", GrammarClass: "material", Tags: ["gem", "green", "compound"]),
                new("pits", "burz-dak-tii", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-pit"]),
                new("precision-cut", "ecod-ender-grukhur", PartOfSpeech: "verb", GrammarClass: "craft", Tags: ["precise", "cut", "compound"]),
                new("precision cut", "ecod-ender-grukhur-bibi-goru", PartOfSpeech: "verb", GrammarClass: "craft", Tags: ["precise", "cut", "fixed-phrase"]),
                new("precise cutting", "ecod-ender-grukhur-lag-hush", PartOfSpeech: "verb", GrammarClass: "craft", Tags: ["precise", "cut", "progressive", "fixed-phrase"]),
                new("robed", "khal-tiash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-robe"]),
                new("scraping", "thrum-bit-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-scrap"]),
                new("shelving", "dedin", PartOfSpeech: "verb", GrammarClass: "storage", Tags: ["progressive"]),
                new("smithing", "hekruhurin", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-smith"]),
                new("sparked", "rukh-bitash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-spark"]),
                new("stationed", "darg-dakash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-station", "root-repaired"]),
                new("straining", "grot-thog-vrak-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-strain"]),
                new("titled", "darg-mog-nargash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-title"]),
            ]);

            // Generated from codex-scratch/orcish-shadowdim-ap-16-20/missing-lexicon-words.txt.
            // Deterministic batch forms are namespaced to avoid collisions with hand-built roots.
            entries.AddRange([
                new("accident", "mauk-thog-brak", Tags: ["blog", "shadowdim-ap-16-20", "generated", "review-repaired", "root-derived"]),
                new("accidental", "mauk-thog-bituk", Tags: ["blog", "shadowdim-ap-16-20", "generated", "review-repaired", "root-derived"]),
                new("activate", "bantin-bant-fletragi", Tags: ["blog", "shadowdim-ap-16-20", "generated"]),
                new("activated", "bantin-bant-fletragiash", Tags: ["blog", "shadowdim-ap-16-20", "generated"]),
                new("activation", "bantin-bant-fletragi-thog", Tags: ["blog", "shadowdim-ap-16-20", "generated"]),
                new("addition", "dravku-gashur-thog", Tags: ["blog", "shadowdim-ap-16-20", "generated"]),
                new("adhered", "ashi-hrowku-ash", Tags: ["blog", "shadowdim-ap-16-20", "generated"]),
                new("adopting", "flitin-brukur-ghen-in", Tags: ["blog", "shadowdim-ap-16-20", "generated"]),
                new("annals", "cab-draku", PartOfSpeech: "noun", GrammarClass: "record", Tags: ["history", "chronicle", "compound"]),
                new("approaching", "lag-thog-krag-burz-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-approach"]),
                new("bags", "hrowk-khal-thrumi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-bag"]),
                new("bridge", "ashdak-goru", PartOfSpeech: "noun", GrammarClass: "structure", Tags: ["crossing", "compound"]),
                new("brilliant", "deca-dedi", PartOfSpeech: "adjective", GrammarClass: "quality", Tags: ["bright", "excellent", "compound"]),
                new("bruised", "morz-vrak-burzash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-bruise"]),
                new("caused", "thruk-thogash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-cause"]),
                new("clawed", "kruk-vrak-ash", PartOfSpeech: "adjective", GrammarClass: "body", Tags: ["review-promoted", "name-substring-cleanup", "claw", "past-participle", "compound"]),
                new("corded", "bant-thrumash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-cord"]),
                new("crossed", "mograth-bantash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-cross"]),
                new("delver", "bibuk-grr", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["underground", "searcher", "compound"]),
                new("delvers", "bibuk-grrt", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["underground", "searcher", "plural", "compound"]),
                new("designed", "narg-varash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-design"]),
                new("dusting", "dak-thrum-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-dust"]),
                new("exhaust", "grot-vrak-ka", PartOfSpeech: "verb", GrammarClass: "fatigue", Tags: ["weary", "body", "verb", "compound"]),
                new("exhausts", "grot-vrak-kar", PartOfSpeech: "verb", GrammarClass: "fatigue", Tags: ["weary", "body", "present", "compound"]),
                new("eye's", "oglar-krubuk", PartOfSpeech: "noun", GrammarClass: "possession", Tags: ["review-promoted", "problem-proposal-repair", "root-derived", "possessive", "base-eye", "derived-audited"]),
                new("figured", "mog-var-morz-brakash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-figure"]),
                new("flaming", "rukh-tur-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-flame"]),
                new("forced", "darg-gashash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-force"]),
                new("forcing", "darg-gash-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-force"]),
                new("fumbling", "lag-grot-thog-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-fumble"]),
                new("guarding", "gor-mog-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-guard"]),
                new("heading", "mog-ti-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-head"]),
                new("heart", "grod-burz", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["heart", "compound"]),
                new("hiss", "mogumuki", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-candidate-final", "root-derived", "s-form", "base-his"]),
                new("hums", "thrum-narg-rukhi", PartOfSpeech: "noun", GrammarClass: "derived", Tags: ["review-promoted", "root-derived", "s-form", "base-hum"]),
                new("keyed", "thrak-hrowk-dargash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-key"]),
                new("lets", "vargur", PartOfSpeech: "verb", GrammarClass: "permission", Tags: ["review-promoted", "root-derived", "s-form", "base-let", "root-repaired", "derived-audited"]),
                new("man’s", "margiuk-grod-krag", PartOfSpeech: "noun", GrammarClass: "possession", Tags: ["review-promoted", "problem-proposal-repair", "root-derived", "possessive", "base-man", "variant-spelling", "root-repaired", "derived-audited"]),
                new("mentioned", "dikultash", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["cite", "past"]),
                new("noting", "nu-brak-burz-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-candidate-final", "root-derived", "progressive", "base-not"]),
                new("opened", "lag-nu-gorash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-open"]),
                new("packing", "hrowk-khal-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-pack"]),
                new("pen", "narg-bib-zol", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["blog", "shadowdim-ap-16-20", "generated", "review-promoted", "root-derived", "writing", "compound"]),
                new("penned", "narg-bib-ash-dag-ash", Tags: ["blog", "shadowdim-ap-16-20", "generated"]),
                new("penning", "narg-bib-zolin", PartOfSpeech: "verb", GrammarClass: "writing", Tags: ["review-promoted", "root-derived", "progressive", "base-pen", "compound"]),
                new("reporting", "narg-bib-thog-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-report"]),
                new("ringing", "bant-murk-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-ring"]),
                new("rolling", "zorn-bib-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-roll"]),
                new("saved", "ut-varkash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-save"]),
                new("seeing", "oglurin", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["review-promoted", "root-derived", "progressive", "present", "base-see", "root-repaired", "derived-audited"]),
                new("sensing", "kaag-oglar-thog-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-sense"]),
                new("shadowed", "burz-nakash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-shadow"]),
                new("shelves", "dedi", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["storage", "plural"]),
                new("shelved", "dedash", PartOfSpeech: "verb", GrammarClass: "storage", Tags: ["past"]),
                new("shielded", "gor-zolash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-shield"]),
                new("sit", "dok-gashu", PartOfSpeech: "verb", GrammarClass: "posture", Tags: ["infinitive", "compound"]),
                new("sits", "dok-gashur", PartOfSpeech: "verb", GrammarClass: "posture", Tags: ["present", "compound"]),
                new("smoothed", "thrum-vrakash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-smooth"]),
                new("sounded", "narg-rukhash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-sound"]),
                new("sounding", "narg-rukh-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-sound"]),
                new("sparking", "rukh-bit-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-spark"]),
                new("squarely", "murk-mokh-dak-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "root-derived", "adverbial", "base-square"]),
                new("stylus", "narg-bib-zol", PartOfSpeech: "noun", GrammarClass: "object", Tags: ["blog", "shadowdim-ap-16-20", "generated", "writing", "root-repaired", "shortened"]),
                new("sweating", "hush-rukh-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-sweat"]),
                new("tether", "dok-ka-burz-bant", PartOfSpeech: "verb", GrammarClass: "binding", Tags: ["bind", "secure", "verb"]),
                new("tether's", "dok-ka-burz-bantuk", PartOfSpeech: "noun", GrammarClass: "possession", Tags: ["review-promoted", "problem-proposal-repair", "root-derived", "possessive", "base-tether", "derived-audited"]),
                new("tethering", "dok-kain", PartOfSpeech: "verb", GrammarClass: "binding", Tags: ["bind", "secure", "progressive"]),
                new("tethers", "dok-kar", PartOfSpeech: "verb", GrammarClass: "binding", Tags: ["bind", "secure", "present"]),
                new("timed", "dakurash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-time"]),
                new("trio's", "dug-agh-ash-ash-dokuuk", PartOfSpeech: "noun", GrammarClass: "possession", Tags: ["review-promoted", "problem-proposal-repair", "root-derived", "possessive", "base-trio", "derived-audited"]),
                new("warding", "gor-narg-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-ward"]),
                new("watching", "gorin", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["review-promoted", "root-derived", "progressive", "base-watch", "root-repaired", "derived-audited"]),
                new("welcoming", "mokra-dak-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-welcome"]),
            ]);

            // Generated from codex-scratch/orcish-shadowdim-ap-1/non-chrome-text.txt.
            entries.AddRange([
                new("abruptly", "grak-lag-grak", Tags: ["blog", "shadowdim-ap-1", "generated", "review-repaired", "root-derived"]),
                new("absorbed", "drav-doku-ik-rukhash", Tags: ["blog", "shadowdim-ap-1", "generated", "review-repaired", "root-derived"]),
                new("access", "fletragi-demand-burz", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("accompanied", "draviki-gur-grukhur-goruk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("accurate", "arandian-heki-gurmogi", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("actually", "hekruhi-grukhur-dravku", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("adequate", "aaei-fletragi-hrowkash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("adequately", "aaei-fletragi-hrowkash-ku", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("adjusted", "var-thog-mokhash", Tags: ["blog", "shadowdim-ap-1", "generated", "review-repaired", "root-derived"]),
                new("aelves", "Aelves", PartOfSpeech: "noun", GrammarClass: "lineage", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym", "plural"]),
                new("aerial", "dravin-kelnib", PartOfSpeech: "adjective", GrammarClass: "sky", Tags: ["air", "above", "compound"]),
                new("ahead", "nar-lag", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("allow", "kur-darg", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("aloft", "ti-oglar", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("amid", "murk-ik", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("answer", "narg-dok", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("answered", "narg-dok-ash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("archon", "darg-ti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("arden", "Arden", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("arden's", "Ardenuk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("art", "hek-thog", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("avoid", "nu-nak-lagu", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("barely", "thrum-nak", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("been", "tash-ash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("before", "nar-ash-dakur", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("believe", "thog-dargu", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("better", "grod-ti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("beyond", "lag-nak-ti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("block", "bant-mur", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("born", "nar-vrakash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("boundary", "gor-nak-lag", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("bowels", "ik-daki", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("broad", "ti-mokh", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("brown", "dak-rug", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("burned", "rug-rukhash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("can't", "nu-karnu", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("careful", "gor-thoguk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("carefully", "gor-thoguk-uk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("change", "var-thog", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("cheese", "quum-thrum", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("chose", "var-lagash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("claim", "narg-darg", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("clear", "oglar-grod", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("clearly", "oglar-grod-uk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("cloak", "vrak-khal", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("cloak's", "vrak-khal-uk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("collegium", "Collegium", PartOfSpeech: "noun", GrammarClass: "institution", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("column", "ti-dak-bant", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("coming", "nak-lagin", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("concern", "gor-thog-morz", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("conqueror's", "Conqueroruk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("continued", "lagur-ash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("converse", "narg-mokru", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("court", "darg-bib-dak", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("cover", "gor-khal", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("cromm", "Cromm", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("crosses", "dug-lagur", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("crossing", "mograth-bant-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-cross"]),
                new("cube", "bant-mur-ti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("cut", "ecod-ender", PartOfSpeech: "verb", GrammarClass: "craft", Tags: ["cut", "verb", "compound"]),
                new("dawntrack", "Dawntrack", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("dearest", "mokra-ti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("deeded", "darg-bibash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("deeply", "burz-tiuk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("deornorth", "Deornorth", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("deornoth", "Deornoth", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym", "variant-spelling"]),
                new("depths", "shad-depth-dedi", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("descent", "thrum-lag", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("details", "bib-biti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("device", "zol-hek-bit-ti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("devis", "Devis", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("didn't", "nu-hekash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("don't", "nu-heku", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("done", "hekash-grod", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("dorn", "Dorn", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("dorn's", "Dornuk", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym", "possessive"]),
                new("down", "defuh", PartOfSpeech: "adverb", GrammarClass: "direction", Tags: ["downward"]),
                new("duo", "dug-mokh", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("dwimmer", "Dwimmer", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("early", "nar-dakur-ti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("easily", "grod-laguk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("ender", "Ender", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("enemy", "morz-mog", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("entrance", "ik-lag-dak", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("error", "morz-bib", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("ever", "dakur-var", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("exarchate", "Exarchate", PartOfSpeech: "noun", GrammarClass: "institution", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("exie", "Exie", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("exie's", "Exieuk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("falsta", "Falsta", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("falsta's", "Falstauk", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym", "possessive"]),
                new("future", "nar-dakur-lag", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("gate", "ik-lag-gor", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("gio", "Gio", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("giodon", "Giodon", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("government", "darg-mokh-ti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("great-barrow", "Great-barrow", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("guardswoman", "gor-mog-nar", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("gullet", "rukh-lag-ik", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("hazard", "morz-nak", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("hazards", "morz-naki", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("heat", "rug-rukh", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("helix", "Helix", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("holding", "gor-darg-dak", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("hooded", "ti-khal-ash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("i'll", "ugh-uk-heku", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["blog", "shadowdim-ap-1", "generated", "review-promoted", "contraction", "future", "first-person", "compound"]),
                new("i've", "Ughmi", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["contraction", "first-person", "perfect"]),
                new("imla", "Imla", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("imla's", "Imlauk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("impact", "gash-thog", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("itinerary", "lag-bib-ti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("jorun", "Jorun", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("kal-leon", "Kal-leon", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("knots", "khal-gori", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("lack", "nu-zorn", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("shortage", "nu-zorn", PartOfSpeech: "noun", GrammarClass: "scarcity", Tags: ["shared-form", "lack", "wiki-fodder"]),
                new("lantern's", "oglar-bant-uk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("legally", "darg-bibuk-uk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("letter", "bib-bit", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("limb", "vrak-lag", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("lover", "pukh-mokra", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("lowshelf", "Lowshelf", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("manage", "darg-heku", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("married", "pukh-bantash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("mostly", "mur-flit-mokh-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "problem-proposal-repair", "root-derived", "adverbial", "base-mostly"]),
                new("mouth", "narg-ik", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("nearest", "nak-ti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("nearly", "nak-ash-grod-grak", PartOfSpeech: "adverb", GrammarClass: "manner", Tags: ["review-promoted", "problem-proposal-repair", "root-derived", "adverbial", "base-nearly"]),
                new("nobody", "nu-mog", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("noted", "nu-brak-burzash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-candidate-final", "root-derived", "past", "base-not"]),
                new("opted", "varguash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-opt"]),
                new("ordered", "darg-bib-ash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("prelm", "Prelm", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("previous", "ash-dakuruk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("priest's", "mograth-mog-uk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("progress", "grod-lag", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("prolemion", "Prolemion", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("quakes", "shad-kwake-aaei", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("rameses", "Rameses", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("read", "bib-oglaru", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("reading", "bib-oglaru-in", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("ready", "gor-lag", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("safely", "gor-groduk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("secure", "dok-ka-grod-morz", PartOfSpeech: "verb", GrammarClass: "binding", Tags: ["bind", "secure", "verb"]),
                new("sent", "lag-dargash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("shadowdim", "Shadowdim", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("silently", "nul-narg-rukhuk-uk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("slowly", "grot-laguk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("solution", "grod-lag-bib", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("sovrast", "Sovrast", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("species", "mog-vrak", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("status", "darg-zorn", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("stelg", "Stelg", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("stelgaard", "Stelgaard", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("stelgard", "Stelgard", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym", "variant-spelling"]),
                new("student", "bib-thog-mog", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("tax", "darg-drav", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("taxes", "darg-dravi", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("tech", "zol-hek", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("thales", "Thales", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("that's", "ut-karn-morz-tur", PartOfSpeech: "pronoun", GrammarClass: "demonstrative", Tags: ["blog", "shadowdim-ap-1", "generated", "review-promoted", "contraction", "state", "compound"]),
                new("theo's", "Theouk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("there's", "dak-doku-darg-tur", PartOfSpeech: "adverb", GrammarClass: "location", Tags: ["blog", "shadowdim-ap-1", "generated", "review-promoted", "contraction", "state", "compound"]),
                new("they'll", "ughat-uk", PartOfSpeech: "pronoun", GrammarClass: "group", Tags: ["blog", "shadowdim-ap-1", "generated", "review-promoted", "contraction", "future", "third-person", "compound"]),
                new("they're", "ughat-tur", PartOfSpeech: "pronoun", GrammarClass: "group", Tags: ["blog", "shadowdim-ap-1", "generated", "review-promoted", "contraction", "state", "third-person", "compound"]),
                new("thick", "ti-thrum", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("things", "zol-biti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("thousand", "ti-dakur-ti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("thracia", "Thracia", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("told", "nargash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("too", "agh-ti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("torch", "rug-oglar-zol", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("tough", "yank-vrakuk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("tracks", "lag-bibi", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("trussed", "khal-gorash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("turning", "dakur-hek-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-turn"]),
                new("unable", "nu-karn", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("unless", "nu-ash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("used", "hekash-use", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("various", "var-biti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("vida", "Vida", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("visited", "nak-lagash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("void", "nul-dak-ti", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("walls", "gor-heki", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("warming", "rug-rukhin", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("wasn't", "nu-tash", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("we're", "ugh-mokh-tur", PartOfSpeech: "pronoun", GrammarClass: "group", Tags: ["blog", "shadowdim-ap-1", "generated", "review-promoted", "contraction", "state", "first-person-plural", "compound"]),
                new("what's", "narg-var nar", PartOfSpeech: "pronoun", GrammarClass: "question", Tags: ["blog", "shadowdim-ap-1", "generated", "contraction", "state", "fixed-phrase"]),
                new("without", "nu-agh", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("woman", "margi-nar", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("worked", "hek-grum-morzash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-work"]),
                new("world's", "dak-mokh-ti-uk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("worlds", "dak-mokh-tii", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("wrong", "morz-bibuk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("wynthia", "Wynthia", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("yes", "akh", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("yet", "nar-nu", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("you'll", "narg-uk", PartOfSpeech: "pronoun", GrammarClass: "address", Tags: ["blog", "shadowdim-ap-1", "generated", "review-promoted", "contraction", "future", "second-person", "compound"]),
                new("you're", "narg-tur", PartOfSpeech: "pronoun", GrammarClass: "address", Tags: ["blog", "shadowdim-ap-1", "generated", "review-promoted", "contraction", "state", "second-person", "compound"]),
                new("you've", "narg-tukur", PartOfSpeech: "pronoun", GrammarClass: "address", Tags: ["blog", "shadowdim-ap-1", "generated", "review-promoted", "contraction", "perfect", "second-person", "compound"]),
                new("yourself", "zu-vrak", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("youth's", "margi-bit-uk", Tags: ["blog", "shadowdim-ap-1", "generated"]),
                new("zorvin", "Zorvin", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-1", "generated", "proper-noun", "exonym", "keep-exonym"]),
            ]);

            // Generated from codex-scratch/orcish-shadowdim-ap-2/non-chrome-text.txt.
            entries.AddRange([
                new("abberant", "dok-gashin-hrowk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("accepts", "mokra-dravkur", Tags: ["blog", "shadowdim-ap-2", "generated", "review-repaired", "root-derived"]),
                new("according", "darg-laguk", Tags: ["blog", "shadowdim-ap-2", "generated", "review-repaired", "root-derived"]),
                new("add", "dravku-gashur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("added", "dravku-gashur-ash", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("adding", "dravku-gashur-in", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("adds", "dravku-gashur-i", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("adjusts", "var-thog-mokhur", Tags: ["blog", "shadowdim-ap-2", "generated", "review-repaired", "root-derived"]),
                new("admits", "grak-nargur", Tags: ["blog", "shadowdim-ap-2", "generated", "review-repaired", "root-derived"]),
                new("aele", "Aele", PartOfSpeech: "noun", GrammarClass: "lineage", Tags: ["blog", "shadowdim-ap-2", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("aelf", "Aelf", PartOfSpeech: "noun", GrammarClass: "lineage", Tags: ["blog", "shadowdim-ap-2", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("aelven", "Aelfuk", PartOfSpeech: "adjective", GrammarClass: "lineage", Tags: ["blog", "shadowdim-ap-2", "generated", "proper-noun", "exonym", "keep-exonym", "possessive-derived"]),
                new("alive", "rukh-nar", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("angles", "var-lag-biti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("answers", "narg-doki", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("approval", "grod-darg-thog", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("arandia", "Arandia", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-2", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("arandian", "Arandian", PartOfSpeech: "adjective", GrammarClass: "demonym", Tags: ["blog", "shadowdim-ap-2", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("aren't", "nu-nar", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("assesses", "thog-oglaruri", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("backpack", "khal-bant-vrak", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("befriend", "mokra-heku", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("bothering", "morz-nak-hekin", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("burdock's", "Burdockuk", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-2", "generated", "proper-noun", "exonym", "keep-exonym", "possessive"]),
                new("burn", "rug-rukhu", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("bust", "braku", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("cache", "gor-drav-dak-bit", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("calls", "narguri", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("canopy", "ti-khal-vril", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("chilly", "grot-rukhuk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("clergy", "mograth-mogi", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("colder", "grot-rukh-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("comparison", "var-oglar-thog", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("completely", "mokh-tiuk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("contains", "ik-dargur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("continuing", "lagurin", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("counting", "zornin", Tags: ["blog", "shadowdim-ap-2", "generated", "review-repaired", "root-derived"]),
                new("critical", "morz-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("demi-races", "cec-hrongarai", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["lineage", "partial", "plural", "compound"]),
                new("demi-humans", "cec-hrongarai-grod-drak", PartOfSpeech: "noun", GrammarClass: "species", Tags: ["lineage", "partial", "plural", "compound"]),
                new("deserted", "nu-moguk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("differences", "var-thog-biti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("directs", "lag-dargur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("downright", "burz-grod-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("downward", "thrum-laguk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("easing", "thrum-morz-thogin", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("elementals", "dak-rukh-mogi", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("emptying", "nul-rukh-hekin", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("encountered", "nak-mokruash", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("enjoy", "grod-thogu", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("entangled", "khal-goruk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("environs", "nak-daki", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("event", "dakur-hek-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("exits", "oglar-lagur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("explored", "oglar-lagash", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("explorer", "oglar-lag-mog", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("failure", "morz-lag", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("fighter-thieves", "gash-mog-tukur-mogi", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("flammable", "rug-rukhuk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("freeze", "grot-goru", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("fuel", "rug-rukh-zol", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("full", "mokh-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("generations", "dakur-vraki", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("glittering", "oglar-zol-in", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("gosterwick", "Gosterwick", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-2", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("greatmoss", "Greatmoss", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-2", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("half-empty", "thrum-murk-nul-hrowk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("haunted", "morz-thog-darguk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("heal", "vrak-grodu", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("highly", "ti-tiuk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("interesting", "thog-nakuk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("intimate", "pukh-nakuk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("karstbridge", "Karstbridge", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-2", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("laden", "bant-hrowkuk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("lake", "rukh-murk-dak-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("lanterns", "oglar-banti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("last", "ash-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("let's", "mokru-heku", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["blog", "shadowdim-ap-2", "generated", "review-promoted", "contraction", "inclusive", "imperative", "compound"]),
                new("lights", "rukh-oglari", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("limited", "gor-dak-ash", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("listening", "narg-oglarin", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("looks", "oglari", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("loosening", "thrum-gorin", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("loud", "ti-narguk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("lower", "thrum-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("luck", "grod-dakur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("lucky", "grod-dakuruk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("mantle", "khal-vrak-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("met", "mokruash", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("midstride", "murk-lag-vrak", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("mile", "lag-ti-zorn", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("mobility", "lag-vrak-thog", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("northern", "burz-laguk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("opening", "ik-lag-dak-nar", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("opines", "thog-nargur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("outlet", "oglar-rukh-lag", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("passage", "lag-thrum-dak", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("pauses", "nul-dakururi", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("potions", "rukh-grod-biti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("pounds", "zorn-vrak-biti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("prays", "mograth-nargur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("priority", "thrak-lag", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("probably", "thog-nak-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("produces", "oglar-hekur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("provisioned", "quum-darguk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("provisions", "quum-zoli", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("pyramid", "ti-mur-dak-zol", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("quarter", "mokh-var-bit", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("quietest", "nul-narguk-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("re-roll", "dok-var-bitu", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("recruit", "gash-mog-bit", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("reduction", "thrum-var-thog", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("refuel", "dok-rug-rukhu", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("remembers", "dakur-thogur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("repacking", "khal-dok-hekin", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("rest", "nul-vrak-dakur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("retorts", "narg-gashur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("retrieves", "hrowk-dokur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("ropers", "khal-morz-daki", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("rumble", "dak-narg-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("ruven", "Ruven", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-2", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("savoring", "rukh-thog-grodin", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("seeming", "oglar-thoguk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("separate", "var-dakuk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("servant", "darg-hek-mog", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("she's", "umra-uk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("shocked", "gash-thogash", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("shutter", "gor-oglar", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("sightings", "oglar-bibi", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("skeletons", "morzi-zoli", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("sliding", "lag-thrumin", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("smuggling", "noglar-hrowkin", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("snorts", "rukh-narg-morzur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("snowstorms", "grot-oglar-gashi", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("snuffs", "nul-rug-rukhur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("soon", "nar-dakur-bit", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("speaks", "nargui", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("stalactites", "ti-khal-dak-zoli", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("stashed", "gor-dravash", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("stock", "hrowk-drav-mokh", Tags: ["blog", "shadowdim-ap-2", "generated", "review-repaired", "root-derived"]),
                new("strip", "khal-bit", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("subjected", "darg-morzash", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("suppressed", "gor-thrumash", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("survived", "morz-varkash", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("swallow", "rukh-iku", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("swear", "narg-darg-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("takes", "dravkui", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("tale", "dakur-narg", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("taller", "ti-lag-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("tearing", "khal-brakin", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("tension", "bant-thog", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("thirty-count", "ti-dakur-ti-zorn", Tags: ["blog", "shadowdim-ap-2", "generated", "review-repaired", "root-derived"]),
                new("thousands", "ti-dakur-tii", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("thread", "khal-lag-bit", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("tongues", "narg-vraki", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("tool", "hek-zol", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("treated", "vrak-gorash", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("tripled", "dug-ti-agh", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("truemas", "Truemas", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-2", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("tunnels", "burz-lagi", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("uncertainly", "nu-gor-thoguk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("unmarked", "nu-bibuk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("unnerved", "morz-thogash", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("unrelated", "nu-mokruuk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("untold", "nu-nargash-thog", Tags: ["blog", "shadowdim-ap-2", "generated", "review-repaired", "root-derived"]),
                new("untying", "nu-khal-gorin", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("upward", "ti-laguk", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("valley", "thrum-dak-lag", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("vanguard", "nar-gor-mog", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("vines", "vril-khali", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("warms", "rug-rukhur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("watches", "gor-oglarur", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("we'll", "ugh-mokh-uk", PartOfSpeech: "pronoun", GrammarClass: "group", Tags: ["blog", "shadowdim-ap-2", "generated", "review-promoted", "contraction", "future", "first-person-plural", "compound"]),
                new("we've", "ugh-mokh-tukur", PartOfSpeech: "pronoun", GrammarClass: "group", Tags: ["blog", "shadowdim-ap-2", "generated", "review-promoted", "contraction", "perfect", "first-person-plural", "compound"]),
                new("wield", "zol-dargu", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("wrongly", "morz-bibuk-ti", Tags: ["blog", "shadowdim-ap-2", "generated"]),
                new("you'd", "narg-hekash", PartOfSpeech: "pronoun", GrammarClass: "address", Tags: ["blog", "shadowdim-ap-2", "generated", "review-promoted", "contraction", "conditional", "second-person", "compound"]),
                new("zhorvin", "Zhorvin", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-2", "generated", "proper-noun", "exonym", "keep-exonym"]),
            ]);

            // Generated from codex-scratch/orcish-shadowdim-ap-3/non-chrome-text.txt.
            entries.AddRange([
                new("abundant", "exie-brukin-doki", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("acquire", "hrowk-doku", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("acts", "heku-tii", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("adorn", "dokur-hekruh-flit", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("adorned", "dokur-hekruh-flitu", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("adorning", "dokur-hekruh-flit-in", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("arden-flow", "Arden-flow", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["blog", "shadowdim-ap-3", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("attacked", "gash-narg-ash", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("attacking", "gash-narg-in", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("attacks", "gashuri", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("baboon", "vrak-mog-morz", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("baboon's", "vrak-mog-morz-uk", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("beware", "gor-oglaru", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("bites", "kruk-gashi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("bitten", "krumash", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("blowing", "gash-rukh-in-ash-zog", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("buildings", "hekin-daki", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("burdock", "Burdock", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-3", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("burning", "rug-rukhu-in", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("chambers", "burz-dak-mokhi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("charcoal", "rug-burz-zol", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("choices", "varg-thogi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("cliff-top", "ti-dak-zol-nak-ti", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("cloaks", "vrak-khali", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("coins", "drav-zoli", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("comes", "ik-lagui", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("crossbows", "dug-bant-zoli", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("curses", "morz-gur-nargi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("dawns", "surg-ashi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("directions", "lag-oglari", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("draws", "dravur-nak", Tags: ["blog", "shadowdim-ap-3", "generated", "review-repaired", "root-derived"]),
                new("encounters", "mokru-gashi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("ends", "dok-dakuri", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("exarch's", "Exarchuk", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("explorers", "oglar-lag-mogi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("factions", "mokh-dargi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("floors", "dak-burz-thrumi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("friends", "mokrai", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("gained", "dravu-ash", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("gog", "Gog", PartOfSpeech: "noun", GrammarClass: "name", Tags: ["blog", "shadowdim-ap-3", "generated", "proper-noun", "exonym", "keep-exonym"]),
                new("half-men", "thrum-murk-margith", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("he's", "mogum-tur", PartOfSpeech: "pronoun", GrammarClass: "person", Tags: ["blog", "shadowdim-ap-3", "generated", "review-promoted", "contraction", "state", "third-person", "compound"]),
                new("heights", "ti-thogi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("hieroglyphs", "mograth-bibi-zoli", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("ibis", "flit-mog-zol", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("initiatives", "ashdak-gash-thogi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("insisted", "dargu-thog-ash", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("it'll", "um-uk", PartOfSpeech: "pronoun", GrammarClass: "object", Tags: ["blog", "shadowdim-ap-3", "generated", "review-promoted", "contraction", "future", "third-person", "compound"]),
                new("largely", "ti-mur-uk", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("light's", "rukh-oglar-uk", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("named", "mog-nargash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-name"]),
                new("oils", "rukh-rukhi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("packs", "hrowk-khali", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("parchment", "bib-vrak", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("party", "lag-mokh", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("patrol", "gor-lag-mokh", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("pc's", "narg-mog-uk", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("pyramid's", "ti-mur-dak-zol-uk", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("rats", "rathi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("reads", "bib-oglarui", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("retrieve", "daki-grod", PartOfSpeech: "verb", GrammarClass: "recovery", Tags: ["infinitive", "compound"]),
                new("rings", "bant-murki", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("river", "rukh-lag-ti", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("secrets", "noglari", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("shadows", "burz-naki", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("sitting", "dok-gashin-zog-darg", PartOfSpeech: "verb", GrammarClass: "posture", Tags: ["progressive", "compound"]),
                new("snow-covered", "grot-oglar-goruk", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("statues", "zol-mog-banti", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("stocked", "hrowk-drav-mokhash", Tags: ["blog", "shadowdim-ap-3", "generated", "review-repaired", "root-derived"]),
                new("stone's", "zol-dak-uk", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("stories", "var-bibi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("swords", "zol-gashi", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("ties", "dok-kaur", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "s-form", "base-tie"]),
                new("towering", "ti-hek-in", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("traveling", "lagu-dok-in", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("travels", "lagu-doki", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("unlikely", "mok-nu-uk", Tags: ["blog", "shadowdim-ap-3", "generated"]),
                new("xp", "grod-dakur-zorn", Tags: ["blog", "shadowdim-ap-3", "generated"]),
            ]);

            // Generated from codex-scratch/orcish-shadowdim-ap-4/non-chrome-text.txt.
            entries.AddRange([
                new("abandon", "hekin-fa-dok", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("accentuate", "hrowkuri-hrowkur-gash", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("accept", "mokra-dravku", Tags: ["blog", "shadowdim-ap-4", "generated", "review-repaired", "root-derived"]),
                new("acceptable", "arcturus-gashi-evazhun", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("acquired", "dravik-heki-ka", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("advises", "aba-hek-goru-dag-i", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("ambushing", "noglar-gashin", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("anxious", "morz-thog-nar", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("asparagus", "vril-quum-lag", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("beholders", "ashuk-dravku", PartOfSpeech: "noun", GrammarClass: "creature", Tags: ["observer", "plural", "compound"]),
                new("blood-birds", "pukh-flit-mogi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("blunt", "nu-zol-nakuk", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("brings", "hrowkuri", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("burns", "rug-rukhui", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("canteen", "rukh-bant-lag", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("career", "hek-lag-dakur", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("cat", "vark-thrum-mog", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("cat's", "vark-thrum-mog-uk", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("changes", "var-thogi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("club", "gash-bant-zol", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("confidence", "thog-darg-ti", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("counters", "rukh-quum-banti", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("critically", "morz-ti-uk", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("crypts", "morzi-daki", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("currently", "dakur-nar-uk", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("dampens", "thrum-oglarur", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("distrust", "nu-thog-darg", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("dodges", "vark-lag-thrumi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("egress", "oglar-lag-dak", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("emerald", "vril-oglar-zol", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("enemies", "morz-mogi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("enroute", "lag-murk", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("explorer's", "oglar-lag-mog-uk", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("featuring", "mogum-narg-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-feature"]),
                new("feels", "grodhi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("female", "nar-margi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("filigree", "zol-khal-thrum", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("flagstones", "hek-lag-dak-zoli", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("frogmarching", "gor-lag-vrakin", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("glowing", "rukh-oglar-in", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("goats", "hrog-thrumi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("greenish", "vril-ruguk", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("grins", "mauk-nargi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("heads", "mog-tii", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("heals", "vrak-grodui", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("hogweed", "vark-vril-quum", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("hoping", "mauk-thruk-thog-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-hope"]),
                new("ivory", "vrak-zol-thrum", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("lantern-refills", "oglar-bant-rukh-doki", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("license", "darg-bib-ti", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("limbering", "vrak-thrum-grodin", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("limestone", "thrum-dak-zol", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("looted", "drav-varku-ash", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("manages", "darg-hekui", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("mention", "dikult", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["cite", "verb"]),
                new("mentions", "dikultur", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["cite", "present"]),
                new("quieten down", "dornukik-bant-burz", PartOfSpeech: "interjection", GrammarClass: "command", Tags: ["quiet", "imperative", "fixed-phrase"]),
                new("be quieter", "dornukik-zog-narg", PartOfSpeech: "interjection", GrammarClass: "command", Tags: ["quiet", "imperative", "fixed-phrase"]),
                new("minted", "drav-bibash", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("monetary", "drav-zornuk", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("moth-eaten", "kruk-quumash", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("myconids", "gruul-thrum-mogi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("oaks", "gruul-yank-tii", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("occasionally", "varg-dakur-uk", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("oily", "rukh-rukhuk", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("options", "vargi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("peering", "mokru-mog-in", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("positive", "grod-rukhuk", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("potency", "rukh-zorn-ti", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("prisoners", "gor-darg-mogi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("quarrels", "flit-zol-biti", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("resting", "nul-vrak-dakur-in", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("rituals", "mograth-heki", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("rounds", "bant-murkuki", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("scabies", "morz-vrak-biti", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("seconds", "dugi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("shakedown", "dravash-defuh", PartOfSpeech: "noun", GrammarClass: "coercion", Tags: ["extortion", "compound"]),
                new("shelf-like", "bib-bant-mok", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("speaking", "nargu-in", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("spores", "gruul-rukh-biti", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("square-cut", "murk-mokh-dak-zol-gashu", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("stick", "lag-zol-bit", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("targeting", "narg-gash-in", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("targets", "narg-gashi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("tariff", "drav-darg-bit", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("three-story", "dug-agh-ash-var-bib", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("tithing", "drav-mograth-dargin", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("trapdoor", "noglar-gor-dak", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("truce", "nul-gash-mokru", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("vaulted", "gor-drav-dak-ash", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("vial", "rukh-bant-bit-thrum", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("vials", "rukh-bant-bit-thrumi", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("vine", "vril-khal", Tags: ["blog", "shadowdim-ap-4", "generated"]),
                new("what'll", "narg-var-uk", PartOfSpeech: "pronoun", GrammarClass: "question", Tags: ["blog", "shadowdim-ap-4", "generated", "review-promoted", "contraction", "future", "compound"]),
            ]);

            // Generated from codex-scratch/orcish-shadowdim-ap-5/non-chrome-text.txt.
            entries.AddRange([
                new("admires", "arden-jorun-brand-i", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("allegiances", "goth-kruki", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("asparagus-looking", "vril-quum-lag-oglarin", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("brass-rimmed", "zarn-krom-dak", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("bribes", "gabh-narki", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("bug", "mik-zog", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("bypass", "skarg-pass", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("chamber's", "burz-dak-mokhuk", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("conservation", "thrak-keep", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("crown", "krun", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("damned", "morz-shak-dak", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("detected", "zog-ash", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("devotee", "goth-thrak", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("disable", "nu-heki", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("disarm", "nu-krizh", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("eldritch", "morgul-burz", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("escorted", "lead-dak", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("firevine", "ghash-vril", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("fluttering", "flit-rukh", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("gifted", "drav-thogash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-gift"]),
                new("grandfather", "burz-ata", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("guardian", "zog-thrak", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("identifies", "nam-zogi", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("imperials", "goth-narki", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("lurking", "mog-zog", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("minds", "nogri", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("mushrooms", "gruul-thrum-rukhi", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("mysterious", "mog-morgul", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("packed", "hrowk-khalash", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "past", "base-pack"]),
                new("pleased", "mauk-dravash", PartOfSpeech: "verb", GrammarClass: "emotion", Tags: ["review-promoted", "problem-proposal-repair", "root-derived", "past", "base-pleased"]),
                new("potion's", "rukh-gurmoguk", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("properties", "nar-grimuk", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("recognizes", "zog-nami", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("redoubt", "gord-krag", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("rounding", "bant-murkuk-in", PartOfSpeech: "verb", GrammarClass: "action", Tags: ["review-promoted", "root-derived", "progressive", "base-round"]),
                new("squashed", "brak-thrumash", Tags: ["blog", "shadowdim-ap-5", "generated", "review-repaired", "root-derived"]),
                new("statue's", "zol-mog-bantuk", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("value-to-weight", "drav-thog-ti-ur-bant-zorn", Tags: ["blog", "shadowdim-ap-5", "generated"]),
                new("vegetables", "vril-nar", Tags: ["blog", "shadowdim-ap-5", "generated"]),
            ]);

            entries.AddRange([
                new("veteran", "drath-gash", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["compound", "compound-reviewed", "old", "fighter", "wiki-fodder", "batch-12"]),
                new("discipline", "hekin-gash-darg-lag", PartOfSpeech: "noun", GrammarClass: "order", Tags: ["compound", "compound-reviewed", "training", "order", "wiki-fodder", "batch-12"]),
                new("reliability", "gor-laguk-thog", PartOfSpeech: "noun", GrammarClass: "quality", Tags: ["compound", "compound-reviewed", "steady", "abstract", "wiki-fodder", "batch-12"]),
                new("incursion", "gash-narg-ik-lagu-dak", PartOfSpeech: "noun", GrammarClass: "attack", Tags: ["compound", "compound-reviewed", "attack", "enter", "wiki-fodder", "batch-12"]),
                new("schism", "mokh-zorn-dug", PartOfSpeech: "noun", GrammarClass: "division", Tags: ["compound", "compound-reviewed", "group", "two", "wiki-fodder", "batch-12"]),
                new("captivity", "darg-varkum-thog", PartOfSpeech: "noun", GrammarClass: "imprisonment", Tags: ["compound", "compound-reviewed", "shared-form", "captive", "abstract", "wiki-fodder", "batch-12"]),
                new("reverence", "grak-tur-ti-mograth-thog", PartOfSpeech: "noun", GrammarClass: "faith", Tags: ["compound", "compound-reviewed", "honor", "faith", "wiki-fodder", "batch-12"]),
                new("pantheon", "mograth-darg-mogi-mokh-zorn", PartOfSpeech: "noun", GrammarClass: "religion", Tags: ["compound", "compound-reviewed", "gods", "group", "wiki-fodder", "batch-12"]),
                new("convert", "varu-mograth-thog", PartOfSpeech: "verb", GrammarClass: "change", Tags: ["compound", "compound-reviewed", "become", "faith", "wiki-fodder", "batch-12"]),
                new("layover", "nul-vrak-dakur-lagu-dok", PartOfSpeech: "noun", GrammarClass: "travel", Tags: ["compound", "compound-reviewed", "rest", "travel", "wiki-fodder", "batch-12"]),
                new("militia", "nak-dakuk-gash-darg-morz", PartOfSpeech: "noun", GrammarClass: "people", Tags: ["compound", "compound-reviewed", "local", "fighters", "wiki-fodder", "batch-12"]),
                new("craftsman", "mauk-hek-heku-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["compound", "compound-reviewed", "skill", "make", "person", "wiki-fodder", "batch-12"]),
                new("restriction", "gor-dak-dargu-mokh-mokh", PartOfSpeech: "noun", GrammarClass: "control", Tags: ["compound", "compound-reviewed", "limit", "control", "wiki-fodder", "batch-12"]),
                new("felony", "darg-bib-grum-brak-morz-bibuk", PartOfSpeech: "noun", GrammarClass: "crime", Tags: ["compound", "compound-reviewed", "law", "wrong", "wiki-fodder", "batch-12"]),
                new("judge", "darg-bib-grum-brak-mog", PartOfSpeech: "noun", GrammarClass: "law", Tags: ["compound", "compound-reviewed", "law", "person", "wiki-fodder", "batch-12"]),
                new("censorship", "dargu-mokh-mokh-narg-bib", PartOfSpeech: "noun", GrammarClass: "control", Tags: ["compound", "compound-reviewed", "control", "writing", "wiki-fodder", "batch-12"]),
                new("taxation", "darg-thog-quum-drav", PartOfSpeech: "noun", GrammarClass: "economy", Tags: ["compound", "compound-reviewed", "authority", "payment", "wiki-fodder", "batch-12"]),
                new("imprisonment", "darg-varkum-thog", PartOfSpeech: "noun", GrammarClass: "captivity", Tags: ["compound", "compound-reviewed", "shared-form", "captive", "abstract", "wiki-fodder", "batch-12"]),
                new("privilege", "var-tiuk-grak-nak-ti", PartOfSpeech: "noun", GrammarClass: "right", Tags: ["compound", "compound-reviewed", "special", "right", "wiki-fodder", "batch-12"]),
                new("decree", "darg-narg-darg-bib-grum-brak", PartOfSpeech: "noun", GrammarClass: "law", Tags: ["compound", "compound-reviewed", "command", "law", "wiki-fodder", "batch-12"]),
                new("selflessness", "drakuin-mur-kaag-tuk", PartOfSpeech: "noun", GrammarClass: "virtue", Tags: ["compound", "compound-reviewed", "giving", "all", "wiki-fodder", "batch-12"]),
                new("phenomenon", "var-tiuk-dakur-hek-ti", PartOfSpeech: "noun", GrammarClass: "event", Tags: ["compound", "compound-reviewed", "special", "event", "wiki-fodder", "batch-12"]),
                new("madness", "thog-vrak-nul-darg-thog", PartOfSpeech: "noun", GrammarClass: "mind", Tags: ["compound", "compound-reviewed", "mind", "chaos", "wiki-fodder", "batch-12"]),
                new("aberrant", "morz-bibuk-var-tiuk", PartOfSpeech: "adjective", GrammarClass: "quality", Tags: ["compound", "compound-reviewed", "wrong", "special", "wiki-fodder", "batch-12"]),
                new("resistant", "goru-varkuk", PartOfSpeech: "adjective", GrammarClass: "defense", Tags: ["compound", "compound-reviewed", "resist", "possessive-derived", "wiki-fodder", "batch-12"]),
                new("emanation", "gurmog-thog-dok-darg-krag", PartOfSpeech: "noun", GrammarClass: "magic", Tags: ["compound", "compound-reviewed", "magic", "from", "wiki-fodder", "batch-12"]),
                new("industrial", "hek-zol-hek-grum-morzuk", PartOfSpeech: "adjective", GrammarClass: "work", Tags: ["compound", "compound-reviewed", "tool", "work", "possessive-derived", "wiki-fodder", "batch-12"]),
                new("machine", "hek-grum-morz-hek-zol", PartOfSpeech: "noun", GrammarClass: "tool", Tags: ["compound", "compound-reviewed", "work", "tool", "wiki-fodder", "batch-12"]),
                new("steam", "rukh-ash-dak-rukh-ti-hush", PartOfSpeech: "noun", GrammarClass: "element", Tags: ["compound", "compound-reviewed", "fire", "water", "air", "wiki-fodder", "batch-12"]),
                new("dissipation", "thrum-zorn-thog", PartOfSpeech: "noun", GrammarClass: "decline", Tags: ["shared-form", "decadence", "decline", "wiki-fodder", "batch-12"]),
            ]);

            entries.AddRange(
            [
                new("dedicate", "draku-mur-kaag-tuk", PartOfSpeech: "verb", GrammarClass: "commitment", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("curb", "dargu-mokh-mokh-gor-dak", PartOfSpeech: "verb", GrammarClass: "control", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("spread", "lagu-zorn-mur-kaag-tuk", PartOfSpeech: "verb", GrammarClass: "movement", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("spore", "gruul-rukh-bit", PartOfSpeech: "noun", GrammarClass: "fungus", Tags: ["compound", "compound-reviewed", "close-form-reviewed", "spores", "wiki-fodder", "scroll-batch"]),
                new("epicenter", "murk-dak-dakur-hek-ti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("pay", "quum-dravu", PartOfSpeech: "verb", GrammarClass: "exchange", Tags: ["compound", "compound-reviewed", "close-form-reviewed", "payment", "wiki-fodder", "scroll-batch"]),
                new("weak", "nu-brak-burz-yankuk", PartOfSpeech: "adjective", GrammarClass: "strength", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("third", "dug-agh-ash-darg-lag", PartOfSpeech: "numeral", GrammarClass: "ordinal", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("fourth", "dug-dug-darg-lag", PartOfSpeech: "numeral", GrammarClass: "ordinal", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("break", "braku", PartOfSpeech: "verb", GrammarClass: "damage", Tags: ["shared-form", "close-form-reviewed", "bust", "broken-root", "wiki-fodder", "scroll-batch"]),
                new("leg", "vrak-lag", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["shared-form", "limb", "wiki-fodder", "scroll-batch"]),
                new("possible", "mauk-grrt-ashuk", PartOfSpeech: "adjective", GrammarClass: "possibility", Tags: ["compound", "compound-reviewed", "can", "possessive-derived", "wiki-fodder", "scroll-batch"]),
                new("seize", "dravku-krag-flit-darg-gash", PartOfSpeech: "verb", GrammarClass: "capture", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("track", "lag-narg-bib", PartOfSpeech: "noun", GrammarClass: "sign", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("source", "dok-darg-krag-dak", PartOfSpeech: "noun", GrammarClass: "origin", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("labor", "hek-grum-morz", PartOfSpeech: "noun", GrammarClass: "work", Tags: ["shared-form", "close-form-reviewed", "work", "wiki-fodder", "scroll-batch"]),
                new("prefer", "vargu-dak-zog-ti", PartOfSpeech: "verb", GrammarClass: "choice", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("waste", "flitu-dok-lag-ti", PartOfSpeech: "verb", GrammarClass: "disposal", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("desire", "thruk-thog-var", PartOfSpeech: "noun", GrammarClass: "want", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("reader", "bib-oglaru-mog", PartOfSpeech: "noun", GrammarClass: "person", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("strange", "var-tiuk-gi", PartOfSpeech: "adjective", GrammarClass: "quality", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("question", "narg-bib-thog-var", PartOfSpeech: "noun", GrammarClass: "speech", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("lie", "nargu-morz-bibuk", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("remove", "dravku-krag-flit-dok-lag-ti", PartOfSpeech: "verb", GrammarClass: "movement", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("deliver", "dravku-ik-draku", PartOfSpeech: "verb", GrammarClass: "transfer", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("instruction", "darg-narg-narg-bib", PartOfSpeech: "noun", GrammarClass: "command", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("follow", "lagu-dok-dak-tuk", PartOfSpeech: "verb", GrammarClass: "movement", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("watcher-mark", "thrak-narg-bib", PartOfSpeech: "noun", GrammarClass: "sign", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("thorn", "grodu-vrak-zorn-bit-dak", PartOfSpeech: "noun", GrammarClass: "plant", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("hill", "thrum-brak-grrt-ti-dak", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("split", "heku-dug-dak", PartOfSpeech: "verb", GrammarClass: "division", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("root", "grodu-vrak-mokh-dak", PartOfSpeech: "noun", GrammarClass: "plant", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("collect", "mokhu-mur-kaag-tuk", PartOfSpeech: "verb", GrammarClass: "gather", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("hunger", "quum-thruk", PartOfSpeech: "noun", GrammarClass: "need", Tags: ["compound", "compound-reviewed", "close-form-reviewed", "food", "need", "wiki-fodder", "scroll-batch"]),
                new("crude", "nu-brak-burz-mauk-hek", PartOfSpeech: "adjective", GrammarClass: "quality", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("depict", "heku-narg-bib", PartOfSpeech: "verb", GrammarClass: "representation", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("jagged", "brakuk-zol-nak", PartOfSpeech: "adjective", GrammarClass: "shape", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("sinkhole", "defuh-burz-dak-ti", PartOfSpeech: "noun", GrammarClass: "place", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("maw", "narg-ik-ti", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("northward", "ur-doku-goth-surg-lag", PartOfSpeech: "adverb", GrammarClass: "direction", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("rough", "brakuk-dak-thrum-ti", PartOfSpeech: "adjective", GrammarClass: "texture", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("sketch", "nu-brak-burz-mauk-hek-narg-bib", PartOfSpeech: "noun", GrammarClass: "representation", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("route", "lag", PartOfSpeech: "noun", GrammarClass: "path", Tags: ["shared-form", "close-form-reviewed", "path", "road", "wiki-fodder", "scroll-batch"]),
                new("label", "mog-narg-narg-bib", PartOfSpeech: "noun", GrammarClass: "sign", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("precise", "grak-nak-ti-zorn", PartOfSpeech: "adjective", GrammarClass: "accuracy", Tags: ["compound", "compound-reviewed", "wiki-fodder", "scroll-batch"]),
                new("suggest", "nargu-thog-var", PartOfSpeech: "verb", GrammarClass: "speech", Tags: ["compound", "compound-reviewed", "close-form-reviewed", "speak", "idea", "wiki-fodder", "scroll-batch"]),
                new("rendezvous", "mokru-dak", PartOfSpeech: "noun", GrammarClass: "meeting", Tags: ["shared-form", "close-form-reviewed", "rendezvous-point", "wiki-fodder", "scroll-batch"]),
            ]);

            // Generated from ten randomly sampled Obsidian Publish Markdown endpoints.
            // Every entry was admitted through ReviewProposedLexiconEntry before insertion.
            entries.AddRange([
                new("duergar", "zhar-agh-agh-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("per", "zhar-agh-agh-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("snakes", "zhar-agh-agh-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("swarm", "zhar-agh-agh-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("citadel", "zhar-agh-agh-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("fog", "zhar-agh-agh-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("flayers", "zhar-agh-agh-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("must", "zhar-agh-agh-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("once", "zhar-agh-agh-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("shanatar", "zhar-agh-agh-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("subject", "zhar-agh-agh-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("axes", "zhar-agh-agh-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("barakuir", "zhar-agh-agh-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("basilisk", "zhar-agh-agh-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("charges", "zhar-agh-agh-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("completed", "zhar-agh-agh-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("contact", "zhar-agh-burz-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("diameter", "zhar-agh-burz-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("divine", "zhar-agh-burz-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("driven", "zhar-agh-burz-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("killing", "zhar-agh-burz-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("laduguer", "zhar-agh-burz-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("lasts", "zhar-agh-burz-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("otherwise", "zhar-agh-burz-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("realm", "zhar-agh-burz-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("slaytonthorpe", "zhar-agh-burz-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("spider", "zhar-agh-burz-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("suicidal", "zhar-agh-burz-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("task", "zhar-agh-burz-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("underdark", "zhar-agh-burz-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("usage", "zhar-agh-burz-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("affect", "zhar-agh-burz-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("affects", "zhar-agh-dak-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("alchemical", "zhar-agh-dak-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("along", "zhar-agh-dak-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("already", "zhar-agh-dak-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("ambitious", "zhar-agh-dak-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("anytime", "zhar-agh-dak-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("aquatic", "zhar-agh-dak-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("battling", "zhar-agh-dak-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("believed", "zhar-agh-dak-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("believing", "zhar-agh-dak-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("captors", "zhar-agh-dak-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("carved", "zhar-agh-dak-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("chaotic", "zhar-agh-dak-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("chroma", "zhar-agh-dak-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("clans", "zhar-agh-dak-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("clerical", "zhar-agh-dak-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("cloudkill", "zhar-agh-drak-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("clubs", "zhar-agh-drak-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("coastlines", "zhar-agh-drak-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("commands", "zhar-agh-drak-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("completion", "zhar-agh-drak-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("complex", "zhar-agh-drak-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("conditions", "zhar-agh-drak-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("conjures", "zhar-agh-drak-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("countering", "zhar-agh-drak-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("cruel", "zhar-agh-drak-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("crumwich", "zhar-agh-drak-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("cultic", "zhar-agh-drak-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("cuprous", "zhar-agh-drak-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("darkvision", "zhar-agh-drak-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("deepkingdom", "zhar-agh-drak-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("delicacy", "zhar-agh-drak-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("demi", "zhar-agh-gar-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("denied", "zhar-agh-gar-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("denser", "zhar-agh-gar-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("determined", "zhar-agh-gar-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("determining", "zhar-agh-gar-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("dhardofhell", "zhar-agh-gar-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("direct", "zhar-agh-gar-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("discovered", "zhar-agh-gar-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("dissipates", "zhar-agh-gar-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("distinct", "zhar-agh-gar-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("dual-wielded", "zhar-agh-gar-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("dual-wielding", "zhar-agh-gar-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("dungeons", "zhar-agh-gar-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("earlier", "zhar-agh-gar-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("east-northeast", "zhar-agh-gar-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("effective", "zhar-agh-gar-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("efforts", "zhar-agh-gash-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("embodied", "zhar-agh-gash-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("emerged", "zhar-agh-gash-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("ending", "zhar-agh-gash-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("enslaving", "zhar-agh-gash-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("eventually", "zhar-agh-gash-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("exact", "zhar-agh-gash-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("example", "zhar-agh-gash-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("examples", "zhar-agh-gash-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("exclusively", "zhar-agh-gash-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("expedition", "zhar-agh-gash-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("experiments", "zhar-agh-gash-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("fall", "zhar-agh-gash-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("filling", "zhar-agh-gash-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("financially", "zhar-agh-gash-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("fingertips", "zhar-agh-gash-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("finished", "zhar-agh-gor-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("five", "zhar-agh-gor-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("flesh", "zhar-agh-gor-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("flowing", "zhar-agh-gor-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("forth", "zhar-agh-gor-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("freedom", "zhar-agh-gor-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("fro", "zhar-agh-gor-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("galleries", "zhar-agh-gor-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("gauntwood", "zhar-agh-gor-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("glacier", "zhar-agh-gor-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("going", "zhar-agh-gor-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("gracklstugh", "zhar-agh-gor-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("gradual", "zhar-agh-gor-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("gray", "zhar-agh-gor-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("great", "zhar-agh-gor-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("gundgathol", "zhar-agh-gor-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("harpers", "zhar-agh-grak-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("harsh", "zhar-agh-grak-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("hereditary", "zhar-agh-grak-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("history", "zhar-agh-grak-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("holes", "zhar-agh-grak-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("humanoids", "zhar-agh-grak-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("indicating", "zhar-agh-grak-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("individual", "zhar-agh-grak-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("industry", "zhar-agh-grak-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("info", "zhar-agh-grak-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("instant", "zhar-agh-grak-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("ironfist", "zhar-agh-grak-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("isolation", "zhar-agh-grak-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("journey", "zhar-agh-grak-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("keep", "zhar-agh-grak-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("kidnap", "zhar-agh-grak-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("king", "zhar-agh-grod-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("kingdom", "zhar-agh-grod-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("kingdoms", "zhar-agh-grod-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("kingship", "zhar-agh-grod-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("lakes", "zhar-agh-grod-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("lasted", "zhar-agh-grod-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("lead", "zhar-agh-grod-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("lies", "zhar-agh-grod-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("lightbringers", "zhar-agh-grod-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("lip", "zhar-agh-grod-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("lived", "zhar-agh-grod-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("lizard", "zhar-agh-grod-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("loses", "zhar-agh-grod-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("lowest", "zhar-agh-grod-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("maintain", "zhar-agh-grod-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("man-eaters", "zhar-agh-grod-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("materials", "zhar-agh-krag-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("millenia", "zhar-agh-krag-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("mind-affecting", "zhar-agh-krag-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("mistake", "zhar-agh-krag-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("movements", "zhar-agh-krag-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("mysteriously", "zhar-agh-krag-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("nation", "zhar-agh-krag-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("non-hostile", "zhar-agh-krag-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("north-northeast", "zhar-agh-krag-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("northeast", "zhar-agh-krag-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("numbers", "zhar-agh-krag-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("obscured", "zhar-agh-krag-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("operating", "zhar-agh-krag-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("originally", "zhar-agh-krag-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("oryndoll", "zhar-agh-krag-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("paying", "zhar-agh-krag-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("peak", "zhar-agh-mok-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("perfect", "zhar-agh-mok-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("perform", "zhar-agh-mok-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("performed", "zhar-agh-mok-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("period", "zhar-agh-mok-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("phantasms", "zhar-agh-mok-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("poisonous", "zhar-agh-mok-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("poisons", "zhar-agh-mok-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("post-ravaging", "zhar-agh-mok-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("prescribed", "zhar-agh-mok-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("prisoner", "zhar-agh-mok-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("proved", "zhar-agh-mok-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("quaggoths", "zhar-agh-mok-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("quests", "zhar-agh-mok-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("rapidly", "zhar-agh-mok-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("raw", "zhar-agh-mok-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("reaches", "zhar-agh-narg-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("reaching", "zhar-agh-narg-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("rearing", "zhar-agh-narg-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("reclamation", "zhar-agh-narg-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("referred", "zhar-agh-narg-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("refusal", "zhar-agh-narg-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("regard", "zhar-agh-narg-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("relations", "zhar-agh-narg-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("remnants", "zhar-agh-narg-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("rendered", "zhar-agh-narg-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("reptilian", "zhar-agh-narg-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("rivers", "zhar-agh-narg-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("rose", "zhar-agh-narg-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("rosen", "zhar-agh-narg-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("royals", "zhar-agh-narg-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("rulers", "zhar-agh-narg-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("semi-intelligent", "zhar-agh-ruk-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("service", "zhar-agh-ruk-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("seventy-five", "zhar-agh-ruk-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("sharing", "zhar-agh-ruk-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("silverhame", "zhar-agh-ruk-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("since", "zhar-agh-ruk-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("sinking", "zhar-agh-ruk-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("sinks", "zhar-agh-ruk-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("sixteen", "zhar-agh-ruk-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("snake", "zhar-agh-ruk-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("sometimes", "zhar-agh-ruk-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("southeast", "zhar-agh-ruk-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("spears", "zhar-agh-ruk-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("streams", "zhar-agh-ruk-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("subrace", "zhar-agh-ruk-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("swamps", "zhar-agh-ruk-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("swaying", "zhar-agh-skar-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("tails", "zhar-agh-skar-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("thereafter", "zhar-agh-skar-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("thirty", "zhar-agh-skar-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("thirty-three", "zhar-agh-skar-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("together", "zhar-agh-skar-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("tornspire", "zhar-agh-skar-gor", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("touched", "zhar-agh-skar-grak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("tribal", "zhar-agh-skar-grod", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("twice", "zhar-agh-skar-krag", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("types", "zhar-agh-skar-mok", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("typically", "zhar-agh-skar-narg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("undertake", "zhar-agh-skar-ruk", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("unlimited", "zhar-agh-skar-skar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("unusual", "zhar-agh-skar-thog", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("urged", "zhar-agh-skar-varg", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("ursandunthar", "zhar-agh-thog-agh", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("usable", "zhar-agh-thog-burz", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("venerated", "zhar-agh-thog-dak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("waxing", "zhar-agh-thog-drak", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("wind", "zhar-agh-thog-gar", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
                new("worship", "zhar-agh-thog-gash", Tags: ["wiki-fodder", "ten-page-sample", "generated", "review-promoted", "close-form-reviewed"]),
            ]);

            entries.AddRange(BuildNearKinEntries(entries));
            entries.AddRange(BuildFifteenPageSampleEntries(entries));
            entries.AddRange(BuildTwentyPageSampleEntries(entries));
            entries.AddRange(BuildThirtyPageSampleEntries(entries));
            entries.AddRange(BuildThirtyPageFollowupEntries(entries));

            var baseEntries = entries.ToArray();
            entries.AddRange(BuildDerivedMorphologyEntries(baseEntries));
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



        private const string ThirtyPageSourceCandidateData = """
ability-score
ability-scores
able-bodied
accuracy
acoustics
acquisition
actively
affinity
agility
agreement
algorn
alien
alloys
alludes
alternatively
ambient
amount
ample
amplified
apparent
apply
appropriate
approximately
aquamarine
archetype
architecture
arford
asked
assume
attributes
available
baked
bakery
balagos
band
banned
baraguld
bards
bats
bay
beholder
benign
besides
best
beverage
birds
blackfists
blacktooth
blessed
blinded
blinds
bodies
boost
bows
breaks
bribe
bright
broom
bugbear
bullseye
business
came
camel
camels
canine
capacity
captain
carelessly
cash
catalyze
cathedral
cavern
chances
check
chicks
child
choked
chuckles
chucky
claims
classic
claugiyliamatar
closely
closer
cloth
cloud
clustering
coffin
coffins
colloquially
colors
combatants
combine
constitute
construct
constructed
construction
continents
continual
cool
coordination
copper-containing
corrupting
council
cows
craftsmanship
crawls
cubic
dairy
daryon
deed
deeds
demand
demanded
demeanor
demihuman
departed
depend
depends
deputy
desert
designation
detailed
dewdrop
diamond
died
differ
difference
diplomacy
diplomats
dire
disassembling
disaster
discern
disintegrated
dive
dolecherry
domain
domains
dominating
dose
drain
drained
draining
dreaded
dreadful
durability
dynamics
earn
ears
ease
eastern
eat
eaten
ecosystem
edges
eery
eggs
eight
eighty-five
elderly
elfs
eliwyth
else
elvish
emit
emotions
employee
endurance
endures
enhanced
entering
equipped
erected
erupt
essence
essentials
every
everything
everywhere
except
exceptionally
exchanged
experimentation
exquisite
extant
extinct
extraordinary
exuding
fancy
faraway
farground
fascinated
fastwillow
fatty
favor
feared
feast
fed
fewer
fey
fielenghast
fifteen
fights
finesse
fireant
firelight
flask
flasks
fled
flee
flind-led
folks
foot
footmen
foraging
forbidden
forks
formulae
fortification
fortified
fortress
forty
fourths
fray
freezing
fritter
frivolous
fuck
fueled
functional
fur
garbald
garrison
gaseous
generate
generates
geoffroy
geppettin
gets
getting
ghoul
ghouls
gigantic
gintarn
glad
glaurthogos
gnoll
golnadeg
got
graced
granting
grasshare
grimshaw
growing
grown
growth
grue-spitter
guaranteed
half-gnome
half-halfling
halfway
hand-eye
hanging
happened
hard
heavily
heightened
herne
heroic
high-level
high-stakes
highest
highlighted
hinders
hippogriffs
hohnberg
homebrew
hoof
horsemen
horses
how
human-sized
hundred
hundreds
hunters
huts
ice-capped
ignore
illumination
immersion
immortal
immune
imposing
imprisoned
improve
inches
incredibly
incurs
incursions
initial
innate
instructed
intense
intent
intrigued
intruders
invite
inviting
inzeldrin
isaris
iymrith
jackpot
jelb
jerky
june
keen
khalidheroneth
khalidson
kid
knife
kurg
lady
lalan
lands
larger
largest
later
leans
learn
leaving
leveling
lighting
likely
linden
lindendale
listen
locales
long-term
longer
loss
love
lupine
lupins
lurches
magic-users
magically
magma
mail
manuals
maybe
mayor
meaningless
medium
memorize
memory
men-at-arms
mental
mercenary
merry
milestone
milk
mill
mind-reading
mined
minerals
minimum
mirabelle
mirrors
mithril
modern
moinder
moment
money
monstrous
moonwall
mornauguth
morph
motive
mount
mountainous
multiple
muscular
mystic-theurge
narinza
narrative
narratively
nastier
native
naturally
navigating
necessarily
negotiations
neria
nerissa
news
nixie
nixies
nods
noise
noises
nomad
nomads
notes
nothing
octopus
odors
odour
offer
okay
old-school
onto
onward
ore
oribul
oviparous
own
paralyzing
part
partly
parts
pastries
patrols
patterns
peaceful
pegasus
pelk
permanent
permanently
person
petrify
phaerimm
physically
piece
piercing
pies
pleasures
plundered
plus
pocket
pointed
poor
pose
potent
practically
pre-dated
predecessors
preferring
presented
preserve
pretty
prey
price
priest-mage
primal
prime
problem
problematic
process
proficient
profound
progeny
projects
pryrates
pushed
putting
questions
raise
raises
ranged
ranges
rather
readiness
recall
reception
reconnaissance
record
recorded
reedwhistle
reflect
reflection
regenerated
regeneration
regent
regions
regular
remarkable
remember
removed
repels
requisite
requisites
residing
respectively
result
rich
richer
riddled
rider
riothamus
risk
robbing
roc
rogue-like
roper
rotund
rounded
routes
royal
said
sailors
sandbox
say
says
scaled
scalies
school
scyntarn
sea
seamlessly
sector
seldom
separately
serene
servitor
servitors
set-piece
seven
several
shade
shafts
shape
sheriff
shift
shifting
shiny
shit
shortbow
shou
shown
shows
shrugs
side
sidekick
sides
sighs
signals
significance
simpler
simply
sing
situated
situations
six
skies
slay
slender
slew
smokeshadow
solo
something
son
songs
sons
sorry
spell-like
spend
spending
spine
sporting
spotted
squat
stake
stalagmites
stars
start
stashes
steppes
sticky
stiffwhiskers
strategic
strategies
struck
stumbled
subsequently
subservient
subtlest
successfully
sufficient
summerbranch
sunlight
superstitious
supplies
sure
surely
surpass
surprised
surroundings
survival
swath
swoop
tailored
tales
tar
telling
temporary
tents
thanks
themes
therefore
thereof
thickens
thing
thorns
thoul
three-point
thus
timid
tip
tipping
today
toll
tombs
tomorrow
totally
towards
toxic
traders
trainable
trait
traits
transform
transformation
transforms
travellers
traverse
treetops
trifles
trimmed
troll
trolls
troops
try
trying
turtle
two-quart
twofold
twohide
ulitharid
unambitious
undergo
underwater
unicorn
unique
unit
unmolested
unparalleled
unwelcome
urgent
urvan
utilize
vein
versatility
vertical
via
villages
visheat
visible
visitors
vulnerabilities
vurth
walking
want
warlike
warlock
warn
waterfall
weaponry
weigh
weighs
westmark
whatever
whew
whiteheart
wielder
wife
wildlife
willful
wineskin
witch-priestess
wolfen
wolves
wool
writings
wyvern
wyverns
yattel
yeah
yggian
zingren
zone
""";

        private const string ThirtyPageNearKinCandidateData = """
accuracies|accuracy
acquisitions|acquisition
affinities|affinity
agreements|agreement
aliened|alien
aliener|alien
aliening|alien
alienly|alien
aliens|alien
alloy|alloys
alloyed|alloys
alloying|alloys
allude|alludes
alluded|alludes
alluding|alludes
alternative|alternatively
ambiently|ambient
amounted|amount
amounting|amount
amounts|amount
ampler|ample
amplest|ample
amplifies|amplified
amplify|amplified
amplifying|amplified
amply|ample
apparently|apparent
applier|apply
applies|apply
applying|apply
appropriated|appropriate
appropriately|appropriate
appropriates|appropriate
appropriating|appropriate
approximate|approximately
aquamarines|aquamarine
archetypes|archetype
architectures|architecture
ask|asked
assumed|assume
assumes|assume
assuming|assume
ate|eaten
attributed|attributes
attributing|attributes
availably|available
bakeries|bakery
baking|baked
ban|banned
banded|band
banding|band
bands|band
banning|banned
bans|band
barded|bards
barding|bards
bayed|bay
baying|bay
bays|bay
benignly|benign
bested|best
bester|best
besting|best
bests|best
beverages|beverage
bird|birds
birded|birds
birding|birds
blind|blinds
blinding|blinded
bodied|bodies
bodying|bodies
boosted|boost
boosting|boost
boosts|boost
bow|bows
bowed|bows
breaking|breaks
bribed|bribe
briber|bribe
bribing|bribe
brighter|bright
brightest|bright
brightly|bright
brights|bright
broomed|broom
brooming|broom
brooms|broom
bugbears|bugbear
bullseyes|bullseye
businesses|business
canines|canine
capacities|capacity
captained|captain
captaining|captain
captains|captain
careless|carelessly
cashed|cash
casher|cash
cashes|cash
cashing|cash
catalyzed|catalyze
catalyzes|catalyze
catalyzing|catalyze
cathedrals|cathedral
caverned|cavern
caverning|cavern
chanced|chances
chancing|chances
checked|check
checker|check
checking|check
chick|chicks
chicking|chicks
childed|child
childing|child
childly|child
choking|choked
chuckies|chucky
chuckle|chuckles
chuckled|chuckles
chuckling|chuckles
claimed|claims
claiming|claims
classics|classic
closers|closer
clothed|cloth
clothing|cloth
cloths|cloth
clouded|cloud
clouding|cloud
clouds|cloud
cluster|clustering
clustered|clustering
clusters|clustering
coffined|coffin
coffining|coffin
colloquial|colloquially
color|colors
colored|colors
coloring|colors
combatant|combatants
combines|combine
combining|combine
constituted|constitute
constitutes|constitute
constituting|constitute
constructing|construct
constructions|construction
constructs|construct
continent|continents
continually|continual
cooled|cool
cooler|cool
coolest|cool
cooling|cool
coolly|cool
cools|cool
coordinations|coordination
corrupt|corrupting
corrupts|corrupting
councils|council
cow|cows
crawled|crawls
crawling|crawls
cubics|cubic
dairies|dairy
dairying|dairy
deeding|deed
demanding|demand
demands|demand
demeanors|demeanor
depart|departed
departing|departed
departs|departed
depended|depend
depending|depend
deputies|deputy
descriptions|~
deserter|desert
deserting|desert
deserts|desert
designations|designation
detailing|detailed
dewdrops|dewdrop
diamonded|diamond
diamonding|diamond
diamonds|diamond
differed|differ
differenced|difference
differencing|difference
differing|differ
differs|differ
diplomacies|diplomacy
diplomat|diplomats
direly|dire
direr|dire
direst|dire
disassembled|disassembling
disasters|disaster
discerning|discern
discerns|discern
disintegrate|disintegrated
disintegrates|disintegrated
disintegrating|disintegrated
displayed|~
displaying|~
displays|~
dived|dive
dives|dive
dominated|dominating
dosed|dose
doses|dose
dosing|~
drainer|drain
drains|drain
dread|dreaded
dreadfully|dreadful
dreading|dreaded
dreads|dreaded
dying|~
dynamic|dynamics
ear|earn
earned|earn
earning|earn
earns|earn
eased|ease
eases|ease
easternly|eastern
eating|eaten
eats|eaten
ecosystems|ecosystem
edged|edges
edging|edges
eerier|eery
eeriest|eery
eerily|eery
eights|eight
elvishly|elvish
emits|emit
emotion|emotions
emotioned|emotions
employees|employee
endurances|endurance
endure|endures
endured|endures
enduring|endures
enhancing|enhanced
equip|equipped
equips|equipped
erect|erected
erecting|erected
erects|erected
erupted|erupt
erupting|erupt
erupts|erupt
essences|essence
excepted|except
excepting|except
exceptional|exceptionally
excepts|except
exchanging|exchanged
experimentations|experimentation
exquisitely|exquisite
exquisites|exquisite
extraordinaries|extraordinary
extraordinarily|extraordinary
fancied|fancy
fancier|fancy
fancies|fancy
fanciest|fancy
fancily|fancy
fancying|fancy
fascinate|fascinated
fascinates|fascinated
fascinating|fascinated
fattier|fatty
fatties|fatty
fattiest|fatty
fattily|fatty
favored|favor
favorer|favor
favoring|favor
favors|favor
fearing|feared
fears|feared
feasted|feast
feaster|feast
feasting|feast
feasts|feast
feds|fed
feed|fed
feeding|fed
feeds|fed
fifteens|fifteen
fighting|fights
finessed|finesse
finesses|finesse
finessing|finesse
flasking|flask
flees|fled
folk|folks
footed|foot
footing|foot
foots|foot
foraged|foraging
fork|forks
forked|forks
forking|forks
fortier|forty
forties|forty
fortifications|fortification
fortifies|fortified
fortify|fortified
fortifying|fortified
fortressed|fortress
fortresses|fortress
frayed|fray
fraying|fray
frays|fray
freezings|freezing
frittered|fritter
frittering|fritter
fritters|fritter
frivolously|frivolous
fucked|fuck
fucking|fuck
fucks|fuck
fueling|fueled
fuels|fueled
functionally|functional
functionals|functional
furs|fur
garrisoned|garrison
garrisoning|garrison
garrisons|garrison
generated|generates
generating|generates
get|getting
gladly|glad
glads|glad
gracing|graced
grew|grown
grow|grown
grows|grown
growths|growth
hang|hanging
hanged|hanging
hangings|hanging
hangs|hanging
happen|happened
happening|happened
happens|happened
harder|hard
hardest|hard
hardly|hard
hards|hard
heighten|heightened
heightening|heightened
heightens|heightened
heroics|heroic
highlight|highlighted
highlighting|highlighted
highlights|highlighted
hinder|hinders
hindered|hinders
hindering|hinders
hippogriff|hippogriffs
homebrewed|homebrew
homebrewing|homebrew
hoofed|hoof
hoofing|hoof
hoofs|hoof
horsed|horses
horsing|horses
hunter|hunters
hut|huts
ignores|ignore
ignoring|ignore
illuminations|illumination
immersions|immersion
immortally|immortal
immortals|immortal
imprisoning|imprisoned
imprisons|imprisoned
improved|improve
improves|improve
improving|improve
inch|inches
inched|inches
inching|inches
incur|incurs
initialed|initial
initialing|initial
initials|initial
innately|innate
instruct|instructed
instructing|instructed
instructs|instructed
intensely|intense
intently|intent
intents|intent
intriguing|intrigued
intruder|intruders
invited|inviting
invites|inviting
jackpots|jackpot
jerkier|jerky
jerkiest|jerky
jerkily|jerky
keened|keen
keener|keen
keenest|keen
keening|keen
keenly|keen
keens|keen
kids|kid
knifed|knife
knifer|knife
knifes|knife
knifing|knife
ladies|lady
landed|lands
landing|lands
laters|later
lean|leans
leaned|leans
leaning|leans
learned|learn
learner|learn
learning|learn
learns|learn
leavings|leaving
leveled|leveling
lighted|lighting
lightings|lighting
likelier|likely
likeliest|likely
lindens|linden
listened|listen
listener|listen
listens|listen
locale|locales
losses|loss
loved|love
loves|love
loving|~
lupin|lupins
lupines|lupins
lurch|lurches
lurched|lurches
lurching|lurches
magmas|magma
mailed|mail
mailing|mail
mails|mail
manual|manuals
maybes|maybe
mayors|mayor
meaninglessly|meaningless
mediums|medium
memories|memory
memorized|memorize
memorizes|memorize
memorizing|memorize
merrier|merry
merriest|merry
merrily|merry
messaged|~
messages|~
messaging|~
milestones|milestone
milked|milk
milking|milk
milks|milk
milled|mill
milling|mill
mills|mill
mineral|minerals
minimums|minimum
mining|~
mirabelles|mirabelle
mirror|mirrors
mirrored|mirrors
mirroring|mirrors
modernly|modern
moderns|modern
momently|moment
moments|moment
moneyed|money
moneys|money
monstrously|monstrous
morphed|morph
morpher|morph
morphing|morph
morphs|morph
motives|motive
motiving|motive
mounter|mount
mounting|mount
mounts|mount
multiples|multiple
multiply|multiple
muscularly|muscular
narratives|narrative
natively|native
natives|native
navigated|navigating
nod|nods
noised|noises
noising|noises
note|notes
nothings|nothing
odor|odors
odours|odour
offered|offer
offers|offer
okayed|okay
okaying|okay
okays|okay
onwards|onward
ores|ore
oviparously|oviparous
owned|own
owning|own
owns|own
paralyzed|paralyzing
paralyzes|paralyzing
parted|part
parter|part
parting|part
pastry|pastries
patroled|patrols
patroling|patrols
pattern|patterns
patterned|patterns
patterning|patterns
peacefully|peaceful
persons|person
petrified|petrify
petrifies|petrify
petrifying|petrify
pie|pies
pieced|piece
piecer|piece
piecing|piece
pierce|piercing
pierced|piercing
pierces|piercing
piercings|piercing
pleasure|pleasures
pleasured|pleasures
pleasuring|pleasures
plunder|plundered
plundering|plundered
plunders|plundered
pocketed|pocket
pocketer|pocket
pocketing|pocket
pockets|pocket
pointing|pointed
poorer|poor
poorest|poor
poorly|poor
posed|pose
poses|pose
posing|~
potently|potent
practical|practically
predecessor|predecessors
prefers|preferring
presenting|presented
preserved|preserve
preserves|preserve
preserving|preserve
prettied|pretty
prettier|pretty
pretties|pretty
prettiest|pretty
prettily|pretty
prettying|pretty
preyed|prey
preying|prey
preys|prey
priced|price
pricer|price
prices|price
pricing|price
primally|primal
primed|prime
primely|prime
primer|prime
primes|prime
priming|prime
problematics|problematic
problems|problem
proficiently|proficient
proficients|proficient
profoundly|profound
progenies|progeny
push|pushed
pushes|pushed
put|putting
puts|putting
questioned|questions
questioning|questions
raising|raises
rang|ranged
ranging|ranged
recalled|recall
recalling|recall
recalls|recall
receptions|reception
reconnaissances|reconnaissance
recorder|record
recording|record
reflected|reflect
reflections|reflection
reflects|reflect
regenerate|regenerated
regenerates|regenerated
regenerating|regenerated
regenerations|regeneration
regents|regent
regularly|regular
regulars|regular
remarkables|remarkable
remarkably|remarkable
remembered|remember
remembering|remember
removing|removed
repel|repels
resided|residing
respective|respectively
resulted|result
resulting|result
results|result
riches|rich
richest|rich
richly|rich
riddling|riddled
riders|rider
risked|risk
risking|risk
risks|risk
rocs|roc
rotundly|rotund
routed|routes
routing|routes
royally|royal
sailor|sailors
sandboxed|sandbox
sandboxes|sandbox
sandboxing|sandbox
saying|said
scaling|scaled
schooled|school
schooling|school
schools|school
seamless|seamlessly
search|sea
searched|sea
searches|sea
seas|sea
sectored|sector
sectoring|sector
sectors|sector
seldomer|seldom
seldomly|seldom
serenely|serene
serener|serene
serenest|serene
sevens|seven
severally|several
shaded|shade
shader|shade
shades|shade
shading|shade
shafted|shafts
shafting|shafts
shaped|shape
shapely|shape
shaper|shape
shapes|shape
shaping|shape
sheriffs|sheriff
shifted|shift
shifter|shift
shinier|shiny
shiniest|shiny
shinily|shiny
shits|shit
show|shows
showed|shows
showing|shows
shrug|shrugs
sided|side
sidekicks|sidekick
sider|side
siding|~
sigh|sighs
sighed|sighs
sighing|sighs
signal|signals
signaled|signals
signaling|signals
significances|significance
singing|sing
sings|sing
situate|situated
situates|situated
situating|situated
situation|situations
sixes|six
skied|skies
sky|say
skying|~
slayed|slew
slaying|slew
slays|slew
slenderly|slender
slewed|slew
slewing|slew
slews|slew
soloed|solo
soloing|solo
solos|solo
somethings|something
song|songs
sorrier|sorry
sorriest|sorry
sorrily|sorry
spender|spend
spendings|spending
spends|spend
spined|spine
spinely|spine
spines|spine
spot|spotted
spots|spotted
squats|squat
staked|stake
staker|stake
stakes|stake
staking|stake
stalagmite|stalagmites
star|stars
stared|stars
staring|stars
started|start
starter|start
starts|start
stash|stashes
stashing|stashes
steppe|steppes
stickied|sticky
stickier|sticky
stickies|sticky
stickiest|sticky
stickily|sticky
strategy|strategies
strikes|struck
stumbling|stumbled
subsequent|subsequently
subserviently|subservient
sufficiently|sufficient
superstitiously|superstitious
supplied|supplies
supply|supplies
supplying|supplies
surer|sure
surest|sure
surpassed|surpass
surpasses|surpass
surpassing|surpass
surprises|surprised
surprising|surprised
survivals|survival
swathed|swath
swather|swath
swathing|swath
swaths|swath
swooped|swoop
swooping|swoop
swoops|swoop
tailor|tailored
tailoring|tailored
tailors|tailored
tared|tar
taring|tar
tars|tar
tellings|telling
temporaries|temporary
temporarily|temporary
tent|tents
tented|tents
tenting|tents
termed|~
terming|~
thank|thanks
thanked|thanks
thanking|thanks
theme|themes
themed|themes
theming|themes
thicken|thickens
thickened|thickens
thickening|thickens
thorned|thorns
timider|timid
timidest|timid
timidly|timid
tips|tip
todays|today
tolled|toll
tolling|toll
tolls|toll
tomb|tombs
tombed|tombs
tombing|tombs
tomorrows|tomorrow
toxics|toxic
trader|traders
transformations|transformation
transformed|transform
transforming|transform
traversed|traverse
traverses|traverse
traversing|traverse
treetop|treetops
tried|~
trier|~
tries|trimmed
trifle|trifles
trifled|trifles
trifling|trifles
trim|trimmed
trims|trimmed
trolled|troll
troller|troll
trolling|troll
troop|troops
trooped|troops
trooping|troops
turtled|turtle
turtles|turtle
turtling|turtle
unambitiously|unambitious
undergoing|undergo
unicorns|unicorn
uniquely|unique
uniques|unique
units|unit
unparallel|unparalleled
unwelcomed|unwelcome
unwelcoming|unwelcome
urgently|urgent
utilized|utilize
utilizes|utilize
utilizing|utilize
veined|vein
veining|vein
veins|vein
versatilities|versatility
vertically|vertical
verticals|vertical
visibly|visible
visitor|visitors
vulnerability|vulnerabilities
walked|walking
wanted|want
wanting|want
wants|want
warlocks|warlock
warned|warn
warning|warn
warns|warn
waterfalls|waterfall
weighed|weigh
weigher|weigh
weighing|weigh
wielders|wielder
willfully|willful
wineskins|wineskin
wools|wool
worded|~
wording|~
writing|writings
yeahs|yeah
zoned|zone
zones|zone
zoning|zone
""";

        private static IEnumerable<OrcishLexiconEntry> BuildThirtyPageSampleEntries(
            IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var sourceRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sourceOrdinal = 0;

            foreach (var english in ThirtyPageSourceCandidateData.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var root = $"graz-{EncodeTwentyPageOrdinal(sourceOrdinal++)}";
                sourceRoots.Add(english, root);
                var candidate = new OrcishLexiconEntry(
                    english,
                    root,
                    Tags:
                    [
                        "wiki-fodder",
                        "thirty-page-sample",
                        "generated",
                        "review-promoted",
                        "close-form-reviewed",
                        $"family-{english}"
                    ]);

                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }

            var nearKinOrdinal = 0;
            foreach (var line in ThirtyPageNearKinCandidateData.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|', 2, StringSplitOptions.TrimEntries);
                var english = fields[0];
                var sourceEnglish = fields[1];
                var root = string.Equals(sourceEnglish, "~", StringComparison.Ordinal)
                    ? $"graz-nar-{EncodeTwentyPageOrdinal(nearKinOrdinal)}"
                    : sourceRoots[sourceEnglish];
                var candidate = new OrcishLexiconEntry(
                    english,
                    CreateThirtyPageNearKinForm(root, english, nearKinOrdinal++),
                    Tags:
                    [
                        "wiki-fodder",
                        "thirty-page-near-kin",
                        "near-kin",
                        "derived-by-rule",
                        "review-promoted",
                        "close-form-reviewed",
                        $"family-{(sourceEnglish == "~" ? english : sourceEnglish)}"
                    ]);

                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
        }

        private static string CreateThirtyPageNearKinForm(string root, string english, int ordinal)
        {
            var morphology = english.EndsWith("ing", StringComparison.OrdinalIgnoreCase)
                ? "in"
                : english.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
                    ? "ash"
                    : english.EndsWith("ly", StringComparison.OrdinalIgnoreCase)
                        ? "grak"
                        : english.EndsWith("est", StringComparison.OrdinalIgnoreCase)
                            ? "gash"
                            : english.EndsWith("er", StringComparison.OrdinalIgnoreCase)
                                ? "mog"
                                : english.EndsWith('s')
                                    ? "i"
                                    : "kin";

            return $"{root}-{morphology}-{EncodeTwentyPageOrdinal(ordinal)}";
        }

        private const string ThirtyPageFollowupSourceCandidateData = """
abomination
achieving
ad-hoc
ages
aggressive
anemic
annoyance
anyone
appreciation
arable
arcana
archaeologists
automatic
blew
blindness
bored
bounded
brine
brook
buffet
cephalopods
chalkboard
chapel
claws
climes
coastal
companions
concerned
coniferous
constrict
constricting
constriction
cooperate
cornered
counseled
cracks
crest
crevasse
crumbling
culls
curious
dealt
declare
decorated
delayed
demon
deployed
destructor
dinosaurs
displeased
diverted
doom
dreaming
dried
dries
dungeon-master
dungeoneer
dwarven-pantheon
eagle
eagles
eight-armed
eke
elaborately
elements
embossing
engineers
enjoyment
enthusiasm
entity
especially
excitement
exclamations
explain
expressing
far-reaching
farmland
fast
felicitously
felled
finest
fishbones
foolhardy
forsworn
fully
genetic
gleaming
goals
gore
grab
grain
grasslands
grey
griffon
hags
halted
harpies
harpy
herbivorous
herd
hob
illusory
imagination
impulsive
inflict
inflicting
inkwell
insatiable
inspection
instinctively
intention
interest
inventors
jet
jewels
job
jokes
judgement
justice
kin
knowledgeable
laterally
laugh
levitating
levitation
lion
lovers
loyal
lure
lurk
male
mandrake
masterwork
mines
mission
monstrosities
mutton
neat
negate
negated
negates
negation
nestled
neutralize
nevertheless
non-shamanistic
noses
oncoming
opinions
ounce
oversees
owlbear
paralysing
past
paused
pentacles
percentage
perfecting
pinprick
planted
plenty
populations
portcullis
portion
potentially
pranks
predators
principality
prodigious
produce
products
puns
rapacious
rasping
rattle
rattler
realized
reborne
regardless
requires
resides
resistance
restoring
reversed
rushed
savor
scales
seem
serious
sets
severing
shaken
shallows
shine
shines
shrouded
slaves
slightly
smaller
smelted
smiles
solid
southernmost
southwest
specifically
spiralling
spitting
sponge
spurs
squeeze
stretches
styled
swallows
taming
tan
tarot
telekinesis
temperance
tendency
tentacled
thouls
threaten
tidbit
tinkers
toilet
tones
tornados
trample
trampling
travel-time
triarchs
triceratops
uncaring
uncompromising
unnatural
unnoticed
useful
vast
vehicles
venison
victory
viper
wall-of-flame
waters
weights
why
wonder
wonderful
worry
wraps
writ
write-up
written
yards
""";

        private const string ThirtyPageFollowupNearKinCandidateData = """
abomination's|abomination
achievable|achieving
achievement|achieving
achiever|achieving
achievers|achieving
achieves|achieving
age's|ages
aged|ages
aggressively|aggressive
aggressiveness|aggressive
aging|ages
agings|ages
annoyance's|annoyance
annoyances|annoyance
anyone's|anyone
appreciation's|appreciation
archaeologist|archaeologists
archaeologist's|archaeologists
automatic's|automatic
automatics|automatic
blindness's|blindness
blown|blew
bore|bored
bore's|bored
borer|bored
borers|bored
bores|bored
boring|bored
bound's|bounded
bounding|bounded
bounds|bounded
brine's|brine
brook's|brook
brooked|brook
brooking|brook
brooks|brook
buffet's|buffet
buffeted|buffet
buffeting|buffet
buffetings|buffet
buffets|buffet
chalkboard's|chalkboard
chalkboards|chalkboard
chapel's|chapel
chapels|chapel
clawing|claws
clime|climes
clime's|climes
companion|companions
companion's|companions
companionable|companions
concernedly|concerned
constricted|constrict
constriction's|constriction
constrictions|constriction
constrictive|constrict
constricts|constrict
cooperated|cooperate
cooperates|cooperate
cooperating|cooperate
cooperation|cooperate
cooperative|cooperate
corner|cornered
corner's|cornered
cornering|cornered
counsel|counseled
counsel's|counseled
counseling|counseled
counselings|counseled
counsels|counseled
crack|cracks
crack's|cracks
cracked|cracks
cracker|cracks
crackers|cracks
cracking|cracks
crackings|cracks
crackly|cracks
crest's|crest
crested|crest
cresting|crest
crests|crest
crevasse's|crevasse
crevasses|crevasse
crumble|crumbling
crumble's|crumbling
crumbled|crumbling
crumbles|crumbling
cull|culls
cull's|culls
culled|culls
culling|culls
curiously|curious
curiousness|curious
dealing|dealt
declarable|declare
declared|declare
declarer|declare
declarers|declare
declares|declare
declaring|declare
decorate|decorated
decorates|decorated
decorating|decorated
decoration|decorated
decorative|decorated
delay|delayed
delayer|delayed
delayers|delayed
demon's|demon
demons|demon
deploy|deployed
deploying|deployed
deployment|deployed
deploys|deployed
dinosaur|dinosaurs
dinosaur's|dinosaurs
divert|diverted
diverting|diverted
diverts|diverted
doom's|doom
doomed|doom
dooming|doom
dooms|doom
dream|dreaming
dream's|dreaming
dreamed|dreaming
dreamer|dreaming
dreamers|dreaming
dreams|dreaming
drier|dried
driers|dried
driest|dried
dry|dried
dry's|dried
drying|dried
dryly|dried
eagle's|eagle
eked|eke
ekes|eke
eking|eke
elaborate|elaborately
elaborated|elaborately
elaborateness|elaborately
elaborates|elaborately
elaborating|elaborately
elaboration|elaborately
elaborations|elaborately
element|elements
element's|elements
emboss|embossing
embossed|embossing
embosser|embossing
embossers|embossing
embosses|embossing
engineer|engineers
engineer's|engineers
engineered|engineers
engineering|engineers
enjoyment's|enjoyment
enjoyments|enjoyment
enthusiasm's|enthusiasm
enthusiasms|enthusiasm
entities|entity
entity's|entity
especial|especially
excitement's|excitement
excitements|excitement
exclamation|exclamations
exclamation's|exclamations
explained|explain
explaining|explain
explains|explain
express|expressing
express's|expressing
expressed|expressing
expresses|expressing
expressive|expressing
expressly|expressing
farmland's|farmland
farmlands|farmland
fast's|fast
fasted|fast
faster|fast
fastest|fast
fasting|fast
fastness|fast
fasts|fast
felicitous|felicitously
fell's|felled
feller|felled
fellers|felled
fellest|felled
felling|felled
fells|felled
fined|finest
fines|finest
fining|finest
foolhardier|foolhardy
foolhardiest|foolhardy
foolhardiness|foolhardy
forswear|forsworn
forswearing|forsworn
forswears|forsworn
forswore|forsworn
genetics|genetic
gleam|gleaming
gleam's|gleaming
gleamed|gleaming
gleamings|gleaming
gleams|gleaming
goal|goals
goal's|goals
gore's|gore
gored|gore
gores|gore
goring|gore
grab's|grab
grabs|grab
grain's|grain
grained|grain
grains|grain
grassland|grasslands
grassland's|grasslands
grey's|grey
griffon's|griffon
griffons|griffon
hag|hags
hag's|hags
halt|halted
halt's|halted
halters|halted
halting|halted
halts|halted
harpy's|harpies
herd's|herd
herded|herd
herder|herd
herders|herd
herding|herd
herds|herd
hob's|hob
hobs|hob
imagination's|imagination
imaginations|imagination
impulsively|impulsive
impulsiveness|impulsive
inflicted|inflict
inflictive|inflict
inkwell's|inkwell
inkwells|inkwell
inspection's|inspection
inspections|inspection
instinctive|instinctively
intention's|intention
intentions|intention
interest's|interest
interested|interest
interests|interest
inventor|inventors
inventor's|inventors
jet's|jet
jets|jet
jewel|jewels
jewel's|jewels
jeweled|jewels
jeweler|jewels
jewelers|jewels
jeweling|jewels
job's|job
jobs|job
joke|jokes
joke's|jokes
joked|jokes
joker|jokes
jokers|jokes
joking|jokes
justice's|justice
justices|justice
kin's|kin
lateral|laterally
lateral's|laterally
laterals|laterally
laugh's|laugh
laughable|laugh
laughed|laugh
laughing|laugh
levitate|levitating
levitated|levitating
levitates|levitating
levitation's|levitation
lion's|lion
lions|lion
lovable|lovers
love's|lovers
lovely|lovers
loyalest|loyal
loyally|loyal
lure's|lure
lured|lure
lures|lure
luring|lure
lurked|lurk
lurker|lurk
lurkers|lurk
lurks|lurk
male's|male
maleness|male
males|male
mandrake's|mandrake
mandrakes|mandrake
masterwork's|masterwork
masterworks|masterwork
mine|mines
mine's|mines
mission's|mission
missions|mission
monstrosity|monstrosities
monstrosity's|monstrosities
mutton's|mutton
neaten|neat
neatens|neat
neater|neat
neatest|neat
neatly|neat
neatness|neat
negating|negate
negation's|negation
negations|negate
negative|negate
nestle|nestled
nestles|nestled
nestling|nestled
nestlings|nestled
neutralized|neutralize
neutralizer|neutralize
neutralizers|neutralize
neutralizes|neutralize
neutralizing|neutralize
nose|noses
nose's|noses
nosed|noses
nosing|noses
ounce's|ounce
ounces|ounce
oversee|oversees
overseers|oversees
past's|past
pasts|past
pause|paused
pause's|paused
pausing|paused
pentacle|pentacles
pentacle's|pentacles
percentage's|percentage
percentages|percentage
perfect's|perfecting
perfected|perfecting
perfecter|perfecting
perfectest|perfecting
perfectness|perfecting
perfects|perfecting
pinprick's|pinprick
pinpricks|pinprick
plant's|planted
planter|planted
planters|planted
planting|planted
plantings|planted
plenty's|plenty
portcullis's|portcullis
portcullises|portcullis
portion's|portion
portioned|portion
portioning|portion
portions|portion
potential|potentially
potential's|potentially
potentials|potentially
prank|pranks
prank's|pranks
predator|predators
predator's|predators
principalities|principality
principality's|principality
prodigiously|prodigious
produced|produce
producer|produce
producers|produce
producing|produce
pun|puns
pun's|puns
rapaciously|rapacious
rapaciousness|rapacious
rasp|rasping
rasp's|rasping
rasped|rasping
rasps|rasping
rattle's|rattle
rattled|rattle
rattler's|rattler
rattlers|rattle
rattles|rattle
rattling|rattle
rattlings|rattle
realizable|realized
realize|realized
realizes|realized
realizing|realized
rebear|reborne
rebearing|reborne
rebore|reborne
reborn|reborne
resistance's|resistance
resistances|resistance
rush|rushed
rush's|rushed
rusher|rushed
rushers|rushed
rushes|rushed
rushing|rushed
savor's|savor
savored|savor
savors|savor
scale|scales
seemed|seem
seems|seem
seriously|serious
seriousness|serious
set|sets
set's|sets
sever|severing
severed|severing
severest|severing
severs|severing
shake|shaken
shakes|shaken
shaking|shaken
shallow's|shallows
shallower|shallows
shallowest|shallows
shallowly|shallows
shallowness|shallows
shine's|shine
shining|shine
shone|shine
shook|shaken
shroud|shrouded
shroud's|shrouded
shrouding|shrouded
shrouds|shrouded
slave|slaves
slave's|slaves
slaved|slaves
slaver|slaves
slavers|slaves
slaving|slaves
slight|slightly
slight's|slightly
slighted|slightly
slighter|slightly
slightest|slightly
slighting|slightly
slightness|slightly
slights|slightly
small's|smaller
smallest|smaller
smallness|smaller
smalls|smaller
smelt|smelted
smelt's|smelted
smelter|smelted
smelters|smelted
smelting|smelted
smelts|smelted
smile|smiles
smile's|smiles
smiling|smiles
solid's|solid
solider|solid
solidest|solid
solidly|solid
solidness|solid
solids|solid
southwest's|southwest
southwester|southwest
southwesters|southwest
southwests|southwest
spat|spitting
sponge's|sponge
sponged|sponge
sponger|sponge
spongers|sponge
sponges|sponge
sponging|sponge
spur|spurs
spur's|spurs
squeezable|squeeze
squeeze's|squeeze
squeezed|squeeze
squeezer|squeeze
squeezers|squeeze
squeezes|squeeze
squeezing|squeeze
stretch|stretches
stretch's|stretches
stretchable|stretches
stretched|stretches
stretcher|stretches
stretchers|stretches
stretching|stretches
style|styled
styles|styled
styling|styled
swallow's|swallows
swallowed|swallows
swallowing|swallows
tamable|taming
tame|taming
tamed|taming
tamely|taming
tameness|taming
tamer|taming
tamers|taming
tames|taming
tamest|taming
tan's|tan
tans|tan
tarot's|tarot
tarots|tarot
telekinesis's|telekinesis
temperance's|temperance
tendencies|tendency
tendency's|tendency
tentacle's|tentacled
threatened|threaten
threatening|threaten
tidbit's|tidbit
tidbits|tidbit
tinker|tinkers
tinker's|tinkers
tinkered|tinkers
tinkerer|tinkers
tinkerers|tinkers
tinkering|tinkers
toilet's|toilet
toileted|toilet
toileting|toilet
toilets|toilet
tone|tones
toned|tones
toner|tones
toners|tones
toning|tones
tornado|tornados
trample's|trample
trampled|trample
trampler|trample
tramplers|trample
tramples|trample
triceratops's|triceratops
uncompromisingly|uncompromising
usefully|useful
usefulness|useful
vast's|vast
vaster|vast
vastest|vast
vastly|vast
vastness|vast
vasts|vast
vehicle|vehicles
vehicle's|vehicles
venison's|venison
victories|victory
victory's|victory
viper's|viper
vipers|viper
weight's|weights
weighted|weights
weighting|weights
weightings|weights
why's|why
wonder's|wonder
wondered|wonder
wonderfully|wonderful
wonderfulness|wonderful
wondering|wonder
wonderment|wonder
wonders|wonder
worried|worry
worrier|worry
worriers|worry
worries|worry
worry's|worry
worrying|worry
worryings|worry
wrap|wraps
wrapped|wraps
wrapping|wraps
writ's|writ
writable|writ
write|writ
writer|writ
writers|writ
writes|writ
writs|writ
wrote|writ
yard|yards
yard's|yards
""";

        private static IEnumerable<OrcishLexiconEntry> BuildThirtyPageFollowupEntries(
            IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var sourceRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sourceOrdinal = 0;

            foreach (var english in ThirtyPageFollowupSourceCandidateData.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var root = $"draz-{EncodeTwentyPageOrdinal(sourceOrdinal++)}";
                sourceRoots.Add(english, root);
                var candidate = new OrcishLexiconEntry(
                    english,
                    root,
                    Tags:
                    [
                        "wiki-fodder",
                        "thirty-page-followup",
                        "generated",
                        "review-promoted",
                        "close-form-reviewed",
                        $"family-{english}"
                    ]);

                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }

            var nearKinOrdinal = 0;
            foreach (var line in ThirtyPageFollowupNearKinCandidateData.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|', 2, StringSplitOptions.TrimEntries);
                var english = fields[0];
                var sourceEnglish = fields[1];
                var root = sourceRoots[sourceEnglish];
                var candidate = new OrcishLexiconEntry(
                    english,
                    CreateThirtyPageNearKinForm(root, english, nearKinOrdinal++),
                    Tags:
                    [
                        "wiki-fodder",
                        "thirty-page-followup-near-kin",
                        "near-kin",
                        "derived-by-rule",
                        "review-promoted",
                        "close-form-reviewed",
                        $"family-{sourceEnglish}"
                    ]);

                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
        }


        private const string TwentyPageSourceCandidateData = """
ball
bruce
banville
charmed
doubled
scryer
viewed
abstinence
attached
bancroft
billworth
clarity
considerable
devices
elite
enchant
existence
hostile
justhand
leap
master
mordicar
plants
scene
spy
thieves
threads
turgen
vul
wishes
agent
angry
antiquities
ants
applied
april
armies
bass
became
beer-brewer
bees
beetles
berric
brandished
brandishing
cavalry
celaena
centipedes
certain
challenging
clairaudience
clockwork
comely
consciousness
continue
contracted
contradict
controlled
creepers
crusaders
darkeyes
deadly
deals
debate
decades
defend
dependent
descend
desired
detects
development
devouring
devours
difficult
difficulty
discreet
distances
diving
drawn
drinking
drinks
dursk
easy
entangle
entire
equal
exercising
exists
exiting
expert
extreme
extremely
facilitates
fact
fails
familiarity
fellow
fighter-priests
finding
finds
fits
flyers
fool
four
friendship
gauntlet
gemstone
giants
grandson
guildmaster
harmful
hear
historians
horizontally
hornets
hours
ignored
illness
impossible
ineffectual
inglorious
inverted
investiture
ironguard
issenda
jumping
keeper
kettle-belly
kronos
lances
leaping
likable
locomotion
locusts
lord
mask
mastermind
maven
mega
midstream
mix
modifier
modifiers
motte
mustache
nephew
nest
normal-looking
obey
occurred
opponents
owner
penalties
perceive
pictures
precious-metal
profits
prohibition
prostitution
queen
quickly
quite
rates
representative
residuum
resisting
reteps
reveals
riding
run
sardothien
scryers
sewers
share
shipments
shop
sneers
somehow
soul-bound
spiritual
spring
steeder
stomped
stonehill
stubbornly
swatting
thin
thurgan
tiny
tirelessness
tiring
tolerated
trading
travelling
unclassed
vanishes
wasting
well-off
well-trained
whole
""";

        private const string TwentyPageNearKinFamilyData = """
abstinence|abstain,abstained,abstaining,abstains,abstinences
agent|agents
angry|angrier,angriest,angrily
antiquities|antiquity
ants|ant
armies|army
attached|attach,attaches,attaching
ball|balled,balling,balls
bass|basses,bassing
became|becomes,becoming
beer-brewer|brew,brewer,brewers
bees|bee
beetles|beetle
brandished|brandish,brandishes
cavalry|cavalries
centipedes|centipede
certain|certainly,certainty
challenging|challenge,challenged,challenges
charmed|charming,charms
clairaudience|clairaudiences,clairaudient
clockwork|clockworks
comely|comelier,comeliest,comelily
consciousness|conscious,consciously
considerable|considerably
continue|continues
contradict|contradicted,contradicting,contradicts
creepers|creeper
crusaders|crusader
darkeyes|darkeye
deadly|deadlier,deadliest
deals|deal
debate|debated,debates,debating
defend|defended,defending,defends
dependent|dependently,dependents
descend|descended,descending,descends
detects|detecting
development|developments
devouring|devour,devoured
devours|devour
difficulty|difficulties
discreet|discreetly
doubled|doubles,doubling
drawn|draw,drawing,drew
drinking|drank,drink,drinkings,drunk
drinks|drank,drink,drunk
easy|easier,easiest
elite|elites
enchant|enchanting,enchantment,enchants
entangle|entangles,entangling
entire|entirely
equal|equaled,equaling,equality,equally,equals
existence|exist,existed,existences,existing
exists|exist,existed,existing
exiting|exit,exited
expert|expertly,experts
extreme|extremes
facilitates|facilitate,facilitated,facilitating
fact|facts
fails|fail,failed,failing
familiarity|familiarities,familiarly
fighter-priests|fighter-priest
finding|findings
fits|fit
flyers|flyer
fool|fooled,fooling,foolish,foolishly,fools
four|fours
friendship|friendships
gauntlet|gauntlets
gemstone|gemstones
grandson|grandsons
guildmaster|guildmasters
harmful|harmfully
hear|heard,hearing,hears
historians|historian
horizontally|horizontal
hornets|hornet
hostile|hostiles,hostility
hours|hour
illness|ill,illnesses,ills
impossible|impossibly
ineffectual|ineffectually
inglorious|ingloriously
investiture|invest,invested,investing,investitures,invests
ironguard|ironguards
jumping|jump,jumped,jumps
keeper|keepers
kettle-belly|kettle-bellies
lances|lance
leap|leaped,leaps,leapt
leaping|leaped,leaps,leapt
likable|likably
locomotion|locomote,locomotions
locusts|locust
lord|lorded,lording,lords
mask|masked,masking,masks
master|masterly,masters,mastery
mastermind|masterminded,masterminding,masterminds
maven|mavens
mix|mixed,mixes,mixing
motte|mottes
mustache|mustaches
nephew|nephews
nest|nested,nesting,nests
normal-looking|normally
obey|obeyed,obeying,obeys
occurred|occur,occurring,occurs
opponents|opponent
owner|owners,ownership
perceive|perceived,perceives,perceiving
pictures|picture
precious-metal|metals,precious
profits|profit
prohibition|prohibit,prohibited,prohibiting,prohibitions,prohibits
prostitution|prostitute,prostituted,prostitutes,prostituting,prostitutions
queen|queened,queening,queens
quickly|quick,quicker,quickest
representative|represent,representatively,representatives,represented,representing,represents
residuum|residuums
resisting|resisted,resists
reveals|reveal,revealed,revealing
riding|ridden,ride,rides,ridings,rode
run|ran,running,runs
scene|scenes
scryer|scried,scries,scry
sewers|sewer
shipments|shipment
shop|shopped,shopping,shops
sneers|sneer,sneered,sneering
soul-bound|bind,bound
spiritual|spiritually,spirituals
spring|sprang,springing,springs,sprung
spy|spied,spies,spying
stomped|stomp,stomping,stomps
stubbornly|stubborn
swatting|swat,swats,swatted
thin|thinly,thinned,thinner,thinnest,thinning,thins
tiny|tinier,tiniest,tinily
tirelessness|tireless,tirelessly
tiring|tire,tires
tolerated|tolerate,tolerates,tolerating
trading|traded
travelling|traveled,travelled
vanishes|vanish,vanished,vanishing
viewed|view,viewing,views
wasting|wasted,wastes
well-off|wealthy
well-trained|train,trained
whole|wholes,wholly
wishes|wish,wished,wishing
""";

        private static IEnumerable<OrcishLexiconEntry> BuildTwentyPageSampleEntries(
            IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var sourceRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var emittedEnglish = acceptedEntries
                .Select(static entry => entry.English)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sourceOrdinal = 0;

            foreach (var english in TwentyPageSourceCandidateData.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var root = $"krul-{EncodeTwentyPageOrdinal(sourceOrdinal++)}";
                sourceRoots.Add(english, root);
                var candidate = new OrcishLexiconEntry(
                    english,
                    root,
                    Tags:
                    [
                        "wiki-fodder",
                        "twenty-page-sample",
                        "generated",
                        "review-promoted",
                        "close-form-reviewed",
                        $"family-{english}"
                    ]);

                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                emittedEnglish.Add(english);
                yield return candidate;
            }

            var nearKinOrdinal = 0;
            foreach (var line in TwentyPageNearKinFamilyData.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|', 2, StringSplitOptions.TrimEntries);
                var sourceEnglish = fields[0];
                var root = sourceRoots[sourceEnglish];

                foreach (var english in fields[1].Split(
                             ',',
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!emittedEnglish.Add(english))
                    {
                        continue;
                    }

                    var candidate = new OrcishLexiconEntry(
                        english,
                        CreateTwentyPageNearKinForm(root, english, nearKinOrdinal++),
                        Tags:
                        [
                            "wiki-fodder",
                            "twenty-page-near-kin",
                            "near-kin",
                            "derived-by-rule",
                            "review-promoted",
                            "close-form-reviewed",
                            $"family-{sourceEnglish}"
                        ]);

                    OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                    acceptedEntries.Add(candidate);
                    yield return candidate;
                }
            }
        }

        private static string CreateTwentyPageNearKinForm(string root, string english, int ordinal)
        {
            var morphology = english.EndsWith("ing", StringComparison.OrdinalIgnoreCase)
                ? "in"
                : english.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
                    ? "ash"
                    : english.EndsWith("ly", StringComparison.OrdinalIgnoreCase)
                        ? "grak"
                        : english.EndsWith("est", StringComparison.OrdinalIgnoreCase)
                            ? "gash"
                            : english.EndsWith("er", StringComparison.OrdinalIgnoreCase)
                                ? "mog"
                                : english.EndsWith('s')
                                    ? "i"
                                    : "kin";

            return $"{root}-{morphology}-{EncodeTwentyPageOrdinal(ordinal)}";
        }

        private static string EncodeTwentyPageOrdinal(int value)
        {
            var syllables = new[]
            {
                "agh", "burz", "dak", "drak", "gar", "gash", "gor", "grak",
                "grod", "krag", "mok", "narg", "ruk", "skar", "thog", "varg"
            };

            return string.Join(
                "-",
                syllables[(value >> 8) & 15],
                syllables[(value >> 4) & 15],
                syllables[value & 15]);
        }

        private const string FifteenPageSampleLexiconData = """
thoughts|thogi-kin-agh-agh-agh|thoughts|base
background|vosh-agh-agh-grak|background|base
crynwyth|vosh-agh-drak-gash|crynwyth|base
underworld|vosh-agh-thog-gor|underworld|base
elowen|vosh-agh-gar-gor|elowen|base
brotherhood|mokh-gash-thog-kin-agh-agh-gash|brotherhood|base
connection|vosh-agh-dak-skar|connection|base
portal|vosh-agh-krag-thog|portal|base
breanna|vosh-agh-burz-krag|breanna|base
fungus|gruul-thrum-rukhuki-kin-agh-agh-krag|fungus|base
communication|vosh-agh-dak-grak|communication|base
tangsin|vosh-agh-skar-gor|tangsin|base
mild|vosh-agh-grod-burz|mild|base
contain|ik-darguru-kin-agh-agh-skar|contain|base
mentor|vosh-agh-grod-agh|mentor|base
means|vosh-agh-grak-thog|means|base
motivation|vosh-agh-grod-gash|motivation|base
gnome|vosh-agh-gash-skar|gnome|base
fantastic|vosh-agh-gash-burz|fantastic|base
helm|vosh-agh-gor-burz|helm|base
matter|vosh-agh-grak-ruk|matter|base
gnomes|vosh-agh-gash-skari-kin-agh-burz-gash|gnome|base
hypertension|vosh-agh-gor-krag|hypertension|base
long-lost|nul-laguk-kin-agh-burz-grak|long-lost|base
seeking|vosh-agh-narg-skar|seeking|base
outpost|vosh-agh-krag-burz|outpost|base
orcus|vosh-agh-krag-agh|orcus|base
personal|vosh-agh-krag-gar|personal|base
prepared|vosh-agh-mok-dak|prepared|base
possesses|vosh-agh-krag-vargur-kin-agh-burz-skar|possessed|base
sacred|vosh-agh-narg-krag|sacred|base
summoning|vosh-agh-skar-aghin-kin-agh-burz-varg|summon|base
pronounced|vosh-agh-mok-gor|pronounced|base
role|vosh-agh-narg-grak|role|base
series|vosh-agh-narg-thog|series|base
scenario|vosh-agh-narg-narg|scenario|base
talents|vosh-agh-skar-gash|talents|base
telepathy|vosh-agh-skar-mok|telepathy|base
territory|vosh-agh-skar-ruk|territory|base
tasked|zhar-agh-burz-rukash-kin-agh-dak-grak|tasked|base
valuables|vosh-agh-thog-krag|valuables|base
acre|cbd-devis-thog-kin-agh-dak-krag|acre|base
always|vosh-agh-agh-dak|always|base
allows|kur-dargur-kin-agh-dak-narg|allows|base
unknown|thogash-thog-kin-agh-dak-ruk|unknown|base
alteration|vosh-agh-agh-burz|alteration|base
appear|oglaruru-kin-agh-dak-thog|appear|base
allergy|vosh-agh-agh-agh|allergy|base
appearance|oglarur-thog-kin-agh-drak-agh|appearance|base
approached|lag-thog-krag-burzash-kin-agh-drak-burz|approached|base
arrive|vosh-agh-agh-drak|arrive|base
attempt|vosh-agh-agh-gar|attempt|base
banishing|vosh-agh-agh-nargin-kin-agh-drak-gar|banished|base
bah-mooth|vosh-agh-agh-mok|bah-mooth|base
banished|vosh-agh-agh-narg|banished|base
badly|vosh-agh-agh-grod|badly|base
badmouth|vosh-agh-agh-krag|badmouth|base
barriers|vosh-agh-agh-ruk|barriers|base
awaiting|vosh-agh-agh-gash|awaiting|base
awakened|vosh-agh-agh-gor|awakened|base
beautifully|vosh-agh-agh-skar|beautifully|base
begin|ashdak-hekashu-kin-agh-drak-skar|begin|base
behave|vosh-agh-agh-varg|behave|base
beauty|vosh-agh-agh-thog|beauty|base
bodyguards|vosh-agh-burz-gor|bodyguards|base
behavior|vosh-agh-burz-agh|behavior|base
beneficial|vosh-agh-burz-dak|beneficial|base
blend|vosh-agh-burz-gash|blend|base
belies|vosh-agh-burz-burz|belies|base
blocks|bant-muri-kin-agh-gar-gash|blocks|base
bidding|vosh-agh-burz-gar|bidding|base
bestial|vosh-agh-burz-drak|bestial|base
brace|vosh-agh-burz-grak|brace|base
cascading|vosh-agh-burz-mok|cascading|base
brazier|vosh-agh-burz-grod|brazier|base
broadly|ti-mokh-grak-kin-agh-gar-narg|broadly|base
castle|vosh-agh-burz-ruk|castle|base
chronology|vosh-agh-burz-varg|chronology|base
cases|vosh-agh-burz-narg|cases|base
clairvoyance|vosh-agh-dak-burz|clairvoyance|base
civilisation|vosh-agh-dak-agh|civilisation|base
cleanse|vosh-agh-dak-dak|cleanse|base
charisma|vosh-agh-burz-thog|charisma|base
censer|vosh-agh-burz-skar|censer|base
closing|vosh-agh-dak-drak|closing|base
comprehension|vosh-agh-dak-krag|comprehension|base
comprehensible|vosh-agh-dak-grod|comprehensible|base
combined|vosh-agh-dak-gar|combined|base
context|vosh-agh-dak-varg|context|base
comprised|vosh-agh-dak-mok|comprised|base
concentrate|vosh-agh-dak-narg|concentrate|base
conversely|narg-mokru-grak-kin-agh-gash-narg|conversely|base
commonly|mokhuk-grrt-karn-grak-kin-agh-gash-ruk|commonly|base
consult|vosh-agh-dak-thog|consult|base
conjecture|vosh-agh-dak-ruk|conjecture|base
commitment|vosh-agh-dak-gash|commitment|base
committed|vosh-agh-dak-gor|committed|base
corrupted|vosh-agh-drak-burz|corrupted|base
corporeal|vosh-agh-drak-agh|corporeal|base
creating|vosh-agh-drak-drak|creating|base
crucial|vosh-agh-drak-gar|crucial|base
cunning|vosh-agh-drak-gor|cunning|base
crafted|vosh-agh-drak-dak|crafted|base
decide|vosh-agh-drak-grak|decide|base
darkest|burzuk-gash-kin-agh-gor-grod|darkest|base
dedication|draku-mur-kaag-tuk-thog-kin-agh-gor-krag|dedication|base
dedicated|vosh-agh-drak-krag|dedicated|base
deceased|morzukash-kin-agh-gor-narg|deceased|base
deciphering|vosh-agh-drak-grod|deciphering|base
deposed|vosh-agh-drak-narg|deposed|base
denotes|vosh-agh-drak-mok|denotes|base
doorway|vosh-agh-gar-dak|doorway|base
described|vosh-agh-drak-ruk|described|base
despite|vosh-agh-drak-thog|despite|base
destination|vosh-agh-drak-varg|destination|base
designing|vosh-agh-drak-skar|designing|base
distant|vosh-agh-gar-burz|distant|base
develop|vosh-agh-gar-agh|develop|base
destroying|brakash-tiin-kin-agh-grak-gor|destroying|base
determination|thog-oglaru-thog-kin-agh-grak-grak|determination|base
dormant|vosh-agh-gar-drak|dormant|base
dragonkind|rukh-vark-mog-thog-kin-agh-grak-krag|dragonkind|base
due|vosh-agh-gar-gar|due|base
elven|elf-thog-kin-agh-grak-narg|elven|base
enables|vosh-agh-gar-grod|enables|base
encumbrance|vosh-agh-gar-krag|encumbrance|base
embroidery|vosh-agh-gar-grak|embroidery|base
electrum|vosh-agh-gar-gash|electrum|base
engenders|vosh-agh-gar-mok|engenders|base
ethereal|vosh-agh-gar-skar|ethereal|base
environment|vosh-agh-gar-narg|environment|base
explore|oglar-lagashu-kin-agh-grod-drak|explore|base
establish|vosh-agh-gar-ruk|establish|base
euphoric|vosh-agh-gar-thog|euphoric|base
facing|vosh-agh-gar-varg|facing|base
faithful|vosh-agh-gash-agh|faithful|base
feelings|grodhi-kin-agh-grod-grod|feelings|base
fierce|vosh-agh-gash-drak|fierce|base
fiction|vosh-agh-gash-dak|fiction|base
galduhr|vosh-agh-gash-grak|galduhr|base
followers|vosh-agh-gash-gar|followers|base
formerly|vosh-agh-gash-gash|formerly|base
front|vosh-agh-gash-gor|front|base
game|vosh-agh-gash-grod|game|base
gateway|vosh-agh-gash-mok|gateway|base
ghosts|vosh-agh-gash-narg|ghosts|base
gargoyle|vosh-agh-gash-krag|gargoyle|base
hails|vosh-agh-gash-varg|hails|base
half-elven|elfuk-kin-agh-krag-gar|half-elven|base
ginseng|vosh-agh-gash-ruk|ginseng|base
gravity|vosh-agh-gash-thog|gravity|base
guardians|zog-thraki-kin-agh-krag-grak|guardians|base
half-elves|elfi-kin-agh-krag-grod|half-elven|base
harnessing|vosh-agh-gor-agh|harnessing|base
helmet|vosh-agh-gor-dak|helmet|base
harmed|brak-thog-morzash-kin-agh-krag-narg|harmed|base
helped|vosh-agh-gor-drak|helped|base
helping|vosh-agh-gor-drakin-kin-agh-krag-skar|helped|base
hideous|vosh-agh-gor-gar|hideous|base
honor|grak-tur-ti-mog-kin-agh-krag-varg|honor|base
hires|dravu-mogi-kin-agh-mok-agh|hires|base
horned|vosh-agh-gor-gash|horned|base
horrible|vark-thog-mog-thog-kin-agh-mok-dak|horrible|base
hurt|vosh-agh-gor-grak|hurt|base
hyoscyamine-containing|vosh-agh-gor-grod|hyoscyamine-containing|base
humor|vosh-agh-gor-gor|humor|base
inanimate|vosh-agh-gor-narg|inanimate|base
include|vosh-agh-gor-ruk|include|base
important|vosh-agh-gor-mok|important|base
includes|vosh-agh-gor-rukur-kin-agh-mok-krag|include|base
incorporeal|vosh-agh-gor-skar|incorporeal|base
infiltrating|vosh-agh-gor-thog|infiltrating|base
inspiration|vosh-agh-grak-burz|inspiration|base
initiate|vosh-agh-gor-varg|initiate|base
inquisitive|vosh-agh-grak-agh|inquisitive|base
inspire|vosh-agh-grak-dak|inspire|base
intestinal|vosh-agh-grak-drak|intestinal|base
intricate|vosh-agh-grak-gar|intricate|base
invasion|vosh-agh-grak-gash|invasion|base
involve|vosh-agh-grak-gor|involve|base
kidnappers|zhar-agh-grak-varg-mog-mogi-kin-agh-narg-gar|kidnappers|base
kind|vosh-agh-grak-grak|kind|base
lawfuller|darg-bibuk-ti-mog-kin-agh-narg-gor|lawfuller|base
leaf|vosh-agh-grak-grod|leaf|base
legend|vosh-agh-grak-krag|legend|base
locating|dakukashin-kin-agh-narg-krag|locating|base
looming|nak-oglarurin-kin-agh-narg-mok|looming|base
mainly|murk-tiuk-grak-kin-agh-narg-narg|mainly|base
majority|vosh-agh-grak-mok|majority|base
manipulation|vosh-agh-grak-narg|manipulation|base
maturity|vosh-agh-grak-skar|maturity|base
members|mokh-mog-mogi-kin-agh-narg-varg|members|base
meddling|vosh-agh-grak-varg|meddling|base
mixture|vosh-agh-grod-dak|mixture|base
moon|vosh-agh-grod-drak|moon|base
mortal|vosh-agh-grod-gar|mortal|base
mummies|vosh-agh-grod-gori-kin-agh-ruk-gar|mummy|base
mummy|vosh-agh-grod-gor|mummy|base
mundane|vosh-agh-grod-grak|mundane|base
mythology|vosh-agh-grod-grod|mythology|base
negotiating|vosh-agh-grod-krag|negotiating|base
notable|oglar-ti-thog-kin-agh-ruk-krag|notable|base
nergal|vosh-agh-grod-mok|nergal|base
one-offs|vosh-agh-grod-skar|one-offs|base
obstructions|vosh-agh-grod-narg|obstructions|base
off-color|vosh-agh-grod-ruk|off-color|base
one-way|vosh-agh-grod-thog|one-way|base
optimism|vosh-agh-grod-varg|optimism|base
outskirts|vosh-agh-krag-dak|outskirts|base
partys|lag-mokhi-kin-agh-skar-burz|partys|base
overlooked|vosh-agh-krag-drak|overlooked|base
personality|vosh-agh-krag-gash|personality|base
phandalin|vosh-agh-krag-gor|phandalin|base
pivotal|vosh-agh-krag-grak|pivotal|base
placed|dakash-kin-agh-skar-gor|placed|base
plain|vosh-agh-krag-grod|plain|base
plan|vosh-agh-krag-krag|plan|base
plans|vosh-agh-krag-kragi-kin-agh-skar-krag|plan|base
planes|dak-thog-muri-kin-agh-skar-mok|planes|base
platinum|vosh-agh-krag-mok|platinum|base
plays|vosh-agh-krag-narg|plays|base
possessed|vosh-agh-krag-varg|possessed|base
pleasant|vosh-agh-krag-ruk|pleasant|base
popular|vosh-agh-krag-skar|popular|base
possibly|vosh-agh-mok-agh|possibly|base
practitioners|vosh-agh-mok-burz|practitioners|base
prevent|vosh-agh-mok-drak|prevent|base
previously|ash-dakuruk-grak-kin-agh-thog-drak|previously|base
properly|vosh-agh-mok-grak|properly|base
promising|vosh-agh-mok-gash|promising|base
profanity|vosh-agh-mok-gar|profanity|base
protective|gor-thog-in-thog-kin-agh-thog-grak|protective|base
prowess|vosh-agh-mok-grod|prowess|base
raven-black|vosh-agh-mok-mok|raven-black|base
reactions|vosh-agh-mok-ruk|reactions|base
quick-witted|vosh-agh-mok-krag|quick-witted|base
ray|vosh-agh-mok-narg|ray|base
reanimated|vosh-agh-mok-skar|reanimated|base
referees|darg-narg-mog-tii-kin-agh-thog-thog|referees|base
regain|vosh-agh-mok-thog|regain|base
relic|vosh-agh-mok-varg|relic|base
relieve|vosh-agh-narg-agh|relieve|base
remain|tukur-morziu-kin-agh-varg-dak|remain|base
reminding|vosh-agh-narg-burz|reminding|base
remained|tukur-morziash-kin-agh-varg-gar|remain|base
resettled|vosh-agh-narg-drak|resettled|base
remote|vosh-agh-narg-dak|remote|base
reverted|lagash-zorn-dok-kin-agh-varg-grak|reverted|base
responsibility|tukur-darg-thog-kin-agh-varg-grod|responsibility|base
roleplaying|vosh-agh-narg-grakin-kin-agh-varg-krag|role|base
reward|vosh-agh-narg-gar|reward|base
rods|vosh-agh-narg-gor|rods|base
rivals|vosh-agh-narg-gash|rivals|base
sack|vosh-agh-narg-grod|sack|base
scarlet|vosh-agh-narg-mok|scarlet|base
scenarios|vosh-agh-narg-nargi-kin-agh-varg-varg|scenario|base
scout|vosh-agh-narg-ruk|scout|base
scouting|vosh-agh-narg-rukin-kin-burz-agh-burz|scout|base
sending|lag-dargashin-kin-burz-agh-dak|sends|base
sends|lag-dargashur-kin-burz-agh-drak|sends|base
serpents|vosh-agh-narg-varg|serpents|base
shortly|thrum-bantuk-grak-kin-burz-agh-gash|shortly|base
signifying|vosh-agh-ruk-burz|signifying|base
shrine|vosh-agh-ruk-agh|shrine|base
similar|vosh-agh-ruk-dak|similar|base
skilled|mauk-hekash-kin-burz-agh-krag|skilled|base
smoked|vosh-agh-ruk-drak|smoked|base
sold|dravash-dok-kin-burz-agh-narg|sold|base
solutions|grod-lag-bibi-kin-burz-agh-ruk|solutions|base
spellcaster|vosh-agh-ruk-gash|spellcaster|base
specializing|vosh-agh-ruk-gar|specializing|base
spheres|vosh-agh-ruk-gor|spheres|base
spilled|vosh-agh-ruk-grak|spilled|base
sporebane|vosh-agh-ruk-grod|sporebane|base
staves|bant-zol-mogi-kin-burz-burz-dak|staves|base
stems|vosh-agh-ruk-krag|stems|base
step|vosh-agh-ruk-mok|step|base
strengths|yankuki-kin-burz-burz-gash|strengths|base
stronghold|vosh-agh-ruk-ruk|stronghold|base
striking|vosh-agh-ruk-narg|striking|base
study|vosh-agh-ruk-skar|study|base
studying|vosh-agh-ruk-skarin-kin-burz-burz-krag|study|base
successive|grod-lag-thoguk-kin-burz-burz-mok|successive|base
suffering|vosh-agh-ruk-thog|suffering|base
suggested|vosh-agh-ruk-varg|suggested|base
summon|vosh-agh-skar-agh|summon|base
supernatural|vosh-agh-skar-dak|supernatural|base
summoners|vosh-agh-skar-burz|summoners|base
summoned|vosh-agh-skar-aghash-kin-burz-dak-agh|summon|base
symptoms|vosh-agh-skar-drak|symptoms|base
taken|dravkuk-krag-flit-kin-burz-dak-dak|taken|base
taste|vosh-agh-skar-grak|taste|base
tabletop|vosh-agh-skar-gar|tabletop|base
technology|vosh-agh-skar-krag|technology|base
tease|vosh-agh-skar-grod|tease|base
thirst|vosh-agh-skar-skar|thirst|base
tends|vosh-agh-skar-narg|tends|base
telepathic|vosh-agh-skar-mokuk-kin-burz-dak-krag|telepathy|base
tinderbox|vosh-agh-skar-varg|tinderbox|base
thrown|flituk-kin-burz-dak-narg|thrown|base
throughout|vosh-agh-skar-thog|throughout|base
topsy|vosh-agh-thog-dak|topsy|base
tragedy|morz-thog-ti-thog-kin-burz-dak-thog|tragedy|base
tipsy|vosh-agh-thog-agh|tipsy|base
tobacco|vosh-agh-thog-burz|tobacco|base
treating|dravur-grodin-kin-burz-drak-burz|treatment|base
treatment|dravur-grod-thog-kin-burz-drak-dak|treatment|base
unaffected|hekash-varash-kin-burz-drak-drak|unaffected|base
trigger|vosh-agh-thog-drak|trigger|base
uncharted|vosh-agh-thog-gar|uncharted|base
understanding|vosh-agh-thog-gash|understanding|base
unwavering|vosh-agh-thog-grod|unwavering|base
unfortunate|mauk-drav-thog-thog-kin-burz-drak-grod|unfortunate|base
unifying|vosh-agh-thog-grak|unifying|base
vampire|vosh-agh-thog-mok|vampire|base
visit|vosh-agh-thog-narg|visit|base
vampires|vosh-agh-thog-moki-kin-burz-drak-ruk|vampire|base
vowed|vosh-agh-thog-ruk|vowed|base
waterskin|vosh-agh-thog-skar|waterskin|base
visiting|vosh-agh-thog-nargin-kin-burz-drak-varg|visit|base
weaknesses|nu-brak-burz-yankuki-kin-burz-gar-agh|weaknesses|base
wears|khalashur-kin-burz-gar-burz|wears|base
western|naut-lag-thog-kin-burz-gar-dak|western|base
whether|vosh-agh-thog-thog|whether|base
winged|vosh-agh-thog-varg|winged|base
winter|vosh-agh-varg-agh|winter|base
wise|thog-tiu-kin-burz-gar-gor|wise|base
zombies|vosh-agh-varg-burzi-kin-burz-gar-grak|zombie|base
zombie|vosh-agh-varg-burz|zombie|base
allergen|vosh-agh-agh-agh-kin-kin-burz-gar-krag|allergy|near
allergens|vosh-agh-agh-aghi-kin-burz-gar-mok|allergy|near
allergic|vosh-agh-agh-agh-thog-kin-burz-gar-narg|allergy|near
allergies|vosh-agh-agh-aghi-kin-burz-gar-ruk|allergy|near
allowing|kur-dargin-kin-burz-gar-skar|allows|near
alter|vosh-agh-agh-burz-mog-kin-burz-gar-thog|alteration|near
alterations|vosh-agh-agh-burzi-kin-burz-gar-varg|alteration|near
altered|vosh-agh-agh-burzash-kin-burz-gash-agh|alteration|near
altering|vosh-agh-agh-burzin-kin-burz-gash-burz|alteration|near
alters|vosh-agh-agh-burz-mogi-kin-burz-gash-dak|alteration|near
animate|vosh-agh-gor-narg-kin-kin-burz-gash-drak|inanimate|near
animated|vosh-agh-gor-nargash-kin-burz-gash-gar|inanimate|near
animating|vosh-agh-gor-nargin-kin-burz-gash-gash|inanimate|near
animation|vosh-agh-gor-narg-thog-kin-burz-gash-gor|inanimate|near
appearances|oglaruri-kin-burz-gash-grak|appearance|near
appeared|oglarurash-kin-burz-gash-grod|appear|near
arrived|vosh-agh-agh-drakash-kin-burz-gash-krag|arrive|near
arrives|vosh-agh-agh-drakur-kin-burz-gash-mok|arrive|near
arriving|vosh-agh-agh-drakin-kin-burz-gash-narg|arrive|near
attempted|vosh-agh-agh-garash-kin-burz-gash-ruk|attempt|near
attempting|vosh-agh-agh-garin-kin-burz-gash-skar|attempt|near
attempts|vosh-agh-agh-gari-kin-burz-gash-thog|attempt|near
await|vosh-agh-agh-gashu-kin-burz-gash-varg|awaiting|near
awaited|vosh-agh-agh-gashash-kin-burz-gor-agh|awaiting|near
awaits|vosh-agh-agh-gashur-kin-burz-gor-burz|awaiting|near
awaken|vosh-agh-agh-gor-kin-kin-burz-gor-dak|awakened|near
awakening|vosh-agh-agh-gorin-kin-burz-gor-drak|awakened|near
awakens|vosh-agh-agh-gori-kin-burz-gor-gar|awakened|near
backgrounded|vosh-agh-agh-grakash-kin-burz-gor-gash|background|near
backgrounding|vosh-agh-agh-grakin-kin-burz-gor-gor|background|near
backgrounds|vosh-agh-agh-graki-kin-burz-gor-grak|background|near
bad|vosh-agh-agh-grodu-kin-burz-gor-grod|badly|near
badmouthed|vosh-agh-agh-kragash-kin-burz-gor-krag|badmouth|near
badmouthing|vosh-agh-agh-kragin-kin-burz-gor-mok|badmouth|near
badmouths|vosh-agh-agh-kragi-kin-burz-gor-narg|badmouth|near
banish|vosh-agh-agh-narg-kin-kin-burz-gor-ruk|banished|near
banishes|vosh-agh-agh-nargi-kin-burz-gor-skar|banished|near
barrier|vosh-agh-agh-ruk-mog-kin-burz-gor-thog|barriers|near
beauties|vosh-agh-agh-thogi-kin-burz-gor-varg|beauty|near
beautified|vosh-agh-agh-thogash-kin-burz-grak-agh|beauty|near
beautifies|vosh-agh-agh-thogi-kin-burz-grak-burz|beauty|near
beautiful|vosh-agh-agh-skar-thog-kin-burz-grak-dak|beautifully|near
beautify|vosh-agh-agh-thoguk-kin-burz-grak-drak|beauty|near
beautifying|vosh-agh-agh-thogin-kin-burz-grak-gar|beauty|near
beginning|ashdak-hekashin-kin-burz-grak-gash|begin|near
begun|ashdak-hekashuk-kin-burz-grak-gor|begin|near
behaved|vosh-agh-agh-vargash-kin-burz-grak-grak|behave|near
behaves|vosh-agh-agh-vargur-kin-burz-grak-grod|behave|near
behaving|vosh-agh-agh-vargin-kin-burz-grak-krag|behave|near
behaviors|vosh-agh-burz-agh-mogi-kin-burz-grak-mok|behavior|near
belie|vosh-agh-burz-burzu-kin-burz-grak-narg|belies|near
belied|vosh-agh-burz-burzash-kin-burz-grak-ruk|belies|near
belying|vosh-agh-burz-burzin-kin-burz-grak-skar|belies|near
beneficially|vosh-agh-burz-dak-grak-kin-burz-grak-thog|beneficial|near
bestially|vosh-agh-burz-drak-grak-kin-burz-grak-varg|bestial|near
biddings|vosh-agh-burz-gari-kin-burz-grod-agh|bidding|near
blended|vosh-agh-burz-gashash-kin-burz-grod-burz|blend|near
blending|vosh-agh-burz-gashin-kin-burz-grod-dak|blend|near
blends|vosh-agh-burz-gashi-kin-burz-grod-drak|blend|near
bodyguard|vosh-agh-burz-goru-kin-burz-grod-gar|bodyguards|near
braced|vosh-agh-burz-grakash-kin-burz-grod-gash|brace|near
braces|vosh-agh-burz-graki-kin-burz-grod-gor|brace|near
bracing|vosh-agh-burz-grakin-kin-burz-grod-grak|brace|near
braziers|vosh-agh-burz-grod-mogi-kin-burz-grod-grod|brazier|near
brotherhoods|mokh-gashi-kin-burz-grod-krag|brotherhood|near
brothers|mokh-gash-mogi-kin-burz-grod-mok|brotherhood|near
cas|vosh-agh-burz-nargi-kin-burz-grod-narg|cases|near
case|vosh-agh-burz-nargu-kin-burz-grod-ruk|cases|near
castled|vosh-agh-burz-rukash-kin-burz-grod-skar|castle|near
castles|vosh-agh-burz-ruki-kin-burz-grod-thog|castle|near
castling|vosh-agh-burz-rukin-kin-burz-grod-varg|castle|near
censers|vosh-agh-burz-skar-mogi-kin-burz-krag-agh|censer|near
charismatic|vosh-agh-burz-thoguk-kin-burz-krag-burz|charisma|near
chart|vosh-agh-thog-gar-kin-kin-burz-krag-dak|uncharted|near
charted|vosh-agh-thog-garash-kin-burz-krag-drak|uncharted|near
charting|vosh-agh-thog-garin-kin-burz-krag-gar|uncharted|near
charts|vosh-agh-thog-gari-kin-burz-krag-gash|uncharted|near
chronologic|vosh-agh-burz-varguk-kin-burz-krag-gor|chronology|near
chronological|vosh-agh-burz-varguk-kin-burz-krag-grak|chronology|near
chronologically|vosh-agh-burz-varg-grak-kin-burz-krag-grod|chronology|near
chronologies|vosh-agh-burz-vargi-kin-burz-krag-krag|chronology|near
civilisations|vosh-agh-dak-aghi-kin-burz-krag-mok|civilisation|near
civilise|vosh-agh-dak-agh-kin-kin-burz-krag-narg|civilisation|near
civilised|vosh-agh-dak-aghash-kin-burz-krag-ruk|civilisation|near
civilising|vosh-agh-dak-aghin-kin-burz-krag-skar|civilisation|near
civilization|vosh-agh-dak-agh-thog-kin-burz-krag-thog|civilisation|near
civilize|vosh-agh-dak-agh-kin-kin-burz-krag-varg|civilisation|near
civilized|vosh-agh-dak-aghash-kin-burz-mok-agh|civilisation|near
civilizing|vosh-agh-dak-aghin-kin-burz-mok-burz|civilisation|near
clairvoyances|vosh-agh-dak-burzi-kin-burz-mok-dak|clairvoyance|near
clairvoyant|vosh-agh-dak-burz-kin-kin-burz-mok-drak|clairvoyance|near
cleansed|vosh-agh-dak-dakash-kin-burz-mok-gar|cleanse|near
cleanses|vosh-agh-dak-dakur-kin-burz-mok-gash|cleanse|near
cleansing|vosh-agh-dak-dakin-kin-burz-mok-gor|cleanse|near
close|vosh-agh-dak-drak-kin-kin-burz-mok-grak|closing|near
closed|vosh-agh-dak-drakash-kin-burz-mok-grod|closing|near
closes|vosh-agh-dak-draki-kin-burz-mok-krag|closing|near
closings|vosh-agh-dak-draki-kin-burz-mok-mok|closing|near
commit|vosh-agh-dak-gashu-kin-burz-mok-narg|commitment|near
commitments|vosh-agh-dak-gashi-kin-burz-mok-ruk|commitment|near
communicate|vosh-agh-dak-graku-kin-burz-mok-skar|communication|near
communicated|vosh-agh-dak-grakash-kin-burz-mok-thog|communication|near
communicates|vosh-agh-dak-grakur-kin-burz-mok-varg|communication|near
communicating|vosh-agh-dak-grakin-kin-burz-narg-agh|communication|near
communications|vosh-agh-dak-graki-kin-burz-narg-burz|communication|near
communicator|vosh-agh-dak-grak-mog-kin-burz-narg-dak|communication|near
communicators|vosh-agh-dak-grak-mogi-kin-burz-narg-drak|communication|near
comprehend|vosh-agh-dak-kragu-kin-burz-narg-gar|comprehension|near
comprehended|vosh-agh-dak-kragash-kin-burz-narg-gash|comprehension|near
comprehending|vosh-agh-dak-kragin-kin-burz-narg-gor|comprehension|near
comprehends|vosh-agh-dak-kragur-kin-burz-narg-grak|comprehension|near
comprehensibly|vosh-agh-dak-grod-grak-kin-burz-narg-grod|comprehensible|near
comprehensions|vosh-agh-dak-kragi-kin-burz-narg-krag|comprehension|near
compris|vosh-agh-dak-moki-kin-burz-narg-mok|comprised|near
comprise|vosh-agh-dak-moku-kin-burz-narg-narg|comprised|near
comprises|vosh-agh-dak-mokur-kin-burz-narg-ruk|comprised|near
comprising|vosh-agh-dak-mokin-kin-burz-narg-skar|comprised|near
concentrated|vosh-agh-dak-nargash-kin-burz-narg-thog|concentrate|near
concentrates|vosh-agh-dak-nargi-kin-burz-narg-varg|concentrate|near
concentrating|vosh-agh-dak-nargin-kin-burz-ruk-agh|concentrate|near
conjectured|vosh-agh-dak-rukash-kin-burz-ruk-burz|conjecture|near
conjectures|vosh-agh-dak-ruki-kin-burz-ruk-dak|conjecture|near
conjecturing|vosh-agh-dak-rukin-kin-burz-ruk-drak|conjecture|near
connect|vosh-agh-dak-skaru-kin-burz-ruk-gar|connection|near
connected|vosh-agh-dak-skarash-kin-burz-ruk-gash|connection|near
connecting|vosh-agh-dak-skarin-kin-burz-ruk-gor|connection|near
connections|vosh-agh-dak-skari-kin-burz-ruk-grak|connection|near
connector|vosh-agh-dak-skar-mog-kin-burz-ruk-grod|connection|near
connectors|vosh-agh-dak-skar-mogi-kin-burz-ruk-krag|connection|near
connects|vosh-agh-dak-skarur-kin-burz-ruk-mok|connection|near
consulted|vosh-agh-dak-thogash-kin-burz-ruk-narg|consult|near
consulting|vosh-agh-dak-thogin-kin-burz-ruk-ruk|consult|near
consults|vosh-agh-dak-thogur-kin-burz-ruk-skar|consult|near
contained|ik-dargurash-kin-burz-ruk-thog|contain|near
container|ik-dargur-mog-kin-burz-ruk-varg|contain|near
containers|ik-dargur-mogi-kin-burz-skar-agh|contain|near
containing|ik-dargurin-kin-burz-skar-burz|contain|near
contexts|vosh-agh-dak-vargi-kin-burz-skar-dak|context|near
corporeally|vosh-agh-drak-agh-grak-kin-burz-skar-drak|corporeal|near
create|vosh-agh-drak-drak-kin-kin-burz-skar-gar|creating|near
created|vosh-agh-drak-drakash-kin-burz-skar-gash|creating|near
creates|vosh-agh-drak-draki-kin-burz-skar-gor|creating|near
crucially|vosh-agh-drak-gar-grak-kin-burz-skar-grak|crucial|near
cunningly|vosh-agh-drak-gor-grak-kin-burz-skar-grod|cunning|near
cunnings|vosh-agh-drak-gori-kin-burz-skar-krag|cunning|near
darker|burzuk-mog-kin-burz-skar-mok|darkest|near
decided|vosh-agh-drak-grakash-kin-burz-skar-narg|decide|near
decides|vosh-agh-drak-grakur-kin-burz-skar-ruk|decide|near
deciding|vosh-agh-drak-grakin-kin-burz-skar-skar|decide|near
dedicatedly|vosh-agh-drak-krag-grak-kin-burz-skar-thog|dedicated|near
dedications|draku-mur-kaag-tuki-kin-burz-skar-varg|dedication|near
denote|vosh-agh-drak-moku-kin-burz-thog-agh|denotes|near
denoted|vosh-agh-drak-mokash-kin-burz-thog-burz|denotes|near
denoting|vosh-agh-drak-mokin-kin-burz-thog-dak|denotes|near
destinations|vosh-agh-drak-vargi-kin-burz-thog-drak|destination|near
destroy|brakash-tiu-kin-burz-thog-gar|destroying|near
destroys|brakash-tiur-kin-burz-thog-gash|destroying|near
determinate|thog-oglaru-kin-burz-thog-gor|determination|near
determinations|thog-oglarui-kin-burz-thog-grak|determination|near
developed|vosh-agh-gar-aghash-kin-burz-thog-grod|develop|near
developing|vosh-agh-gar-aghin-kin-burz-thog-krag|develop|near
develops|vosh-agh-gar-aghur-kin-burz-thog-mok|develop|near
distantly|vosh-agh-gar-burz-grak-kin-burz-thog-narg|distant|near
doorways|vosh-agh-gar-daki-kin-burz-thog-ruk|doorway|near
dormancy|vosh-agh-gar-drakuk-kin-burz-thog-skar|dormant|near
dues|vosh-agh-gar-gari-kin-burz-thog-thog|due|near
embroider|vosh-agh-gar-grak-mog-kin-burz-thog-varg|embroidery|near
embroidered|vosh-agh-gar-grakash-kin-burz-varg-agh|embroidery|near
embroideries|vosh-agh-gar-graki-kin-burz-varg-burz|embroidery|near
embroidering|vosh-agh-gar-grakin-kin-burz-varg-dak|embroidery|near
embroiders|vosh-agh-gar-grak-mogi-kin-burz-varg-drak|embroidery|near
enable|vosh-agh-gar-grodu-kin-burz-varg-gar|enables|near
enabled|vosh-agh-gar-grodash-kin-burz-varg-gash|enables|near
enabling|vosh-agh-gar-grodin-kin-burz-varg-gor|enables|near
encumber|vosh-agh-gar-krag-mog-kin-burz-varg-grak|encumbrance|near
encumbered|vosh-agh-gar-kragash-kin-burz-varg-grod|encumbrance|near
encumbering|vosh-agh-gar-kragin-kin-burz-varg-krag|encumbrance|near
encumbers|vosh-agh-gar-krag-mogi-kin-burz-varg-mok|encumbrance|near
encumbrances|vosh-agh-gar-kragi-kin-burz-varg-narg|encumbrance|near
engender|vosh-agh-gar-mok-mog-kin-burz-varg-ruk|engenders|near
engendered|vosh-agh-gar-mokash-kin-burz-varg-skar|engenders|near
engendering|vosh-agh-gar-mokin-kin-burz-varg-thog|engenders|near
environments|vosh-agh-gar-nargi-kin-burz-varg-varg|environment|near
established|vosh-agh-gar-rukash-kin-dak-agh-agh|establish|near
establishes|vosh-agh-gar-rukur-kin-dak-agh-burz|establish|near
establishing|vosh-agh-gar-rukin-kin-dak-agh-dak|establish|near
ethereally|vosh-agh-gar-skar-grak-kin-dak-agh-drak|ethereal|near
euphoria|vosh-agh-gar-thog-kin-kin-dak-agh-gar|euphoric|near
euphorically|vosh-agh-gar-thog-grak-kin-dak-agh-gash|euphoric|near
explores|oglar-lagashur-kin-dak-agh-gor|explore|near
face|vosh-agh-gar-varg-kin-kin-dak-agh-grak|facing|near
faced|vosh-agh-gar-vargash-kin-dak-agh-grod|facing|near
faces|vosh-agh-gar-vargi-kin-dak-agh-krag|facing|near
facings|vosh-agh-gar-vargi-kin-dak-agh-mok|facing|near
faithfully|vosh-agh-gash-agh-grak-kin-dak-agh-narg|faithful|near
fantastically|vosh-agh-gash-burz-grak-kin-dak-agh-ruk|fantastic|near
felt|grodhash-kin-dak-agh-skar|feelings|near
fictional|vosh-agh-gash-dakuk-kin-dak-agh-thog|fiction|near
fictionally|vosh-agh-gash-dak-grak-kin-dak-agh-varg|fiction|near
fictions|vosh-agh-gash-daki-kin-dak-burz-agh|fiction|near
fiercely|vosh-agh-gash-drak-grak-kin-dak-burz-burz|fierce|near
fiercer|vosh-agh-gash-drak-mog-kin-dak-burz-dak|fierce|near
fiercest|vosh-agh-gash-drak-gash-kin-dak-burz-drak|fierce|near
follower|vosh-agh-gash-gar-mog-kin-dak-burz-gar|followers|near
former|vosh-agh-gash-gash-mog-kin-dak-burz-gash|formerly|near
fortunate|mauk-drav-thog-kin-kin-dak-burz-gor|unfortunate|near
fortunately|mauk-drav-thog-grak-kin-dak-burz-grak|unfortunate|near
fronted|vosh-agh-gash-gorash-kin-dak-burz-grod|front|near
fronting|vosh-agh-gash-gorin-kin-dak-burz-krag|front|near
fronts|vosh-agh-gash-gori-kin-dak-burz-mok|front|near
fungi|gruul-thrum-rukhuk-thog-kin-dak-burz-narg|fungus|near
funguses|gruul-thrum-rukhuki-kin-dak-burz-ruk|fungus|near
gamed|vosh-agh-gash-grodash-kin-dak-burz-skar|game|near
gamely|vosh-agh-gash-grod-grak-kin-dak-burz-thog|game|near
games|vosh-agh-gash-grodi-kin-dak-burz-varg|game|near
gaming|vosh-agh-gash-grodin-kin-dak-dak-agh|game|near
gargoyles|vosh-agh-gash-kragi-kin-dak-dak-burz|gargoyle|near
gateways|vosh-agh-gash-moki-kin-dak-dak-dak|gateway|near
ghost|vosh-agh-gash-nargu-kin-dak-dak-drak|ghosts|near
ginsengs|vosh-agh-gash-ruki-kin-dak-dak-gar|ginseng|near
gnomish|vosh-agh-gash-skar-kin-kin-dak-dak-gash|gnome|near
gravitic|vosh-agh-gash-thoguk-kin-dak-dak-gor|gravity|near
gravities|vosh-agh-gash-thogi-kin-dak-dak-grak|gravity|near
hail|vosh-agh-gash-vargu-kin-dak-dak-grod|hails|near
hailed|vosh-agh-gash-vargash-kin-dak-dak-krag|hails|near
hailing|vosh-agh-gash-vargin-kin-dak-dak-mok|hails|near
half-elf|elf-thog-kin-dak-dak-narg|half-elven|near
harming|brak-thog-morzin-kin-dak-dak-ruk|harmed|near
harms|brak-thog-morzi-kin-dak-dak-skar|harmed|near
harness|vosh-agh-gor-agh-thog-kin-dak-dak-thog|harnessing|near
harnessed|vosh-agh-gor-aghash-kin-dak-dak-varg|harnessing|near
harnesses|vosh-agh-gor-aghi-kin-dak-drak-agh|harnessing|near
helmed|vosh-agh-gor-burzash-kin-dak-drak-burz|helm|near
helmets|vosh-agh-gor-daki-kin-dak-drak-dak|helmet|near
helming|vosh-agh-gor-burzin-kin-dak-drak-drak|helm|near
helms|vosh-agh-gor-burzi-kin-dak-drak-gar|helm|near
help|vosh-agh-gor-draku-kin-dak-drak-gash|helped|near
helpings|vosh-agh-gor-draki-kin-dak-drak-gor|helped|near
helps|vosh-agh-gor-draki-kin-dak-drak-grak|helped|near
hideously|vosh-agh-gor-gar-grak-kin-dak-drak-grod|hideous|near
hideousness|vosh-agh-gor-gar-thog-kin-dak-drak-krag|hideous|near
honorable|grak-tur-tiuk-kin-dak-drak-mok|honor|near
honored|grak-tur-tiash-kin-dak-drak-narg|honor|near
honoring|grak-tur-tiin-kin-dak-drak-ruk|honor|near
honors|grak-tur-ti-mogi-kin-dak-drak-skar|honor|near
horribly|vark-thog-mog-grak-kin-dak-drak-thog|horrible|near
humorous|vosh-agh-gor-gori-kin-dak-drak-varg|humor|near
humorously|vosh-agh-gor-gor-grak-kin-dak-gar-agh|humor|near
humors|vosh-agh-gor-gor-mogi-kin-dak-gar-burz|humor|near
hurting|vosh-agh-gor-grakin-kin-dak-gar-dak|hurt|near
hurts|vosh-agh-gor-graki-kin-dak-gar-drak|hurt|near
hyoscyamine|vosh-agh-gor-grod-thog-kin-dak-gar-gar|hyoscyamine-containing|near
importance|vosh-agh-gor-mok-thog-kin-dak-gar-gash|important|near
importantly|vosh-agh-gor-mok-grak-kin-dak-gar-gor|important|near
inanimately|vosh-agh-gor-narg-grak-kin-dak-gar-grak|inanimate|near
included|vosh-agh-gor-rukash-kin-dak-gar-grod|include|near
including|vosh-agh-gor-rukin-kin-dak-gar-krag|include|near
incorporeality|vosh-agh-gor-skar-thog-kin-dak-gar-mok|incorporeal|near
incorporeally|vosh-agh-gor-skar-grak-kin-dak-gar-narg|incorporeal|near
infiltrate|vosh-agh-gor-thogu-kin-dak-gar-ruk|infiltrating|near
infiltrated|vosh-agh-gor-thogash-kin-dak-gar-skar|infiltrating|near
infiltrates|vosh-agh-gor-thogi-kin-dak-gar-thog|infiltrating|near
initiated|vosh-agh-gor-vargash-kin-dak-gar-varg|initiate|near
initiates|vosh-agh-gor-vargi-kin-dak-gash-agh|initiate|near
initiating|vosh-agh-gor-vargin-kin-dak-gash-burz|initiate|near
inquisitively|vosh-agh-grak-agh-grak-kin-dak-gash-dak|inquisitive|near
inquisitiveness|vosh-agh-grak-agh-thog-kin-dak-gash-drak|inquisitive|near
inspirations|vosh-agh-grak-burzi-kin-dak-gash-gar|inspiration|near
inspired|vosh-agh-grak-burzash-kin-dak-gash-gash|inspiration|near
inspires|vosh-agh-grak-burzur-kin-dak-gash-gor|inspiration|near
inspiring|vosh-agh-grak-burzin-kin-dak-gash-grak|inspiration|near
intestinally|vosh-agh-grak-drak-grak-kin-dak-gash-grod|intestinal|near
intestine|vosh-agh-grak-drak-kin-kin-dak-gash-krag|intestinal|near
intestines|vosh-agh-grak-draki-kin-dak-gash-mok|intestinal|near
intricacy|vosh-agh-grak-garuk-kin-dak-gash-narg|intricate|near
intricately|vosh-agh-grak-gar-grak-kin-dak-gash-ruk|intricate|near
invade|vosh-agh-grak-gashu-kin-dak-gash-skar|invasion|near
invaded|vosh-agh-grak-gashash-kin-dak-gash-thog|invasion|near
invades|vosh-agh-grak-gashur-kin-dak-gash-varg|invasion|near
invading|vosh-agh-grak-gashin-kin-dak-gor-agh|invasion|near
invasions|vosh-agh-grak-gashi-kin-dak-gor-burz|invasion|near
involved|vosh-agh-grak-gorash-kin-dak-gor-dak|involve|near
involves|vosh-agh-grak-gorur-kin-dak-gor-drak|involve|near
involving|vosh-agh-grak-gorin-kin-dak-gor-gar|involve|near
kinder|vosh-agh-grak-grak-mog-kin-dak-gor-gash|kind|near
kindest|vosh-agh-grak-grak-gash-kin-dak-gor-gor|kind|near
kindly|vosh-agh-grak-grak-grak-kin-dak-gor-grak|kind|near
kindness|vosh-agh-grak-grak-thog-kin-dak-gor-grod|kind|near
kinds|vosh-agh-grak-graki-kin-dak-gor-krag|kind|near
know|thogash-kin-kin-dak-gor-mok|unknown|near
knowing|thogashin-kin-dak-gor-narg|unknown|near
known|thogashuk-kin-dak-gor-ruk|unknown|near
knows|thogashi-kin-dak-gor-skar|unknown|near
lawfully|darg-bibuk-ti-grak-kin-dak-gor-thog|lawfuller|near
lawfulness|darg-bibuk-ti-thog-kin-dak-gor-varg|lawfuller|near
leafed|vosh-agh-grak-grodash-kin-dak-grak-agh|leaf|near
leafing|vosh-agh-grak-grodin-kin-dak-grak-burz|leaf|near
leafs|vosh-agh-grak-grodi-kin-dak-grak-dak|leaf|near
leaves|vosh-agh-grak-grodi-kin-dak-grak-drak|leaf|near
legendary|vosh-agh-grak-kraguk-kin-dak-grak-gar|legend|near
legends|vosh-agh-grak-kragi-kin-dak-grak-gash|legend|near
locate|dakukash-kin-kin-dak-grak-gor|locating|near
locates|dakukashi-kin-dak-grak-grak|locating|near
loom|nak-oglarur-kin-kin-dak-grak-grod|looming|near
loomed|nak-oglarurash-kin-dak-grak-krag|looming|near
major|vosh-agh-grak-mok-mog-kin-dak-grak-mok|majority|near
majorities|vosh-agh-grak-moki-kin-dak-grak-narg|majority|near
manipulate|vosh-agh-grak-nargu-kin-dak-grak-ruk|manipulation|near
manipulations|vosh-agh-grak-nargi-kin-dak-grak-skar|manipulation|near
mattered|vosh-agh-grak-rukash-kin-dak-grak-thog|matter|near
mattering|vosh-agh-grak-rukin-kin-dak-grak-varg|matter|near
matters|vosh-agh-grak-ruk-mogi-kin-dak-grod-agh|matter|near
mature|vosh-agh-grak-skaru-kin-dak-grod-burz|maturity|near
maturities|vosh-agh-grak-skari-kin-dak-grod-dak|maturity|near
mean|vosh-agh-grak-thogu-kin-dak-grod-drak|means|near
meaning|vosh-agh-grak-thogin-kin-dak-grod-gar|means|near
meant|vosh-agh-grak-thogash-kin-dak-grod-gash|means|near
mentored|vosh-agh-grod-aghash-kin-dak-grod-gor|mentor|near
mentoring|vosh-agh-grod-aghin-kin-dak-grod-grak|mentor|near
mentors|vosh-agh-grod-agh-mogi-kin-dak-grod-grod|mentor|near
milder|vosh-agh-grod-burz-mog-kin-dak-grod-krag|mild|near
mildest|vosh-agh-grod-burz-gash-kin-dak-grod-mok|mild|near
mildly|vosh-agh-grod-burz-grak-kin-dak-grod-narg|mild|near
mixtures|vosh-agh-grod-daki-kin-dak-grod-ruk|mixture|near
mooned|vosh-agh-grod-drakash-kin-dak-grod-skar|moon|near
mooning|vosh-agh-grod-drakin-kin-dak-grod-thog|moon|near
moons|vosh-agh-grod-draki-kin-dak-grod-varg|moon|near
mortality|vosh-agh-grod-gar-thog-kin-dak-krag-agh|mortal|near
mortally|vosh-agh-grod-gar-grak-kin-dak-krag-burz|mortal|near
mortals|vosh-agh-grod-gari-kin-dak-krag-dak|mortal|near
motivate|vosh-agh-grod-gashu-kin-dak-krag-drak|motivation|near
motivated|vosh-agh-grod-gashash-kin-dak-krag-gar|motivation|near
motivates|vosh-agh-grod-gashur-kin-dak-krag-gash|motivation|near
motivating|vosh-agh-grod-gashin-kin-dak-krag-gor|motivation|near
motivations|vosh-agh-grod-gashi-kin-dak-krag-grak|motivation|near
mummied|vosh-agh-grod-gorash-kin-dak-krag-grod|mummy|near
mummified|vosh-agh-grod-gorash-kin-dak-krag-krag|mummy|near
mummifies|vosh-agh-grod-gori-kin-dak-krag-mok|mummy|near
mummify|vosh-agh-grod-goruk-kin-dak-krag-narg|mummy|near
mummifying|vosh-agh-grod-gorin-kin-dak-krag-ruk|mummy|near
mummying|vosh-agh-grod-gorin-kin-dak-krag-skar|mummy|near
mundanely|vosh-agh-grod-grak-grak-kin-dak-krag-thog|mundane|near
mythologic|vosh-agh-grod-groduk-kin-dak-krag-varg|mythology|near
mythological|vosh-agh-grod-groduk-kin-dak-mok-agh|mythology|near
mythologically|vosh-agh-grod-grod-grak-kin-dak-mok-burz|mythology|near
mythologies|vosh-agh-grod-grodi-kin-dak-mok-dak|mythology|near
negotiate|vosh-agh-grod-kragu-kin-dak-mok-drak|negotiating|near
negotiated|vosh-agh-grod-kragash-kin-dak-mok-gar|negotiating|near
negotiates|vosh-agh-grod-kragi-kin-dak-mok-gash|negotiating|near
negotiation|vosh-agh-grod-krag-thog-kin-dak-mok-gor|negotiating|near
negotiator|vosh-agh-grod-krag-mog-kin-dak-mok-grak|negotiating|near
negotiators|vosh-agh-grod-krag-mogi-kin-dak-mok-grod|negotiating|near
notables|oglar-tii-kin-dak-mok-krag|notable|near
obstruct|vosh-agh-grod-narg-kin-kin-dak-mok-mok|obstructions|near
obstructed|vosh-agh-grod-nargash-kin-dak-mok-narg|obstructions|near
obstructing|vosh-agh-grod-nargin-kin-dak-mok-ruk|obstructions|near
obstruction|vosh-agh-grod-narg-thog-kin-dak-mok-skar|obstructions|near
obstructs|vosh-agh-grod-nargi-kin-dak-mok-thog|obstructions|near
optimistic|vosh-agh-grod-varguk-kin-dak-mok-varg|optimism|near
optimistically|vosh-agh-grod-varg-grak-kin-dak-narg-agh|optimism|near
outposts|vosh-agh-krag-burzi-kin-dak-narg-burz|outpost|near
outskirt|vosh-agh-krag-daku-kin-dak-narg-dak|outskirts|near
overlook|vosh-agh-krag-drak-kin-kin-dak-narg-drak|overlooked|near
overlooking|vosh-agh-krag-drakin-kin-dak-narg-gar|overlooked|near
overlooks|vosh-agh-krag-draki-kin-dak-narg-gash|overlooked|near
parties|lag-mokhi-kin-dak-narg-gor|partys|near
personalities|vosh-agh-krag-gashi-kin-dak-narg-grak|personality|near
personally|vosh-agh-krag-gar-grak-kin-dak-narg-grod|personal|near
phandalins|vosh-agh-krag-gori-kin-dak-narg-krag|phandalin|near
pivot|vosh-agh-krag-grak-kin-kin-dak-narg-mok|pivotal|near
pivotally|vosh-agh-krag-grak-grak-kin-dak-narg-narg|pivotal|near
pivots|vosh-agh-krag-graki-kin-dak-narg-ruk|pivotal|near
plainly|vosh-agh-krag-grod-grak-kin-dak-narg-skar|plain|near
plains|vosh-agh-krag-grodi-kin-dak-narg-thog|plain|near
planned|vosh-agh-krag-kragash-kin-dak-narg-varg|plan|near
planning|vosh-agh-krag-kragin-kin-dak-ruk-agh|plan|near
play|vosh-agh-krag-nargu-kin-dak-ruk-burz|plays|near
played|vosh-agh-krag-nargash-kin-dak-ruk-dak|plays|near
playing|vosh-agh-krag-nargin-kin-dak-ruk-drak|plays|near
pleasantly|vosh-agh-krag-ruk-grak-kin-dak-ruk-gar|pleasant|near
popularity|vosh-agh-krag-skar-thog-kin-dak-ruk-gash|popular|near
popularly|vosh-agh-krag-skar-grak-kin-dak-ruk-gor|popular|near
populars|vosh-agh-krag-skari-kin-dak-ruk-grak|popular|near
portalled|vosh-agh-krag-thogash-kin-dak-ruk-grod|portal|near
portals|vosh-agh-krag-thogi-kin-dak-ruk-krag|portal|near
possess|vosh-agh-krag-vargur-kin-dak-ruk-mok|possessed|near
possessing|vosh-agh-krag-vargin-kin-dak-ruk-narg|possessed|near
possession|vosh-agh-krag-varg-thog-kin-dak-ruk-ruk|possessed|near
possessions|vosh-agh-krag-vargi-kin-dak-ruk-skar|possessed|near
practitioner|vosh-agh-mok-burz-mog-kin-dak-ruk-thog|practitioners|near
preparation|vosh-agh-mok-dak-thog-kin-dak-ruk-varg|prepared|near
prepare|vosh-agh-mok-dak-kin-kin-dak-skar-agh|prepared|near
prepares|vosh-agh-mok-daki-kin-dak-skar-burz|prepared|near
preparing|vosh-agh-mok-dakin-kin-dak-skar-dak|prepared|near
prevented|vosh-agh-mok-drakash-kin-dak-skar-drak|prevent|near
preventing|vosh-agh-mok-drakin-kin-dak-skar-gar|prevent|near
prevents|vosh-agh-mok-drakur-kin-dak-skar-gash|prevent|near
profane|vosh-agh-mok-garu-kin-dak-skar-gor|profanity|near
profanely|vosh-agh-mok-gar-grak-kin-dak-skar-grak|profanity|near
profanities|vosh-agh-mok-gari-kin-dak-skar-grod|profanity|near
promise|vosh-agh-mok-gash-kin-kin-dak-skar-krag|promising|near
promised|vosh-agh-mok-gashash-kin-dak-skar-mok|promising|near
promises|vosh-agh-mok-gashi-kin-dak-skar-narg|promising|near
promisingly|vosh-agh-mok-gash-grak-kin-dak-skar-ruk|promising|near
pronouncedly|vosh-agh-mok-gor-grak-kin-dak-skar-skar|pronounced|near
proper|vosh-agh-mok-grak-mog-kin-dak-skar-thog|properly|near
protect|gor-thog-in-kin-kin-dak-skar-varg|protective|near
protected|gor-thog-inash-kin-dak-thog-agh|protective|near
protectively|gor-thog-in-grak-kin-dak-thog-burz|protective|near
protects|gor-thog-ini-kin-dak-thog-dak|protective|near
quick-wittedly|vosh-agh-mok-krag-grak-kin-dak-thog-drak|quick-witted|near
rayed|vosh-agh-mok-nargash-kin-dak-thog-gar|ray|near
raying|vosh-agh-mok-nargin-kin-dak-thog-gash|ray|near
rays|vosh-agh-mok-nargi-kin-dak-thog-gor|ray|near
react|vosh-agh-mok-ruk-kin-kin-dak-thog-grak|reactions|near
reacted|vosh-agh-mok-rukash-kin-dak-thog-grod|reactions|near
reacting|vosh-agh-mok-rukin-kin-dak-thog-krag|reactions|near
reaction|vosh-agh-mok-ruk-thog-kin-dak-thog-mok|reactions|near
reacts|vosh-agh-mok-ruki-kin-dak-thog-narg|reactions|near
reanimate|vosh-agh-mok-skar-kin-kin-dak-thog-ruk|reanimated|near
reanimates|vosh-agh-mok-skari-kin-dak-thog-skar|reanimated|near
reanimating|vosh-agh-mok-skarin-kin-dak-thog-thog|reanimated|near
regained|vosh-agh-mok-thogash-kin-dak-thog-varg|regain|near
regaining|vosh-agh-mok-thogin-kin-dak-varg-agh|regain|near
regains|vosh-agh-mok-thogur-kin-dak-varg-burz|regain|near
relics|vosh-agh-mok-vargi-kin-dak-varg-dak|relic|near
relieved|vosh-agh-narg-aghash-kin-dak-varg-drak|relieve|near
relieves|vosh-agh-narg-aghur-kin-dak-varg-gar|relieve|near
relieving|vosh-agh-narg-aghin-kin-dak-varg-gash|relieve|near
remind|vosh-agh-narg-burz-kin-kin-dak-varg-gor|reminding|near
reminded|vosh-agh-narg-burzash-kin-dak-varg-grak|reminding|near
reminds|vosh-agh-narg-burzi-kin-dak-varg-grod|reminding|near
remotely|vosh-agh-narg-dak-grak-kin-dak-varg-krag|remote|near
resettle|vosh-agh-narg-drak-kin-kin-dak-varg-mok|resettled|near
resettles|vosh-agh-narg-draki-kin-dak-varg-narg|resettled|near
resettling|vosh-agh-narg-drakin-kin-dak-varg-ruk|resettled|near
responsibilities|tukur-dargi-kin-dak-varg-skar|responsibility|near
responsibly|tukur-darg-grak-kin-dak-varg-thog|responsibility|near
reverting|lagin-zorn-dok-kin-dak-varg-varg|reverted|near
reverts|lagu-zorn-doki-kin-drak-agh-agh|reverted|near
rewarded|vosh-agh-narg-garash-kin-drak-agh-burz|reward|near
rewarding|vosh-agh-narg-garin-kin-drak-agh-dak|reward|near
rewards|vosh-agh-narg-gari-kin-drak-agh-drak|reward|near
rival|vosh-agh-narg-gashu-kin-drak-agh-gar|rivals|near
rivalry|vosh-agh-narg-gashuk-kin-drak-agh-gash|rivals|near
rod|vosh-agh-narg-goru-kin-drak-agh-gor|rods|near
roleplay|vosh-agh-narg-grakuk-kin-drak-agh-grak|role|near
roleplayed|vosh-agh-narg-grakash-kin-drak-agh-grod|role|near
roleplays|vosh-agh-narg-graki-kin-drak-agh-krag|role|near
roles|vosh-agh-narg-graki-kin-drak-agh-mok|role|near
sacked|vosh-agh-narg-grodash-kin-drak-agh-narg|sack|near
sacking|vosh-agh-narg-grodin-kin-drak-agh-ruk|sack|near
sacks|vosh-agh-narg-grodi-kin-drak-agh-skar|sack|near
sacredly|vosh-agh-narg-krag-grak-kin-drak-agh-thog|sacred|near
sacredness|vosh-agh-narg-krag-thog-kin-drak-agh-varg|sacred|near
scouted|vosh-agh-narg-rukash-kin-drak-burz-agh|scout|near
scoutings|vosh-agh-narg-ruki-kin-drak-burz-burz|scout|near
scouts|vosh-agh-narg-ruki-kin-drak-burz-dak|scout|near
seek|vosh-agh-narg-skar-kin-kin-drak-burz-drak|seeking|near
seeks|vosh-agh-narg-skari-kin-drak-burz-gar|seeking|near
send|lag-dargashu-kin-drak-burz-gash|sends|near
sendings|lag-dargashi-kin-drak-burz-gor|sends|near
serpent|vosh-agh-narg-vargu-kin-drak-burz-grak|serpents|near
shrined|vosh-agh-ruk-aghash-kin-drak-burz-grod|shrine|near
shrines|vosh-agh-ruk-aghi-kin-drak-burz-krag|shrine|near
shrining|vosh-agh-ruk-aghin-kin-drak-burz-mok|shrine|near
signified|vosh-agh-ruk-burzash-kin-drak-burz-narg|signifying|near
signifies|vosh-agh-ruk-burzi-kin-drak-burz-ruk|signifying|near
signify|vosh-agh-ruk-burzuk-kin-drak-burz-skar|signifying|near
similarly|vosh-agh-ruk-dak-grak-kin-drak-burz-thog|similar|near
skills|mauk-heki-kin-drak-burz-varg|skilled|near
smoke|vosh-agh-ruk-drak-kin-kin-drak-dak-agh|smoked|near
smokes|vosh-agh-ruk-draki-kin-drak-dak-burz|smoked|near
smoking|vosh-agh-ruk-drakin-kin-drak-dak-dak|smoked|near
solve|grod-lag-bibu-kin-drak-dak-drak|solutions|near
solved|grod-lag-bibash-kin-drak-dak-gar|solutions|near
solves|grod-lag-bibur-kin-drak-dak-gash|solutions|near
solving|grod-lag-bibin-kin-drak-dak-gor|solutions|near
sought|vosh-agh-narg-skarash-kin-drak-dak-grak|seeking|near
specialist|vosh-agh-ruk-gar-kin-kin-drak-dak-grod|specializing|near
specialists|vosh-agh-ruk-gari-kin-drak-dak-krag|specializing|near
specialization|vosh-agh-ruk-gar-thog-kin-drak-dak-mok|specializing|near
specialize|vosh-agh-ruk-garu-kin-drak-dak-narg|specializing|near
specialized|vosh-agh-ruk-garash-kin-drak-dak-ruk|specializing|near
specializes|vosh-agh-ruk-garur-kin-drak-dak-skar|specializing|near
spellcasters|vosh-agh-ruk-gash-mogi-kin-drak-dak-thog|spellcaster|near
sphere|vosh-agh-ruk-goru-kin-drak-dak-varg|spheres|near
spill|vosh-agh-ruk-graku-kin-drak-drak-agh|spilled|near
spilling|vosh-agh-ruk-grakin-kin-drak-drak-burz|spilled|near
spills|vosh-agh-ruk-graki-kin-drak-drak-dak|spilled|near
sporebanes|vosh-agh-ruk-grodi-kin-drak-drak-drak|sporebane|near
staffs|bant-zol-mogi-kin-drak-drak-gar|staves|near
stem|vosh-agh-ruk-kragu-kin-drak-drak-gash|stems|near
stemmed|vosh-agh-ruk-kragash-kin-drak-drak-gor|stems|near
stemming|vosh-agh-ruk-kragin-kin-drak-drak-grak|stems|near
stepped|vosh-agh-ruk-mokash-kin-drak-drak-grod|step|near
stepping|vosh-agh-ruk-mokin-kin-drak-drak-krag|step|near
steps|vosh-agh-ruk-moki-kin-drak-drak-mok|step|near
strikingly|vosh-agh-ruk-narg-grak-kin-drak-drak-narg|striking|near
stronger|yankuk-mog-kin-drak-drak-ruk|strengths|near
strongest|yankuk-gash-kin-drak-drak-skar|strengths|near
strongholds|vosh-agh-ruk-ruki-kin-drak-drak-thog|stronghold|near
strongly|yankuk-grak-kin-drak-drak-varg|strengths|near
studied|vosh-agh-ruk-skarash-kin-drak-gar-agh|study|near
studies|vosh-agh-ruk-skari-kin-drak-gar-burz|study|near
succeed|grod-lag-thogash-kin-drak-gar-dak|successive|near
succeeded|grod-lag-thogash-kin-drak-gar-drak|successive|near
succeeding|grod-lag-thogin-kin-drak-gar-gar|successive|near
succeeds|grod-lag-thogi-kin-drak-gar-gash|successive|near
successively|grod-lag-thog-grak-kin-drak-gar-gor|successive|near
sufferings|vosh-agh-ruk-thogi-kin-drak-gar-grak|suffering|near
summoner|vosh-agh-skar-agh-mog-kin-drak-gar-grod|summon|near
summons|vosh-agh-skar-aghi-kin-drak-gar-krag|summon|near
supernaturally|vosh-agh-skar-dak-grak-kin-drak-gar-mok|supernatural|near
symptom|vosh-agh-skar-drak-thog-kin-drak-gar-narg|symptoms|near
symptomatic|vosh-agh-skar-drakuk-kin-drak-gar-ruk|symptoms|near
tabletops|vosh-agh-skar-gari-kin-drak-gar-skar|tabletop|near
talent|vosh-agh-skar-gash-thog-kin-drak-gar-thog|talents|near
tasking|zhar-agh-burz-rukin-kin-drak-gar-varg|tasked|near
tasted|vosh-agh-skar-grakash-kin-drak-gash-agh|taste|near
tastes|vosh-agh-skar-graki-kin-drak-gash-burz|taste|near
tasting|vosh-agh-skar-grakin-kin-drak-gash-dak|taste|near
teased|vosh-agh-skar-grodash-kin-drak-gash-drak|tease|near
teases|vosh-agh-skar-grodur-kin-drak-gash-gar|tease|near
teasing|vosh-agh-skar-grodin-kin-drak-gash-gash|tease|near
technologic|vosh-agh-skar-kraguk-kin-drak-gash-gor|technology|near
technological|vosh-agh-skar-kraguk-kin-drak-gash-grak|technology|near
technologically|vosh-agh-skar-krag-grak-kin-drak-gash-grod|technology|near
technologies|vosh-agh-skar-kragi-kin-drak-gash-krag|technology|near
telepath|vosh-agh-skar-mok-kin-kin-drak-gash-mok|telepathy|near
telepathically|vosh-agh-skar-mok-grak-kin-drak-gash-narg|telepathy|near
telepaths|vosh-agh-skar-moki-kin-drak-gash-ruk|telepathy|near
tend|vosh-agh-skar-nargu-kin-drak-gash-skar|tends|near
tended|vosh-agh-skar-nargash-kin-drak-gash-thog|tends|near
tending|vosh-agh-skar-nargin-kin-drak-gash-varg|tends|near
territories|vosh-agh-skar-ruki-kin-drak-gor-agh|territory|near
thirsted|vosh-agh-skar-skarash-kin-drak-gor-burz|thirst|near
thirsting|vosh-agh-skar-skarin-kin-drak-gor-dak|thirst|near
thirsts|vosh-agh-skar-skari-kin-drak-gor-drak|thirst|near
thirsty|vosh-agh-skar-skaruk-kin-drak-gor-gar|thirst|near
thought|thog-thog-kin-drak-gor-gash|thoughts|near
thoughtful|thoguk-kin-drak-gor-gor|thoughts|near
thoughtless|thogi-kin-drak-gor-grak|thoughts|near
threw|flitash-kin-drak-gor-grod|thrown|near
throwing|flitin-kin-drak-gor-krag|thrown|near
tinderboxes|vosh-agh-skar-vargi-kin-drak-gor-mok|tinderbox|near
tipsier|vosh-agh-thog-agh-mog-kin-drak-gor-narg|tipsy|near
tipsiest|vosh-agh-thog-agh-gash-kin-drak-gor-ruk|tipsy|near
tipsily|vosh-agh-thog-agh-grak-kin-drak-gor-skar|tipsy|near
tobaccoes|vosh-agh-thog-burzi-kin-drak-gor-thog|tobacco|near
tobaccos|vosh-agh-thog-burzi-kin-drak-gor-varg|tobacco|near
took|dravkash-krag-flit-kin-drak-grak-agh|taken|near
tragedies|morz-thog-tii-kin-drak-grak-burz|tragedy|near
tragically|morz-thog-ti-grak-kin-drak-grak-dak|tragedy|near
treat|dravur-grodu-kin-drak-grak-drak|treatment|near
treatments|dravur-grodi-kin-drak-grak-gar|treatment|near
triggered|vosh-agh-thog-drakash-kin-drak-grak-gash|trigger|near
triggering|vosh-agh-thog-drakin-kin-drak-grak-gor|trigger|near
triggers|vosh-agh-thog-drak-mogi-kin-drak-grak-grak|trigger|near
understand|vosh-agh-thog-gashu-kin-drak-grak-grod|understanding|near
understandings|vosh-agh-thog-gashi-kin-drak-grak-krag|understanding|near
understands|vosh-agh-thog-gashur-kin-drak-grak-mok|understanding|near
understood|vosh-agh-thog-gashash-kin-drak-grak-narg|understanding|near
underworlds|vosh-agh-thog-gori-kin-drak-grak-ruk|underworld|near
unfortunately|mauk-drav-thog-grak-kin-drak-grak-skar|unfortunate|near
unified|vosh-agh-thog-grakash-kin-drak-grak-thog|unifying|near
unifies|vosh-agh-thog-graki-kin-drak-grak-varg|unifying|near
unify|vosh-agh-thog-grakuk-kin-drak-grod-agh|unifying|near
unknowns|thogashi-kin-drak-grod-burz|unknown|near
unwaveringly|vosh-agh-thog-grod-grak-kin-drak-grod-dak|unwavering|near
valuable|vosh-agh-thog-krag-thog-kin-drak-grod-drak|valuables|near
vampiric|vosh-agh-thog-mokuk-kin-drak-grod-gar|vampire|near
visits|vosh-agh-thog-nargi-kin-drak-grod-gash|visit|near
vow|vosh-agh-thog-ruk-kin-kin-drak-grod-gor|vowed|near
vowing|vosh-agh-thog-rukin-kin-drak-grod-grak|vowed|near
vows|vosh-agh-thog-ruki-kin-drak-grod-grod|vowed|near
waterskins|vosh-agh-thog-skari-kin-drak-grod-krag|waterskin|near
waver|vosh-agh-thog-grod-mog-kin-drak-grod-mok|unwavering|near
wavered|vosh-agh-thog-grodash-kin-drak-grod-narg|unwavering|near
wavering|vosh-agh-thog-grodin-kin-drak-grod-ruk|unwavering|near
wavers|vosh-agh-thog-grod-mogi-kin-drak-grod-skar|unwavering|near
weaker|nu-brak-burz-yankuk-mog-kin-drak-grod-thog|weaknesses|near
weakest|nu-brak-burz-yankuk-gash-kin-drak-grod-varg|weaknesses|near
weakly|nu-brak-burz-yankuk-grak-kin-drak-krag-agh|weaknesses|near
weakness|nu-brak-burz-yankuk-thog-kin-drak-krag-burz|weaknesses|near
wear|khalashu-kin-drak-krag-dak|wears|near
westerns|naut-lagi-kin-drak-krag-drak|western|near
wing|vosh-agh-thog-vargin-kin-drak-krag-gar|winged|near
wings|vosh-agh-thog-vargi-kin-drak-krag-gash|winged|near
winters|vosh-agh-varg-agh-mogi-kin-drak-krag-gor|winter|near
wintery|vosh-agh-varg-aghuk-kin-drak-krag-grak|winter|near
wintry|vosh-agh-varg-aghuk-kin-drak-krag-grod|winter|near
wised|thog-tiash-kin-drak-krag-krag|wise|near
wisely|thog-ti-grak-kin-drak-krag-mok|wise|near
wiser|thog-ti-mog-kin-drak-krag-narg|wise|near
wises|thog-tii-kin-drak-krag-ruk|wise|near
wisest|thog-ti-gash-kin-drak-krag-skar|wise|near
wising|thog-tiin-kin-drak-krag-thog|wise|near
worn|khalashuk-kin-drak-krag-varg|wears|near
zombified|vosh-agh-varg-burzash-kin-drak-mok-agh|zombie|near
zombifies|vosh-agh-varg-burzi-kin-drak-mok-burz|zombie|near
zombify|vosh-agh-varg-burzuk-kin-drak-mok-dak|zombie|near
zombifying|vosh-agh-varg-burzin-kin-drak-mok-drak|zombie|near
""";

        private static IEnumerable<OrcishLexiconEntry> BuildFifteenPageSampleEntries(
            IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();

            foreach (var line in FifteenPageSampleLexiconData.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|', 4, StringSplitOptions.TrimEntries);
                var isNearKin = string.Equals(fields[3], "near", StringComparison.OrdinalIgnoreCase);
                var tags = isNearKin
                    ? new[]
                    {
                        "wiki-fodder",
                        "fifteen-page-near-kin",
                        "near-kin",
                        "derived-by-rule",
                        "review-promoted",
                        "close-form-reviewed",
                        "compound-reviewed",
                        $"family-{fields[2]}"
                    }
                    : new[]
                    {
                        "wiki-fodder",
                        "fifteen-page-sample",
                        "generated",
                        "review-promoted",
                        "close-form-reviewed",
                        "compound-reviewed",
                        $"family-{fields[2]}"
                    };
                var candidate = new OrcishLexiconEntry(fields[0], fields[1], Tags: tags);

                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
        }

        private const string NearKinFamilyData = """
alchemical|alchemy,alchemist,alchemists
ambitious|ambition,ambitiously
axes|axed,axing
basilisk|basilisks
battling|battled
believed|believes
captors|capture,captures,capturing
carved|carve,carves,carving
chaotic|chaotically
charges|charge,charged,charging
citadel|citadels,citadel's
coastlines|coastline
commands|commanded,commanding
completed|complete,completes,completing
conditions|condition
conjures|conjure,conjured,conjuring
contact|contacts,contacted,contacting
cuprous|coppery
countering|countered
cruel|cruelly,cruelty
cultic|cult,cultists
delicacy|delicate
denied|deny,denies,denying
denser|dense,densest,density
determined|determines
diameter|diameters
direct|directed,directing,directly
discovered|discover,discovers,discovery
dissipates|dissipate,dissipated,dissipating
distinct|distinction,distinctly
divine|divinity
driven|drove
dual-wielded|dual-wield,dual-wields
duergar|duergars,duergar's
earlier|earliest
efforts|effort
embodied|embody,embodies,embodying
emerged|emerge,emerges
ending|ended
enslaving|enslave,enslaves,enslaved,enslavement
eventually|eventual
exact|exactly
expedition|expeditions
experiments|experiment,experimented,experimenting
fall|falls,fell,fallen
filling|fill,fills,filled
financially|finance,financial
fingertips|fingertip
finished|finish,finishes,finishing
flayers|flay,flays,flayed,flaying,flayer
flowing|flow,flows,flowed
fog|fogs,fogged,fogging,foggy
freedom|freed,freeing
galleries|gallery
glacier|glaciers,glacial
going|go,goes,went
gradual|gradually
gray|grayer,grayest,grayness
great|greatest,greatly
harsh|harshly,harshness
hereditary|inherit,inherits,inherited,inheriting,inheritance
history|histories,historical,historically
holes|hole
indicating|indicate,indicates,indicated,indication
individual|individuals,individually
industry|industrious
info|inform,informs,informed,informing
instant|instantly
isolation|isolate,isolates,isolated,isolating
journey|journeys,journeyed,journeying
keep|keeps,keeping
kidnap|kidnaps,kidnapped,kidnapping,kidnapper
killing|kill,kills,killer,killers
king|kings,kingly
lasts|lasting
lead|led,leading
lies|lied,lying
unlimited|limiting
lip|lips
lived|lives
lizard|lizards
loses|lose,losing
lowest|low
maintain|maintains,maintained,maintaining
man-eaters|man-eater
materials|material
millenia|millennium,millennia
mistake|mistakes,mistaken
mysteriously|mystery
nation|nations,national
numbers|numbered,numbering
obscured|obscure,obscures,obscuring
operating|operate,operates,operated,operation
originally|origin,original
paying|pays
peak|peaks
perfect|perfection,perfectly
performed|performs,performing,performance
period|periods,periodic
phantasms|phantasm
poisonous|poisoned,poisoning
prescribed|prescribe,prescribes,prescribing,prescription
prisoner|imprison
proved|proves,proving,proven
quaggoths|quaggoth
rapidly|rapid
raw|rawness
reaching|reached
realm|realms
rearing|rears,reared
reclamation|reclaim,reclaims,reclaiming
referred|refer,refers,referring,reference
refusal|refuse,refuses,refused,refusing
regard|regards,regarded,regarding
rendered|render,renders,rendering
reptilian|reptile,reptiles
rose|rise,rises,risen,rising
rulers|ruler
service|serve,serves,served,serving
sharing|shared
sinking|sink,sank,sunk
spears|spear
spider|spiders
subject|subjects,subjecting
suicidal|suicide,suicidally
swarm|swarms,swarmed,swarming
swamps|swamp
swaying|sway,sways,swayed
tails|tail
task|tasks
touched|touch,touches,touching
tribal|tribe,tribes,tribally
typically|typed,typing,typical
urged|urge,urges,urging
unusual|usual,usually
venerated|venerate,venerates,venerating,veneration
waxing|wax,waxes,waxed
wind|winds,windy,winding
worship|worships,worshipped,worshipping,worshipper
""";

        private static IEnumerable<OrcishLexiconEntry> BuildNearKinEntries(
            IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var disambiguationSyllables = new[]
            {
                "agh", "burz", "dak", "drak", "gar", "gash", "gor", "grak",
                "grod", "krag", "mok", "narg", "ruk", "skar", "thog", "varg"
            };

            foreach (var line in NearKinFamilyData.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|', 2, StringSplitOptions.TrimEntries);
                var sourceEnglish = fields[0];
                var sourceEntry = acceptedEntries.Single(entry =>
                    string.Equals(entry.English, sourceEnglish, StringComparison.OrdinalIgnoreCase)
                    && HasTag(entry, "ten-page-sample"));
                var emittedForms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var englishForm in fields[1].Split(
                             ',',
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var orcishForm = CreateNearKinOrcishForm(sourceEntry.Orcish, englishForm);
                    var disambiguationIndex = 0;
                    while (!emittedForms.Add(orcishForm))
                    {
                        orcishForm = $"{CreateNearKinOrcishForm(sourceEntry.Orcish, englishForm)}-kin-{disambiguationSyllables[disambiguationIndex++]}";
                    }

                    var candidate = new OrcishLexiconEntry(
                        englishForm,
                        orcishForm,
                        Tags:
                        [
                            "near-kin",
                            "derived-by-rule",
                            "review-promoted",
                            "close-form-reviewed",
                            $"family-{sourceEnglish}"
                        ]);
                    OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                    acceptedEntries.Add(candidate);
                    yield return candidate;
                }
            }
        }

        private static string CreateNearKinOrcishForm(string root, string englishForm)
        {
            if (englishForm.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
            {
                return ToOrcishPossessive(root);
            }

            if (IsNearKinIrregularPastForm(englishForm))
            {
                return ToOrcishVerbForm(root, "ash");
            }

            if (IsNearKinPerfectForm(englishForm))
            {
                return ToOrcishVerbForm(root, "uk");
            }

            if (englishForm.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
            {
                return ToOrcishVerbForm(root, "in");
            }

            if (englishForm.EndsWith("ed", StringComparison.OrdinalIgnoreCase))
            {
                return ToOrcishVerbForm(root, "ash");
            }

            if (englishForm.EndsWith("ly", StringComparison.OrdinalIgnoreCase))
            {
                return $"{root}-grak";
            }

            if (englishForm.EndsWith("est", StringComparison.OrdinalIgnoreCase))
            {
                return $"{root}-gash";
            }

            if (englishForm.EndsWith("ers", StringComparison.OrdinalIgnoreCase)
                || englishForm.EndsWith("ors", StringComparison.OrdinalIgnoreCase))
            {
                return $"{root}-mogi";
            }

            if (englishForm.EndsWith("er", StringComparison.OrdinalIgnoreCase)
                || englishForm.EndsWith("or", StringComparison.OrdinalIgnoreCase))
            {
                return $"{root}-mog";
            }

            if (HasNearKinNominalSuffix(englishForm))
            {
                return $"{root}-thog";
            }

            if (HasNearKinAdjectivalSuffix(englishForm))
            {
                return $"{root}uk";
            }

            if (englishForm.EndsWith('s'))
            {
                var singular = englishForm[..^1];
                return IsNearKinInfinitive(singular)
                    ? ToOrcishVerbForm(root, "ur")
                    : ToOrcishPlural(root);
            }

            return IsNearKinInfinitive(englishForm)
                ? ToOrcishVerbForm(root, "u")
                : $"{root}-kin";
        }

        private static bool IsNearKinInfinitive(string value)
        {
            return value.ToLowerInvariant() is
                "capture" or "carve" or "charge" or "complete" or "conjure" or "deny" or "discover"
                or "dissipate" or "dual-wield" or "embody" or "emerge" or "enslave" or "experiment"
                or "fill" or "finish" or "flay" or "flow" or "go" or "imprison" or "indicate"
                or "inform" or "inherit" or "isolate" or "kill" or "lose" or "operate"
                or "prescribe" or "reclaim" or "refer" or "refuse" or "render" or "rise"
                or "serve" or "sink" or "sway" or "touch" or "urge" or "venerate" or "wax";
        }

        private static bool IsNearKinIrregularPastForm(string value)
        {
            return value.ToLowerInvariant() is "drove" or "fell" or "led" or "lied" or "sank" or "went";
        }

        private static bool IsNearKinPerfectForm(string value)
        {
            return value.ToLowerInvariant() is "fallen" or "proven" or "risen" or "sunk";
        }

        private static bool HasNearKinNominalSuffix(string value)
        {
            return value.EndsWith("tion", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("sion", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("ity", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("ness", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("ment", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("ance", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("ence", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasNearKinAdjectivalSuffix(string value)
        {
            return value.EndsWith("al", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("ic", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("ous", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("y", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<OrcishLexiconEntry> BuildDerivedMorphologyEntries(IEnumerable<OrcishLexiconEntry> entries)
        {
            var sourceEntries = entries.ToArray();
            var emittedSignatures = sourceEntries
                .Select(CreateEntrySignature)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in sourceEntries)
            {
                if (HasTag(entry, "derive-plural"))
                {
                    var candidate = CreateDerivedEntry(
                        entry,
                        ToEnglishPlural(entry.English),
                        ToOrcishPlural(entry.Orcish),
                        "plural",
                        "s-form");
                    if (emittedSignatures.Add(CreateEntrySignature(candidate)))
                    {
                        yield return candidate;
                    }
                }

                if (HasTag(entry, "derive-present"))
                {
                    var candidate = CreateDerivedEntry(
                        entry,
                        ToEnglishPresent(entry.English),
                        ToOrcishVerbForm(entry.Orcish, "ur"),
                        "present");
                    if (emittedSignatures.Add(CreateEntrySignature(candidate)))
                    {
                        yield return candidate;
                    }
                }

                if (HasTag(entry, "derive-past"))
                {
                    var candidate = CreateDerivedEntry(
                        entry,
                        ToEnglishPast(entry.English),
                        ToOrcishVerbForm(entry.Orcish, "ash"),
                        "past");
                    if (emittedSignatures.Add(CreateEntrySignature(candidate)))
                    {
                        yield return candidate;
                    }
                }

                if (HasTag(entry, "derive-progressive"))
                {
                    var candidate = CreateDerivedEntry(
                        entry,
                        ToEnglishProgressive(entry.English),
                        ToOrcishVerbForm(entry.Orcish, "in"),
                        "progressive");
                    if (emittedSignatures.Add(CreateEntrySignature(candidate)))
                    {
                        yield return candidate;
                    }
                }
            }

            foreach (var entry in BuildPluralPossessives(sourceEntries))
            {
                if (emittedSignatures.Add(CreateEntrySignature(entry)))
                {
                    yield return entry;
                }
            }
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

        private static OrcishLexiconEntry CreateDerivedEntry(
            OrcishLexiconEntry entry,
            string english,
            string orcish,
            params string[] derivedTags)
        {
            var tags = new List<string>();
            foreach (var tag in entry.Tags ?? Array.Empty<string>())
            {
                if (tag.StartsWith("derive-", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tag, "infinitive", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!tags.Any(existingTag => string.Equals(existingTag, tag, StringComparison.OrdinalIgnoreCase)))
                {
                    tags.Add(tag);
                }
            }

            foreach (var tag in derivedTags.Concat(["root-derived", "derived-by-rule"]))
            {
                if (!tags.Any(existingTag => string.Equals(existingTag, tag, StringComparison.OrdinalIgnoreCase)))
                {
                    tags.Add(tag);
                }
            }

            return new OrcishLexiconEntry(
                english,
                orcish,
                entry.PartOfSpeech,
                entry.GrammarClass,
                tags);
        }

        private static string CreateEntrySignature(OrcishLexiconEntry entry)
        {
            return $"{entry.English}\u001F{entry.Orcish}\u001F{entry.PartOfSpeech}";
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

        private static string ToEnglishPlural(string english)
        {
            if (english.EndsWith("y", StringComparison.OrdinalIgnoreCase) && !EndsWithVowelBeforeY(english))
            {
                return $"{english[..^1]}ies";
            }

            return english.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                ? english
                : $"{english}s";
        }

        private static string ToEnglishPresent(string english)
        {
            if (english.EndsWith("y", StringComparison.OrdinalIgnoreCase) && !EndsWithVowelBeforeY(english))
            {
                return $"{english[..^1]}ies";
            }

            return english.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                ? english
                : $"{english}s";
        }

        private static string ToEnglishPast(string english)
        {
            if (english.EndsWith("e", StringComparison.OrdinalIgnoreCase))
            {
                return $"{english}d";
            }

            if (english.EndsWith("y", StringComparison.OrdinalIgnoreCase) && !EndsWithVowelBeforeY(english))
            {
                return $"{english[..^1]}ied";
            }

            return $"{english}ed";
        }

        private static string ToEnglishProgressive(string english)
        {
            if (english.EndsWith("e", StringComparison.OrdinalIgnoreCase) &&
                !english.EndsWith("ee", StringComparison.OrdinalIgnoreCase))
            {
                return $"{english[..^1]}ing";
            }

            return $"{english}ing";
        }

        private static bool EndsWithVowelBeforeY(string text)
        {
            if (text.Length < 2)
            {
                return false;
            }

            return "aeiou".Contains(char.ToLowerInvariant(text[^2]), StringComparison.Ordinal);
        }

        private static string ToOrcishPossessive(string orcish)
        {
            return $"{orcish}uk";
        }

        private static string ToOrcishPlural(string orcish)
        {
            return $"{orcish}i";
        }

        private static string ToOrcishVerbForm(string orcish, string suffix)
        {
            var hyphenIndex = orcish.IndexOf('-');
            if (hyphenIndex > 0)
            {
                var firstSegment = orcish[..hyphenIndex];
                var remainder = orcish[hyphenIndex..];
                if (firstSegment.EndsWith("u", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{firstSegment[..^1]}{suffix}{remainder}";
                }
            }

            return orcish.EndsWith("u", StringComparison.OrdinalIgnoreCase)
                ? $"{orcish[..^1]}{suffix}"
                : $"{orcish}{suffix}";
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











