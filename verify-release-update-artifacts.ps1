param(
    [string]$PublishArchivePath,
    [string]$InstallerPath,
    [string]$Version,
    [string]$ManifestPath = (Join-Path $PSScriptRoot 'Release\installer\p-assist-updates.json'),
    [string]$SignaturePath = (Join-Path $PSScriptRoot 'Release\installer\p-assist-updates.json.sig'),
    [string]$PublicKeyXmlPath = (Join-Path $PSScriptRoot 'Release\installer\p-assist-updates.public-key.xml'),
    [string]$ExpectedPublicKeyXmlPath,
    [string]$ExpectedSignerSubject = $env:PLAYER_ASSISTANT_RELEASE_SIGNER_SUBJECT,
    [string]$ExpectedSignerThumbprint = $env:PLAYER_ASSISTANT_RELEASE_SIGNER_THUMBPRINT,
    [switch]$RequireCodeSigning
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'version-metadata.ps1')
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-PlayerAssistantVersionMetadata -RepoRoot $PSScriptRoot).Version
}
$defaultInstallerVersion = ($Version -split '[-+]')[0]
if ([string]::IsNullOrWhiteSpace($PublishArchivePath)) {
    $PublishArchivePath = Join-Path $PSScriptRoot "Release\installer\p-assist-$defaultInstallerVersion.zip"
}
if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $PSScriptRoot "Release\installer\p-assist-$defaultInstallerVersion.exe"
}

function Assert-RequiredFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required $Description is missing: $Path"
    }

    if ((Get-Item -LiteralPath $Path).Length -le 0) {
        throw "Required $Description is empty: $Path"
    }
}

function Get-InstallerVersion {
    param([Parameter(Mandatory = $true)][string]$Version)

    if ($Version -match '^(\d+\.\d+\.\d+)') {
        return $Matches[1]
    }

    throw "Version '$Version' does not start with a numeric major.minor.patch segment for release artifact naming."
}

