namespace PlayerAssistant
{
    internal static partial class OrcishTranslatorUtility
    {
        private const string MegadungeonsSourceCandidateData = """
abyssal
caster's
infestation
ingested
doppelganger
giant-sized
dwarf-sized
wielder's
cleric's
pointy
water-filled
vandalized
footlockers
east-west
mismatched
backlash
deviant
gem-studded
avian
bedrolls
pitons
semiprecious
horrendous
stalker's
hellfire
man-shaped
mapped
six-armed
extra-dimensional
vampire's
stinky
zinc
ingesting
decor
hit-and-run
normal-sized
spews
gremlin
doppelgangers
bedroll
armband
armbands
rat-men
elemental's
water-based
arch-devil
counterclockwise
geode
symbiotic
femurs
nightstand
burbles
freestanding
orc's
sauna
gooey
odorless
rockfall
strongbox
blue-skinned
bricked-up
extra-planar
footlocker
hatchery
ingestion
claustrophobia
worm-like
cagey
drop-off
open-topped
well-informed
flightless
impeccably
chameleon-like
face's
land-based
paper-thin
salvageable
elitist
gem-encrusted
orchestrate
spring-loaded
throne's
acolyte's
addictive
half-fiend
cavemen
iron-reinforced
daemonic
double-doors
half-dragon
arboretum
off-white
smith's
brown-green
ubiquitous
parlay
snake-headed
construct's
handheld
two-tiered
carnelians
delver's
intuit
ghoul's
motile
ogre-sized
worktable
roils
three-tiered
tomb's
aesthete
armrest
escapees
extrude
half-ogre
hubris
malnourished
mannequin
rust-colored
armrests
goop
pupil-less
yeti
bat-winged
bone-white
stone-like
deathtrap
dog-sized
evocative
goblin-sized
hassle
herbivores
long-dried
nonhuman
skewering
skinless
subsumed
amoral
blockage
blubbery
child-sized
flexes
ground-up
manic
nihilistic
side-chamber
silty
well-gnawed
re-animates
dully-glowing
deathwatch
double-strength
hellhole
healer's
medusae
waterwheels
dumbwaiter
scrags
sub-demons
half-orcs
swarm's
metal's
rough-carved
air-filled
gawping
self-repair
bulls-eye
triple-strength
yurt
fortress's
hand-sized
high-status
multi-headed
worktables
flea-infested
light-absorbent
oddly-shaped
papery
chest's
dodgy
expeditionary
horror's
long-hidden
mind-influencing
nodular
once-fine
scrimshawed
south-facing
almost-human
diamond-studded
disc-shaped
green-black
gremlins
hip-high
humanoid-shaped
judder
knight-commander
mannequins
orchestrated
ritually
rock-hard
self-serving
sub-chief's
torturer's
twenty-one
armor-clad
asymmetrical
banditry
blood-covered
bunk-beds
chest-high
deathtraps
earth-based
gravid
green-white
ingests
left-most
long-rotted
loyalists
mid-rank
mismatching
nosferatu
octagon-shaped
off-putting
once-living
scrimshaw
slab's
spinnerets
starburst
walk-in
warlock's
yipping
templar
lizard-being
teeny-tiny
consortium
nine-bones
behemoths
scuttlebutt
thrice-cursed
occluded
wall-mounted
angelfish
man-catcher
silver-trimmed
reseal
stronghold's
dwarf-like
vocalize
frog-god
two-bladed
mineral-rich
waist-length
good-quality
maggoty
non-goblins
skull-tipped
anti-painting
base-relief
bounty-hunter
burbling
cookbooks
eye-shaped
half-pillars
headbutt
resurgent
spelunking
arcane-locked
arrow-slits
cross-chamber
deep-yellow
demon-prince
implosion
pushcart
rhizomes
runny
sabre-tooth
settlement's
solarium
trunk-like
weird's
wellspring
wisher
zombie's
alpha-werewolf
atypical
brain-eating
crusher's
gargoyle's
highest-ranking
jackal-headed
monkey-demons
moon-eyed
plaster-covered
plops
salamander-woman
still-usable
sword-arms
teeth-wheels
tentacle-like
tuberous
underbelly
weed-man
calfskin
clan-hold
double-trapped
draconian
dungeon-crawling
eastern-most
escalating
fire-hardened
half-fiends
hellholes
hex-crawling
humidor
inside-out
legionnaires
life-stealing
lower-class
merchant-lord
milky-white
mist-filled
mist-shrouded
northern-most
run-off
sabre-toothed
silver-chased
spelunkers
spire's
theologians
twenty-two
""";

