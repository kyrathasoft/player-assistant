param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'Release\installer'),
    [string]$PublishDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [string]$Version,
    [string]$InnoCompilerPath,
    [string]$ExpectedSignerSubject = $env:PLAYER_ASSISTANT_RELEASE_SIGNER_SUBJECT,
    [string]$ExpectedSignerThumbprint = $env:PLAYER_ASSISTANT_RELEASE_SIGNER_THUMBPRINT,
    [switch]$RequireCodeSigning,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'version-metadata.ps1')
$versionMetadata = Get-PlayerAssistantVersionMetadata -RepoRoot $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $versionMetadata.Version
}

$SettingsEncryptionSeed = 'PlayerAssistant.LocalSettings.v1'
$SettingsSchemaVersion = 1
$SettingsLocalFileName = 'settings.local.json'
$XpPasswordFileName = 'xp-passwords.json'
$PackageRootName = "player-assistant-$Version"
$PackageFileName = "player-assistant-$Version-installer.zip"
$InnoScriptPath = Join-Path $PSScriptRoot 'Installer\player-assistant.iss'

function Get-InstallerVersion {
    param([Parameter(Mandatory = $true)][string]$Version)

    if ($Version -match '^(\d+\.\d+\.\d+)') {
        return $Matches[1]
    }

    throw "Version '$Version' does not start with a numeric major.minor.patch segment for installer naming."
}

$InstallerVersion = Get-InstallerVersion -Version $Version
$InnoOutputFileName = "p-assist-$InstallerVersion.exe"

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

function Protect-RuntimeSidecarFiles {
    param([Parameter(Mandatory = $true)][string]$Directory)

    foreach ($fileName in @($XpPasswordFileName, $SettingsLocalFileName)) {
        $path = Join-Path $Directory $fileName
        Assert-RequiredFile -Path $path -Description "installer payload runtime sidecar $fileName"
        Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $true
    }
}

function Get-Sha256Bytes {
    param([Parameter(Mandatory = $true)][string]$Value)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ,$sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Value))
    }
    finally {
        $sha256.Dispose()
    }
}

function Test-FixedTimeEquals {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Left,
        [Parameter(Mandatory = $true)][byte[]]$Right
    )

    if ($Left.Length -ne $Right.Length) {
        return $false
    }

    [byte]$difference = 0
    for ($index = 0; $index -lt $Left.Length; $index++) {
        $difference = $difference -bor ($Left[$index] -bxor $Right[$index])
    }

    return $difference -eq 0
}

function ConvertTo-PlainSettingsObject {
    param([Parameter(Mandatory = $true)][object]$Settings)

    $plainSettings = [ordered]@{}
    foreach ($property in $Settings.PSObject.Properties) {
        if ($property.Name -eq 'schema_version') {
            continue
        }

        $plainSettings[$property.Name] = [string]$property.Value
    }

    return [pscustomobject]$plainSettings
}

function Get-SettingsDerivationScope {
    param([Parameter(Mandatory = $true)][string]$SettingsPath)

    $fullPath = [System.IO.Path]::GetFullPath($SettingsPath)
    $directoryPath = [System.IO.Path]::GetDirectoryName($fullPath)
    if ([string]::IsNullOrWhiteSpace($directoryPath)) {
        $directoryPath = $PSScriptRoot
    }

    $installPath = [System.IO.Path]::GetFullPath($directoryPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar).ToUpperInvariant()
    $machine = [Environment]::MachineName.ToUpperInvariant()
    $user = "$([Environment]::UserDomainName)\$([Environment]::UserName)".ToUpperInvariant()
    return "$machine|$user|$installPath"
}

