param(
    [string]$PasswordHashPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'xp-passwords.json'),
    [string]$ApiUrl = 'https://bryanmiller.us/scarlethorizons/api/v1/admin/character-accounts/import',
    [Security.SecureString]$AdminKey
)

$ErrorActionPreference = 'Stop'

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

    $response = Invoke-RestMethod `
        -Method Post `
        -Uri $ApiUrl `
        -ContentType 'application/json' `
        -Headers @{ 'X-Broker-Admin-Key' = $plainAdminKey } `
        -MaximumRedirection 0 `
        -Body (Get-Content -Raw -LiteralPath $PasswordHashPath)
    Write-Output "Imported $([int]$response.imported) character password hashes."
}
finally {
    if ($keyPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer)
    }
    $plainAdminKey = $null
}
