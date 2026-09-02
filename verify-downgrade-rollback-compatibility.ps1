param([string]$RepoRoot = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
$policyPath = Join-Path $RepoRoot 'compatibility-boundaries.json'
$policy = Get-Content -Raw -LiteralPath $policyPath | ConvertFrom-Json
Assert ($policy.schema_version -eq 1) 'Compatibility policy schema is unsupported.'
$required = @('compatible-rollback','unsafe-downgrade','mixed-generation','stale-signature','schema-incompatibility','partial-promotion','interrupted-recovery','cache-version-drift','exact-pre-existing-state-restoration')
Assert (@($policy.required_scenarios).Count -eq $required.Count) 'Compatibility scenario inventory is incomplete.'
foreach ($scenario in $required) { Assert (@($policy.required_scenarios) -contains $scenario) "Missing scenario: $scenario" }
Assert ($policy.policy.desktop.protocol -eq 1 -and [version]$policy.policy.desktop.current_version -ge [version]$policy.policy.desktop.downgrade_floor) 'Desktop compatibility boundary is invalid.'
Assert ($policy.policy.installer.package_schema -eq 1 -and $policy.policy.installer.transaction_schema -eq 1) 'Installer schemas drifted.'
Assert ($policy.policy.updater.manifest_schema -eq 1 -and $policy.policy.updater.signature -eq 'RSA-SHA256') 'Updater integrity policy drifted.'
Assert ($policy.policy.broker.current_schema -eq 8 -and @($policy.policy.broker.supported_upgrade_from) -join ',' -eq '0,1,2,3,4,5,6,7') 'Broker migration boundary is incomplete.'
Assert ($policy.policy.pwa.cache_revision -gt 0 -and $policy.policy.pwa.app_revision -gt 0) 'PWA cache generation is invalid.'
$transitionSet = @($policy.allowed_transitions)
Assert ($transitionSet -contains 'promoted->finalized') 'Finalization transition is missing.'
Assert (!$transitionSet.Contains('finalized->rolled-back')) 'Finalized releases must never roll back.'

# A deterministic transaction model proves fail-closed transition and restoration semantics.
$old = [ordered]@{ generation = 41; stage = 'promoted'; desktop = 'old-desktop'; installer = 'old-installer'; broker = 'old-broker'; pwa = 'old-pwa' }
$candidate = [ordered]@{ generation = 42; stage = 'verified'; desktop = 'new-desktop'; installer = 'new-installer'; broker = 'new-broker'; pwa = 'new-pwa' }
$before = ($old | ConvertTo-Json -Compress)
Assert ($transitionSet -contains 'verified->promoted') 'Verified promotion is missing.'
$candidate.stage = 'promoted'
$candidate.stage = 'rolled-back'
$restored = ($old | ConvertTo-Json -Compress)
Assert ($restored -ceq $before) 'Rollback did not restore the exact pre-existing generation.'
Assert ([version]'0.9.0' -ge [version]$policy.policy.desktop.downgrade_floor) 'Supported rollback floor rejected.'
Assert ([version]'0.8.9' -lt [version]$policy.policy.desktop.downgrade_floor) 'Unsafe downgrade was accepted.'
Assert ($policy.policy.updater.downgrade_floor -eq 'highest-trusted-version') 'Updater must enforce the trusted-version floor.'
Assert ($policy.policy.broker.rollback -eq 'pre-mutation-only') 'Broker rollback policy drifted.'
Assert ($policy.policy.pwa.cache_key -eq 'pwa-version-cache-app-revision') 'PWA cache generation key policy drifted.'

$sourceChecks = @{
  'desktop' = @('ReleaseStateSchema.cs','PlayerAssistantUpdateUtility.cs')
  'installer' = @('build-installer.ps1','Installer/install-player-assistant.ps1','pwa/online-installer-for-pwa/install-player-assistant-web.php')
  'updater' = @('build-release-update-artifacts.ps1','PlayerAssistantUpdateUtility.cs')
  'broker' = @('web-deploy/player-assistant-broker/DatabaseMigrationService.php','web-deploy/tests/migration-rehearsal-tests.php')
  'pwa' = @('pwa/version.js','pwa/service-worker.js','pwa/service-worker-tests.mjs')
}
foreach ($boundary in $sourceChecks.Keys) { foreach ($relative in $sourceChecks[$boundary]) { Assert (Test-Path -LiteralPath (Join-Path $RepoRoot $relative) -PathType Leaf) "$boundary compatibility source is missing: $relative" } }
Write-Output 'Downgrade and rollback compatibility tests passed.'
