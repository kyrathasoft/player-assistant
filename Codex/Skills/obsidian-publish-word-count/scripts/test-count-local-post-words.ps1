$ErrorActionPreference = 'Stop'

$fixtureRoot = Join-Path $env:TEMP (
    'player-assistant-word-count-test-' + [Guid]::NewGuid().ToString('N'))
$resolvedSystemTemp = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'

try {
    New-Item -ItemType Directory `
        -Path (Join-Path $fixtureRoot 'IC'), (Join-Path $fixtureRoot 'OOC') `
        -Force | Out-Null

    $icHtml = @'
<html><body>
<div class='message'>
<div class='messagebody' id='msg1'>Three player words.<span class='systemmessage'>This message was last edited by the player at 12:00, Today.</span></div>
</div><!-- 1 --></div><!-- 2 -->
</body></html>
'@
    $oocHtml = @'
<html><body>
<div class='message'>
<div class='messagebody' id='msg1'>Two words.</div>
</div><!-- 1 --></div><!-- 2 -->
</body></html>
'@
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'IC\complete.html'), $icHtml)
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'OOC\complete.html'), $oocHtml)

    $icMarkdown = @'
---
title: Fixture
---
Message #99
Chapter metadata by 123456

Markdown three words.
This message was last edited by the player at 12:00, Today.
'@
    $oocMarkdown = @'
Message #100
OOC metadata by 123456

Markdown two words.
'@
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'IC\complete.md'), $icMarkdown)
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'OOC\complete.md'), $oocMarkdown)

    $counterPath = Join-Path $PSScriptRoot 'count-local-post-words.ps1'
    $result = (& $counterPath -PostsRoot $fixtureRoot) | ConvertFrom-Json
    if ($result.IcWords -ne 6 -or $result.OocWords -ne 5) {
        throw "Edit-notice fixture returned unexpected totals: $($result | ConvertTo-Json -Compress)"
    }

    $paginatedHtml = $icHtml.Replace(
        '</body>',
        "<a href='display.cgi?gi=80170&amp;ti=7&amp;msgpage=1'>1</a></body>")
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'IC\complete.html'), $paginatedHtml)
    try {
        & $counterPath -PostsRoot $fixtureRoot | Out-Null
        throw 'The local counter accepted paginated IC input.'
    }
    catch {
        if ($_.Exception.Message -notmatch 'Paginated IC content') {
            throw
        }
    }

    Write-Output 'Local post word-count tests passed.'
}
finally {
    $resolvedFixtureRoot = [IO.Path]::GetFullPath($fixtureRoot)
    if ($resolvedFixtureRoot.StartsWith(
            $resolvedSystemTemp,
            [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedFixtureRoot) -like 'player-assistant-word-count-test-*') {
        Remove-Item -LiteralPath $resolvedFixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
