param(
    [string]$AppDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [switch]$RequireReadOnlyAttribute,
    [switch]$RequireInstallerScriptProtection,
    [string]$InstallerScriptPath = (Join-Path $PSScriptRoot 'Installer\install-player-assistant.ps1')
)

$ErrorActionPreference = 'Stop'

$SettingsLocalFileName = 'settings.local.json'
$XpPasswordFileName = 'xp-passwords.json'
$XpPasswordFormat = 'xp-password-hashes-v2'
$XpPasswordSchemaVersion = 2
$XpPasswordAlgorithm = 'PBKDF2-HMAC-SHA256'
$XpPasswordMinimumIterations = 600000
$RequiredSidecarFileNames = @(
    $XpPasswordFileName
)
$AllowedEncryptedFormats = @(
    'app-protected-v1',
    'app-protected-v2',
    'app-protected-v3'
)
$ForbiddenPlaintextMarkers = @(
    '"RPOL password"',
    '"RPOL user name"',
    'Lucian99!',
    'gemstone',
    'spell-component',
    'killzone',
    'mystic-cleric'
)
$ForbiddenRuntimeFileNames = @(
    'rpol-storage-state.json',
    'startup-errors.log',
    'startup-health.json',
    'last-crash.json',
    'startup-remediation.txt'
)
$KnownWritableRuntimeDirectoryNames = @(
    'Posts',
    'PCs',
    'Images',
    'temp'
)

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
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

function Assert-EncryptedSidecar {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$FileName
    )

    Assert-RequiredFile -Path $Path -Description "runtime sidecar $FileName"

    $raw = Get-Content -Raw -LiteralPath $Path
    foreach ($marker in $ForbiddenPlaintextMarkers) {
        if ($raw.Contains($marker)) {
            throw "Runtime sidecar $FileName contains plaintext sensitive marker '$marker'."
        }
    }

    try {
        $json = $raw | ConvertFrom-Json
    }
    catch {
        throw "Runtime sidecar $FileName is not valid JSON: $($_.Exception.Message)"
    }

    if ($json.schema_version -ne 1) {
        throw "Runtime sidecar $FileName must declare schema_version 1."
    }

    if ($AllowedEncryptedFormats -notcontains [string]$json.format) {
        throw "Runtime sidecar $FileName must use an approved encrypted app-protected format."
    }

    if ([string]::IsNullOrWhiteSpace([string]$json.payload)) {
        throw "Runtime sidecar $FileName has an empty encrypted payload."
    }
}

function Assert-XpPasswordHashSidecar {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$FileName
    )

    Assert-RequiredFile -Path $Path -Description "runtime sidecar $FileName"
    $raw = Get-Content -Raw -LiteralPath $Path
    foreach ($marker in $ForbiddenPlaintextMarkers) {
        if ($raw.Contains($marker)) {
            throw "Runtime sidecar $FileName contains plaintext sensitive marker '$marker'."
        }
    }

    try {
        $json = $raw | ConvertFrom-Json
    }
    catch {
        throw "Runtime sidecar $FileName is not valid JSON: $($_.Exception.Message)"
    }

    if ($json.schema_version -ne $XpPasswordSchemaVersion -or $json.format -ne $XpPasswordFormat) {
        throw "Runtime sidecar $FileName must use salted password hash format '$XpPasswordFormat' with schema_version $XpPasswordSchemaVersion."
    }

    $unexpectedDocumentProperties = @($json.PSObject.Properties.Name | Where-Object {
        @('schema_version', 'format', 'entries') -notcontains $_
    })
    if ($unexpectedDocumentProperties.Count -gt 0) {
        throw "Runtime sidecar $FileName contains unexpected property '$($unexpectedDocumentProperties[0])'."
    }

    $entries = @($json.entries)
    if ($entries.Count -eq 0) {
        throw "Runtime sidecar $FileName does not contain any password hash entries."
    }

    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $canonicalIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $aliases = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $salts = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($entry in $entries) {
        $canonicalName = [string]$entry.canonical_name
        if ([string]::IsNullOrWhiteSpace($canonicalName) -or $canonicalName -cne $canonicalName.Trim()) {
            throw "Runtime sidecar $FileName contains a blank or untrimmed canonical name."
        }
        if (!$names.Add(([regex]::Replace($canonicalName.Trim(), '\s+', ' ')).ToUpperInvariant())) {
            throw "Runtime sidecar $FileName contains duplicate canonical name '$canonicalName'."
        }
    }
    foreach ($entry in $entries) {
        $unexpectedEntryProperties = @($entry.PSObject.Properties.Name | Where-Object {
            @('canonical_name', 'canonical_id', 'aliases', 'algorithm', 'iterations', 'salt', 'hash') -notcontains $_
        })
        if ($unexpectedEntryProperties.Count -gt 0) {
            throw "Runtime sidecar $FileName contains unexpected entry property '$($unexpectedEntryProperties[0])'."
        }

        $canonicalName = [string]$entry.canonical_name
        $canonicalId = [string]$entry.canonical_id
        if ([string]::IsNullOrWhiteSpace($canonicalId) -or $canonicalId -cne $canonicalId.Trim() -or !$canonicalIds.Add($canonicalId)) {
            throw "Runtime sidecar $FileName contains a blank or duplicate canonical ID."
        }

        if (!$entry.PSObject.Properties['aliases'] -or $null -eq $entry.aliases) {
            throw "Runtime sidecar $FileName entry '$canonicalName' must declare an aliases array."
        }
        $entryAliasNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($alias in @($entry.aliases)) {
            $aliasText = [string]$alias
            if ([string]::IsNullOrWhiteSpace($aliasText) -or $aliasText -cne $aliasText.Trim()) {
                throw "Runtime sidecar $FileName entry '$canonicalName' contains a blank or untrimmed alias."
            }
            $normalizedAlias = ([regex]::Replace($aliasText.Trim(), '\s+', ' ')).ToUpperInvariant()
            if ($names.Contains($normalizedAlias) -or !$entryAliasNames.Add($normalizedAlias) -or !$aliases.Add($normalizedAlias)) {
                throw "Runtime sidecar $FileName entry '$canonicalName' contains a duplicate or colliding alias '$aliasText'."
            }
        }

        if ($entry.algorithm -ne $XpPasswordAlgorithm -or [int64]$entry.iterations -lt $XpPasswordMinimumIterations) {
            throw "Runtime sidecar $FileName contains unsupported password hash parameters."
        }

        try {
            $saltBytes = [Convert]::FromBase64String([string]$entry.salt)
            $hashBytes = [Convert]::FromBase64String([string]$entry.hash)
        }
        catch {
            throw "Runtime sidecar $FileName contains invalid base64 hash data."
        }

        if ($saltBytes.Length -lt 16 -or $hashBytes.Length -ne 32 -or !$salts.Add([string]$entry.salt)) {
            throw "Runtime sidecar $FileName contains invalid or reused password hash data."
        }
    }
}

