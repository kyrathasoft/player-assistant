[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://publish\.obsidian\.md/[^/]+/?$')]
    [string]$SiteUrl,

    [Parameter(Mandatory = $true)]
    [string]$OutputCsv,

    [string]$HistoryFile = 'C:\repos\player-assistant\obsidian-wiki-word-count.md',

    [string]$LocalPostsRoot = 'C:\repos\player-assistant\Release\Posts',

    [ValidateRange(1, 32)]
    [int]$ThrottleLimit = 8,

    [ValidateRange(1, 6)]
    [int]$RetryCount = 3
)

$ErrorActionPreference = 'Stop'
$siteRoot = $SiteUrl.TrimEnd('/')
$sitemapUrl = "$siteRoot/sitemap.xml"

[xml]$sitemap = (Invoke-WebRequest -Uri $sitemapUrl -UseBasicParsing).Content
$pageUrls = @($sitemap.urlset.url.loc | ForEach-Object { [string]$_ })
if ($pageUrls.Count -eq 0) {
    throw "The sitemap contains no page URLs: $sitemapUrl"
}

$shell = (Invoke-WebRequest -Uri $pageUrls[0] -UseBasicParsing).Content
$siteInfoMatch = [regex]::Match(
    $shell,
    'window\.siteInfo=(\{.*?\});',
    [System.Text.RegularExpressions.RegexOptions]::Singleline
)
if (-not $siteInfoMatch.Success) {
    throw 'Could not discover the Obsidian Publish site host and UID.'
}

$siteInfo = $siteInfoMatch.Groups[1].Value | ConvertFrom-Json
$accessRoot = "https://$($siteInfo.host)/access/$($siteInfo.uid)"
$pathPrefix = "/$($siteInfo.slug)/"

