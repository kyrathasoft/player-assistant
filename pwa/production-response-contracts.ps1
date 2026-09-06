Set-StrictMode -Version Latest

function Assert-ProductionResponseCondition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (!$Condition) {
        throw $Message
    }
}

function Get-ProductionResponseTimestamp {
    param(
        [Parameter(Mandatory = $true)]$Payload,
        [Parameter(Mandatory = $true)][string]$PropertyName,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Assert-ProductionResponseCondition `
        -Condition ($Payload.PSObject.Properties.Name -contains $PropertyName) `
        -Message "$Label is missing."
    $rawValue = $Payload.$PropertyName
    Assert-ProductionResponseCondition `
        -Condition ($rawValue -is [string] -or $rawValue -is [DateTime] -or $rawValue -is [DateTimeOffset]) `
        -Message "$Label is invalid."
    try {
        if ($rawValue -is [DateTimeOffset]) {
            return $rawValue
        }
        if ($rawValue -is [DateTime]) {
            return [DateTimeOffset]$rawValue
        }
        Assert-ProductionResponseCondition -Condition ($rawValue.Length -le 40 -and $rawValue -cmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,9})?(?:Z|[+-]\d{2}:\d{2})$') -Message "$Label is invalid."
        return [DateTimeOffset]::Parse(
            $rawValue,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind)
    }
    catch {
        throw "$Label is invalid."
    }
}

function Assert-ProductionTimestampFreshness {
    param(
        [Parameter(Mandatory = $true)][DateTimeOffset]$Timestamp,
        [Parameter(Mandatory = $true)][int]$MaximumAgeSeconds,
        [Parameter(Mandatory = $true)][string]$StaleMessage
    )

    Assert-ProductionResponseCondition -Condition ($MaximumAgeSeconds -gt 0) -Message 'Freshness limits must be positive.'
    $ageSeconds = ([DateTimeOffset]::UtcNow - $Timestamp.ToUniversalTime()).TotalSeconds
    Assert-ProductionResponseCondition -Condition ($ageSeconds -ge -300) -Message 'A protected response timestamp is unexpectedly in the future.'
    Assert-ProductionResponseCondition -Condition ($ageSeconds -le $MaximumAgeSeconds) -Message $StaleMessage
}

function Test-ProductionInteger {
    param($Value)
    return $Value -is [byte] -or
        $Value -is [sbyte] -or
        $Value -is [int16] -or
        $Value -is [uint16] -or
        $Value -is [int32] -or
        $Value -is [uint32] -or
        $Value -is [int64] -or
        $Value -is [uint64]
}

function Test-ProductionCount {
    param($Value, [switch]$MustBePositive)
    if (!(Test-ProductionInteger $Value)) {
        return $false
    }
    $count = [decimal]$Value
    return $count -le 1000000000 -and ($count -gt 0 -or (!$MustBePositive -and $count -eq 0))
}

function Invoke-ProductionSessionCleanup {
    param(
        [Parameter(Mandatory = $true)][bool]$Authenticated,
        [Parameter(Mandatory = $true)][scriptblock]$CleanupAction,
        $PrimaryFailure = $null
    )

    $result = $PrimaryFailure
    if ($Authenticated) {
        try {
            $null = & $CleanupAction
        }
        catch {
            $cleanupFailure = $_
            if ($null -eq $result) {
                $result = $cleanupFailure
            }
            else {
                $primaryException = if ($result -is [Management.Automation.ErrorRecord]) {
                    $result.Exception
                }
                elseif ($result -is [Exception]) {
                    $result
                }
                else {
                    [InvalidOperationException]::new([string]$result)
                }
                $result = [InvalidOperationException]::new(
                    "$($primaryException.Message) Monitor session cleanup also failed: $($cleanupFailure.Exception.Message)",
                    $primaryException)
            }
        }
    }
    return $result
}

function Assert-ProductionAnonymousSessionResponse {
    param([Parameter(Mandatory = $true)]$Payload)

    Assert-ProductionResponseCondition -Condition ($Payload.authenticated -is [bool] -and $Payload.authenticated -eq $false) -Message 'The anonymous session response has an invalid authentication state.'
    Assert-ProductionResponseCondition -Condition (@($Payload.PSObject.Properties.Name).Count -eq 1) -Message 'The anonymous session response exposed unexpected account data.'
}