function ConvertFrom-EncryptedSettingsFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $raw = Get-Content -Raw -LiteralPath $Path
    $envelope = $raw | ConvertFrom-Json
    $format = [string]$envelope.format
    $payloadBytes = [Convert]::FromBase64String([string]$envelope.payload)
    $encryptionKey = $null
    $authenticationKey = $null

    if ($format -eq 'app-protected-v3') {
        $scope = Get-SettingsDerivationScope -SettingsPath $Path
        $encryptionKey = Get-Sha256Bytes -Value "$SettingsEncryptionSeed.v3.encryption.$scope"
        $authenticationKey = Get-Sha256Bytes -Value "$SettingsEncryptionSeed.v3.hmac.$scope"
    }
    elseif ($format -eq 'app-protected-v2') {
        $encryptionKey = Get-Sha256Bytes -Value $SettingsEncryptionSeed
        $authenticationKey = Get-Sha256Bytes -Value "$SettingsEncryptionSeed.hmac"
    }
    elseif ($format -eq 'app-protected-v1') {
        $encryptionKey = Get-Sha256Bytes -Value $SettingsEncryptionSeed
    }
    else {
        throw "Unsupported settings encryption format '$format' for installer packaging."
    }

    if ($format -eq 'app-protected-v3' -or $format -eq 'app-protected-v2') {
        if ($payloadBytes.Length -lt 49) {
            throw 'Encrypted settings payload is too short.'
        }

        $tag = [byte[]]::new(32)
        $protectedContent = [byte[]]::new($payloadBytes.Length - $tag.Length)
        [System.Buffer]::BlockCopy($payloadBytes, 0, $protectedContent, 0, $protectedContent.Length)
        [System.Buffer]::BlockCopy($payloadBytes, $protectedContent.Length, $tag, 0, $tag.Length)
        $hmac = [System.Security.Cryptography.HMACSHA256]::new($authenticationKey)
        try {
            $actualTag = $hmac.ComputeHash($protectedContent)
        }
        finally {
            $hmac.Dispose()
        }

        if (!(Test-FixedTimeEquals -Left $actualTag -Right $tag)) {
            throw 'Encrypted settings authentication tag did not match.'
        }
    }
    else {
        $protectedContent = $payloadBytes
    }

    $iv = [byte[]]::new(16)
    $ciphertext = [byte[]]::new($protectedContent.Length - $iv.Length)
    [System.Buffer]::BlockCopy($protectedContent, 0, $iv, 0, $iv.Length)
    [System.Buffer]::BlockCopy($protectedContent, $iv.Length, $ciphertext, 0, $ciphertext.Length)

    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.Key = $encryptionKey
        $aes.IV = $iv
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $decryptor = $aes.CreateDecryptor()
        try {
            $plaintextBytes = $decryptor.TransformFinalBlock($ciphertext, 0, $ciphertext.Length)
        }
        finally {
            $decryptor.Dispose()
        }
    }
    finally {
        $aes.Dispose()
    }

    return ConvertTo-PlainSettingsObject -Settings (([System.Text.Encoding]::UTF8.GetString($plaintextBytes)) | ConvertFrom-Json)
}

function ConvertFrom-SettingsFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $raw = Get-Content -Raw -LiteralPath $Path
    $json = $raw | ConvertFrom-Json
    if ($json.PSObject.Properties['format'] -and $json.PSObject.Properties['payload']) {
        return ConvertFrom-EncryptedSettingsFile -Path $Path
    }

    return ConvertTo-PlainSettingsObject -Settings $json
}

function Write-PortableEncryptedSettings {
    param(
        [Parameter(Mandatory = $true)][object]$Settings,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $plaintextJson = $Settings | ConvertTo-Json -Depth 10
    $plaintextBytes = [System.Text.Encoding]::UTF8.GetBytes($plaintextJson)
    $iv = [byte[]]::new(16)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($iv)
    }
    finally {
        $rng.Dispose()
    }

    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.Key = Get-Sha256Bytes -Value $SettingsEncryptionSeed
        $aes.IV = $iv
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $encryptor = $aes.CreateEncryptor()
        try {
            $ciphertext = $encryptor.TransformFinalBlock($plaintextBytes, 0, $plaintextBytes.Length)
        }
        finally {
            $encryptor.Dispose()
        }
    }
    finally {
        $aes.Dispose()
    }

    $protectedContent = [byte[]]::new($iv.Length + $ciphertext.Length)
    [System.Buffer]::BlockCopy($iv, 0, $protectedContent, 0, $iv.Length)
    [System.Buffer]::BlockCopy($ciphertext, 0, $protectedContent, $iv.Length, $ciphertext.Length)
    $hmac = [System.Security.Cryptography.HMACSHA256]::new((Get-Sha256Bytes -Value "$SettingsEncryptionSeed.hmac"))
    try {
        $tag = $hmac.ComputeHash($protectedContent)
    }
    finally {
        $hmac.Dispose()
    }

    $payloadBytes = [byte[]]::new($protectedContent.Length + $tag.Length)
    [System.Buffer]::BlockCopy($protectedContent, 0, $payloadBytes, 0, $protectedContent.Length)
    [System.Buffer]::BlockCopy($tag, 0, $payloadBytes, $protectedContent.Length, $tag.Length)

    $envelope = [ordered]@{
        schema_version = $SettingsSchemaVersion
        format = 'app-protected-v2'
        payload = [Convert]::ToBase64String($payloadBytes)
    }

    [System.IO.File]::WriteAllText(
        $DestinationPath,
        ([pscustomobject]$envelope | ConvertTo-Json -Depth 4),
        [System.Text.UTF8Encoding]::new($false))
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

