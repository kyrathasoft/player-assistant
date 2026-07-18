param(
    [string[]]$SelectedUrls
)

$ErrorActionPreference = 'Stop'

$base = 'https://publish.obsidian.md/scarlethorizons'
$sitemapCandidates = @("$base/sitemap.xml", "$base/sitemap-index.xml")
$sitemapXml = $null
$sitemapSource = $null

foreach ($candidate in $sitemapCandidates) {
    try {
        $response = Invoke-WebRequest -Uri $candidate -Headers @{
            'User-Agent' = 'player-assistant-orcish-candidate-sampler/1.0'
            Accept = 'application/xml,text/xml,*/*'
        } -TimeoutSec 30
        if ($response.StatusCode -eq 200 -and $response.Content -match '<loc>') {
            $sitemapXml = [xml]$response.Content
            $sitemapSource = $candidate
            break
        }
    }
    catch {
    }
}

if ($null -eq $sitemapXml) {
    throw 'Unable to retrieve the Obsidian wiki sitemap.'
}

$urls = @(
    $sitemapXml.SelectNodes('//*[local-name()="loc"]') |
        ForEach-Object { $_.InnerText.Trim() } |
        Where-Object { $_ -like "$base/*" } |
        Sort-Object -Unique
)

if ($urls.Count -lt 10) {
    throw "Only $($urls.Count) eligible sitemap URLs were found."
}

$selected = if ($SelectedUrls.Count -gt 0) {
    @($SelectedUrls)
}
else {
    @($urls | Get-Random -Count 10)
}
$lexiconText = Get-Content -Raw (Join-Path $PSScriptRoot '..\OrcishTranslatorUtility.cs')
$known = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($match in [regex]::Matches($lexiconText, 'new\("(?<english>(?:[^"\\]|\\.)*)"\s*,')) {
    $english = [regex]::Unescape($match.Groups['english'].Value).ToLowerInvariant()
    [void]$known.Add($english)
    foreach ($part in [regex]::Matches($english, "[a-z][a-z'-]{2,}")) {
        [void]$known.Add($part.Value)
    }
}

$stop = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@(
    'about', 'above', 'after', 'again', 'against', 'also', 'among', 'another', 'around', 'because',
    'been', 'before', 'being', 'below', 'between', 'both', 'could', 'does', 'doing', 'down', 'during',
    'each', 'either', 'from', 'further', 'have', 'having', 'here', 'hers', 'herself', 'himself', 'into',
    'itself', 'just', 'more', 'most', 'other', 'ours', 'ourselves', 'over', 'same', 'should', 'some',
    'such', 'than', 'that', 'their', 'theirs', 'them', 'themselves', 'then', 'there', 'these', 'they',
    'this', 'those', 'through', 'under', 'until', 'very', 'what', 'when', 'where', 'which', 'while',
    'who', 'whom', 'whose', 'will', 'with', 'would', 'your', 'yours', 'yourself', 'yourselves', 'were',
    'was', 'are', 'and', 'but', 'for', 'not', 'the', 'you', 'his', 'her', 'she', 'him', 'its', 'our',
    'out', 'use', 'used', 'using', 'page', 'pages', 'site', 'website', 'wiki', 'obsidian', 'publish',
    'published', 'markdown', 'frontmatter', 'navigation', 'navigate', 'menu', 'sidebar', 'footer',
    'header', 'search', 'graph', 'view', 'open', 'close', 'click', 'link', 'links', 'linked', 'embed',
    'embedded', 'image', 'images', 'attachment', 'attachments', 'canvas', 'script', 'style',
    'stylesheet', 'html', 'http', 'https', 'www', 'com', 'scarlethorizons', 'index', 'home', 'content',
    'title', 'aliases', 'alias', 'tags', 'tag', 'date', 'created', 'updated', 'css', 'class', 'target',
    'blank', 'width', 'height', 'align', 'center', 'left', 'right', 'table', 'column', 'row', 'span',
    'div', 'nbsp', 'amp', 'quot', 'true', 'false', 'null', 'none', 'todo', 'callout', 'toc', 'backlinks',
    'bab', 'enc', 'etc', 'fgt', 'str', 'thac', 'th-level'
) | ForEach-Object { [void]$stop.Add($_) }

