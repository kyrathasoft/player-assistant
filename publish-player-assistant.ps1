param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [switch]$VerifyOnly,
    [string]$ExpectedSignerSubject = $env:PLAYER_ASSISTANT_RELEASE_SIGNER_SUBJECT,
    [string]$ExpectedSignerThumbprint = $env:PLAYER_ASSISTANT_RELEASE_SIGNER_THUMBPRINT,
    [switch]$RequireCodeSigning
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'version-metadata.ps1')

$SettingsLocalFileName = 'settings.local.json'
$ProjectFileName = 'player-assistant.csproj'
$SettingsFormat = 'app-protected-v3'
$PreviousSettingsFormat = 'app-protected-v2'
$V1SettingsFormat = 'app-protected-v1'
$LegacySettingsFormat = 'dpapi-current-user'
$SettingsSchemaVersionPropertyName = 'schema_version'
$SettingsSchemaVersion = 1
$SettingsEncryptionSeed = 'PlayerAssistant.LocalSettings.v1'
$XpPasswordFileName = 'xp-passwords.json'
$XpPasswordFormat = 'xp-password-hashes-v1'
$XpPasswordAlgorithm = 'PBKDF2-HMAC-SHA256'
$XpPasswordMinimumIterations = 600000
$KeywordIndexFileName = 'keyword-index.json'
$KeywordTermsFileName = 'game-posts-key-terms.md'
$SitemapFileName = 'sitemap.xml'
$SitemapKeywordUrlsFileName = 'sitemap-keyword-urls.json'
$MagicItemsFileName = 'magic-items.json'
$ReleaseManifestFileName = 'release-manifest.json'
$RuntimeInventoryFileName = 'release-runtime-inventory.json'
$ReleaseProvenanceFileName = 'release-provenance.json'
$ReleaseScriptFileNames = @(
    'version.props',
    'version-metadata.ps1',
    'publish-player-assistant.ps1',
    'verify-rc-checklist.ps1',
    'verify-rc-self-tests.ps1',
    'verify-secret-scan.ps1',
    'verify-published-health.ps1',
    'verify-release-publish-parity.ps1',
    'verify-publish-runtime-integrity.ps1',
    'verify-runtime-sidecars.ps1',
    'verify-release-startup-smoke.ps1',
    'collect-diagnostics.ps1',
    'clean-diagnostics-retention.ps1',
    'diagnose-player-assistant-locks.ps1',
    'build-installer.ps1',
    'verify-installer-package.ps1',
    'build-release-update-artifacts.ps1',
    'verify-release-update-artifacts.ps1',
    'Installer\player-assistant.iss',
    'Installer\install-player-assistant.ps1',
    'Installer\install-player-assistant.cmd'
)
$SensitiveFileNames = @(
    'rpol-storage-state.json'
)
$ForbiddenPublishFileNames = @(
    'startup-errors.log',
    'startup-health.json',
    'outbound-network-diagnostics.json',
    'last-crash.json',
    'startup-remediation.txt',
    'used-for-orcish-translation-candidates.md'
)
$ForbiddenPublishDirectoryNames = @(
    'temp'
)
$ForbiddenPlaintextPatterns = @(
    '"RPOL password"\s*:',
    '"RPOL user name"\s*:'
)
$IgnoredKeywordTermsSourceDirectories = @(
    '.git',
    'bin',
    'obj',
    'graphify-out',
    'Release'
)
$RequiredSettingsUrlKeys = @(
    'RPOL Site',
    'Game Intro',
    'The Cast',
    'Obsidian Game Vault'
)
$RequiredLocalSettingsUrlKeys = @(
    'XP Tracking'
)
$RequiredLocalSettingsCredentialKeys = @(
    'RPOL user name',
    'RPOL password'
)
$ProcessLockDiagnosticsScriptPath = Join-Path $PSScriptRoot 'diagnose-player-assistant-locks.ps1'
$RuntimeSidecarVerificationScriptPath = Join-Path $PSScriptRoot 'verify-runtime-sidecars.ps1'

function Get-PowerShellExecutable {
    $pwsh = Get-Command pwsh.exe -ErrorAction SilentlyContinue
    if ($pwsh) {
        return $pwsh.Source
    }

    $windowsPowerShell = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($windowsPowerShell) {
        return $windowsPowerShell.Source
    }

    throw 'Neither pwsh.exe nor powershell.exe is available.'
}

function Get-WindowsPowerShellExecutable {
    $systemDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::System)
    if ([string]::IsNullOrWhiteSpace($systemDirectory)) {
        $systemDirectory = Join-Path $env:WINDIR 'System32'
    }

    $systemPowerShell = Join-Path $systemDirectory 'WindowsPowerShell\v1.0\powershell.exe'
    if (Test-Path -LiteralPath $systemPowerShell -PathType Leaf) {
        return $systemPowerShell
    }

    $windowsPowerShell = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($windowsPowerShell) {
        return $windowsPowerShell.Source
    }

    throw 'Windows PowerShell is required for Authenticode inspection but was not found.'
}

function Get-Sha256Hash {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = $null
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([System.BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '')
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

$PowerShellExecutable = Get-PowerShellExecutable
$WindowsPowerShellExecutable = Get-WindowsPowerShellExecutable

function Get-AuthenticodeSignatureObject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $signatureCommand = Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue
    if ($signatureCommand) {
        try {
            return Get-AuthenticodeSignature -LiteralPath $Path -ErrorAction Stop
        }
        catch {
        }
    }

    $payload = @{
        Path = $Path
    } | ConvertTo-Json -Compress
    $encodedPayload = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($payload))
    $fallbackScript = @'
param([string]$PayloadBase64)
$json = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($PayloadBase64))
$request = $json | ConvertFrom-Json
$signature = Get-AuthenticodeSignature -LiteralPath $request.Path
[pscustomobject]@{
    Status = [string]$signature.Status
    StatusMessage = [string]$signature.StatusMessage
    Path = [string]$signature.Path
    SignerCertificate = if ($signature.SignerCertificate) {
        [pscustomobject]@{
            Subject = [string]$signature.SignerCertificate.Subject
            Thumbprint = [string]$signature.SignerCertificate.Thumbprint
            Issuer = [string]$signature.SignerCertificate.Issuer
            NotBefore = $signature.SignerCertificate.NotBefore.ToString('O')
            NotAfter = $signature.SignerCertificate.NotAfter.ToString('O')
        }
    } else {
        $null
    }
    TimeStamperCertificate = if ($signature.TimeStamperCertificate) {
        [pscustomobject]@{
            Subject = [string]$signature.TimeStamperCertificate.Subject
        }
    } else {
        $null
    }
} | ConvertTo-Json -Compress -Depth 6
'@
    $stdoutPath = Join-Path ([System.IO.Path]::GetTempPath()) "player-assistant-authenticode-stdout-$([Guid]::NewGuid().ToString('N')).txt"
    $stderrPath = Join-Path ([System.IO.Path]::GetTempPath()) "player-assistant-authenticode-stderr-$([Guid]::NewGuid().ToString('N')).txt"
    try {
        $process = Start-Process `
            -FilePath $WindowsPowerShellExecutable `
            -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $fallbackScript, '-PayloadBase64', $encodedPayload) `
            -NoNewWindow `
            -PassThru `
            -Wait `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath

        $stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -Raw -LiteralPath $stdoutPath } else { '' }
        $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -Raw -LiteralPath $stderrPath } else { '' }
        if ($process.ExitCode -ne 0) {
            return [pscustomobject]@{
                Status = 'Unknown'
                StatusMessage = "Unable to inspect Authenticode signature. $stderr $stdout"
                Path = $Path
                SignerCertificate = $null
                TimeStamperCertificate = $null
            }
        }
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }

    return $stdout | ConvertFrom-Json
}

