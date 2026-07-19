$ErrorActionPreference = 'Stop'

$data = Get-Content -Raw -LiteralPath 'codex-scratch\blog-followup-near-raw.json' | ConvertFrom-Json
$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path 'Release\player-assistant.dll'))
$type = $assembly.GetType('PlayerAssistant.OrcishTranslatorUtility')
$entries = $type.GetMethod('GetLexiconEntries', [System.Reflection.BindingFlags]'Public,Static').Invoke($null, @())
$existing = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) { [void]$existing.Add($entry.English) }

$sources = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($word in $data.sources) { [void]$sources.Add($word) }

$rejected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    # Dictionary homographs and false families found during review.
    'fated','fating','sanger','sangs','sang''s','lenten','lents','lent''s','putt','putt''s','putted','putting','putts',
    'version','versions','graves''s','dimmer''s','dimmers',
    # Awkward comparative/superlative or malformed dictionary products.
    'acridest','balmiest','brashest','coyest','falser','falsest','flashest','forwardest','gravest',
    'grislier','grisliest','patientest','saltest','sheerer','sheerest','slipperier','slipperiest',
    'somewhats','vivider','vividest',
    # Previously reviewed Hunspell false positives.
    'minion','minions','lateraled','lateraling','waters''s','combs''s','demographics''s',
    'missive','pigment','piped','piper','pipers','piping','ripen','ripens','riper','ripest',
    'wined','wining','doz','dozed','dozes','dozing','corn','corned','corning','corns',
    'cursive','butch','butch''s','butches','hamburg','hamburg''s','hamburgs','grippe','grippe''s',
    'hostilities''s','odds''s','precised','precises','precising','restive','rustication','saucer',
    'saucers','skew','skew''s','skewed','skewing','skews','stealth','tinged','twined','twiner',
    'twiners','twining','wits''s','less''s','julies','lemming','lemmings','latiner','mister',
    'misters','fib','fib''s','fiber','fibs','cubed','cuber','cubers','cubing','rockies','taped',
    'taper','tapers','taping','dimer','dined','diner','diners','dining','laded','lading','ladings',
    'luster','meter','meters','moped','moper','mopers','moping','out','out''s','outed','outing',
    'outings','outs','pee','pee''s','peed','pees','seamen','archers','feel''s','staged','staging',
    'stagings','worms''s','barest','beaker','beakers','caper','capers','dearth','fir','fir''s','firs',
    'firth','gaiter','gaiters','griped','griper','gripers','hanker','hankers','hunker','hunkers',
    'innings','kited','kiting','liter','liters','lobed','oliver','palings','probable','regaled',
    'regaling','scared','scaring','scarper','scarpers','tuber','tubers','votive','wicker','wickers',
    'wiles''s','abed','abs','ala','farmings','ling','lings','lowing','lunged','lunging','om''s','oms',
    'complication','meanings','sat''s','sung''s','tensing','wag','wag''s','wags','wager','wagers',
    'radiation','nuclear','science','archontos'
) | ForEach-Object { [void]$rejected.Add($_) }

$candidateToSource = [ordered]@{}
foreach ($family in $data.families.PSObject.Properties) {
    foreach ($candidate in $family.Value) {
        if ($candidate.Length -lt 3 -or $candidate -match "s's$" -or $existing.Contains($candidate) -or $sources.Contains($candidate) -or $rejected.Contains($candidate)) { continue }
        if (-not $candidateToSource.Contains($candidate)) { $candidateToSource[$candidate] = $family.Name }
    }
}

# Correct the dictionary's ambiguous analysis of "putters" as a form of "putt".
foreach ($candidate in @('putter','putter''s','puttered','puttering')) {
    if (-not $existing.Contains($candidate) -and -not $sources.Contains($candidate)) { $candidateToSource[$candidate] = 'putters' }
}

