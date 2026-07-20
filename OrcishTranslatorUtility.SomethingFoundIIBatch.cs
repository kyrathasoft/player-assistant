namespace PlayerAssistant
{
    internal static partial class OrcishTranslatorUtility
    {
        private const string SomethingFoundIISourceCandidateData = """
aftereffects
admittedly
all-encompassing
ambiance
arguably
bearhug
bloodbath
bone-chilling
boxy
catty-corner
cerebral
cityscape
clamshell
cloudscape
confounded
decades-long
disappointment
dollop
eye-catching
gaggle
gawking
goosebumps
gothic
grey-black
heart-reader
heart-racing
heart-sinking
heart-stopping
incomparable
invasive
landmass
lavender-white
life-ending
liminal
liver-spotted
metamorphosis
mid-stride
midship
mind-boggling
nexus-point
nonstop
off-guard
off-yellow
otherness
pencil-thin
petrichor
piggyback
reconsider
resemblance
riptide
roiling
seabed
smaller-framed
soul-sinking
splat
squish
starstruck
still-standing
stone-faced
sulfurous
thunderheads
toasty
toothy
topsy-turvy
unabated
unadulterated
unbearable
unbelievable
unbelievably
unblemished
unbreathable
unbridled
unearthly
unfathomable
unfazed
unfiltered
unheard
unhinged
uninhabited
uninvited
unleash
unnaturally
unorthodox
unquenchable
unreality
unresolvable
unruffled
unsettlingly
unspool
unthreatening
unwarranted
veiny
vegetal
vertiginous
vibrancy
viewpoint
war-ravaged
warzone
wasp-like
water-whip
wild-haired
wind-rushing
wine-colored
world-changing
world-ending
worrisome
yellow-gold
""";

        private static IEnumerable<OrcishLexiconEntry> BuildSomethingFoundIICandidateEntries(
            IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var generatedOrdinal = 0;
            var nouns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "aftereffect", "ambiance", "bearhug", "bloodbath", "cityscape", "clamshell",
                "cloudscape", "disappointment", "dollop", "gaggle", "goosebump", "heart-reader",
                "landmass", "metamorphosis", "midship", "nexus-point", "otherness", "petrichor",
                "resemblance", "riptide", "seabed", "splat", "thunderhead", "unreality", "vibrancy",
                "viewpoint", "warzone", "water-whip"
            };
            var adverbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "admittedly", "arguably", "unbelievably", "unnaturally", "unsettlingly"
            };
            var verbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "reconsider", "squish", "unleash", "unspool"
            };
            var sourceCandidates = SomethingFoundIISourceCandidateData.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var sourceEnglish in sourceCandidates)
            {
                var english = sourceEnglish switch
                {
                    "aftereffects" => "aftereffect",
                    "goosebumps" => "goosebump",
                    "thunderheads" => "thunderhead",
                    _ => sourceEnglish
                };
                var partOfSpeech = nouns.Contains(english)
                    ? "noun"
                    : adverbs.Contains(english)
                        ? "adverb"
                        : verbs.Contains(english)
                            ? "verb"
                            : "adjective";
                var grammarClass = partOfSpeech switch
                {
                    "noun" => "object",
                    "verb" => "action",
                    "adverb" => "manner",
                    _ => "description"
                };
                var tags = new List<string>
                {
                    "proofread-docx",
                    "something-found-ii",
                    "source-candidate",
                    "review-promoted",
                    "close-form-reviewed",
                    $"family-{english}"
                };

                if (!string.Equals(sourceEnglish, english, StringComparison.OrdinalIgnoreCase))
                {
                    tags.AddRange(["source-family-seed", "derive-plural"]);
                }
                else if (string.Equals(partOfSpeech, "noun", StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("derive-plural");
                }
                else if (string.Equals(partOfSpeech, "verb", StringComparison.OrdinalIgnoreCase))
                {
                    tags.AddRange(["derive-present", "derive-past", "derive-progressive"]);
                }

                string orcish;
                if (TryComposeSomethingFoundIICompound(acceptedEntries, english, out var compound))
                {
                    orcish = compound;
                    tags.AddRange(["compound", "compound-reviewed", "shared-form"]);
                }
                else
                {
                    orcish = $"dak-mur-ti-toll-{EncodeTwentyPageOrdinal(generatedOrdinal++)}";
                    tags.Add("generated");
                }

                var candidate = new OrcishLexiconEntry(
                    english,
                    orcish,
                    partOfSpeech,
                    grammarClass,
                    tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
                OrcishLexiconReviewUtility.EnsureCanAdd(candidate, acceptedEntries);
                acceptedEntries.Add(candidate);
                yield return candidate;
            }
        }

        private static bool TryComposeSomethingFoundIICompound(
            IReadOnlyList<OrcishLexiconEntry> entries,
            string english,
            out string orcish)
        {
            orcish = string.Empty;
            if (!english.Contains('-', StringComparison.Ordinal))
            {
                return false;
            }

            var components = english.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var resolved = new List<string>();
            foreach (var component in components)
            {
                var match = entries.FirstOrDefault(entry =>
                    string.Equals(entry.English, component, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    return false;
                }

                resolved.Add(match.Orcish);
            }

            orcish = string.Join("-", resolved);
            return true;
        }
    }
}
