[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot 'dist'
}
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$pwaRoot = Join-Path $repoRoot 'pwa'
$brokerRoot = Join-Path $repoRoot 'web-deploy\player-assistant-broker'
$apiRoot = Join-Path $repoRoot 'web-deploy\bryanmiller.us\scarlethorizons\api'
$layout = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'package-layout.json') | ConvertFrom-Json
[xml]$versions = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'version.props')
$version = [string]$versions.Project.PropertyGroup.PlayerAssistantPwaVersion
if ($version -notmatch '^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$') {
    throw "Invalid PWA package version: $version"
}

$stage = Join-Path ([IO.Path]::GetTempPath()) ("pa-online-installer-{0}" -f [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage 'payload'
$entries = [System.Collections.Generic.List[object]]::new()

function Copy-PayloadFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][ValidateSet('public', 'private')][string]$Visibility,
        [Parameter(Mandatory = $true)][string]$Mode,
        [string]$Substitution = ''
    )
    if (!(Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required installer payload file is missing: $Source"
    }
    $normalizedArchivePath = $ArchivePath.Replace('\', '/')
    if ($normalizedArchivePath -notmatch '^[A-Za-z0-9._/-]+$' -or $normalizedArchivePath -match '(^|/)\.\.($|/)') {
        throw "Unsafe installer archive path: $normalizedArchivePath"
    }
    $destination = Join-Path $payload ($normalizedArchivePath.Replace('/', '\'))
    $destinationDirectory = Split-Path -Parent $destination
    if (!(Test-Path -LiteralPath $destinationDirectory -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    }
    Copy-Item -LiteralPath $Source -Destination $destination
    $entry = [ordered]@{
        path = "payload/$normalizedArchivePath"
        sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        bytes = (Get-Item -LiteralPath $destination).Length
        visibility = $Visibility
        mode = $Mode
    }
    if ($Substitution -ne '') {
        $entry.substitution = $Substitution
    }
    $entries.Add([pscustomobject]$entry)
}

try {
    New-Item -ItemType Directory -Force -Path $payload | Out-Null

    foreach ($relative in @($layout.public_pwa_files)) {
        Copy-PayloadFile -Source (Join-Path $pwaRoot ([string]$relative)) `
            -ArchivePath ("public/scarlethorizons/pwa/{0}" -f ([string]$relative).Replace('\', '/')) `
            -Visibility public -Mode '0644'
    }
    foreach ($relativeDirectory in @($layout.public_pwa_directories)) {
        $sourceDirectory = Join-Path $pwaRoot ([string]$relativeDirectory)
        if (!(Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
            throw "Required PWA payload directory is missing: $sourceDirectory"
        }
        foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -File -Recurse | Sort-Object FullName) {
            $relative = $file.FullName.Substring($pwaRoot.Length).TrimStart('\', '/').Replace('\', '/')
            Copy-PayloadFile -Source $file.FullName `
                -ArchivePath "public/scarlethorizons/pwa/$relative" `
                -Visibility public -Mode '0644'
        }
    }

    foreach ($relative in @($layout.public_api_files)) {
        Copy-PayloadFile -Source (Join-Path $apiRoot ([string]$relative)) `
            -ArchivePath ("public/scarlethorizons/api/{0}" -f ([string]$relative).Replace('\', '/')) `
            -Visibility public -Mode '0644'
    }

    $apiSource = Join-Path $apiRoot 'index.php'
    $apiTemplate = Join-Path $stage 'api-index.php.template'
    $apiText = [IO.File]::ReadAllText($apiSource)
    $privateDirectoryLine = "`$privateDirectory = dirname(__DIR__, 3) . '/player-assistant-broker';"
    if (($apiText.Split([string[]]@($privateDirectoryLine), [StringSplitOptions]::None).Count - 1) -ne 1) {
        throw 'The public API private-directory substitution point is missing or ambiguous.'
    }
    $apiText = $apiText.Replace(
        $privateDirectoryLine,
        "`$privateDirectory = __PLAYER_ASSISTANT_PRIVATE_ROOT__;")
    [IO.File]::WriteAllText($apiTemplate, $apiText, [Text.UTF8Encoding]::new($false))
    Copy-PayloadFile -Source $apiTemplate `
        -ArchivePath 'public/scarlethorizons/api/index.php.template' `
        -Visibility public -Mode '0644' -Substitution 'private_root_php_literal'

    foreach ($relative in @($layout.private_runtime_files)) {
        Copy-PayloadFile -Source (Join-Path $brokerRoot ([string]$relative)) `
            -ArchivePath ("private/{0}" -f ([string]$relative).Replace('\', '/')) `
            -Visibility private -Mode '0600'
    }

    $manifest = [ordered]@{
        schema_version = 1
        product = 'player-assistant-web'
        version = $version
        fixed_url_layout = [ordered]@{
            pwa = '/scarlethorizons/pwa/'
            api = '/scarlethorizons/api/'
        }
        required_php_extensions = @('curl', 'openssl', 'pdo_sqlite', 'phar', 'sodium')
        files = @($entries | Sort-Object path)
    }
    $manifestPath = Join-Path $stage 'manifest.json'
    [IO.File]::WriteAllText(
        $manifestPath,
        ($manifest | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $archiveName = "player-assistant-web-payload-$version.tar"
    $archivePath = Join-Path $OutputDirectory $archiveName
    Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
    $tar = Join-Path $env:WINDIR 'System32\tar.exe'
    if (!(Test-Path -LiteralPath $tar -PathType Leaf)) {
        throw 'Native Windows tar.exe is required to build the online installer payload.'
    }
    & $tar -cf $archivePath -C $stage manifest.json payload
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        throw 'Unable to create the online installer payload archive.'
    }

    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(
        "$archivePath.sha256",
        "$archiveHash  $archiveName`n",
        [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-player-assistant-web.php') `
        -Destination (Join-Path $OutputDirectory 'install-player-assistant-web.php') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'config.template.php') `
        -Destination (Join-Path $OutputDirectory 'config.template.php') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') `
        -Destination (Join-Path $OutputDirectory 'README.md') -Force

    Write-Output "Online PWA installer payload created: $archivePath"
    Write-Output "Payload SHA-256: $archiveHash"
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
