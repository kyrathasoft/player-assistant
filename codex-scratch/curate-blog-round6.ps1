$ErrorActionPreference = 'Stop'
$raw = @(Get-Content -LiteralPath 'codex-scratch\blog-round6-raw-candidates.txt' | Where-Object { $_.Trim() })
$rejected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    # Proper names, campaign exonyms, systems, and biological taxon names.
    'adrienic','anganach','aveda','cabe','cervus','crom','deino','docil','exodore','hequeti','homer','narsileon','newmarket','osric','stenn','thutmose','valdghar','valdghardt','varumani','venator','weskenim',
    # Platform, metadata, authoring, and low-value conversational residue.
    'among','here','ones','others','out','patreon','substack','temp','true','verbs','weird','whom','world'
) | ForEach-Object { [void]$rejected.Add($_) }
$accepted = @($raw | Where-Object { -not $rejected.Contains($_) } | Sort-Object -Unique)
$removed = @($raw | Where-Object { $rejected.Contains($_) } | Sort-Object -Unique)
$accepted | Set-Content -LiteralPath 'codex-scratch\blog-round6-source-candidates.txt' -Encoding utf8
$removed | Set-Content -LiteralPath 'codex-scratch\blog-round6-rejected-candidates.txt' -Encoding utf8
$accepted | Set-Content -LiteralPath 'codex-scratch\candidates.txt' -Encoding utf8
[pscustomobject]@{generatedAt=(Get-Date).ToUniversalTime().ToString('o');rawCandidateCount=$raw.Count;sourceCandidateCount=$accepted.Count;rejectedCandidateCount=$removed.Count} | ConvertTo-Json | Set-Content -LiteralPath 'codex-scratch\blog-round6-curation-manifest.json' -Encoding utf8
[pscustomobject]@{raw=$raw.Count;source=$accepted.Count;rejected=$removed.Count}|ConvertTo-Json
