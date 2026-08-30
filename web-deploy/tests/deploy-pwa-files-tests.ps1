$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '..\deploy-pwa-files.ps1'
$scriptText = Get-Content -Raw -LiteralPath $scriptPath
$deployWorkflow = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '..\..\.github\workflows\deploy-pwa.yml')
$campaignDeployWorkflow = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '..\..\.github\workflows\pwa-campaign-word-count-deploy.yml')
if ($deployWorkflow -notmatch '(?m)^concurrency:\s*$' -or $deployWorkflow -notmatch 'group:\s*pwa-release-transactions' -or $deployWorkflow -notmatch 'cancel-in-progress:\s*false') { throw 'Full PWA deployment is missing the shared non-cancelling concurrency group.' }
if ($campaignDeployWorkflow -notmatch '(?m)^concurrency:\s*$' -or $campaignDeployWorkflow -notmatch 'group:\s*pwa-release-transactions' -or $campaignDeployWorkflow -notmatch 'cancel-in-progress:\s*false') { throw 'Campaign deployment is missing the shared non-cancelling concurrency group.' }
if ($scriptText -notmatch '\$remoteLock = "\$RemoteDirectory/\.pwa-release-lock"') { throw 'Host-side PWA release lock is missing.' }
if ($scriptText -notmatch 'mkdir ''\$remoteLock''') { throw 'Host-side PWA release lock acquisition is missing.' }
if ($scriptText -notmatch 'rmdir ''\$remoteLock''') { throw 'Host-side PWA release lock release is missing.' }
$match = [regex]::Match($scriptText, "(?s)\$controller = @'\r?\n(?<php>.*?)\r?\n'@\.Replace")
if (-not $match.Success) { throw 'Could not extract the deployment controller template.' }
$controllerTemplate = $match.Groups['php'].Value
$root = Join-Path ([IO.Path]::GetTempPath()) ('pwa-deploy-test-' + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $root 'stage'
$directory = Join-Path $root 'live'
$state = Join-Path $stage '.transaction.json'
New-Item -ItemType Directory -Path $stage, $directory | Out-Null
try {
    $file = 'campaign-search.json'
    $source = Join-Path $stage $file
    $target = Join-Path $directory $file
    [IO.File]::WriteAllText($source, '{"version":2,"entries":["new"]}')
    [IO.File]::WriteAllText($target, '{"version":1,"entries":["old"]}')
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant()
    $manifest = @{ directory = $directory; stage = $stage; state = $state; release_id = 'test-release-1'; files = @($file); hashes = @{ $file = $hash } } | ConvertTo-Json -Compress
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($manifest))
    $controller = $controllerTemplate.Replace('__MANIFEST__', $encoded)
    $controllerPath = Join-Path $root 'install.php'
    [IO.File]::WriteAllText($controllerPath, $controller, [Text.UTF8Encoding]::new($false))

    & php -l $controllerPath
    if ($LASTEXITCODE -ne 0) { throw 'Generated deployment controller did not parse.' }
    & php $controllerPath install | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Initial promotion failed.' }
    if ((Get-Content -Raw $target) -ne '{"version":2,"entries":["new"]}') { throw 'Promotion did not publish the staged bytes.' }
    $firstState = Get-Content -Raw $state | ConvertFrom-Json
    if ($firstState.state -ne 'promoted') { throw 'Promotion did not persist promoted state.' }

    & php $controllerPath install | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Replay of a promoted install was not idempotent.' }
    if ((Get-Content -Raw $target) -ne '{"version":2,"entries":["new"]}') { throw 'Replay changed the promoted bytes.' }

    & php $controllerPath finalize | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Finalization failed.' }
    & php $controllerPath finalize | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Replay of finalization was not idempotent.' }
    $rollbackOutput = @(& cmd.exe /c "php `"$controllerPath`" rollback 2>&1")
    $rollbackExitCode = $LASTEXITCODE
    if ($rollbackExitCode -eq 0) { throw 'Rollback-after-finalization was incorrectly accepted.' }
    if (Test-Path -LiteralPath ($target + '.rollback-test-release-1')) { throw 'Finalization did not remove rollback evidence.' }
    Write-Output 'PWA deployment transaction tests passed.'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
