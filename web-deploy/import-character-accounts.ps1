param(
    [string]$PasswordHashPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'xp-passwords.json'),
    [string]$ApiUrl = 'https://bryanmiller.us/scarlethorizons/api/v1/admin/character-accounts/import',
    [Security.SecureString]$AdminKey
)

$ErrorActionPreference = 'Stop'

function New-BrokerAdminHeaders {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Route,
        [Parameter(Mandatory = $true)][string]$BodyJson,
        [Parameter(Mandatory = $true)][string]$Key
    )
    $timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
    $nonce = [Guid]::NewGuid().ToString('N')
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bodyHash = [BitConverter]::ToString(
            $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($BodyJson)))
    }
    finally {
        $sha.Dispose()
    }
    $bodyHash = $bodyHash.Replace('-', '').ToLowerInvariant()
    $canonical = "$timestamp`n$nonce`n$($Method.ToUpperInvariant())`n$Route`n$bodyHash"
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Key))
    try {
        $signature = [BitConverter]::ToString(
            $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hmac.Dispose()
    }
    return @{
        'X-Broker-Admin-Timestamp' = $timestamp
        'X-Broker-Admin-Nonce' = $nonce
        'X-Broker-Admin-Signature' = $signature
    }
}

$parsedApiUrl = $null
if (![Uri]::TryCreate($ApiUrl, [UriKind]::Absolute, [ref]$parsedApiUrl) -or
    $parsedApiUrl.Scheme -ne [Uri]::UriSchemeHttps -or
    !$parsedApiUrl.IsDefaultPort -or
    ![string]::IsNullOrEmpty($parsedApiUrl.UserInfo)) {
    throw 'The account-import API URL must be a default-port HTTPS URL without embedded credentials.'
}

if (!(Test-Path -LiteralPath $PasswordHashPath -PathType Leaf)) {
    throw "Password hash file was not found: $PasswordHashPath"
}

$document = Get-Content -Raw -LiteralPath $PasswordHashPath | ConvertFrom-Json
if ([int]$document.schema_version -ne 1 -or
    [string]$document.format -ne 'xp-password-hashes-v1' -or
    @($document.entries).Count -eq 0) {
    throw 'The password hash file does not use the expected XP password hash format.'
}

if ($null -eq $AdminKey) {
    $AdminKey = Read-Host 'Broker administrator key' -AsSecureString
}

$keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($AdminKey)
try {
    $plainAdminKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPointer)
    if ([string]::IsNullOrWhiteSpace($plainAdminKey)) {
        throw 'The broker administrator key is required.'
    }

    $requestBody = (Get-Content -Raw -LiteralPath $PasswordHashPath | ConvertFrom-Json | ConvertTo-Json -Depth 32 -Compress)
    $response = Invoke-RestMethod `
        -Method Post `
        -Uri $ApiUrl `
        -ContentType 'application/json' `
        -Headers (New-BrokerAdminHeaders -Method 'POST' -Route '/v1/admin/character-accounts/import' -BodyJson $requestBody -Key $plainAdminKey) `
        -MaximumRedirection 0 `
        -Body $requestBody
    Write-Output "Imported $([int]$response.imported) character password hashes."
}
finally {
    if ($keyPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer)
    }
    $plainAdminKey = $null
}
