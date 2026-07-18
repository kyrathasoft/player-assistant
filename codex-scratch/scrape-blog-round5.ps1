param(
    [int]$Seed = 20260718
)

$ErrorActionPreference = 'Stop'
$client = [System.Net.Http.HttpClient]::new()
$client.DefaultRequestHeaders.UserAgent.ParseAdd('player-assistant-orcish-round5/1.0')
$client.Timeout = [TimeSpan]::FromSeconds(45)

$sitemap = $client.GetStringAsync('https://bryanmiller.us/blog/sitemap.post.1.xml').GetAwaiter().GetResult()
$allUrls = @([regex]::Matches($sitemap, '<loc>(?<url>.*?)</loc>') | ForEach-Object {
    [System.Net.WebUtility]::HtmlDecode($_.Groups['url'].Value).TrimEnd('/')
})
$ledger = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
Select-String -Path 'dont-scrape-again.md' -Pattern '^- https://bryanmiller\.us/blog/' | ForEach-Object {
    [void]$ledger.Add($_.Line.Substring(2).Trim().TrimEnd('/'))
}
$unused = [System.Collections.Generic.List[string]]::new()
foreach ($url in $allUrls) { if (-not $ledger.Contains($url)) { $unused.Add($url) } }
if ($unused.Count -lt 100) { throw "Only $($unused.Count) unused blog URLs remain." }

$random = [System.Random]::new($Seed)
for ($index = $unused.Count - 1; $index -gt 0; $index--) {
    $swapIndex = $random.Next($index + 1)
    $swapValue = $unused[$index]
    $unused[$index] = $unused[$swapIndex]
    $unused[$swapIndex] = $swapValue
}
$selected = @($unused | Select-Object -First 100)

$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path 'Release\player-assistant.dll'))
$type = $assembly.GetType('PlayerAssistant.OrcishTranslatorUtility')
$entries = $type.GetMethod('GetLexiconEntries', [System.Reflection.BindingFlags]'Public,Static').Invoke($null, @())
$existing = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) { [void]$existing.Add($entry.English) }

$stopWords = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    'about','after','again','against','also','among','another','around','because','been','before','being','between','both','could','did','does','doing','down','during','each','even','every','from','further','had','has','have','having','herself','himself','into','itself','just','more','most','much','must','ones','only','other','our','ours','ourselves','out','over','own','same','should','some','such','than','that','their','theirs','them','themselves','then','there','these','they','this','those','through','under','until','very','was','were','what','when','where','which','while','who','whom','whose','why','will','with','would','you','your','yours','yourself','yourselves',
    'article','author','blog','browser','category','comments','content','email','header','html','http','https','indexed','javascript','margin','package','page','posts','profile','refresh','resolution','response','rpol','section','state','system','text','token','tokens','website'
) | ForEach-Object { [void]$stopWords.Add($_) }

$taskByUrl = @{}
foreach ($url in $selected) { $taskByUrl[$url] = $client.GetStringAsync($url) }
[System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($taskByUrl.Values), 90000)

$pages = [System.Collections.Generic.List[object]]::new()
$raw = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($url in $selected) {
    $task = $taskByUrl[$url]
    if (-not $task.IsCompletedSuccessfully) {
        $pages.Add([pscustomobject]@{ url=$url; fetched=$false; characters=0; candidateCount=0; candidates=@() })
        continue
    }
    $html = $task.Result
    $bodyMatch = [regex]::Match($html, '(?is)<div[^>]+itemprop=["'']articleBody["''][^>]*>(?<body>.*?)</div>\s*<!--//desc-->')
    $body = if ($bodyMatch.Success) { $bodyMatch.Groups['body'].Value } else { '' }
    $body = [regex]::Replace($body, '(?is)<(script|style|pre)\b.*?</\1>', ' ')
    $body = [regex]::Replace($body, '(?is)<[^>]+>', ' ')
    $body = [System.Net.WebUtility]::HtmlDecode($body)
    $body = [regex]::Replace($body, '\s+', ' ').Trim()
    $words = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($match in [regex]::Matches($body, "(?<![A-Za-z])[A-Za-z][A-Za-z'-]{2,}(?![A-Za-z])")) {
        $word = $match.Value.ToLowerInvariant().Trim("'", '-')
        $word = $word -replace "'(s|d|ll|re|ve|m|t)$", ''
        if ($word.Length -ge 3 -and -not $existing.Contains($word) -and -not $stopWords.Contains($word)) {
            [void]$words.Add($word)
            [void]$raw.Add($word)
        }
    }
    $pages.Add([pscustomobject]@{ url=$url; fetched=$true; characters=$body.Length; candidateCount=$words.Count; candidates=@($words | Sort-Object) })
}

$selected | Set-Content -LiteralPath 'codex-scratch\blog-round5-selected-urls.txt' -Encoding utf8
$raw | Sort-Object | Set-Content -LiteralPath 'codex-scratch\blog-round5-raw-candidates.txt' -Encoding utf8
[pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    randomSeed = $Seed
    availableUnusedUrlCount = $unused.Count
    selectedUrlCount = $selected.Count
    fetchedPageCount = @($pages | Where-Object fetched).Count
    nonEmptyPageCount = @($pages | Where-Object { $_.candidateCount -gt 0 }).Count
    rawCandidateCount = $raw.Count
    lexiconEntryCount = $entries.Count
    pages = $pages
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath 'codex-scratch\blog-round5-scrape-manifest.json' -Encoding utf8

[pscustomobject]@{ selected=$selected.Count; fetched=@($pages | Where-Object fetched).Count; nonEmpty=@($pages | Where-Object { $_.candidateCount -gt 0 }).Count; rawCandidates=$raw.Count } | ConvertTo-Json