function Protect-RuntimeSidecarFiles {
    param([Parameter(Mandatory = $true)][string]$Directory)

    foreach ($fileName in @($XpPasswordFileName)) {
        $path = Join-Path $Directory $fileName
        Assert-RequiredFile -Path $path -Description "published runtime sidecar $fileName"
        Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $true
    }
}

function Invoke-RuntimeSidecarVerification {
    param([Parameter(Mandatory = $true)][string]$Directory)

    Assert-RequiredFile -Path $RuntimeSidecarVerificationScriptPath -Description 'runtime sidecar verification script'
    & $PowerShellExecutable `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $RuntimeSidecarVerificationScriptPath `
        -AppDir $Directory `
        -RequireReadOnlyAttribute
    if ($LASTEXITCODE -ne 0) {
        throw "Runtime sidecar verification failed for $Directory."
    }
}

function Get-ProjectVersionInfo {
    $metadata = Get-PlayerAssistantVersionMetadata -RepoRoot $PSScriptRoot

    return [pscustomobject]@{
        Version = $metadata.Version
        FileVersion = $metadata.AssemblyVersion
        InformationalVersion = $metadata.Version
    }
}

function Get-ProjectRuntimeInfo {
    $projectPath = Join-Path $PSScriptRoot $ProjectFileName
    Assert-RequiredFile -Path $projectPath -Description $ProjectFileName

    [xml]$project = Get-Content -Raw -LiteralPath $projectPath
    $propertyGroups = @($project.Project.PropertyGroup)

    function Get-ProjectPropertyValue {
        param(
            [Parameter(Mandatory = $true)]
            [string]$Name
        )

        foreach ($propertyGroup in $propertyGroups) {
            $value = [string]$propertyGroup.$Name
            if (![string]::IsNullOrWhiteSpace($value)) {
                return $value
            }
        }

        return $null
    }

    $packages = @($project.Project.ItemGroup | ForEach-Object { $_.PackageReference } | Where-Object { $_ -and $_.Include } | ForEach-Object {
        [ordered]@{
            name = [string]$_.Include
            version = [string]$_.Version
        }
    } | Sort-Object { $_.name })

    return [pscustomobject]@{
        TargetFramework = Get-ProjectPropertyValue -Name 'TargetFramework'
        RuntimeIdentifier = Get-ProjectPropertyValue -Name 'RuntimeIdentifier'
        SelfContained = Get-ProjectPropertyValue -Name 'SelfContained'
        PublishSingleFile = Get-ProjectPropertyValue -Name 'PublishSingleFile'
        Packages = $packages
    }
}

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-PathInsideRepo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $repoRoot = Resolve-FullPath $PSScriptRoot
    $fullPath = Resolve-FullPath $Path
    $repoRootWithSeparator = $repoRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

    if (!$fullPath.StartsWith($repoRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use $Description outside repo root: $fullPath"
    }
}

function Assert-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required $Description is missing: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.Length -le 0) {
        throw "Required $Description is empty: $Path"
    }
}

function Assert-RequiredDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (!(Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Required $Description is missing: $Path"
    }

    if (-not (Get-ChildItem -LiteralPath $Path -Force -Recurse | Select-Object -First 1)) {
        throw "Required $Description is empty: $Path"
    }
}

function Get-SettingsV1EncryptionKey {
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ,$sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($SettingsEncryptionSeed))
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-SettingsV2EncryptionKey {
    return ,(Get-SettingsV1EncryptionKey)
}

function Get-SettingsV2AuthenticationKey {
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ,$sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("$SettingsEncryptionSeed.hmac"))
    }
    finally {
        $sha256.Dispose()
    }
}

function Test-FixedTimeEquals {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$Left,

        [Parameter(Mandatory = $true)]
        [byte[]]$Right
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

function Get-SettingsSchemaVersion {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $property = $Settings.PSObject.Properties[$SettingsSchemaVersionPropertyName]
    if ($null -eq $property) {
        return 0
    }

    $value = $property.Value
    $schemaVersion = 0
    if ($value -isnot [int] -and $value -isnot [long] -and $value -isnot [decimal]) {
        throw "$Description has an invalid '$SettingsSchemaVersionPropertyName' value."
    }

    try {
        $schemaVersion = [int]$value
    }
    catch {
        throw "$Description has an invalid '$SettingsSchemaVersionPropertyName' value."
    }

    if ($schemaVersion -lt 0 -or $schemaVersion -ne [decimal]$value) {
        throw "$Description has an invalid '$SettingsSchemaVersionPropertyName' value."
    }

    if ($schemaVersion -gt $SettingsSchemaVersion) {
        throw "$Description uses unsupported schema version $schemaVersion. This verifier supports schema version $SettingsSchemaVersion."
    }

    return $schemaVersion
}

function ConvertTo-PlainSettingsObject {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings
    )

    $plainSettings = [ordered]@{}
    foreach ($property in $Settings.PSObject.Properties) {
        if ($property.Name -eq $SettingsSchemaVersionPropertyName) {
            continue
        }

        $plainSettings[$property.Name] = [string]$property.Value
    }

    return [pscustomobject]$plainSettings
}

function ConvertFrom-AppEncryptedLocalSettings {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

    $raw = Get-Content -Raw -LiteralPath $SettingsPath
    $envelope = $raw | ConvertFrom-Json
    [void](Get-SettingsSchemaVersion -Settings $envelope -Description $SettingsLocalFileName)
    if ($envelope.format -ne $SettingsFormat -and $envelope.format -ne $PreviousSettingsFormat -and $envelope.format -ne $V1SettingsFormat) {
        throw "$SettingsLocalFileName must use encrypted format '$SettingsFormat', '$PreviousSettingsFormat', or '$V1SettingsFormat', but found '$($envelope.format)'."
    }

    if ([string]::IsNullOrWhiteSpace($envelope.payload)) {
        throw "$SettingsLocalFileName has an empty encrypted payload."
    }

    $payloadBytes = [Convert]::FromBase64String($envelope.payload)
    if ($envelope.format -eq $SettingsFormat -or $envelope.format -eq $PreviousSettingsFormat) {
        if ($payloadBytes.Length -lt 49) {
            throw "$SettingsLocalFileName authenticated encrypted payload is too short."
        }

        $tag = [byte[]]::new(32)
        $protectedContent = [byte[]]::new($payloadBytes.Length - $tag.Length)
        [System.Buffer]::BlockCopy($payloadBytes, 0, $protectedContent, 0, $protectedContent.Length)
        [System.Buffer]::BlockCopy($payloadBytes, $protectedContent.Length, $tag, 0, $tag.Length)
        $authenticationKey = if ($envelope.format -eq $SettingsFormat) {
            Get-SettingsV3AuthenticationKey -SettingsPath $SettingsPath
        }
        else {
            Get-SettingsV2AuthenticationKey
        }
        $hmac = [System.Security.Cryptography.HMACSHA256]::new($authenticationKey)
        try {
            $actualTag = $hmac.ComputeHash($protectedContent)
        }
        finally {
            $hmac.Dispose()
        }

        if (!(Test-FixedTimeEquals -Left $actualTag -Right $tag)) {
            throw "$SettingsLocalFileName encrypted payload authentication tag did not match."
        }

        $iv = [byte[]]::new(16)
        $ciphertext = [byte[]]::new($protectedContent.Length - $iv.Length)
        [System.Buffer]::BlockCopy($protectedContent, 0, $iv, 0, $iv.Length)
        [System.Buffer]::BlockCopy($protectedContent, $iv.Length, $ciphertext, 0, $ciphertext.Length)

        $aes = [System.Security.Cryptography.Aes]::Create()
        $aes.Key = if ($envelope.format -eq $SettingsFormat) {
            Get-SettingsV3EncryptionKey -SettingsPath $SettingsPath
        }
        else {
            Get-SettingsV2EncryptionKey
        }
        $aes.IV = $iv
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        try {
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

        $plaintextJson = [System.Text.Encoding]::UTF8.GetString($plaintextBytes)
        return ConvertTo-PlainSettingsObject -Settings ($plaintextJson | ConvertFrom-Json)
    }

    if ($payloadBytes.Length -lt 17) {
        throw "$SettingsLocalFileName encrypted payload is too short."
    }

    $iv = [byte[]]::new(16)
    $ciphertext = [byte[]]::new($payloadBytes.Length - $iv.Length)
    [System.Buffer]::BlockCopy($payloadBytes, 0, $iv, 0, $iv.Length)
    [System.Buffer]::BlockCopy($payloadBytes, $iv.Length, $ciphertext, 0, $ciphertext.Length)

    $aes = [System.Security.Cryptography.Aes]::Create()
    $aes.Key = Get-SettingsV1EncryptionKey
    $aes.IV = $iv
    $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
    $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
    try {
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

    $plaintextJson = [System.Text.Encoding]::UTF8.GetString($plaintextBytes)
    return ConvertTo-PlainSettingsObject -Settings ($plaintextJson | ConvertFrom-Json)
}

function ConvertFrom-LegacyDpapiLocalSettings {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

    $raw = Get-Content -Raw -LiteralPath $SettingsPath
    $envelope = $raw | ConvertFrom-Json
    if ($envelope.format -ne $LegacySettingsFormat) {
        throw "$SettingsLocalFileName must use legacy format '$LegacySettingsFormat', but found '$($envelope.format)'."
    }

    if ([string]::IsNullOrWhiteSpace($envelope.payload)) {
        throw "$SettingsLocalFileName has an empty legacy encrypted payload."
    }

    $protectedBytes = [Convert]::FromBase64String($envelope.payload)
    $plaintextBytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
        $protectedBytes,
        $null,
        [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    $plaintextJson = [System.Text.Encoding]::UTF8.GetString($plaintextBytes)
    return ConvertTo-PlainSettingsObject -Settings ($plaintextJson | ConvertFrom-Json)
}

function Test-IsEncryptedLocalSettings {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings
    )

    return $Settings.PSObject.Properties['format'] `
        -and $Settings.PSObject.Properties['payload'] `
        -and ($Settings.format -eq $SettingsFormat -or $Settings.format -eq $PreviousSettingsFormat -or $Settings.format -eq $V1SettingsFormat -or $Settings.format -eq $LegacySettingsFormat)
}

function ConvertFrom-LocalSettingsFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

    $settings = Get-Content -Raw -LiteralPath $SettingsPath | ConvertFrom-Json
    if (Test-IsEncryptedLocalSettings -Settings $settings) {
        if ($settings.format -eq $SettingsFormat -or $settings.format -eq $PreviousSettingsFormat -or $settings.format -eq $V1SettingsFormat) {
            return ConvertFrom-AppEncryptedLocalSettings -SettingsPath $SettingsPath
        }

        return ConvertFrom-LegacyDpapiLocalSettings -SettingsPath $SettingsPath
    }

    [void](Get-SettingsSchemaVersion -Settings $settings -Description $SettingsLocalFileName)
    return ConvertTo-PlainSettingsObject -Settings $settings
}

function Write-AppEncryptedLocalSettings {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    Assert-RequiredFile -Path $SourcePath -Description $SettingsLocalFileName

    $settings = ConvertFrom-LocalSettingsFile -SettingsPath $SourcePath
    $plaintextJson = $settings | ConvertTo-Json -Depth 10
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
    $aes.Key = Get-SettingsV3EncryptionKey -SettingsPath $DestinationPath
    $aes.IV = $iv
    $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
    $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
    try {
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
    $hmac = [System.Security.Cryptography.HMACSHA256]::new((Get-SettingsV3AuthenticationKey -SettingsPath $DestinationPath))
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
        format = $SettingsFormat
        payload = [Convert]::ToBase64String($payloadBytes)
        key_scope = Get-SettingsKeyScope -SettingsPath $DestinationPath
    }

    [System.IO.File]::WriteAllText(
        $DestinationPath,
        ([pscustomobject]$envelope | ConvertTo-Json -Depth 4),
        [System.Text.UTF8Encoding]::new($false))
}

function Assert-EncryptedLocalSettings {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishedPath
    )

    Assert-RequiredFile -Path $PublishedPath -Description "published $SettingsLocalFileName"

    $publishedRaw = Get-Content -Raw -LiteralPath $PublishedPath
    if ($publishedRaw -match '"RPOL password"\s*:') {
        throw "Published $SettingsLocalFileName appears to contain plaintext RPOL credentials."
    }

    $publishedSettings = ConvertFrom-AppEncryptedLocalSettings -SettingsPath $PublishedPath
    $publishedEnvelope = Read-JsonFile -Path $PublishedPath -Description "published $SettingsLocalFileName"
    $publishedSchemaVersion = Get-SettingsSchemaVersion -Settings $publishedEnvelope -Description "published $SettingsLocalFileName"
    if ($publishedSchemaVersion -ne $SettingsSchemaVersion) {
        throw "Published $SettingsLocalFileName must declare schema version $SettingsSchemaVersion."
    }

    foreach ($settingsKey in $RequiredLocalSettingsUrlKeys) {
        $publishedProperty = $publishedSettings.PSObject.Properties[$settingsKey]
        if ($null -eq $publishedProperty -or [string]::IsNullOrWhiteSpace([string]$publishedProperty.Value)) {
            throw "Published $SettingsLocalFileName is missing required URL setting '$settingsKey'."
        }

        $uri = $null
        if (![System.Uri]::TryCreate([string]$publishedProperty.Value, [System.UriKind]::Absolute, [ref]$uri) -or
            ($uri.Scheme -ne [System.Uri]::UriSchemeHttp -and $uri.Scheme -ne [System.Uri]::UriSchemeHttps)) {
            throw "Published $SettingsLocalFileName value '$settingsKey' must be an absolute HTTP or HTTPS URL."
        }
    }

    foreach ($settingsKey in $RequiredLocalSettingsCredentialKeys) {
        $publishedProperty = $publishedSettings.PSObject.Properties[$settingsKey]
        if ($null -eq $publishedProperty -or [string]::IsNullOrWhiteSpace([string]$publishedProperty.Value)) {
            throw "Published $SettingsLocalFileName is missing required RPOL credential setting '$settingsKey'."
        }
    }

    foreach ($property in $publishedSettings.PSObject.Properties) {
        if (![string]::IsNullOrWhiteSpace([string]$property.Value) -and $publishedRaw.Contains([string]$property.Value)) {
            throw "Published $SettingsLocalFileName contains plaintext value for '$($property.Name)'."
        }
    }
}

function Assert-PublishedXpPasswordSidecar {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Assert-RequiredFile -Path $Path -Description "published $XpPasswordFileName"

    $envelope = Read-JsonFile -Path $Path -Description "published $XpPasswordFileName"
    [void](Get-SettingsSchemaVersion -Settings $envelope -Description "published $XpPasswordFileName")

    if ($envelope.format -ne $XpPasswordFormat) {
        throw "Published $XpPasswordFileName must use salted password hash format '$XpPasswordFormat'."
    }

    $unexpectedDocumentProperties = @($envelope.PSObject.Properties.Name | Where-Object {
        @('schema_version', 'format', 'entries') -notcontains $_
    })
    if ($unexpectedDocumentProperties.Count -gt 0) {
        throw "Published $XpPasswordFileName contains unexpected property '$($unexpectedDocumentProperties[0])'."
    }

    $entries = @($envelope.entries)
    if ($entries.Count -eq 0) {
        throw "Published $XpPasswordFileName does not contain any password hash entries."
    }

    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $canonicalIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $salts = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($entry in $entries) {
        $unexpectedEntryProperties = @($entry.PSObject.Properties.Name | Where-Object {
            @('name', 'canonical_id', 'algorithm', 'iterations', 'salt', 'hash') -notcontains $_
        })
        if ($unexpectedEntryProperties.Count -gt 0) {
            throw "Published $XpPasswordFileName contains unexpected entry property '$($unexpectedEntryProperties[0])'."
        }

        if ([string]::IsNullOrWhiteSpace([string]$entry.name)) {
            throw "Published $XpPasswordFileName contains a blank PC name."
        }

        if (!$names.Add([string]$entry.name)) {
            throw "Published $XpPasswordFileName contains duplicate PC name '$($entry.name)'."
        }

        $canonicalId = if ($entry.PSObject.Properties['canonical_id']) { [string]$entry.canonical_id } else { [string]$entry.name }
        if ([string]::IsNullOrWhiteSpace($canonicalId) -or !$canonicalIds.Add($canonicalId)) {
            throw "Published $XpPasswordFileName contains a blank or duplicate canonical ID."
        }

        if ($entry.algorithm -ne $XpPasswordAlgorithm) {
            throw "Published $XpPasswordFileName entry '$($entry.name)' must use algorithm '$XpPasswordAlgorithm'."
        }

        if ([int64]$entry.iterations -lt $XpPasswordMinimumIterations) {
            throw "Published $XpPasswordFileName entry '$($entry.name)' must use at least $XpPasswordMinimumIterations iterations."
        }

        try {
            $saltBytes = [Convert]::FromBase64String([string]$entry.salt)
            $hashBytes = [Convert]::FromBase64String([string]$entry.hash)
        }
        catch {
            throw "Published $XpPasswordFileName entry '$($entry.name)' contains invalid base64 hash data."
        }

        if ($saltBytes.Length -lt 16) {
            throw "Published $XpPasswordFileName entry '$($entry.name)' must use a salt of at least 16 bytes."
        }

        if ($hashBytes.Length -ne 32) {
            throw "Published $XpPasswordFileName entry '$($entry.name)' must use a 32-byte hash."
        }

        if (!$salts.Add([string]$entry.salt)) {
            throw "Published $XpPasswordFileName contains a reused password salt."
        }
    }
}

function Assert-NoSensitiveFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    foreach ($fileName in $SensitiveFileNames) {
        $matches = Get-ChildItem -LiteralPath $Directory -Recurse -Force -File -Filter $fileName
        if ($matches) {
            $paths = $matches | ForEach-Object { $_.FullName }
            throw "Publish output contains sensitive file '$fileName': $($paths -join ', ')"
        }
    }
}

function Assert-NoForbiddenPublishArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    foreach ($fileName in $ForbiddenPublishFileNames) {
        $matches = Get-ChildItem -LiteralPath $Directory -Recurse -Force -File -Filter $fileName
        if ($matches) {
            $paths = $matches | ForEach-Object { $_.FullName }
            throw "Publish output contains forbidden file '$fileName': $($paths -join ', ')"
        }
    }

    foreach ($directoryName in $ForbiddenPublishDirectoryNames) {
        $matches = Get-ChildItem -LiteralPath $Directory -Recurse -Force -Directory -Filter $directoryName
        if ($matches) {
            $paths = $matches | ForEach-Object { $_.FullName }
            throw "Publish output contains forbidden directory '$directoryName': $($paths -join ', ')"
        }
    }

    $pdbFiles = Get-ChildItem -LiteralPath $Directory -Recurse -Force -File -Filter '*.pdb'
    if ($pdbFiles) {
        $paths = $pdbFiles | ForEach-Object { $_.FullName }
        throw "Publish output contains debug symbol files: $($paths -join ', ')"
    }
}

function Assert-NoPlaintextCredentialMarkers {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $filesToScan = Get-ChildItem -LiteralPath $Directory -Recurse -Force -File |
        Where-Object {
            $_.Extension -in @('.json', '.config', '.txt', '.md', '.xml', '.log', '.ps1')
        }

    foreach ($file in $filesToScan) {
        $content = Get-Content -Raw -LiteralPath $file.FullName
        foreach ($pattern in $ForbiddenPlaintextPatterns) {
            if ($content -match $pattern) {
                throw "Publish output contains plaintext credential marker '$pattern' in $($file.FullName)"
            }
        }
    }
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    Assert-RequiredFile -Path $Path -Description $Description

    try {
        return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    }
    catch {
        throw "$Description is not valid JSON: $Path. $($_.Exception.Message)"
    }
}

function Assert-PublishedSettingsJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $settings = Read-JsonFile -Path $Path -Description 'published settings.json'
    [void](Get-SettingsSchemaVersion -Settings $settings -Description 'published settings.json')
    foreach ($settingsKey in $RequiredSettingsUrlKeys) {
        $property = $settings.PSObject.Properties[$settingsKey]
        if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw "Published settings.json is missing required URL setting '$settingsKey'."
        }

        $uri = $null
        if (![System.Uri]::TryCreate([string]$property.Value, [System.UriKind]::Absolute, [ref]$uri) -or
            ($uri.Scheme -ne [System.Uri]::UriSchemeHttp -and $uri.Scheme -ne [System.Uri]::UriSchemeHttps)) {
            throw "Published settings.json value '$settingsKey' must be an absolute HTTP or HTTPS URL."
        }
    }
}

function Assert-PublishedMagicItems {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $payload = Read-JsonFile -Path $Path -Description 'published magic-item fallback'
    if ([int]$payload.schema_version -ne 1) {
        throw 'Published magic-item fallback must use schema version 1.'
    }

    $expectedSource = 'https://publish.obsidian.md/scarlethorizons/Magic+Items/Kirkilston+Crew+Magic+Items'
    if ([string]$payload.source -ne $expectedSource) {
        throw 'Published magic-item fallback has an unexpected source.'
    }

    $items = @($payload.items)
    if ($items.Count -eq 0) {
        throw 'Published magic-item fallback contains no items.'
    }

    $requiredFields = @('name', 'description', 'date-acquired', 'meta-date-acquired', 'longevity', 'provenance', 'whereabouts')
    $validLongevity = @('one-shot', 'limited-use', 'permanent')
    foreach ($item in $items) {
        foreach ($fieldName in $requiredFields) {
            if ($item.PSObject.Properties.Name -notcontains $fieldName -or
                [string]::IsNullOrWhiteSpace([string]$item.$fieldName)) {
                throw "Published magic-item fallback contains an item with missing or empty '$fieldName'."
            }
        }
        if ($validLongevity -notcontains [string]$item.longevity) {
            throw "Published magic-item fallback contains invalid longevity '$($item.longevity)'."
        }
    }
}

function Assert-PublishedKeywordIndex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $index = Read-JsonFile -Path $Path -Description $Description
    if ($null -eq $index.PSObject.Properties['words']) {
        throw "$Description must contain a words object."
    }

    $wordCount = @($index.words.PSObject.Properties).Count
    if ($wordCount -le 0) {
        throw "$Description must contain at least one indexed word."
    }

    if ($null -eq $index.PSObject.Properties['index_metadata']) {
        throw "$Description must contain index_metadata."
    }
}

function Assert-PublishedKeywordTerms {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Assert-RequiredFile -Path $Path -Description 'published keyword terms file'
    $terms = @(Get-Content -LiteralPath $Path | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
    if ($terms.Count -le 0) {
        throw "Published $KeywordTermsFileName must contain at least one term."
    }
}

function Assert-PublishedSitemap {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Assert-RequiredFile -Path $Path -Description 'published sitemap'

    try {
        [xml]$sitemap = Get-Content -Raw -LiteralPath $Path
    }
    catch {
        throw "Published $SitemapFileName is not valid XML: $($_.Exception.Message)"
    }

    if ($null -eq $sitemap.DocumentElement) {
        throw "Published $SitemapFileName has no XML document element."
    }
}

function Assert-PublishedPlaywrightRuntime {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    Assert-RequiredDirectory -Path $Directory -Description 'published Playwright runtime'
    Assert-RequiredFile -Path (Join-Path $Directory 'node\win32_x64\node.exe') -Description 'published Playwright node.exe'
    Assert-RequiredFile -Path (Join-Path $Directory 'package\package.json') -Description 'published Playwright package manifest'
    Assert-RequiredFile -Path (Join-Path $Directory 'package\browsers.json') -Description 'published Playwright browser manifest'
    [void](Read-JsonFile -Path (Join-Path $Directory 'package\package.json') -Description 'published Playwright package manifest')
    [void](Read-JsonFile -Path (Join-Path $Directory 'package\browsers.json') -Description 'published Playwright browser manifest')
}

function Assert-PublishedExecutableVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Assert-RequiredFile -Path $Path -Description 'published executable'

    $expected = Get-ProjectVersionInfo
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if ($versionInfo.FileVersion -ne $expected.FileVersion) {
        throw "Published executable FileVersion '$($versionInfo.FileVersion)' does not match project FileVersion '$($expected.FileVersion)'."
    }

    if ($versionInfo.ProductVersion -ne $expected.InformationalVersion) {
        throw "Published executable ProductVersion '$($versionInfo.ProductVersion)' does not match project InformationalVersion '$($expected.InformationalVersion)'."
    }
}

function Get-KeywordTermsSourceCandidate {
    $pendingDirectories = [System.Collections.Generic.Stack[string]]::new()
    $pendingDirectories.Push($PSScriptRoot)

    while ($pendingDirectories.Count -gt 0) {
        $currentDirectory = $pendingDirectories.Pop()

        $matches = @(Get-ChildItem -LiteralPath $currentDirectory -File -Filter $KeywordTermsFileName -ErrorAction SilentlyContinue)
        if ($matches.Count -gt 0) {
            return ($matches | Sort-Object FullName | Select-Object -First 1).FullName
        }

        $children = @(Get-ChildItem -LiteralPath $currentDirectory -Directory -ErrorAction SilentlyContinue)
        foreach ($child in ($children | Sort-Object FullName -Descending)) {
            if ($IgnoredKeywordTermsSourceDirectories -contains $child.Name) {
                continue
            }

            $pendingDirectories.Push($child.FullName)
        }
    }

    return $null
}

function Write-KeywordTermsFromKeywordIndex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$IndexPath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    Assert-RequiredFile -Path $IndexPath -Description 'Release keyword index'

    $index = Get-Content -Raw -LiteralPath $IndexPath | ConvertFrom-Json
    if ($null -eq $index.PSObject.Properties['words']) {
        throw "Cannot generate $KeywordTermsFileName because $KeywordIndexFileName does not contain a words object."
    }

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($property in $index.words.PSObject.Properties) {
        $term = [string]$property.Name
        $term = $term.Trim()
        if ($term.Length -gt 0) {
            [void]$seen.Add($term)
        }
    }

    if ($seen.Count -le 0) {
        throw "Cannot generate $KeywordTermsFileName because $KeywordIndexFileName contains no indexed terms."
    }

    $terms = [string[]]$seen
    [Array]::Sort($terms, [System.StringComparer]::OrdinalIgnoreCase)
    [System.IO.File]::WriteAllLines($DestinationPath, $terms, [System.Text.UTF8Encoding]::new($false))
}

function Stage-KeywordTermsFile {
    $releaseDirectory = Join-Path $PSScriptRoot 'Release'
    $destinationPath = Join-Path $releaseDirectory $KeywordTermsFileName
    $indexPath = Join-Path $releaseDirectory $KeywordIndexFileName

    if (Test-Path -LiteralPath $destinationPath -PathType Leaf) {
        Assert-RequiredFile -Path $destinationPath -Description 'keyword terms file'
        return
    }

    $sourcePath = Get-KeywordTermsSourceCandidate
    if (![string]::IsNullOrWhiteSpace($sourcePath)) {
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
        Assert-RequiredFile -Path $destinationPath -Description 'keyword terms file'
        return
    }

    Write-KeywordTermsFromKeywordIndex -IndexPath $indexPath -DestinationPath $destinationPath
    Assert-RequiredFile -Path $destinationPath -Description 'generated keyword terms file'
}

function Assert-PublishInputs {
    Assert-RequiredFile -Path (Join-Path $PSScriptRoot "Release\$KeywordIndexFileName") -Description 'Release keyword index'
    Assert-RequiredFile -Path (Join-Path $PSScriptRoot "Release\$KeywordTermsFileName") -Description 'keyword terms file'
    Assert-RequiredFile -Path (Join-Path $PSScriptRoot "Release\$SitemapFileName") -Description 'Release sitemap'
    Assert-RequiredFile -Path (Join-Path $PSScriptRoot "Release\$SitemapKeywordUrlsFileName") -Description 'Release sitemap keyword URL library'
    Assert-RequiredFile -Path (Join-Path $PSScriptRoot $XpPasswordFileName) -Description $XpPasswordFileName
}

function Get-ManifestFileEntry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $path = Join-Path $Directory $RelativePath
    Assert-RequiredFile -Path $path -Description "release manifest file $RelativePath"
    $item = Get-Item -LiteralPath $path
    return [ordered]@{
        relative_path = $RelativePath
        length = $item.Length
        sha256 = Get-Sha256Hash -Path $path
    }
}

function Get-SettingsDerivationScope {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

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

function Get-ScopedSha256Bytes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ,$sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Value))
    }
    finally {
        $sha256.Dispose()
    }
}

function Convert-BytesToHex {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$Bytes
    )

    return [System.BitConverter]::ToString($Bytes).Replace('-', '')
}

function Get-SettingsV3EncryptionKey {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

    $scope = Get-SettingsDerivationScope -SettingsPath $SettingsPath
    return ,(Get-ScopedSha256Bytes -Value "$SettingsEncryptionSeed.v3.encryption.$scope")
}

function Get-SettingsV3AuthenticationKey {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

    $scope = Get-SettingsDerivationScope -SettingsPath $SettingsPath
    return ,(Get-ScopedSha256Bytes -Value "$SettingsEncryptionSeed.v3.hmac.$scope")
}

function Get-SettingsKeyScope {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

    $scopeHashBytes = Get-ScopedSha256Bytes -Value (Get-SettingsDerivationScope -SettingsPath $SettingsPath)
    return [ordered]@{
        machine_bound = $true
        user_bound = $true
        install_path_bound = $true
        scope_hash = [System.BitConverter]::ToString($scopeHashBytes).Replace('-', '')
    }
}

function Get-ReleaseScriptHashEntries {
    return @($ReleaseScriptFileNames | ForEach-Object {
        $relativePath = $_
        $path = Join-Path $PSScriptRoot $relativePath
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $item = Get-Item -LiteralPath $path
            [ordered]@{
                relative_path = $relativePath
                length = $item.Length
                sha256 = Get-Sha256Hash -Path $path
            }
        }
    })
}

function Invoke-GitOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.Arguments = ConvertTo-ProcessArguments -Arguments $Arguments
    $startInfo.WorkingDirectory = $PSScriptRoot
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false

    try {
        $process = [System.Diagnostics.Process]::Start($startInfo)
        if ($null -eq $process) {
            return $null
        }

        $standardOutput = $process.StandardOutput.ReadToEnd()
        $process.StandardError.ReadToEnd() | Out-Null
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            return $null
        }

        return $standardOutput.Trim()
    }
    catch {
        return $null
    }
}

function ConvertTo-ProcessArguments {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $escapedArguments = $Arguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + ($_ -replace '"', '\"') + '"'
        }
        else {
            $_
        }
    }

    return ($escapedArguments -join ' ')
}

function Get-GitProvenanceInfo {
    $status = Invoke-GitOutput -Arguments @('status', '--short')
    $statusLines = if ([string]::IsNullOrWhiteSpace($status)) {
        @()
    }
    else {
        @($status -split "`r?`n" | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
    }

    $commit = Invoke-GitOutput -Arguments @('rev-parse', 'HEAD')
    $tags = Invoke-GitOutput -Arguments @('tag', '--points-at', 'HEAD')
    $tagList = [System.Collections.Generic.List[string]]::new()
    if (![string]::IsNullOrWhiteSpace($tags)) {
        @($tags -split "`r?`n" | Where-Object { ![string]::IsNullOrWhiteSpace($_) }) |
            ForEach-Object { [void]$tagList.Add($_) }
    }

    return [ordered]@{
        commit = $commit
        commit_short = if (![string]::IsNullOrWhiteSpace($commit) -and $commit.Length -ge 12) { $commit.Substring(0, 12) } else { $commit }
        branch = Invoke-GitOutput -Arguments @('branch', '--show-current')
        tags_at_commit = [string[]]$tagList.ToArray()
        dirty = $statusLines.Count -gt 0
        status_count = $statusLines.Count
        status_sha256 = if ($statusLines.Count -gt 0) { Convert-BytesToHex -Bytes (Get-ScopedSha256Bytes -Value ($statusLines -join "`n")) } else { $null }
    }
}

