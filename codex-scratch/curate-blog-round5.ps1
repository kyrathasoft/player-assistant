$ErrorActionPreference = 'Stop'

$raw = @(Get-Content -LiteralPath 'codex-scratch\blog-round5-raw-candidates.txt' | Where-Object { $_.Trim() })
$rejected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    # Proper names, campaign exonyms, product titles, real-world names, and opaque coined forms.
    'aedelwine','afer','al-fewn','alexia','alfunsh','altgrimmr','ame','anganach','arctos','arden-vul','armaggedon','astableon','athelbyn','atheylbyn','aurimignos','aveda','balthazar','barrowmaze','beretun','blackpeak','bonux','bristlebeard','bryan','bujant','cabe','canden','chaosgrenade','cintra','crom','daggerheart','dalgor','dalreth','dinton','dolmenwood','domitius','dordogne','doulous-sou','drogan','druff','eastscarp','eddson','eddison','egyptian','ehdra','elben','elk-fist','elkfist','en-amenti','enjant','eulogeson','eusebia','exodore','fanghorn','feist','fodor','france','fritz','futurus','gahn','garrick','gesellion','ghindar','gullby','hal','haldric','hamelet','hansen','harren','he-who-walks-between-the-stalks','heker-set','hermes','hinsman','homer','horatius','hourigan','hrowaka','iah','impurax','jack','january','jenkla','kal-arath','kate','killik','klaus','komnene','korsaro','kruger','kybalion','kyrathasoft','larrabietti','lazarus','leiber','leifcrim','lenomarnd','lothaybin','luisignon','lycandus','lycandrus','ma''at','magit','malachai','manford','marius','mckillip','menzoberranzan','merbak','michael','mon','morkin','narsileon','navare','newmarket','norda','noviomagus','palestrim','patricia','pbegames','pelevin','pelfry','phokas','psuche-stratos','santy','scaurus','setian','setite','silvanus','stenn','stigrix','telperronian','the-archon','theodora','thorcin','thoth','thoth-amon','thoth-hermes','thothians','tim','tricotor','ulf','ursus','uta','valdghar','valerius','varumani','venator','victor','vodor','voltus','weskenim','wizardtower','zuul',
    # Rules, software, web, media, metadata, and authoring residue.
    'algorithm','atk','audiobooks','average','bab','btw','built','button','call','character-creation','cod','com','comment','computer','converter','course','dec','default','dex','dice-pool','dice-roller','dmg','document','download','dual-classed','fighter-thief','finalized','five-star','formatted','forum','generator','gimp','gmail','gme','hex-crawling','high-tech','hospital','igdt','image','imported','incrementing','index','instructions','int','internet','intro','level-ups','licenses','list','logs','lonerrpg','max','maxed','mechanic','metadata','min','modded','movies','nd-level','non','non-shou','nov','novels','npc-only','operations','optimal','ose','papers','paragraph','pbe','play-list','player-character','poi','post','post-ch','pre-generated','pre-req','premier','premium','prep','pro','procedure','proposal','protagonist','pts','rd-level','real-time','redditor','relevant','replacement','retcon','review','roleplayer','roleplayers','rpg','ruleset','sampled','screenshot','screenshots','selected','sentence','server','session','simplified','site','snapshot','solo-rp','specify','st-level','states','sub-classes','submit','subreddit','summary','synopsis','tagging','temp','tick-worth','tools','ttrpg','ttrpgs','tue','up-to-date','update','updated','variable','verbs','version','videos','wis','wiz','wiz-prst',
    # Malformed, truncated, misspelled, contraction, and low-value chatter tokens.
    'aren','arguably','babau','bewteen','bre','brung','cha','cle','didn','docil','either','failture','fgt','fgt-thf','fiddly','firstly','greek','gypsy','hasn','haub','hence','here','ime','inqury','isn','jerkass','leave','millenium','none','off','post-ch','poweful','seen','ser','sharphen','sharphened','sharphening','shouldn','sorceror','spe','str','suprise-attacked','termperature','terminology','thereby','thf','tou','tranformation','une','wasn','whereas','world'
) | ForEach-Object { [void]$rejected.Add($_) }

$accepted = @($raw | Where-Object { -not $rejected.Contains($_) } | Sort-Object -Unique)
$removed = @($raw | Where-Object { $rejected.Contains($_) } | Sort-Object -Unique)
$accepted | Set-Content -LiteralPath 'codex-scratch\blog-round5-source-candidates.txt' -Encoding utf8
$removed | Set-Content -LiteralPath 'codex-scratch\blog-round5-rejected-candidates.txt' -Encoding utf8
$accepted | Set-Content -LiteralPath 'codex-scratch\candidates.txt' -Encoding utf8

[pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    rawCandidateCount = $raw.Count
    sourceCandidateCount = $accepted.Count
    rejectedCandidateCount = $removed.Count
} | ConvertTo-Json | Set-Content -LiteralPath 'codex-scratch\blog-round5-curation-manifest.json' -Encoding utf8

[pscustomobject]@{ raw=$raw.Count; source=$accepted.Count; rejected=$removed.Count } | ConvertTo-Json
