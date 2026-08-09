[CmdletBinding()]
param([string]$RepoRoot = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = $PSScriptRoot
}

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$scheduledPublisherPath = Join-Path $RepoRoot 'publish-full-word-counts.ps1'
Assert-Condition (Test-Path -LiteralPath $scheduledPublisherPath -PathType Leaf) 'The scheduled full word-count publisher is missing.'
$installerPath = Join-Path $RepoRoot 'install-full-word-count-scheduled-task.ps1'
Assert-Condition (Test-Path -LiteralPath $installerPath -PathType Leaf) 'The portable full word-count task installer is missing.'

$scheduledPublisher = Get-Content -Raw -LiteralPath $scheduledPublisherPath
$installer = Get-Content -Raw -LiteralPath $installerPath
$brokerPublisher = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'web-deploy\publish-word-counts.ps1')
$snapshotPublisher = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'publish-rpol-snapshots.ps1')

Assert-Condition ($scheduledPublisher.Contains("New-ScheduledTaskTrigger -Daily -At '4:00 AM'") -and
    $scheduledPublisher.Contains("New-ScheduledTaskTrigger -Daily -At '8:30 PM'") -and
    $scheduledPublisher.Contains("New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At '8:00 AM'") -and
    $scheduledPublisher.Contains("New-ScheduledTaskTrigger -Weekly -DaysOfWeek Wednesday -At '5:00 PM'")) 'The full recount must run daily at 4:00 AM and 8:30 PM, Sunday at 8:00 AM, and Wednesday at 5:00 PM local Central time.'
Assert-Condition ($snapshotPublisher.Contains("New-ScheduledTaskTrigger -Daily -At '3:00 AM'") -and
    $snapshotPublisher.Contains("New-ScheduledTaskTrigger -Weekly -DaysOfWeek Wednesday -At '5:00 PM'")) 'The RPOL snapshot publisher must run daily at 3:00 AM and Wednesday at 5:00 PM local Central time.'
Assert-Condition ($scheduledPublisher.Contains("Get-Command 'pwsh.exe'") -and
    $scheduledPublisher.Contains("Join-Path `$env:ProgramFiles 'PowerShell\7\pwsh.exe'") -and
    $scheduledPublisher.Contains('PSVersion.Major -lt 7')) 'The full recount must use PowerShell 7 because the canonical crawler uses parallel execution.'
Assert-Condition $scheduledPublisher.Contains('New-ScheduledTaskSettingsSet -StartWhenAvailable') 'The full recount task must start when available.'
Assert-Condition ($scheduledPublisher.Contains('count-obsidian-publish-words.ps1') -and
    $scheduledPublisher.Contains('count-local-post-words.ps1') -and
    $scheduledPublisher.Contains('web-deploy\publish-word-counts.ps1')) 'The scheduled task must run the canonical recount and broker publisher.'
Assert-Condition ($scheduledPublisher.IndexOf('& $localCountScript') -lt
    $scheduledPublisher.IndexOf('& $countScript')) 'Local IC/OOC inputs must fail closed before the wiki crawl starts.'
Assert-Condition $scheduledPublisher.Contains('TicksPerMillisecond') 'The publisher observation time must match the signed broker timestamp precision.'
Assert-Condition ($scheduledPublisher.IndexOf('count-obsidian-publish-words.ps1') -lt
    $scheduledPublisher.IndexOf('web-deploy\publish-word-counts.ps1')) 'The recount must complete before publication starts.'
Assert-Condition ($scheduledPublisher.Contains("[int]`$summary.FailedPages -ne 0") -and
    $scheduledPublisher.Contains("[int]`$summary.SuccessfulPages -ne [int]`$summary.SitemapPages") -and
    $scheduledPublisher.Contains("[int]`$summary.LocalIcFiles -lt 1") -and
    $scheduledPublisher.Contains("[int]`$summary.LocalOocFiles -lt 1")) 'The scheduled recount must fail closed on incomplete wiki or local-post inputs.'
Assert-Condition ($brokerPublisher.Contains("[int]`$brokerResponse.wiki.pages -ne `$WikiPages") -and
    $brokerPublisher.Contains("[int]`$brokerResponse.ic.files -ne `$IcFiles") -and
    $brokerPublisher.Contains("[int]`$brokerResponse.ooc.files -ne `$OocFiles")) 'Authenticated broker verification must validate all published page and file counts.'
Assert-Condition ($brokerPublisher.Contains("System32\OpenSSH\ssh.exe") -and
    $brokerPublisher.Contains("System32\OpenSSH\scp.exe") -and
    $brokerPublisher.Contains("dh_4gg2za@pdx1-shared-a1-13.dreamhost.com")) 'DreamHost publication must use native Windows OpenSSH with an explicit target.'
Assert-Condition ($installer.Contains("& `$publisherPath -InstallScheduledTask -TaskName `$TaskName") -and
    $installer.Contains("'Central Standard Time'") -and
    $installer.Contains("EndsWith('pwsh.exe'") -and
    $installer.Contains('StartWhenAvailable') -and
    $installer.Contains("[TimeSpan]::FromHours(4)") -and
    $installer.Contains("[TimeSpan]::FromHours(8)") -and
    $installer.Contains("[TimeSpan]::FromHours(17)") -and
    $installer.Contains('DaysOfWeek') -and
    $installer.Contains("[TimeSpan]::FromMinutes(30)")) 'The portable installer must create and verify the Central-time task contract.'

Write-Output 'Full word-count schedule policy verified.'
