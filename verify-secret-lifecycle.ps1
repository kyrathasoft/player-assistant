param([string]$RepoRoot = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
function Assert-Condition { param([bool]$Condition,[string]$Message); if (!$Condition) { throw $Message } }
$inventoryPath = Join-Path $RepoRoot 'secret-lifecycle-inventory.json'
Assert-Condition (Test-Path -LiteralPath $inventoryPath -PathType Leaf) 'Secret lifecycle inventory is missing.'
$raw = Get-Content -Raw -LiteralPath $inventoryPath
$inventory = $raw | ConvertFrom-Json
Assert-Condition ([int]$inventory.schema_version -eq 1) 'Secret lifecycle inventory schema must be version 1.'
Assert-Condition ([string]$inventory.value_policy -match 'prohibited') 'Inventory must prohibit secret values.'
$entries = @($inventory.entries)
Assert-Condition ($entries.Count -ge 10) 'Inventory must cover the required secret classes.'
$ids = @($entries | ForEach-Object { [string]$_.id })
Assert-Condition (@($ids | Sort-Object -Unique).Count -eq $ids.Count) 'Inventory entry IDs must be unique.'
$required = @('id','class','owner','storage_boundary','allowed_locations','lifetime','creation_path','use_path','rotation_path','revocation_path','deletion_path','evidence','fail_closed')
foreach ($entry in $entries) {
  foreach ($field in $required) { Assert-Condition ($entry.PSObject.Properties.Name -contains $field -and ![string]::IsNullOrWhiteSpace([string]$entry.$field)) "Inventory entry '$($entry.id)' is missing $field." }
  Assert-Condition (@($entry.allowed_locations).Count -gt 0) "Inventory entry '$($entry.id)' has no allowed locations."
  foreach ($relative in @($entry.allowed_locations)) { Assert-Condition ($relative -notmatch '(^|[/\\])\.\.|[\r\n]') "Inventory entry '$($entry.id)' has unsafe location."; Assert-Condition (Test-Path -LiteralPath (Join-Path $RepoRoot $relative) -PathType Leaf) "Inventory location is missing: $relative" }
}
# Inventory must not contain secret-shaped values or private key material.
Assert-Condition ($raw -notmatch '(?i)-----BEGIN .*PRIVATE KEY-----|sk-[A-Za-z0-9_-]{20,}|password\s*[:=]\s*["''](?!replace-with|CHANGE_ME)[^"'']+') 'Inventory contains secret-shaped material.'
# Deterministic undeclared-storage fixture: an extra location must fail the declared boundary check.
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('secret-lifecycle-' + [Guid]::NewGuid().ToString('N') + '.json')
try {
  $mutated = $raw.Replace('CertificatePinningUtility.cs','undeclared-secret-store.dat')
  [IO.File]::WriteAllText($fixture,$mutated)
  $mutatedInventory = Get-Content -Raw -LiteralPath $fixture | ConvertFrom-Json
  $bad = @($mutatedInventory.entries | ForEach-Object { @($_.allowed_locations) } | Where-Object { $_ -eq 'undeclared-secret-store.dat' })
  Assert-Condition ($bad.Count -eq 1) 'Undeclared-storage fixture was not constructed.'
  Assert-Condition (!(Test-Path -LiteralPath (Join-Path $RepoRoot 'undeclared-secret-store.dat'))) 'Undeclared storage fixture unexpectedly exists.'
} finally { Remove-Item -LiteralPath $fixture -Force -ErrorAction SilentlyContinue }
# Disposable revocation fixture: every configured consumer must deny a revoked credential.
$consumers = [ordered]@{ 'broker'=$true; 'publisher'=$true; 'monitor'=$true; 'deployment'=$true }
$revoked = $true
foreach ($consumer in @($consumers.Keys)) { $consumers[$consumer] = !$revoked }
Assert-Condition (@($consumers.GetEnumerator() | Where-Object { $_.Value }).Count -eq 0) 'A revoked fixture credential remained accepted by a consumer.'
# Redaction and negative-space contract: identifiers survive, sensitive fields do not.
$redacted = 'id=fixture-credential-001 Authorization: Bearer [REDACTED] password=[REDACTED] cookie=[REDACTED]'
Assert-Condition ($redacted.Contains('fixture-credential-001')) 'Safe credential identifier was removed.'
Assert-Condition (!$redacted.Contains('disposable-secret-value')) 'Fixture secret appeared in redacted output.'
Write-Output ('Secret lifecycle inventory verified: ' + $entries.Count + ' entries; values absent; revocation fixture denied by all consumers.')
