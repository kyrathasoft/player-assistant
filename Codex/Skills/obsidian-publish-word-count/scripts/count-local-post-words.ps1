[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PostsRoot
)

$ErrorActionPreference = 'Stop'

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

$resolvedPostsRoot = [System.IO.Path]::GetFullPath($PostsRoot)
$totals = [ordered]@{}

foreach ($kind in @('IC', 'OOC')) {
    $directory = Join-Path $resolvedPostsRoot $kind
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "Local post directory not found: $directory"
    }

    $files = @(
        Get-ChildItem -LiteralPath $directory -File -Recurse -Filter '*.html' |
            Where-Object Name -NotMatch '\.bak-'
    )
    if ($files.Count -eq 0) {
        throw "No current HTML post files were found in $directory"
    }
    Write-Verbose "$kind files ($($files.Count)): $($files.Name -join ', ')"

    [long]$wordTotal = 0
    foreach ($file in $files) {
        $html = [System.IO.File]::ReadAllText($file.FullName)
        $fileWordCount = Get-VisibleWordCount -Html $html -Kind $kind -SourcePath $file.FullName
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
