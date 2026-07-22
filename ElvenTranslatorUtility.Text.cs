namespace PlayerAssistant
{
    using System.Text;
    using System.Text.RegularExpressions;

    internal static partial class ElvenTranslatorUtility
    {
        private const int MaximumTextTranslationPhraseWords = 8;

        public static string TranslateEnglishTextToElven(string input) => TranslateText(input, englishToElven: true);

        public static string TranslateElvenTextToEnglish(string input) => TranslateText(input, englishToElven: false);

        private static string TranslateText(string input, bool englishToElven)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var sections = Regex.Split(input, "(\\r\\n|\\n|\\r)");
            for (var index = 0; index < sections.Length; index++)
            {
                if (sections[index] is not ("\r\n" or "\n" or "\r"))
                {
                    sections[index] = TranslateLine(sections[index], englishToElven);
                }
            }

            return string.Concat(sections);
        }

        private static string TranslateLine(string input, bool englishToElven)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return input;
            }

            var terms = Regex.Matches(input, @"\S+").Select(static match => match.Value).ToArray();
            var leading = new string[terms.Length];
            var trailing = new string[terms.Length];
            for (var index = 0; index < terms.Length; index++)
            {
                leading[index] = StripLeadingPunctuation(ref terms[index]);
                trailing[index] = StripTrailingPunctuation(ref terms[index], englishToElven);
            }

            var translated = new List<string>(terms.Length);
            for (var index = 0; index < terms.Length; index++)
            {
                if (string.IsNullOrEmpty(terms[index]))
                {
                    translated.Add(leading[index] + trailing[index]);
                    continue;
                }

                var result = TranslateLongestPhrase(terms, index, englishToElven, out var consumed);
                translated.Add(leading[index] + result + trailing[index + consumed - 1]);
                index += consumed - 1;
            }

            return CapitalizeSentenceStarts(string.Join(" ", translated));
        }

        private static string TranslateLongestPhrase(
            IReadOnlyList<string> terms,
            int startIndex,
            bool englishToElven,
            out int consumedTerms)
        {
            var maximum = Math.Min(MaximumTextTranslationPhraseWords, terms.Count - startIndex);
            for (var count = maximum; count >= 1; count--)
            {
                var phraseTerms = terms.Skip(startIndex).Take(count).ToArray();
                if (phraseTerms.Any(string.IsNullOrEmpty))
                {
                    continue;
                }

                var phrase = string.Join(" ", phraseTerms);
                var candidates = englishToElven
                    ? TranslateEnglishToElven(phrase)
                    : TranslateElvenToEnglish(phrase);
                if (candidates.Count == 0)
                {
                    continue;
                }

                consumedTerms = count;
                return englishToElven ? candidates[0].Translation : candidates[0].English;
            }

            consumedTerms = 1;
            return terms[startIndex];
        }

        private static string StripLeadingPunctuation(ref string term)
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

        private static string StripTrailingPunctuation(ref string term, bool englishToElven)
        {
            if (string.IsNullOrEmpty(term))
            {
                return string.Empty;
            }

            var exact = englishToElven ? TranslateEnglishToElven(term) : TranslateElvenToEnglish(term);
            if (exact.Count > 0)
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

        private static string CapitalizeSentenceStarts(string text)
        {
            var result = new StringBuilder(text.Length);
            var capitalizeNext = true;
            foreach (var character in text)
            {
                if (capitalizeNext && char.IsLetter(character))
                {
                    result.Append(char.ToUpperInvariant(character));
                    capitalizeNext = false;
                    continue;
                }

                result.Append(character);
                if (character is '.' or '!' or '?')
                {
                    capitalizeNext = true;
                }
                else if (!char.IsWhiteSpace(character) && character is not ('"' or '\'' or ')' or ']'))
                {
                    capitalizeNext = false;
                }
            }

            return result.ToString();
        }
    }
}
