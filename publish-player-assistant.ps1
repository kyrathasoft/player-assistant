param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [switch]$VerifyOnly,
    [string]$SourceSettingsPath = (Join-Path $PSScriptRoot 'settings.local.json')
)

$ErrorActionPreference = 'Stop'

$SettingsLocalFileName = 'settings.local.json'
$ProjectFileName = 'player-assistant.csproj'
$SettingsFormat = 'app-protected-v2'
$PreviousSettingsFormat = 'app-protected-v1'
$LegacySettingsFormat = 'dpapi-current-user'
$SettingsEncryptionSeed = 'PlayerAssistant.LocalSettings.v1'
$KeywordIndexFileName = 'keyword-index.json'
$KeywordTermsFileName = 'game-posts-key-terms.md'
$SitemapFileName = 'sitemap.xml'
$SitemapKeywordUrlsFileName = 'sitemap-keyword-urls.json'
$ReleaseManifestFileName = 'release-manifest.json'
$SensitiveFileNames = @(
    'rpol-storage-state.json'
)
$ForbiddenPublishFileNames = @(
    'startup-errors.log',
    'startup-health.json',
    'last-crash.json',
    'startup-remediation.txt'
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
$ProcessLockDiagnosticsScriptPath = Join-Path $PSScriptRoot 'diagnose-player-assistant-locks.ps1'

function Get-ProjectVersionInfo {
    $projectPath = Join-Path $PSScriptRoot $ProjectFileName
    Assert-RequiredFile -Path $projectPath -Description $ProjectFileName

    [xml]$project = Get-Content -Raw -LiteralPath $projectPath
    $propertyGroup = @($project.Project.PropertyGroup | Where-Object { $_.Version -or $_.FileVersion -or $_.InformationalVersion } | Select-Object -First 1)
    if ($propertyGroup.Count -eq 0) {
        throw "$ProjectFileName does not define Version, FileVersion, or InformationalVersion."
    }

    $version = [string]$propertyGroup[0].Version
    $fileVersion = [string]$propertyGroup[0].FileVersion
    $informationalVersion = [string]$propertyGroup[0].InformationalVersion

    if ([string]::IsNullOrWhiteSpace($version) -or
        [string]::IsNullOrWhiteSpace($fileVersion) -or
        [string]::IsNullOrWhiteSpace($informationalVersion)) {
        throw "$ProjectFileName must define non-empty Version, FileVersion, and InformationalVersion."
    }

    return [pscustomobject]@{
        Version = $version
        FileVersion = $fileVersion
        InformationalVersion = $informationalVersion
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

function Get-SettingsEncryptionKey {
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ,$sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($SettingsEncryptionSeed))
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-SettingsAuthenticationKey {
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

function ConvertFrom-AppEncryptedLocalSettings {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

    $raw = Get-Content -Raw -LiteralPath $SettingsPath
    $envelope = $raw | ConvertFrom-Json
    if ($envelope.format -ne $SettingsFormat -and $envelope.format -ne $PreviousSettingsFormat) {
        throw "$SettingsLocalFileName must use encrypted format '$SettingsFormat' or '$PreviousSettingsFormat', but found '$($envelope.format)'."
    }

    if ([string]::IsNullOrWhiteSpace($envelope.payload)) {
        throw "$SettingsLocalFileName has an empty encrypted payload."
    }

    $payloadBytes = [Convert]::FromBase64String($envelope.payload)
    if ($envelope.format -eq $SettingsFormat) {
        if ($payloadBytes.Length -lt 49) {
            throw "$SettingsLocalFileName authenticated encrypted payload is too short."
        }

        $tag = [byte[]]::new(32)
        $protectedContent = [byte[]]::new($payloadBytes.Length - $tag.Length)
        [System.Buffer]::BlockCopy($payloadBytes, 0, $protectedContent, 0, $protectedContent.Length)
        [System.Buffer]::BlockCopy($payloadBytes, $protectedContent.Length, $tag, 0, $tag.Length)
        $hmac = [System.Security.Cryptography.HMACSHA256]::new((Get-SettingsAuthenticationKey))
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
        $aes.Key = Get-SettingsEncryptionKey
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
        return $plaintextJson | ConvertFrom-Json
    }

    if ($payloadBytes.Length -lt 17) {
        throw "$SettingsLocalFileName encrypted payload is too short."
    }

    $iv = [byte[]]::new(16)
    $ciphertext = [byte[]]::new($payloadBytes.Length - $iv.Length)
    [System.Buffer]::BlockCopy($payloadBytes, 0, $iv, 0, $iv.Length)
    [System.Buffer]::BlockCopy($payloadBytes, $iv.Length, $ciphertext, 0, $ciphertext.Length)

    $aes = [System.Security.Cryptography.Aes]::Create()
    $aes.Key = Get-SettingsEncryptionKey
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
    return $plaintextJson | ConvertFrom-Json
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
    return $plaintextJson | ConvertFrom-Json
}

function Test-IsEncryptedLocalSettings {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings
    )

    return $Settings.PSObject.Properties['format'] `
        -and $Settings.PSObject.Properties['payload'] `
        -and ($Settings.format -eq $SettingsFormat -or $Settings.format -eq $PreviousSettingsFormat -or $Settings.format -eq $LegacySettingsFormat)
}

function ConvertFrom-LocalSettingsFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

    $settings = Get-Content -Raw -LiteralPath $SettingsPath | ConvertFrom-Json
    if (Test-IsEncryptedLocalSettings -Settings $settings) {
        if ($settings.format -eq $SettingsFormat -or $settings.format -eq $PreviousSettingsFormat) {
            return ConvertFrom-AppEncryptedLocalSettings -SettingsPath $SettingsPath
        }

        return ConvertFrom-LegacyDpapiLocalSettings -SettingsPath $SettingsPath
    }

    return $settings
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
    $aes.Key = Get-SettingsEncryptionKey
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
    $hmac = [System.Security.Cryptography.HMACSHA256]::new((Get-SettingsAuthenticationKey))
    try {
        $tag = $hmac.ComputeHash($protectedContent)
    }
    finally {
        $hmac.Dispose()
    }

    $payloadBytes = [byte[]]::new($protectedContent.Length + $tag.Length)
    [System.Buffer]::BlockCopy($protectedContent, 0, $payloadBytes, 0, $protectedContent.Length)
    [System.Buffer]::BlockCopy($tag, 0, $payloadBytes, $protectedContent.Length, $tag.Length)

    $envelope = [pscustomobject]@{
        format = $SettingsFormat
        payload = [Convert]::ToBase64String($payloadBytes)
    }

    $envelope | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $DestinationPath -Encoding UTF8
}

function Assert-EncryptedLocalSettings {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$PublishedPath
    )

    Assert-RequiredFile -Path $PublishedPath -Description "published $SettingsLocalFileName"

    $publishedRaw = Get-Content -Raw -LiteralPath $PublishedPath
    if ($publishedRaw -match '"RPOL password"\s*:') {
        throw "Published $SettingsLocalFileName appears to contain plaintext RPOL credentials."
    }

    $sourceSettings = ConvertFrom-LocalSettingsFile -SettingsPath $SourcePath
    $publishedSettings = ConvertFrom-AppEncryptedLocalSettings -SettingsPath $PublishedPath

    foreach ($property in $sourceSettings.PSObject.Properties) {
        $publishedProperty = $publishedSettings.PSObject.Properties[$property.Name]
        if ($null -eq $publishedProperty) {
            throw "Published $SettingsLocalFileName is missing decrypted setting '$($property.Name)'."
        }

        if ([string]$publishedProperty.Value -ne [string]$property.Value) {
            throw "Published $SettingsLocalFileName did not decrypt back to the source value for '$($property.Name)'."
        }

        if (![string]::IsNullOrWhiteSpace([string]$property.Value) -and $publishedRaw.Contains([string]$property.Value)) {
            throw "Published $SettingsLocalFileName contains plaintext value for '$($property.Name)'."
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
    Assert-RequiredFile -Path (Join-Path $PSScriptRoot $SettingsLocalFileName) -Description $SettingsLocalFileName
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
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
}

function Get-ReleaseManifestFileList {
    return @(
        'player-assistant.exe',
        'settings.json',
        $SettingsLocalFileName,
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

        $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($actualHash -ne [string]$entry[0].sha256) {
            throw "$ReleaseManifestFileName SHA256 mismatch for '$relativePath'."
        }
    }
}

function Assert-PublishOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [string]$SourceSettingsPath
    )

    $settingsPath = Join-Path $Directory $SettingsLocalFileName

    Assert-PublishedExecutableVersion -Path (Join-Path $Directory 'player-assistant.exe')
    Assert-PublishedSettingsJson -Path (Join-Path $Directory 'settings.json')
    Assert-PublishedKeywordIndex -Path (Join-Path $Directory $KeywordIndexFileName) -Description 'published keyword index'
    Assert-PublishedKeywordTerms -Path (Join-Path $Directory $KeywordTermsFileName)
    Assert-PublishedSitemap -Path (Join-Path $Directory $SitemapFileName)
    [void](Read-JsonFile -Path (Join-Path $Directory $SitemapKeywordUrlsFileName) -Description 'published sitemap keyword URL library')
    Assert-PublishedPlaywrightRuntime -Directory (Join-Path $Directory '.playwright')
    Assert-EncryptedLocalSettings -SourcePath $SourceSettingsPath -PublishedPath $settingsPath
    Assert-NoSensitiveFiles -Directory $Directory
    Assert-NoForbiddenPublishArtifacts -Directory $Directory
    Assert-NoPlaintextCredentialMarkers -Directory $Directory
    Assert-ReleaseIntegrityManifest -Directory $Directory
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
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ProcessLockDiagnosticsScriptPath `
        -ReleasePath (Join-Path $PSScriptRoot 'Release\player-assistant.exe') `
        -PublishPath (Join-Path $PublishDirectory 'player-assistant.exe')
}

$resolvedOutputDir = Resolve-FullPath $OutputDir
Assert-PathInsideRepo -Path $resolvedOutputDir -Description 'publish output directory'

if ($VerifyOnly) {
    Assert-PublishInputs
    Assert-PublishOutput -Directory $resolvedOutputDir -SourceSettingsPath $SourceSettingsPath
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
    'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '--output',
    $resolvedOutputDir
)
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    Invoke-ProcessLockDiagnostics -PublishDirectory $resolvedOutputDir
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -Path $resolvedOutputDir -Filter *.pdb -File | Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Release\$KeywordIndexFileName") -Destination (Join-Path $resolvedOutputDir $KeywordIndexFileName) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Release\$KeywordTermsFileName") -Destination (Join-Path $resolvedOutputDir $KeywordTermsFileName) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Release\$SitemapFileName") -Destination (Join-Path $resolvedOutputDir $SitemapFileName) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Release\$SitemapKeywordUrlsFileName") -Destination (Join-Path $resolvedOutputDir $SitemapKeywordUrlsFileName) -Force
Write-AppEncryptedLocalSettings -SourcePath (Join-Path $PSScriptRoot $SettingsLocalFileName) -DestinationPath (Join-Path $resolvedOutputDir $SettingsLocalFileName)
Write-ReleaseIntegrityManifest -Directory $resolvedOutputDir

Assert-PublishOutput -Directory $resolvedOutputDir -SourceSettingsPath $SourceSettingsPath
Write-Output "Publish verified: $resolvedOutputDir"
