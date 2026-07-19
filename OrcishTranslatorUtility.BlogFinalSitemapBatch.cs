namespace PlayerAssistant
{
    internal static partial class OrcishTranslatorUtility
    {
        private const string BlogFinalSitemapSourceCandidateData = """
aflame
audience
boar-men
cervine
cohort
dispatched
favoured
foul-tempered
fur-clad
ghastly
goat-men
infantry
inhabits
inimical
lowest-ranking
messy
neighbouring
ovine
porters
rank-and-file
under-earth
ursine
""";

        private const string BlogFinalSitemapNearKinCandidateData = """
audience's|audience
audiences|audience
boar-man|boar-men
cohort's|cohort
cohorts|cohort
dispatch|dispatched
dispatching|dispatched
favouring|favoured
ghastlier|ghastly
ghastliest|ghastly
ghastliness|ghastly
goat-man|goat-men
infantries|infantry
infantry's|infantry
inimically|inimical
low-ranking|lowest-ranking
messier|messy
messiest|messy
messiness|messy
neighbour|neighbouring
neighboured|neighbouring
neighbours|neighbouring
porter's|porters
""";

        private static IEnumerable<OrcishLexiconEntry> BuildBlogFinalSitemapCandidateEntries(IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var sourceRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sourceOrdinal = 0;
            foreach (var english in BlogFinalSitemapSourceCandidateData.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var root = $"ulgraz-{EncodeTwentyPageOrdinal(sourceOrdinal++)}";
                sourceRoots.Add(english, root);
                var candidate = new OrcishLexiconEntry(english, root, Tags: ["blog", "blog-final-sitemap-candidate-batch", "generated", "review-promoted", "close-form-reviewed", $"family-{english}"]);
                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
            var nearKinOrdinal = 0;
            foreach (var line in BlogFinalSitemapNearKinCandidateData.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|', 2, StringSplitOptions.TrimEntries);
                var english = fields[0];
                var sourceEnglish = fields[1];
                var candidate = new OrcishLexiconEntry(english, CreateThirtyPageNearKinForm(sourceRoots[sourceEnglish], english, nearKinOrdinal++), Tags: ["blog", "blog-final-sitemap-near-kin", "near-kin", "derived-by-rule", "review-promoted", "close-form-reviewed", $"family-{sourceEnglish}"]);
                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
        }
    }
}
