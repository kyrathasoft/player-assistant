param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'Release\installer'),
    [string]$PublishDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [string]$Version,
    [string]$ManifestBaseUri = 'https://bryanmiller.us/scarlethorizons/',
    [string]$InstallerPath,
    [string]$ArchivePath,
    [string]$ManifestPath,
    [string]$SignaturePath,
    [string]$PublicKeyXmlPath,
    [string]$PrivateKeyXmlPath,
    [string]$ExpectedPublicKeyXmlPath,
    [switch]$GenerateEphemeralSigningKey,
    [ValidateSet('', 'archive', 'manifest', 'signature', 'public-key', 'promotion')]
    [string]$FaultAfterStep = ''
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'version-metadata.ps1')
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-PlayerAssistantVersionMetadata -RepoRoot $PSScriptRoot).Version
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

function Assert-SigningKeyMatchesExpected {
    param(
        [Parameter(Mandatory = $true)][System.Security.Cryptography.RSACryptoServiceProvider]$SigningKey,
        [Parameter(Mandatory = $true)][string]$ExpectedPublicKeyXmlPath
    )

    Assert-RequiredFile -Path $ExpectedPublicKeyXmlPath -Description 'expected trusted public signing key XML'
    $expected = [System.Security.Cryptography.RSACryptoServiceProvider]::new()
    $expected.PersistKeyInCsp = $false
    try {
        $expected.FromXmlString((Get-Content -Raw -LiteralPath $ExpectedPublicKeyXmlPath))
        $actualParameters = $SigningKey.ExportParameters($false)
        $expectedParameters = $expected.ExportParameters($false)
        $actualModulus = [Convert]::ToBase64String($actualParameters.Modulus)
        $expectedModulus = [Convert]::ToBase64String($expectedParameters.Modulus)
        $actualExponent = [Convert]::ToBase64String($actualParameters.Exponent)
        $expectedExponent = [Convert]::ToBase64String($expectedParameters.Exponent)
        if (($actualModulus -ne $expectedModulus) -or ($actualExponent -ne $expectedExponent)) {
            throw 'The configured private signing key does not match the expected trusted public signing key.'
        }
    }
    finally {
        $expected.Clear()
        $expected.Dispose()
    }
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
$finalOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$resolvedOutputDir = Join-Path $finalOutputDir ('.generation-{0}' -f ([Guid]::NewGuid().ToString('N')))
$resolvedPublishDir = [System.IO.Path]::GetFullPath($PublishDir)

$sourceInstallerPath = if (![string]::IsNullOrWhiteSpace($InstallerPath)) {
    [System.IO.Path]::GetFullPath($InstallerPath)
}
else {
    Join-Path $finalOutputDir "p-assist-$installerVersion.exe"
}

$resolvedInstallerPath = Join-Path $resolvedOutputDir "p-assist-$installerVersion.exe"

$archiveFileName = if (![string]::IsNullOrWhiteSpace($ArchivePath)) { [System.IO.Path]::GetFileName($ArchivePath) } else { "p-assist-$installerVersion.zip" }
$manifestFileName = if (![string]::IsNullOrWhiteSpace($ManifestPath)) { [System.IO.Path]::GetFileName($ManifestPath) } else { 'p-assist-updates.json' }
$signatureFileName = if (![string]::IsNullOrWhiteSpace($SignaturePath)) { [System.IO.Path]::GetFileName($SignaturePath) } else { 'p-assist-updates.json.sig' }
$publicKeyFileName = if (![string]::IsNullOrWhiteSpace($PublicKeyXmlPath)) { [System.IO.Path]::GetFileName($PublicKeyXmlPath) } else { 'p-assist-updates.public-key.xml' }
$resolvedArchivePath = Join-Path $resolvedOutputDir $archiveFileName
$resolvedManifestPath = Join-Path $resolvedOutputDir $manifestFileName
$resolvedSignaturePath = Join-Path $resolvedOutputDir $signatureFileName
$resolvedPublicKeyXmlPath = Join-Path $resolvedOutputDir $publicKeyFileName

Assert-RequiredDirectory -Path $resolvedPublishDir -Description 'publish directory'
Assert-RequiredFile -Path $sourceInstallerPath -Description 'release installer executable'
New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null
Copy-Item -LiteralPath $sourceInstallerPath -Destination $resolvedInstallerPath -Force

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
if ($FaultAfterStep -eq 'archive') { throw 'Injected release generation failure after archive.' }

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
if ($FaultAfterStep -eq 'manifest') { throw 'Injected release generation failure after manifest.' }

if (![string]::IsNullOrWhiteSpace($ExpectedPublicKeyXmlPath) -and $GenerateEphemeralSigningKey) {
    throw 'An expected trusted public key cannot be combined with ephemeral signing.'
}
$rsa = Get-SigningKey -PrivateKeyXmlPath $PrivateKeyXmlPath -GenerateEphemeralSigningKey:$GenerateEphemeralSigningKey
try {
    if (![string]::IsNullOrWhiteSpace($ExpectedPublicKeyXmlPath)) {
        Assert-SigningKeyMatchesExpected -SigningKey $rsa -ExpectedPublicKeyXmlPath $ExpectedPublicKeyXmlPath
    }
    $manifestBytes = [System.IO.File]::ReadAllBytes($resolvedManifestPath)
    $signatureBytes = $rsa.SignData($manifestBytes, 'SHA256')
    [System.IO.File]::WriteAllText($resolvedSignaturePath, [Convert]::ToBase64String($signatureBytes) + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($resolvedPublicKeyXmlPath, $rsa.ToXmlString($false), [System.Text.UTF8Encoding]::new($false))
}
finally {
    $rsa.Clear()
    $rsa.Dispose()
}

if ($FaultAfterStep -eq 'signature') { throw 'Injected release generation failure after signature.' }
if ($FaultAfterStep -eq 'public-key') { throw 'Injected release generation failure after public key.' }

$artifactNames = @(
    [System.IO.Path]::GetFileName($resolvedArchivePath),
    [System.IO.Path]::GetFileName($resolvedInstallerPath),
    [System.IO.Path]::GetFileName($resolvedManifestPath),
    [System.IO.Path]::GetFileName($resolvedSignaturePath),
    [System.IO.Path]::GetFileName($resolvedPublicKeyXmlPath)
)
$journalPath = Join-Path $finalOutputDir 'release-update-generation.journal.json'
$backupRoot = Join-Path $finalOutputDir ('.rollback-{0}' -f ([Guid]::NewGuid().ToString('N')))
$journal = [ordered]@{ schema_version = 1; state = 'promoting'; generation = $resolvedOutputDir; artifacts = $artifactNames }
New-Item -ItemType Directory -Force -Path $finalOutputDir, $backupRoot | Out-Null
[System.IO.File]::WriteAllText($journalPath, ($journal | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
try {
    foreach ($name in $artifactNames) {
        $target = Join-Path $finalOutputDir $name
        $backup = Join-Path $backupRoot $name
        if (Test-Path -LiteralPath $target -PathType Leaf) { Move-Item -LiteralPath $target -Destination $backup -Force }
        Move-Item -LiteralPath (Join-Path $resolvedOutputDir $name) -Destination $target -Force
    }
    if ($FaultAfterStep -eq 'promotion') { throw 'Injected release generation failure during promotion.' }
    $journal.state = 'committed'
    [System.IO.File]::WriteAllText($journalPath, ($journal | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
    Remove-Item -LiteralPath $backupRoot -Recurse -Force
    Remove-Item -LiteralPath $journalPath -Force
}
catch {
    foreach ($name in $artifactNames) {
        $target = Join-Path $finalOutputDir $name
        $backup = Join-Path $backupRoot $name
        if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
        if (Test-Path -LiteralPath $backup -PathType Leaf) { Move-Item -LiteralPath $backup -Destination $target -Force }
    }
    $journal.state = 'rolled_back'
    [System.IO.File]::WriteAllText($journalPath, ($journal | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
    Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
    throw
}
Remove-Item -LiteralPath $resolvedOutputDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Output "Release update archive created: $(Join-Path $finalOutputDir ([System.IO.Path]::GetFileName($resolvedArchivePath)))"
Write-Output "Signed update manifest created: $(Join-Path $finalOutputDir ([System.IO.Path]::GetFileName($resolvedManifestPath)))"
Write-Output "Update manifest signature created: $(Join-Path $finalOutputDir ([System.IO.Path]::GetFileName($resolvedSignaturePath)))"
Write-Output "Update manifest public key created: $(Join-Path $finalOutputDir ([System.IO.Path]::GetFileName($resolvedPublicKeyXmlPath)))"
