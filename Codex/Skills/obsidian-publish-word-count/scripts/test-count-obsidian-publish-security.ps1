$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'word-count-markdown-helpers.ps1')

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Expected,

        [Parameter(Mandatory = $true)]
        [object]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected'; actual '$Actual'."
    }
}

$backtickFixture = "Visible before`n``````powershell`nWrite-Output 'excluded words'`n```````nVisible after"
Assert-Equal 'Visible before
Visible after' (Remove-MarkdownFencedBlocks $backtickFixture) 'Backtick fenced code was counted.'

$tildeFixture = "One`n~~~ text`nhidden prose`n~~~~`nTwo"
Assert-Equal 'One
Two' (Remove-MarkdownFencedBlocks $tildeFixture) 'Tilde fenced code was counted.'

Assert-Equal "'=HYPERLINK(`"https://example.test`")" `
    (Protect-SpreadsheetText '=HYPERLINK("https://example.test")') `
    'Formula-like CSV text was not neutralized.'
Assert-Equal "'  @SUM(1,2)" `
    (Protect-SpreadsheetText '  @SUM(1,2)') `
    'Whitespace-prefixed formula-like CSV text was not neutralized.'
Assert-Equal 'ordinary/page' `
    (Protect-SpreadsheetText 'ordinary/page') `
    'Ordinary CSV text was modified.'

Write-Output 'Obsidian Publish word-count security tests passed.'
