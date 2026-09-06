[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PostsRoot
)

$ErrorActionPreference = 'Stop'
$markdownHelpersPath = Join-Path $PSScriptRoot 'word-count-markdown-helpers.ps1'
if (-not [System.IO.File]::Exists($markdownHelpersPath)) {
    throw "The Markdown counting helpers are missing: $markdownHelpersPath"
}
. $markdownHelpersPath

function Get-VisibleWordCount {
    param(
        [Parameter(Mandatory = $true)][string]$Html,
        [Parameter(Mandatory = $true)][ValidateSet('IC', 'OOC')][string]$Kind,
        [Parameter(Mandatory = $true)][string]$SourcePath
    )

    $singlelineIgnoreCase =
        [System.Text.RegularExpressions.RegexOptions]::Singleline -bor
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    $usedBodyFallback = $false

    if ($Kind -eq 'IC') {
        if ([regex]::IsMatch(
            $Html,
            '<a\b[^>]*\bhref=([''"])[^''"]*\bmsgpage=\d+[^''"]*\1',
            $singlelineIgnoreCase)) {
            throw "Paginated IC content was found in $SourcePath; refresh it from the RPOL show-all URL before counting."
        }

        $matches = [regex]::Matches(
            $Html,
            '<div\b[^>]*\bclass=([''"])[^''"]*\bmessagebody\b[^''"]*\1[^>]*>(.*?)</div>\s*</div><!--\s*1\s*-->',
            $singlelineIgnoreCase)
    }
    else {
        $matches = [regex]::Matches(
            $Html,
            '<div\b[^>]*\bclass=([''"])[^''"]*\bmessagebody\b[^''"]*\1[^>]*>(.*?)</div>\s*</div><!--\s*1\s*-->',
            $singlelineIgnoreCase)

        if ($matches.Count -eq 0) {
            $matches = [regex]::Matches(
                $Html,
                '<section\b[^>]*\bclass=([''"])[^''"]*\bcontent\b[^''"]*\1[^>]*>(.*?)</section>',
                $singlelineIgnoreCase)
        }

        if ($matches.Count -eq 0) {
            $matches = [regex]::Matches(
                $Html,
                '<body\b([^>]*)>(.*?)</body>',
                $singlelineIgnoreCase)
            $usedBodyFallback = $matches.Count -gt 0
        }
    }

    if ($matches.Count -eq 0) {
        throw "No $Kind content region was found in $SourcePath"
    }

    $content = ($matches | ForEach-Object { $_.Groups[2].Value }) -join "`n"
    if ($usedBodyFallback) {
        $content = [regex]::Replace(
            $content,
            '<header\b[^>]*\bid=([''"])title-block-header\1[^>]*>.*?</header>',
            ' ',
            $singlelineIgnoreCase)
        $content = [regex]::Replace(
            $content,
            'Message #\d+\s*<br\s*/?>.*?[\-—–]{3,}\s*<br\s*/?>',
            ' ',
            $singlelineIgnoreCase)
        $content = [regex]::Replace(
            $content,
            '<p\b[^>]*>\s*Message #\d+\s*<br\s*/?>.*?</p>',
            ' ',
            $singlelineIgnoreCase)
    }
    $content = [regex]::Replace(
        $content,
        'This message was last (edited|updated)\b.*?(?=</(?:span|p|div)>)',
        ' ',
        $singlelineIgnoreCase)
    $content = [regex]::Replace(
        $content,
        '<(script|style|noscript)\b[^>]*>.*?</\1>',
        ' ',
        $singlelineIgnoreCase)
    $content = [regex]::Replace(
        $content,
        '<!--.*?-->',
        ' ',
        $singlelineIgnoreCase)
    $content = [regex]::Replace(
        $content,
        '<img\b[^>]*>',
        ' ',
        $singlelineIgnoreCase)
    $content = [regex]::Replace($content, '<[^>]+>', ' ')
    $content = [System.Net.WebUtility]::HtmlDecode($content)
    $content = [regex]::Replace($content, 'https?://\S+', ' ')

    return [regex]::Matches(
        $content,
        "[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*"
    ).Count
}

