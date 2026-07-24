namespace PlayerAssistant
{
    internal static partial class OrcishTranslatorUtility
    {
        private const string EarthItCaresNotCandidateData = """
alcohol-fueled|adjective|condition|alcohol,fueled||
backswing|noun|motion|back,swing||
brass-plated|adjective|material|brass,plated||
cannon-fodder|noun|person|cannon,fodder||
cellar-like|adjective|place|cellar,like||
cut-open|adjective|condition|cut,open||
darkflame|noun|magic|dark,flame||
dead-panning|verb|speech|dead,pan||
discounted|adjective|value|||
dungeon-like|adjective|place|dungeon,like||
even-numbered|adjective|number|even,numbered||
fine-tune|verb|creation|fine,tune||derive-present,derive-past,derive-progressive
fire-roasted|adjective|condition|fire,roasted||
firelight-shadowed|adjective|light|firelight,shadowed||
fresh-baked|adjective|condition|fresh,baked||
good-naturedly|adverb|manner|good,nature||
high-sun|noun|time|high,sun||
hip-waders|noun|object|hip,waders||
icebolt|noun|magic|ice,bolt||
inn-tavern|noun|place|inn,tavern||
magically-fueled|adjective|magic|magically,fueled||
magister|noun|person|||campaign-lore
matter-of-factly|adverb|manner|matter,fact||
maze-like|adjective|place|maze,like||
mid-calf|adjective|measure|||
midsentence|adverb|language|||
mud-sucking|adjective|condition|mud,sucking||
ne'er-do-wells|noun|person|||
nerve-jangling|adjective|emotion|nerve,jangling||
no-longer-jumping|adjective|condition|not,longer,jumping||
one-hundred|adjective|number|one,hundred||
over-exert|verb|action|over,exert||derive-present,derive-past,derive-progressive
overcommitted|adjective|condition|||
poxed|adjective|condition|||
prodding|noun|action|||
reappear|verb|motion|||derive-present,derive-past,derive-progressive
repast|noun|food|||
reconstitute|verb|creation|||derive-present,derive-past,derive-progressive
resecure|verb|action|||derive-present,derive-past,derive-progressive
resource|noun|object|||derive-plural
ring-finger|noun|body|ring,finger||
second-wave|noun|group|second,wave||
stewpot|noun|object|stew,pot||
tomb-raiding|noun|action|tomb,raiding||
upperdark|noun|place|upper,dark||campaign-lore
wader-covered|adjective|condition|wader,covered||
walled-over|adjective|condition|walled,over||
white-light|adjective|light|white,light||
win-win|adjective|value|win,win||
hasiko|noun|drink|||campaign-lore
neshralk|noun|object|||campaign-lore
plast|noun|material|||campaign-lore
querma|noun|drink|||campaign-lore
cuemess|noun|drink||shared:cuemess|campaign-lore
cuumess|noun|drink||shared:cuemess|campaign-lore
halfling's|noun|person||possessive:halfling|
dungeoneer's|noun|person||possessive:dungeoneer|
""";

        private static IEnumerable<OrcishLexiconEntry> BuildEarthItCaresNotCandidateEntries(
            IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var generatedRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var generatedOrdinal = 0;

            foreach (var line in EarthItCaresNotCandidateData.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|');
                var english = fields[0];
                var partOfSpeech = fields[1];
                var grammarClass = fields[2];
                var components = fields[3];
                var special = fields[4];
                var extraTags = fields[5]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var tags = new List<string>
                {
                    "local-markdown",
                    "earth-it-cares-not",
                    "source-candidate",
                    "review-promoted",
                    "close-form-reviewed",
                    $"family-{english}"
                };

                string orcish;
                if (special.StartsWith("possessive:", StringComparison.OrdinalIgnoreCase))
                {
                    var sourceEnglish = special["possessive:".Length..];
                    orcish = ToOrcishPossessive(ResolveEarthItCaresNotComponent(acceptedEntries, sourceEnglish));
                    tags.AddRange(["possessive", "root-derived", "derived-by-rule", $"base-{sourceEnglish}"]);
                }
                else if (special.StartsWith("shared:", StringComparison.OrdinalIgnoreCase))
                {
                    var sourceEnglish = special["shared:".Length..];
                    if (!generatedRoots.TryGetValue(sourceEnglish, out orcish!))
                    {
                        orcish = $"dak-mur-ti-ecar-{EncodeTwentyPageOrdinal(generatedOrdinal++)}";
                        generatedRoots.Add(sourceEnglish, orcish);
                    }

                    tags.AddRange(["campaign-lore", "shared-form", "spelling-variant"]);
                }
                else if (!string.IsNullOrWhiteSpace(components))
                {
                    orcish = string.Join(
                        "-",
                        components.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(component => ResolveEarthItCaresNotComponent(acceptedEntries, component)));
                    tags.AddRange(["compound", "compound-reviewed"]);
                }
                else
                {
                    orcish = $"dak-mur-ti-ecar-{EncodeTwentyPageOrdinal(generatedOrdinal++)}";
                    generatedRoots.Add(english, orcish);
                    tags.Add("generated");
                }

                tags.AddRange(extraTags);
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

        private static string ResolveEarthItCaresNotComponent(
            IReadOnlyList<OrcishLexiconEntry> entries,
            string english)
        {
            var match = entries.FirstOrDefault(entry =>
                string.Equals(entry.English, english, StringComparison.OrdinalIgnoreCase));
            return match?.Orcish
                ?? throw new InvalidOperationException($"No established Orcish component exists for '{english}'.");
        }
    }
}
