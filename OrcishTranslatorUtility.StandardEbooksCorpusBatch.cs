namespace PlayerAssistant
{
    internal static partial class OrcishTranslatorUtility
    {
        private const string StandardEbooksCorpusSourceCandidateData = """
armchair
caresses
good-looking
sensibility
sordid
""";

        private const string StandardEbooksCorpusNearKinCandidateData = """
armchair's|armchair
armchairs|armchair
caress|caresses
caressed|caresses
caressing|caresses
sensibility's|sensibility
sordidly|sordid
sordidness|sordid
""";

        private static IEnumerable<OrcishLexiconEntry> BuildStandardEbooksCorpusCandidateEntries(IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var sourceRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sourceOrdinal = 0;
            foreach (var english in StandardEbooksCorpusSourceCandidateData.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var root = $"standruk-{EncodeTwentyPageOrdinal(sourceOrdinal++)}";
                sourceRoots.Add(english, root);
                var candidate = new OrcishLexiconEntry(english, root, Tags: ["gutenberg", "standard-ebooks-corpus-candidate-batch", "generated", "review-promoted", "close-form-reviewed", $"family-{english}"]);
                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
            var nearKinOrdinal = 0;
            foreach (var line in StandardEbooksCorpusNearKinCandidateData.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|', 2, StringSplitOptions.TrimEntries);
                var english = fields[0];
                var sourceEnglish = fields[1];
                var candidate = new OrcishLexiconEntry(english, CreateThirtyPageNearKinForm(sourceRoots[sourceEnglish], english, nearKinOrdinal++), Tags: ["gutenberg", "standard-ebooks-corpus-near-kin", "near-kin", "derived-by-rule", "review-promoted", "close-form-reviewed", $"family-{sourceEnglish}"]);
                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
        }
    }
}
