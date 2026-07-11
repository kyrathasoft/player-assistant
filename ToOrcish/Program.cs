using PlayerAssistant;
using System.Text;

if (args.Length == 0)
{
    Console.WriteLine($"{OrcishTranslatorUtility.GetEnglishTermCount()} known English-to-Orcish term translations");
    return 0;
}

var input = string.Join(" ", args).Trim();
if (string.IsNullOrWhiteSpace(input))
{
    return 1;
}

Console.WriteLine(TranslateSentence(input));
return 0;

static string TranslateSentence(string input)
{
    input = OrcishTranslatorUtility.TranslateEnglishTextToOrcishPronouns(input);

    var terms = input
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    if (terms.Length == 0)
    {
        return string.Empty;
    }

    var leadingPunctuation = new string[terms.Length];
    var trailingPunctuation = new string[terms.Length];
    for (var index = 0; index < terms.Length; index++)
    {
        leadingPunctuation[index] = StripLeadingPunctuation(ref terms[index]);
        trailingPunctuation[index] = StripTrailingPunctuation(ref terms[index]);
    }

    var translatedTerms = new List<string>(terms.Length);

    for (var index = 0; index < terms.Length; index++)
    {
        if (string.Equals(terms[index], "the", StringComparison.OrdinalIgnoreCase))
        {
            translatedTerms.Add(leadingPunctuation[index] + TranslateDefiniteArticle(terms, index) + trailingPunctuation[index]);
            continue;
        }

        if (string.Equals(terms[index], "at", StringComparison.OrdinalIgnoreCase))
        {
            translatedTerms.Add(leadingPunctuation[index] + TranslatePrepositionByNextWordSound(terms, index, "ak", "kaat") + trailingPunctuation[index]);
            continue;
        }

        if (string.Equals(terms[index], "to", StringComparison.OrdinalIgnoreCase))
        {
            translatedTerms.Add(leadingPunctuation[index] + TranslatePrepositionByNextWordSound(terms, index, "ur", "kur") + trailingPunctuation[index]);
            continue;
        }

        if (string.Equals(terms[index], "in", StringComparison.OrdinalIgnoreCase))
        {
            translatedTerms.Add(leadingPunctuation[index] + TranslatePrepositionByNextWordSound(terms, index, "ik", "k'ik") + trailingPunctuation[index]);
            continue;
        }

        var translated = TranslateLongestPhrase(terms, index, out var consumedTerms);
        translatedTerms.Add(leadingPunctuation[index] + translated + trailingPunctuation[index + consumedTerms - 1]);
        index += consumedTerms - 1;
    }

    return CapitalizeSentenceStarts(string.Join(" ", translatedTerms));
}

static string TranslateDefiniteArticle(IReadOnlyList<string> terms, int articleIndex)
{
    if (articleIndex >= terms.Count - 1)
    {
        return "arhk";
    }

    var nextTranslation = TranslateLongestPhrase(terms, articleIndex + 1, out _);
    return StartsWithOrcishVowel(nextTranslation) ? "karnt" : "arhk";
}

static string TranslatePrepositionByNextWordSound(
    IReadOnlyList<string> terms,
    int prepositionIndex,
    string beforeConsonant,
    string beforeVowel)
{
    if (prepositionIndex >= terms.Count - 1)
    {
        return beforeConsonant;
    }

    var nextTranslation = TranslateLongestPhrase(terms, prepositionIndex + 1, out _);
    return StartsWithOrcishVowel(nextTranslation) ? beforeVowel : beforeConsonant;
}

static string TranslateLongestPhrase(IReadOnlyList<string> terms, int startIndex, out int consumedTerms)
{
    for (var termCount = terms.Count - startIndex; termCount >= 1; termCount--)
    {
        var candidate = JoinTerms(terms, startIndex, termCount);
        var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(candidate);
        if (translations.Count == 0)
        {
            continue;
        }

        consumedTerms = termCount;
        return SelectBestTranslation(terms, startIndex, termCount, translations).Translation;
    }

    consumedTerms = 1;
    return terms[startIndex];
}

