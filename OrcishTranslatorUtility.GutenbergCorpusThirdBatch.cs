namespace PlayerAssistant
{
    internal static partial class OrcishTranslatorUtility
    {
        private const string GutenbergCorpusThirdSourceCandidateData = """
barber
betrothed
boyhood
chattering
courier
extravagant
flinging
formula
fraud
greedy
habitual
heroine
humiliation
intelligible
liar
mainland
malignant
mockery
overcame
perplexity
picnic
saloon
tapped
trot
verdict
""";

        private const string GutenbergCorpusThirdNearKinCandidateData = """
barber's|barber
barbered|barber
barbering|barber
betrothed's|betrothed
boyhood's|boyhood
boyhoods|boyhood
chatter|chattering
chatter's|chattering
chattered|chattering
chatterer|chattering
chatterers|chattering
chatters|chattering
courier's|courier
couriered|courier
couriering|courier
couriers|courier
extravagantly|extravagant
fling's|flinging
formula's|formula
formulas|formula
frauds|fraud
greedier|greedy
greediest|greedy
greediness|greedy
habitually|habitual
habitualness|habitual
heroine's|heroine
heroines|heroine
humiliation's|humiliation
liar's|liar
liars|liar
mainland's|mainland
mainlands|mainland
malignantly|malignant
mockeries|mockery
mockery's|mockery
perplexities|perplexity
perplexity's|perplexity
picnic's|picnic
picnics|picnic
saloon's|saloon
saloons|saloon
trot's|trot
trots|trot
trotted|trot
trotting|trot
verdict's|verdict
verdicts|verdict
""";

        private static IEnumerable<OrcishLexiconEntry> BuildGutenbergCorpusThirdCandidateEntries(IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var sourceRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sourceOrdinal = 0;
            foreach (var english in GutenbergCorpusThirdSourceCandidateData.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var root = $"guthmok-{EncodeTwentyPageOrdinal(sourceOrdinal++)}";
                sourceRoots.Add(english, root);
                var candidate = new OrcishLexiconEntry(english, root, Tags: ["gutenberg", "gutenberg-third-corpus-candidate-batch", "generated", "review-promoted", "close-form-reviewed", $"family-{english}"]);
                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
            var nearKinOrdinal = 0;
            foreach (var line in GutenbergCorpusThirdNearKinCandidateData.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|', 2, StringSplitOptions.TrimEntries);
                var english = fields[0];
                var sourceEnglish = fields[1];
                var candidate = new OrcishLexiconEntry(english, CreateThirtyPageNearKinForm(sourceRoots[sourceEnglish], english, nearKinOrdinal++), Tags: ["gutenberg", "gutenberg-third-corpus-near-kin", "near-kin", "derived-by-rule", "review-promoted", "close-form-reviewed", $"family-{sourceEnglish}"]);
                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
        }
    }
}