function Assert-ProductionAccountShape {
    param([Parameter(Mandatory = $true)]$Account)

    Assert-ProductionResponseCondition -Condition ($null -ne $Account) -Message 'The authenticated response is missing its account identity.'
    Assert-ProductionResponseCondition -Condition ($Account.id -is [string] -and $Account.id -cmatch '^[a-f0-9]{32}$') -Message 'The authenticated response has an invalid account ID.'
    Assert-ProductionResponseCondition -Condition ($Account.character_name -is [string] -and $Account.character_name.Length -gt 0 -and $Account.character_name.Length -le 100 -and $Account.character_name.Trim() -ceq $Account.character_name) -Message 'The authenticated response has an invalid character name.'
    Assert-ProductionResponseCondition -Condition ($Account.character_key -is [string] -and $Account.character_key -cmatch '^[a-z0-9][a-z0-9._:-]{0,99}$') -Message 'The authenticated response has an invalid character key.'
    Assert-ProductionResponseCondition -Condition (@('player', 'dm') -ccontains [string]$Account.role) -Message 'The authenticated response has an invalid account role.'
    Assert-ProductionResponseCondition -Condition ($Account.enabled -is [bool] -and $Account.enabled -eq $true) -Message 'The authenticated response account is not enabled.'
}

function Assert-ProductionLoginResponse {
    param([Parameter(Mandatory = $true)]$Payload)

    Assert-ProductionResponseCondition -Condition ($Payload.authenticated -is [bool] -and $Payload.authenticated -eq $true) -Message 'The authenticated login response has an invalid shape.'
    Assert-ProductionAccountShape -Account $Payload.account
    Assert-ProductionResponseCondition -Condition ($Payload.csrf_token -is [string] -and $Payload.csrf_token -cmatch '^[A-Za-z0-9_-]{32,128}$') -Message 'The authenticated login response is missing its CSRF token.'
    $idleExpiresAt = Get-ProductionResponseTimestamp -Payload $Payload -PropertyName 'idle_expires_at' -Label 'The login idle-expiration timestamp'
    $absoluteExpiresAt = Get-ProductionResponseTimestamp -Payload $Payload -PropertyName 'absolute_expires_at' -Label 'The login absolute-expiration timestamp'
    Assert-ProductionResponseCondition -Condition ($idleExpiresAt -gt [DateTimeOffset]::UtcNow) -Message 'The authenticated login session is already idle-expired.'
    Assert-ProductionResponseCondition -Condition ($absoluteExpiresAt -ge $idleExpiresAt) -Message 'The authenticated login expiration order is invalid.'
}

function Assert-ProductionIdentityResponse {
    param(
        [Parameter(Mandatory = $true)]$Payload,
        [Parameter(Mandatory = $true)][string]$ExpectedAccountId
    )

    Assert-ProductionResponseCondition -Condition ($ExpectedAccountId -cmatch '^[a-f0-9]{32}$') -Message 'The expected monitor account ID is invalid.'
    Assert-ProductionResponseCondition -Condition ($Payload.authenticated -is [bool] -and $Payload.authenticated -eq $true) -Message 'The authorized identity response has an invalid authentication state.'
    Assert-ProductionAccountShape -Account $Payload.account
    Assert-ProductionResponseCondition -Condition ($Payload.account.id -ceq $ExpectedAccountId) -Message 'The authorized identity response does not match the monitor account.'
}

function Assert-ProductionCharacterShape {
    param([Parameter(Mandatory = $true)]$Character)

    $names = @($Character.PSObject.Properties.Name)
    Assert-ProductionResponseCondition -Condition ($names -contains 'character_name' -and $Character.character_name -is [string] -and $Character.character_name.Length -gt 0) -Message 'The authorized XP response contains an invalid character name.'
    Assert-ProductionResponseCondition -Condition ($names -contains 'character_class' -and $Character.character_class -is [string] -and $Character.character_class.Length -gt 0) -Message 'The authorized XP response contains an invalid character class.'
    Assert-ProductionResponseCondition -Condition (Test-ProductionCount $Character.level -MustBePositive) -Message 'The authorized XP response contains an invalid level.'
    Assert-ProductionResponseCondition -Condition (Test-ProductionCount $Character.xp_total) -Message 'The authorized XP response contains an invalid XP total.'
    Assert-ProductionResponseCondition -Condition (Test-ProductionCount $Character.hit_points) -Message 'The authorized XP response contains invalid hit points.'
    Assert-ProductionResponseCondition -Condition ($null -eq $Character.xp_to_next_level -or (Test-ProductionCount $Character.xp_to_next_level)) -Message 'The authorized XP response contains an invalid XP-to-next-level value.'
}