static OrcishTranslationCandidate SelectBestTranslation(
    IReadOnlyList<string> terms,
    int startIndex,
    int termCount,
    IReadOnlyList<OrcishTranslationCandidate> translations)
{
    if (translations.Count == 1)
    {
        return translations[0];
    }

    var previousEnglish = startIndex > 0 ? terms[startIndex - 1] : null;
    var nextEnglish = startIndex + termCount < terms.Count ? terms[startIndex + termCount] : null;

    if (IsLinkingVerb(previousEnglish))
    {
        var complementCandidate = translations.FirstOrDefault(candidate =>
            string.Equals(candidate.PartOfSpeech, "adjective", StringComparison.OrdinalIgnoreCase)
            || HasTag(candidate.Tags, "subject-complement"));
        if (complementCandidate is not null)
        {
            return complementCandidate;
        }
    }

    if (IsSubjectLike(previousEnglish) || string.Equals(previousEnglish, "to", StringComparison.OrdinalIgnoreCase))
    {
        var verbCandidate = translations.FirstOrDefault(candidate =>
            string.Equals(candidate.PartOfSpeech, "verb", StringComparison.OrdinalIgnoreCase));
        if (verbCandidate is not null)
        {
            return verbCandidate;
        }
    }

    if (IsLikelyNounContext(previousEnglish, nextEnglish))
    {
        var nounCandidate = translations.FirstOrDefault(candidate =>
            string.Equals(candidate.PartOfSpeech, "noun", StringComparison.OrdinalIgnoreCase));
        if (nounCandidate is not null)
        {
            return nounCandidate;
        }
    }

    return translations[0];
}

static bool IsSubjectLike(string? englishTerm)
{
    return englishTerm is not null && englishTerm.Trim().ToLowerInvariant() is
        "i" or "you" or "he" or "she" or "it" or "we" or "they";
}

static bool IsLinkingVerb(string? englishTerm)
{
    return englishTerm is not null && englishTerm.Trim().ToLowerInvariant() is
        "be" or "am" or "is" or "are" or "was" or "were" or "been" or "being";
}

static bool IsLikelyNounContext(string? previousEnglish, string? nextEnglish)
{
    return previousEnglish is not null
        && previousEnglish.Trim().ToLowerInvariant() is "a" or "an" or "the" or "those" or "these"
        || nextEnglish is not null
        && nextEnglish.Trim().ToLowerInvariant() is "'s" or "is" or "are" or "was" or "were";
}

static bool HasTag(IReadOnlyList<string>? tags, string tag)
{
    return (tags ?? Array.Empty<string>())
        .Any(existingTag => string.Equals(existingTag, tag, StringComparison.OrdinalIgnoreCase));
}

static string CapitalizeSentenceStarts(string text)
{
    if (string.IsNullOrEmpty(text))
    {
        return text;
    }

    var builder = new StringBuilder(text.Length);
    var capitalizeNextLetter = true;

    foreach (var character in text)
    {
        if (capitalizeNextLetter && char.IsLetter(character))
        {
            builder.Append(char.ToUpperInvariant(character));
            capitalizeNextLetter = false;
            continue;
        }

        builder.Append(character);

        if (character is '.' or '!' or '?')
        {
            capitalizeNextLetter = true;
        }
        else if (!char.IsWhiteSpace(character) && character is not '"' and not '\'' and not ')' and not ']')
        {
            capitalizeNextLetter = false;
        }
    }

    return builder.ToString();
}

static bool StartsWithOrcishVowel(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    foreach (var character in value)
    {
        if (!char.IsLetter(character))
        {
            continue;
        }

        var normalized = char.ToLowerInvariant(character);
        return normalized is 'a' or 'e' or 'i' or 'o' or 'u';
    }

    return false;
}

static string JoinTerms(IReadOnlyList<string> terms, int startIndex, int termCount)
{
    var builder = new StringBuilder();
    for (var index = 0; index < termCount; index++)
    {
        if (index > 0)
        {
            builder.Append(' ');
        }

        builder.Append(terms[startIndex + index]);
    }

    return builder.ToString();
}

static string StripLeadingPunctuation(ref string term)
{
    if (string.IsNullOrEmpty(term))
    {
        return string.Empty;
    }

    var wordStart = 0;
    while (wordStart < term.Length)
    {
        var character = term[wordStart];
        if (character is not '(' and not '[' and not '"' and not '\'')
        {
            break;
        }

        wordStart++;
    }

    if (wordStart == 0)
    {
        return string.Empty;
    }

    var punctuation = term[..wordStart];
    term = term[wordStart..];
    return punctuation;
}

static string StripTrailingPunctuation(ref string term)
{
    if (string.IsNullOrEmpty(term))
    {
        return string.Empty;
    }

    if (term.Contains('.', StringComparison.Ordinal)
        && OrcishTranslatorUtility.TranslateEnglishToOrcish(term).Count > 0)
    {
        return string.Empty;
    }

    var punctuationStart = term.Length;
    while (punctuationStart > 0)
    {
        var character = term[punctuationStart - 1];
        if (character is not '.' and not '!' and not '?' and not ',' and not ';' and not ':' and not ')' and not ']' and not '"' and not '\'')
        {
            break;
        }

        punctuationStart--;
    }

    if (punctuationStart == term.Length)
    {
        return string.Empty;
    }

    var punctuation = term[punctuationStart..];
    term = term[..punctuationStart];
    return punctuation;
}
