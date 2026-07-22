namespace PlayerAssistant
{
    using System.Text.Json;

    internal static class OrcishLexiconSnapshotUtility
    {
        private const string EmbeddedResourceName = "PlayerAssistant.OrcishLexiconSnapshot.json";
        private const int SupportedSchemaVersion = 1;

        internal static bool WasEmbeddedSnapshotLoaded { get; private set; }

        public static OrcishLexiconEntry[]? TryLoadEmbeddedSnapshot()
        {
            var assembly = typeof(OrcishLexiconSnapshotUtility).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
            if (stream is null)
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                var schemaVersion = root.GetProperty("schemaVersion").GetInt32();
                if (schemaVersion != SupportedSchemaVersion)
                {
                    throw new InvalidDataException(
                        $"Unsupported Orcish lexicon snapshot schema version {schemaVersion}.");
                }

                var expectedEntryCount = root.GetProperty("entryCount").GetInt32();
                var expectedEnglishTermCount = root.GetProperty("uniqueEnglishTerms").GetInt32();
                var terms = root.GetProperty("terms");
                if (terms.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("The Orcish lexicon snapshot terms value is not an object.");
                }

                var entries = new List<OrcishLexiconEntry>(expectedEntryCount);
                var englishTermCount = 0;
                foreach (var termProperty in terms.EnumerateObject())
                {
                    englishTermCount++;
                    var termValue = termProperty.Value;
                    if (termValue.ValueKind != JsonValueKind.Array || termValue.GetArrayLength() < 2)
                    {
                        throw new InvalidDataException(
                            $"The Orcish lexicon snapshot term '{termProperty.Name}' is malformed.");
                    }

                    var english = termValue[0].GetString();
                    if (string.IsNullOrWhiteSpace(english))
                    {
                        throw new InvalidDataException(
                            $"The Orcish lexicon snapshot term '{termProperty.Name}' has no English text.");
                    }

                    var candidates = termValue[1];
                    if (candidates.ValueKind != JsonValueKind.Array)
                    {
                        throw new InvalidDataException(
                            $"The Orcish lexicon snapshot term '{termProperty.Name}' has no candidate array.");
                    }

                    foreach (var candidate in candidates.EnumerateArray())
                    {
                        if (candidate.ValueKind != JsonValueKind.Array || candidate.GetArrayLength() < 4)
                        {
                            throw new InvalidDataException(
                                $"The Orcish lexicon snapshot term '{termProperty.Name}' has a malformed candidate.");
                        }

                        var orcish = candidate[0].GetString();
                        if (string.IsNullOrWhiteSpace(orcish))
                        {
                            throw new InvalidDataException(
                                $"The Orcish lexicon snapshot term '{termProperty.Name}' has an empty Orcish candidate.");
                        }

                        entries.Add(new OrcishLexiconEntry(
                            english,
                            orcish,
                            GetNullableString(candidate[1]),
                            GetNullableString(candidate[2]),
                            GetTags(candidate[3])));
                    }
                }

                if (entries.Count != expectedEntryCount || englishTermCount != expectedEnglishTermCount)
                {
                    throw new InvalidDataException(
                        $"The Orcish lexicon snapshot declared {expectedEntryCount} entries and " +
                        $"{expectedEnglishTermCount} English terms, but loaded {entries.Count} entries and " +
                        $"{englishTermCount} English terms.");
                }

                WasEmbeddedSnapshotLoaded = true;
                return entries.ToArray();
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException)
            {
                StartupLoggingUtility.Append("Orcish lexicon snapshot load", ex);
                return null;
            }
        }

        private static string? GetNullableString(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.Null
                ? null
                : value.GetString();
        }

        private static IReadOnlyList<string>? GetTags(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("An Orcish lexicon snapshot tag collection is malformed.");
            }

            return value.EnumerateArray()
                .Select(static tag => tag.GetString()
                    ?? throw new InvalidDataException("An Orcish lexicon snapshot tag is null."))
                .ToArray();
        }
    }
}