function Get-AuthenticodeSignatureSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $signature = Get-AuthenticodeSignatureObject -Path $Path
    return [ordered]@{
        status = [string]$signature.Status
        signer_subject = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
        thumbprint = if ($signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { $null }
        issuer = if ($signature.SignerCertificate) { $signature.SignerCertificate.Issuer } else { $null }
        not_before = if ($signature.SignerCertificate) { $signature.SignerCertificate.NotBefore.ToString('O') } else { $null }
        not_after = if ($signature.SignerCertificate) { $signature.SignerCertificate.NotAfter.ToString('O') } else { $null }
        timestamp_subject = if ($signature.TimeStamperCertificate) { $signature.TimeStamperCertificate.Subject } else { $null }
    }
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
        return Get-AuthenticodeSignatureSummary -Path $Path
    }

    Assert-RequiredFile -Path $Path -Description $Description
    $signature = Get-AuthenticodeSignatureObject -Path $Path
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

    return Get-AuthenticodeSignatureSummary -Path $Path
}

function Write-ReleaseProvenance {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $projectVersion = Get-ProjectVersionInfo
    $manifestEntry = Get-ManifestFileEntry -Directory $Directory -RelativePath $ReleaseManifestFileName
    $inventoryEntry = Get-ManifestFileEntry -Directory $Directory -RelativePath $RuntimeInventoryFileName
    $executablePath = Join-Path $Directory 'player-assistant.exe'
    $provenance = [ordered]@{
        schema_version = 1
        generated_at = (Get-Date).ToString('O')
        app = [ordered]@{
            version = $projectVersion.Version
            file_version = $projectVersion.FileVersion
            product_version = $projectVersion.InformationalVersion
        }
        git = Get-GitProvenanceInfo
        release_manifest = $manifestEntry
        runtime_inventory = $inventoryEntry
        executable_signature = Get-AuthenticodeSignatureSummary -Path $executablePath
        hash_algorithm = 'SHA256'
    }

    $provenancePath = Join-Path $Directory $ReleaseProvenanceFileName
    $provenance | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $provenancePath -Encoding UTF8
    Assert-RequiredFile -Path $provenancePath -Description $ReleaseProvenanceFileName
}

