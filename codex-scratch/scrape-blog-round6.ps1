$ErrorActionPreference = 'Stop'
$urls = @(
    'https://bryanmiller.us/blog/2026/01/herne-the-hunter',
    'https://bryanmiller.us/blog/2026/01/scene-verbs',
    'https://bryanmiller.us/blog/2026/01/the-down',
    'https://bryanmiller.us/blog/2026/01/the-shadowdim-actual-play-reports',
    'https://bryanmiller.us/blog/2026/01/valdghar',
    'https://bryanmiller.us/blog/2026/02/beastiary-of-shadowdim-campaign',
    'https://bryanmiller.us/blog/2026/02/beastmen-of-arden-vul',
    'https://bryanmiller.us/blog/2026/02/shadowdim-goblins',
    'https://bryanmiller.us/blog/2026/02/swift-dorns-magical-longsword',
    'https://bryanmiller.us/blog/2026/05/jelb-garrick',
    'https://bryanmiller.us/blog/2026/05/npc-homer-docil'
)
$client = [System.Net.Http.HttpClient]::new()
$client.DefaultRequestHeaders.UserAgent.ParseAdd('player-assistant-orcish-round6/1.0')
$client.Timeout = [TimeSpan]::FromSeconds(45)
$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path 'Release\player-assistant.dll'))
$type = $assembly.GetType('PlayerAssistant.OrcishTranslatorUtility')
$entries = $type.GetMethod('GetLexiconEntries', [System.Reflection.BindingFlags]'Public,Static').Invoke($null, @())
$existing = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) { [void]$existing.Add($entry.English) }
$stop = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@('about','after','again','also','another','because','been','before','being','between','both','could','does','doing','each','every','from','have','having','into','itself','just','more','most','much','must','only','other','ourselves','should','some','such','than','that','their','them','themselves','then','there','these','they','this','those','through','under','until','very','were','what','when','where','which','while','will','with','would','your','article','author','blog','category','comments','content','header','html','image','index','page','post','rpol','section','site','state','text','website') | ForEach-Object { [void]$stop.Add($_) }

$tasks = @{}
foreach ($url in $urls) { $tasks[$url] = $client.GetStringAsync($url) }
[System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($tasks.Values), 90000)
$raw = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$pages = [System.Collections.Generic.List[object]]::new()
foreach ($url in $urls) {
    $task = $tasks[$url]
    if (-not $task.IsCompletedSuccessfully) { $pages.Add([pscustomobject]@{url=$url;fetched=$false;characters=0;candidateCount=0;candidates=@()}); continue }
    $html = $task.Result
    $match = [regex]::Match($html, '(?is)<div[^>]+itemprop=["'']articleBody["''][^>]*>(?<body>.*?)</div>\s*<!--//desc-->')
    $body = if ($match.Success) { $match.Groups['body'].Value } else { '' }
    $body = [regex]::Replace($body, '(?is)<(script|style|pre)\b.*?</\1>', ' ')
    $body = [System.Net.WebUtility]::HtmlDecode([regex]::Replace($body, '(?is)<[^>]+>', ' '))
    $body = [regex]::Replace($body, '\s+', ' ').Trim()
    $words = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($hit in [regex]::Matches($body, "(?<![A-Za-z])[A-Za-z][A-Za-z'-]{2,}(?![A-Za-z])")) {
        $word = $hit.Value.ToLowerInvariant().Trim("'", '-') -replace "'(s|d|ll|re|ve|m|t)$", ''
        if ($word.Length -ge 3 -and -not $existing.Contains($word) -and -not $stop.Contains($word)) { [void]$words.Add($word); [void]$raw.Add($word) }
    }
    $pages.Add([pscustomobject]@{url=$url;fetched=$true;characters=$body.Length;candidateCount=$words.Count;candidates=@($words|Sort-Object)})
}
$raw | Sort-Object | Set-Content -LiteralPath 'codex-scratch\blog-round6-raw-candidates.txt' -Encoding utf8
[pscustomobject]@{generatedAt=(Get-Date).ToUniversalTime().ToString('o');pageCount=$urls.Count;fetchedPageCount=@($pages|Where-Object fetched).Count;nonEmptyPageCount=@($pages|Where-Object{$_.candidateCount-gt 0}).Count;rawCandidateCount=$raw.Count;lexiconEntryCount=$entries.Count;pages=$pages} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath 'codex-scratch\blog-round6-scrape-manifest.json' -Encoding utf8
[pscustomobject]@{pages=$urls.Count;fetched=@($pages|Where-Object fetched).Count;nonEmpty=@($pages|Where-Object{$_.candidateCount-gt 0}).Count;raw=$raw.Count}|ConvertTo-Json
