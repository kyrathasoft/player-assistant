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
$deployWorkflowPath = Join-Path $RepoRoot '.github\workflows\deploy-pwa.yml'
$browserPackagePath = Join-Path $RepoRoot 'pwa\package.json'
$browserTestPath = Join-Path $RepoRoot 'pwa\browser-smoke.mjs'
$translatorWorkerTestPath = Join-Path $RepoRoot 'pwa\translator-worker-tests.mjs'
$serviceWorkerTestPath = Join-Path $RepoRoot 'pwa\service-worker-tests.mjs'
$httpAuthTestPath = Join-Path $RepoRoot 'web-deploy/tests/run-http-auth-tests.ps1'
$webTranslatorBoundaryTestPath = Join-Path $RepoRoot 'web-deploy/tests/run-web-translator-boundary.ps1'
$nativeFailFastVerifierPath = Join-Path $RepoRoot 'verify-native-test-fail-fast.ps1'
$brokerOperationsPath = Join-Path $RepoRoot 'web-deploy\player-assistant-broker\BrokerOperations.php'
$boundedRepairPath = Join-Path $RepoRoot 'web-deploy\player-assistant-broker\BoundedRepairService.php'
$boundedRepairTestPath = Join-Path $RepoRoot 'web-deploy\tests\bounded-repair-tests.php'
$operationsConfigExamplePath = Join-Path $RepoRoot 'web-deploy\player-assistant-broker\config.operations.example.php'
$wordCountDeploymentPath = Join-Path $RepoRoot 'web-deploy\deploy-word-count-refresh.ps1'
$directoryBuildPropsPath = Join-Path $RepoRoot 'Directory.Build.props'
$dotnetDependencyVerifierPath = Join-Path $RepoRoot 'verify-dotnet-dependencies.ps1'
$globalJsonPath = Join-Path $RepoRoot 'global.json'
$launcherLockPath = Join-Path $RepoRoot 'PlayerAssistant.Launcher\packages.lock.json'
$launcherProjectPath = Join-Path $RepoRoot 'PlayerAssistant.Launcher\PlayerAssistant.Launcher.csproj'
$dependencyReviewWorkflowPath = Join-Path $RepoRoot '.github\workflows\dependency-review.yml'
$dependabotPath = Join-Path $RepoRoot '.github\dependabot.yml'
$hygieneVerifierPath = Join-Path $RepoRoot 'verify-repository-hygiene.ps1'
$secretLifecycleVerifierPath = Join-Path $RepoRoot 'verify-secret-lifecycle.ps1'
$lexiconVerifierPath = Join-Path $RepoRoot 'verify-lexicon-artifacts.py'
$versionVerifierPath = Join-Path $RepoRoot 'verify-version-metadata.py'
$compatibilityVerifierPath = Join-Path $RepoRoot 'verify-downgrade-rollback-compatibility.ps1'
$releaseReadinessAggregatorPath = Join-Path $RepoRoot 'release-readiness-aggregate.ps1'
$releaseReadinessTestsPath = Join-Path $RepoRoot 'release-readiness-tests.ps1'
$requiredLockFiles = @(
    'packages.lock.json',
    'ToOrcish\packages.lock.json',
    'PlayerAssistant.Launcher\packages.lock.json',
    'PlayerAssistant.Tests\packages.lock.json'
)

Assert-Condition -Condition (Test-Path -LiteralPath $workflowPath -PathType Leaf) -Message 'The full-regression workflow is missing.'