function Get-Sha256Hash {
    param([Parameter(Mandatory = $true)][string]$Path)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        try {
            return ([System.BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '')
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-ManifestEntryFileName {
    param([Parameter(Mandatory = $true)][string]$Value)

    $trimmed = $Value.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        throw 'Manifest URL/file value was empty.'
    }

    $absoluteUri = $null
    if ([System.Uri]::TryCreate($trimmed, [System.UriKind]::Absolute, [ref]$absoluteUri)) {
        return [System.IO.Path]::GetFileName($absoluteUri.LocalPath)
    }

    return [System.IO.Path]::GetFileName($trimmed)
}

function Test-CodeSigningPolicyConfigured {
    return $RequireCodeSigning -or
        ![string]::IsNullOrWhiteSpace($ExpectedSignerSubject) -or
        ![string]::IsNullOrWhiteSpace($ExpectedSignerThumbprint)
}

function Assert-AuthenticodeSignatureMatchesPolicy {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (!(Test-CodeSigningPolicyConfigured)) {
        return
    }

    Assert-RequiredFile -Path $Path -Description $Description
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid') {
        throw "$Description Authenticode signature status '$($signature.Status)' is not valid."
    }

    if ($null -eq $signature.SignerCertificate) {
        throw "$Description is missing an Authenticode signer certificate."
    }

    $actualSubject = [string]$signature.SignerCertificate.Subject
    $actualThumbprint = ([string]$signature.SignerCertificate.Thumbprint).Replace(' ', '').ToUpperInvariant()

    if (![string]::IsNullOrWhiteSpace($ExpectedSignerSubject) -and
        $actualSubject.IndexOf($ExpectedSignerSubject, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "$Description signer subject '$actualSubject' did not contain expected subject '$ExpectedSignerSubject'."
    }

    if (![string]::IsNullOrWhiteSpace($ExpectedSignerThumbprint)) {
        $expectedThumbprint = $ExpectedSignerThumbprint.Replace(' ', '').ToUpperInvariant()
        if ($actualThumbprint -ne $expectedThumbprint) {
            throw "$Description signer thumbprint '$actualThumbprint' did not match expected thumbprint '$expectedThumbprint'."
        }
    }
}

function Assert-ManifestSignature {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$SignaturePath,
        [Parameter(Mandatory = $true)][string]$PublicKeyXmlPath
    )

    Assert-RequiredFile -Path $ManifestPath -Description 'signed update manifest'
    Assert-RequiredFile -Path $SignaturePath -Description 'signed update manifest signature'
    Assert-RequiredFile -Path $PublicKeyXmlPath -Description 'signed update manifest public key'

    $manifestBytes = [System.IO.File]::ReadAllBytes($ManifestPath)
    $signatureBytes = [Convert]::FromBase64String((Get-Content -Raw -LiteralPath $SignaturePath).Trim())
    $publicKeyXml = Get-Content -Raw -LiteralPath $PublicKeyXmlPath

    $rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new()
    $rsa.PersistKeyInCsp = $false
    try {
        $rsa.FromXmlString($publicKeyXml)
        $legacyManifestBytes = [System.Text.Encoding]::UTF8.GetBytes((Get-Content -Raw -LiteralPath $ManifestPath).TrimEnd("`r", "`n", "`t", " "))
        if (!$rsa.VerifyData($manifestBytes, 'SHA256', $signatureBytes) -and !$rsa.VerifyData($legacyManifestBytes, 'SHA256', $signatureBytes)) {
            throw 'Signed update manifest signature verification failed.'
        }
    }
    finally {
        $rsa.Clear()
        $rsa.Dispose()
    }
}

function Assert-PublicKeyMatchesExpected {
    param(
        [Parameter(Mandatory = $true)][string]$ActualPublicKeyXmlPath,
        [Parameter(Mandatory = $true)][string]$ExpectedPublicKeyXmlPath
    )

    Assert-RequiredFile -Path $ExpectedPublicKeyXmlPath -Description 'expected trusted public signing key XML'
    $actual = [System.Security.Cryptography.RSACryptoServiceProvider]::new()
    $expected = [System.Security.Cryptography.RSACryptoServiceProvider]::new()
    $actual.PersistKeyInCsp = $false
    $expected.PersistKeyInCsp = $false
    try {
        $actual.FromXmlString((Get-Content -Raw -LiteralPath $ActualPublicKeyXmlPath))
        $expected.FromXmlString((Get-Content -Raw -LiteralPath $ExpectedPublicKeyXmlPath))
        $actualParameters = $actual.ExportParameters($false)
        $expectedParameters = $expected.ExportParameters($false)
        $actualModulus = [Convert]::ToBase64String($actualParameters.Modulus)
        $expectedModulus = [Convert]::ToBase64String($expectedParameters.Modulus)
        $actualExponent = [Convert]::ToBase64String($actualParameters.Exponent)
        $expectedExponent = [Convert]::ToBase64String($expectedParameters.Exponent)
        if (($actualModulus -ne $expectedModulus) -or ($actualExponent -ne $expectedExponent)) {
            throw 'The emitted update-manifest public key does not match the configured trusted public key.'
        }
    }
    finally {
        $actual.Clear(); $actual.Dispose(); $expected.Clear(); $expected.Dispose()
    }
}

function Assert-ReleaseArchiveVersion {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion
    )

    $scratchDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("player-assistant-release-archive-verify-{0}" -f ([Guid]::NewGuid().ToString('N')))
    try {
        New-Item -ItemType Directory -Force -Path $scratchDirectory | Out-Null
        Expand-Archive -LiteralPath $ArchivePath -DestinationPath $scratchDirectory -Force

        $payloadExecutable = Get-ChildItem -LiteralPath $scratchDirectory -Filter 'player-assistant.exe' -File -Recurse | Select-Object -First 1
        if ($null -eq $payloadExecutable) {
            throw "Release update archive '$ArchivePath' did not contain player-assistant.exe."
        }

        $payloadManifest = Get-ChildItem -LiteralPath $scratchDirectory -Filter 'release-manifest.json' -File -Recurse | Select-Object -First 1
        if ($null -eq $payloadManifest) {
            throw "Release update archive '$ArchivePath' did not contain release-manifest.json."
        }

        $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($payloadExecutable.FullName)
        if ($versionInfo.ProductVersion -ne $ExpectedVersion) {
            throw "Release update archive payload ProductVersion '$($versionInfo.ProductVersion)' did not match expected version '$ExpectedVersion'."
        }

        $manifest = Get-Content -Raw -LiteralPath $payloadManifest.FullName | ConvertFrom-Json
        if ([string]$manifest.app_version -ne $ExpectedVersion) {
            throw "Release update archive release-manifest.json app_version '$($manifest.app_version)' did not match expected version '$ExpectedVersion'."
        }
    }
    finally {
        if (Test-Path -LiteralPath $scratchDirectory) {
            Remove-Item -LiteralPath $scratchDirectory -Recurse -Force
        }
    }
}

