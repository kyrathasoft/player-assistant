$ErrorActionPreference = 'Stop'

$data = Get-Content -Raw -LiteralPath 'codex-scratch\batch-50b-exact-remaining.json' | ConvertFrom-Json
$drop = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    'duerra','matthias','aurie','thromb','mattie','revole','thelgarr','asmodeus','dex','int','wis','cha',
    'caddyshanks','caldamis','cere-lukh','hurg','querma','arcantryl','blackstaff','brenda','brookhollow',
    'cumerians','cuumess','daul','kelda','norothor','stigrix','achaia','ag-kirzhak','archeron','auriochos',
    'barrowmaze','blackbeard','bran','breldik','caddy','darkflame','ecks-uh-door-ay','ernie','exodore','feyre',
    'gursk','halfast','hammerspike','ilsensine','isabel','izrut','kellen','kesloril','krizhurg','mathhias',
    'mor','moradin','morndinsamman','mueshine','tauste','thelgarrs','val-uh-shine-as','valashinaz',
    'word','words','code','data','description','method','methods','requirements','response','select','tools','url',
    'system','systems','users','workflow','sample','scrape','format','lines','listed','listings','instance','primary',
    'optional','specify','request','replacement','procedure','parentheses','commentary','validate','final','top','temp',
    'getmarkdownfromresponse','getmarkdownfromurl','st-level','nd-level','laborat','ratory','hourby','dont','wh-what',
    'radiation','science','nuclear','archontos','cum','oct','minutos','plast','britain','lancaster','hellbane',
    'faerzress','duerran','demithuerge','excoriators','esp','mindaxes'
) | ForEach-Object { [void]$drop.Add($_) }

$candidates = @($data | ForEach-Object Word | Where-Object { -not $drop.Contains($_) } | Sort-Object -Unique)
Set-Content -LiteralPath 'codex-scratch\batch-50b-source-candidates.txt' -Value $candidates
[pscustomobject]@{ ExactRemaining=$data.Count; Dropped=$drop.Count; Curated=$candidates.Count } | ConvertTo-Json -Compress