$workflow = Get-Content -Raw -LiteralPath $workflowPath
$browserSmoke = Get-Content -Raw -LiteralPath $browserTestPath
$deployWorkflow = Get-Content -Raw -LiteralPath $deployWorkflowPath
$httpAuthTest = Get-Content -Raw -LiteralPath $httpAuthTestPath
$nativeFailFastVerifier = Get-Content -Raw -LiteralPath $nativeFailFastVerifierPath
$launcherProject = Get-Content -Raw -LiteralPath $launcherProjectPath
Assert-Condition -Condition ($workflow.Contains('name: Full regression')) -Message 'The workflow must expose the stable Full regression check name.'
Assert-Condition -Condition ($workflow.Contains('  full-regression:') -and $workflow.Contains('    name: Required full regression')) -Message 'The workflow must define the required full-regression job.'
Assert-WorkflowRunCommand -WorkflowText $workflow -Command 'dotnet build .\player-assistant.csproj --configuration Release --nologo --no-restore' -Message 'The required job must build the desktop application without an implicit restore.'
Assert-WorkflowRunCommand -WorkflowText $workflow -Command 'dotnet build .\ToOrcish\to-orcish.csproj --configuration Release --nologo --no-restore' -Message 'The required job must build ToOrcish without an implicit restore.'
Assert-WorkflowRunCommand -WorkflowText $workflow -Command 'dotnet build .\PlayerAssistant.Tests\PlayerAssistant.Tests.csproj --configuration Release --nologo --no-restore -p:UseSharedCompilation=false' -Message 'The required job must build the test harness without an implicit restore.'
Assert-WorkflowRunCommand -WorkflowText $workflow -Command 'dotnet format .\player-assistant.slnx --verify-no-changes --no-restore' -Message 'The required job must reject unformatted .NET source without performing another restore.'
Assert-WorkflowRunCommand -WorkflowText $workflow -Command '.\verify-repository-hygiene.ps1' -Message 'The required job must verify local corpus and Hermes scratch-file hygiene.'
Assert-Condition -Condition (Test-Path -LiteralPath $hygieneVerifierPath -PathType Leaf) -Message 'The repository hygiene verifier is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath $secretLifecycleVerifierPath -PathType Leaf) -Message 'The secret lifecycle verifier is missing.'
Assert-Condition -Condition ($workflow.Contains('.\verify-secret-lifecycle.ps1')) -Message 'The required job must verify secret lifecycle inventory and revocation.'
Assert-Condition -Condition ($workflow.Contains('.\PlayerAssistant.Tests\bin\Release\net10.0-windows\PlayerAssistant.Tests.exe')) -Message 'The required job must run the complete desktop harness without a filter.'
Assert-Condition -Condition (!$workflow.Contains('Verify hosted settings fetch and decrypt path') -and !$workflow.Contains('Verify hosted settings negative paths')) -Message 'Focused desktop filters must not substitute for the complete harness.'
Assert-Condition -Condition ($workflow.Contains('.\pwa\verify-pwa.ps1')) -Message 'The required job must run the PWA verifier.'
Assert-Condition -Condition ($workflow.Contains('migration-rehearsal-tests.php')) -Message 'The required CI paths must run deterministic broker migration rehearsal coverage.'
Assert-Condition -Condition (Test-Path -LiteralPath $boundedRepairPath -PathType Leaf) -Message 'Bounded repair tooling is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath $boundedRepairTestPath -PathType Leaf) -Message 'Bounded repair deterministic coverage is missing.'
Assert-Condition -Condition ($workflow.Contains('bounded-repair-tests.php')) -Message 'Canonical CI must run bounded repair coverage.'
Assert-Condition -Condition ($deployWorkflow.Contains('pwa-release-transactions') -and $deployWorkflow.Contains('cancel-in-progress: false')) -Message 'Release workflow must retain serialized PWA release transactions.'
Assert-Condition -Condition (Test-Path -LiteralPath (Join-Path $RepoRoot 'web-deploy\tests\migration-rehearsal-tests.php') -PathType Leaf) -Message 'The deterministic broker migration rehearsal suite is missing.'
Assert-Condition -Condition ($workflow.Contains('python .\verify-lexicon-artifacts.py')) -Message 'The required job must verify canonical lexicon projections.'
Assert-Condition -Condition (Test-Path -LiteralPath $lexiconVerifierPath -PathType Leaf) -Message 'The canonical lexicon verifier is missing.'
Assert-Condition -Condition ($workflow.Contains('.\release-manifest-tests.ps1') -and $workflow.Contains('.\release-manifest.ps1 -Mode Generate') -and $workflow.Contains('.\release-manifest.ps1 -Mode Verify')) -Message 'The required job must run deterministic release-manifest tests and generate/verify the complete release inventory.'
Assert-Condition -Condition (Test-Path -LiteralPath $releaseReadinessAggregatorPath -PathType Leaf) -Message 'The release-readiness evidence aggregator is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath $releaseReadinessTestsPath -PathType Leaf) -Message 'The release-readiness evidence fixtures are missing.'
Assert-Condition -Condition ($workflow.Contains('release-readiness-tests.ps1')) -Message 'The required job must run deterministic release-readiness evidence fixtures.'
Assert-Condition -Condition (Test-Path -LiteralPath (Join-Path $RepoRoot 'release-manifest.inventory.json') -PathType Leaf) -Message 'The canonical release-manifest inventory is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath (Join-Path $RepoRoot 'release-manifest.ps1') -PathType Leaf) -Message 'The canonical release-manifest generator/verifier is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath (Join-Path $RepoRoot 'release-manifest-tests.ps1') -PathType Leaf) -Message 'The deterministic release-manifest regression suite is missing.'
Assert-Condition -Condition ($workflow.Contains('Load canonical version metadata') -and $workflow.Contains('.\version-metadata.ps1')) -Message 'The required job must load canonical version metadata for release artifact paths.'
Assert-Condition -Condition ($workflow.Contains('python .\verify-version-metadata.py')) -Message 'The required job must verify canonical version projections.'
Assert-Condition -Condition (Test-Path -LiteralPath $versionVerifierPath -PathType Leaf) -Message 'The canonical version verifier is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath $compatibilityVerifierPath -PathType Leaf) -Message 'The downgrade and rollback compatibility verifier is missing.'
Assert-Condition -Condition ($workflow.Contains('.\verify-downgrade-rollback-compatibility.ps1') -and (Test-Path -LiteralPath (Join-Path $RepoRoot 'compatibility-boundaries.json') -PathType Leaf)) -Message 'The required job must run downgrade and rollback compatibility coverage.'
Assert-Condition -Condition ($workflow.Contains("Get-ChildItem -LiteralPath .\web-deploy\tests -Filter '*-tests.php' -File") -and $workflow.Contains('ForEach-Object {')) -Message 'The required job must run all PHP broker test suites.'
Assert-Condition -Condition ($workflow.Contains('throw "PHP suite ''$($suite.Name)'' failed with exit code $exitCode."')) -Message 'Each PHP suite must fail the workflow immediately and identify the failing suite.'
Assert-Condition -Condition ($workflow.Contains('throw "Verification ''$Name'' failed with exit code $exitCode."')) -Message 'Sequential PowerShell/native verification commands must fail immediately and identify the failing suite.'
Assert-Condition -Condition (Test-Path -LiteralPath $nativeFailFastVerifierPath -PathType Leaf) -Message 'The native test fail-fast policy self-test is missing.'
Assert-Condition -Condition ($workflow.Contains('.\verify-native-test-fail-fast.ps1')) -Message 'The required job must execute the native test fail-fast policy self-test.'
Assert-Condition -Condition (Test-Path -LiteralPath $webTranslatorBoundaryTestPath -PathType Leaf) -Message 'The web translator HTTP boundary test is missing.'
Assert-Condition -Condition ($workflow.Contains("Invoke-CheckedVerification 'web translator HTTP boundary' './web-deploy/tests/run-web-translator-boundary.ps1'")) -Message 'The required workflow must run the web translator HTTP boundary test.'
Assert-Condition -Condition ($nativeFailFastVerifier.Contains('$global:LASTEXITCODE = 0')) -Message 'The native fail-fast self-test must clear its intentional native failure before returning to the GitHub Actions host.'
Assert-Condition -Condition ($launcherProject.Contains('<PublishSingleFile>true</PublishSingleFile>') -and $launcherProject.Contains('<EnableSingleFileAnalyzer>false</EnableSingleFileAnalyzer>')) -Message 'The launcher must remain single-file while disabling the SDK-patch-specific single-file analyzer dependency.'
$brokerOperations = Get-Content -Raw -LiteralPath $brokerOperationsPath
$operationsConfigExample = Get-Content -Raw -LiteralPath $operationsConfigExamplePath
$wordCountDeployment = Get-Content -Raw -LiteralPath $wordCountDeploymentPath
Assert-Condition -Condition (!$operationsConfigExample.Contains('getenv(')) -Message 'The example operations config must not evaluate FTPS secrets before deployment serializes config.php.'
Assert-Condition -Condition ($brokerOperations.Contains('BACKUP_FTPS_PASSWORD') -and $brokerOperations.Contains('BACKUP_FTPS_REMOTE_PATH')) -Message 'BrokerOperations must resolve FTPS secrets directly from the runtime environment.'
Assert-Condition -Condition (!$wordCountDeployment.Contains("copy(`$configPath, `$configPath . '.bak-deploy-'") -and $wordCountDeployment.Contains("`$config['operations']['offsite'] = [")) -Message 'Deployment must scrub FTPS credentials and avoid retaining config.php copies that could contain them.'
Assert-Condition -Condition ($wordCountDeployment.Contains("`$configBackupPatterns = [") -and $wordCountDeployment.Contains("'config.php.bak-deploy-*'") -and $wordCountDeployment.Contains("'config.php.bak-word-count-refresh-*'")) -Message 'Deployment must remove legacy config backups that may contain serialized FTPS credentials.'
Assert-Condition -Condition ($workflow.Contains('npm ci --prefix .\pwa') -and $workflow.Contains('npm --prefix .\pwa test')) -Message 'The required job must install and run the browser-level PWA smoke tests.'
Assert-Condition -Condition ($browserSmoke.Contains('const assertAuthenticatedAccessibility = async') -and $browserSmoke.Contains('protectedState: true') -and $browserSmoke.Contains('mobile: true') -and $browserSmoke.Contains('Failed-login error announcement contract failed')) -Message 'The browser smoke gate must enforce authenticated/error accessibility acceptance.'
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
Assert-Condition -Condition ($workflow.Contains('[hashtable]$Parameters = @{}') -and
    $workflow.Contains('& $FilePath @Parameters') -and
    $workflow.Contains("Invoke-CheckedVerification 'HTTP authentication' './web-deploy/tests/run-http-auth-tests.ps1' -Parameters @{ PhpPath = (Get-Command php).Source }")) -Message 'The required workflow must pass the setup PHP executable to the HTTP authentication suite through named PowerShell parameters.'
