param(
    [string]$SiteUrl = 'https://publish.obsidian.md/scarlethorizons',
    [string]$OutputPath = (Join-Path $PSScriptRoot 'campaign-search.json'),
    [ValidateRange(1, 64)][int]$Concurrency = 12,
    [string[]]$ExcludedPageTitles = @('XP Tracking')
)

$ErrorActionPreference = 'Stop'

function ConvertTo-AccessPath {
    param(
        [Parameter(Mandatory = $true)][uri]$PageUri,
        [Parameter(Mandatory = $true)][string]$SiteSlug
    )

    $prefix = "/$SiteSlug/"
    if (!$PageUri.AbsolutePath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Page URL is outside the expected Obsidian Publish site: $PageUri"
    }

    $encodedPath = $PageUri.AbsolutePath.Substring($prefix.Length)
    $decodedPath = [uri]::UnescapeDataString($encodedPath.Replace('+', ' '))
    $segments = $decodedPath.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { [uri]::EscapeDataString($_) }
    return ($segments -join '/') + '.md'
}

function ConvertFrom-MarkdownToSearchText {
    param([AllowEmptyString()][string]$Markdown)

    if ([string]::IsNullOrWhiteSpace($Markdown)) {
        return ''
    }

    $text = $Markdown -replace '^\s*---\s*\r?\n[\s\S]*?\r?\n---\s*(?:\r?\n|$)', ' '
    $text = $text -replace '<!--[\s\S]*?-->', ' '
    $text = $text -replace '!\[\[([^\]|]+)(?:\|[^\]]+)?\]\]', '$1'
    $text = $text -replace '\[\[([^\]|]+)\|([^\]]+)\]\]', '$2'
    $text = $text -replace '\[\[([^\]]+)\]\]', '$1'
    $text = $text -replace '!\[([^\]]*)\]\([^)]+\)', '$1'
    $text = $text -replace '\[([^\]]+)\]\([^)]+\)', '$1'
    $text = $text -replace 'https?://\S+', ' '
    $text = $text -replace '<[^>]+>', ' '
    $text = $text -replace '(?m)^\s*```[^\r\n]*$', ' '
    $text = $text -replace '(?m)^\s*(?:#{1,6}|>|[-+*]\s|\d+\.\s)', ' '
    $text = $text -replace '[*_~`|]', ' '
    $text = [System.Net.WebUtility]::HtmlDecode($text)
    return ([regex]::Replace($text, '\s+', ' ')).Trim()
}

function Get-PageTitle {
    param([Parameter(Mandatory = $true)][uri]$PageUri)

    $segment = $PageUri.AbsolutePath.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries)[-1]
    return [uri]::UnescapeDataString($segment.Replace('+', ' ')).Trim()
}

$siteUri = [uri]$SiteUrl
$siteSlug = $siteUri.AbsolutePath.Trim('/')
if ([string]::IsNullOrWhiteSpace($siteSlug)) {
    throw 'The Obsidian Publish site URL must include its site slug.'
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.AutomaticDecompression = [System.Net.DecompressionMethods]::All
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(45)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('PlayerAssistant-PWA-Indexer/1.0')

try {
    $siteHtml = $client.GetStringAsync($siteUri).GetAwaiter().GetResult()
    $uidMatch = [regex]::Match($siteHtml, '"uid":"(?<uid>[^"]+)"')
    $hostMatch = [regex]::Match($siteHtml, '"host":"(?<host>[^"]+)"')
    if (!$uidMatch.Success -or !$hostMatch.Success) {
        throw 'Obsidian Publish site metadata could not be found.'
    }

    $uid = $uidMatch.Groups['uid'].Value
    $publishHost = $hostMatch.Groups['host'].Value
    if ($publishHost -notmatch '^publish-\d+\.obsidian\.md$') {
        throw "Unexpected Obsidian Publish content host: $publishHost"
    }

    $sitemapUrl = [uri]::new($siteUri, "$($siteUri.AbsolutePath.TrimEnd('/'))/sitemap.xml")
    $sitemapXml = $client.GetStringAsync($sitemapUrl).GetAwaiter().GetResult()
    $sitemap = [xml]$sitemapXml
    $pageUrls = @($sitemap.SelectNodes("//*[local-name()='loc']") |
        ForEach-Object { [string]$_.InnerText } |
        Where-Object { $_.StartsWith("$($siteUri.Scheme)://$($siteUri.Host)/$siteSlug/", [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object -Unique)

    $requests = foreach ($pageUrl in $pageUrls) {
        $pageUri = [uri]$pageUrl
        $pageTitle = Get-PageTitle -PageUri $pageUri
        if ($ExcludedPageTitles -contains $pageTitle) {
            continue
        }
        $accessPath = ConvertTo-AccessPath -PageUri $pageUri -SiteSlug $siteSlug
        [pscustomobject]@{
            Title = $pageTitle
            Url = $pageUri.AbsoluteUri
            MarkdownUrl = "https://$publishHost/access/$uid/$accessPath"
        }
    }

    $pages = [System.Collections.Generic.List[object]]::new()
    $failedCount = 0
    $contentPageCount = 0
    $wordCount = 0L

    for ($offset = 0; $offset -lt $requests.Count; $offset += $Concurrency) {
        $last = [Math]::Min($offset + $Concurrency - 1, $requests.Count - 1)
        $batch = @($requests[$offset..$last])
        $tasks = @($batch | ForEach-Object { $client.GetAsync([uri]$_.MarkdownUrl) })

        for ($index = 0; $index -lt $batch.Count; $index++) {
            $markdown = ''
            try {
                $response = $tasks[$index].GetAwaiter().GetResult()
                try {
                    if ($response.IsSuccessStatusCode) {
                        $markdown = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    }
                    else {
                        $failedCount++
                    }
                }
                finally {
                    $response.Dispose()
                }
            }
            catch {
                $failedCount++
            }

            $content = ConvertFrom-MarkdownToSearchText -Markdown $markdown
            if ($content.Length -gt 0) {
                $contentPageCount++
                $wordCount += [regex]::Matches($content, "[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)*").Count
            }

            $pages.Add([ordered]@{
                title = $batch[$index].Title
                url = $batch[$index].Url
                content = $content
            })
        }
    }

    $payload = [ordered]@{
        schemaVersion = 2
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        source = $SiteUrl.TrimEnd('/')
        pageCount = $pages.Count
        contentPageCount = $contentPageCount
        failedPageCount = $failedCount
        wordCount = $wordCount
        pages = $pages
    }

    $outputDirectory = Split-Path -Parent $OutputPath
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    $jsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
    $jsonOptions.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
    $json = [System.Text.Json.JsonSerializer]::Serialize($payload, $jsonOptions)
    [System.IO.File]::WriteAllText($OutputPath, $json, [System.Text.UTF8Encoding]::new($false))

    Write-Output "Campaign search index generated: $($pages.Count) public pages, $contentPageCount with Markdown content, $wordCount searchable words, $failedCount fetch failures."
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
