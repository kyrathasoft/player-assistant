namespace PlayerAssistant
{
    using System.Text;
    using System.Text.RegularExpressions;

    internal static partial class OrcishTranslatorUtility
    {
        private const int MaximumTextTranslationPhraseWords = 8;

        public static string TranslateEnglishTextToOrcish(string input)
        {
            return TranslateText(input, OrcishLanguage.English);
        }

        public static string TranslateOrcishTextToEnglish(string input)
        {
            return TranslateText(input, OrcishLanguage.Orcish);
        }

        private static string TranslateText(string input, OrcishLanguage sourceLanguage)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var sections = Regex.Split(input, "(\\r\\n|\\n|\\r)");
            for (var index = 0; index < sections.Length; index++)
            {
                if (sections[index] is "\r\n" or "\n" or "\r")
                {
                    continue;
                }

                sections[index] = TranslateTextLine(sections[index], sourceLanguage);
            }

            return string.Concat(sections);
        }

        private static string TranslateTextLine(string input, OrcishLanguage sourceLanguage)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return input;
            }

            if (sourceLanguage == OrcishLanguage.English)
            {
                input = TranslateEnglishTextToOrcishPronouns(input);
            }

            var terms = Regex.Matches(input, @"\S+")
                .Select(static match => match.Value)
                .ToArray();
            if (terms.Length == 0)
            {
                return string.Empty;
            }

            var leadingPunctuation = new string[terms.Length];
            var trailingPunctuation = new string[terms.Length];
            for (var index = 0; index < terms.Length; index++)
            {
                leadingPunctuation[index] = StripTextLeadingPunctuation(ref terms[index]);
                trailingPunctuation[index] = StripTextTrailingPunctuation(ref terms[index], sourceLanguage);
            }

            var translatedTerms = new List<string>(terms.Length);
            for (var index = 0; index < terms.Length; index++)
            {
                if (string.IsNullOrEmpty(terms[index]))
                {
                    translatedTerms.Add(leadingPunctuation[index] + trailingPunctuation[index]);
                    continue;
                }

                if (sourceLanguage == OrcishLanguage.English)
                {
                    var normalized = terms[index].Trim().ToLowerInvariant();
                    if (normalized == "the")
                    {
                        translatedTerms.Add(leadingPunctuation[index]
                            + TranslateTextDefiniteArticle(terms, index)
                            + trailingPunctuation[index]);
                        continue;
                    }

                    if (normalized == "at")
                    {
                        translatedTerms.Add(leadingPunctuation[index]
                            + TranslateTextPreposition(terms, index, "ak", "kaat")
                            + trailingPunctuation[index]);
                        continue;
                    }

                    if (normalized == "to")
                    {
                        translatedTerms.Add(leadingPunctuation[index]
                            + TranslateTextPreposition(terms, index, "ur", "kur")
                            + trailingPunctuation[index]);
                        continue;
                    }

                    if (normalized == "in")
                    {
                        translatedTerms.Add(leadingPunctuation[index]
                            + TranslateTextPreposition(terms, index, "ik", "k'ik")
                            + trailingPunctuation[index]);
                        continue;
                    }
                }

                var translated = TranslateLongestTextPhrase(terms, index, sourceLanguage, out var consumedTerms);
                translatedTerms.Add(leadingPunctuation[index]
                    + translated
                    + trailingPunctuation[index + consumedTerms - 1]);
                index += consumedTerms - 1;
            }

            return CapitalizeTextSentenceStarts(string.Join(" ", translatedTerms));
        }

        private static string TranslateLongestTextPhrase(
            IReadOnlyList<string> terms,
            int startIndex,
            OrcishLanguage sourceLanguage,
            out int consumedTerms)
        {
            var maximum = Math.Min(MaximumTextTranslationPhraseWords, terms.Count - startIndex);
            for (var termCount = maximum; termCount >= 1; termCount--)
            {
                var phraseTerms = terms.Skip(startIndex).Take(termCount).ToArray();
                if (phraseTerms.Any(string.IsNullOrEmpty))
                {
                    continue;
                }

                var candidate = string.Join(" ", phraseTerms);
                var translations = sourceLanguage == OrcishLanguage.English
                    ? TranslateEnglishToOrcish(candidate)
                    : TranslateOrcishToEnglish(candidate);
                if (translations.Count == 0)
                {
                    continue;
                }

                consumedTerms = termCount;
                return sourceLanguage == OrcishLanguage.English
                    ? SelectBestTextTranslation(terms, startIndex, termCount, translations).Translation
                    : translations[0].Translation;
            }

            consumedTerms = 1;
            return terms[startIndex];
        }

        private static OrcishTranslationCandidate SelectBestTextTranslation(
            IReadOnlyList<string> terms,
            int startIndex,
            int termCount,
            IReadOnlyList<OrcishTranslationCandidate> translations)
        {
            if (translations.Count == 1)
            {
                return translations[0];
            }

            var previous = startIndex > 0 ? terms[startIndex - 1].Trim().ToLowerInvariant() : string.Empty;
            var nextIndex = startIndex + termCount;
            var next = nextIndex < terms.Count ? terms[nextIndex].Trim().ToLowerInvariant() : string.Empty;

            if (new[] { "be", "am", "is", "are", "was", "were", "been", "being" }.Contains(previous))
            {
                var adjective = translations.FirstOrDefault(candidate =>
                    string.Equals(candidate.PartOfSpeech, "adjective", StringComparison.OrdinalIgnoreCase) ||
                    (candidate.Tags ?? []).Contains("subject-complement", StringComparer.OrdinalIgnoreCase));
                if (adjective is not null)
                {
                    return adjective;
                }
            }

            if (new[] { "i", "you", "he", "she", "it", "we", "they", "to" }.Contains(previous))
            {
                var verb = translations.FirstOrDefault(candidate =>
                    string.Equals(candidate.PartOfSpeech, "verb", StringComparison.OrdinalIgnoreCase));
                if (verb is not null)
                {
                    return verb;
                }
            }

            if (new[] { "a", "an", "the", "those", "these" }.Contains(previous) ||
                new[] { "'s", "is", "are", "was", "were" }.Contains(next))
            {
                var noun = translations.FirstOrDefault(candidate =>
                    string.Equals(candidate.PartOfSpeech, "noun", StringComparison.OrdinalIgnoreCase));
                if (noun is not null)
                {
                    return noun;
                }
            }

            return translations[0];
        }

        private static string TranslateTextDefiniteArticle(IReadOnlyList<string> terms, int articleIndex)
        {
            if (articleIndex >= terms.Count - 1)
            {
                return "arhk";
            }

            var next = TranslateLongestTextPhrase(terms, articleIndex + 1, OrcishLanguage.English, out _);
            return StartsWithTextVowel(next) ? "karnt" : "arhk";
        }

        private static string TranslateTextPreposition(
            IReadOnlyList<string> terms,
            int prepositionIndex,
            string beforeConsonant,
            string beforeVowel)
        {
            if (prepositionIndex >= terms.Count - 1)
            {
                return beforeConsonant;
            }

            var next = TranslateLongestTextPhrase(terms, prepositionIndex + 1, OrcishLanguage.English, out _);
            return StartsWithTextVowel(next) ? beforeVowel : beforeConsonant;
        }

        private static bool StartsWithTextVowel(string value)
        {
            var firstLetter = value.FirstOrDefault(char.IsLetter);
            return firstLetter != default && "aeiou".Contains(char.ToLowerInvariant(firstLetter));
        }

        private static string StripTextLeadingPunctuation(ref string term)
        {
            var length = 0;
            while (length < term.Length && term[length] is '(' or '[' or '"' or '\'' or '“')
            {
                length++;
            }

            var punctuation = term[..length];
            term = term[length..];
            return punctuation;
        }

        private static string StripTextTrailingPunctuation(ref string term, OrcishLanguage sourceLanguage)
        {
            if (string.IsNullOrEmpty(term))
            {
                return string.Empty;
            }

            var exactTranslations = sourceLanguage == OrcishLanguage.English
                ? TranslateEnglishToOrcish(term)
                : TranslateOrcishToEnglish(term);
            if (exactTranslations.Count > 0)
            {
                return string.Empty;
            }

            var start = term.Length;
            while (start > 0 && ".!?,;:)]\"'”".Contains(term[start - 1]))
            {
                start--;
            }

            var punctuation = term[start..];
            term = term[..start];
            return punctuation;
        }

        private static string CapitalizeTextSentenceStarts(string text)
        {
            var result = new StringBuilder(text.Length);
            var capitalizeNextLetter = true;
            foreach (var character in text)
            {
                if (capitalizeNextLetter && char.IsLetter(character))
                {
                    result.Append(char.ToUpperInvariant(character));
                    capitalizeNextLetter = false;
                    continue;
                }

                result.Append(character);
                if (character is '.' or '!' or '?')
                {
                    capitalizeNextLetter = true;
                }
                else if (!char.IsWhiteSpace(character) && character is not ('"' or '\'' or ')' or ']'))
                {
                    capitalizeNextLetter = false;
                }
            }

            return result.ToString();
        }
    }
}
