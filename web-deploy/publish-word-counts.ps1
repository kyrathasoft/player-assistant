param(
    [Parameter(Mandatory = $true)][ValidateRange(1, 1000000000)][int]$WikiPages,
    [Parameter(Mandatory = $true)][ValidateRange(0, 1000000000)][long]$WikiWords,
    [Parameter(Mandatory = $true)][ValidateRange(1, 1000000000)][int]$IcFiles,
    [Parameter(Mandatory = $true)][ValidateRange(0, 1000000000)][long]$IcWords,
    [Parameter(Mandatory = $true)][ValidateRange(1, 1000000000)][int]$OocFiles,
    [Parameter(Mandatory = $true)][ValidateRange(0, 1000000000)][long]$OocWords,
    [DateTimeOffset]$ObservedAt = [DateTimeOffset]::UtcNow,
    [ValidateLength(1, 100)][string]$CountingRuleVersion = 'obsidian-publish-word-count-v1',
    [uri]$ApiUrl = 'https://bryanmiller.us/scarlethorizons/api/v1/admin/word-counts',
    [Security.SecureString]$AdminKey
)

$ErrorActionPreference = 'Stop'

if ($ApiUrl.Scheme -ne [Uri]::UriSchemeHttps -or
    !$ApiUrl.IsDefaultPort -or
    ![string]::IsNullOrEmpty($ApiUrl.UserInfo)) {
    throw 'The word-count API URL must be a default-port HTTPS URL without embedded credentials.'
}

$snapshot = [ordered]@{
    schema_version = 1
    observed_at = $ObservedAt.UtcDateTime.ToString(
        'yyyy-MM-ddTHH:mm:ss.fffZ',
        [Globalization.CultureInfo]::InvariantCulture)
    counting_rule_version = $CountingRuleVersion
    wiki = [ordered]@{ pages = $WikiPages; words = $WikiWords }
    ic = [ordered]@{ files = $IcFiles; words = $IcWords }
    ooc = [ordered]@{ files = $OocFiles; words = $OocWords }
}

if ($null -eq $AdminKey) {
    $AdminKey = Read-Host 'Broker administrator key' -AsSecureString
}

$keyPointer = [IntPtr]::Zero
$plainAdminKey = $null
try {
    $keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($AdminKey)
    $plainAdminKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPointer)
    if ([string]::IsNullOrWhiteSpace($plainAdminKey)) {
        throw 'The broker administrator key is required.'
    }

    $response = Invoke-RestMethod `
        -Method Put `
        -Uri $ApiUrl `
        -ContentType 'application/json' `
        -Headers @{ 'X-Broker-Admin-Key' = $plainAdminKey } `
        -MaximumRedirection 0 `
        -Body ($snapshot | ConvertTo-Json -Depth 4 -Compress)

    if ([int]$response.schema_version -ne 1 -or
        [DateTimeOffset]$response.observed_at -ne [DateTimeOffset]$snapshot.observed_at -or
        [long]$response.wiki.words -ne $WikiWords -or
        [long]$response.ic.words -ne $IcWords -or
        [long]$response.ooc.words -ne $OocWords) {
        throw 'The broker did not return the exact uploaded word-count snapshot.'
    }

    [pscustomobject]@{
        Published = $true
        ObservedAt = ([DateTimeOffset]$response.observed_at).ToString('o')
        WikiWords = [long]$response.wiki.words
        IcWords = [long]$response.ic.words
        OocWords = [long]$response.ooc.words
    }
}
finally {
    if ($keyPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer)
    }
    $plainAdminKey = $null
}
