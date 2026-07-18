param(
    [string]$OutputPath = 'codex-scratch\blog-discovery-round3.json'
)

$ErrorActionPreference = 'Stop'

$headers = [System.Net.Http.Headers.ProductInfoHeaderValue]::new('player-assistant-orcish-discovery', '1.0')
$client = [System.Net.Http.HttpClient]::new()
$client.DefaultRequestHeaders.UserAgent.Add($headers)
$client.Timeout = [TimeSpan]::FromSeconds(45)

$sitemap = $client.GetStringAsync('https://bryanmiller.us/blog/sitemap.post.1.xml').GetAwaiter().GetResult()
$allUrls = @([regex]::Matches($sitemap, '<loc>(?<url>.*?)</loc>') | ForEach-Object {
    [System.Net.WebUtility]::HtmlDecode($_.Groups['url'].Value).TrimEnd('/')
})
$ledgerUrls = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
Select-String -Path 'dont-scrape-again.md' -Pattern '^- https://bryanmiller\.us/blog/' | ForEach-Object {
    [void]$ledgerUrls.Add($_.Line.Substring(2).Trim().TrimEnd('/'))
}
$unusedUrls = @($allUrls | Where-Object { -not $ledgerUrls.Contains($_) })

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

$taskByUrl = @{}
foreach ($url in $unusedUrls) { $taskByUrl[$url] = $client.GetStringAsync($url) }
[System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($taskByUrl.Values), 90000)

$results = [System.Collections.Generic.List[object]]::new()
foreach ($url in $unusedUrls) {
    $task = $taskByUrl[$url]
    if (-not $task.IsCompletedSuccessfully) { continue }
    $html = $task.Result
    $bodyMatch = [regex]::Match($html, '(?is)<div[^>]+itemprop=["'']articleBody["''][^>]*>(?<body>.*?)</div>\s*<!--//desc-->')
    if (-not $bodyMatch.Success) { continue }

    $body = [regex]::Replace($bodyMatch.Groups['body'].Value, '(?is)<(script|style|pre)\b.*?</\1>', ' ')
    $body = [regex]::Replace($body, '(?is)<[^>]+>', ' ')
    $body = [System.Net.WebUtility]::HtmlDecode($body)
    $body = [regex]::Replace($body, '\s+', ' ').Trim()
    if ($body.Length -lt 100) { continue }

    $titleMatch = [regex]::Match($html, '(?is)<meta[^>]+property=["'']og:title["''][^>]+content=["''](?<title>.*?)["'']')
    $title = if ($titleMatch.Success) { [System.Net.WebUtility]::HtmlDecode($titleMatch.Groups['title'].Value).Trim() } else { $url.Split('/')[-1] }
    $words = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($match in [regex]::Matches($body, "(?<![A-Za-z])[A-Za-z][A-Za-z'-]{2,}(?![A-Za-z])")) {
        $word = $match.Value.ToLowerInvariant().Trim("'", '-')
        $word = $word -replace "'(s|d|ll|re|ve|m|t)$", ''
        if ($word.Length -ge 3 -and -not $existing.Contains($word) -and -not $stopWords.Contains($word)) { [void]$words.Add($word) }
    }

    $results.Add([pscustomobject]@{
        url = $url
        title = $title
        characters = $body.Length
        candidateCount = $words.Count
        candidates = @($words | Sort-Object)
    })
}

$manifest = [pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    sitemapCount = $allUrls.Count
    recordedBlogUrlCount = $ledgerUrls.Count
    unusedUrlCount = $unusedUrls.Count
    usablePageCount = $results.Count
    lexiconEntryCount = $entries.Count
    pages = @($results | Sort-Object candidateCount -Descending)
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$manifest.pages | Select-Object -First 35 url,title,characters,candidateCount | ConvertTo-Json -Depth 3
