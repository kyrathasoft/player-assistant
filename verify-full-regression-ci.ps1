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

$workflowPath = Join-Path $RepoRoot '.github\workflows\hardening.yml'
$browserPackagePath = Join-Path $RepoRoot 'pwa\package.json'
$browserTestPath = Join-Path $RepoRoot 'pwa\browser-smoke.mjs'
$brokerOperationsPath = Join-Path $RepoRoot 'web-deploy\player-assistant-broker\BrokerOperations.php'
$operationsConfigExamplePath = Join-Path $RepoRoot 'web-deploy\player-assistant-broker\config.operations.example.php'
$wordCountDeploymentPath = Join-Path $RepoRoot 'web-deploy\deploy-word-count-refresh.ps1'

Assert-Condition -Condition (Test-Path -LiteralPath $workflowPath -PathType Leaf) -Message 'The full-regression workflow is missing.'

$workflow = Get-Content -Raw -LiteralPath $workflowPath
Assert-Condition -Condition ($workflow.Contains('name: Full regression')) -Message 'The workflow must expose the stable Full regression check name.'
Assert-Condition -Condition ($workflow.Contains('  full-regression:') -and $workflow.Contains('    name: Required full regression')) -Message 'The workflow must define the required full-regression job.'
Assert-Condition -Condition ($workflow.Contains('dotnet build .\ToOrcish\to-orcish.csproj --configuration Release --nologo')) -Message 'The required job must build the standalone ToOrcish executable used by the complete harness.'
Assert-Condition -Condition ($workflow.Contains('.\PlayerAssistant.Tests\bin\Release\net10.0-windows\PlayerAssistant.Tests.exe')) -Message 'The required job must run the complete desktop harness without a filter.'
Assert-Condition -Condition (!$workflow.Contains('Verify hosted settings fetch and decrypt path') -and !$workflow.Contains('Verify hosted settings negative paths')) -Message 'Focused desktop filters must not substitute for the complete harness.'
Assert-Condition -Condition ($workflow.Contains('.\pwa\verify-pwa.ps1')) -Message 'The required job must run the PWA verifier.'
Assert-Condition -Condition ($workflow.Contains("Get-ChildItem -LiteralPath .\web-deploy\tests -Filter '*-tests.php' -File") -and $workflow.Contains('ForEach-Object { php $_.FullName }')) -Message 'The required job must run all PHP broker test suites.'
$brokerOperations = Get-Content -Raw -LiteralPath $brokerOperationsPath
$operationsConfigExample = Get-Content -Raw -LiteralPath $operationsConfigExamplePath
$wordCountDeployment = Get-Content -Raw -LiteralPath $wordCountDeploymentPath
Assert-Condition -Condition (!$operationsConfigExample.Contains('getenv(')) -Message 'The example operations config must not evaluate FTPS secrets before deployment serializes config.php.'
Assert-Condition -Condition ($brokerOperations.Contains('BACKUP_FTPS_PASSWORD') -and $brokerOperations.Contains('BACKUP_FTPS_REMOTE_PATH')) -Message 'BrokerOperations must resolve FTPS secrets directly from the runtime environment.'
Assert-Condition -Condition (!$wordCountDeployment.Contains("copy(`$configPath, `$configPath . '.bak-deploy-'") -and $wordCountDeployment.Contains("`$config['operations']['offsite'] = [")) -Message 'Deployment must scrub FTPS credentials and avoid retaining config.php copies that could contain them.'
Assert-Condition -Condition ($wordCountDeployment.Contains("`$configBackupPatterns = [") -and $wordCountDeployment.Contains("'config.php.bak-deploy-*'") -and $wordCountDeployment.Contains("'config.php.bak-word-count-refresh-*'")) -Message 'Deployment must remove legacy config backups that may contain serialized FTPS credentials.'
Assert-Condition -Condition ($workflow.Contains('npm ci --prefix .\pwa') -and $workflow.Contains('npm --prefix .\pwa test')) -Message 'The required job must install and run the browser-level PWA smoke tests.'
Assert-Condition -Condition (Test-Path -LiteralPath $browserPackagePath -PathType Leaf) -Message 'The browser smoke package manifest is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath $browserTestPath -PathType Leaf) -Message 'The browser smoke test is missing.'

$package = Get-Content -Raw -LiteralPath $browserPackagePath | ConvertFrom-Json
Assert-Condition -Condition ([string]$package.scripts.test -eq 'node browser-smoke.mjs') -Message 'The PWA browser smoke test script is not pinned to browser-smoke.mjs.'
Assert-Condition -Condition (![string]::IsNullOrWhiteSpace([string]$package.devDependencies.playwright)) -Message 'The browser smoke test must declare Playwright explicitly.'

Write-Output 'Full regression CI policy verified.'
