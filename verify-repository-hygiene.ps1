param(
    [string]$RepoRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (!$Condition) {
        throw $Message
    }
}

$gitIgnorePath = Join-Path $RepoRoot '.gitignore'
Assert-Condition -Condition (Test-Path -LiteralPath $gitIgnorePath -PathType Leaf) -Message 'The repository .gitignore file is missing.'

$ignoreLines = @(Get-Content -LiteralPath $gitIgnorePath | ForEach-Object { $_.Trim() })
foreach ($requiredPattern in @('.hermes-tmp*', '/local-corpus*/')) {
    Assert-Condition -Condition ($ignoreLines -contains $requiredPattern) -Message ".gitignore must include '$requiredPattern'."
}

foreach ($samplePath in @(
    '.hermes-tmp.verify/probe.txt',
    '.hermes-tmpfoo/probe.txt',
    'nested/.hermes-tmpfoo/probe.txt',
    'local-corpus/probe.txt',
    'local-corpus-experiment/probe.txt'
)) {
    & git -C $RepoRoot check-ignore --quiet --no-index -- $samplePath
    Assert-Condition -Condition ($LASTEXITCODE -eq 0) -Message "Repository hygiene rules do not ignore '$samplePath'."
}

foreach ($samplePath in @('local-corpus.txt', 'src/local-corpus/probe.txt', 'docs/corpus-notes.md')) {
    & git -C $RepoRoot check-ignore --quiet --no-index -- $samplePath
    Assert-Condition -Condition ($LASTEXITCODE -eq 1) -Message "Repository hygiene rules are too broad and unexpectedly ignore '$samplePath'."
}

$trackedPaths = @(& git -C $RepoRoot ls-files)
Assert-Condition -Condition ($LASTEXITCODE -eq 0) -Message 'Unable to inspect tracked repository paths.'

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($trackedPath in $trackedPaths) {
    $segments = @($trackedPath -split '/')
    if ($segments | Where-Object { $_ -like '.hermes-tmp*' }) {
        [void]$violations.Add($trackedPath)
        continue
    }

    if ($segments.Count -gt 1 -and $segments[0] -like 'local-corpus*') {
        [void]$violations.Add($trackedPath)
    }
}

Assert-Condition -Condition ($violations.Count -eq 0) -Message (
    'Local corpus or Hermes scratch paths must not be tracked: ' + ($violations -join ', '))

Write-Output 'Repository hygiene verified.'