function Get-MarkdownVisibleWordCount {
    param(
        [Parameter(Mandatory = $true)][string]$Markdown,
        [Parameter(Mandatory = $true)][string]$SourcePath
    )

    $text = Remove-MarkdownFencedBlocks $Markdown
    if ($text.StartsWith("---`n", [System.StringComparison]::Ordinal)) {
        $text = [regex]::Replace(
            $text,
            '\A---\n.*?\n---\s*(?:\n|$)',
            '',
            [System.Text.RegularExpressions.RegexOptions]::Singleline)
    }

    $contentLines = [System.Collections.Generic.List[string]]::new()
    $skipMessageMetadata = $false
    foreach ($line in ($text.Replace("`r", '') -split "`n")) {
        if ($line -match '^\s*Message\s+#\d+\s*$') {
            $skipMessageMetadata = $true
            continue
        }
        if ($skipMessageMetadata) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $skipMessageMetadata = $false
            continue
        }
        if ($line -match '^\s*This message was last (edited|updated)\b') { continue }
        $contentLines.Add($line)
    }

    $content = $contentLines -join "`n"
    $singleline = [System.Text.RegularExpressions.RegexOptions]::Singleline
    $content = [regex]::Replace($content, '<!--.*?-->', ' ', $singleline)
    $content = [regex]::Replace($content, '!\[[^\]]*\]\([^\)]*\)', ' ')
    $content = [regex]::Replace($content, '\[\[([^\]|#]+)(?:#[^\]|]+)?\|([^\]]+)\]\]', '$2')
    $content = [regex]::Replace($content, '\[\[([^\]#]+)(?:#[^\]]+)?\]\]', '$1')
    $content = [regex]::Replace($content, '\[([^\]]+)\]\([^\)]*\)', '$1')
    $content = [regex]::Replace($content, 'https?://\S+', ' ')
    $content = [regex]::Replace($content, '<[^>]+>', ' ')
    $content = [regex]::Replace($content, '(?m)^\s{0,3}#{1,6}\s+', ' ')
    $content = [System.Net.WebUtility]::HtmlDecode($content)

    return [regex]::Matches(
        $content,
        "[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*"
    ).Count
}

$resolvedPostsRoot = [System.IO.Path]::GetFullPath($PostsRoot)
$totals = [ordered]@{}

foreach ($kind in @('IC', 'OOC')) {
    $directory = Join-Path $resolvedPostsRoot $kind
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "Local post directory not found: $directory"
    }

    $files = @(
        Get-ChildItem -LiteralPath $directory -File -Recurse |
            Where-Object { $_.Extension -in @('.html', '.md') -and $_.Name -notmatch '\.bak-' }
    )
    if ($files.Count -eq 0) {
        throw "No current HTML or Markdown post files were found in $directory"
    }
    Write-Verbose "$kind files ($($files.Count)): $($files.Name -join ', ')"

    [long]$wordTotal = 0
    foreach ($file in $files) {
        $content = [System.IO.File]::ReadAllText($file.FullName)
        if ($file.Extension -ieq '.md') {
            $fileWordCount = Get-MarkdownVisibleWordCount -Markdown $content -SourcePath $file.FullName
        }
        else {
            $fileWordCount = Get-VisibleWordCount -Html $content -Kind $kind -SourcePath $file.FullName
        }
        $wordTotal += $fileWordCount
        Write-Verbose "$kind words ($fileWordCount): $($file.FullName)"
    }

    if ($kind -eq 'IC') {
        $totals['IcFiles'] = $files.Count
        $totals['IcWords'] = $wordTotal
    }
    else {
        $totals['OocFiles'] = $files.Count
        $totals['OocWords'] = $wordTotal
    }
}

[pscustomobject]$totals | ConvertTo-Json
