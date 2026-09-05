[CmdletBinding()]
param(
    [string]$RepoRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$resolvedRoot = [IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
$legacyRoot = [IO.Path]::Combine('C:\repos', 'player-assistant')

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (-not $Condition) {
        throw $Message
    }
}

Assert-Condition (-not $resolvedRoot.Equals($legacyRoot, [StringComparison]::OrdinalIgnoreCase)) 'The relocation test must not run against the legacy repository path.'
Assert-Condition (Test-Path -LiteralPath (Join-Path $resolvedRoot '.git') -PathType Container) 'The copied repository is missing its Git metadata.'

$scriptExtensions = @('*.ps1', '*.py', '*.bat', '*.cmd', '*.vbs', '*.js', '*.cs')
$operationalScripts = foreach ($pattern in $scriptExtensions) {
    Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/]\.git[\\/]' -and $_.Name -ne 'verify-repository-relocation.ps1' }
}
$legacyMatches = foreach ($script in $operationalScripts) {
    Select-String -LiteralPath $script.FullName -SimpleMatch $legacyRoot -ErrorAction SilentlyContinue
}
Assert-Condition (@($legacyMatches).Count -eq 0) 'An operational script still references the old repository path.'

$taskExpectations = @{
    'Player Assistant Full Word Count Publisher' = 'publish-full-word-counts.ps1'
    'Player Assistant RPOL Snapshot Publisher' = 'publish-rpol-snapshots.ps1'
    'Player Assistant Mouse Keep-Alive' = 'keep-alive-mouse-hidden.vbs'
}
foreach ($taskName in $taskExpectations.Keys) {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
    $command = @($task.Actions | ForEach-Object { "$($_.Execute) $($_.Arguments)" }) -join "`n"
    Assert-Condition ($command.Contains($resolvedRoot)) "Scheduled task '$taskName' does not point to the relocated repository."
    Assert-Condition ($command.Contains($taskExpectations[$taskName])) "Scheduled task '$taskName' does not invoke the expected relocated script."
}

Write-Output 'Repository relocation policy verified.'
