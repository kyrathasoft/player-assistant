[CmdletBinding()]
param(
    [switch]$InstallScheduledTask,
    [string]$TaskName = 'Player Assistant Full Word Count Publisher',
    [ValidatePattern('^https://publish\.obsidian\.md/[^/]+/?$')]
    [string]$SiteUrl = 'https://publish.obsidian.md/scarlethorizons',
    [string]$OutputCsv,
    [string]$HistoryFile,
    [string]$LocalPostsRoot
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$installedPwshPath = Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'
$pwshCommand = Get-Command 'pwsh.exe' -ErrorAction SilentlyContinue
$pwshPath = if (Test-Path -LiteralPath $installedPwshPath -PathType Leaf) {
    $installedPwshPath
} elseif ($null -ne $pwshCommand) {
    $pwshCommand.Source
} else {
    $null
}
if ([string]::IsNullOrWhiteSpace($pwshPath)) {
    throw 'PowerShell 7 (pwsh.exe) is required for the full word-count crawler.'
}
if ([string]::IsNullOrWhiteSpace($OutputCsv)) {
    $OutputCsv = Join-Path $repositoryRoot 'Release\word-counts\obsidian-wiki-word-count.csv'
}
if ([string]::IsNullOrWhiteSpace($HistoryFile)) {
    $HistoryFile = Join-Path $repositoryRoot 'obsidian-wiki-word-count.md'
}
if ([string]::IsNullOrWhiteSpace($LocalPostsRoot)) {
    $LocalPostsRoot = Join-Path $repositoryRoot 'Release\Posts'
}

$countScript = Join-Path $repositoryRoot 'Codex\Skills\obsidian-publish-word-count\scripts\count-obsidian-publish-words.ps1'
$localCountScript = Join-Path $repositoryRoot 'Codex\Skills\obsidian-publish-word-count\scripts\count-local-post-words.ps1'
$publishScript = Join-Path $repositoryRoot 'web-deploy\publish-word-counts.ps1'
$resultPath = Join-Path $repositoryRoot 'Release\word-count-publish-result.json'

if ($InstallScheduledTask) {
    $scriptPath = $MyInvocation.MyCommand.Path
    $action = New-ScheduledTaskAction -Execute $pwshPath -Argument (
        "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$scriptPath`"")
    $triggers = @(
        New-ScheduledTaskTrigger -Daily -At '4:00 AM'
        New-ScheduledTaskTrigger -Daily -At '8:30 PM'
        New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At '8:00 AM'
        New-ScheduledTaskTrigger -Weekly -DaysOfWeek Wednesday -At '5:00 PM'
    )
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable `
        -ExecutionTimeLimit (New-TimeSpan -Hours 2) `
        -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Settings $settings `
        -Description 'Runs a complete Obsidian wiki and local IC/OOC recount, then publishes and verifies the signed broker snapshot.' `
        -Force | Out-Null
    Write-Output "Installed scheduled task '$TaskName'."
    return
}

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "Run the full word-count publisher with PowerShell 7: '$pwshPath' -NoProfile -File '$($MyInvocation.MyCommand.Path)'"
}

foreach ($requiredScript in @($countScript, $localCountScript, $publishScript)) {
    if (-not (Test-Path -LiteralPath $requiredScript -PathType Leaf)) {
        throw "Required word-count script not found: $requiredScript"
    }
}
foreach ($kind in @('IC', 'OOC')) {
    $directory = Join-Path $LocalPostsRoot $kind
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "Local post directory not found: $directory"
    }
}

$localPreflightJson = @(& $localCountScript -PostsRoot $LocalPostsRoot) -join [Environment]::NewLine
$localPreflight = $localPreflightJson | ConvertFrom-Json
if ([int]$localPreflight.IcFiles -lt 1 -or
    [long]$localPreflight.IcWords -lt 1 -or
    [int]$localPreflight.OocFiles -lt 1 -or
    [long]$localPreflight.OocWords -lt 1) {
    throw 'The local IC/OOC corpus is incomplete; the wiki crawl and publication were blocked.'
}

$summaryJson = @(& $countScript `
    -SiteUrl $SiteUrl `
    -OutputCsv $OutputCsv `
    -HistoryFile $HistoryFile `
    -LocalPostsRoot $LocalPostsRoot) -join [Environment]::NewLine
$summary = $summaryJson | ConvertFrom-Json
if ([int]$summary.FailedPages -ne 0 -or
    [int]$summary.SuccessfulPages -ne [int]$summary.SitemapPages -or
    [int]$summary.SitemapPages -lt 1 -or
    [long]$summary.TotalWords -lt 1 -or
    [int]$summary.LocalIcFiles -lt 1 -or
    [long]$summary.LocalIcWords -lt 1 -or
    [int]$summary.LocalOocFiles -lt 1 -or
    [long]$summary.LocalOocWords -lt 1) {
    throw 'The full wiki and local IC/OOC recount was incomplete; publication was blocked.'
}

$now = [DateTimeOffset]::UtcNow
$observedAt = [DateTimeOffset]::new(
    $now.Ticks - ($now.Ticks % [TimeSpan]::TicksPerMillisecond),
    [TimeSpan]::Zero)
$publication = & $publishScript `
    -WikiPages ([int]$summary.SitemapPages) `
    -WikiWords ([long]$summary.TotalWords) `
    -IcFiles ([int]$summary.LocalIcFiles) `
    -IcWords ([long]$summary.LocalIcWords) `
    -OocFiles ([int]$summary.LocalOocFiles) `
    -OocWords ([long]$summary.LocalOocWords) `
    -ObservedAt $observedAt

if (-not [bool]$publication.Published -or
    -not [bool]$publication.SourcePublished -or
    [DateTimeOffset]$publication.ObservedAt -ne $observedAt -or
    [int]$publication.WikiPages -ne [int]$summary.SitemapPages -or
    [long]$publication.WikiWords -ne [long]$summary.TotalWords -or
    [int]$publication.IcFiles -ne [int]$summary.LocalIcFiles -or
    [long]$publication.IcWords -ne [long]$summary.LocalIcWords -or
    [int]$publication.OocFiles -ne [int]$summary.LocalOocFiles -or
    [long]$publication.OocWords -ne [long]$summary.LocalOocWords) {
    throw 'The authenticated broker did not return the exact full recount snapshot.'
}

$result = [ordered]@{
    completed_at = [DateTimeOffset]::UtcNow.ToString('o')
    observed_at = $publication.ObservedAt
    wiki = [ordered]@{ pages = $publication.WikiPages; words = $publication.WikiWords }
    ic = [ordered]@{ files = $publication.IcFiles; words = $publication.IcWords }
    ooc = [ordered]@{ files = $publication.OocFiles; words = $publication.OocWords }
    output_csv = [System.IO.Path]::GetFullPath($OutputCsv)
    history_file = [System.IO.Path]::GetFullPath($HistoryFile)
    published = $true
}
$resultDirectory = Split-Path -Parent $resultPath
[System.IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
[System.IO.File]::WriteAllText(
    $resultPath,
    ($result | ConvertTo-Json -Depth 4),
    [System.Text.UTF8Encoding]::new($false))
[pscustomobject]$result
