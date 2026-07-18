$ErrorActionPreference = 'Stop'

$urls = @(
    'https://bryanmiller.us/blog/2026/02/the-city-of-prelm'
    'https://bryanmiller.us/blog/2026/02/shadowdim-4-treasure-tithing'
    'https://bryanmiller.us/blog/2026/02/shadowdim-6-the-syncretic-breakthrough'
    'https://bryanmiller.us/blog/2025/12/endless-rime-session-1-scene-sky-glyphs'
    'https://bryanmiller.us/blog/2026/01/endless-rime-session-1-scene-the-whispering-wind'
    'https://bryanmiller.us/blog/2026/01/scene-last-night-at-home'
    'https://bryanmiller.us/blog/2026/02/the-chamber-of-rectitude'
    'https://bryanmiller.us/blog/2025/12/endless-rime-session-1'
    'https://bryanmiller.us/blog/2026/02/shadowdim-9-the-sovereign-of-the-mushroom-forest'
    'https://bryanmiller.us/blog/2026/02/shadowdim-8-the-heartstone-run'
    'https://bryanmiller.us/blog/2026/01/scene-an-unexpected-return'
    'https://bryanmiller.us/blog/2026/05/principality-of-brine'
)

$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path 'Release\player-assistant.dll'))
$type = $assembly.GetType('PlayerAssistant.OrcishTranslatorUtility')
$entries = $type.GetMethod('GetLexiconEntries', [System.Reflection.BindingFlags]'Public,Static').Invoke($null, @())
$existing = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) { [void]$existing.Add($entry.English) }

$stopWords = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    'about','after','again','against','also','among','another','around','because','been','before','being','between','both','could','did','does','doing','down','during','each','even','every','from','further','had','has','have','having','herself','himself','into','itself','just','more','most','much','must','ones','only','other','our','ours','ourselves','out','over','own','same','should','some','such','than','that','their','theirs','them','themselves','then','there','these','they','this','those','through','under','until','very','was','were','what','when','where','which','while','who','whom','whose','why','will','with','would','you','your','yours','yourself','yourselves',
    'article','author','blog','browser','category','checklist','comments','content','desc','email','finder','header','html','http','https','indexed','javascript','margin','package','page','posts','profile','queries','refresh','resolution','response','rpol','section','state','system','text','token','tokens','website'
) | ForEach-Object { [void]$stopWords.Add($_) }

$counts = @{}
$sources = @{}
$pages = [System.Collections.Generic.List[object]]::new()

foreach ($url in $urls) {
    $response = Invoke-WebRequest -Uri $url -Headers @{
        'User-Agent' = 'player-assistant-orcish-candidate-scraper/1.0'
        Accept = 'text/html,*/*'
    } -TimeoutSec 45
    if ($response.StatusCode -ne 200) { throw "Unexpected HTTP $($response.StatusCode) for $url" }

    $bodyMatch = [regex]::Match($response.Content, '(?is)<div[^>]+itemprop=["'']articleBody["''][^>]*>(?<body>.*?)</div>\s*<!--//desc-->')
    if (-not $bodyMatch.Success) { throw "No article body found for $url" }

    $body = [regex]::Replace($bodyMatch.Groups['body'].Value, '(?is)<(script|style|pre)\b.*?</\1>', ' ')
    $body = [regex]::Replace($body, '(?is)<[^>]+>', ' ')
    $body = [System.Net.WebUtility]::HtmlDecode($body)
    $body = [regex]::Replace($body, '\s+', ' ').Trim()
    if ($body.Length -lt 100) { throw "Article body was unexpectedly short for $url" }

    $titleMatch = [regex]::Match($response.Content, '(?is)<meta[^>]+property=["'']og:title["''][^>]+content=["''](?<title>.*?)["'']')
    $title = if ($titleMatch.Success) { [System.Net.WebUtility]::HtmlDecode($titleMatch.Groups['title'].Value).Trim() } else { $url.Split('/')[-1] }
    $pageWords = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($match in [regex]::Matches($body, "(?<![A-Za-z])[A-Za-z][A-Za-z'-]{2,}(?![A-Za-z])")) {
        $word = $match.Value.ToLowerInvariant().Trim("'", '-')
        $word = $word -replace "'(s|d|ll|re|ve|m|t)$", ''
        if ($word.Length -lt 3 -or $existing.Contains($word) -or $stopWords.Contains($word)) { continue }
        if (-not $counts.ContainsKey($word)) { $counts[$word] = 0; $sources[$word] = [System.Collections.Generic.List[string]]::new() }
        $counts[$word]++
        if ($pageWords.Add($word)) { $sources[$word].Add($url) }
    }

    $pages.Add([pscustomobject]@{ url = $url; title = $title; characters = $body.Length; candidateCount = $pageWords.Count })
}

$candidates = foreach ($word in ($counts.Keys | Sort-Object)) {
    [pscustomobject]@{ word = $word; occurrences = $counts[$word]; urls = @($sources[$word]) }
}

$manifest = [pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    lexiconEntryCount = $entries.Count
    pageCount = $pages.Count
    candidateCount = @($candidates).Count
    pages = @($pages)
    candidates = @($candidates)
}

$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath 'codex-scratch\blog-followup-scrape.json' -Encoding utf8
$candidates.word | Set-Content -LiteralPath 'codex-scratch\blog-followup-raw-candidates.txt' -Encoding utf8
$manifest | Select-Object pageCount,candidateCount,lexiconEntryCount,pages | ConvertTo-Json -Depth 4
