$ErrorActionPreference = 'Stop'

$data = Get-Content -Raw -LiteralPath 'codex-scratch\blog-followup-scrape.json' | ConvertFrom-Json
$rejected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    # Named characters, places, peoples, and setting-specific proper forms.
    'arcantryl','arctos','astableon','astablion','blackfist','bujant','certracles','cluney','cromm-thoth','deino','dordogne','drogan','elkfist','en-amenti','ewen','exodore','fodor','france','gahn','ghindar','hrowaka','jenkla','joora','juga','kaelen','kuju','kulva','lexie','lycandrus','malachai','mike','mog-ur','nanefer','newmarket','norda','nort','occoro','oghma','oghman','ordna','pah''et','pale-urm','pelrm','phlebotomas','plutark','prelrm','psuche-stratos','rhekla','rosk','roskelly','setite','setites','setmose','stenn','stenosian','sulla','thalas','thon-takar','thon-vahl','thoth','thoth-beast','thoth-hermes','thothians','tradewell','ursus','uta','valdghar','vey','vodor','zuidgeest','zuul','zuulite',
    # Contraction fragments, misspellings, vocalizations, and malformed tokens.
    'aren','biolumenescent','chesire','comerade','cro','didn','doesn','frostden','frostdens','goatses','hasn','hearbeats','hmppf','intensies','interferring','isn','moreso','obeissance','sharphened','sharphening','shinker','shouldn','su''hed','trible','ughhh','wasn','woohoo',
    # Web, implementation, and document residue rather than source vocabulary.
    'atk','authentication','average','bab','battlemap','browse','com','connectivity','crits','digital','dmg','download','downloadable','editing','exported','file','flaticon','folder','footnotes','gme','helper','hyperlink','idrive','images','imported','index','italicized','layout','lbs','licenses','links','log','max','misc','misspellings','obsidian','onedrive','online','osr','post','processed','processing','product','prover','python','queried','redirected','reopen','request','requests','resetting','resources','rnd','scans','select','sentence','snapshot','sourced','sources','spec','states','storage','summary','synopsis','systems','terminology','test','tools','update','user','valid','visualize',
    # Session and rules scaffolding, not reusable in-world vocabulary.
    'after-action','fighter-gestalt','fighter-thief','hitpoints','level-ups','non-fray','player-characters','re-rolls','session','to-damage',
    # Low-value discourse fragments and accidental common-page residue.
    'built','call','course','either','here','leave','none','off','others','seen','true','unto','world'
) | ForEach-Object { [void]$rejected.Add($_) }

$accepted = @($data.candidates | Where-Object { -not $rejected.Contains($_.word) } | Sort-Object word)
$removed = @($data.candidates | Where-Object { $rejected.Contains($_.word) } | Sort-Object word)

$accepted.word | Set-Content -LiteralPath 'codex-scratch\candidates.txt' -Encoding utf8
$removed.word | Set-Content -LiteralPath 'codex-scratch\blog-followup-rejected-candidates.txt' -Encoding utf8

[pscustomobject]@{
    rawCount = @($data.candidates).Count
    acceptedCount = $accepted.Count
    rejectedCount = $removed.Count
    unmatchedRejectRules = @($rejected | Where-Object { $_ -notin $data.candidates.word }).Count
} | ConvertTo-Json