# Irregular forms and transparent compound families that Hunspell does not connect.
$manualFamilies = [ordered]@{
    'agarics' = @('agaric')
    'alchemically' = @('alchemical')
    'arrowing' = @('arrow','arrowed','arrows')
    'associations' = @('association')
    'aurochs' = @('auroch')
    'batted' = @('bat','bats','batting')
    'bearclaws' = @('bearclaw')
    'broodings' = @('brooding')
    'breech' = @('breeched','breeching')
    'caught' = @('catch','catches','catching')
    'cavern-spiders' = @('cavern-spider')
    'chipped' = @('chip','chips','chipping')
    'clapped' = @('clap','claps','clapping')
    'clipped' = @('clip','clips','clipping')
    'clubbed' = @('club','clubs','clubbing')
    'dabbed' = @('dab','dabs','dabbing')
    'deprecations' = @('deprecation')
    'delight' = @('delighted','delightful','delighting','delights')
    'derived' = @('derive','derives','deriving','derivation')
    'devourer' = @('devour','devoured','devouring','devours')
    'diplomatic' = @('diplomatically','diplomacy','diplomat','diplomats')
    'diseased' = @('disease','diseases')
    'disliking' = @('dislike','disliked','dislikes')
    'distracts' = @('distract','distracted','distracting','distraction')
    'disappear' = @('disappeared','disappearing','disappearance','disappears')
    'echo-takers' = @('echo-taker')
    'ensconsed' = @('ensconce','ensconces','ensconcing')
    'entanglements' = @('entanglement')
    'factional' = @('faction','factions','factionally')
    'fall-saboteurs' = @('fall-saboteur')
    'farthest' = @('far','farther')
    'feebly' = @('feeble')
    'feudal' = @('feudally','feudalism')
    'gravity-chutes' = @('gravity-chute')
    'held' = @('hold','holding','holds')
    'ice-giants' = @('ice-giant')
    'inhale' = @('inhales','inhaling')
    'intoned' = @('intone','intones','intoning')
    'intolerable' = @('intolerably','intolerance')
    'invocation' = @('invocations')
    'lift-credits' = @('lift-credit')
    'moss-lanterns' = @('moss-lantern')
    'mushroom-men' = @('mushroom-man')
    'mythic' = @('mythical','mythically')
    'pall-carriers' = @('pall-carrier')
    'patted' = @('pat','pats','patting')
    'perceptible' = @('perceptibly')
    'proclaimed' = @('proclaim','proclaiming','proclaims','proclamation')
    'provisioning' = @('provision','provisioned','provisions')
    'provocation' = @('provocations','provoke','provoked','provokes','provoking')
    'quaffes' = @('quaff','quaffed','quaffing')
    'realities' = @('reality')
    'recollection' = @('recollections')
    'rediscovered' = @('rediscover','rediscovering','rediscovers')
    'redoubles' = @('redouble','redoubled','redoubling')
    'rejoin' = @('rejoined','rejoining','rejoins')
    'removes' = @('remove','removed','removing')
    'replied' = @('reply','replies','replying')
    'resin' = @('resins','resinous')
    'revelatory' = @('revelation','revelations')
    'ritualized' = @('ritualize','ritualizes','ritualizing')
    'rotted' = @('rot','rots','rotting')
    'sang' = @('sing','singing','sings','sung')
    'satisfactory' = @('satisfactorily','satisfactoriness')
    'sheddings' = @('shed','shedding','sheds')
    'sigil' = @('sigils')
    'sky-caskets' = @('sky-casket')
    'sled-plates' = @('sled-plate')
    'sparring' = @('spar','sparred','spars')
    'spinels' = @('spinel')
    'spun' = @('spin','spinning','spins')
    'spurred' = @('spur','spurring','spurs')
    'stirges' = @('stirge')
    'thuggishly' = @('thuggish')
    'triarch' = @('triarchs','triarchy')
    'unauthorized' = @('authorize','authorized','authorizes','authorizing','authorization')
    'uncurls' = @('uncurl','uncurled','uncurling')
    'undoer' = @('undo','undoes','undoing','undone')
    'unfolds' = @('unfold','unfolded','unfolding')
    'unsettling' = @('unsettle','unsettled','unsettles')
    'unspecialized' = @('specialization','specialize','specialized','specializes','specializing')
    'unsurprised' = @('surprise','surprised','surprises','surprising')
    'unwraps' = @('unwrap','unwrapped','unwrapping')
    'unzip' = @('unzipped','unzipping','unzips')
    'winch-barons' = @('winch-baron')
    'wizard-priests' = @('wizard-priest')
    'wove' = @('weave','weaves','weaving','woven')
    'zipped' = @('zip','zipping','zips')
    'lent' = @('lend','lending','lends')
}
foreach ($family in $manualFamilies.GetEnumerator()) {
    foreach ($candidate in $family.Value) {
        if (-not $existing.Contains($candidate) -and -not $sources.Contains($candidate) -and -not $rejected.Contains($candidate)) {
            $candidateToSource[$candidate] = $family.Name
        }
    }
}

$near = @($candidateToSource.Keys | Sort-Object)
$familyLines = @($candidateToSource.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name)|$($_.Value)" })
$combined = @($sources + $near | Sort-Object -Unique)

$sources | Sort-Object | Set-Content -LiteralPath 'codex-scratch\blog-followup-source-candidates.txt' -Encoding utf8
$near | Set-Content -LiteralPath 'codex-scratch\blog-followup-near-candidates.txt' -Encoding utf8
$familyLines | Set-Content -LiteralPath 'codex-scratch\blog-followup-near-families.txt' -Encoding utf8
$combined | Set-Content -LiteralPath 'codex-scratch\candidates.txt' -Encoding utf8

[pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    sourceCount = $sources.Count
    nearKinCount = $near.Count
    combinedCount = $combined.Count
    lexiconEntryCount = $entries.Count
    sourceFile = 'codex-scratch/blog-followup-source-candidates.txt'
    nearKinFile = 'codex-scratch/blog-followup-near-candidates.txt'
    familyFile = 'codex-scratch/blog-followup-near-families.txt'
} | ConvertTo-Json | Set-Content -LiteralPath 'codex-scratch\blog-followup-near-manifest.json' -Encoding utf8

[pscustomobject]@{
    sourceCount = $sources.Count
    nearKinCount = $near.Count
    combinedCount = $combined.Count
    lexiconEntryCount = $entries.Count
} | ConvertTo-Json
