namespace PlayerAssistant
{
    internal static partial class OrcishTranslatorUtility
    {
        private const string LocalShadowdimCandidateData = """
world|noun|place||||
section|noun|object|||derive-plural
tools|noun|object||plural:tool|
cultist's|noun|person||possessive:cultist|
operations|noun|action||plural:operation|
site|noun|place|||derive-plural
snap-crack|interjection|sound|snap,crack||
document|noun|language|||derive-plural
dungeon's|noun|place||possessive:dungeon|
fighter-thief|noun|person||fixed:gash-mog-tukur-mog|
instructions|noun|command||plural:instruction|
lapis-lazuli-tiled|adjective|material|lapis,lazuli,tiled||campaign-lore
logothete's|noun|person||possessive:logothete|campaign-lore
orojiam|noun|drink|||campaign-lore
over-muscled|adjective|body|over,muscled||
passphrase|noun|language|pass,phrase||derive-plural
beastman's|noun|person||possessive:beastman|
beastmen's|noun|person||possessive:beastmen|
caprine's|noun|creature||possessive:caprine|
colossai|noun|creature||plural:colossus|campaign-lore,morphology-reviewed
curation|noun|action|||campaign-lore
sources|noun|origin||plural:source|
trapdoor's|noun|object||possessive:trapdoor|
""";

        private static IEnumerable<OrcishLexiconEntry> BuildLocalShadowdimCandidateEntries(
            IEnumerable<OrcishLexiconEntry> entries)
        {
            var acceptedEntries = entries.ToList();
            var generatedOrdinal = 0;

            foreach (var line in LocalShadowdimCandidateData.Split(
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
                    "local-shadowdim",
                    "source-candidate",
                    "review-promoted",
                    "close-form-reviewed",
                    $"family-{english}"
                };

                string orcish;
                if (special.StartsWith("possessive:", StringComparison.OrdinalIgnoreCase))
                {
                    var sourceEnglish = special["possessive:".Length..];
                    orcish = ToOrcishPossessive(ResolveLocalShadowdimComponent(acceptedEntries, sourceEnglish));
                    tags.AddRange(["possessive", "root-derived", "derived-by-rule", $"base-{sourceEnglish}"]);
                }
                else if (special.StartsWith("plural:", StringComparison.OrdinalIgnoreCase))
                {
                    var sourceEnglish = special["plural:".Length..];
                    orcish = ToOrcishPlural(ResolveLocalShadowdimComponent(acceptedEntries, sourceEnglish));
                    tags.AddRange(["plural", "s-form", "root-derived", "derived-by-rule", $"base-{sourceEnglish}"]);
                }
                else if (special.StartsWith("fixed:", StringComparison.OrdinalIgnoreCase))
                {
                    orcish = special["fixed:".Length..];
                    tags.Add("root-repaired");
                }
                else if (!string.IsNullOrWhiteSpace(components))
                {
                    orcish = string.Join(
                        "-",
                        components.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(component => ResolveLocalShadowdimComponent(acceptedEntries, component)));
                    tags.AddRange(["compound", "compound-reviewed"]);
                }
                else
                {
                    orcish = $"dak-mur-ti-shad-{EncodeTwentyPageOrdinal(generatedOrdinal++)}";
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

        private static string ResolveLocalShadowdimComponent(
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