function Resolve-InnoCompilerPath {
    param([string]$RequestedPath)

    if (![string]::IsNullOrWhiteSpace($RequestedPath)) {
        if (!(Test-Path -LiteralPath $RequestedPath -PathType Leaf)) {
            throw "Inno Setup compiler was not found: $RequestedPath"
        }

        return [System.IO.Path]::GetFullPath($RequestedPath)
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidatePaths = @(
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )

    foreach ($candidatePath in $candidatePaths) {
        if (![string]::IsNullOrWhiteSpace($candidatePath) -and (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
            return [System.IO.Path]::GetFullPath($candidatePath)
        }
    }

    return $null
}

function Test-CodeSigningPolicyConfigured {
    return $RequireCodeSigning -or
        ![string]::IsNullOrWhiteSpace($ExpectedSignerSubject) -or
        ![string]::IsNullOrWhiteSpace($ExpectedSignerThumbprint)
}

function Assert-AuthenticodeSignatureMatchesPolicy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
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

if (!$SkipPublish) {
    $publishArguments = @{
        OutputDir = $PublishDir
    }
    if ($RequireCodeSigning) {
        $publishArguments['RequireCodeSigning'] = $true
    }
    if (![string]::IsNullOrWhiteSpace($ExpectedSignerSubject)) {
        $publishArguments['ExpectedSignerSubject'] = $ExpectedSignerSubject
    }
    if (![string]::IsNullOrWhiteSpace($ExpectedSignerThumbprint)) {
        $publishArguments['ExpectedSignerThumbprint'] = $ExpectedSignerThumbprint
    }

    & (Join-Path $PSScriptRoot 'publish-player-assistant.ps1') @publishArguments
}

Assert-RequiredFile -Path (Join-Path $PublishDir 'player-assistant.exe') -Description 'published executable'
Assert-RequiredFile -Path (Join-Path $PSScriptRoot $SettingsLocalFileName) -Description $SettingsLocalFileName

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$packageRoot = Join-Path $OutputDir $PackageRootName
$payloadRoot = Join-Path $packageRoot 'payload'
$packagePath = Join-Path $OutputDir $PackageFileName

if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Installer\install-player-assistant.ps1') -Destination (Join-Path $packageRoot 'install-player-assistant.ps1') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Installer\install-player-assistant.cmd') -Destination (Join-Path $packageRoot 'install-player-assistant.cmd') -Force
Copy-DirectoryContents -Source $PublishDir -Destination $payloadRoot

$sourceSettings = ConvertFrom-SettingsFile -Path (Join-Path $PSScriptRoot $SettingsLocalFileName)
$payloadSettingsPath = Join-Path $payloadRoot $SettingsLocalFileName
if (Test-Path -LiteralPath $payloadSettingsPath -PathType Leaf) {
    Set-ItemProperty -LiteralPath $payloadSettingsPath -Name IsReadOnly -Value $false
}
Write-PortableEncryptedSettings -Settings $sourceSettings -DestinationPath $payloadSettingsPath
Protect-RuntimeSidecarFiles -Directory $payloadRoot
& powershell.exe `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File (Join-Path $PSScriptRoot 'verify-runtime-sidecars.ps1') `
    -AppDir $payloadRoot `
    -RequireReadOnlyAttribute `
    -RequireInstallerScriptProtection `
    -InstallerScriptPath (Join-Path $packageRoot 'install-player-assistant.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "Installer staging runtime sidecar verification failed."
}

Compress-Archive -LiteralPath $packageRoot -DestinationPath $packagePath -Force

$verifyInstallerArguments = @{
    PackagePath = $packagePath
    ExpectedVersion = $Version
}
if ($RequireCodeSigning) {
    $verifyInstallerArguments['RequireCodeSigning'] = $true
}
if (![string]::IsNullOrWhiteSpace($ExpectedSignerSubject)) {
    $verifyInstallerArguments['ExpectedSignerSubject'] = $ExpectedSignerSubject
}
if (![string]::IsNullOrWhiteSpace($ExpectedSignerThumbprint)) {
    $verifyInstallerArguments['ExpectedSignerThumbprint'] = $ExpectedSignerThumbprint
}

& (Join-Path $PSScriptRoot 'verify-installer-package.ps1') @verifyInstallerArguments

Write-Output "Installer package created: $packagePath"

$resolvedInnoCompilerPath = Resolve-InnoCompilerPath -RequestedPath $InnoCompilerPath
if ($resolvedInnoCompilerPath) {
    Assert-RequiredFile -Path $InnoScriptPath -Description 'Inno Setup script'
    $innoOutputPath = Join-Path $OutputDir $InnoOutputFileName
    if (Test-Path -LiteralPath $innoOutputPath) {
        Remove-Item -LiteralPath $innoOutputPath -Force
    }

    & $resolvedInnoCompilerPath `
        "/DPayloadDir=$payloadRoot" `
        "/DOutputDir=$OutputDir" `
        "/DVersion=$Version" `
        "/DInstallerVersion=$InstallerVersion" `
        $InnoScriptPath
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
    }

    Assert-RequiredFile -Path $innoOutputPath -Description 'Inno Setup installer'
    Assert-AuthenticodeSignatureMatchesPolicy -Path $innoOutputPath -Description 'Inno Setup installer'
    Write-Output "Inno Setup installer created: $innoOutputPath"
}
else {
    Write-Warning "Inno Setup compiler was not found. Zip installer package was created, but no setup.exe was built."
}
