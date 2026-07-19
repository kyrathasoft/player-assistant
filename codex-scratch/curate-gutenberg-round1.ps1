param(
    [string]$Round = 'round1',
    [string]$Corpus = 'gutenberg',
    [int]$MinimumDocuments = 30,
    [int]$MinimumFrequency = 100
)
$ErrorActionPreference = 'Stop'
$prefix = "codex-scratch\$Corpus-$Round"
$stats = @(Import-Csv -Delimiter "`t" -LiteralPath "$prefix-word-stats.tsv")
$raw = @($stats | Where-Object { [int]$_.documents -ge $MinimumDocuments -and [int]$_.frequency -ge $MinimumFrequency } | Select-Object -ExpandProperty word | Sort-Object -Unique)
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
    # Final third-corpus review: residual names, publication terms, and modern abstractions.
    'above-named','alice''s','allen''s','canning','comma','commits','consonant','consonants','driveway','genealogical','gregory''s','header','jock','josh','negatives','null','octavo','proctor','prospecting','prospector','prospectors','queue','quorum','rapport','school-house','school-room','sea-level','signalized','stereotyped','superstructure','title-page','transatlantic','ubiquitous','verbatim','week-end','well-defined','well-informed','woodcuts'
) | ForEach-Object { [void]$rejected.Add($_) }
@(
    # Third mixed-corpus review: real-world peoples, places, sects, names, and slurs.
    'afghan','arabians','arnold''s','asiatics','assyrians','aztecs','babylonians','basque','bedouins','ben''s','britain''s','buddhists','bushman','bushmen','campbell''s','carthaginians','catherine''s','cherokees','cicero''s','coon','creole','dominicans','english-speaking','eskimos','ethiopians','finns','france''s','franciscans','french''s','gordon''s','half-breeds','half-caste','hamilton''s','harrison''s','hellenes','hessian','hollanders','hottentots','huguenots','hungarians','incas','jerry''s','jew''s','johnston''s','jove''s','kaiser''s','malays','mcclellan','milton''s','moore''s','murray''s','norwegians','patrick''s','peruvians','pharaohs','pharisees','phoenicians','plutarch''s','presbyterians','ptolemies','putnam''s','quakers','redskin','rome''s','russia''s','sahib','satan''s','sherman''s','sicilians','simon''s','slavs','stanley''s','stewart''s','stuart''s','stuarts','syrians','thompson''s','tommy''s','trojans','vedas','wellington''s','zulus',
    # Modern technology, science, military hardware, measurement, and transport.
    'airlock','airmen','analytical','anthropological','approximation','arithmetical','averaged','binoculars','carbon','championship','climatic','coke','collegiate','computation','corona','curvature','decreasing','deg','derivative','demolition','documentary','equations','ethnological','ethnology','etymology','fiber','fiscal','frontal','galactic','generator','generators','geographers','geologist','glossary','graphically','gravitational','hangar','hemorrhage','homogeneous','hyphenation','hypotheses','indicator','infantile','incision','inflection','intensive','interpolated','interpolation','interstellar','ionic','italic','jiffy','kilometers','lancet','ligature','linguistic','litigation','magnesia','malaria','mapped','margins','martian','math','median','metamorphosis','meteorological','meter','meters','militarism','nomenclature','noun','nouns','optic','orthography','parachute','patents','percussion','phenomenal','philological','philology','phonetic','posterior','postpaid','practicability','prefix','prescriptions','present-day','preventive','projector','projectors','proportional','prototype','quantum','quarantine','radar','refraction','rifleman','robot','saline','saliva','sextant','six-shooter','small-pox','snipers','spaceport','spaceship','spellings','steamer''s','synthetic','take-off','terminal','text-books','textual','topographical','transcript','translators','transmitter','transmitting','transverse','tuition','underscores','veterinary','zeppelin','zinc',
    # Modern government, institutional, legal, academic, and publication vocabulary.
    'absolutism','abstention','academies','acclaimed','admissible','admittedly','admixture','advisability','amendments','appellations','attachments','attestation','attorney-general','attributable','authenticated','autocracy','autocrat','autograph','boroughs','consular','consulship','controversial','custom-house','dependencies','diplomatist','diplomatists','disciplinary','disunion','electoral','entente','examiners','expeditionary','governmental','imperialists','impeached','impeachment','intermediary','jurisprudence','junta','legislator','legislators','lieutenant-governor','loyalist','loyalists','municipality','plaintiff','plenipotentiaries','plenipotentiary','protectorate','reactionary','rear-admiral','requisitions','resumption','senatorial','seniority','solicitation','solicitations','state''s','statecraft','statesmanship','subdivided','subdivision','subdivisions','submits','surveyor','unconstitutional','unofficial','vice-admiral',
    # Numeric compounds, abbreviations, foreign fragments, and low-value corpus residue.
    'ais','app','coll','comp','corp','dona','ergo','esprit','fifty-eight','fifty-four','fifty-one','fifty-seven','fifty-six','forty-one','forty-six','fourthly','fut','gif','idem','lac','lite','loco','madras','meg','mot','nine-tenths','ninety-four','ninety-six','noel','nope','one-fourth','one-tenth','omega','passe','passim','quin','quasi','sac','sacra','sars','second-class','senor','sens','seventy-four','sixty-four','sixty-three','soc','sop','supra','thar''s','third-class','tush','twa','twenty-sixth','var','viva'
) | ForEach-Object { [void]$rejected.Add($_) }
@(
    # Second 1,000-book corpus review: real-world names, peoples, sects, and slurs.
    'abbe','africans','apaches','armenians','benedictine','bey','caliph','celts','dante''s','hindus','homer''s','jackson''s','julia''s','lama','mohammedans','papist','papists','plato''s','rabbi','redskins','riviera','thomas''s','whigs',
    # Modern technology, science, scholarship, institutions, politics, and publication residue.
    'affidavit','applicants','biographical','bourgeoisie','campus','cerebral','circulars','cocktail','communism','communist','compendium','compilation','congressional','courthouse','criminality','dictatorship','digitized','divergence','dividends','edit','equatorial','exploitation','full-page','gene','genera','google','gradations','gram','handbag','heredity','ice-cream','immigration','ion','liabilities','manifesto','maternity','operative','operators','pianist','pictorial','prefixed','prevalence','proficiency','proletariat','promulgated','propagation','registration','relevant','revue','scholastic','self-government','shareholders','speculators','spiritualism','stenographer','student''s','taxicab','testimonial','theologian','theologians','traceable','typographical','undergraduate','up-to-date','vaudeville','veto','viewpoint','voters','waitress',
    # Numeric compounds, abbreviations, foreign fragments, dialect residue, and malformed OCR tokens.
    'bis','cir','dea','ene','fifty-two','fol','fora','forty-nine','forty-seven','gov','illus','inc','ing','ins','iss','lan','lire','ltd','mag','mam','mas','mos','mys','nae','nee','och','para','pic','pol','pres','prom','qua','ques','rand','reg','rel','rep','sen','seventy-two','sim','spec','tab','tam','til','trans','tum','tween','twenty-fourth','twenty-second','vita','wen'
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

function Test-CapitalizedOnlyNameForm([string]$Candidate) {
    $stem = $Candidate -replace "'s$", ''
    return $capitalizedOnly.Contains($stem)
}

$acceptedBeforeAnachronism = @($raw | Where-Object { -not $rejected.Contains($_) -and -not (Test-CapitalizedOnlyNameForm $_) -and $_ -notmatch '^(?:[ivxlcdm]+)$' } | Sort-Object)
$removed = @($raw | Where-Object { $rejected.Contains($_) -or (Test-CapitalizedOnlyNameForm $_) -or $_ -match '^(?:[ivxlcdm]+)$' } | Sort-Object)
$raw | Set-Content -LiteralPath "$prefix-threshold-candidates.txt" -Encoding utf8
$acceptedBeforeAnachronism | Set-Content -LiteralPath "$prefix-pre-anachronism-candidates.txt" -Encoding utf8
python 'codex-scratch\filter-anachronistic-candidates.py' "$prefix-pre-anachronism-candidates.txt" "$prefix-source-candidates.txt" "$prefix-anachronism-rejected-candidates.txt"
$accepted = @(Get-Content -LiteralPath "$prefix-source-candidates.txt")
$anachronismRejected = @(Get-Content -LiteralPath "$prefix-anachronism-rejected-candidates.txt")
$removed | Set-Content -LiteralPath "$prefix-rejected-candidates.txt" -Encoding utf8
$accepted | Set-Content -LiteralPath 'codex-scratch\candidates.txt' -Encoding utf8
[pscustomobject]@{generatedAt=(Get-Date).ToUniversalTime().ToString('o');minimumDocuments=$MinimumDocuments;minimumFrequency=$MinimumFrequency;thresholdCandidateCount=$raw.Count;sourceCandidateCount=$accepted.Count;rejectedCandidateCount=$removed.Count;anachronismRejectedCandidateCount=$anachronismRejected.Count;capitalizedOnlyDictionaryCount=$capitalizedOnly.Count} | ConvertTo-Json | Set-Content -LiteralPath "$prefix-curation-manifest.json" -Encoding utf8
[pscustomobject]@{threshold=$raw.Count;source=$accepted.Count;rejected=$removed.Count;anachronismRejected=$anachronismRejected.Count}|ConvertTo-Json
