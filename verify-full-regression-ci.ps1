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

function Assert-WorkflowRunCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkflowText,

        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $pattern = '(?m)^\s+run:\s*' + [regex]::Escape($Command) + '\s*$'
    Assert-Condition -Condition ([regex]::IsMatch($WorkflowText, $pattern)) -Message $Message
}

$workflowPath = Join-Path $RepoRoot '.github\workflows\hardening.yml'
$browserPackagePath = Join-Path $RepoRoot 'pwa\package.json'
$browserTestPath = Join-Path $RepoRoot 'pwa\browser-smoke.mjs'
$translatorWorkerTestPath = Join-Path $RepoRoot 'pwa\translator-worker-tests.mjs'
$httpAuthTestPath = Join-Path $RepoRoot 'web-deploy\tests\run-http-auth-tests.ps1'
$brokerOperationsPath = Join-Path $RepoRoot 'web-deploy\player-assistant-broker\BrokerOperations.php'
$operationsConfigExamplePath = Join-Path $RepoRoot 'web-deploy\player-assistant-broker\config.operations.example.php'
$wordCountDeploymentPath = Join-Path $RepoRoot 'web-deploy\deploy-word-count-refresh.ps1'
$directoryBuildPropsPath = Join-Path $RepoRoot 'Directory.Build.props'
$dotnetDependencyVerifierPath = Join-Path $RepoRoot 'verify-dotnet-dependencies.ps1'
$dependencyReviewWorkflowPath = Join-Path $RepoRoot '.github\workflows\dependency-review.yml'
$dependabotPath = Join-Path $RepoRoot '.github\dependabot.yml'
$hygieneVerifierPath = Join-Path $RepoRoot 'verify-repository-hygiene.ps1'
$lexiconVerifierPath = Join-Path $RepoRoot 'verify-lexicon-artifacts.py'
$versionVerifierPath = Join-Path $RepoRoot 'verify-version-metadata.py'
$requiredLockFiles = @(
    'packages.lock.json',
    'ToOrcish\packages.lock.json',
    'PlayerAssistant.Launcher\packages.lock.json',
    'PlayerAssistant.Tests\packages.lock.json'
)

Assert-Condition -Condition (Test-Path -LiteralPath $workflowPath -PathType Leaf) -Message 'The full-regression workflow is missing.'

