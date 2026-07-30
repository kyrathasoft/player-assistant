param(
    [string] $PhpPath,
    [string] $PhpIniPath,
    [string[]] $Extensions = @('curl', 'pdo_sqlite', 'sqlite3')
)

$ErrorActionPreference = 'Stop'

Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($PhpPath)) {
    $phpFromPath = Get-Command php -ErrorAction SilentlyContinue
    if ($null -eq $phpFromPath) {
        throw 'Unable to locate php.exe. Pass -PhpPath explicitly.'
    }
    $PhpPath = $phpFromPath.Source
}

$resolvedPhp = Resolve-Path -LiteralPath $PhpPath
$phpItem = Get-Item -LiteralPath $resolvedPhp.Path
if ($phpItem.PSIsContainer) {
    $phpExecutable = Join-Path $phpItem.FullName 'php.exe'
} else {
    $phpExecutable = $phpItem.FullName
}

if (-not (Test-Path -LiteralPath $phpExecutable)) {
    throw "php executable not found at '$phpExecutable'."
}

$phpRoot = Split-Path -Parent $phpExecutable
$extDir = Join-Path $phpRoot 'ext'
if (-not (Test-Path -LiteralPath $extDir)) {
    throw "PHP extension directory not found at '$extDir'."
}

function Resolve-PhpIniPath {
    param(
        [string] $Executable,
        [string] $ExplicitIni,
        [string] $DefaultIni
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitIni)) {
        return (Resolve-Path -LiteralPath $ExplicitIni).Path
    }

    $iniOutput = & $Executable --ini
    $loadedIni = (
        $iniOutput |
        Select-String -Pattern 'Loaded Configuration File'
    ) |
    ForEach-Object { $_.Line } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object {
        if ($_ -match 'Loaded Configuration File.*=>\s*(.+)$') { $Matches[1].Trim() } else { '' }
    } |
    Select-Object -First 1

    if (-not [string]::IsNullOrWhiteSpace($loadedIni) -and $loadedIni -ne '(none)') {
        return $loadedIni
    }

    $phpIni = Join-Path $DefaultIni 'php.ini'
    if (-not (Test-Path -LiteralPath $phpIni)) {
        $devIni = Join-Path $DefaultIni 'php.ini-development'
        $prodIni = Join-Path $DefaultIni 'php.ini-production'
        $template = $null
        if (Test-Path -LiteralPath $devIni) {
            $template = $devIni
        } elseif (Test-Path -LiteralPath $prodIni) {
            $template = $prodIni
        } else {
            throw "No php.ini exists and no php.ini-development/php.ini-production template was found under '$DefaultIni'."
        }

        Copy-Item -LiteralPath $template -Destination $phpIni -Force
    }

    return $phpIni
}

$phpIniPath = Resolve-PhpIniPath -Executable $phpExecutable -ExplicitIni $PhpIniPath -DefaultIni $phpRoot

if (-not (Test-Path -LiteralPath $phpIniPath)) {
    throw "php.ini not found at '$phpIniPath'."
}

$lines = Get-Content -LiteralPath $phpIniPath -Encoding UTF8
$normalizedExtensions = $Extensions | ForEach-Object { $_.Trim().ToLowerInvariant() } | Sort-Object -Unique
$requiredSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$normalizedExtensions | ForEach-Object { [void]$requiredSet.Add($_) }

$filteredLines = @()
$seenExtensionDir = $false
$extensionDirLineUpdated = $false

foreach ($line in $lines) {
    $trimmed = $line.Trim()
    if ($trimmed -match '^\s*extension_dir\s*=') {
        $filteredLines += "extension_dir = `"$extDir`""
        $seenExtensionDir = $true
        $extensionDirLineUpdated = $true
        continue
    }

    if ($trimmed -match '^\s*;?\s*(?:zend_)?extension\s*=\s*["'']?\s*([^;"'']+)\s*["'']?\s*(?:;.*)?$') {
        $token = $matches[1]
        $token = $token.Trim()
        if ($token.Contains('\')) {
            $token = [IO.Path]::GetFileName($token)
        }
        $token = $token.ToLowerInvariant() -replace '^php_', '' -replace '\.dll$', ''
        if ($requiredSet.Contains($token)) {
            continue
        }
        $normalizedToken = $token -replace '\.so$', ''
        if ($requiredSet.Contains($normalizedToken)) {
            continue
        }
    }

    $filteredLines += $line
}

if (-not $seenExtensionDir) {
    $filteredLines += "extension_dir = `"$extDir`""
}

foreach ($extension in $normalizedExtensions) {
    $dllName = switch ($extension) {
        'curl'       { 'php_curl.dll' }
        'pdo_sqlite' { 'php_pdo_sqlite.dll' }
        'sqlite3'    { 'php_sqlite3.dll' }
        default      { "php_$extension.dll" }
    }
    $dllPath = Join-Path $extDir $dllName
    if (-not (Test-Path -LiteralPath $dllPath)) {
        throw "Missing extension binary '$dllName' in '$extDir'."
    }

    $filteredLines += "extension=$extension"
}

if ($extensionDirLineUpdated) {
    Write-Output "Updated extension_dir in '$phpIniPath'."
}

Set-Content -LiteralPath $phpIniPath -Value $filteredLines -Encoding UTF8

Write-Output "Enabled extensions in '$phpIniPath':"
$normalizedExtensions | ForEach-Object { Write-Output " - $_" }
Write-Output "Verifying modules using '$phpExecutable -n -c `"$phpIniPath`" -m'..."

$modules = & $phpExecutable -n -c $phpIniPath -m 2>$null
$moduleSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$modules | ForEach-Object { [void]$moduleSet.Add($_.Trim()) }

$missing = @()
foreach ($extension in $normalizedExtensions) {
    if (-not $moduleSet.Contains($extension)) {
        $missing += $extension
    }
}

if ($missing.Count -gt 0) {
    Write-Output "Missing extensions after edit: $($missing -join ', ')"
    Write-Output 'Run:'
    Write-Output "  $phpExecutable -n -c `"$phpIniPath`" -m"
    throw "Activation verification failed."
}

Write-Output 'PHP CLI extensions are now active.'
Write-Output "PHP_INI=$phpIniPath"