function Assert-ProductionProtectedResourceEnvelope {
    param([Parameter(Mandatory = $true)]$Payload, [Parameter(Mandatory = $true)][string]$ExpectedAccountId)
    $meta = $Payload._protected_resource
    Assert-ProductionResponseCondition -Condition ($null -ne $meta) -Message 'The protected response is missing its freshness envelope.'
    Assert-ProductionResponseCondition -Condition ((Test-ProductionInteger $meta.schema_version) -and [decimal]$meta.schema_version -eq 1) -Message 'The protected response envelope schema is invalid.'
    Assert-ProductionResponseCondition -Condition ($meta.account_id -is [string] -and $meta.account_id -ceq $ExpectedAccountId) -Message 'The protected response is bound to the wrong account.'
    Assert-ProductionResponseCondition -Condition ($meta.resource -ceq '/v1/protected') -Message 'The protected response resource binding is invalid.'
    Assert-ProductionResponseCondition -Condition ($meta.generation -is [string] -and $meta.generation -cmatch '^[a-f0-9]{64}$') -Message 'The protected response generation binding is invalid.'
    Assert-ProductionResponseCondition -Condition ($meta.resource_revision -is [string] -and $meta.resource_revision -cmatch '^[a-f0-9]{64}$') -Message 'The protected response revision is invalid.'
    Assert-ProductionResponseCondition -Condition ($meta.nonce -is [string] -and $meta.nonce -cmatch '^[a-f0-9]{32}$') -Message 'The protected response replay nonce is invalid.'
    $issuedAt = Get-ProductionResponseTimestamp -Payload $meta -PropertyName 'issued_at' -Label 'The protected response issue timestamp'
    $expiresAt = Get-ProductionResponseTimestamp -Payload $meta -PropertyName 'expires_at' -Label 'The protected response expiry timestamp'
    Assert-ProductionResponseCondition -Condition ($expiresAt -gt [DateTimeOffset]::UtcNow) -Message 'The protected response envelope is expired.'
    Assert-ProductionResponseCondition -Condition (($expiresAt - $issuedAt).TotalSeconds -le 305) -Message 'The protected response envelope lifetime is too long.'
}

function Assert-ProductionDataInvariant {
    param([Parameter(Mandatory = $true)]$Payload, [Parameter(Mandatory = $true)][ValidateSet('xp','word-count','quests','messages')][string]$Kind)
    if ($Kind -eq 'xp') {
        Assert-ProductionResponseCondition -Condition ($null -ne $Payload.scope) -Message 'Invariant failed: xp.protected-scope.'
        if ([string]$Payload.scope -eq 'party') { Assert-ProductionResponseCondition -Condition ($Payload.characters -is [array]) -Message 'Invariant failed: xp.authoritative-shape.' }
        else { Assert-ProductionResponseCondition -Condition ($null -ne $Payload.character) -Message 'Invariant failed: xp.authoritative-shape.' }
    }
    elseif ($Kind -eq 'word-count') {
        Assert-ProductionResponseCondition -Condition ($Payload.wiki.words -ge 0 -and $Payload.ic.words -ge 0 -and $Payload.ooc.words -ge 0) -Message 'Invariant failed: word-count.bounded-shape.'
    }
    elseif ($Kind -eq 'quests') {
        Assert-ProductionResponseCondition -Condition ($Payload.quests -is [array]) -Message 'Invariant failed: quests.shape.'
    }
    else {
        Assert-ProductionResponseCondition -Condition ($Payload.messages -is [array]) -Message 'Invariant failed: messages.shape.'
    }
}
function Assert-ProductionXpResponse {
    param(
        [Parameter(Mandatory = $true)]$Payload,
        [Parameter(Mandatory = $true)][int]$MaximumAgeSeconds,
        [string]$ExpectedAccountId = ''
    )

    Assert-ProductionDataInvariant -Payload $Payload -Kind 'xp'
    if ($ExpectedAccountId -ne '') { Assert-ProductionProtectedResourceEnvelope -Payload $Payload -ExpectedAccountId $ExpectedAccountId }
    Assert-ProductionResponseCondition -Condition ((Test-ProductionInteger $Payload.schema_version) -and [decimal]$Payload.schema_version -eq 1) -Message 'The authorized XP response schema is not version 1.'
    Assert-ProductionResponseCondition -Condition ($Payload.stale -is [bool] -and [bool]$Payload.stale -eq $false) -Message 'XP source snapshot is stale.'
    Assert-ProductionResponseCondition -Condition ($Payload.date_label -is [string] -and $Payload.date_label.Length -gt 0 -and $Payload.date_label.Length -le 80) -Message 'The authorized XP response has an invalid date label.'
    $fetchedAt = Get-ProductionResponseTimestamp -Payload $Payload -PropertyName 'fetched_at' -Label 'The XP source fetch timestamp'
    Assert-ProductionTimestampFreshness -Timestamp $fetchedAt -MaximumAgeSeconds $MaximumAgeSeconds -StaleMessage 'XP source snapshot is stale.'

    Assert-ProductionResponseCondition -Condition (@('character', 'party') -ccontains [string]$Payload.scope) -Message 'The authorized XP response has an invalid scope.'
    if ($Payload.scope -eq 'character') {
        Assert-ProductionResponseCondition -Condition ($null -ne $Payload.character) -Message 'The authorized player XP response is missing its character.'
        Assert-ProductionResponseCondition -Condition ($Payload.PSObject.Properties.Name -notcontains 'characters') -Message 'The authorized player XP response exposed party data.'
        Assert-ProductionCharacterShape -Character $Payload.character
    }
    else {
        Assert-ProductionResponseCondition -Condition ($Payload.PSObject.Properties.Name -notcontains 'character') -Message 'The authorized party XP response exposed a single-character field.'
        Assert-ProductionResponseCondition -Condition ($Payload.characters -is [array] -and $Payload.characters.Count -gt 0) -Message 'The authorized party XP response is missing its characters.'
        foreach ($character in $Payload.characters) {
            Assert-ProductionCharacterShape -Character $character
        }
    }
    Assert-ProductionResponseCondition -Condition ($Payload.PSObject.Properties.Name -notcontains 'source_url') -Message 'The authorized XP response exposed its private source URL.'
}