Assert-Condition -Condition ($httpAuthTest.Contains("FullName -eq 'System.Net.Http.HttpResponseMessage'") -and $httpAuthTest.Contains('$_.ErrorDetails.Message') -and $httpAuthTest.Contains('ReadAsStringAsync()')) -Message 'The HTTP authentication suite must inspect disposed error responses under PowerShell 7 without breaking Windows PowerShell.'
Assert-Condition -Condition ($workflow.Contains('./web-deploy/tests/backup-encryption-tests.ps1')) -Message 'The required workflow must run the broker backup encryption suite.'
Assert-Condition -Condition ($workflow.Contains('.\verify-word-count-schedule.ps1')) -Message 'The required workflow must verify the full word-count scheduled publisher.'
Assert-Condition -Condition (Test-Path -LiteralPath $browserPackagePath -PathType Leaf) -Message 'The browser smoke package manifest is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath $browserTestPath -PathType Leaf) -Message 'The browser smoke test is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath $translatorWorkerTestPath -PathType Leaf) -Message 'The translator worker runtime test is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath $serviceWorkerTestPath -PathType Leaf) -Message 'The service-worker failure-injection test is missing.'

$package = Get-Content -Raw -LiteralPath $browserPackagePath | ConvertFrom-Json
$pwaTestScript = [string]$package.scripts.test
foreach ($requiredPwaTest in @('node service-worker-tests.mjs', 'node translator-worker-tests.mjs', 'node browser-smoke.mjs')) {
    Assert-Condition -Condition ($pwaTestScript.Contains($requiredPwaTest)) -Message "The PWA test script must run $requiredPwaTest."
}
Assert-Condition -Condition (![string]::IsNullOrWhiteSpace([string]$package.devDependencies.playwright)) -Message 'The browser smoke test must declare Playwright explicitly.'