function Assert-SidecarReadOnlyAttribute {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$FileName
    )

    if (!$RequireReadOnlyAttribute) {
        return
    }

    $item = Get-Item -LiteralPath $Path
    if (!$item.IsReadOnly) {
        throw "Runtime sidecar $FileName must be marked read-only."
    }
}

function Assert-NoForbiddenRuntimeFiles {
    param([Parameter(Mandatory = $true)][string]$Directory)

    foreach ($fileName in $ForbiddenRuntimeFileNames) {
        $matches = @(Get-ChildItem -LiteralPath $Directory -Recurse -Force -File -Filter $fileName)
        if ($matches.Count -gt 0) {
            $paths = $matches | ForEach-Object { $_.FullName }
            throw "Runtime sidecar validation found forbidden runtime artifact '$fileName': $($paths -join ', ')"
        }
    }
}

function Assert-NoWritableRuntimeDirectoriesInAppDir {
    param([Parameter(Mandatory = $true)][string]$Directory)

    foreach ($directoryName in $KnownWritableRuntimeDirectoryNames) {
        $path = Join-Path $Directory $directoryName
        if (Test-Path -LiteralPath $path -PathType Container) {
            throw "Writable runtime directory '$directoryName' must not be shipped under the application directory. Use ProgramData or LocalAppData runtime roots instead."
        }
    }
}

function Assert-InstallerProtectsSidecars {
    if (!$RequireInstallerScriptProtection) {
        return
    }

    Assert-RequiredFile -Path $InstallerScriptPath -Description 'installer script'
    $script = Get-Content -Raw -LiteralPath $InstallerScriptPath
    foreach ($requiredText in @(
        'Protect-EncryptedSidecars',
        'Assert-ProtectedEncryptedSidecars',
        'xp-passwords.json',
        'settings.local.json',
        'icacls.exe',
        'S-1-5-32-545'
    )) {
        if (!$script.Contains($requiredText)) {
            throw "Installer script does not include required sidecar protection marker '$requiredText'."
        }
    }
}

$resolvedAppDir = Resolve-FullPath $AppDir
Assert-RequiredDirectory -Path $resolvedAppDir -Description 'application runtime directory'

foreach ($fileName in $RequiredSidecarFileNames) {
    $path = Join-Path $resolvedAppDir $fileName
    Assert-XpPasswordHashSidecar -Path $path -FileName $fileName
    Assert-SidecarReadOnlyAttribute -Path $path -FileName $fileName
}

if ($RequireInstallerScriptProtection) {
    $settingsLocalPath = Join-Path $resolvedAppDir $SettingsLocalFileName
    Assert-EncryptedSidecar -Path $settingsLocalPath -FileName $SettingsLocalFileName
    Assert-SidecarReadOnlyAttribute -Path $settingsLocalPath -FileName $SettingsLocalFileName
}

Assert-NoForbiddenRuntimeFiles -Directory $resolvedAppDir
Assert-NoWritableRuntimeDirectoriesInAppDir -Directory $resolvedAppDir
Assert-InstallerProtectsSidecars

$programDataRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'KyrathaSoft\player-assistant'
$localAppDataRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'KyrathaSoft\player-assistant'

Write-Output "Runtime sidecar validation passed: $resolvedAppDir"
Write-Output "Approved shared writable root: $programDataRoot"
Write-Output "Approved user writable root: $localAppDataRoot"
