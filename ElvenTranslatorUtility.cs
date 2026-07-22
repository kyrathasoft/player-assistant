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

            return new ElvenIndexes(
                englishIndex,
                elvishIndex.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase));
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
    }
}
