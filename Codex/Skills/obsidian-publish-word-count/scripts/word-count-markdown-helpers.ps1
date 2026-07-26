function Remove-MarkdownFencedBlocks {
    param(
        [AllowEmptyString()]
        [string]$Text
    )

    $contentLines = [System.Collections.Generic.List[string]]::new()
    $inFencedBlock = $false
    $fenceCharacter = ''
    $fenceLength = 0

    foreach ($line in ($Text.Replace("`r", '') -split "`n")) {
        if (-not $inFencedBlock -and $line -match '^\s*(?<fence>`{3,}|~{3,})') {
            $inFencedBlock = $true
            $fenceCharacter = $Matches.fence.Substring(0, 1)
            $fenceLength = $Matches.fence.Length
            continue
        }
        if ($inFencedBlock) {
            $closingFencePattern = '^\s*' + [regex]::Escape($fenceCharacter) +
                '{' + $fenceLength + ',}\s*$'
            if ($line -match $closingFencePattern) {
                $inFencedBlock = $false
            }
            continue
        }
        $contentLines.Add($line)
    }

    return $contentLines -join "`n"
}

function Protect-SpreadsheetText {
    param(
        [AllowEmptyString()]
        [string]$Text
    )

    if ($Text -match '^\s*[=+\-@]') {
        return "'$Text"
    }
    return $Text
}