function Assert-ReleaseProvenance {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $provenance = Read-JsonFile -Path (Join-Path $Directory $ReleaseProvenanceFileName) -Description $ReleaseProvenanceFileName
    if ($provenance.schema_version -ne 1) {
        throw "$ReleaseProvenanceFileName schema_version '$($provenance.schema_version)' is not supported."
    }

    if ($provenance.hash_algorithm -ne 'SHA256') {
        throw "$ReleaseProvenanceFileName must use SHA256 hashes."
    }

    $expectedVersion = Get-ProjectVersionInfo
    if ($provenance.app.version -ne $expectedVersion.Version -or
        $provenance.app.file_version -ne $expectedVersion.FileVersion -or
        $provenance.app.product_version -ne $expectedVersion.InformationalVersion) {
        throw "$ReleaseProvenanceFileName app version does not match $ProjectFileName."
    }

    if ([string]::IsNullOrWhiteSpace([string]$provenance.git.commit)) {
        throw "$ReleaseProvenanceFileName is missing the Git commit."
    }

    foreach ($entryProperty in @('release_manifest', 'runtime_inventory')) {
        $entry = $provenance.$entryProperty
        $relativePath = [string]$entry.relative_path
        $path = Join-Path $Directory $relativePath
        Assert-RequiredFile -Path $path -Description "$ReleaseProvenanceFileName referenced $relativePath"
        $item = Get-Item -LiteralPath $path
        if ([long]$entry.length -ne [long]$item.Length) {
            throw "$ReleaseProvenanceFileName length mismatch for '$relativePath'."
        }

        $actualHash = Get-Sha256Hash -Path $path
        if ($actualHash -ne [string]$entry.sha256) {
            throw "$ReleaseProvenanceFileName SHA256 mismatch for '$relativePath'."
        }
    }

    if ($null -eq $provenance.PSObject.Properties['executable_signature']) {
        throw "$ReleaseProvenanceFileName is missing executable signature status."
    }

    $executablePath = Join-Path $Directory 'player-assistant.exe'
    $actualSignature = Assert-AuthenticodeSignatureMatchesPolicy -Path $executablePath -Description 'Published executable'
    if ((Test-CodeSigningPolicyConfigured) -and [string]$provenance.executable_signature.status -ne [string]$actualSignature.status) {
        throw "$ReleaseProvenanceFileName executable signature status does not match the current executable."
    }

    if ((Test-CodeSigningPolicyConfigured) -and
        [string]$provenance.executable_signature.thumbprint -ne [string]$actualSignature.thumbprint) {
        throw "$ReleaseProvenanceFileName executable signature thumbprint does not match the current executable."
    }
}

