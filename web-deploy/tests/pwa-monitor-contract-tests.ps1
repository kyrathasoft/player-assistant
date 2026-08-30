$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\..\pwa\production-response-contracts.ps1')

function Assert-ThrowsMessage {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$ExpectedMessage
    )
    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -ne $ExpectedMessage) {
            throw "Expected '$ExpectedMessage' but received '$($_.Exception.Message)'."
        }
        return
    }
    throw "Expected failure: $ExpectedMessage"
}

$now = [DateTimeOffset]::UtcNow.ToString('o')
$validAnonymousSession = '{"authenticated":false}' | ConvertFrom-Json
Assert-ProductionAnonymousSessionResponse -Payload $validAnonymousSession

$stringAnonymousSession = '{"authenticated":"false"}' | ConvertFrom-Json
Assert-ThrowsMessage -ExpectedMessage 'The anonymous session response has an invalid authentication state.' -Action {
    Assert-ProductionAnonymousSessionResponse -Payload $stringAnonymousSession
}

$integerAnonymousSession = '{"authenticated":0}' | ConvertFrom-Json
Assert-ThrowsMessage -ExpectedMessage 'The anonymous session response has an invalid authentication state.' -Action {
    Assert-ProductionAnonymousSessionResponse -Payload $integerAnonymousSession
}

$cleanupState = [pscustomobject]@{ called = $false }
$primaryFailure = [InvalidOperationException]::new('protected validation failed')
$preservedFailure = Invoke-ProductionSessionCleanup `
    -Authenticated $true `
    -PrimaryFailure $primaryFailure `
    -CleanupAction { $cleanupState.called = $true }
if (!$cleanupState.called -or $preservedFailure -ne $primaryFailure) {
    throw 'Session cleanup did not run after an intermediate protected-response failure.'
}

$combinedFailure = Invoke-ProductionSessionCleanup `
    -Authenticated $true `
    -PrimaryFailure $primaryFailure `
    -CleanupAction { throw 'logout failed' }
if ($combinedFailure.Message -notmatch 'protected validation failed' -or
    $combinedFailure.Message -notmatch 'cleanup also failed: logout failed') {
    throw 'Session cleanup failure did not preserve both failure reasons.'
}

$unauthenticatedCleanupState = [pscustomobject]@{ called = $false }
$null = Invoke-ProductionSessionCleanup `
    -Authenticated $false `
    -CleanupAction { $unauthenticatedCleanupState.called = $true }
if ($unauthenticatedCleanupState.called) {
    throw 'Session cleanup ran without a successful authentication.'
}

$accountId = '0123456789abcdef0123456789abcdef'
$validLogin = (@{
    authenticated = $true
    account = @{
        id = $accountId
        character_name = 'Monitor Hero'
        character_key = 'monitor-hero'
        role = 'player'
        enabled = $true
        password_changed_at = $now
        last_login_at = $now
    }
    csrf_token = 'abcdefghijklmnopqrstuvwxyzABCDEFGH123456789_-'
    idle_expires_at = [DateTimeOffset]::UtcNow.AddMinutes(10).ToString('o')
    absolute_expires_at = [DateTimeOffset]::UtcNow.AddHours(1).ToString('o')
} | ConvertTo-Json -Depth 5 | ConvertFrom-Json)
Assert-ProductionLoginResponse -Payload $validLogin

$stringAuthenticatedLogin = $validLogin.PSObject.Copy()
$stringAuthenticatedLogin.authenticated = 'true'
Assert-ThrowsMessage -ExpectedMessage 'The authenticated login response has an invalid shape.' -Action {
    Assert-ProductionLoginResponse -Payload $stringAuthenticatedLogin
}

$validIdentity = (@{
    authenticated = $true
    account = $validLogin.account
} | ConvertTo-Json -Depth 5 | ConvertFrom-Json)
Assert-ProductionIdentityResponse -Payload $validIdentity -ExpectedAccountId $accountId

$stringAuthenticatedIdentity = $validIdentity.PSObject.Copy()
$stringAuthenticatedIdentity.authenticated = 'true'
Assert-ThrowsMessage -ExpectedMessage 'The authorized identity response has an invalid authentication state.' -Action {
    Assert-ProductionIdentityResponse -Payload $stringAuthenticatedIdentity -ExpectedAccountId $accountId
}

$nullIdentityIds = $validIdentity.PSObject.Copy()
$nullIdentityIds.account = $validIdentity.account.PSObject.Copy()
$nullIdentityIds.account.id = $null
Assert-ThrowsMessage -ExpectedMessage 'The authenticated response has an invalid account ID.' -Action {
    Assert-ProductionIdentityResponse -Payload $nullIdentityIds -ExpectedAccountId $accountId
}

$validCharacter = [pscustomobject]@{
    character_name = 'Monitor Hero'
    character_class = 'Ranger'
    level = 4
    xp_total = 12345
    hit_points = 17
    xp_to_next_level = 7655
}
$validXp = ([pscustomobject]@{
    schema_version = 1
    date_label = 'As of 8.10.2026'
    fetched_at = $now
    stale = $false
    scope = 'character'
    character = $validCharacter
} | ConvertTo-Json -Depth 5 | ConvertFrom-Json)
Assert-ProductionXpResponse -Payload $validXp -MaximumAgeSeconds 86400

$validPartyXp = (@{
    schema_version = 1
    date_label = 'As of 8.10.2026'
    fetched_at = $now
    stale = $false
    scope = 'party'
    characters = @($validCharacter, $validCharacter)
} | ConvertTo-Json -Depth 5 | ConvertFrom-Json)
Assert-ProductionXpResponse -Payload $validPartyXp -MaximumAgeSeconds 86400

