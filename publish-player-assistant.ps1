param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'

$SettingsLocalFileName = 'settings.local.json'
$SettingsFormat = 'app-protected-v1'
$LegacySettingsFormat = 'dpapi-current-user'
$SettingsEncryptionSeed = 'PlayerAssistant.LocalSettings.v1'
$KeywordIndexFileName = 'keyword-index.json'
$KeywordTermsFileName = 'game-posts-key-terms.md'
$SitemapFileName = 'sitemap.xml'
$SensitiveFileNames = @(
    'rpol-storage-state.json'
)
$ForbiddenPublishFileNames = @(
    'startup-errors.log'
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

function ConvertFrom-AppEncryptedLocalSettings {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

    $raw = Get-Content -Raw -LiteralPath $SettingsPath
    $envelope = $raw | ConvertFrom-Json
    if ($envelope.format -ne $SettingsFormat) {
        throw "$SettingsLocalFileName must use encrypted format '$SettingsFormat', but found '$($envelope.format)'."
    }

    if ([string]::IsNullOrWhiteSpace($envelope.payload)) {
        throw "$SettingsLocalFileName has an empty encrypted payload."
    }

    $payloadBytes = [Convert]::FromBase64String($envelope.payload)
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
        -and ($Settings.format -eq $SettingsFormat -or $Settings.format -eq $LegacySettingsFormat)
}

function ConvertFrom-LocalSettingsFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

    $settings = Get-Content -Raw -LiteralPath $SettingsPath | ConvertFrom-Json
    if (Test-IsEncryptedLocalSettings -Settings $settings) {
        if ($settings.format -eq $SettingsFormat) {
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

    $aes = [System.Security.Cryptography.Aes]::Create()
    $aes.Key = Get-SettingsEncryptionKey
    $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
    $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
    $aes.GenerateIV()
    $iv = [byte[]]$aes.IV.Clone()
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

    $payloadBytes = [byte[]]::new($iv.Length + $ciphertext.Length)
    [System.Buffer]::BlockCopy($iv, 0, $payloadBytes, 0, $iv.Length)
    [System.Buffer]::BlockCopy($ciphertext, 0, $payloadBytes, $iv.Length, $ciphertext.Length)

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
    Assert-RequiredFile -Path (Join-Path $PSScriptRoot $SettingsLocalFileName) -Description $SettingsLocalFileName
}

function Assert-PublishOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $settingsPath = Join-Path $Directory $SettingsLocalFileName

    Assert-RequiredFile -Path (Join-Path $Directory 'player-assistant.exe') -Description 'published executable'
    Assert-RequiredFile -Path (Join-Path $Directory 'settings.json') -Description 'published settings.json'
    Assert-RequiredFile -Path (Join-Path $Directory $KeywordIndexFileName) -Description 'published keyword index'
    Assert-RequiredFile -Path (Join-Path $Directory 'keyword-index.md') -Description 'published keyword index markdown sidecar'
    Assert-RequiredFile -Path (Join-Path $Directory $KeywordTermsFileName) -Description 'published keyword terms file'
    Assert-RequiredFile -Path (Join-Path $Directory $SitemapFileName) -Description 'published sitemap'
    Assert-RequiredDirectory -Path (Join-Path $Directory '.playwright') -Description 'published Playwright runtime'
    Assert-EncryptedLocalSettings -SourcePath (Join-Path $PSScriptRoot $SettingsLocalFileName) -PublishedPath $settingsPath
    Assert-NoSensitiveFiles -Directory $Directory
    Assert-NoForbiddenPublishArtifacts -Directory $Directory
    Assert-NoPlaintextCredentialMarkers -Directory $Directory
}

$resolvedOutputDir = Resolve-FullPath $OutputDir
Assert-PathInsideRepo -Path $resolvedOutputDir -Description 'publish output directory'

if ($VerifyOnly) {
    Assert-PublishInputs
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

dotnet publish "$PSScriptRoot\player-assistant.csproj" `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $resolvedOutputDir

Get-ChildItem -Path $resolvedOutputDir -Filter *.pdb -File | Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Release\$KeywordIndexFileName") -Destination (Join-Path $resolvedOutputDir $KeywordIndexFileName) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Release\$KeywordIndexFileName") -Destination (Join-Path $resolvedOutputDir 'keyword-index.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Release\$KeywordTermsFileName") -Destination (Join-Path $resolvedOutputDir $KeywordTermsFileName) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Release\$SitemapFileName") -Destination (Join-Path $resolvedOutputDir $SitemapFileName) -Force
Write-AppEncryptedLocalSettings -SourcePath (Join-Path $PSScriptRoot $SettingsLocalFileName) -DestinationPath (Join-Path $resolvedOutputDir $SettingsLocalFileName)

Assert-PublishOutput -Directory $resolvedOutputDir
Write-Output "Publish verified: $resolvedOutputDir"