        internal static IReadOnlyList<string> GetMegadungeonsSourceCandidates()
        {
            return MegadungeonsSourceCandidateData.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static IEnumerable<OrcishLexiconEntry> BuildMegadungeonsCandidateEntries(
            IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var sourceTerms = GetMegadungeonsSourceCandidates();
            var sourceSet = sourceTerms.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var generatedOrdinal = 0;

            foreach (var english in sourceTerms
                         .OrderBy(static term => term.Length)
                         .ThenBy(static term => term, StringComparer.OrdinalIgnoreCase))
            {
                if (IsGeneratedMegadungeonsSourceForm(english, sourceSet))
                {
                    continue;
                }

                var tags = new List<string>
                {
                    "local-pdf",
                    "megadungeons",
                    "megadungeons-source-candidate",
                    "review-promoted",
                    "close-form-reviewed",
                    $"family-{english}"
                };
                string? partOfSpeech = null;
                string? grammarClass = null;
                string orcish;

                if (TryCreateMegadungeonsDerivedForm(
                        acceptedEntries,
                        english,
                        out orcish,
                        out partOfSpeech,
                        out grammarClass,
                        out var derivedTags))
                {
                    tags.AddRange(derivedTags);
                }
                else if (TryComposeMegadungeonsCompound(acceptedEntries, english, out var compound))
                {
                    orcish = compound;
                    tags.AddRange(["compound", "compound-reviewed", "shared-form"]);
                }
                else
                {
                    orcish = $"megskar-{EncodeTwentyPageOrdinal(generatedOrdinal / 4096)}-{EncodeTwentyPageOrdinal(generatedOrdinal++ % 4096)}";
                    tags.Add("generated");
                }

                if (sourceSet.Contains(ToEnglishPlural(english)))
                {
                    partOfSpeech = "noun";
                    grammarClass ??= "object";
                    tags.Add("derive-plural");
                }

                if (sourceSet.Contains(ToEnglishPast(english)))
                {
                    partOfSpeech = "verb";
                    grammarClass ??= "action";
                    tags.Add("derive-past");
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

        private static bool IsGeneratedMegadungeonsSourceForm(
            string english,
            IReadOnlySet<string> sourceTerms)
        {
            return sourceTerms.Any(source =>
                !string.Equals(source, english, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(ToEnglishPlural(source), english, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(ToEnglishPast(source), english, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool TryCreateMegadungeonsDerivedForm(
            IReadOnlyList<OrcishLexiconEntry> entries,
            string english,
            out string orcish,
            out string? partOfSpeech,
            out string? grammarClass,
            out string[] tags)
        {
            orcish = string.Empty;
            partOfSpeech = null;
            grammarClass = null;
            tags = [];

            if (english.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
            {
                var baseEnglish = english[..^2];
                var baseEntry = FindMegadungeonsBase(entries, baseEnglish, "noun");
                if (baseEntry is not null)
                {
                    orcish = ToOrcishPossessive(baseEntry.Orcish);
                    partOfSpeech = "noun";
                    grammarClass = baseEntry.GrammarClass;
                    tags = ["possessive", "root-derived", "derived-by-rule", "shared-form", "collision-reviewed", $"base-{baseEnglish}"];
                    return true;
                }
            }

            foreach (var baseEnglish in GetMegadungeonsVerbBaseCandidates(english, "ing"))
            {
                var baseEntry = FindMegadungeonsBase(entries, baseEnglish, "verb");
                if (baseEntry is null)
                {
                    continue;
                }

                orcish = ToOrcishVerbForm(baseEntry.Orcish, "in");
                partOfSpeech = "verb";
                grammarClass = baseEntry.GrammarClass;
                tags = ["progressive", "present", "root-derived", "derived-by-rule", $"base-{baseEnglish}"];
                return true;
            }

            foreach (var baseEnglish in GetMegadungeonsVerbBaseCandidates(english, "ed"))
            {
                var baseEntry = FindMegadungeonsBase(entries, baseEnglish, "verb");
                if (baseEntry is null)
                {
                    continue;
                }

                orcish = ToOrcishVerbForm(baseEntry.Orcish, "ash");
                partOfSpeech = "verb";
                grammarClass = baseEntry.GrammarClass;
                tags = ["past", "root-derived", "derived-by-rule", $"base-{baseEnglish}"];
                return true;
            }

            foreach (var baseEnglish in GetMegadungeonsSFormBaseCandidates(english))
            {
                var verbEntry = FindMegadungeonsBase(entries, baseEnglish, "verb");
                if (verbEntry is not null)
                {
                    orcish = ToOrcishVerbForm(verbEntry.Orcish, "ur");
                    partOfSpeech = "verb";
                    grammarClass = verbEntry.GrammarClass;
                    tags = ["present", "root-derived", "derived-by-rule", $"base-{baseEnglish}"];
                    return true;
                }

                var nounEntry = FindMegadungeonsBase(entries, baseEnglish, "noun");
                if (nounEntry is not null)
                {
                    orcish = ToOrcishPlural(nounEntry.Orcish);
                    partOfSpeech = "noun";
                    grammarClass = nounEntry.GrammarClass;
                    tags = ["plural", "s-form", "root-derived", "derived-by-rule", $"base-{baseEnglish}"];
                    return true;
                }
            }

            return false;
        }

        private static OrcishLexiconEntry? FindMegadungeonsBase(
            IEnumerable<OrcishLexiconEntry> entries,
            string english,
            string partOfSpeech)
        {
            return entries.FirstOrDefault(entry =>
                string.Equals(entry.English, english, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.PartOfSpeech, partOfSpeech, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> GetMegadungeonsVerbBaseCandidates(string english, string suffix)
        {
            if (!english.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || english.Length <= suffix.Length + 1)
            {
                yield break;
            }

            var stem = english[..^suffix.Length];
            yield return stem;
            yield return stem + "e";
            if (stem.Length > 2 && stem[^1] == stem[^2])
            {
                yield return stem[..^1];
            }
        }

        private static IEnumerable<string> GetMegadungeonsSFormBaseCandidates(string english)
        {
            if (!english.EndsWith('s') || english.Length < 5 || english.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            yield return english[..^1];
            if (english.EndsWith("ches", StringComparison.OrdinalIgnoreCase) ||
                english.EndsWith("shes", StringComparison.OrdinalIgnoreCase) ||
                english.EndsWith("xes", StringComparison.OrdinalIgnoreCase) ||
                english.EndsWith("zes", StringComparison.OrdinalIgnoreCase) ||
                english.EndsWith("ses", StringComparison.OrdinalIgnoreCase) ||
                english.EndsWith("oes", StringComparison.OrdinalIgnoreCase))
            {
                yield return english[..^2];
            }

            if (english.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
            {
                yield return english[..^3] + "y";
            }
        }

        private static bool TryComposeMegadungeonsCompound(
            IReadOnlyList<OrcishLexiconEntry> entries,
            string english,
            out string orcish)
        {
            orcish = string.Empty;
            if (!english.Contains('-', StringComparison.Ordinal))
            {
                return false;
            }

            var resolved = new List<string>();
            foreach (var component in english.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var entry = entries.FirstOrDefault(candidate =>
                    string.Equals(candidate.English, component, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    return false;
                }

                resolved.Add(entry.Orcish);
            }

            orcish = string.Join("-", resolved);
            return true;
        }
    }
}