$stringSchemaXp = $validXp.PSObject.Copy()
$stringSchemaXp.schema_version = '1'
Assert-ThrowsMessage -ExpectedMessage 'The authorized XP response schema is not version 1.' -Action {
    Assert-ProductionXpResponse -Payload $stringSchemaXp -MaximumAgeSeconds 86400
}

$invalidScopeXp = $validXp.PSObject.Copy()
$invalidScopeXp.scope = 'all'
Assert-ThrowsMessage -ExpectedMessage 'The authorized XP response has an invalid scope.' -Action {
    Assert-ProductionXpResponse -Payload $invalidScopeXp -MaximumAgeSeconds 86400
}

$caseVariantScopeXp = $validXp.PSObject.Copy()
$caseVariantScopeXp.scope = 'Character'
Assert-ThrowsMessage -ExpectedMessage 'The authorized XP response has an invalid scope.' -Action {
    Assert-ProductionXpResponse -Payload $caseVariantScopeXp -MaximumAgeSeconds 86400
}

$contradictoryPartyXp = $validPartyXp.PSObject.Copy()
$contradictoryPartyXp | Add-Member -NotePropertyName character -NotePropertyValue $validCharacter
Assert-ThrowsMessage -ExpectedMessage 'The authorized party XP response exposed a single-character field.' -Action {
    Assert-ProductionXpResponse -Payload $contradictoryPartyXp -MaximumAgeSeconds 86400
}

$staleXpFlag = $validXp.PSObject.Copy()
$staleXpFlag.stale = $true
Assert-ThrowsMessage -ExpectedMessage 'XP source snapshot is stale.' -Action {
    Assert-ProductionXpResponse -Payload $staleXpFlag -MaximumAgeSeconds 86400
}

$oldXp = $validXp.PSObject.Copy()
$oldXp.fetched_at = [DateTimeOffset]::UtcNow.AddDays(-2).ToString('o')
Assert-ThrowsMessage -ExpectedMessage 'XP source snapshot is stale.' -Action {
    Assert-ProductionXpResponse -Payload $oldXp -MaximumAgeSeconds 86400
}

$malformedXp = $validXp.PSObject.Copy()
$malformedXp.schema_version = 2
Assert-ThrowsMessage -ExpectedMessage 'The authorized XP response schema is not version 1.' -Action {
    Assert-ProductionXpResponse -Payload $malformedXp -MaximumAgeSeconds 86400
}

$futureXp = $validXp.PSObject.Copy()
$futureXp.fetched_at = [DateTimeOffset]::UtcNow.AddMinutes(10).ToString('o')
Assert-ThrowsMessage -ExpectedMessage 'A protected response timestamp is unexpectedly in the future.' -Action {
    Assert-ProductionXpResponse -Payload $futureXp -MaximumAgeSeconds 86400
}

$invalidTimestampXp = $validXp.PSObject.Copy()
$invalidTimestampXp.fetched_at = 'not-a-time'
Assert-ThrowsMessage -ExpectedMessage 'The XP source fetch timestamp is invalid.' -Action {
    Assert-ProductionXpResponse -Payload $invalidTimestampXp -MaximumAgeSeconds 86400
}

$permissiveTimestampXp = $validXp.PSObject.Copy()
$permissiveTimestampXp.fetched_at = 'August 10, 2026'
Assert-ThrowsMessage -ExpectedMessage 'The XP source fetch timestamp is invalid.' -Action {
    Assert-ProductionXpResponse -Payload $permissiveTimestampXp -MaximumAgeSeconds 86400
}

$validWordCounts = ([pscustomobject]@{
    schema_version = 1
    observed_at = $now
    uploaded_at = $now
    counting_rule_version = 'obsidian-publish-word-count-v1'
    wiki = [pscustomobject]@{ pages = 990; words = 233048 }
    ic = [pscustomobject]@{ files = 8; words = 15099 }
    ooc = [pscustomobject]@{ files = 6; words = 18753 }
} | ConvertTo-Json -Depth 5 | ConvertFrom-Json)
Assert-ProductionWordCountResponse -Payload $validWordCounts -MaximumAgeSeconds 604800

$whitespaceCountingRule = $validWordCounts.PSObject.Copy()
$whitespaceCountingRule.counting_rule_version = ' obsidian-publish-word-count-v1 '
Assert-ThrowsMessage -ExpectedMessage 'The authorized word-count response has an invalid counting rule.' -Action {
    Assert-ProductionWordCountResponse -Payload $whitespaceCountingRule -MaximumAgeSeconds 604800
}

$staleSource = $validWordCounts.PSObject.Copy()
$staleSource.observed_at = [DateTimeOffset]::UtcNow.AddDays(-8).ToString('o')
Assert-ThrowsMessage -ExpectedMessage 'Word-count source snapshot is stale.' -Action {
    Assert-ProductionWordCountResponse -Payload $staleSource -MaximumAgeSeconds 604800
}

$staleBroker = $validWordCounts.PSObject.Copy()
$staleBroker.uploaded_at = [DateTimeOffset]::UtcNow.AddDays(-8).ToString('o')
Assert-ThrowsMessage -ExpectedMessage 'Word-count broker snapshot is stale.' -Action {
    Assert-ProductionWordCountResponse -Payload $staleBroker -MaximumAgeSeconds 604800
}

$malformedWordCounts = $validWordCounts.PSObject.Copy()
$malformedWordCounts.wiki = [pscustomobject]@{ pages = 0; words = 233048 }
Assert-ThrowsMessage -ExpectedMessage 'The authorized word-count response has an invalid wiki pages count.' -Action {
    Assert-ProductionWordCountResponse -Payload $malformedWordCounts -MaximumAgeSeconds 604800
}

Write-Output 'PWA production-response contract tests passed.'
