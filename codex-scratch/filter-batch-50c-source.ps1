$ErrorActionPreference = 'Stop'

$data = Get-Content -Raw -LiteralPath 'codex-scratch\batch-50c-exact-remaining.json' | ConvertFrom-Json
$drop = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    'anu-eya','maximilian','gaston','stigrix','valdghar','achaia','achaians','cere-lukh','cruach','fellshroud',
    'futurus','impurax','sammeister','sirac','yragerne','zuul','anganach',"anu-eya's",'arcantryl','archaia',
    'asmodeus','astaldo','branderscar','calea','chalrassan','delk','fangorran','feyre','garnetstone','garrick',
    'illitar','imperiosa','matthias','morin','nabblat','nemere','nolo','nuanda','obold','retep','rivenshield',
    'rogier','sagius','sedgefry','silvanus','silverdale','silvershaper','thoth','trium','valdghardt','vandenheim',
    'whitewand','zistrus','rtk','esp','int','dex','wis','att','hrs','git','grep','npm','pytest','rpol',
    'dangerously-bypass-approvals-and-sandbox','description','describes','lists','list','listed','sources','matrix',
    'data','diff','downloaded','output','pdfs','printed','profile','requirements','select','selected','specified',
    'st-level','test','token','top','untracked','x-in','youll','final','chapter','chapters','codex','constraints',
    'guidelines','issue','log','logs','noisy','numerical','paper','rating','representation','procedures','application',
    'fishs','necroticgnome','radiation','science','nuclear','archontos'
) | ForEach-Object { [void]$drop.Add($_) }

$candidates = @($data | ForEach-Object Word | Where-Object { -not $drop.Contains($_) } | Sort-Object -Unique)
Set-Content -LiteralPath 'codex-scratch\batch-50c-source-candidates.txt' -Value $candidates
[pscustomobject]@{ ExactRemaining=$data.Count; Dropped=$drop.Count; Curated=$candidates.Count } | ConvertTo-Json -Compress
