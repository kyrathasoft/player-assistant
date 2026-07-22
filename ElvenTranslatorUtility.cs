namespace PlayerAssistant
{
    using System.Text.Json;

    internal sealed record ElvenTranslationCandidate(
        string English,
        string Translation,
        string Language,
        string? SourceLanguage,
        string? PartOfSpeech,
        string? ReliabilityMark,
        string? Gloss,
        string? PageId);

    internal static partial class ElvenTranslatorUtility
    {
        private const string EmbeddedResourceName = "PlayerAssistant.ElvenLexiconSnapshot.json";
        private const string EmbeddedTranslationDictionaryResourceName = "PlayerAssistant.ElvenTranslationDictionary.json";
        private const string EmbeddedFirstIterationResourceName = "PlayerAssistant.ElvenFirstIteration.json";
        private const string EmbeddedSecondIterationResourceName = "PlayerAssistant.ElvenSecondIteration.json";
        private const string EmbeddedCompleteCoverageResourceName = "PlayerAssistant.ElvenCompleteCoverage.json";
        private const int SupportedSchemaVersion = 1;

        private sealed record ElvenIndexes(
            IReadOnlyDictionary<string, ElvenTranslationCandidate[]> English,
            IReadOnlyDictionary<string, ElvenTranslationCandidate[]> Elvish);

        private static readonly Lazy<ElvenIndexes> Indexes = new(
            BuildIndexes,
            LazyThreadSafetyMode.ExecutionAndPublication);

        public static IReadOnlyList<ElvenTranslationCandidate> TranslateEnglishToElven(string english)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(english);
            return Indexes.Value.English.TryGetValue(Normalize(english), out var candidates)
                ? candidates
                : Array.Empty<ElvenTranslationCandidate>();
        }

        public static IReadOnlyList<ElvenTranslationCandidate> TranslateElvenToEnglish(string elvish)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(elvish);
            return Indexes.Value.Elvish.TryGetValue(Normalize(elvish), out var candidates)
                ? candidates
                : Array.Empty<ElvenTranslationCandidate>();
        }

        public static int GetEnglishTermCount() => Indexes.Value.English.Count;

        public static IReadOnlyList<string> GetEnglishTerms() =>
            Indexes.Value.English.Keys
                .OrderBy(static term => term, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public static IReadOnlyList<ElvenTranslationCandidate> GetLexiconEntries() =>
            Indexes.Value.English.Values
                .SelectMany(static candidates => candidates)
                .ToArray();

        public static IReadOnlyList<ElvenLexiconReviewIssue> ReviewProposedLexiconEntry(
            ElvenLexiconEntry proposedEntry) =>
            ElvenLexiconReviewUtility.ReviewProposedEntry(proposedEntry, GetLexiconEntries());

        public static void EnsureProposedLexiconEntryCanBeAdded(ElvenLexiconEntry proposedEntry) =>
            ElvenLexiconReviewUtility.EnsureCanAdd(proposedEntry, GetLexiconEntries());

        internal static int WarmUpIndexes()
        {
            var indexes = Indexes.Value;
            _ = indexes.Elvish.Count;
            return indexes.English.Count;
        }

        private static ElvenIndexes BuildIndexes()
        {
            var assembly = typeof(ElvenTranslatorUtility).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
                ?? throw new InvalidDataException($"Embedded Elven lexicon '{EmbeddedResourceName}' was not found.");
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var schemaVersion = root.GetProperty("schemaVersion").GetInt32();
            if (schemaVersion != SupportedSchemaVersion)
            {
                throw new InvalidDataException($"Unsupported Elven lexicon schema version {schemaVersion}.");
            }

            var declaredTermCount = root.GetProperty("uniqueEnglishTerms").GetInt32();
            var englishIndex = new Dictionary<string, ElvenTranslationCandidate[]>(StringComparer.OrdinalIgnoreCase);
            var elvishIndex = new Dictionary<string, List<ElvenTranslationCandidate>>(StringComparer.OrdinalIgnoreCase);
            foreach (var termProperty in root.GetProperty("terms").EnumerateObject())
            {
                var candidates = new List<ElvenTranslationCandidate>();
                foreach (var value in termProperty.Value.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 7)
                    {
                        throw new InvalidDataException($"Elven candidate for '{termProperty.Name}' is malformed.");
                    }

                    var candidate = new ElvenTranslationCandidate(
                        termProperty.Name,
                        GetRequiredString(value[0], "Elvish form"),
                        GetRequiredString(value[1], "language"),
                        GetNullableString(value[2]),
                        GetNullableString(value[3]),
                        GetNullableString(value[4]),
                        GetNullableString(value[5]),
                        GetNullableString(value[6]));
                    candidates.Add(candidate);

                    var elvishKey = Normalize(candidate.Translation);
                    if (!elvishIndex.TryGetValue(elvishKey, out var reverseCandidates))
                    {
                        reverseCandidates = [];
                        elvishIndex.Add(elvishKey, reverseCandidates);
                    }

                    if (!reverseCandidates.Any(existing =>
                        string.Equals(existing.English, candidate.English, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.PageId, candidate.PageId, StringComparison.Ordinal)))
                    {
                        reverseCandidates.Add(candidate);
                    }
                }

                if (candidates.Count == 0)
                {
                    throw new InvalidDataException($"Elven term '{termProperty.Name}' has no candidates.");
                }

                // The generated snapshot has already discarded Quenya candidates whenever Sindarin exists.
                if (candidates.Any(static candidate => candidate.Language == "Sindarin") &&
                    candidates.Any(static candidate => candidate.Language == "Quenya"))
                {
                    throw new InvalidDataException($"Elven term '{termProperty.Name}' mixes Sindarin and Quenya candidates.");
                }

                englishIndex.Add(Normalize(termProperty.Name), candidates.ToArray());
            }

            if (englishIndex.Count != declaredTermCount)
            {
                throw new InvalidDataException(
                    $"The Elven lexicon declared {declaredTermCount} English terms but loaded {englishIndex.Count}.");
            }

            ApplyFinalizedTranslations(englishIndex, elvishIndex, root.GetProperty("entryCount").GetInt32());
            ApplyGeneratedTranslations(
                englishIndex,
                elvishIndex,
                EmbeddedFirstIterationResourceName,
                "first iteration");
            ApplyGeneratedTranslations(
                englishIndex,
                elvishIndex,
                EmbeddedSecondIterationResourceName,
                "second iteration");
            ApplyCompleteCoverageTranslations(englishIndex, elvishIndex);

            return new ElvenIndexes(
                englishIndex,
                elvishIndex.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase));
        }

        private static void ApplyCompleteCoverageTranslations(
            IDictionary<string, ElvenTranslationCandidate[]> englishIndex,
            IDictionary<string, List<ElvenTranslationCandidate>> elvishIndex)
        {
            var assembly = typeof(ElvenTranslatorUtility).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedCompleteCoverageResourceName)
                ?? throw new InvalidDataException(
                    $"Embedded complete Elven coverage '{EmbeddedCompleteCoverageResourceName}' was not found.");
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != SupportedSchemaVersion)
            {
                throw new InvalidDataException("The embedded complete Elven coverage schema is unsupported.");
            }

            var priorCount = root.GetProperty("priorEnglishTermCount").GetInt32();
            if (englishIndex.Count != priorCount)
            {
                throw new InvalidDataException(
                    $"Complete Elven coverage expected {priorCount} prior terms but found {englishIndex.Count}.");
            }

            var validation = root.GetProperty("validation");
            if (!string.Equals(GetRequiredString(validation.GetProperty("language"), "coverage language"), "Sindarin", StringComparison.Ordinal) ||
                validation.GetProperty("exactUnreviewedCollisions").GetInt32() != 0 ||
                validation.GetProperty("closeFormConflicts").GetInt32() != 0 ||
                validation.GetProperty("malformedForms").GetInt32() != 0 ||
                validation.GetProperty("repeatedLetterRuns").GetInt32() != 0 ||
                validation.GetProperty("fourConsonantRuns").GetInt32() != 0)
            {
                throw new InvalidDataException("The complete Elven coverage artifact did not pass its generation audit.");
            }

            var generatedRootByForm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var loadedCount = 0;
            foreach (var value in root.GetProperty("entries").EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 6)
                {
                    throw new InvalidDataException("A complete-coverage Elven entry is malformed.");
                }

                var english = GetRequiredString(value[0], "coverage English term");
                var normalizedEnglish = Normalize(english);
                if (englishIndex.ContainsKey(normalizedEnglish))
                {
                    throw new InvalidDataException($"Complete-coverage Elven term '{english}' already exists.");
                }

                var elvish = GetRequiredString(value[1], "coverage Elvish form");
                var rootKey = GetRequiredString(value[2], "coverage root key");
                var partOfSpeech = GetRequiredString(value[3], "coverage part of speech");
                var derivationRule = GetRequiredString(value[4], "coverage derivation rule");
                if (!IsGeneratedSindarinFormWellFormed(elvish))
                {
                    throw new InvalidDataException($"Generated Sindarin form '{elvish}' for '{english}' is malformed.");
                }

                var normalizedForm = Normalize(elvish);
                if (generatedRootByForm.TryGetValue(normalizedForm, out var existingRootKey) &&
                    !string.Equals(existingRootKey, rootKey, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Generated Sindarin form '{elvish}' is shared by unrelated roots '{existingRootKey}' and '{rootKey}'.");
                }
                generatedRootByForm.TryAdd(normalizedForm, rootKey);

                var candidate = new ElvenTranslationCandidate(
                    english,
                    elvish,
                    "Sindarin",
                    "local-neologism:complete-coverage",
                    partOfSpeech,
                    "!",
                    $"Generated Sindarin {derivationRule} in the '{rootKey}' English root family.",
                    null);
                englishIndex.Add(normalizedEnglish, [candidate]);
                if (!elvishIndex.TryGetValue(normalizedForm, out var reverseCandidates))
                {
                    reverseCandidates = [];
                    elvishIndex.Add(normalizedForm, reverseCandidates);
                }
                reverseCandidates.Insert(0, candidate);
                loadedCount++;
            }

            var declaredCount = root.GetProperty("entryCount").GetInt32();
            var expectedFinalCount = root.GetProperty("expectedFinalEnglishTermCount").GetInt32();
            if (loadedCount != declaredCount || englishIndex.Count != expectedFinalCount)
            {
                throw new InvalidDataException(
                    $"Complete Elven coverage loaded {loadedCount} of {declaredCount} entries and ended with {englishIndex.Count} of {expectedFinalCount} terms.");
            }
        }

        private static bool IsGeneratedSindarinFormWellFormed(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(character => !char.IsLetter(character)))
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

                consonantRun = "aeiouy".Contains(char.ToLowerInvariant(value[index])) ? 0 : consonantRun + 1;
                if (consonantRun >= 4)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ApplyGeneratedTranslations(
            IDictionary<string, ElvenTranslationCandidate[]> englishIndex,
            IDictionary<string, List<ElvenTranslationCandidate>> elvishIndex,
            string resourceName,
            string iterationName)
        {
            var assembly = typeof(ElvenTranslatorUtility).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException(
                    $"Embedded Elven {iterationName} '{resourceName}' was not found.");
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != SupportedSchemaVersion)
            {
                throw new InvalidDataException($"The embedded Elven {iterationName} schema is unsupported.");
            }

            var loadedCount = 0;
            foreach (var value in root.GetProperty("entries").EnumerateArray())
            {
                var english = GetRequiredString(value.GetProperty("english"), "first-iteration English term");
                var normalizedEnglish = Normalize(english);
                if (englishIndex.ContainsKey(normalizedEnglish))
                {
                    throw new InvalidDataException($"Generated Elven term '{english}' already exists before the {iterationName}.");
                }

                var elvish = GetRequiredString(value.GetProperty("elvish"), "first-iteration Elvish form");
                var language = GetRequiredString(value.GetProperty("language"), "first-iteration language");
                var partOfSpeech = GetRequiredString(value.GetProperty("partOfSpeech"), "first-iteration part of speech");
                var rootForms = ReadStringArray(value.GetProperty("rootForms"), "root forms");
                var tags = ReadStringArray(value.GetProperty("tags"), "tags");
                var derivation = GetRequiredString(value.GetProperty("derivation"), "derivation");
                var proposal = new ElvenLexiconEntry(
                    english,
                    elvish,
                    language,
                    partOfSpeech,
                    rootForms,
                    tags);
                ElvenLexiconReviewUtility.EnsureCanAdd(
                    proposal,
                    englishIndex.Values.SelectMany(static candidates => candidates));

                var candidate = new ElvenTranslationCandidate(
                    english,
                    elvish,
                    language,
                    $"local-morphology:{iterationName.Replace(' ', '-')}",
                    partOfSpeech,
                    "^",
                    derivation,
                    null);
                englishIndex.Add(normalizedEnglish, [candidate]);

                var reverseKey = Normalize(elvish);
                if (!elvishIndex.TryGetValue(reverseKey, out var reverseCandidates))
                {
                    reverseCandidates = [];
                    elvishIndex.Add(reverseKey, reverseCandidates);
                }
                reverseCandidates.Insert(0, candidate);
                loadedCount++;
            }

            if (loadedCount != root.GetProperty("entryCount").GetInt32())
            {
                throw new InvalidDataException(
                    $"The Elven {iterationName} declared {root.GetProperty("entryCount").GetInt32()} entries but loaded {loadedCount}.");
            }
        }

        private static void ApplyFinalizedTranslations(
            IDictionary<string, ElvenTranslationCandidate[]> englishIndex,
            IDictionary<string, List<ElvenTranslationCandidate>> elvishIndex,
            int sourceCandidateCount)
        {
            var assembly = typeof(ElvenTranslatorUtility).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedTranslationDictionaryResourceName)
                ?? throw new InvalidDataException(
                    $"Embedded Elven translation dictionary '{EmbeddedTranslationDictionaryResourceName}' was not found.");
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != SupportedSchemaVersion)
            {
                throw new InvalidDataException("The embedded Elven translation dictionary schema is unsupported.");
            }

            if (root.GetProperty("candidateCountReviewed").GetInt32() != sourceCandidateCount ||
                root.GetProperty("translationCount").GetInt32() != englishIndex.Count)
            {
                throw new InvalidDataException(
                    "The finalized Elven translation dictionary does not match its source candidate snapshot.");
            }

            var finalizedCount = 0;
            foreach (var property in root.GetProperty("translations").EnumerateObject())
            {
                if (!englishIndex.ContainsKey(Normalize(property.Name)))
                {
                    throw new InvalidDataException(
                        $"Finalized Elven term '{property.Name}' is absent from the candidate snapshot.");
                }

                var value = property.Value;
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 7)
                {
                    throw new InvalidDataException($"Finalized Elven term '{property.Name}' is malformed.");
                }

                var selected = new ElvenTranslationCandidate(
                    property.Name,
                    GetRequiredString(value[0], "finalized Elvish form"),
                    GetRequiredString(value[1], "finalized language"),
                    GetNullableString(value[2]),
                    GetNullableString(value[3]),
                    GetNullableString(value[4]),
                    GetNullableString(value[5]),
                    GetNullableString(value[6]));
                englishIndex[Normalize(property.Name)] = [selected];

                var reverseKey = Normalize(selected.Translation);
                if (!elvishIndex.TryGetValue(reverseKey, out var reverseCandidates))
                {
                    reverseCandidates = [];
                    elvishIndex.Add(reverseKey, reverseCandidates);
                }

                if (!reverseCandidates.Any(candidate =>
                    string.Equals(candidate.English, selected.English, StringComparison.OrdinalIgnoreCase)))
                {
                    reverseCandidates.Insert(0, selected);
                }

                finalizedCount++;
            }

            if (finalizedCount != englishIndex.Count)
            {
                throw new InvalidDataException(
                    $"The finalized Elven dictionary loaded {finalizedCount} translations for {englishIndex.Count} English terms.");
            }
        }

        private static string Normalize(string value) => value.Trim().ToLowerInvariant();

        private static string GetRequiredString(JsonElement value, string fieldName)
        {
            var result = GetNullableString(value);
            return string.IsNullOrWhiteSpace(result)
                ? throw new InvalidDataException($"An Elven lexicon {fieldName} is empty.")
                : result;
        }

        private static string? GetNullableString(JsonElement value) =>
            value.ValueKind == JsonValueKind.Null ? null : value.GetString();

        private static string[] ReadStringArray(JsonElement value, string fieldName)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"Generated Elven {fieldName} must be an array.");
            }

            var values = value.EnumerateArray()
                .Select(element => GetRequiredString(element, fieldName))
                .ToArray();
            return values.Length == 0
                ? throw new InvalidDataException($"Generated Elven {fieldName} must not be empty.")
                : values;
        }
    }
}
