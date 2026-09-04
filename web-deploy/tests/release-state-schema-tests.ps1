$ErrorActionPreference = 'Stop'
$verifierPath = Join-Path $PSScriptRoot '..\release-state-schema.ps1'
. $verifierPath

function Assert-ReleaseStateFailure {
    param([hashtable]$State, [hashtable]$Previous, [string]$Expected)
    try {
        Test-ReleaseState -State $State -PreviousState $Previous
        throw "Expected release-state validation to fail: $Expected"
    }
    catch {
        if ($_.Exception.Message -notmatch [regex]::Escape($Expected)) { throw }
    }
}

function New-Fixture {
    param([int]$SchemaVersion = 1, [string]$Component = 'deployment', [string]$State = 'new', [string]$Version = '1.2.3')
    return [ordered]@{
        schema_version = $SchemaVersion
        component = $Component
        transaction_id = 'tx-fixed-001'
        state = $State
        release_version = $Version
        updated_at = '2026-09-03T12:00:00Z'
    }
}

$valid = New-Fixture
$validated = Test-ReleaseState -State $valid
if (-not $validated.Valid) { throw 'A valid release state was rejected.' }
if ($validated.SchemaVersion -ne 1) { throw 'The validated schema version was not returned.' }

Assert-ReleaseStateFailure -State (New-Fixture | ForEach-Object { $_.Remove('component'); $_ }) -Expected 'missing required field'
$unknown = New-Fixture
$unknown['unexpected'] = $true
Assert-ReleaseStateFailure -State $unknown -Expected 'unknown field'
$badTransition = New-Fixture -State 'finalized'
Assert-ReleaseStateFailure -State $badTransition -Previous (New-Fixture -State 'preparing') -Expected 'transition'
$rollback = New-Fixture -State 'preparing' -Version '1.2.2'
Assert-ReleaseStateFailure -State $rollback -Previous (New-Fixture -State 'preparing' -Version '1.2.3') -Expected 'rollback'
$forward = Test-ReleaseState -State (New-Fixture -State 'preparing' -Version '1.3.0') -PreviousState (New-Fixture -State 'preparing' -Version '1.2.3')
if (-not $forward.Valid) { throw 'A monotonic release-version advance was rejected.' }
$future = New-Fixture -SchemaVersion 2
Assert-ReleaseStateFailure -State $future -Expected 'future schema version'
$invalid = New-Fixture -State 'not-a-state'
Assert-ReleaseStateFailure -State $invalid -Expected 'invalid state'

$allowed = @(
    @('new', 'preparing'), @('preparing', 'preparing'), @('preparing', 'promoted'),
    @('preparing', 'rolled_back'), @('promoted', 'finalized'), @('promoted', 'rolled_back'),
    @('finalized', 'finalized'), @('rolled_back', 'rolled_back')
)
foreach ($pair in $allowed) {
    $result = Test-ReleaseState -State (New-Fixture -State $pair[1]) -PreviousState (New-Fixture -State $pair[0])
    if (-not $result.Valid) { throw "Allowed transition rejected: $($pair -join ' -> ')" }
}

Write-Output 'Release-state schema tests passed.'
