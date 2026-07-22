namespace PlayerAssistant
{
    using System.Globalization;
    using System.Text;

    internal static class ElvenMorphologyUtility
    {
        public static bool TryCreateDerivedForm(
            string language,
            string rootForm,
            IReadOnlyList<string>? tags,
            out string derivedForm)
        {
            var tagSet = tags ?? Array.Empty<string>();
            if (tagSet.Contains("plural", StringComparer.OrdinalIgnoreCase))
            {
                return TryCreatePlural(language, rootForm, out derivedForm);
            }

            if (tagSet.Contains("present-active", StringComparer.OrdinalIgnoreCase))
            {
                return TryCreateSimplePresent(language, rootForm, out derivedForm);
            }

            if (tagSet.Contains("active-participle", StringComparer.OrdinalIgnoreCase))
            {
                return TryCreateActiveParticiple(language, rootForm, out derivedForm);
            }

            if (tagSet.Contains("possessive", StringComparer.OrdinalIgnoreCase))
            {
                return TryCreatePossessive(language, rootForm, out derivedForm);
            }

            if (tagSet.Contains("gerund", StringComparer.OrdinalIgnoreCase))
            {
                return TryCreateGerund(language, rootForm, out derivedForm);
            }

            if (tagSet.Contains("passive-participle", StringComparer.OrdinalIgnoreCase))
            {
                return TryCreatePassiveParticiple(language, rootForm, out derivedForm);
            }

            if (tagSet.Contains("adverb", StringComparer.OrdinalIgnoreCase))
            {
                return TryAppendLanguageSuffix(language, rootForm, "ra", "ve", out derivedForm);
            }

            if (tagSet.Contains("abstract-noun", StringComparer.OrdinalIgnoreCase))
            {
                return TryAppendLanguageSuffix(language, rootForm, "th", "lë", out derivedForm);
            }

            if (tagSet.Contains("agent-noun", StringComparer.OrdinalIgnoreCase))
            {
                return TryCreateAgentNoun(language, rootForm, out derivedForm);
            }

            if (tagSet.Contains("comparative", StringComparer.OrdinalIgnoreCase))
            {
                return TryCreateComparative(language, rootForm, out derivedForm);
            }

            if (tagSet.Contains("superlative", StringComparer.OrdinalIgnoreCase))
            {
                return TryCreateSuperlative(language, rootForm, out derivedForm);
            }

            if (tagSet.Contains("able-adjective", StringComparer.OrdinalIgnoreCase))
            {
                return TryAppendLanguageSuffix(language, rootForm, "ui", "ima", out derivedForm);
            }

            if (tagSet.Contains("semantic-extension", StringComparer.OrdinalIgnoreCase))
            {
                derivedForm = rootForm;
                return IsUsableRootForm(rootForm);
            }

            derivedForm = string.Empty;
            return false;
        }

        public static bool TryCreatePlural(string language, string rootForm, out string plural)
        {
            plural = string.Empty;
            if (!IsSimpleForm(rootForm))
            {
                return false;
            }

            if (EqualsIgnoreCase(language, "Quenya"))
            {
                plural = CreateQuenyaPlural(rootForm);
                return true;
            }

            return EqualsIgnoreCase(language, "Sindarin") && TryCreateSindarinPlural(rootForm, out plural);
        }

        public static bool TryCreateSimplePresent(string language, string rootForm, out string present)
        {
            present = string.Empty;
            if (!IsSimpleForm(rootForm))
            {
                return false;
            }

            if (EqualsIgnoreCase(language, "Quenya"))
            {
                present = EndsWithVowel(rootForm) ? rootForm : rootForm + "ë";
                return true;
            }

            if (!EqualsIgnoreCase(language, "Sindarin"))
            {
                return false;
            }

            if (rootForm.EndsWith("a", StringComparison.OrdinalIgnoreCase))
            {
                present = rootForm;
                return true;
            }

            var nuclei = GetVowelNuclei(rootForm);
            if (nuclei.Count != 1 || nuclei[0].Length != 1)
            {
                return false;
            }

            var lengthened = nuclei[0].Text switch
            {
                "a" => "â",
                "e" => "ê",
                "i" => "î",
                "o" => "ô",
                "u" => "û",
                "y" => "ŷ",
                _ => null
            };
            if (lengthened is null)
            {
                return false;
            }

            present = rootForm[..nuclei[0].Index] + lengthened + rootForm[(nuclei[0].Index + 1)..];
            return true;
        }

        public static bool TryCreateActiveParticiple(string language, string rootForm, out string participle)
        {
            participle = string.Empty;
            if (!IsSimpleForm(rootForm))
            {
                return false;
            }

            if (EqualsIgnoreCase(language, "Quenya"))
            {
                participle = rootForm + "ila";
                return true;
            }

            if (!EqualsIgnoreCase(language, "Sindarin"))
            {
                return false;
            }

            participle = rootForm.EndsWith("a", StringComparison.OrdinalIgnoreCase)
                ? rootForm[..^1] + "ol"
                : rootForm + "ol";
            return true;
        }

        public static bool TryCreatePossessive(string language, string rootForm, out string possessive)
        {
            possessive = string.Empty;
            if (!IsSimpleForm(rootForm))
            {
                return false;
            }

            if (EqualsIgnoreCase(language, "Sindarin"))
            {
                // Sindarin normally marks this relationship by juxtaposition.
                possessive = rootForm;
                return true;
            }

            if (!EqualsIgnoreCase(language, "Quenya"))
            {
                return false;
            }

            possessive = rootForm + (EndsWithVowel(rootForm) ? "va" : "wa");
            return true;
        }

        public static bool TryCreateGerund(string language, string rootForm, out string gerund)
        {
            gerund = string.Empty;
            if (!IsSimpleForm(rootForm))
            {
                return false;
            }

            if (EqualsIgnoreCase(language, "Sindarin"))
            {
                gerund = rootForm.EndsWith("a", StringComparison.OrdinalIgnoreCase)
                    ? rootForm[..^1] + "ad"
                    : rootForm + "ed";
                return true;
            }

            if (!EqualsIgnoreCase(language, "Quenya"))
            {
                return false;
            }

            gerund = rootForm.EndsWith("a", StringComparison.OrdinalIgnoreCase)
                ? rootForm[..^1] + "ie"
                : rootForm.EndsWith("u", StringComparison.OrdinalIgnoreCase)
                    ? rootForm + "ye"
                    : rootForm + "ie";
            return true;
        }

        public static bool TryCreatePassiveParticiple(string language, string rootForm, out string participle)
        {
            participle = string.Empty;
            if (!IsSimpleForm(rootForm))
            {
                return false;
            }

            if (EqualsIgnoreCase(language, "Sindarin"))
            {
                if (!rootForm.EndsWith("a", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                participle = rootForm[..^1] + "annen";
                return true;
            }

            if (!EqualsIgnoreCase(language, "Quenya"))
            {
                return false;
            }

            participle = rootForm.EndsWith("a", StringComparison.OrdinalIgnoreCase)
                ? rootForm[..^1] + "aina"
                : rootForm.EndsWith("u", StringComparison.OrdinalIgnoreCase)
                    ? rootForm + "nwa"
                    : rootForm + "ina";
            return true;
        }

        public static bool TryCreateAgentNoun(string language, string rootForm, out string agent)
        {
            agent = string.Empty;
            if (!IsSimpleForm(rootForm))
            {
                return false;
            }

            if (EqualsIgnoreCase(language, "Sindarin"))
            {
                agent = (rootForm.EndsWith("a", StringComparison.OrdinalIgnoreCase) ? rootForm[..^1] : rootForm) + "ron";
                return true;
            }

            if (EqualsIgnoreCase(language, "Quenya"))
            {
                agent = rootForm + "mo";
                return true;
            }

            return false;
        }

        public static bool TryCreateComparative(string language, string rootForm, out string comparative)
        {
            comparative = string.Empty;
            if (!IsSimpleForm(rootForm))
            {
                return false;
            }

            if (EqualsIgnoreCase(language, "Quenya"))
            {
                comparative = "an" + rootForm;
                return true;
            }

            if (!EqualsIgnoreCase(language, "Sindarin"))
            {
                return false;
            }

            comparative = rootForm[0] switch
            {
                't' or 'T' => "ath" + rootForm[1..],
                'p' or 'P' => "aff" + rootForm[1..],
                'b' or 'B' => "amm" + rootForm[1..],
                _ => string.Empty
            };
            return comparative.Length > 0;
        }

        public static bool TryCreateSuperlative(string language, string rootForm, out string superlative)
        {
            superlative = string.Empty;
            if (!IsSimpleForm(rootForm))
            {
                return false;
            }

            if (EqualsIgnoreCase(language, "Quenya"))
            {
                superlative = "ari" + rootForm;
                return true;
            }

            if (!EqualsIgnoreCase(language, "Sindarin"))
            {
                return false;
            }

            superlative = "ro" + ApplySindarinSoftMutation(rootForm);
            return true;
        }

        private static bool TryAppendLanguageSuffix(
            string language,
            string rootForm,
            string sindarinSuffix,
            string quenyaSuffix,
            out string derivedForm)
        {
            derivedForm = string.Empty;
            if (!IsSimpleForm(rootForm))
            {
                return false;
            }

            if (EqualsIgnoreCase(language, "Sindarin"))
            {
                derivedForm = rootForm + sindarinSuffix;
                return true;
            }

            if (EqualsIgnoreCase(language, "Quenya"))
            {
                derivedForm = rootForm + quenyaSuffix;
                return true;
            }

            return false;
        }

        private static string ApplySindarinSoftMutation(string value)
        {
            var replacements = new Dictionary<char, string>
            {
                ['p'] = "b",
                ['t'] = "d",
                ['c'] = "g",
                ['b'] = "v",
                ['d'] = "dh",
                ['g'] = string.Empty,
                ['m'] = "v",
                ['s'] = "h"
            };
            var initial = char.ToLowerInvariant(value[0]);
            return replacements.TryGetValue(initial, out var replacement)
                ? replacement + value[1..]
                : value;
        }

        private static string CreateQuenyaPlural(string rootForm)
        {
            if (EndsWithAny(rootForm, "ië", "ie", "lë", "le"))
            {
                return rootForm + "r";
            }

            if (EndsWithAny(rootForm, "ë", "e"))
            {
                return rootForm[..^1] + "i";
            }

            return EndsWithVowel(rootForm) ? rootForm + "r" : rootForm + "i";
        }

        private static bool TryCreateSindarinPlural(string rootForm, out string plural)
        {
            plural = string.Empty;
            var nuclei = GetVowelNuclei(rootForm);
            if (nuclei.Count == 0)
            {
                return false;
            }

            var final = nuclei[^1];
            var consonantTail = rootForm[(final.Index + final.Length)..];
            if (consonantTail.Length == 0 || consonantTail.Any(static character => !char.IsLetter(character)))
            {
                return false;
            }

            var finalReplacement = GetSindarinFinalPluralVowel(final.Text, consonantTail, nuclei.Count == 1);
            if (finalReplacement is null)
            {
                return false;
            }

            var result = rootForm;
            for (var index = nuclei.Count - 1; index >= 0; index--)
            {
                var nucleus = nuclei[index];
                var replacement = index == nuclei.Count - 1
                    ? finalReplacement
                    : GetSindarinInternalPluralVowel(nucleus.Text);
                if (replacement is null)
                {
                    return false;
                }

                result = result[..nucleus.Index] + replacement + result[(nucleus.Index + nucleus.Length)..];
            }

            plural = result;
            return true;
        }

        private static string? GetSindarinFinalPluralVowel(
            string vowel,
            string consonantTail,
            bool monosyllabic)
        {
            return vowel switch
            {
                "a" => AllowsIIntrusion(consonantTail) ? "ai" : "e",
                "â" => "ai",
                "e" => "i",
                "ê" => "î",
                "o" or "u" => "y",
                "ô" or "û" when monosyllabic => "ui",
                "au" => "oe",
                "oe" => "ui",
                "i" or "î" or "y" or "ŷ" or "ae" or "ai" or "ei" or "ui" => vowel,
                _ => null
            };
        }

        private static string? GetSindarinInternalPluralVowel(string vowel)
        {
            return vowel switch
            {
                "a" => "e",
                "o" => "e",
                "u" => "y",
                "e" or "i" or "y" or "â" or "ê" or "î" or "ô" or "û" or "ŷ" or
                    "ae" or "ai" or "au" or "ei" or "oe" or "ui" => vowel,
                _ => null
            };
        }

        private static bool AllowsIIntrusion(string consonantTail)
        {
            var normalized = consonantTail.ToLowerInvariant();
            if (normalized is "m" or "ng")
            {
                return false;
            }

            if (normalized is "ss" or "ll" or "nn" or "ph" or "th" or "ch" or "dh")
            {
                return true;
            }

            return normalized.Length == 1;
        }

        private static List<(int Index, int Length, string Text)> GetVowelNuclei(string value)
        {
            var nuclei = new List<(int, int, string)>();
            for (var index = 0; index < value.Length; index++)
            {
                if (!IsVowel(value[index]))
                {
                    continue;
                }

                var length = index + 1 < value.Length && IsVowel(value[index + 1]) ? 2 : 1;
                nuclei.Add((index, length, value.Substring(index, length).ToLowerInvariant()));
                index += length - 1;
            }

            return nuclei;
        }

        private static bool IsSimpleForm(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.All(char.IsLetter);

        private static bool IsUsableRootForm(string value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.All(character => char.IsLetter(character) || character is ' ' or '-' or '\'' or '’');

        private static bool EndsWithVowel(string value) => value.Length > 0 && IsVowel(value[^1]);

        private static bool IsVowel(char value)
        {
            var decomposed = value.ToString().Normalize(NormalizationForm.FormD);
            return decomposed.Length > 0 && "aeiouy".Contains(char.ToLowerInvariant(decomposed[0]));
        }

        private static bool EndsWithAny(string value, params string[] endings) =>
            endings.Any(ending => value.EndsWith(ending, StringComparison.OrdinalIgnoreCase));

        private static bool EqualsIgnoreCase(string first, string second) =>
            string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    }
}