$counts = @{}
$pageResults = @()

foreach ($url in $selected) {
    try {
        $page = Invoke-WebRequest -Uri $url -Headers @{
            'User-Agent' = 'player-assistant-orcish-candidate-sampler/1.0'
            Accept = 'text/html,*/*'
        } -TimeoutSec 30
        $match = [regex]::Match($page.Content, 'window\.preloadPage=f\("(?<url>https://[^" ]+?\.md)"\)')
        if (-not $match.Success) {
            throw "Markdown endpoint was not found for $url"
        }

        $mdUrl = $match.Groups['url'].Value.Replace('\u0026', '&').Replace('\/', '/')
        $markdown = (Invoke-WebRequest -Uri $mdUrl -Headers @{
            'User-Agent' = 'player-assistant-orcish-candidate-sampler/1.0'
            Accept = 'text/markdown,text/plain'
        } -TimeoutSec 30).Content
    }
    catch {
        $pageResults += [pscustomobject]@{
            PageUrl = $url
            MarkdownUrl = $null
            MarkdownCharacters = 0
            Error = $_.Exception.Message
        }
        continue
    }

    $clean = $markdown -replace '(?ms)^---\s*.*?\s*---\s*', ' '
    $clean = $clean -replace '(?ms)```.*?```', ' '
    $clean = $clean -replace '(?m)^\s*\[[^\]]+\]:\s*\S+.*$', ' '
    $clean = $clean -replace '!\[\[[^\]]+\]\]', ' '
    $clean = $clean -replace '!\[[^\]]*\]\([^)]*\)', ' '
    $clean = $clean -replace '\[(?<label>[^\]]+)\]\([^)]*\)', '${label}'
    $clean = $clean -replace '\[\[(?<target>[^\]|#]+)(?:#[^\]|]+)?(?:\|(?<label>[^\]]+))?\]\]', '${label} ${target}'
    $clean = $clean -replace '<[^>]+>', ' '
    $clean = $clean -replace 'https?://\S+', ' '
    $clean = [System.Net.WebUtility]::HtmlDecode($clean)

    foreach ($tokenMatch in [regex]::Matches($clean, "(?<![A-Za-z])[A-Za-z][A-Za-z'-]{2,}(?![A-Za-z])")) {
        $token = $tokenMatch.Value.Trim("'-").ToLowerInvariant()
        if ($token -match "^(?:[a-z]+)'(?:s|d|ll|re|ve)$") {
            $token = $token -replace "'(?:s|d|ll|re|ve)$", ''
        }
        if ($token.Length -lt 3 -or $stop.Contains($token) -or $known.Contains($token)) {
            continue
        }
        if ($counts.ContainsKey($token)) {
            $counts[$token]++
        }
        else {
            $counts[$token] = 1
        }
    }

    $pageResults += [pscustomobject]@{
        PageUrl = $url
        MarkdownUrl = $mdUrl
        MarkdownCharacters = $markdown.Length
        Error = $null
    }
}

$candidates = @(
    $counts.GetEnumerator() |
        Sort-Object @{ Expression = 'Value'; Descending = $true }, @{ Expression = 'Key'; Descending = $false } |
        ForEach-Object { [pscustomobject]@{ Word = $_.Key; Occurrences = $_.Value } }
)

[pscustomobject]@{
    Sitemap = $sitemapSource
    SitemapUrlCount = $urls.Count
    Pages = $pageResults
    CandidateWordCount = $candidates.Count
    Candidates = $candidates
} | ConvertTo-Json -Depth 6