function Write-ReleaseRuntimeInventory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $projectVersion = Get-ProjectVersionInfo
    $runtimeInfo = Get-ProjectRuntimeInfo
    $inventory = [ordered]@{
        schema_version = 1
        generated_at = (Get-Date).ToString('O')
        app = [ordered]@{
            version = $projectVersion.Version
            file_version = $projectVersion.FileVersion
            product_version = $projectVersion.InformationalVersion
        }
        runtime = [ordered]@{
            target_framework = $runtimeInfo.TargetFramework
            runtime_identifier = $runtimeInfo.RuntimeIdentifier
            self_contained = $runtimeInfo.SelfContained
            publish_single_file = 'false'
            publish_runtime_identifier = 'win-x64'
            publish_self_contained = 'false'
        }
        packages = @($runtimeInfo.Packages)
        scripts = @(Get-ReleaseScriptHashEntries)
        hash_algorithm = 'SHA256'
    }

    $inventoryPath = Join-Path $Directory $RuntimeInventoryFileName
    $inventory | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $inventoryPath -Encoding UTF8
    Assert-RequiredFile -Path $inventoryPath -Description $RuntimeInventoryFileName
}

function Assert-PublishedRuntimeInventory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $inventoryPath = Join-Path $Directory $RuntimeInventoryFileName
    $inventory = Read-JsonFile -Path $inventoryPath -Description $RuntimeInventoryFileName
    if ($inventory.schema_version -ne 1) {
        throw "$RuntimeInventoryFileName schema_version '$($inventory.schema_version)' is not supported."
    }

    if ($inventory.hash_algorithm -ne 'SHA256') {
        throw "$RuntimeInventoryFileName must use SHA256 hashes."
    }

    $expectedVersion = Get-ProjectVersionInfo
    if ($inventory.app.version -ne $expectedVersion.Version -or
        $inventory.app.file_version -ne $expectedVersion.FileVersion -or
        $inventory.app.product_version -ne $expectedVersion.InformationalVersion) {
        throw "$RuntimeInventoryFileName app version does not match $ProjectFileName."
    }

    $expectedRuntime = Get-ProjectRuntimeInfo
    if ($inventory.runtime.target_framework -ne $expectedRuntime.TargetFramework) {
        throw "$RuntimeInventoryFileName target framework does not match $ProjectFileName."
    }

    foreach ($packageName in @('Microsoft.Playwright', 'SkiaSharp')) {
        $package = @($inventory.packages | Where-Object { $_.name -eq $packageName } | Select-Object -First 1)
        if ($package.Count -eq 0 -or [string]::IsNullOrWhiteSpace([string]$package[0].version)) {
            throw "$RuntimeInventoryFileName is missing package version for '$packageName'."
        }
    }

    $scriptEntry = @($inventory.scripts | Where-Object { $_.relative_path -eq 'publish-player-assistant.ps1' } | Select-Object -First 1)
    if ($scriptEntry.Count -eq 0 -or [string]::IsNullOrWhiteSpace([string]$scriptEntry[0].sha256)) {
        throw "$RuntimeInventoryFileName is missing publish script hash."
    }
}

