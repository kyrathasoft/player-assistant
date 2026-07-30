param(
    [string]$PhpPath = 'C:\php-8.4.23-Win32-vs17-x64\php.exe',
    [string]$CredentialTarget = 'PlayerAssistant/WordCounts/SigningPrivateKey',
    [string]$KeyId = 'word-count-2026-01',
    [string]$PublicMetadataPath = (Join-Path $PSScriptRoot 'word-count-signing-public.json'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'word-count-publishing.ps1')

if (-not $Force -and (Test-Path -LiteralPath $PublicMetadataPath)) {
    [void](Get-WordCountCredentialSecret -TargetName $CredentialTarget)
    Write-Output "Word-count signing key already configured: $KeyId"
    exit 0
}

$keyJson = & $PhpPath -r '$pair=sodium_crypto_sign_keypair(); echo json_encode(["private_key"=>base64_encode(sodium_crypto_sign_secretkey($pair)),"public_key"=>base64_encode(sodium_crypto_sign_publickey($pair))]);'
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to generate the Ed25519 word-count signing key.'
}
$key = $keyJson | ConvertFrom-Json
Set-WordCountCredentialSecret -TargetName $CredentialTarget -Secret ([string]$key.private_key)

$metadata = [ordered]@{
    algorithm = 'Ed25519'
    key_id = $KeyId
    public_key = [string]$key.public_key
}
[IO.File]::WriteAllText(
    [IO.Path]::GetFullPath($PublicMetadataPath),
    ($metadata | ConvertTo-Json -Depth 3),
    [Text.UTF8Encoding]::new($false))

$key.private_key = $null
Write-Output "Word-count signing key stored in Windows Credential Manager: $CredentialTarget"
Write-Output "Public metadata: $([IO.Path]::GetFullPath($PublicMetadataPath))"
