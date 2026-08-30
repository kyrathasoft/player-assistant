$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot 'resource-budgets.json'
if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'resource-budgets.json is missing.' }
$payload = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
if ([int]$payload.schema_version -ne 1) { throw 'Unsupported resource budget schema.' }
$required = @('broker_query_latency_ms','message_table_rows','cache_retention_days','backup_retention_count','startup_ms','pwa_polling_seconds','optional_pack_bytes','diagnostic_bytes')
foreach ($name in $required) {
    $value = $payload.budgets.$name
    if ($null -eq $value -or [int64]$value -le 0) { throw "Resource budget '$name' must be positive." }
}
if ([int64]$payload.budgets.pwa_polling_seconds -lt 15) { throw 'PWA polling budget is below the safe minimum.' }
if ([int64]$payload.fixture.message_rows -ne [int64]$payload.budgets.message_table_rows) { throw 'Large message fixture does not reach its configured boundary.' }
if ([int64]$payload.fixture.slow_io_ms -ne [int64]$payload.budgets.broker_query_latency_ms) { throw 'Slow-I/O fixture does not reach its configured boundary.' }
$app = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'pwa\app.js')
if ($app -notmatch 'RESOURCE_BUDGET_PWA_POLLING_SECONDS') { throw 'PWA polling does not name its resource budget.' }
$loader = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'pwa\optional-pack-loader.js')
if ($loader -notmatch 'optionalPackBudgetBytes') { throw 'Optional-pack storage budget is not enforced.' }
Write-Output 'Resource budget verification passed.'