function Get-ReleaseManifestFileList {
    return @(
        'player-assistant.exe',
        'settings.json',
        $MagicItemsFileName,
        $XpPasswordFileName,
        $RuntimeInventoryFileName,
        $KeywordIndexFileName,
        $KeywordTermsFileName,
        $SitemapFileName,
        $SitemapKeywordUrlsFileName,
        '.playwright\node\win32_x64\node.exe',
        '.playwright\package\package.json',
        '.playwright\package\browsers.json'
    )
}

function Write-ReleaseIntegrityManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $projectVersion = Get-ProjectVersionInfo
    $files = @(Get-ReleaseManifestFileList | ForEach-Object {
        Get-ManifestFileEntry -Directory $Directory -RelativePath $_
    })
    $manifest = [ordered]@{
        schema_version = 1
        generated_at = (Get-Date).ToString('O')
        app_version = $projectVersion.Version
        file_version = $projectVersion.FileVersion
        product_version = $projectVersion.InformationalVersion
        hash_algorithm = 'SHA256'
        files = $files
    }

    $manifestPath = Join-Path $Directory $ReleaseManifestFileName
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Assert-RequiredFile -Path $manifestPath -Description $ReleaseManifestFileName
}

function Assert-ReleaseIntegrityManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $manifestPath = Join-Path $Directory $ReleaseManifestFileName
    $manifest = Read-JsonFile -Path $manifestPath -Description $ReleaseManifestFileName
    if ($manifest.schema_version -ne 1) {
        throw "$ReleaseManifestFileName schema_version '$($manifest.schema_version)' is not supported."
    }

    if ($manifest.hash_algorithm -ne 'SHA256') {
        throw "$ReleaseManifestFileName must use SHA256 hashes."
    }

    $entries = @($manifest.files)
    $requiredPaths = @(Get-ReleaseManifestFileList)
    foreach ($relativePath in $requiredPaths) {
        $entry = @($entries | Where-Object { $_.relative_path -eq $relativePath } | Select-Object -First 1)
        if ($entry.Count -eq 0) {
            throw "$ReleaseManifestFileName is missing an entry for '$relativePath'."
        }

        $path = Join-Path $Directory $relativePath
        Assert-RequiredFile -Path $path -Description "manifested file $relativePath"
        $item = Get-Item -LiteralPath $path
        if ([long]$entry[0].length -ne [long]$item.Length) {
            throw "$ReleaseManifestFileName length mismatch for '$relativePath'."
        }

        $actualHash = Get-Sha256Hash -Path $path
        if ($actualHash -ne [string]$entry[0].sha256) {
            throw "$ReleaseManifestFileName SHA256 mismatch for '$relativePath'."
        }
    }
}

