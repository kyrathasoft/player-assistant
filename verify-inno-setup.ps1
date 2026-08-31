[CmdletBinding()]
param(
    [string]$CompilerPath,

    [string]$PackagePath,
    [switch]$PackageOnly,

    [string]$ExpectedVersion = '7.1.0',
    [string]$ExpectedPackageSha256 = '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F',
    [string]$ExpectedCompilerSha256 = 'D06EBD38F38E3CEE60A3C50CC45BD449D77E0BC6A5CABC607EA9886808E4DE1A',
    [string]$ExpectedSignerSubject = 'CN=Pyrsys B.V., O=Pyrsys B.V., S=Noord-Holland, C=NL',
    [string]$ExpectedSignerThumbprint = 'E0AB19C8D38CBF9C44709925122A7A02F8C70CB7'
)

$ErrorActionPreference = 'Stop'

function Normalize-Sha256([string]$Value) {
    return ($Value -replace '\s', '').ToUpperInvariant()
}

function Get-Sha256HashText {
    param([string]$Path)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        return ([System.BitConverter]::ToString($algorithm.ComputeHash($stream)) -replace '-', '')
    }
    finally {
        $stream.Dispose()
        $algorithm.Dispose()
    }
}

function Assert-ExactHash {
    param([string]$Path, [string]$Expected, [string]$Description)
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Description is missing: $Path" }
    $actual = Get-Sha256HashText -Path $Path
    if ((Normalize-Sha256 $actual) -ne (Normalize-Sha256 $Expected)) {
        throw "$Description SHA256 '$actual' did not match approved SHA256 '$Expected'."
    }
}

function Assert-ExactAuthenticode {
    param([string]$Path, [string]$Description)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid') { throw "$Description Authenticode status '$($signature.Status)' is not valid." }
    if ($null -eq $signature.SignerCertificate) { throw "$Description has no Authenticode signer certificate." }
    $subject = [string]$signature.SignerCertificate.Subject
    $thumbprint = Normalize-Sha256 ([string]$signature.SignerCertificate.Thumbprint)
    if ($subject -ne $ExpectedSignerSubject) { throw "$Description signer subject '$subject' did not match approved subject '$ExpectedSignerSubject'." }
    if ($thumbprint -ne (Normalize-Sha256 $ExpectedSignerThumbprint)) { throw "$Description signer thumbprint '$thumbprint' did not match approved thumbprint '$ExpectedSignerThumbprint'." }
}

if (![string]::IsNullOrWhiteSpace($PackagePath)) {
    Assert-ExactHash -Path $PackagePath -Expected $ExpectedPackageSha256 -Description 'Inno Setup package'
    Assert-ExactAuthenticode -Path $PackagePath -Description 'Inno Setup package'
}
if ($PackageOnly) {
    Write-Output 'Inno Setup package attestation passed.'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($CompilerPath)) { throw 'CompilerPath is required unless PackageOnly is specified.' }
$CompilerPath = [System.IO.Path]::GetFullPath($CompilerPath)
if (!(Test-Path -LiteralPath $CompilerPath -PathType Leaf)) { throw "Approved Inno Setup compiler is missing: $CompilerPath" }

$versionInfo = (Get-Item -LiteralPath $CompilerPath).VersionInfo
$actualVersion = [string]$versionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($actualVersion) -or $actualVersion -eq '0.0.0.0') { $actualVersion = [string]$versionInfo.FileVersion }
if ([string]::IsNullOrWhiteSpace($actualVersion) -or $actualVersion -eq '0.0.0.0') {
    foreach ($registryPath in @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1')) {
        if (Test-Path -LiteralPath $registryPath) {
            $actualVersion = [string](Get-ItemProperty -LiteralPath $registryPath -Name DisplayVersion -ErrorAction SilentlyContinue).DisplayVersion
            if (![string]::IsNullOrWhiteSpace($actualVersion)) { break }
        }
    }
}
if ($actualVersion -notmatch "^$([regex]::Escape($ExpectedVersion))(?:\.\d+)?$") {
    throw "Inno Setup compiler version '$actualVersion' did not match approved version '$ExpectedVersion'."
}

Assert-ExactHash -Path $CompilerPath -Expected $ExpectedCompilerSha256 -Description 'Inno Setup compiler'
Assert-ExactAuthenticode -Path $CompilerPath -Description 'Inno Setup compiler'

[ordered]@{
    tool = 'Inno Setup'
    edition = 'x64'
    version = $ExpectedVersion
    source = 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe'
    package_sha256 = (Normalize-Sha256 $ExpectedPackageSha256)
    compiler_sha256 = (Normalize-Sha256 $ExpectedCompilerSha256)
    signer_subject = $ExpectedSignerSubject
    signer_thumbprint = (Normalize-Sha256 $ExpectedSignerThumbprint)
    compiler_path = [System.IO.Path]::GetFullPath($CompilerPath)
} | ConvertTo-Json -Compress