Assert-Condition -Condition (Test-Path -LiteralPath $directoryBuildPropsPath -PathType Leaf) -Message 'Directory.Build.props is missing.'
$directoryBuildProps = Get-Content -Raw -LiteralPath $directoryBuildPropsPath
Assert-Condition -Condition ($directoryBuildProps.Contains('<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>')) -Message 'All .NET projects must generate NuGet lock files.'
Assert-Condition -Condition (Test-Path -LiteralPath $dotnetDependencyVerifierPath -PathType Leaf) -Message 'The .NET locked-restore and vulnerability verifier is missing.'
Assert-Condition -Condition ($workflow.Contains('.\verify-dotnet-dependencies.ps1')) -Message 'The required job must run locked restores and transitive vulnerability scans.'
$dotnetDependencyVerifier = Get-Content -Raw -LiteralPath $dotnetDependencyVerifierPath
Assert-Condition -Condition (Test-Path -LiteralPath $globalJsonPath -PathType Leaf) -Message 'The repository SDK pinning file is missing.'
$globalJson = Get-Content -Raw -LiteralPath $globalJsonPath | ConvertFrom-Json
Assert-Condition -Condition ([string]$globalJson.sdk.version -eq '10.0.301' -and [string]$globalJson.sdk.rollForward -eq 'latestPatch') -Message 'The repository must pin the supported .NET SDK feature band and patch roll-forward policy.'
Assert-Condition -Condition (Test-Path -LiteralPath $launcherLockPath -PathType Leaf) -Message 'The launcher NuGet lock file is missing.'
$launcherLock = Get-Content -Raw -LiteralPath $launcherLockPath | ConvertFrom-Json
Assert-Condition -Condition (@($launcherLock.dependencies.'net10.0-windows7.0'.PSObject.Properties).Count -eq 0 -and $null -eq $launcherLock.dependencies.'net10.0-windows7.0'.'Microsoft.NET.ILLink.Tasks') -Message 'The launcher lock file must remain independent of SDK-patch-specific ILLink task injection.'
Assert-Condition -Condition ($dotnetDependencyVerifier.Contains("'--locked-mode'")) -Message 'Every project restore must run in locked mode, including the self-contained launcher.'
Assert-Condition -Condition ($workflow.Contains('dotnet-version: 10.0.301') -and $workflow.Contains('dotnet nuget locals all --clear')) -Message 'The required job must use the pinned SDK and clear NuGet state before locked restore verification.'
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
