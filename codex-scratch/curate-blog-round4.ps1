$ErrorActionPreference = 'Stop'

$discovery = Get-Content -Raw -LiteralPath 'codex-scratch\blog-discovery-round4.json' | ConvertFrom-Json
$selectedPages = @($discovery.pages | Select-Object -First 26 | Where-Object {
    $_.url -notmatch '/currently-or-finished-reading$|/endless-rime-characters$'
})
if ($selectedPages.Count -ne 24) { throw "Expected 24 selected pages, found $($selectedPages.Count)." }

$raw = @($selectedPages.candidates | Sort-Object -Unique)
$rejected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    # Named characters, places, factions, deities, and setting-specific exonyms.
    'aedelwine','alexia','anpu','archonteans','archontos','arcturian','arcturus','astableon','athelbyn','auel','aurimignos','aval','avernian','belaphas','brian','bujant','cabe','cameron','chaosgrenade','creon','cromm-thoth','cull-va','deino','doulous-sou','drome','eeii','en-amenti','eulogeson','eveda','exodore','fagan','fath','ghindar','glesteon','grandmoth','hrowaka','heqeti','igdt','jean','jenkla','kaelen','kal-arath','kate','kauket','keeva','keko','kerma','khamet','khem-ur-shatter','komnene','korsaro','korvin','kuju','kulva','lapiz-lazuli-tiled','lexie','lycandrus','ma''at','maat','manfred','nanefer','narsileon','navare','newmarket','norda','orojiam','pah''et','paleologue','paul','psilofyr','psuche-stratos','roderick','rosk','roskelly','rudigar','setians','setmose','stenn','storegga','su''hed','sulla','surret','tacere','thalas','theodora','theron','theta','thoth','thoth-amon','thoth-cromm','thoth-hermes','thothians','thoththas','thrales','thrangir','torunn','tradewell','uta','vedecab','vhal-gorgoth','voss','vothian','weskenim','zorael',
    # Web, software, rules notation, publication scaffolding, and bookkeeping residue.
    'aac','accessibility','algorithm','anti-government','attachments','atk','automated','average','ave','bab','base-attack','basic','beta','blank','blogging','boh-arm','boh-arms','button','checkboxes','checkpoint','cigarette','code-behind','codex','comment','concise','condensed','contents','countdown','curation','css','desktop','dex','dmg','document','emulator','extraction','fgt-thf','file','files','follow-up','footnote','footnotes','format','formatting','forum','framework','freeware','game-world','gamemaster','gameplay','gender','generator','geographic','gme','headers','hex-crawling','high-difficulty','high-tech','highest-level','hitpoints','htmly','image','index','init','instructions','instance','int','issue','journaling','key-finder','layout','level-up','level-ups','licenses','log','long-form','longer-form','luck-die','markdown','markers','markup','max','mechanic','menu','menus','mod','netflix','nodes','non-fiction','novel','novelize','numerical','operations','optimal','overlays','pages','paper','paragraph','parse','passphrase','password','poi','post','preliminary','print','prints','protagonist','protocols','provider','publication','published','ratio','re-edit','re-indexing','readable','readers','recension','relevant','removal','replacement','reproduce','resolutions','resources','retcon','role-play','roleplayer','rpg','rpg-mechanics','sandbox-style','sanitize','science','scope','scrape','sections','sentence','session','sex','site','snapshot','solo-flow','solo-play','solo-rpg','sources','stylesheets','substack','summarize','summary','systems','temp','temps','terminal','test','testing','th-level','timer','tools','update','updated','user-created','utility','variant','version','videogames','warnings','write-ups',
    # Contraction fragments, misspellings, truncated forms, and vocalizations.
    'ade','aka','aren','bre','cafefully','cha','clandestin','clink-clink-clink','couldn','corruping','didn','everytime','guage','hadn','hasn','haub','hearbeats','heroers','hourlgass','inaccesible','incease','iniative','interferring','isn','knawing','lah','leakl','lstone','magificent','magit','mutliple','res','scounting','sharphening','shouldn','siezes','snap-crack','spe','str','supressing','surpressed','thrum-thrum','tou','un-ticked','wasn','weren','wis','wiz-prst','wouldn',
    # Low-value conversational or authoring chatter.
    'admittedly','approve','call','catchy','concerning','contest','contemporary','course','either','fan','here','leave','none','off','others','seen','true','weird','world'
) | ForEach-Object { [void]$rejected.Add($_) }

$accepted = @($raw | Where-Object { -not $rejected.Contains($_) } | Sort-Object)
$removed = @($raw | Where-Object { $rejected.Contains($_) } | Sort-Object)

$raw | Set-Content -LiteralPath 'codex-scratch\blog-round4-raw-candidates.txt' -Encoding utf8
$accepted | Set-Content -LiteralPath 'codex-scratch\blog-round4-source-candidates.txt' -Encoding utf8
$removed | Set-Content -LiteralPath 'codex-scratch\blog-round4-rejected-candidates.txt' -Encoding utf8
$accepted | Set-Content -LiteralPath 'codex-scratch\candidates.txt' -Encoding utf8

$manifest = [pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    pageCount = $selectedPages.Count
    rawCandidateCount = $raw.Count
    sourceCandidateCount = $accepted.Count
    rejectedCandidateCount = $removed.Count
    pages = @($selectedPages | Select-Object url,title,characters,candidateCount)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath 'codex-scratch\blog-round4-manifest.json' -Encoding utf8
$manifest | ConvertTo-Json -Depth 4
