[CmdletBinding()]
param(
    [ValidateSet('Generate', 'Verify')][string]$Mode = 'Verify',
    [string]$Root,
    [string]$ManifestPath,
    [string]$InventoryPath,
    [string]$SourceRevision
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = $PSScriptRoot }
if ([string]::IsNullOrWhiteSpace($ManifestPath)) { $ManifestPath = Join-Path $Root 'Release\release-manifest.json' }
if ([string]::IsNullOrWhiteSpace($InventoryPath)) { $InventoryPath = Join-Path $Root 'release-manifest.inventory.json' }
$Utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Normalize-RelativePath([string]$Path) {
    return ($Path.Replace('\', '/').TrimStart('./'))
}
function Get-Hash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}
function Test-Glob([string]$Path, [string]$Pattern) {
    $regex = [regex]::Escape((Normalize-RelativePath $Pattern)).Replace('\*\*', '.*').Replace('\*', '[^/]*').Replace('\?', '[^/]')
    return [regex]::IsMatch((Normalize-RelativePath $Path), "^$regex$", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
}
function Get-RelativePath([string]$BasePath, [string]$TargetPath) {
    $base = [IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $target = [IO.Path]::GetFullPath($TargetPath)
    if (!$target.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw "Path is outside root: $TargetPath" }
    return Normalize-RelativePath $target.Substring($base.Length)
}
function Assert-SafeRelativePath([string]$Path) {
    $p = Normalize-RelativePath $Path
    if ([string]::IsNullOrWhiteSpace($p) -or $p.StartsWith('/') -or $p.Contains('../') -or $p -match '(^|/)\.\.($|/)') { throw "Unsafe manifest path '$Path'." }
    return $p
}
function Get-Inventory {
    if (!(Test-Path -LiteralPath $InventoryPath -PathType Leaf)) { throw "Inventory is missing: $InventoryPath" }
    $inventory = Get-Content -Raw -LiteralPath $InventoryPath | ConvertFrom-Json
    if ($inventory.schema_version -ne 1 -or $inventory.hash_algorithm -ne 'SHA256') { throw 'Unsupported release inventory schema or hash algorithm.' }
    return $inventory
}
function Get-CandidatePaths($Inventory) {
    $root = [IO.Path]::GetFullPath($Root)
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in @($Inventory.roots)) {
        $relative = Assert-SafeRelativePath ([string]$entry)
        $full = Join-Path $root $relative
        if (Test-Path -LiteralPath $full -PathType Leaf) { [void]$paths.Add($relative); continue }
        if (!(Test-Path -LiteralPath $full -PathType Container)) { throw "Inventory root is missing: $relative" }
        Get-ChildItem -LiteralPath $full -File -Recurse -Force | ForEach-Object {
            $candidate = Get-RelativePath $root $_.FullName
            [void]$paths.Add($candidate)
        }
    }
    return @($paths | Sort-Object { $_ })
}
function Test-Excluded([string]$Path, $Inventory) {
    foreach ($pattern in @($Inventory.exclude, $Inventory.mutable)) { foreach ($item in @($pattern)) { if (Test-Glob $Path ([string]$item)) { return $true } } }
    return $false
}
function Assert-NoForbiddenFiles($Inventory) {
    foreach ($relative in Get-CandidatePaths $Inventory) {
        foreach ($pattern in @($Inventory.forbidden)) {
            if (Test-Glob $relative ([string]$pattern)) { throw "Forbidden private or secret-shaped file is in the release inventory: $relative" }
        }
    }
}
function Assert-SourcePackageParity($Inventory) {
    foreach ($pair in @($Inventory.source_package_pairs)) {
        $sourceRelative = Assert-SafeRelativePath ([string]$pair.source)
        $distributionRelative = Assert-SafeRelativePath ([string]$pair.distribution)
        $source = Join-Path $Root $sourceRelative
        $distribution = Join-Path $Root $distributionRelative
        if (!(Test-Path -LiteralPath $source -PathType Leaf) -or !(Test-Path -LiteralPath $distribution -PathType Leaf)) { throw "Source/package pair is incomplete: $sourceRelative -> $distributionRelative" }
        if ((Get-Hash $source) -ne (Get-Hash $distribution)) { throw "Source/package drift detected: $sourceRelative -> $distributionRelative" }
    }
}
function Get-ToolIdentity {
    $ps = $PSVersionTable.PSVersion.ToString()
    $dotnet = try { (& dotnet --version 2>$null).Trim() } catch { 'unavailable' }
    return [ordered]@{ powershell = $ps; dotnet = $dotnet }
}
function Get-Entries($Inventory) {
    $manifestRelative = Get-RelativePath $Root $ManifestPath
    $entries = foreach ($relative in Get-CandidatePaths $Inventory) {
        if (Test-Excluded $relative $Inventory -or $relative -eq $manifestRelative) { continue }
        $full = Join-Path $Root $relative
        $item = Get-Item -LiteralPath $full
        [ordered]@{ path = $relative; bytes = [int64]$item.Length; sha256 = Get-Hash $full }
    }
    return @($entries | Sort-Object { $_.path })
}
function ConvertTo-CanonicalJson($Object) {
    return (($Object | ConvertTo-Json -Depth 12) -replace "`r`n", "`n")
}
function Get-Revision {
    if (![string]::IsNullOrWhiteSpace($SourceRevision)) { return $SourceRevision }
    try { return (& git -C $Root rev-parse HEAD 2>$null).Trim() } catch { return 'unavailable' }
}
function New-Manifest {
    $inventory = Get-Inventory
    Assert-NoForbiddenFiles $inventory
    Assert-SourcePackageParity $inventory
    $entries = Get-Entries $inventory
    return [ordered]@{
        schema_version = 1
        hash_algorithm = 'SHA256'
        source_revision = Get-Revision
        tools = Get-ToolIdentity
        inventory = [IO.Path]::GetFileName($InventoryPath)
        exclusions = @($Inventory.exclude + $Inventory.mutable | Sort-Object)
        files = $entries
    }
}
function Write-Manifest($Manifest) {
    $json = ConvertTo-CanonicalJson $Manifest
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($ManifestPath), $json + "`n", $Utf8NoBom)
}
function Verify-Manifest {
    if (!(Test-Path -LiteralPath $ManifestPath -PathType Leaf)) { throw "Release manifest is missing: $ManifestPath" }
    $expected = New-Manifest
    $actual = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
    if ($actual.schema_version -ne 1 -or $actual.hash_algorithm -ne 'SHA256') { throw 'Unsupported release manifest schema or hash algorithm.' }
    $actualJson = ConvertTo-CanonicalJson $actual
    $expectedJson = ConvertTo-CanonicalJson $expected
    if ($actualJson -ne $expectedJson) { throw 'Release manifest is not reproducible or its inventory/source revision differs.' }
    $actualPaths = @($actual.files | ForEach-Object { [string]$_.path })
    if ((@($actualPaths | Sort-Object) -join "`n") -cne ($actualPaths -join "`n")) { throw 'Release manifest entries are not in canonical path order.' }
    Write-Output "Release manifest verified: $($actualPaths.Count) files, source revision $($actual.source_revision)."
}

if ($Mode -eq 'Generate') { $manifest = New-Manifest; New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ManifestPath))) | Out-Null; Write-Manifest $manifest; Verify-Manifest } else { Verify-Manifest }
