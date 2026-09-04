# Versioned, fail-closed contract for release transaction journals.
Set-StrictMode -Version Latest

$script:ReleaseStateSchemaVersion = 1
$script:ReleaseStateRequiredFields = @(
    'schema_version', 'component', 'transaction_id', 'state', 'release_version', 'updated_at'
)
$script:ReleaseStateAllowedFields = $script:ReleaseStateRequiredFields
$script:ReleaseStateComponents = @('deployment', 'installer', 'updater', 'broker')
$script:ReleaseStateValues = @('new', 'preparing', 'promoted', 'finalized', 'rolled_back')
$script:ReleaseStateTransitions = @{
    new = @('preparing')
    preparing = @('preparing', 'promoted', 'rolled_back')
    promoted = @('promoted', 'finalized', 'rolled_back')
    finalized = @('finalized')
    rolled_back = @('rolled_back')
}

function Test-ReleaseState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$State,
        [System.Collections.IDictionary]$PreviousState
    )

    foreach ($field in $script:ReleaseStateRequiredFields) {
        if (-not $State.Contains($field)) {
            throw "Release state missing required field '$field'."
        }
    }
    foreach ($field in $State.Keys) {
        if ($field -notin $script:ReleaseStateAllowedFields) {
            throw "Release state contains unknown field '$field'."
        }
    }

    $schemaVersion = 0
    if (-not [int]::TryParse([string]$State.schema_version, [ref]$schemaVersion) -or $schemaVersion -ne $State.schema_version) {
        throw 'Release state schema_version must be an integer.'
    }
    if ($schemaVersion -gt $script:ReleaseStateSchemaVersion) {
        throw "Release state uses future schema version $schemaVersion; maximum supported is $($script:ReleaseStateSchemaVersion)."
    }
    if ($schemaVersion -lt 1) { throw "Release state schema version $schemaVersion is unsupported." }

    if ([string]$State.component -notin $script:ReleaseStateComponents) {
        throw "Release state has invalid component '$($State.component)'."
    }
    if ([string]$State.state -notin $script:ReleaseStateValues) {
        throw "Release state has invalid state '$($State.state)'."
    }
    foreach ($field in @('transaction_id', 'release_version', 'updated_at')) {
        if ([string]::IsNullOrWhiteSpace([string]$State[$field])) {
            throw "Release state field '$field' must be non-empty."
        }
    }
    $releaseVersion = $null
    try { $releaseVersion = [Version]$State.release_version } catch { throw "Release state release_version '$($State.release_version)' is invalid." }
    if ($releaseVersion -lt [Version]'0.0.0') { throw 'Release state release_version cannot be negative.' }

    if ($null -ne $PreviousState) {
        if (([string]$PreviousState.component -ne [string]$State.component) -or ([string]$PreviousState.transaction_id -ne [string]$State.transaction_id)) {
            throw 'Release state transition identity does not match the previous state.'
        }
        $previousVersion = $null
        try { $previousVersion = [Version]$PreviousState.release_version } catch { throw "Previous release state release_version '$($PreviousState.release_version)' is invalid." }
        if ($releaseVersion -lt $previousVersion) {
            throw "Release state release version rollback from $previousVersion to $releaseVersion is not allowed."
        }
        if ([string]$State.state -notin $script:ReleaseStateTransitions[[string]$PreviousState.state]) {
            throw "Invalid release state transition from '$($PreviousState.state)' to '$($State.state)'."
        }
    }

    return [pscustomobject]@{
        Valid = $true
        SchemaVersion = $schemaVersion
        Component = [string]$State.component
        TransactionId = [string]$State.transaction_id
        State = [string]$State.state
        ReleaseVersion = $releaseVersion.ToString()
    }
}