$targets = for ($index = 0; $index -lt $pageUrls.Count; $index++) {
    $pageUrl = $pageUrls[$index]
    $uri = [uri]$pageUrl
    if (-not $uri.AbsolutePath.StartsWith($pathPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Sitemap URL is outside the expected site path: $pageUrl"
    }

    $encodedSitePath = $uri.AbsolutePath.Substring($pathPrefix.Length)
    $pagePath = [uri]::UnescapeDataString($encodedSitePath.Replace('+', ' '))
    $encodedMarkdownPath = (($pagePath -split '/') | ForEach-Object {
        [uri]::EscapeDataString($_)
    }) -join '/'

    [pscustomobject]@{
        Index       = $index
        PagePath    = $pagePath
        Url         = $pageUrl
        MarkdownUrl = "$accessRoot/$encodedMarkdownPath.md"
    }
}

$results = $targets | ForEach-Object -Parallel {
    $target = $_
    $maximumAttempts = $using:RetryCount
    $raw = $null
    $lastError = $null

    for ($attempt = 1; $attempt -le $maximumAttempts; $attempt++) {
        try {
            $raw = (Invoke-WebRequest -Uri $target.MarkdownUrl -UseBasicParsing -TimeoutSec 30).Content
            $lastError = $null
            break
        }
        catch {
            $lastError = $_.Exception.Message
            if ($attempt -lt $maximumAttempts) {
                Start-Sleep -Milliseconds (250 * $attempt)
            }
        }
    }

    if ($null -eq $raw) {
        return [pscustomobject]@{
            Index     = $target.Index
            PagePath  = $target.PagePath
            Url       = $target.Url
            WordCount = $null
            Status    = "Failed: $lastError"
        }
    }

    $text = $raw.Replace("`r", '')
    if ($text.StartsWith("---`n", [System.StringComparison]::Ordinal)) {
        $text = [regex]::Replace(
            $text,
            '\A---\n.*?\n---\s*(?:\n|$)',
            '',
            [System.Text.RegularExpressions.RegexOptions]::Singleline
        )
    }

    $contentLines = [System.Collections.Generic.List[string]]::new()
    $inForumHeader = $false
    foreach ($line in ($text -split "`n")) {
        if ($line -match '^Message #\d+\s*$') {
            $inForumHeader = $true
            continue
        }
        if ($inForumHeader) {
            if ($line -match '^\s*$' -or $line -match '^-{3,}\s*$') {
                $inForumHeader = $false
            }
            continue
        }
        if ($line -match '^This message was last (edited|updated)\b') {
            continue
        }
        if ($line -match '^\s*\[[^\]]+\]:\s+\S+') {
            continue
        }
        $contentLines.Add($line)
    }

    $clean = $contentLines -join "`n"
    $singleline = [System.Text.RegularExpressions.RegexOptions]::Singleline
    $multiline = [System.Text.RegularExpressions.RegexOptions]::Multiline

    $clean = [regex]::Replace($clean, '<!--.*?-->', ' ', $singleline)
    $clean = [regex]::Replace($clean, '%%.*?%%', ' ', $singleline)
    $clean = [regex]::Replace($clean, '(?m)^\s*```[^\n]*$', ' ', $multiline)
    $clean = [regex]::Replace($clean, '!\[\[[^\]]+\]\]', ' ')
    $clean = [regex]::Replace($clean, '\[!\[[^\]]*\]\([^\)]*\)\]\([^\)]*\)', ' ')
    $clean = [regex]::Replace($clean, '!\[[^\]]*\]\([^\)]*\)', ' ')
    $clean = [regex]::Replace($clean, '\[\[([^\]|#]+)(?:#[^\]|]+)?\|([^\]]+)\]\]', '$2')
    $clean = [regex]::Replace($clean, '\[\[([^\]#]+)(?:#[^\]]+)?\]\]', '$1')
    $clean = [regex]::Replace($clean, '\[([^\]]+)\]\([^\)]*\)', '$1')
    $clean = [regex]::Replace($clean, '(?m)^\s*>\s*\[![^\]]+\][+-]?\s*', ' ', $multiline)
    $clean = [regex]::Replace($clean, 'https?://\S+', ' ')
    $clean = [regex]::Replace($clean, '<[^>]+>', ' ')
    $clean = [System.Net.WebUtility]::HtmlDecode($clean)

    $wordCount = [regex]::Matches(
        $clean,
        "[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*"
    ).Count

    [pscustomobject]@{
        Index     = $target.Index
        PagePath  = $target.PagePath
        Url       = $target.Url
        WordCount = $wordCount
        Status    = 'OK'
    }
} -ThrottleLimit $ThrottleLimit

$orderedResults = @($results | Sort-Object Index)
$outputPath = [System.IO.Path]::GetFullPath($OutputCsv)
$outputDirectory = [System.IO.Path]::GetDirectoryName($outputPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$orderedResults |
    Select-Object PagePath, Url, WordCount, Status |
    Export-Csv -LiteralPath $outputPath -NoTypeInformation -Encoding utf8

$failedPages = @($orderedResults | Where-Object Status -ne 'OK')
$successfulPages = $orderedResults.Count - $failedPages.Count
$totalWords = ($orderedResults |
    Where-Object Status -eq 'OK' |
    Measure-Object -Property WordCount -Sum).Sum

$summary = [ordered]@{
    SiteUrl         = $siteRoot
    SitemapUrl      = $sitemapUrl
    SitemapPages    = $orderedResults.Count
    SuccessfulPages = $successfulPages
    FailedPages     = $failedPages.Count
    TotalWords      = [long]$totalWords
    OutputCsv       = $outputPath
}

if ($failedPages.Count -gt 0) {
    [pscustomobject]$summary | ConvertTo-Json
    exit 2
}

$localCounterPath = Join-Path $PSScriptRoot 'count-local-post-words.ps1'
if (-not [System.IO.File]::Exists($localCounterPath)) {
    throw "The local post counter is missing: $localCounterPath"
}
$localCounts = (& $localCounterPath -PostsRoot $LocalPostsRoot) | ConvertFrom-Json
$summary['LocalIcFiles'] = [int]$localCounts.IcFiles
$summary['LocalIcWords'] = [long]$localCounts.IcWords
$summary['LocalOocFiles'] = [int]$localCounts.OocFiles
$summary['LocalOocWords'] = [long]$localCounts.OocWords

$historyPath = [System.IO.Path]::GetFullPath($HistoryFile)
$historyDirectory = [System.IO.Path]::GetDirectoryName($historyPath)
if (-not [string]::IsNullOrWhiteSpace($historyDirectory)) {
    [System.IO.Directory]::CreateDirectory($historyDirectory) | Out-Null
}
if (-not [System.IO.File]::Exists($historyPath)) {
    [System.IO.File]::WriteAllText(
        $historyPath,
        "# Obsidian Wiki Word Count`r`n`r`n",
        [System.Text.UTF8Encoding]::new($false))
}

$historyDate = [DateTime]::Today.ToString(
    'M/d/yyyy',
    [System.Globalization.CultureInfo]::InvariantCulture)
$formattedTotal = ([long]$totalWords).ToString(
    'N0',
    [System.Globalization.CultureInfo]::InvariantCulture)
$formattedIcTotal = ([long]$localCounts.IcWords).ToString(
    'N0',
    [System.Globalization.CultureInfo]::InvariantCulture)
$formattedOocTotal = ([long]$localCounts.OocWords).ToString(
    'N0',
    [System.Globalization.CultureInfo]::InvariantCulture)
$historyEntry = "- As of $historyDate, the wiki contained $formattedTotal words; total IC words: $formattedIcTotal; total OOC words: $formattedOocTotal"
[System.IO.File]::AppendAllText(
    $historyPath,
    "$historyEntry`r`n",
    [System.Text.UTF8Encoding]::new($false))

$summary['HistoryFile'] = $historyPath
$summary['HistoryEntry'] = $historyEntry
[pscustomobject]$summary | ConvertTo-Json