function Assert-PublishOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    Assert-PublishedExecutableVersion -Path (Join-Path $Directory 'player-assistant.exe')
    Assert-PublishedSettingsJson -Path (Join-Path $Directory 'settings.json')
    Assert-PublishedMagicItems -Path (Join-Path $Directory $MagicItemsFileName)
    Assert-PublishedKeywordIndex -Path (Join-Path $Directory $KeywordIndexFileName) -Description 'published keyword index'
    Assert-PublishedKeywordTerms -Path (Join-Path $Directory $KeywordTermsFileName)
    Assert-PublishedSitemap -Path (Join-Path $Directory $SitemapFileName)
    [void](Read-JsonFile -Path (Join-Path $Directory $SitemapKeywordUrlsFileName) -Description 'published sitemap keyword URL library')
    Assert-PublishedPlaywrightRuntime -Directory (Join-Path $Directory '.playwright')
    Assert-EncryptedLocalSettings -PublishedPath (Join-Path $Directory $SettingsLocalFileName)
    Assert-PublishedXpPasswordSidecar -Path (Join-Path $Directory $XpPasswordFileName)
    Assert-PublishedRuntimeInventory -Directory $Directory
    Assert-NoSensitiveFiles -Directory $Directory
    Assert-NoForbiddenPublishArtifacts -Directory $Directory
    Assert-NoPlaintextCredentialMarkers -Directory $Directory
    Assert-ReleaseIntegrityManifest -Directory $Directory
    Assert-ReleaseProvenance -Directory $Directory
    Invoke-RuntimeSidecarVerification -Directory $Directory
}

