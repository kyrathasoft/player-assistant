namespace PlayerAssistant
{
    internal sealed record OrcishLexiconReviewIssue(
        string Code,
        string Message,
        OrcishLexiconEntry? ConflictingEntry = null);

    internal static class OrcishLexiconReviewUtility
    {
        private const string CollisionReviewedTag = "collision-reviewed";
        private const string CloseFormReviewedTag = "close-form-reviewed";
        private const string RootExceptionReviewedTag = "root-exception-reviewed";
        private const string CompoundReviewedTag = "compound-reviewed";

        public static IReadOnlyList<OrcishLexiconReviewIssue> ReviewProposedEntry(
            OrcishLexiconEntry proposedEntry,
            IEnumerable<OrcishLexiconEntry> existingEntries)
        {
            ArgumentNullException.ThrowIfNull(proposedEntry);
            ArgumentNullException.ThrowIfNull(existingEntries);

            var entries = existingEntries.ToArray();
            var issues = new List<OrcishLexiconReviewIssue>();

            ReviewRequiredValues(proposedEntry, issues);
            ReviewExactCollisions(proposedEntry, entries, issues);
            ReviewRootFidelity(proposedEntry, entries, issues);
            ReviewCloseForms(proposedEntry, entries, issues);
            ReviewCompoundProvenance(proposedEntry, issues);

            return issues;
        }

        public static void EnsureCanAdd(
            OrcishLexiconEntry proposedEntry,
            IEnumerable<OrcishLexiconEntry> existingEntries)
        {
            var issues = ReviewProposedEntry(proposedEntry, existingEntries);
            if (issues.Count == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                $"The proposed Orcish lexicon entry requires review: {string.Join("; ", issues.Select(static issue => issue.Message))}");
        }

        private static void ReviewRequiredValues(
            OrcishLexiconEntry proposedEntry,
            ICollection<OrcishLexiconReviewIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(proposedEntry.English))
            {
                issues.Add(new OrcishLexiconReviewIssue("missing-english", "The English term is required."));
            }

            if (string.IsNullOrWhiteSpace(proposedEntry.Orcish))
            {
                issues.Add(new OrcishLexiconReviewIssue("missing-orcish", "The Orcish form is required."));
            }
        }

        private static void ReviewExactCollisions(
            OrcishLexiconEntry proposedEntry,
            IEnumerable<OrcishLexiconEntry> existingEntries,
            ICollection<OrcishLexiconReviewIssue> issues)
        {
            foreach (var existingEntry in existingEntries)
            {
                var sameEnglish = EqualsIgnoreCase(proposedEntry.English, existingEntry.English);
                var sameOrcish = EqualsIgnoreCase(proposedEntry.Orcish, existingEntry.Orcish);
                var samePartOfSpeech = EqualsIgnoreCase(proposedEntry.PartOfSpeech, existingEntry.PartOfSpeech);

                if (sameEnglish && sameOrcish && samePartOfSpeech)
                {
                    issues.Add(new OrcishLexiconReviewIssue(
                        "exact-duplicate",
                        $"'{proposedEntry.English}' -> '{proposedEntry.Orcish}' duplicates an existing entry.",
                        existingEntry));
                    continue;
                }

                if (sameEnglish && samePartOfSpeech && !sameOrcish && !HasTag(proposedEntry, CollisionReviewedTag))
                {
                    issues.Add(new OrcishLexiconReviewIssue(
                        "english-sense-collision",
                        $"'{proposedEntry.English}' already maps to '{existingEntry.Orcish}' for the same part of speech; add '{CollisionReviewedTag}' only after the distinct sense is reviewed.",
                        existingEntry));
                }

                if (sameOrcish && !sameEnglish && !HasAnyTag(proposedEntry, "shared-form", CollisionReviewedTag))
                {
                    issues.Add(new OrcishLexiconReviewIssue(
                        "orcish-form-collision",
                        $"'{proposedEntry.Orcish}' already maps back to '{existingEntry.English}'; add 'shared-form' only when the shared reverse meaning is intentional.",
                        existingEntry));
                }
            }
        }

        private static void ReviewRootFidelity(
            OrcishLexiconEntry proposedEntry,
            IReadOnlyList<OrcishLexiconEntry> existingEntries,
            ICollection<OrcishLexiconReviewIssue> issues)
        {
            var baseTags = (proposedEntry.Tags ?? Array.Empty<string>())
                .Where(static tag => tag.StartsWith("base-", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var declaresRootDerivation = HasTag(proposedEntry, "root-derived") || baseTags.Length > 0;
            if (!declaresRootDerivation)
            {
                return;
            }

            if (baseTags.Length != 1)
            {
                issues.Add(new OrcishLexiconReviewIssue(
                    "root-base-required",
                    "A root-derived entry must declare exactly one 'base-<english term>' tag."));
                return;
            }

            var baseEnglish = baseTags[0]["base-".Length..];
            var baseEntries = existingEntries
                .Where(entry => EqualsIgnoreCase(entry.English, baseEnglish))
                .Where(entry => proposedEntry.PartOfSpeech is null ||
                                entry.PartOfSpeech is null ||
                                EqualsIgnoreCase(entry.PartOfSpeech, proposedEntry.PartOfSpeech))
                .ToArray();
            if (baseEntries.Length == 0)
            {
                issues.Add(new OrcishLexiconReviewIssue(
                    "root-base-missing",
                    $"The declared root '{baseEnglish}' does not exist with a compatible part of speech."));
                return;
            }

            if (HasTag(proposedEntry, RootExceptionReviewedTag))
            {
                return;
            }

            var expectedForms = baseEntries
                .Select(entry => GetExpectedDerivedForm(proposedEntry, entry.Orcish))
                .Where(static form => form is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (expectedForms.Length > 0 && !expectedForms.Contains(proposedEntry.Orcish, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new OrcishLexiconReviewIssue(
                    "root-morphology-mismatch",
                    $"'{proposedEntry.Orcish}' does not match the established root morphology; expected {string.Join(" or ", expectedForms.Select(static form => $"'{form}'"))}."));
                return;
            }

            if (expectedForms.Length == 0 && !baseEntries.Any(entry => SharesRoot(entry.Orcish, proposedEntry.Orcish)))
            {
                issues.Add(new OrcishLexiconReviewIssue(
                    "root-form-mismatch",
                    $"'{proposedEntry.Orcish}' does not preserve the declared '{baseEnglish}' Orcish root."));
            }
        }

        private static void ReviewCloseForms(
            OrcishLexiconEntry proposedEntry,
            IEnumerable<OrcishLexiconEntry> existingEntries,
            ICollection<OrcishLexiconReviewIssue> issues)
        {
            if (HasAnyTag(proposedEntry, CloseFormReviewedTag, "root-derived", "derived-by-rule"))
            {
                return;
            }

            var proposedForm = NormalizeForm(proposedEntry.Orcish);
            if (proposedForm.Length < 3)
            {
                return;
            }

            foreach (var existingEntry in existingEntries
                         .Where(entry => !EqualsIgnoreCase(entry.Orcish, proposedEntry.Orcish))
                         .GroupBy(static entry => entry.Orcish, StringComparer.OrdinalIgnoreCase)
                         .Select(static group => group.First()))
            {
                var existingForm = NormalizeForm(existingEntry.Orcish);
                if (Math.Abs(proposedForm.Length - existingForm.Length) > 1 ||
                    GetEditDistanceAtMostOne(proposedForm, existingForm) > 1)
                {
                    continue;
                }

                issues.Add(new OrcishLexiconReviewIssue(
                    "close-form-conflict",
                    $"'{proposedEntry.Orcish}' is easily confused with existing '{existingEntry.Orcish}' for '{existingEntry.English}'; add '{CloseFormReviewedTag}' only after review.",
                    existingEntry));
            }
        }

        private static void ReviewCompoundProvenance(
            OrcishLexiconEntry proposedEntry,
            ICollection<OrcishLexiconReviewIssue> issues)
        {
            if (!HasTag(proposedEntry, "compound") ||
                HasAnyTag(proposedEntry, "root-derived", CompoundReviewedTag) ||
                (proposedEntry.Tags ?? Array.Empty<string>()).Any(static tag => tag.StartsWith("base-", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            issues.Add(new OrcishLexiconReviewIssue(
                "compound-root-review-required",
                $"Compound '{proposedEntry.Orcish}' must identify its source root with a 'base-' tag or carry '{CompoundReviewedTag}' after manual root review."));
        }

        private static string? GetExpectedDerivedForm(OrcishLexiconEntry proposedEntry, string baseOrcish)
        {
            if (HasTag(proposedEntry, "possessive"))
            {
                return $"{baseOrcish}uk";
            }

            if (HasTag(proposedEntry, "past"))
            {
                return ApplyVerbSuffix(baseOrcish, "ash");
            }

            if (HasTag(proposedEntry, "progressive"))
            {
                return ApplyVerbSuffix(baseOrcish, "in");
            }

            if (HasTag(proposedEntry, "present") ||
                (HasTag(proposedEntry, "s-form") && EqualsIgnoreCase(proposedEntry.PartOfSpeech, "verb")))
            {
                return ApplyVerbSuffix(baseOrcish, "ur");
            }

            if (HasAnyTag(proposedEntry, "plural", "s-form"))
            {
                return $"{baseOrcish}i";
            }

            return null;
        }

        private static string ApplyVerbSuffix(string orcish, string suffix)
        {
            var hyphenIndex = orcish.IndexOf('-');
            if (hyphenIndex > 0)
            {
                var firstSegment = orcish[..hyphenIndex];
                var remainder = orcish[hyphenIndex..];
                return firstSegment.EndsWith("u", StringComparison.OrdinalIgnoreCase)
                    ? $"{firstSegment[..^1]}{suffix}{remainder}"
                    : $"{firstSegment}{suffix}{remainder}";
            }

            return orcish.EndsWith("u", StringComparison.OrdinalIgnoreCase)
                ? $"{orcish[..^1]}{suffix}"
                : $"{orcish}{suffix}";
        }

        private static bool SharesRoot(string baseOrcish, string proposedOrcish)
        {
            var baseRoot = GetFirstSegment(baseOrcish).TrimEnd('u');
            var proposedRoot = GetFirstSegment(proposedOrcish);
            return proposedRoot.StartsWith(baseRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetFirstSegment(string value)
        {
            var separatorIndex = value.IndexOfAny(['-', ' ']);
            return separatorIndex < 0 ? value : value[..separatorIndex];
        }

        private static int GetEditDistanceAtMostOne(string left, string right)
        {
            if (left.Equals(right, StringComparison.Ordinal))
            {
                return 0;
            }

            if (Math.Abs(left.Length - right.Length) > 1)
            {
                return 2;
            }

            if (left.Length == right.Length)
            {
                var differences = 0;
                for (var index = 0; index < left.Length; index++)
                {
                    if (left[index] != right[index] && ++differences > 1)
                    {
                        return 2;
                    }
                }

                return differences;
            }

            var shorter = left.Length < right.Length ? left : right;
            var longer = left.Length < right.Length ? right : left;
            var shortIndex = 0;
            var longIndex = 0;
            var edits = 0;
            while (shortIndex < shorter.Length && longIndex < longer.Length)
            {
                if (shorter[shortIndex] == longer[longIndex])
                {
                    shortIndex++;
                    longIndex++;
                    continue;
                }

                if (++edits > 1)
                {
                    return 2;
                }

                longIndex++;
            }

            return 1;
        }

        private static string NormalizeForm(string value)
        {
            return new string(value
                .Where(static character => char.IsLetter(character))
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static bool HasAnyTag(OrcishLexiconEntry entry, params string[] tags)
        {
            return tags.Any(tag => HasTag(entry, tag));
        }

        private static bool HasTag(OrcishLexiconEntry entry, string tag)
        {
            return (entry.Tags ?? Array.Empty<string>())
                .Any(existingTag => EqualsIgnoreCase(existingTag, tag));
        }

        private static bool EqualsIgnoreCase(string? left, string? right)
        {
            return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
