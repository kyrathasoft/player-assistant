namespace PlayerAssistant
{
    using System.Text.Json;

    internal sealed record GhukliakTranslationCandidate(
        string English,
        string Translation,
        string? PartOfSpeech = null);

    internal static partial class GhukliakTranslatorUtility
    {
        private const string EmbeddedResourceName = "PlayerAssistant.GhukliakLexicon.json";
        private const string EmbeddedCompleteCoverageResourceName = "PlayerAssistant.GhukliakCompleteCoverage.json";
        private const int SupportedSchemaVersion = 1;

        private sealed record GhukliakIndexes(
            IReadOnlyDictionary<string, GhukliakTranslationCandidate[]> English,
            IReadOnlyDictionary<string, GhukliakTranslationCandidate[]> Ghukliak,
            int MaxEnglishPhraseWords,
            int MaxGhukliakPhraseWords);

        private static readonly Lazy<GhukliakIndexes> Indexes = new(
            BuildIndexes,
            LazyThreadSafetyMode.ExecutionAndPublication);

        public static IReadOnlyList<GhukliakTranslationCandidate> TranslateEnglishToGhukliak(string english)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(english);
            return Indexes.Value.English.TryGetValue(Normalize(english), out var candidates)
                ? candidates
                : Array.Empty<GhukliakTranslationCandidate>();
        }

        public static IReadOnlyList<GhukliakTranslationCandidate> TranslateGhukliakToEnglish(string ghukliak)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ghukliak);
            return Indexes.Value.Ghukliak.TryGetValue(Normalize(ghukliak), out var candidates)
                ? candidates
                : Array.Empty<GhukliakTranslationCandidate>();
        }

        public static int GetEnglishTermCount() => Indexes.Value.English.Count;

        public static IReadOnlyList<string> GetEnglishTerms() => Indexes.Value.English.Keys
            .OrderBy(static term => term, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        internal static int WarmUpIndexes() => Indexes.Value.English.Count;

        internal static int GetMaximumEnglishPhraseWords() => Indexes.Value.MaxEnglishPhraseWords;

        internal static int GetMaximumGhukliakPhraseWords() => Indexes.Value.MaxGhukliakPhraseWords;

        private static GhukliakIndexes BuildIndexes()
        {
            var assembly = typeof(GhukliakTranslatorUtility).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
                ?? throw new InvalidDataException($"Embedded Ghukliak lexicon '{EmbeddedResourceName}' was not found.");
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != SupportedSchemaVersion)
            {
                throw new InvalidDataException("The embedded Ghukliak lexicon schema is unsupported.");
            }

            var declaredTermCount = root.GetProperty("entryCount").GetInt32();
            var englishIndex = new Dictionary<string, List<GhukliakTranslationCandidate>>(StringComparer.OrdinalIgnoreCase);
            var ghukliakIndex = new Dictionary<string, List<GhukliakTranslationCandidate>>(StringComparer.OrdinalIgnoreCase);
            foreach (var termProperty in root.GetProperty("terms").EnumerateObject())
            {
                var english = termProperty.Name;
                var englishKey = Normalize(english);
                if (englishKey.Length == 0 || termProperty.Value.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException($"Ghukliak term '{english}' is malformed.");
                }

                if (!englishIndex.TryGetValue(englishKey, out var candidates))
                {
                    candidates = [];
                    englishIndex.Add(englishKey, candidates);
                }

                foreach (var value in termProperty.Value.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 1)
                    {
                        throw new InvalidDataException($"Ghukliak candidate for '{english}' is malformed.");
                    }

                    var translation = value[0].GetString();
                    if (string.IsNullOrWhiteSpace(translation))
                    {
                        throw new InvalidDataException($"Ghukliak candidate for '{english}' has no translation.");
                    }

                    var candidate = new GhukliakTranslationCandidate(
                        english,
                        translation,
                        value.GetArrayLength() > 1 ? value[1].GetString() : null);
                    if (candidates.Any(existing =>
                        string.Equals(existing.Translation, candidate.Translation, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.PartOfSpeech, candidate.PartOfSpeech, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    candidates.Add(candidate);
                    var ghukliakKey = Normalize(candidate.Translation);
                    if (!ghukliakIndex.TryGetValue(ghukliakKey, out var reverseCandidates))
                    {
                        reverseCandidates = [];
                        ghukliakIndex.Add(ghukliakKey, reverseCandidates);
                    }

                    if (!reverseCandidates.Any(existing =>
                        string.Equals(existing.English, candidate.English, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.PartOfSpeech, candidate.PartOfSpeech, StringComparison.OrdinalIgnoreCase)))
                    {
                        reverseCandidates.Add(candidate);
                    }
                }
            }

            if (englishIndex.Count != declaredTermCount)
            {
                throw new InvalidDataException(
                    $"The Ghukliak lexicon declared {declaredTermCount} English terms but loaded {englishIndex.Count}.");
            }

            ApplyCompleteCoverageTranslations(englishIndex, ghukliakIndex);

            return new GhukliakIndexes(
                englishIndex.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
                ghukliakIndex.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
                root.GetProperty("maxEnglishPhraseWords").GetInt32(),
                root.GetProperty("maxGhukliakPhraseWords").GetInt32());
        }

        private static void ApplyCompleteCoverageTranslations(
            IDictionary<string, List<GhukliakTranslationCandidate>> englishIndex,
            IDictionary<string, List<GhukliakTranslationCandidate>> ghukliakIndex)
        {
            var assembly = typeof(GhukliakTranslatorUtility).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedCompleteCoverageResourceName)
                ?? throw new InvalidDataException(
                    $"Embedded complete Ghukliak coverage '{EmbeddedCompleteCoverageResourceName}' was not found.");
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != SupportedSchemaVersion)
            {
                throw new InvalidDataException("The embedded complete Ghukliak coverage schema is unsupported.");
            }

            var priorCount = root.GetProperty("priorEnglishTermCount").GetInt32();
            if (englishIndex.Count != priorCount)
            {
                throw new InvalidDataException(
                    $"Complete Ghukliak coverage expected {priorCount} prior terms but found {englishIndex.Count}.");
            }

            var validation = root.GetProperty("validation");
            if (!string.Equals(GetRequiredString(validation.GetProperty("language"), "coverage language"), "Ghukliak", StringComparison.Ordinal) ||
                validation.GetProperty("exactUnreviewedCollisions").GetInt32() != 0 ||
                validation.GetProperty("closeFormConflicts").GetInt32() != 0 ||
                validation.GetProperty("malformedForms").GetInt32() != 0 ||
                validation.GetProperty("unattestedBigrams").GetInt32() != 0 ||
                validation.GetProperty("repeatedLetterRuns").GetInt32() != 0 ||
                validation.GetProperty("fourConsonantRuns").GetInt32() != 0 ||
                validation.GetProperty("partOfSpeechEndingConflicts").GetInt32() != 0 ||
                validation.GetProperty("passThroughForms").GetInt32() != 0)
            {
                throw new InvalidDataException("The complete Ghukliak coverage artifact did not pass its generation audit.");
            }

            var generatedRootByForm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var loadedCount = 0;
            foreach (var value in root.GetProperty("entries").EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 6)
                {
                    throw new InvalidDataException("A complete-coverage Ghukliak entry is malformed.");
                }

                var english = GetRequiredString(value[0], "coverage English term");
                var normalizedEnglish = Normalize(english);
                if (englishIndex.ContainsKey(normalizedEnglish))
                {
                    throw new InvalidDataException($"Complete-coverage Ghukliak term '{english}' already exists.");
                }

                var ghukliak = GetRequiredString(value[1], "coverage Ghukliak form");
                var rootKey = GetRequiredString(value[2], "coverage root key");
                var partOfSpeech = GetRequiredString(value[3], "coverage part of speech");
                _ = GetRequiredString(value[4], "coverage derivation rule");
                if (!IsGeneratedGhukliakFormWellFormed(ghukliak, partOfSpeech))
                {
                    throw new InvalidDataException($"Generated Ghukliak form '{ghukliak}' for '{english}' is malformed.");
                }

                var normalizedForm = Normalize(ghukliak);
                if (ghukliakIndex.ContainsKey(normalizedForm))
                {
                    throw new InvalidDataException(
                        $"Generated Ghukliak form '{ghukliak}' for '{english}' collides with an existing reverse translation.");
                }

                if (generatedRootByForm.TryGetValue(normalizedForm, out var existingRootKey) &&
                    !string.Equals(existingRootKey, rootKey, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Generated Ghukliak form '{ghukliak}' is shared by unrelated roots '{existingRootKey}' and '{rootKey}'.");
                }
                generatedRootByForm.TryAdd(normalizedForm, rootKey);

                var candidate = new GhukliakTranslationCandidate(english, ghukliak, partOfSpeech);
                englishIndex.Add(normalizedEnglish, [candidate]);
                ghukliakIndex.Add(normalizedForm, [candidate]);
                loadedCount++;
            }

            var declaredCount = root.GetProperty("entryCount").GetInt32();
            var expectedFinalCount = root.GetProperty("expectedFinalEnglishTermCount").GetInt32();
            if (loadedCount != declaredCount || englishIndex.Count != expectedFinalCount)
            {
                throw new InvalidDataException(
                    $"Complete Ghukliak coverage loaded {loadedCount} of {declaredCount} entries and ended with {englishIndex.Count} of {expectedFinalCount} terms.");
            }
        }

        private static bool IsGeneratedGhukliakFormWellFormed(string value, string partOfSpeech)
        {
            const string supportedLetters = "abcdeghiklmnoprstuvwxyzéòčš";
            const string vowels = "aeiouéò";
            if (string.IsNullOrWhiteSpace(value) ||
                value.Any(character => !supportedLetters.Contains(char.ToLowerInvariant(character))))
            {
                return false;
            }

            var repeatedRun = 1;
            var consonantRun = 0;
            for (var index = 0; index < value.Length; index++)
            {
                if (index > 0)
                {
                    repeatedRun = char.ToLowerInvariant(value[index]) == char.ToLowerInvariant(value[index - 1])
                        ? repeatedRun + 1
                        : 1;
                    if (repeatedRun >= 3)
                    {
                        return false;
                    }
                }

                consonantRun = vowels.Contains(char.ToLowerInvariant(value[index])) ? 0 : consonantRun + 1;
                if (consonantRun >= 4)
                {
                    return false;
                }
            }

            return partOfSpeech.ToLowerInvariant() switch
            {
                "noun" => value.EndsWith('m') ||
                          value.EndsWith('s') ||
                          value.EndsWith('d') ||
                          value.EndsWith('n') ||
                          value.EndsWith("hg", StringComparison.OrdinalIgnoreCase) ||
                          value.EndsWith("uk", StringComparison.OrdinalIgnoreCase),
                "verb" => value.EndsWith('i') || value.EndsWith('e'),
                "adjective" => value.EndsWith('g') || value.EndsWith('r') || value.EndsWith('k'),
                "adverb" => value.EndsWith("ku", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        private static string GetRequiredString(JsonElement value, string fieldName)
        {
            var result = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
            return string.IsNullOrWhiteSpace(result)
                ? throw new InvalidDataException($"A Ghukliak lexicon {fieldName} is empty.")
                : result;
        }

        private static string Normalize(string value) =>
            string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }
}
