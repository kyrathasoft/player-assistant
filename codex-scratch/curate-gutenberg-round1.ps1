param([string]$Round = 'round1', [string]$Corpus = 'gutenberg')
$ErrorActionPreference = 'Stop'
$prefix = "codex-scratch\$Corpus-$Round"
$stats = @(Import-Csv -Delimiter "`t" -LiteralPath "$prefix-word-stats.tsv")
$raw = @($stats | Where-Object { [int]$_.documents -ge 30 -and [int]$_.frequency -ge 100 } | Select-Object -ExpandProperty word | Sort-Object -Unique)
$rejected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    # Recurrent personal names, place names, countries, nationalities, and direct exonyms.
    'adam','africa','african','alfred','alexander','america','american','americans','andrew','arabian','arthur','asia','asiatic','atlantic','austria','ben','berlin','boston','britain','british','california','cambridge','canada','canadian','charles','chicago','china','chinese','christ','christianity','christmas','david','december','dutch','edward','edinburgh','egypt','egyptian','elizabeth','england','england''s','english','englishman','englishmen','europe','european','february','flanders','france','francis','francisco','frank','frederick','french','frenchman','frenchmen','george','german','germans','germany','greece','greek','greeks','hamilton','henry','holland','india','indian','indians','indies','ireland','irish','israel','italian','italy','jack','jacob','james','january','japan','japanese','jerusalem','jesus','jew','jews','john','john''s','johnson','jones','joseph','jove','ken','lawrence','lee','les','lewis','london','louis','marie','mars','martin','mary','mediterranean','mexican','mexico','mississippi','napoleon','nicholas','oxford','pacific','paris','paul','persian','peter','philadelphia','philip','portuguese','richard','robert','rome','russia','russian','sally','sam','samuel','san','santa','satan','saxon','scotch','scotland','scott','spain','spaniard','spaniards','spanish','thomas','tom','turks','venus','victor','victoria','virginia','wales','walter','washington','william','williams','wilson','yankee','york',
    # Publication, transcription, navigation, software, and corpus scaffolding.
    'application','average','built','button','call','chapters','cited','colophon','comment','content','contributors','copyrighted','course','courses','digital','digitization','document','documents','download','edited','editorial','errors','essay','file','hist','illustrated','image','images','import','index','instance','instructions','internet','introduction','issue','issued','issuing','list','lists','log','logs','magazine','magazines','margin','markup','memoirs','metadata','method','methods','model','newspaper','newspapers','non-authorship','novel','operations','org','page','pages','paper','papers','paragraph','patent','photographs','post','posted','posts','preliminary','print','procedure','program','publication','published','publishers','quotation','quote','quoted','readers','references','removal','request','requested','response','revision','science','scope','section','sections','seen','select','selected','sentence','site','sources','state','states','stating','students','submit','submitted','system','systems','test','text','tools','transcriber''s','typefaces','typography','typos','university','updates','users','version','vol','volunteer-driven','worldwide','www',
    # Unsuitable slurs, malformed contractions, low-value function residue, and notation.
    'did','divers','during','either','esq','est','here','here''s','it''s','just','leave','mon','mrs','negro','negroes','none','off','one''s','ones','other''s','others','our','ours','out','que','rev','reverend','sex','somebody','thereby','to-day','to-morrow','to-night','true','twas','twenty-eight','twenty-one','twenty-two','viz','where''s','whereas','whereby','wherefore','whereupon','who''s','wife''s','world'
) | ForEach-Object { [void]$rejected.Add($_) }
@(
    # Batch-reviewed exonyms, personal/place names, slurs, and ambiguous name-derived forms.
    'arabs','athenians','baptist','britons','byzantine','charlie','christ''s','danes','danish','egyptians','europeans','francs','george''s','henry''s','homer','indian''s','italians','jack''s','johnny','kaiser','maria','mary''s','max','morocco','napoleon''s','nelson','newton','nigger','panama','paul''s','persians','peter''s','russians','saxons','smith''s','squaw','tartar','tom''s','troy','wellington','welsh','yankees',
    # Abbreviations, foreign fragments, malformed residue, and low-value publishing/navigation terms.
    'aux','ave','capt','col','com','cum','dis','fer','files','frontispiece','git','hon','ibid','lib','min','mit','mons','mus','nos','ole','online','operator','pas','prof','res','sic','texts','topics','val','versions','vocabulary','vols'
) | ForEach-Object { [void]$rejected.Add($_) }

$capitalized = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$lowercase = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
Get-Content -LiteralPath 'C:\Users\Bryan\AppData\Local\Programs\MiKTeX\hunspell\dicts\en_US.dic' | Select-Object -Skip 1 | ForEach-Object {
    $stem = ((($_ -split "`t")[0]) -split '/')[0]
    if ($stem -cmatch '^[A-Z][a-z]+$') { [void]$capitalized.Add($stem.ToLowerInvariant()) }
    elseif ($stem -cmatch '^[a-z]') { [void]$lowercase.Add($stem) }
}
$capitalizedOnly = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($word in $capitalized) {
    if (-not $lowercase.Contains($word)) { [void]$capitalizedOnly.Add($word) }
}

$accepted = @($raw | Where-Object { -not $rejected.Contains($_) -and -not $capitalizedOnly.Contains($_) -and $_ -notmatch '^(?:[ivxlcdm]+)$' } | Sort-Object)
$removed = @($raw | Where-Object { $rejected.Contains($_) -or $capitalizedOnly.Contains($_) -or $_ -match '^(?:[ivxlcdm]+)$' } | Sort-Object)
$raw | Set-Content -LiteralPath "$prefix-threshold-candidates.txt" -Encoding utf8
$accepted | Set-Content -LiteralPath "$prefix-source-candidates.txt" -Encoding utf8
$removed | Set-Content -LiteralPath "$prefix-rejected-candidates.txt" -Encoding utf8
$accepted | Set-Content -LiteralPath 'codex-scratch\candidates.txt' -Encoding utf8
[pscustomobject]@{generatedAt=(Get-Date).ToUniversalTime().ToString('o');minimumDocuments=30;minimumFrequency=100;thresholdCandidateCount=$raw.Count;sourceCandidateCount=$accepted.Count;rejectedCandidateCount=$removed.Count;capitalizedOnlyDictionaryCount=$capitalizedOnly.Count} | ConvertTo-Json | Set-Content -LiteralPath "$prefix-curation-manifest.json" -Encoding utf8
[pscustomobject]@{threshold=$raw.Count;source=$accepted.Count;rejected=$removed.Count}|ConvertTo-Json