$workflow = Get-Content -Raw -LiteralPath $workflowPath
$httpAuthTest = Get-Content -Raw -LiteralPath $httpAuthTestPath
Assert-Condition -Condition ($workflow.Contains('name: Full regression')) -Message 'The workflow must expose the stable Full regression check name.'
Assert-Condition -Condition ($workflow.Contains('  full-regression:') -and $workflow.Contains('    name: Required full regression')) -Message 'The workflow must define the required full-regression job.'
Assert-WorkflowRunCommand -WorkflowText $workflow -Command 'dotnet build .\player-assistant.csproj --configuration Release --nologo --no-restore' -Message 'The required job must build the desktop application without an implicit restore.'
Assert-WorkflowRunCommand -WorkflowText $workflow -Command 'dotnet build .\ToOrcish\to-orcish.csproj --configuration Release --nologo --no-restore' -Message 'The required job must build ToOrcish without an implicit restore.'
Assert-WorkflowRunCommand -WorkflowText $workflow -Command 'dotnet build .\PlayerAssistant.Tests\PlayerAssistant.Tests.csproj --configuration Release --nologo --no-restore -p:UseSharedCompilation=false' -Message 'The required job must build the test harness without an implicit restore.'
Assert-WorkflowRunCommand -WorkflowText $workflow -Command 'dotnet format .\player-assistant.slnx --verify-no-changes --no-restore' -Message 'The required job must reject unformatted .NET source without performing another restore.'
Assert-WorkflowRunCommand -WorkflowText $workflow -Command '.\verify-repository-hygiene.ps1' -Message 'The required job must verify local corpus and Hermes scratch-file hygiene.'
Assert-Condition -Condition (Test-Path -LiteralPath $hygieneVerifierPath -PathType Leaf) -Message 'The repository hygiene verifier is missing.'
Assert-Condition -Condition ($workflow.Contains('.\PlayerAssistant.Tests\bin\Release\net10.0-windows\PlayerAssistant.Tests.exe')) -Message 'The required job must run the complete desktop harness without a filter.'
Assert-Condition -Condition (!$workflow.Contains('Verify hosted settings fetch and decrypt path') -and !$workflow.Contains('Verify hosted settings negative paths')) -Message 'Focused desktop filters must not substitute for the complete harness.'
Assert-Condition -Condition ($workflow.Contains('.\pwa\verify-pwa.ps1')) -Message 'The required job must run the PWA verifier.'
Assert-Condition -Condition ($workflow.Contains('python .\verify-lexicon-artifacts.py')) -Message 'The required job must verify canonical lexicon projections.'
Assert-Condition -Condition (Test-Path -LiteralPath $lexiconVerifierPath -PathType Leaf) -Message 'The canonical lexicon verifier is missing.'
Assert-Condition -Condition ($workflow.Contains('Load canonical version metadata') -and $workflow.Contains('.\version-metadata.ps1')) -Message 'The required job must load canonical version metadata for release artifact paths.'
Assert-Condition -Condition ($workflow.Contains('python .\verify-version-metadata.py')) -Message 'The required job must verify canonical version projections.'
Assert-Condition -Condition (Test-Path -LiteralPath $versionVerifierPath -PathType Leaf) -Message 'The canonical version verifier is missing.'
Assert-Condition -Condition ($workflow.Contains("Get-ChildItem -LiteralPath .\web-deploy\tests -Filter '*-tests.php' -File") -and $workflow.Contains('ForEach-Object { php $_.FullName }')) -Message 'The required job must run all PHP broker test suites.'
$brokerOperations = Get-Content -Raw -LiteralPath $brokerOperationsPath
$operationsConfigExample = Get-Content -Raw -LiteralPath $operationsConfigExamplePath
$wordCountDeployment = Get-Content -Raw -LiteralPath $wordCountDeploymentPath
Assert-Condition -Condition (!$operationsConfigExample.Contains('getenv(')) -Message 'The example operations config must not evaluate FTPS secrets before deployment serializes config.php.'
Assert-Condition -Condition ($brokerOperations.Contains('BACKUP_FTPS_PASSWORD') -and $brokerOperations.Contains('BACKUP_FTPS_REMOTE_PATH')) -Message 'BrokerOperations must resolve FTPS secrets directly from the runtime environment.'
Assert-Condition -Condition (!$wordCountDeployment.Contains("copy(`$configPath, `$configPath . '.bak-deploy-'") -and $wordCountDeployment.Contains("`$config['operations']['offsite'] = [")) -Message 'Deployment must scrub FTPS credentials and avoid retaining config.php copies that could contain them.'
Assert-Condition -Condition ($wordCountDeployment.Contains("`$configBackupPatterns = [") -and $wordCountDeployment.Contains("'config.php.bak-deploy-*'") -and $wordCountDeployment.Contains("'config.php.bak-word-count-refresh-*'")) -Message 'Deployment must remove legacy config backups that may contain serialized FTPS credentials.'
Assert-Condition -Condition ($workflow.Contains('npm ci --prefix .\pwa') -and $workflow.Contains('npm --prefix .\pwa test')) -Message 'The required job must install and run the browser-level PWA smoke tests.'
Assert-Condition -Condition ([regex]::IsMatch($workflow, "- name: Build ephemeral release update artifacts for untrusted events[\x0D\x0A]+\s+if: github[.]event_name != 'push'[\x0D\x0A]")) -Message 'Pull-request and manually dispatched builds must use an explicit ephemeral update-manifest signing key step.'
Assert-Condition -Condition ([regex]::IsMatch($workflow, "- name: Build signed release update artifacts[\x0D\x0A]+\s+if: github[.]event_name == 'push'[\x0D\x0A]")) -Message 'The secret-bearing release signing step must run only for protected push events.'
$workflowLines = @($workflow -split "`n")
for ($lineIndex = 0; $lineIndex -lt $workflowLines.Count; $lineIndex++) {
    if ($workflowLines[$lineIndex] -notmatch '\$\{\{\s*secrets[.]') { continue }
    $stepStart = $lineIndex
    while ($stepStart -ge 0 -and $workflowLines[$stepStart] -notmatch '^\s{6}- name:') { $stepStart-- }
    Assert-Condition -Condition ($stepStart -ge 0) -Message "Signing or deployment secret found outside an individually guarded step at workflow line $($lineIndex + 1)."
    $stepEnd = $lineIndex + 1
    while ($stepEnd -lt $workflowLines.Count -and $workflowLines[$stepEnd] -notmatch '^\s{6}- name:') { $stepEnd++ }
    $stepBlock = $workflowLines[$stepStart..($stepEnd - 1)] -join "`n"
    Assert-Condition -Condition ($stepBlock -match "(?m)^\s+if: github[.]event_name == 'push'\s*$") -Message "A secret-bearing workflow step can run outside protected push events near line $($lineIndex + 1)."
}
Assert-Condition -Condition ($workflow.Contains('./web-deploy/tests/publish-word-counts-tests.ps1')) -Message 'The required workflow must run the PowerShell publication test suite.'
Assert-Condition -Condition ($workflow.Contains('./web-deploy/tests/run-http-auth-tests.ps1 -PhpPath (Get-Command php).Source')) -Message 'The required workflow must run the HTTP authentication integration suite with the setup PHP executable.'
Assert-Condition -Condition ($httpAuthTest.Contains("FullName -eq 'System.Net.Http.HttpResponseMessage'") -and $httpAuthTest.Contains('ReadAsStringAsync()')) -Message 'The HTTP authentication suite must inspect expected error responses under PowerShell 7 HttpClient without breaking Windows PowerShell.'
Assert-Condition -Condition ($workflow.Contains('./web-deploy/tests/backup-encryption-tests.ps1')) -Message 'The required workflow must run the broker backup encryption suite.'
Assert-Condition -Condition (Test-Path -LiteralPath $browserPackagePath -PathType Leaf) -Message 'The browser smoke package manifest is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath $browserTestPath -PathType Leaf) -Message 'The browser smoke test is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath $translatorWorkerTestPath -PathType Leaf) -Message 'The translator worker runtime test is missing.'

