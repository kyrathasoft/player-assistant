$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\pwa-deployment.ps1')

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)][object]$Expected,
        [Parameter(Mandatory = $true)][object]$Actual,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (($Expected -join ',') -ne ($Actual -join ',')) {
        throw "$Message Expected '$($Expected -join ',')', got '$($Actual -join ',')'."
    }
}

$events = [Collections.Generic.List[string]]::new()
$errorMessage = $null
try {
    Invoke-PwaDeploymentTransaction `
        -InstallRelease { $events.Add('install') } `
        -VerifyPublic { $events.Add('verify'); throw 'public verification failed' } `
        -CommitRelease { $events.Add('commit') } `
        -RollbackRelease { $events.Add('rollback') }
}
catch {
    $errorMessage = $_.Exception.Message
}

Assert-Equal @('install', 'verify', 'rollback') $events.ToArray() 'Verification failure did not roll back the installed release.'
Assert-Equal 'public verification failed' $errorMessage 'Verification failure was not preserved.'

$rollbackFailure = $null
try {
    Invoke-PwaDeploymentTransaction `
        -InstallRelease { } `
        -VerifyPublic { throw 'verification failed' } `
        -CommitRelease { } `
        -RollbackRelease { throw 'rollback failed' }
}
catch {
    $rollbackFailure = $_.Exception.Message
}
if (!$rollbackFailure.Contains('verification failed') -or !$rollbackFailure.Contains('rollback failed')) {
    throw 'Combined deployment and rollback failure did not preserve both errors.'
}

$events.Clear()
Invoke-PwaDeploymentTransaction `
    -InstallRelease { $events.Add('install') } `
    -VerifyPublic { $events.Add('verify') } `
    -CommitRelease { $events.Add('commit') } `
    -RollbackRelease { $events.Add('rollback') }
Assert-Equal @('install', 'verify', 'commit') $events.ToArray() 'Successful deployment transaction order was incorrect.'

$deployScript = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '..\deploy-pwa-files.ps1')
if (!$deployScript.Contains('Invoke-PwaDeploymentTransaction')) {
    throw 'PWA deployment does not use the verified transaction boundary.'
}
if (!$deployScript.Contains("'..\pwa\test-deployment.ps1'") -or !$deployScript.Contains('-Files $Files')) {
    throw 'PWA deployment does not verify deployed HTTPS hashes, headers, and API behavior.'
}

$installerPath = Join-Path $PSScriptRoot '..\pwa-release-installer.php'
$installerSource = Get-Content -Raw -LiteralPath $installerPath
if (!$installerSource.Contains('symlink($releasePath, $temporaryLink)')) {
    throw 'PWA activation does not use an atomic release symlink switch.'
}
if (!$installerSource.Contains('RENAME_EXCHANGE')) {
    throw 'The initial PWA directory migration is not activated atomically.'
}
if (!$installerSource.Contains('scheduleRollbackWatchdog')) {
    throw 'An interrupted SSH session can leave an unverified PWA release active.'
}
$phpOsFamily = (& php -r 'echo PHP_OS_FAMILY;').Trim()
if ($phpOsFamily -eq 'Windows') {
    Write-Output 'PWA release filesystem integration skipped because Windows PHP cannot create unprivileged symlinks.'
    Write-Output 'PWA deployment transaction tests passed.'
    return
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "player-assistant-pwa-deployment-test-$([Guid]::NewGuid().ToString('N'))"
$targetDirectory = Join-Path $fixtureRoot 'target'
$releaseRoot = Join-Path $fixtureRoot 'releases'
$releaseId = '0123456789abcdef0123456789abcdef'
$stageDirectory = Join-Path $releaseRoot "release-$releaseId"

try {
    New-Item -ItemType Directory -Path $targetDirectory, $stageDirectory | Out-Null
    [IO.File]::WriteAllText((Join-Path $targetDirectory 'existing.txt'), 'previous')
    [IO.File]::WriteAllText((Join-Path $stageDirectory 'existing.txt'), 'replacement')
    [IO.File]::WriteAllText((Join-Path $stageDirectory 'introduced.txt'), 'introduced')
    [IO.File]::WriteAllText((Join-Path $stageDirectory '.htaccess'), 'headers')

    $files = @('existing.txt', 'introduced.txt', '.htaccess')
    $hashes = @{}
    foreach ($file in $files) {
        $hashes[$file] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $stageDirectory $file)).Hash.ToLowerInvariant()
    }
    $manifest = @{
        directory = $targetDirectory.Replace('\', '/')
        release_root = $releaseRoot.Replace('\', '/')
        stage = $stageDirectory.Replace('\', '/')
        release_id = $releaseId
        watchdog_seconds = 2
        files = $files
        hashes = $hashes
    } | ConvertTo-Json -Compress
    $manifest64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($manifest))

    & php $installerPath install $manifest64
    if ($LASTEXITCODE -ne 0) {
        throw 'Fixture release installation failed.'
    }
    & php -r 'exit(is_link($argv[1]) ? 0 : 1);' -- $targetDirectory
    if ($LASTEXITCODE -ne 0) {
        throw 'PWA activation did not atomically switch a release symlink.'
    }
    Assert-Equal 'replacement' (Get-Content -Raw -LiteralPath (Join-Path $targetDirectory 'existing.txt')) 'Existing file was not replaced.'
    Assert-Equal 'introduced' (Get-Content -Raw -LiteralPath (Join-Path $targetDirectory 'introduced.txt')) 'New file was not installed.'

    & php $installerPath rollback $manifest64
    if ($LASTEXITCODE -ne 0) {
        throw 'Fixture release rollback failed.'
    }
    Assert-Equal 'previous' (Get-Content -Raw -LiteralPath (Join-Path $targetDirectory 'existing.txt')) 'Existing file was not restored.'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $targetDirectory 'introduced.txt')) 'Rollback left a newly introduced file behind.'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $targetDirectory '.htaccess')) 'Rollback left a newly introduced .htaccess file behind.'

    New-Item -ItemType Directory -Path $stageDirectory | Out-Null
    [IO.File]::WriteAllText((Join-Path $stageDirectory 'existing.txt'), 'replacement')
    [IO.File]::WriteAllText((Join-Path $stageDirectory 'introduced.txt'), 'introduced')
    [IO.File]::WriteAllText((Join-Path $stageDirectory '.htaccess'), 'headers')
    & php $installerPath install $manifest64
    if ($LASTEXITCODE -ne 0) {
        throw 'Fixture release reinstallation failed.'
    }
    & php $installerPath commit $manifest64
    if ($LASTEXITCODE -ne 0) {
        throw 'Fixture release commit failed.'
    }
    Assert-Equal 'replacement' (Get-Content -Raw -LiteralPath (Join-Path $targetDirectory 'existing.txt')) 'Committed file content was incorrect.'
    Assert-Equal 'introduced' (Get-Content -Raw -LiteralPath (Join-Path $targetDirectory 'introduced.txt')) 'Committed new file was missing.'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $releaseRoot "previous-$releaseId")) 'Commit left the prior release behind.'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $releaseRoot 'transaction.json')) 'Commit left transaction state behind.'
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'PWA deployment transaction tests passed.'
