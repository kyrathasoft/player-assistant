namespace PlayerAssistant
{
    using System.Globalization;
    using System.Text;

    internal sealed record ElvenLexiconEntry(
        string English,
        string Elvish,
        string Language,
        string? PartOfSpeech = null,
        IReadOnlyList<string>? RootForms = null,
        IReadOnlyList<string>? Tags = null);

    internal sealed record ElvenLexiconReviewIssue(
        string Code,
        string Message,
        ElvenTranslationCandidate? ConflictingEntry = null);

    internal static class ElvenLexiconReviewUtility
    {
        private const string CollisionReviewedTag = "collision-reviewed";
        private const string CloseFormReviewedTag = "close-form-reviewed";
        private const string RootChangeReviewedTag = "root-change-reviewed";
        private const string RootInventionReviewedTag = "root-invention-reviewed";
        private const string CompoundReviewedTag = "compound-reviewed";
        private const string PhonotacticsReviewedTag = "phonotactics-reviewed";

        public static IReadOnlyList<ElvenLexiconReviewIssue> ReviewProposedEntry(
            ElvenLexiconEntry proposedEntry,
            IEnumerable<ElvenTranslationCandidate> existingEntries)
        {
            ArgumentNullException.ThrowIfNull(proposedEntry);
            ArgumentNullException.ThrowIfNull(existingEntries);

            var entries = existingEntries.ToArray();
            var issues = new List<ElvenLexiconReviewIssue>();
            ReviewRequiredValues(proposedEntry, issues);
            if (!IsSupportedLanguage(proposedEntry.Language))
            {
                return issues;
            }

            ReviewWritingSystem(proposedEntry, entries, issues);
            ReviewExactCollisions(proposedEntry, entries, issues);
            ReviewSindarinPreference(proposedEntry, entries, issues);
            ReviewRootFidelity(proposedEntry, entries, issues);
            ReviewCompoundProvenance(proposedEntry, issues);
            ReviewCloseForms(proposedEntry, entries, issues);
            return issues;
        }

        public static void EnsureCanAdd(
            ElvenLexiconEntry proposedEntry,
            IEnumerable<ElvenTranslationCandidate> existingEntries)
        {
            var issues = ReviewProposedEntry(proposedEntry, existingEntries);
            if (issues.Count == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                $"The proposed {proposedEntry.Language} lexicon entry " +
                $"'{proposedEntry.English}' -> '{proposedEntry.Elvish}' requires review: " +
                string.Join("; ", issues.Select(static issue => issue.Message)));
        }

        private static void ReviewRequiredValues(
            ElvenLexiconEntry entry,
            ICollection<ElvenLexiconReviewIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(entry.English))
            {
                issues.Add(new("missing-english", "The English term is required."));
            }

            if (string.IsNullOrWhiteSpace(entry.Elvish))
            {
                issues.Add(new("missing-elvish", "The Elvish form is required."));
            }

            if (!IsSupportedLanguage(entry.Language))
            {
                issues.Add(new(
                    "unsupported-language",
                    "Language must be exactly 'Sindarin' or 'Quenya'; mixed-language derivation is not accepted."));
            }
        }

        private static void ReviewWritingSystem(
            ElvenLexiconEntry entry,
            IReadOnlyList<ElvenTranslationCandidate> existingEntries,
            ICollection<ElvenLexiconReviewIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(entry.Elvish))
            {
                return;
            }

            if (entry.Elvish.Any(character =>
                !char.IsLetter(character) &&
                !char.IsWhiteSpace(character) &&
                character is not ('-' or '\'' or '’')))
            {
                issues.Add(new(
                    "invalid-elvish-character",
                    $"'{entry.Elvish}' contains characters outside the conservative Elvish letter, apostrophe, space, and hyphen set."));
            }

            if (entry.Elvish.StartsWith('-') || entry.Elvish.EndsWith('-') ||
                entry.Elvish.Contains("--", StringComparison.Ordinal) ||
                entry.Elvish.Contains("  ", StringComparison.Ordinal))
            {
                issues.Add(new(
                    "bound-or-malformed-form",
                    $"'{entry.Elvish}' looks like a bound affix or contains an empty compound component."));
            }

            var normalized = NormalizeLetters(entry.Elvish);
            if (HasRepeatedCharacterRun(normalized, 3))
            {
                issues.Add(new(
                    "repeated-letter-pattern",
                    $"'{entry.Elvish}' contains a three-letter repetition not accepted without correction."));
            }

            if (HasTag(entry, PhonotacticsReviewedTag))
            {
                return;
            }

            var attestedBigrams = BuildAttestedBigrams(entry.Language, existingEntries);
            var unattested = TokenizeNormalized(entry.Elvish)
                .SelectMany(GetBigrams)
                .Where(bigram => !attestedBigrams.Contains(bigram))
                .Distinct(StringComparer.Ordinal)
                .Take(5)
                .ToArray();
            if (unattested.Length > 0)
            {
                issues.Add(new(
                    "unattested-letter-sequence",
                    $"'{entry.Elvish}' contains letter sequences not found in the curated {entry.Language} corpus: {string.Join(", ", unattested)}. Add '{PhonotacticsReviewedTag}' only after manual review."));
            }

            if (TokenizeNormalized(entry.Elvish).Any(static token => HasConsonantRun(token, 4)))
            {
                issues.Add(new(
                    "unattested-consonant-cluster",
                    $"'{entry.Elvish}' contains four or more consecutive consonants; add '{PhonotacticsReviewedTag}' only after manual review."));
            }
        }

        private static void ReviewExactCollisions(
            ElvenLexiconEntry entry,
            IEnumerable<ElvenTranslationCandidate> existingEntries,
            ICollection<ElvenLexiconReviewIssue> issues)
        {
            foreach (var existing in existingEntries.Where(candidate => SameLanguage(entry.Language, candidate.Language)))
            {
                var sameEnglish = EqualsIgnoreCase(entry.English, existing.English);
                var sameElvish = EqualsIgnoreCase(entry.Elvish, existing.Translation);
                var samePartOfSpeech = EqualsIgnoreCase(entry.PartOfSpeech, existing.PartOfSpeech);
                if (sameEnglish && sameElvish && samePartOfSpeech)
                {
                    issues.Add(new(
                        "exact-duplicate",
                        $"'{entry.English}' -> '{entry.Elvish}' duplicates an existing {entry.Language} entry.",
                        existing));
                }
                else if (sameEnglish && samePartOfSpeech && !sameElvish && !HasTag(entry, CollisionReviewedTag))
                {
                    issues.Add(new(
                        "english-sense-collision",
                        $"'{entry.English}' already maps to '{existing.Translation}' in {entry.Language}; add '{CollisionReviewedTag}' only after the distinct sense is reviewed.",
                        existing));
                }
                else if (sameElvish && !sameEnglish && !HasAnyTag(entry, "shared-form", CollisionReviewedTag))
                {
                    issues.Add(new(
                        "elvish-form-collision",
                        $"'{entry.Elvish}' already maps back to '{existing.English}' in {entry.Language}; add 'shared-form' only when the shared meaning is intentional.",
                        existing));
                }
            }
        }

        private static void ReviewSindarinPreference(
            ElvenLexiconEntry entry,
            IEnumerable<ElvenTranslationCandidate> existingEntries,
            ICollection<ElvenLexiconReviewIssue> issues)
        {
            if (!EqualsIgnoreCase(entry.Language, "Quenya"))
            {
                return;
            }

            var sindarin = existingEntries.FirstOrDefault(candidate =>
                EqualsIgnoreCase(candidate.English, entry.English) &&
                EqualsIgnoreCase(candidate.Language, "Sindarin"));
            if (sindarin is not null)
            {
                issues.Add(new(
                    "quenya-shadowed-by-sindarin",
                    $"'{entry.English}' already has the preferred Sindarin form '{sindarin.Translation}', so a Quenya fallback must not be added.",
                    sindarin));
            }
        }

        private static void ReviewRootFidelity(
            ElvenLexiconEntry entry,
            IReadOnlyList<ElvenTranslationCandidate> existingEntries,
            ICollection<ElvenLexiconReviewIssue> issues)
        {
            var roots = (entry.RootForms ?? Array.Empty<string>())
                .Where(static root => !string.IsNullOrWhiteSpace(root))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (roots.Length == 0)
            {
                if (!HasTag(entry, RootInventionReviewedTag))
                {
                    issues.Add(new(
                        "root-provenance-required",
                        $"A local Elven entry must name one or more established {entry.Language} root forms, or carry '{RootInventionReviewedTag}' after an invented root is reviewed."));
                }

                return;
            }

            foreach (var root in roots)
            {
                var rootEntry = existingEntries.FirstOrDefault(candidate =>
                    SameLanguage(entry.Language, candidate.Language) &&
                    EqualsIgnoreCase(root, candidate.Translation));
                if (rootEntry is null)
                {
                    var otherLanguage = existingEntries.FirstOrDefault(candidate =>
                        EqualsIgnoreCase(root, candidate.Translation));
                    issues.Add(new(
                        otherLanguage is null ? "root-form-missing" : "cross-language-root",
                        otherLanguage is null
                            ? $"Declared {entry.Language} root '{root}' is not present in the curated lexicon."
                            : $"Declared root '{root}' is attested as {otherLanguage.Language}, not {entry.Language}.",
                        otherLanguage));
                    continue;
                }

                if (!HasTag(entry, RootChangeReviewedTag) && !PreservesRoot(entry, root))
                {
                    issues.Add(new(
                        "root-form-mismatch",
                        $"'{entry.Elvish}' does not visibly preserve declared {entry.Language} root '{root}'; add '{RootChangeReviewedTag}' only after mutation or historical sound change is reviewed.",
                        rootEntry));
                }
            }
        }

        private static void ReviewCompoundProvenance(
            ElvenLexiconEntry entry,
            ICollection<ElvenLexiconReviewIssue> issues)
        {
            var looksCompound = entry.Elvish.Contains(' ') || entry.Elvish.Contains('-') || HasTag(entry, "compound");
            if (!looksCompound || HasTag(entry, CompoundReviewedTag))
            {
                return;
            }

            var rootCount = (entry.RootForms ?? Array.Empty<string>())
                .Count(static root => !string.IsNullOrWhiteSpace(root));
            if (rootCount < 2)
            {
                issues.Add(new(
                    "compound-roots-required",
                    $"Compound '{entry.Elvish}' must declare at least two source roots or carry '{CompoundReviewedTag}' after manual analysis."));
            }
        }

        private static void ReviewCloseForms(
            ElvenLexiconEntry entry,
            IEnumerable<ElvenTranslationCandidate> existingEntries,
            ICollection<ElvenLexiconReviewIssue> issues)
        {
            if (HasTag(entry, CloseFormReviewedTag))
            {
                return;
            }

            var proposed = NormalizeLetters(entry.Elvish);
            if (proposed.Length < 4)
            {
                return;
            }

            var roots = entry.RootForms ?? Array.Empty<string>();
            foreach (var existing in existingEntries
                         .Where(candidate => SameLanguage(entry.Language, candidate.Language))
                         .Where(candidate => !EqualsIgnoreCase(entry.Elvish, candidate.Translation))
                         .GroupBy(static candidate => candidate.Translation, StringComparer.OrdinalIgnoreCase)
                         .Select(static group => group.First()))
            {
                if (roots.Contains(existing.Translation, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var existingForm = NormalizeLetters(existing.Translation);
                if (Math.Abs(proposed.Length - existingForm.Length) <= 1 &&
                    GetEditDistanceAtMostOne(proposed, existingForm) <= 1)
                {
                    issues.Add(new(
                        "close-form-conflict",
                        $"'{entry.Elvish}' is easily confused with existing {entry.Language} '{existing.Translation}' for '{existing.English}'; add '{CloseFormReviewedTag}' only after review.",
                        existing));
                    return;
                }
            }
        }

        private static bool PreservesRoot(ElvenLexiconEntry entry, string root)
        {
            var proposed = NormalizeLetters(entry.Elvish);
            var normalizedRoot = NormalizeLetters(root);
            if (proposed.Contains(normalizedRoot, StringComparison.Ordinal))
            {
                return true;
            }

            if (!EqualsIgnoreCase(entry.Language, "Sindarin"))
            {
                return false;
            }

            return GetSindarinMutationVariants(normalizedRoot)
                .Any(variant => variant.Length >= 2 && proposed.Contains(variant, StringComparison.Ordinal));
        }

        private static IEnumerable<string> GetSindarinMutationVariants(string root)
        {
            yield return root;
            var replacements = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["b"] = ["v", "m"],
                ["c"] = ["g", "ch"],
                ["d"] = ["dh", "n"],
                ["g"] = ["", "ng"],
                ["m"] = ["v"],
                ["p"] = ["b", "ph"],
                ["s"] = ["h"],
                ["t"] = ["d", "th"]
            };
            var initial = replacements.Keys.FirstOrDefault(root.StartsWith);
            if (initial is null)
            {
                yield break;
            }

            foreach (var replacement in replacements[initial])
            {
                yield return replacement + root[initial.Length..];
            }
        }

        private static HashSet<string> BuildAttestedBigrams(
            string language,
            IEnumerable<ElvenTranslationCandidate> existingEntries) =>
            existingEntries
                .Where(candidate => SameLanguage(language, candidate.Language))
                .SelectMany(candidate => TokenizeNormalized(candidate.Translation))
                .SelectMany(GetBigrams)
                .ToHashSet(StringComparer.Ordinal);

        private static IEnumerable<string> TokenizeNormalized(string value) =>
            value.Split([' ', '-', '\'', '’'], StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeLetters)
                .Where(static token => token.Length > 0);

        private static IEnumerable<string> GetBigrams(string value)
        {
            for (var index = 0; index < value.Length - 1; index++)
            {
                yield return value.Substring(index, 2);
            }
        }

        private static string NormalizeLetters(string value)
        {
            var result = new StringBuilder(value.Length);
            foreach (var character in value.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetter(character))
                {
                    result.Append(char.ToLowerInvariant(character));
                }
            }

            return result.ToString();
        }

        private static bool HasConsonantRun(string value, int requiredLength)
        {
            var run = 0;
            foreach (var character in value)
            {
                run = "aeiouy".Contains(character) ? 0 : run + 1;
                if (run >= requiredLength)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasRepeatedCharacterRun(string value, int requiredLength)
        {
            var run = 1;
            for (var index = 1; index < value.Length; index++)
            {
                run = value[index] == value[index - 1] ? run + 1 : 1;
                if (run >= requiredLength)
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetEditDistanceAtMostOne(string first, string second)
        {
            if (first == second)
            {
                return 0;
            }

            if (Math.Abs(first.Length - second.Length) > 1)
            {
                return 2;
            }

            var firstIndex = 0;
            var secondIndex = 0;
            var edits = 0;
            while (firstIndex < first.Length && secondIndex < second.Length)
            {
                if (first[firstIndex] == second[secondIndex])
                {
                    firstIndex++;
                    secondIndex++;
                    continue;
                }

                if (++edits > 1)
                {
                    return edits;
                }

                if (first.Length > second.Length)
                {
                    firstIndex++;
                }
                else if (second.Length > first.Length)
                {
                    secondIndex++;
                }
                else
                {
                    firstIndex++;
                    secondIndex++;
                }
            }

            return edits + (firstIndex < first.Length || secondIndex < second.Length ? 1 : 0);
        }

        private static bool IsSupportedLanguage(string? language) =>
            EqualsIgnoreCase(language, "Sindarin") || EqualsIgnoreCase(language, "Quenya");

        private static bool SameLanguage(string first, string second) => EqualsIgnoreCase(first, second);

        private static bool EqualsIgnoreCase(string? first, string? second) =>
            string.Equals(first?.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool HasTag(ElvenLexiconEntry entry, string tag) =>
            (entry.Tags ?? Array.Empty<string>()).Contains(tag, StringComparer.OrdinalIgnoreCase);

        private static bool HasAnyTag(ElvenLexiconEntry entry, params string[] tags) =>
            tags.Any(tag => HasTag(entry, tag));
    }
}
