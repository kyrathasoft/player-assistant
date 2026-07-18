$ErrorActionPreference = 'Stop'

$discovery = Get-Content -Raw -LiteralPath 'codex-scratch\blog-discovery-round3.json' | ConvertFrom-Json
$selectedPages = @($discovery.pages | Select-Object -First 12)
if ($selectedPages.Count -ne 12) { throw "Expected 12 selected pages, found $($selectedPages.Count)." }

$raw = @($selectedPages.candidates | Sort-Object -Unique)
$rejected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    # Named characters, places, deities, and setting-specific exonyms.
    'anubis','barnaby','bugthrak','cabe','debens','duat','exodore','exxie','griz-maw','indiana','jones','kaelen','keeva','ma''at','newmarket','orym','snik','stelga','tacere','thalas','theo','thoth','thothian-beastman','thothians','thrangir','thutmos','thutmose','vedecab','voss','weskenim','zorael',
    # Rule notation, page scaffolding, and authoring residue.
    'aac','authentication','bab','backup','bofd','botd','bro','contents','dex','dmg','document','doi','image','index','instructions','int','link','links','list','log','max','movie','pages','parses','processing','programming','radians','referencing','repository','resources','samples','sections','site','snapshot','snapshots','snippets','str','summary','tag','temp','tested','testing','th-level','tools','update','updated','utility',
    # Contraction fragments, misspellings, malformed tokens, and vocalizations.
    'aren','didn','hadn','hasn','heores','isn','pedstal','saphires','shh','shouldn','stalagtites','surface-loords','tha','thiste','thoom','tink-tink','tink-tink-tink','tsk-tsked','wasn','weren','wouldn',
    # Low-value session chatter or modern bookkeeping language.
    'built','call','course','either','here','leave','none','off','others','paychecks','payday','seen','true','world'
) | ForEach-Object { [void]$rejected.Add($_) }

$accepted = @($raw | Where-Object { -not $rejected.Contains($_) } | Sort-Object)
$removed = @($raw | Where-Object { $rejected.Contains($_) } | Sort-Object)

$accepted | Set-Content -LiteralPath 'codex-scratch\blog-round3-source-candidates.txt' -Encoding utf8
$removed | Set-Content -LiteralPath 'codex-scratch\blog-round3-rejected-candidates.txt' -Encoding utf8
$accepted | Set-Content -LiteralPath 'codex-scratch\candidates.txt' -Encoding utf8

$manifest = [pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    pageCount = $selectedPages.Count
    rawCandidateCount = $raw.Count
    sourceCandidateCount = $accepted.Count
    rejectedCandidateCount = $removed.Count
    pages = @($selectedPages | Select-Object url,title,characters,candidateCount)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath 'codex-scratch\blog-round3-manifest.json' -Encoding utf8
$manifest | ConvertTo-Json -Depth 4
