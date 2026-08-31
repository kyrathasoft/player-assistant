param([string]$RepoRoot = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$php = Get-Command php -ErrorAction SilentlyContinue
if ($php) {
  & $php.Source (Join-Path $RepoRoot 'web-deploy\tests\protected-data-negative-space-tests.php')
  if ($LASTEXITCODE -ne 0) { throw 'Protected-data PHP negative-space tests failed.' }
}
$node = Get-Command node -ErrorAction SilentlyContinue
if ($node) {
  & $node.Source (Join-Path $RepoRoot 'pwa\protected-data-negative-space-tests.mjs')
  if ($LASTEXITCODE -ne 0) { throw 'Protected-data PWA negative-space tests failed.' }
}
$required = @(
  'SensitiveTextRedactionUtility.cs', 'collect-diagnostics.ps1', 'LastCrashDiagnosticUtility.cs',
  'OutboundNetworkDiagnosticsUtility.cs', 'pwa\browser-smoke.mjs', '.github\workflows\hardening.yml'
)
foreach ($relative in $required) {
  if (!(Test-Path -LiteralPath (Join-Path $RepoRoot $relative) -PathType Leaf)) { throw "Negative-space inventory target missing: $relative" }
}
$workflow = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot '.github\workflows\hardening.yml')
if (!$workflow.Contains('verify-protected-data-negative-space.ps1')) { throw 'Canonical hardening workflow does not run protected-data negative-space verification.' }
Write-Output 'Protected-data negative-space verification passed.'
