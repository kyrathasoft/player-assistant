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
$hygieneVerifierPath = Join-Path $RepoRoot 'verify-repository-hygiene.ps1'
$lexiconVerifierPath = Join-Path $RepoRoot 'verify-lexicon-artifacts.py'

Assert-Condition -Condition (Test-Path -LiteralPath $workflowPath -PathType Leaf) -Message 'The full-regression workflow is missing.'

$workflow = Get-Content -Raw -LiteralPath $workflowPath
Assert-Condition -Condition ($workflow.Contains('name: Full regression')) -Message 'The workflow must expose the stable Full regression check name.'
Assert-Condition -Condition ($workflow.Contains('  full-regression:') -and $workflow.Contains('    name: Required full regression')) -Message 'The workflow must define the required full-regression job.'
Assert-Condition -Condition ($workflow.Contains('dotnet build .\ToOrcish\to-orcish.csproj --configuration Release --nologo')) -Message 'The required job must build the standalone ToOrcish executable used by the complete harness.'
Assert-Condition -Condition ($workflow.Contains('dotnet format .\player-assistant.slnx --verify-no-changes')) -Message 'The required job must reject unformatted .NET source files.'
Assert-Condition -Condition ($workflow.Contains('.\verify-repository-hygiene.ps1')) -Message 'The required job must verify local corpus and Hermes scratch-file hygiene.'
Assert-Condition -Condition (Test-Path -LiteralPath $hygieneVerifierPath -PathType Leaf) -Message 'The repository hygiene verifier is missing.'
Assert-Condition -Condition ($workflow.Contains('.\PlayerAssistant.Tests\bin\Release\net10.0-windows\PlayerAssistant.Tests.exe')) -Message 'The required job must run the complete desktop harness without a filter.'
Assert-Condition -Condition (!$workflow.Contains('Verify hosted settings fetch and decrypt path') -and !$workflow.Contains('Verify hosted settings negative paths')) -Message 'Focused desktop filters must not substitute for the complete harness.'
Assert-Condition -Condition ($workflow.Contains('.\pwa\verify-pwa.ps1')) -Message 'The required job must run the PWA verifier.'
Assert-Condition -Condition ($workflow.Contains('python .\verify-lexicon-artifacts.py')) -Message 'The required job must verify canonical lexicon projections.'
Assert-Condition -Condition (Test-Path -LiteralPath $lexiconVerifierPath -PathType Leaf) -Message 'The canonical lexicon verifier is missing.'
Assert-Condition -Condition ($workflow.Contains('.\web-deploy\tests\deploy-pwa-files-tests.ps1')) -Message 'The required job must run PWA deployment transaction tests.'
Assert-Condition -Condition ($workflow.Contains('pwa-deployment-linux:')) -Message 'The workflow must exercise atomic PWA symlink activation on Linux.'
Assert-Condition -Condition ($workflow.Contains('needs: pwa-deployment-linux')) -Message 'The required full regression job must depend on Linux PWA deployment tests.'
Assert-Condition -Condition ($workflow.Contains('if: ${{ always() }}') -and $workflow.Contains("needs.pwa-deployment-linux.result != 'success'")) -Message 'The required check must fail, not skip successfully, when Linux PWA deployment tests fail.'
Assert-Condition -Condition ($workflow.Contains('web-deploy/tests/character-auth-tests.php')) -Message 'The required job must run character authentication tests.'
Assert-Condition -Condition ($workflow.Contains('web-deploy/tests/xp-tracking-tests.php')) -Message 'The required job must run XP tracking tests.'
Assert-Condition -Condition ($workflow.Contains('web-deploy/tests/broker-auth-routing-tests.php')) -Message 'The required job must run broker routing tests.'
Assert-Condition -Condition ($workflow.Contains('npm ci --prefix .\pwa') -and $workflow.Contains('npm --prefix .\pwa test')) -Message 'The required job must install and run the browser-level PWA smoke tests.'
Assert-Condition -Condition (Test-Path -LiteralPath $browserPackagePath -PathType Leaf) -Message 'The browser smoke package manifest is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath $browserTestPath -PathType Leaf) -Message 'The browser smoke test is missing.'

$package = Get-Content -Raw -LiteralPath $browserPackagePath | ConvertFrom-Json
Assert-Condition -Condition ([string]$package.scripts.test -eq 'node browser-smoke.mjs') -Message 'The PWA browser smoke test script is not pinned to browser-smoke.mjs.'
Assert-Condition -Condition (![string]::IsNullOrWhiteSpace([string]$package.devDependencies.playwright)) -Message 'The browser smoke test must declare Playwright explicitly.'

Write-Output 'Full regression CI policy verified.'
