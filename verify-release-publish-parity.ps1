param(
    [string]$ReleaseDir = (Join-Path $PSScriptRoot 'Release'),
    [string]$PublishDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'

$ExecutableFileName = 'player-assistant.exe'
$RuntimeOnlyPublishFileNames = @(
    'startup-errors.log',
    'startup-health.json',
    'outbound-network-diagnostics.json',
    'last-crash.json',
    'startup-remediation.txt',
    'rpol-storage-state.json'
)
$ParityPairs = @(
    [pscustomobject]@{ ReleasePath = 'settings.json'; PublishPath = 'settings.json' },
    [pscustomobject]@{ ReleasePath = 'magic-items.json'; PublishPath = 'magic-items.json' },
    [pscustomobject]@{ ReleasePath = 'keyword-index.json'; PublishPath = 'keyword-index.json' },
    [pscustomobject]@{ ReleasePath = 'game-posts-key-terms.md'; PublishPath = 'game-posts-key-terms.md' },
    [pscustomobject]@{ ReleasePath = 'sitemap.xml'; PublishPath = 'sitemap.xml' },
    [pscustomobject]@{ ReleasePath = 'sitemap-keyword-urls.json'; PublishPath = 'sitemap-keyword-urls.json' }
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

    if (!$fullPath.StartsWith($repoRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase) -and
        !$fullPath.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
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

function Get-FileHashText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $getFileHashCommand = Get-Command Get-FileHash -ErrorAction SilentlyContinue
    if ($getFileHashCommand) {
        return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([System.BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '')
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-ExecutableVersionSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Assert-RequiredFile -Path $Path -Description "executable $Path"
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    return [pscustomobject]@{
        FileVersion = $version.FileVersion
        ProductVersion = $version.ProductVersion
        ProductName = $version.ProductName
        OriginalFileName = $version.OriginalFilename
    }
}

function Assert-ExecutableVersionsMatch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseExecutablePath,

        [Parameter(Mandatory = $true)]
        [string]$PublishExecutablePath
    )

    $releaseVersion = Get-ExecutableVersionSummary -Path $ReleaseExecutablePath
    $publishVersion = Get-ExecutableVersionSummary -Path $PublishExecutablePath
    foreach ($propertyName in @('FileVersion', 'ProductVersion', 'ProductName', 'OriginalFileName')) {
        if ([string]$releaseVersion.$propertyName -ne [string]$publishVersion.$propertyName) {
            throw "Executable version metadata differs for $propertyName. Release='$($releaseVersion.$propertyName)' Publish='$($publishVersion.$propertyName)'."
        }
    }
}

function Assert-ParityFilesMatch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseDirectory,

        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory,

        [Parameter(Mandatory = $true)]
        [object[]]$Pairs
    )

    $differences = [System.Collections.Generic.List[string]]::new()
    foreach ($pair in $Pairs) {
        $releaseRelativePath = [string]$pair.ReleasePath
        $publishRelativePath = [string]$pair.PublishPath
        $releasePath = Join-Path $ReleaseDirectory $releaseRelativePath
        $publishPath = Join-Path $PublishDirectory $publishRelativePath
        Assert-RequiredFile -Path $releasePath -Description "Release $releaseRelativePath"
        Assert-RequiredFile -Path $publishPath -Description "published $publishRelativePath"

        $releaseHash = Get-FileHashText -Path $releasePath
        $publishHash = Get-FileHashText -Path $publishPath
        if ($releaseHash -ne $publishHash) {
            [void]$differences.Add("$releaseRelativePath -> $publishRelativePath SHA256 differs")
        }
    }

    if ($differences.Count -gt 0) {
        Write-Output "Release/publish parity differences:"
        $differences | ForEach-Object { Write-Output "  $_" }
        throw "Release/publish parity verification failed."
    }
}

function Assert-NoRuntimeOnlyPublishFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory
    )

    foreach ($fileName in $RuntimeOnlyPublishFileNames) {
        $matches = @(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -Force -File -Filter $fileName -ErrorAction SilentlyContinue)
        if ($matches.Count -gt 0) {
            $paths = $matches | ForEach-Object { $_.FullName }
            throw "Published output contains runtime-only file '$fileName': $($paths -join ', ')"
        }
    }
}

$resolvedReleaseDir = Resolve-FullPath $ReleaseDir
$resolvedPublishDir = Resolve-FullPath $PublishDir
Assert-PathInsideRepo -Path $resolvedReleaseDir -Description 'Release directory'
Assert-PathInsideRepo -Path $resolvedPublishDir -Description 'publish directory'

if ($PlanOnly) {
    Write-Output "Release/publish parity plan:"
    Write-Output "  ReleaseDir: $resolvedReleaseDir"
    Write-Output "  PublishDir: $resolvedPublishDir"
    Write-Output "  Executable metadata: $ExecutableFileName"
    Write-Output "  Hash-matched file pairs:"
    $ParityPairs | ForEach-Object { Write-Output "    $($_.ReleasePath) -> $($_.PublishPath)" }
    Write-Output "  Runtime-only files forbidden in publish:"
    $RuntimeOnlyPublishFileNames | ForEach-Object { Write-Output "    $_" }
    return
}

Assert-ParityFilesMatch -ReleaseDirectory $resolvedReleaseDir -PublishDirectory $resolvedPublishDir -Pairs $ParityPairs
Assert-ExecutableVersionsMatch `
    -ReleaseExecutablePath (Join-Path $resolvedReleaseDir $ExecutableFileName) `
    -PublishExecutablePath (Join-Path $resolvedPublishDir $ExecutableFileName)
Assert-NoRuntimeOnlyPublishFiles -PublishDirectory $resolvedPublishDir

Write-Output "Release/publish parity verification passed."
Write-Output "  Hash-matched file pairs: $($ParityPairs.Count)"
Write-Output "  Executable metadata matched: $ExecutableFileName"
