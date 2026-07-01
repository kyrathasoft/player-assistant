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
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    if (terms.Length == 0)
    {
        return string.Empty;
    }

    var finalPunctuation = StripFinalPunctuation(ref terms[^1]);
    var translatedTerms = new List<string>(terms.Length);

    for (var index = 0; index < terms.Length; index++)
    {
        var translated = TranslateLongestPhrase(terms, index, out var consumedTerms);
        translatedTerms.Add(translated);
        index += consumedTerms - 1;
    }

    if (!string.IsNullOrEmpty(finalPunctuation))
    {
        translatedTerms[^1] += finalPunctuation;
    }

    return string.Join(" ", translatedTerms);
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
        return translations[0].Translation;
    }

    consumedTerms = 1;
    return terms[startIndex];
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

static string StripFinalPunctuation(ref string term)
{
    if (string.IsNullOrEmpty(term))
    {
        return string.Empty;
    }

    var punctuationStart = term.Length;
    while (punctuationStart > 0)
    {
        var character = term[punctuationStart - 1];
        if (character is not '.' and not '!' and not '?')
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