$package = Get-Content -Raw -LiteralPath $browserPackagePath | ConvertFrom-Json
Assert-Condition -Condition ([string]$package.scripts.test -eq 'node translator-worker-tests.mjs && node browser-smoke.mjs') -Message 'The PWA test script must run translator-worker runtime and browser smoke coverage.'
Assert-Condition -Condition (![string]::IsNullOrWhiteSpace([string]$package.devDependencies.playwright)) -Message 'The browser smoke test must declare Playwright explicitly.'

Assert-Condition -Condition (Test-Path -LiteralPath $directoryBuildPropsPath -PathType Leaf) -Message 'Directory.Build.props is missing.'
$directoryBuildProps = Get-Content -Raw -LiteralPath $directoryBuildPropsPath
Assert-Condition -Condition ($directoryBuildProps.Contains('<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>')) -Message 'All .NET projects must generate NuGet lock files.'
Assert-Condition -Condition (Test-Path -LiteralPath $dotnetDependencyVerifierPath -PathType Leaf) -Message 'The .NET locked-restore and vulnerability verifier is missing.'
Assert-Condition -Condition ($workflow.Contains('.\verify-dotnet-dependencies.ps1')) -Message 'The required job must run locked restores and transitive vulnerability scans.'
$dotnetDependencyVerifier = Get-Content -Raw -LiteralPath $dotnetDependencyVerifierPath
Assert-Condition -Condition ($dotnetDependencyVerifier.Contains('package --vulnerable --include-transitive --format json --no-restore')) -Message 'Vulnerability scans must not perform an unlocked implicit restore after locked restore verification.'
Assert-Condition -Condition ($workflow.Contains("hashFiles('**/packages.lock.json')")) -Message 'The NuGet cache must be keyed from lock files.'
foreach ($relativeLockFile in $requiredLockFiles) {
    Assert-Condition -Condition (Test-Path -LiteralPath (Join-Path $RepoRoot $relativeLockFile) -PathType Leaf) -Message "Required NuGet lock file is missing: $relativeLockFile"
}

Assert-Condition -Condition (Test-Path -LiteralPath $dependencyReviewWorkflowPath -PathType Leaf) -Message 'The dependency-review workflow is missing.'
$dependencyReviewWorkflow = Get-Content -Raw -LiteralPath $dependencyReviewWorkflowPath
Assert-Condition -Condition ($dependencyReviewWorkflow.Contains('actions/dependency-review-action@a1d282b36b6f3519aa1f3fc636f609c47dddb294')) -Message 'Dependency review must use the pinned v5.0.0 commit.'
Assert-Condition -Condition ($dependencyReviewWorkflow.Contains('fail-on-severity: moderate')) -Message 'Dependency review must reject moderate-or-higher vulnerabilities.'

Assert-Condition -Condition (Test-Path -LiteralPath $dependabotPath -PathType Leaf) -Message 'Dependabot configuration is missing.'
$dependabot = Get-Content -Raw -LiteralPath $dependabotPath
foreach ($ecosystem in @('nuget', 'npm', 'github-actions')) {
    Assert-Condition -Condition ($dependabot.Contains("package-ecosystem: '$ecosystem'")) -Message "Dependabot must monitor the $ecosystem ecosystem."
}

Write-Output 'Full regression CI policy verified.'