$resolvedPublishArchivePath = [System.IO.Path]::GetFullPath($PublishArchivePath)
$resolvedInstallerPath = [System.IO.Path]::GetFullPath($InstallerPath)
$resolvedManifestPath = [System.IO.Path]::GetFullPath($ManifestPath)
$resolvedSignaturePath = [System.IO.Path]::GetFullPath($SignaturePath)
$resolvedPublicKeyXmlPath = [System.IO.Path]::GetFullPath($PublicKeyXmlPath)

Assert-RequiredFile -Path $resolvedPublishArchivePath -Description 'release update archive'
Assert-RequiredFile -Path $resolvedInstallerPath -Description 'release installer executable'
Assert-ManifestSignature -ManifestPath $resolvedManifestPath -SignaturePath $resolvedSignaturePath -PublicKeyXmlPath $resolvedPublicKeyXmlPath
if (![string]::IsNullOrWhiteSpace($ExpectedPublicKeyXmlPath)) {
    Assert-PublicKeyMatchesExpected -ActualPublicKeyXmlPath $resolvedPublicKeyXmlPath -ExpectedPublicKeyXmlPath ([System.IO.Path]::GetFullPath($ExpectedPublicKeyXmlPath))
}

$installerVersion = Get-InstallerVersion -Version $Version
$expectedArchiveName = "p-assist-$installerVersion.zip"
$expectedInstallerName = "p-assist-$installerVersion.exe"
if ([System.IO.Path]::GetFileName($resolvedPublishArchivePath) -ne $expectedArchiveName) {
    throw "Release update archive file name '$([System.IO.Path]::GetFileName($resolvedPublishArchivePath))' did not match expected '$expectedArchiveName'."
}

if ([System.IO.Path]::GetFileName($resolvedInstallerPath) -ne $expectedInstallerName) {
    throw "Release installer executable file name '$([System.IO.Path]::GetFileName($resolvedInstallerPath))' did not match expected '$expectedInstallerName'."
}

$manifest = Get-Content -Raw -LiteralPath $resolvedManifestPath | ConvertFrom-Json
if ($manifest.schema_version -ne 1) {
    throw "Signed update manifest schema_version '$($manifest.schema_version)' is not supported."
}

$entries = @($manifest.updates)
$entry = @($entries | Where-Object { [string]$_.version -eq $Version } | Select-Object -First 1)
if ($entry.Count -eq 0) {
    throw "Signed update manifest did not contain an entry for version '$Version'."
}

$entryArchiveName = Get-ManifestEntryFileName -Value ([string]$entry[0].url)
$entryInstallerName = Get-ManifestEntryFileName -Value ([string]$entry[0].installer_url)
if ($entryArchiveName -ne $expectedArchiveName) {
    throw "Signed update manifest archive file name '$entryArchiveName' did not match expected '$expectedArchiveName'."
}

if ($entryInstallerName -ne $expectedInstallerName) {
    throw "Signed update manifest installer file name '$entryInstallerName' did not match expected '$expectedInstallerName'."
}

$archiveSha256 = Get-Sha256Hash -Path $resolvedPublishArchivePath
$installerSha256 = Get-Sha256Hash -Path $resolvedInstallerPath
if ($archiveSha256 -ne [string]$entry[0].sha256) {
    throw "Signed update manifest archive SHA256 '$($entry[0].sha256)' did not match produced archive SHA256 '$archiveSha256'."
}

if ($installerSha256 -ne [string]$entry[0].installer_sha256) {
    throw "Signed update manifest installer SHA256 '$($entry[0].installer_sha256)' did not match produced installer SHA256 '$installerSha256'."
}

$installerVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($resolvedInstallerPath)
$installerProductVersion = ([string]$installerVersionInfo.ProductVersion).Trim()
if ($installerProductVersion -ne $Version) {
    throw "Release installer executable ProductVersion '$installerProductVersion' did not match expected version '$Version'."
}

Assert-AuthenticodeSignatureMatchesPolicy -Path $resolvedInstallerPath -Description 'Release installer executable'
Assert-ReleaseArchiveVersion -ArchivePath $resolvedPublishArchivePath -ExpectedVersion $Version

Write-Output "Release update artifacts verification passed: $resolvedPublishArchivePath and $resolvedInstallerPath"
