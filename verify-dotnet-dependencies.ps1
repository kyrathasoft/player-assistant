[CmdletBinding()]
param(
    [string]$RepoRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path

$excludedDirectoryPattern = '[\\/](?:bin|obj|Release|Debug|codex-scratch|(?:bin|obj|Release|Debug)-[^\\/]+)(?:[\\/]|$)'
$projects = @(
    Get-ChildItem -LiteralPath $RepoRoot -Recurse -Filter '*.csproj' -File |
        Where-Object { $_.FullName -notmatch $excludedDirectoryPattern } |
        Sort-Object FullName
)

if ($projects.Count -eq 0) {
    throw 'No source .csproj files were found.'
}

foreach ($project in $projects) {
    $relativePath = Resolve-Path -LiteralPath $project.FullName -Relative
    $lockPath = Join-Path $project.DirectoryName 'packages.lock.json'
    if (!(Test-Path -LiteralPath $lockPath -PathType Leaf)) {
        throw "NuGet lock file is missing for $relativePath."
    }

    $restoreArguments = @($project.FullName, '--nologo', '--locked-mode')
    & dotnet restore @restoreArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Locked restore failed for $relativePath."
    }

    $scanOutput = & dotnet list $project.FullName package --vulnerable --include-transitive --format json --no-restore 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet vulnerability scan failed for $relativePath.`n$($scanOutput -join [Environment]::NewLine)"
    }

    try {
        $scan = ($scanOutput -join [Environment]::NewLine) | ConvertFrom-Json
    }
    catch {
        throw "NuGet vulnerability scan returned invalid JSON for $relativePath."
    }

    $vulnerabilities = @(
        $scan.projects.frameworks.topLevelPackages.vulnerabilities
        $scan.projects.frameworks.transitivePackages.vulnerabilities
    ) | Where-Object { $null -ne $_ }

    if ($vulnerabilities.Count -gt 0) {
        throw "NuGet vulnerability scan found vulnerable packages in $relativePath."
    }
}

Write-Output "Locked restore and transitive vulnerability scans passed for $($projects.Count) projects."
