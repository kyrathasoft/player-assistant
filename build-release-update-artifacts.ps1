param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'Release\installer'),
    [string]$PublishDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [string]$Version = '0.9.4',
    [string]$ManifestBaseUri = 'https://bryanmiller.us/scarlethorizons/',
    [string]$InstallerPath,
    [string]$ArchivePath,
    [string]$ManifestPath,
    [string]$SignaturePath,
    [string]$PublicKeyXmlPath,
    [string]$PrivateKeyXmlPath,
    [switch]$GenerateEphemeralSigningKey
)

$ErrorActionPreference = 'Stop'

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

function Assert-RequiredDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (!(Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Required $Description is missing: $Path"
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

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

function Resolve-ArtifactUrl {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUri,
        [Parameter(Mandatory = $true)][string]$FileName
    )

    $base = if ($BaseUri.EndsWith('/')) { $BaseUri } else { "$BaseUri/" }
    return ([System.Uri]::new([System.Uri]$base, $FileName)).AbsoluteUri
}

function New-SigningKeyPair {
    $rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new(2048)
    $rsa.PersistKeyInCsp = $false
    return $rsa
}

function Get-SigningKey {
    param(
        [string]$PrivateKeyXmlPath,
        [switch]$GenerateEphemeralSigningKey
    )

    if (![string]::IsNullOrWhiteSpace($PrivateKeyXmlPath)) {
        Assert-RequiredFile -Path $PrivateKeyXmlPath -Description 'private signing key XML'
        $rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new()
        $rsa.PersistKeyInCsp = $false
        $rsa.FromXmlString((Get-Content -Raw -LiteralPath $PrivateKeyXmlPath))
        return $rsa
    }

    if ($GenerateEphemeralSigningKey) {
        return New-SigningKeyPair
    }

    throw 'A private signing key XML path is required unless -GenerateEphemeralSigningKey is supplied.'
}

$installerVersion = Get-InstallerVersion -Version $Version
$resolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$resolvedPublishDir = [System.IO.Path]::GetFullPath($PublishDir)

$resolvedInstallerPath = if (![string]::IsNullOrWhiteSpace($InstallerPath)) {
    [System.IO.Path]::GetFullPath($InstallerPath)
}
else {
    Join-Path $resolvedOutputDir "p-assist-$installerVersion.exe"
}

$resolvedArchivePath = if (![string]::IsNullOrWhiteSpace($ArchivePath)) {
    [System.IO.Path]::GetFullPath($ArchivePath)
}
else {
    Join-Path $resolvedOutputDir "p-assist-$installerVersion.zip"
}

$resolvedManifestPath = if (![string]::IsNullOrWhiteSpace($ManifestPath)) {
    [System.IO.Path]::GetFullPath($ManifestPath)
}
else {
    Join-Path $resolvedOutputDir 'p-assist-updates.json'
}

$resolvedSignaturePath = if (![string]::IsNullOrWhiteSpace($SignaturePath)) {
    [System.IO.Path]::GetFullPath($SignaturePath)
}
else {
    Join-Path $resolvedOutputDir 'p-assist-updates.json.sig'
}

$resolvedPublicKeyXmlPath = if (![string]::IsNullOrWhiteSpace($PublicKeyXmlPath)) {
    [System.IO.Path]::GetFullPath($PublicKeyXmlPath)
}
else {
    Join-Path $resolvedOutputDir 'p-assist-updates.public-key.xml'
}

Assert-RequiredDirectory -Path $resolvedPublishDir -Description 'publish directory'
Assert-RequiredFile -Path $resolvedInstallerPath -Description 'release installer executable'
New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null

if (Test-Path -LiteralPath $resolvedArchivePath) {
    Remove-Item -LiteralPath $resolvedArchivePath -Force
}

$archiveStagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("player-assistant-release-archive-{0}" -f ([Guid]::NewGuid().ToString('N')))
try {
    New-Item -ItemType Directory -Force -Path $archiveStagingRoot | Out-Null
    $archiveRoot = Join-Path $archiveStagingRoot "player-assistant-$Version"
    Copy-DirectoryContents -Source $resolvedPublishDir -Destination $archiveRoot
    Compress-Archive -LiteralPath $archiveRoot -DestinationPath $resolvedArchivePath -Force
}
finally {
    if (Test-Path -LiteralPath $archiveStagingRoot) {
        Remove-Item -LiteralPath $archiveStagingRoot -Recurse -Force
    }
}

Assert-RequiredFile -Path $resolvedArchivePath -Description 'release update archive'

$archiveFileName = [System.IO.Path]::GetFileName($resolvedArchivePath)
$installerFileName = [System.IO.Path]::GetFileName($resolvedInstallerPath)
$manifest = [ordered]@{
    schema_version = 1
    updates = @(
        [ordered]@{
            version = $Version
            url = Resolve-ArtifactUrl -BaseUri $ManifestBaseUri -FileName $archiveFileName
            sha256 = Get-Sha256Hash -Path $resolvedArchivePath
            installer_url = Resolve-ArtifactUrl -BaseUri $ManifestBaseUri -FileName $installerFileName
            installer_sha256 = Get-Sha256Hash -Path $resolvedInstallerPath
        }
    )
}

$manifestJson = $manifest | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($resolvedManifestPath, $manifestJson + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

$rsa = Get-SigningKey -PrivateKeyXmlPath $PrivateKeyXmlPath -GenerateEphemeralSigningKey:$GenerateEphemeralSigningKey
try {
    $manifestBytes = [System.Text.Encoding]::UTF8.GetBytes($manifestJson)
    $signatureBytes = $rsa.SignData($manifestBytes, 'SHA256')
    [System.IO.File]::WriteAllText($resolvedSignaturePath, [Convert]::ToBase64String($signatureBytes) + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($resolvedPublicKeyXmlPath, $rsa.ToXmlString($false), [System.Text.UTF8Encoding]::new($false))
}
finally {
    $rsa.Clear()
    $rsa.Dispose()
}

Write-Output "Release update archive created: $resolvedArchivePath"
Write-Output "Signed update manifest created: $resolvedManifestPath"
Write-Output "Update manifest signature created: $resolvedSignaturePath"
Write-Output "Update manifest public key created: $resolvedPublicKeyXmlPath"