function Invoke-ProcessLockDiagnostics {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory
    )

    if (!(Test-Path -LiteralPath $ProcessLockDiagnosticsScriptPath -PathType Leaf)) {
        Write-Output "Process-lock diagnostics script is missing: $ProcessLockDiagnosticsScriptPath"
        return
    }

    Write-Output ''
    Write-Output 'Process-lock diagnostics after publish failure:'
    & $PowerShellExecutable -NoProfile -ExecutionPolicy Bypass -File $ProcessLockDiagnosticsScriptPath `
        -ReleasePath (Join-Path $PSScriptRoot 'Release\player-assistant.exe') `
        -PublishPath (Join-Path $PublishDirectory 'player-assistant.exe')
}

$resolvedOutputDir = Resolve-FullPath $OutputDir
Assert-PathInsideRepo -Path $resolvedOutputDir -Description 'publish output directory'

if ($VerifyOnly) {
    Assert-PublishOutput -Directory $resolvedOutputDir
    Write-Output "Publish verification passed: $resolvedOutputDir"
    return
}

Stage-KeywordTermsFile
Assert-PublishInputs

if (Test-Path -LiteralPath $resolvedOutputDir) {
    Get-ChildItem -LiteralPath $resolvedOutputDir -Force | Remove-Item -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null

$publishArguments = @(
    'publish',
    "$PSScriptRoot\player-assistant.csproj",
    '--configuration',
    'Release',
    '--runtime',
    'win-x64',
    '--self-contained',
    'false',
    '-p:PublishSingleFile=false',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '--output',
    $resolvedOutputDir
)
& dotnet @publishArguments
$publishExitCode = $LASTEXITCODE
if ($publishExitCode -ne 0) {
    Invoke-ProcessLockDiagnostics -PublishDirectory $resolvedOutputDir
    throw "dotnet publish failed with exit code $publishExitCode."
}

$releaseLocalSettingsPath = Join-Path (Join-Path $PSScriptRoot 'Release') $SettingsLocalFileName
if (Test-Path -LiteralPath $releaseLocalSettingsPath -PathType Leaf) {
    Remove-Item -LiteralPath $releaseLocalSettingsPath -Force
}

Get-ChildItem -LiteralPath $resolvedOutputDir -Recurse -Filter '*.pdb' -File | Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Release\$KeywordIndexFileName") -Destination (Join-Path $resolvedOutputDir $KeywordIndexFileName) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Release\$KeywordTermsFileName") -Destination (Join-Path $resolvedOutputDir $KeywordTermsFileName) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Release\$SitemapFileName") -Destination (Join-Path $resolvedOutputDir $SitemapFileName) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Release\$SitemapKeywordUrlsFileName") -Destination (Join-Path $resolvedOutputDir $SitemapKeywordUrlsFileName) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot $XpPasswordFileName) -Destination (Join-Path $resolvedOutputDir $XpPasswordFileName) -Force
Write-AppEncryptedLocalSettings `
    -SourcePath (Join-Path $PSScriptRoot $SettingsLocalFileName) `
    -DestinationPath (Join-Path $resolvedOutputDir $SettingsLocalFileName)
Protect-RuntimeSidecarFiles -Directory $resolvedOutputDir
Write-ReleaseRuntimeInventory -Directory $resolvedOutputDir
Write-ReleaseIntegrityManifest -Directory $resolvedOutputDir
Write-ReleaseProvenance -Directory $resolvedOutputDir

Assert-PublishOutput -Directory $resolvedOutputDir
Write-Output "Publish verified: $resolvedOutputDir"
