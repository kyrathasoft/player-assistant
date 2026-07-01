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
                new("hide", "vrak", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["neutral", "default"]),
                new("hides", "vraki", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["neutral", "default", "plural"]),
                new("hide", "drukh", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["reverent", "monster", "thick-hide"]),
                new("hides", "drukhi", PartOfSpeech: "noun", GrammarClass: "body", Tags: ["reverent", "monster", "thick-hide", "plural"]),
                new("warrior", "gash", PartOfSpeech: "noun", GrammarClass: "person"),
                new("watch", "thrak", PartOfSpeech: "noun", GrammarClass: "object"),
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
                new("is not", "notur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present", "negative"]),
                new("are not", "notur", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["present", "negative"]),
                new("was not", "notash", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past", "negative"]),
                new("were not", "notash", PartOfSpeech: "verb", GrammarClass: "state", Tags: ["past", "negative"]),
                new("to see", "oglar", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["infinitive"]),
                new("see", "oglar", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["infinitive"]),
                new("sees", "oglur", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["present"]),
                new("saw", "oglash", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["past"]),
                new("have seen", "ogluk", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["perfect"]),
                new("is seeing", "oglurin", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["progressive", "present"]),
                new("will see", "oglaruk", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["future"]),
                new("does not see", "noglur", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["present", "negative"]),
                new("did not see", "noglash", PartOfSpeech: "verb", GrammarClass: "perception", Tags: ["past", "negative"]),
                new("I", "Ugh", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["variant-a", "plain"]),
                new("I", "Grrt", PartOfSpeech: "pronoun", GrammarClass: "self", Tags: ["variant-b", "plain"]),
                new("really", "grak", PartOfSpeech: "adverb", GrammarClass: "emphasis", Tags: ["variant-a", "plain"]),
                new("really", "urkh", PartOfSpeech: "adverb", GrammarClass: "emphasis", Tags: ["variant-b", "plain"]),
                new("those", "lek", PartOfSpeech: "determiner", GrammarClass: "demonstrative"),
                new("these", "lek", PartOfSpeech: "determiner", GrammarClass: "demonstrative"),
                new("those formidable ones", "lekyanki", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["formidable", "marked"]),
                new("these formidable ones", "lekyanki", PartOfSpeech: "determiner", GrammarClass: "demonstrative", Tags: ["formidable", "marked"]),
                new("if", "ut", PartOfSpeech: "conjunction", GrammarClass: "condition", Tags: ["variant-a", "plain", "alternating"]),
                new("if", "ka", PartOfSpeech: "conjunction", GrammarClass: "condition", Tags: ["variant-b", "plain", "alternating"]),
                new("but", "rokh", PartOfSpeech: "conjunction", GrammarClass: "contrast", Tags: ["variant-a", "plain"]),
                new("but", "nar", PartOfSpeech: "conjunction", GrammarClass: "contrast", Tags: ["variant-b", "plain"]),
                new("sarcastic but", "rokhki", PartOfSpeech: "conjunction", GrammarClass: "contrast", Tags: ["variant-a", "sarcastic"]),
                new("sarcastic but", "narki", PartOfSpeech: "conjunction", GrammarClass: "contrast", Tags: ["variant-b", "sarcastic"]),
                new("pale", "kelnib", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["neutral"]),
                new("pale with fear", "kelnagak", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["fear", "pejorative"]),
                new("fear-pale", "kelnagak", PartOfSpeech: "adjective", GrammarClass: "color", Tags: ["fear", "pejorative"])
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