function Assert-ProductionWordCountResponse {
    param(
        [Parameter(Mandatory = $true)]$Payload,
        [Parameter(Mandatory = $true)][int]$MaximumAgeSeconds,
        [string]$ExpectedAccountId = ''
    )

    if ($ExpectedAccountId -ne '') { Assert-ProductionProtectedResourceEnvelope -Payload $Payload -ExpectedAccountId $ExpectedAccountId }
    Assert-ProductionResponseCondition -Condition ((Test-ProductionInteger $Payload.schema_version) -and [decimal]$Payload.schema_version -eq 1) -Message 'The authorized word-count response schema is not version 1.'
    Assert-ProductionResponseCondition -Condition ($Payload.counting_rule_version -is [string] -and $Payload.counting_rule_version.Length -gt 0 -and $Payload.counting_rule_version.Length -le 100 -and $Payload.counting_rule_version.Trim() -ceq $Payload.counting_rule_version) -Message 'The authorized word-count response has an invalid counting rule.'
    $observedAt = Get-ProductionResponseTimestamp -Payload $Payload -PropertyName 'observed_at' -Label 'The word-count observation timestamp'
    $uploadedAt = Get-ProductionResponseTimestamp -Payload $Payload -PropertyName 'uploaded_at' -Label 'The word-count upload timestamp'
    Assert-ProductionTimestampFreshness -Timestamp $observedAt -MaximumAgeSeconds $MaximumAgeSeconds -StaleMessage 'Word-count source snapshot is stale.'
    Assert-ProductionTimestampFreshness -Timestamp $uploadedAt -MaximumAgeSeconds $MaximumAgeSeconds -StaleMessage 'Word-count broker snapshot is stale.'

    foreach ($section in @(@('wiki', 'pages'), @('ic', 'files'), @('ooc', 'files'))) {
        $value = $Payload.($section[0])
        Assert-ProductionResponseCondition -Condition ($null -ne $value) -Message "The authorized word-count response is missing $($section[0])."
        Assert-ProductionResponseCondition -Condition (Test-ProductionCount $value.($section[1]) -MustBePositive) -Message "The authorized word-count response has an invalid $($section[0]) $($section[1]) count."
        Assert-ProductionResponseCondition -Condition (Test-ProductionCount $value.words) -Message "The authorized word-count response has an invalid $($section[0]) word count."
    }
}
