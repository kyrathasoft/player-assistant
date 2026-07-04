param(
    [string]$ReleaseDir = (Join-Path $PSScriptRoot 'Release'),
    [string]$PublishDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [string]$RcTag = 'v0.9.0-hardening.5-rc1',
    [string[]]$ExpectedChangedPath = @(),
    [string[]]$TestFilter = @(
        'application version',
        'startup dependency matrix',
        'startup health records',
        'runtime housekeeping',
        'publish verification'
    ),
    [string]$DryRunJson,
    [switch]$SkipSecretScan,
    [switch]$SkipGit,
    [switch]$SkipSelfTests,
    [switch]$SkipTests,
    [switch]$SkipReleasePublishParity,
    [switch]$SkipPublishedHealth,
    [switch]$SkipPublishRuntimeIntegrity,
    [switch]$SkipDiagnostics
)

$ErrorActionPreference = 'Stop'

$ProjectFileName = 'player-assistant.csproj'
$ExecutableFileName = 'player-assistant.exe'
$TestExecutablePath = Join-Path $PSScriptRoot 'PlayerAssistant.Tests\bin\Release\net10.0-windows\PlayerAssistant.Tests.exe'
$TestStartupLogPath = Join-Path $PSScriptRoot 'PlayerAssistant.Tests\bin\Release\net10.0-windows\startup-errors.log'
$SecretScanScriptPath = Join-Path $PSScriptRoot 'verify-secret-scan.ps1'
$RcSelfTestsScriptPath = Join-Path $PSScriptRoot 'verify-rc-self-tests.ps1'
$ReleasePublishParityScriptPath = Join-Path $PSScriptRoot 'verify-release-publish-parity.ps1'
$PublishedHealthScriptPath = Join-Path $PSScriptRoot 'verify-published-health.ps1'
$PublishRuntimeIntegrityScriptPath = Join-Path $PSScriptRoot 'verify-publish-runtime-integrity.ps1'
$DiagnosticsScriptPath = Join-Path $PSScriptRoot 'collect-diagnostics.ps1'
$RcDryRunSteps = [System.Collections.Generic.List[object]]::new()
$RcDryRunFailed = $false

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

function Get-ProjectVersionInfo {
    $projectPath = Join-Path $PSScriptRoot $ProjectFileName
    Assert-RequiredFile -Path $projectPath -Description $ProjectFileName

    [xml]$project = Get-Content -Raw -LiteralPath $projectPath
    $propertyGroup = @($project.Project.PropertyGroup |
        Where-Object { $_.Version -or $_.FileVersion -or $_.InformationalVersion } |
        Select-Object -First 1)

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

function Format-Command {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,

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

    return "$FileName $($escapedArguments -join ' ')".Trim()
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

function Invoke-ExternalCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [string]$WorkingDirectory = $PSScriptRoot,

        [switch]$AllowFailure
    )

    $displayCommand = Format-Command -FileName $FileName -Arguments $Arguments
    Write-Host "Running: $displayCommand"

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.Arguments = ConvertTo-ProcessArguments -Arguments $Arguments
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Unable to start command: $displayCommand"
    }

    $standardOutput = $process.StandardOutput.ReadToEnd()
    $standardError = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if (![string]::IsNullOrWhiteSpace($standardOutput)) {
        $standardOutput.TrimEnd() -split "`r?`n" | ForEach-Object { Write-Host $_ }
    }

    if (![string]::IsNullOrWhiteSpace($standardError)) {
        $standardError.TrimEnd() -split "`r?`n" | ForEach-Object { Write-Host $_ }
    }

    if ($process.ExitCode -ne 0 -and !$AllowFailure) {
        throw "Command failed with exit code $($process.ExitCode): $displayCommand"
    }

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = (($standardOutput, $standardError) -join [Environment]::NewLine).TrimEnd()
    }
}

function Get-GitCommand {
    $rtk = Get-Command rtk -ErrorAction SilentlyContinue
    if ($rtk) {
        return [pscustomobject]@{
            FileName = $rtk.Source
            Prefix = @('git')
        }
    }

    return [pscustomobject]@{
        FileName = 'git'
        Prefix = @()
    }
}

function Invoke-GitCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [switch]$AllowFailure
    )

    $gitCommand = Get-GitCommand
    return Invoke-ExternalCommand `
        -FileName $gitCommand.FileName `
        -Arguments ([string[]]($gitCommand.Prefix + $Arguments)) `
        -AllowFailure:$AllowFailure
}

function Get-StatusPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StatusLine
    )

    if ($StatusLine.Length -lt 4) {
        return $StatusLine
    }

    $path = $StatusLine.Substring(3).Trim()
    if ($path.Contains(' -> ')) {
        $path = ($path -split ' -> ')[-1]
    }

    return $path.Replace('/', '\')
}

function Assert-ExpectedChangedPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$StatusLines,

        [string[]]$ExpectedPaths
    )

    if ($ExpectedPaths.Count -eq 0) {
        return
    }

    $expected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $ExpectedPaths) {
        [void]$expected.Add($path.Replace('/', '\').Trim())
    }

    $actual = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($line in $StatusLines) {
        [void]$actual.Add((Get-StatusPath -StatusLine $line))
    }

    $unexpected = @($actual | Where-Object { !$expected.Contains($_) } | Sort-Object)
    $missing = @($expected | Where-Object { !$actual.Contains($_) } | Sort-Object)

    if ($unexpected.Count -gt 0 -or $missing.Count -gt 0) {
        if ($unexpected.Count -gt 0) {
            Write-Output "Unexpected changed paths:"
            $unexpected | ForEach-Object { Write-Output "  $_" }
        }

        if ($missing.Count -gt 0) {
            Write-Output "Expected paths not currently changed:"
            $missing | ForEach-Object { Write-Output "  $_" }
        }

        throw "Git status does not match ExpectedChangedPath."
    }
}

function Test-GitReady {
    if ($SkipGit) {
        Write-Output "Skipping git diff hygiene because -SkipGit was supplied."
        return
    }

    Write-Output "Checking working tree diff hygiene..."
    [void](Invoke-GitCommand -Arguments @('diff', '--check'))

    $status = Invoke-GitCommand -Arguments @('status', '--short')
    $statusLines = @()
    if (![string]::IsNullOrWhiteSpace($status.Output)) {
        $statusLines = @($status.Output -split "`r?`n" | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
    }

    if ($statusLines.Count -eq 0) {
        Write-Output "Working tree status: clean."
    }
    else {
        Write-Output "Working tree status: review intended changes before committing."
        $statusLines | ForEach-Object { Write-Output "  $_" }
    }

    Assert-ExpectedChangedPaths -StatusLines $statusLines -ExpectedPaths $ExpectedChangedPath
}

function Assert-ExecutableVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$ExpectedVersion,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    Assert-RequiredFile -Path $Path -Description $Description

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if ($versionInfo.FileVersion -ne $ExpectedVersion.FileVersion) {
        throw "$Description FileVersion '$($versionInfo.FileVersion)' does not match project FileVersion '$($ExpectedVersion.FileVersion)'."
    }

    if ($versionInfo.ProductVersion -ne $ExpectedVersion.InformationalVersion) {
        throw "$Description ProductVersion '$($versionInfo.ProductVersion)' does not match project InformationalVersion '$($ExpectedVersion.InformationalVersion)'."
    }

    Write-Output "$Description version verified: FileVersion=$($versionInfo.FileVersion), ProductVersion=$($versionInfo.ProductVersion)"
}

function Assert-RcTagMatchesVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $expectedPrefix = "v$Version-rc"
    if (!$Tag.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "RC tag '$Tag' must start with '$expectedPrefix'."
    }
}

function Invoke-FocusedHardeningTests {
    if ($SkipTests) {
        Write-Output "Skipping focused hardening tests because -SkipTests was supplied."
        return
    }

    $hadStartupLog = Test-Path -LiteralPath $TestStartupLogPath -PathType Leaf
    $startupLogBackup = $null
    if ($hadStartupLog) {
        $startupLogBackup = [System.IO.File]::ReadAllBytes($TestStartupLogPath)
    }

    try {
        foreach ($filter in $TestFilter) {
            if (Test-Path -LiteralPath $TestExecutablePath -PathType Leaf) {
                [void](Invoke-ExternalCommand `
                    -FileName $TestExecutablePath `
                    -Arguments @($filter))
                continue
            }

            Write-Output "Release test executable is missing; falling back to dotnet run."
            [void](Invoke-ExternalCommand `
                -FileName 'dotnet' `
                -Arguments @('run', '--project', 'PlayerAssistant.Tests', '--configuration', 'Release', '--', $filter))
        }
    }
    finally {
        if ($hadStartupLog) {
            [System.IO.File]::WriteAllBytes($TestStartupLogPath, $startupLogBackup)
        }
        elseif (Test-Path -LiteralPath $TestStartupLogPath -PathType Leaf) {
            Remove-Item -LiteralPath $TestStartupLogPath -Force
        }
    }
}

function Add-RcDryRunStep {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Step
    )

    $RcDryRunSteps.Add($Step)
}

function Invoke-RcChecklistStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Command,

        [string[]]$Artifacts = @(),

        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    $startedAt = Get-Date
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $status = 'passed'
    $failureSummary = $null

    try {
        & $Action
    }
    catch {
        $status = 'failed'
        $failureSummary = $_.Exception.Message
        if ([string]::IsNullOrWhiteSpace($DryRunJson)) {
            throw
        }

        $script:RcDryRunFailed = $true
    }
    finally {
        $stopwatch.Stop()
        Add-RcDryRunStep ([ordered]@{
            name = $Name
            status = $status
            elapsed_ms = [int64]$stopwatch.Elapsed.TotalMilliseconds
            started_at = $startedAt.ToString('O')
            command = $Command
            artifacts = @($Artifacts)
            failure_summary = $failureSummary
        })
    }
}

function Write-RcDryRunJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$ProjectVersion,

        [Parameter(Mandatory = $true)]
        [string]$ResolvedReleaseDir,

        [Parameter(Mandatory = $true)]
        [string]$ResolvedPublishDir
    )

    $summaryPath = Resolve-FullPath $Path
    Assert-PathInsideRepo -Path $summaryPath -Description 'RC dry-run JSON output'
    $summaryDirectory = Split-Path -Parent $summaryPath
    if (![string]::IsNullOrWhiteSpace($summaryDirectory)) {
        New-Item -ItemType Directory -Force -Path $summaryDirectory | Out-Null
    }

    $failedCount = @($RcDryRunSteps | Where-Object { $_.status -eq 'failed' }).Count
    $summary = [ordered]@{
        schema_version = 1
        generated_at = (Get-Date).ToString('O')
        rc_tag = $RcTag
        version = $ProjectVersion.Version
        release_dir = $ResolvedReleaseDir
        publish_dir = $ResolvedPublishDir
        status = if ($failedCount -eq 0) { 'passed' } else { 'failed' }
        failed_step_count = $failedCount
        step_count = $RcDryRunSteps.Count
        steps = @($RcDryRunSteps)
    }

    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Write-Output "RC dry-run JSON written: $summaryPath"
}

function Invoke-SecretScan {
    if ($SkipSecretScan) {
        Write-Output "Skipping secret scan because -SkipSecretScan was supplied."
        return
    }

    Assert-RequiredFile -Path $SecretScanScriptPath -Description 'secret scan script'
    [void](Invoke-ExternalCommand `
        -FileName 'powershell.exe' `
        -Arguments @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $SecretScanScriptPath,
            '-RepoRoot',
            $PSScriptRoot,
            '-IncludeHistory'
        ))
}

function Invoke-RcSelfTests {
    if ($SkipSelfTests) {
        Write-Output "Skipping RC checklist self-tests because -SkipSelfTests was supplied."
        return
    }

    Assert-RequiredFile -Path $RcSelfTestsScriptPath -Description 'RC checklist self-test script'
    [void](Invoke-ExternalCommand `
        -FileName 'powershell.exe' `
        -Arguments @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $RcSelfTestsScriptPath,
            '-ReleaseDir',
            $resolvedReleaseDir,
            '-PublishDir',
            $resolvedPublishDir
        ))
}

function Invoke-PublishRuntimeIntegrityCheck {
    if ($SkipPublishRuntimeIntegrity) {
        Write-Output "Skipping published-folder runtime integrity check because -SkipPublishRuntimeIntegrity was supplied."
        return
    }

    Assert-RequiredFile -Path $PublishRuntimeIntegrityScriptPath -Description 'published-folder runtime integrity script'
    [void](Invoke-ExternalCommand `
        -FileName 'powershell.exe' `
        -Arguments @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $PublishRuntimeIntegrityScriptPath,
            '-ReleaseDir',
            $resolvedReleaseDir,
            '-PublishDir',
            $resolvedPublishDir
        ))
}

function Invoke-ReleasePublishParityCheck {
    if ($SkipReleasePublishParity) {
        Write-Output "Skipping Release/publish parity check because -SkipReleasePublishParity was supplied."
        return
    }

    Assert-RequiredFile -Path $ReleasePublishParityScriptPath -Description 'Release/publish parity script'
    [void](Invoke-ExternalCommand `
        -FileName 'powershell.exe' `
        -Arguments @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $ReleasePublishParityScriptPath,
            '-ReleaseDir',
            $resolvedReleaseDir,
            '-PublishDir',
            $resolvedPublishDir
        ))
}

function Invoke-PublishedHealthCheck {
    if ($SkipPublishedHealth) {
        Write-Output "Skipping published health check because -SkipPublishedHealth was supplied."
        return
    }

    Assert-RequiredFile -Path $PublishedHealthScriptPath -Description 'published health verification script'
    [void](Invoke-ExternalCommand `
        -FileName 'powershell.exe' `
        -Arguments @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $PublishedHealthScriptPath,
            '-PublishDir',
            $resolvedPublishDir
        ))
}

function Invoke-DiagnosticsBundleCheck {
    if ($SkipDiagnostics) {
        Write-Output "Skipping diagnostic bundle check because -SkipDiagnostics was supplied."
        return
    }

    Assert-RequiredFile -Path $DiagnosticsScriptPath -Description 'diagnostic bundle script'
    $diagnosticsOutputDir = Join-Path $PSScriptRoot 'codex-scratch\rc-diagnostics'
    if (Test-Path -LiteralPath $diagnosticsOutputDir) {
        Remove-Item -LiteralPath $diagnosticsOutputDir -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $diagnosticsOutputDir | Out-Null
    try {
        $result = Invoke-ExternalCommand `
            -FileName 'powershell.exe' `
            -Arguments @(
                '-NoProfile',
                '-ExecutionPolicy',
                'Bypass',
                '-File',
                $DiagnosticsScriptPath,
                '-ReleaseDir',
                $resolvedReleaseDir,
                '-PublishDir',
                $resolvedPublishDir,
                '-OutputDir',
                $diagnosticsOutputDir,
                '-NoPublishVerification'
            )

        $zipLine = @($result.Output -split "`r?`n" |
            Where-Object { $_ -like 'Diagnostic bundle created:*' } |
            Select-Object -Last 1)
        if ($zipLine.Count -eq 0) {
            throw "Diagnostic bundle script did not report a created zip."
        }

        $zipPath = $zipLine[0].Substring('Diagnostic bundle created:'.Length).Trim()
        Assert-RequiredFile -Path $zipPath -Description 'diagnostic bundle zip'

        [void](Invoke-ExternalCommand `
            -FileName 'powershell.exe' `
            -Arguments @(
                '-NoProfile',
                '-ExecutionPolicy',
                'Bypass',
                '-File',
                $DiagnosticsScriptPath,
                '-OutputDir',
                $diagnosticsOutputDir,
                '-VerifyOnly',
                $zipPath
            ))
    }
    finally {
        if (Test-Path -LiteralPath $diagnosticsOutputDir) {
            Remove-Item -LiteralPath $diagnosticsOutputDir -Recurse -Force
        }
    }
}

function Write-ReleaseCommands {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $branch = (Invoke-GitCommand -Arguments @('branch', '--show-current')).Output.Trim()
    if ([string]::IsNullOrWhiteSpace($branch)) {
        $branch = '<current-branch>'
    }

    Write-Output ''
    Write-Output 'RC commit/tag commands to run after reviewing the checklist output:'
    Write-Output '  git add -- <reviewed files>'
    Write-Output "  git commit -m ""Release player-assistant $Version RC1"""
    Write-Output "  git tag -a $Tag -m ""player-assistant $Version RC1"""
    Write-Output "  git push origin $branch"
    Write-Output "  git push origin $Tag"
}

$resolvedReleaseDir = Resolve-FullPath $ReleaseDir
$resolvedPublishDir = Resolve-FullPath $PublishDir
$projectVersion = $null

Invoke-RcChecklistStep `
    -Name 'validate paths and version' `
    -Command 'validate ReleaseDir, PublishDir, project version, and RC tag' `
    -Artifacts @($resolvedReleaseDir, $resolvedPublishDir, (Join-Path $PSScriptRoot $ProjectFileName)) `
    -Action {
        Assert-PathInsideRepo -Path $resolvedReleaseDir -Description 'Release directory'
        Assert-PathInsideRepo -Path $resolvedPublishDir -Description 'publish directory'
        $script:projectVersion = Get-ProjectVersionInfo
        Assert-RcTagMatchesVersion -Tag $RcTag -Version $script:projectVersion.Version
    }

if ($null -eq $projectVersion) {
    $projectVersion = [pscustomobject]@{
        Version = '<unknown>'
        FileVersion = '<unknown>'
        InformationalVersion = '<unknown>'
    }
}

Write-Output "RC checklist for $($projectVersion.Version) using tag $RcTag"

Invoke-RcChecklistStep `
    -Name 'git diff hygiene' `
    -Command 'git diff --check; git status --short' `
    -Artifacts @($PSScriptRoot) `
    -Action { Test-GitReady }

Invoke-RcChecklistStep `
    -Name 'secret scan' `
    -Command "powershell.exe -NoProfile -ExecutionPolicy Bypass -File $SecretScanScriptPath -RepoRoot $PSScriptRoot -IncludeHistory" `
    -Artifacts @($SecretScanScriptPath) `
    -Action { Invoke-SecretScan }

Invoke-RcChecklistStep `
    -Name 'RC checklist self-tests' `
    -Command "powershell.exe -NoProfile -ExecutionPolicy Bypass -File $RcSelfTestsScriptPath -ReleaseDir $resolvedReleaseDir -PublishDir $resolvedPublishDir" `
    -Artifacts @($RcSelfTestsScriptPath) `
    -Action { Invoke-RcSelfTests }

Invoke-RcChecklistStep `
    -Name 'focused hardening tests' `
    -Command "PlayerAssistant.Tests $($TestFilter -join ', ')" `
    -Artifacts @($TestExecutablePath) `
    -Action { Invoke-FocusedHardeningTests }

Invoke-RcChecklistStep `
    -Name 'Release/publish parity' `
    -Command "powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ReleasePublishParityScriptPath -ReleaseDir $resolvedReleaseDir -PublishDir $resolvedPublishDir" `
    -Artifacts @($ReleasePublishParityScriptPath, $resolvedReleaseDir, $resolvedPublishDir) `
    -Action { Invoke-ReleasePublishParityCheck }

Invoke-RcChecklistStep `
    -Name 'published health' `
    -Command "powershell.exe -NoProfile -ExecutionPolicy Bypass -File $PublishedHealthScriptPath -PublishDir $resolvedPublishDir" `
    -Artifacts @($PublishedHealthScriptPath, $resolvedPublishDir) `
    -Action { Invoke-PublishedHealthCheck }

Invoke-RcChecklistStep `
    -Name 'publish runtime integrity' `
    -Command "powershell.exe -NoProfile -ExecutionPolicy Bypass -File $PublishRuntimeIntegrityScriptPath -ReleaseDir $resolvedReleaseDir -PublishDir $resolvedPublishDir" `
    -Artifacts @($PublishRuntimeIntegrityScriptPath, $resolvedReleaseDir, $resolvedPublishDir) `
    -Action { Invoke-PublishRuntimeIntegrityCheck }

Invoke-RcChecklistStep `
    -Name 'diagnostic bundle' `
    -Command "powershell.exe -NoProfile -ExecutionPolicy Bypass -File $DiagnosticsScriptPath -ReleaseDir $resolvedReleaseDir -PublishDir $resolvedPublishDir" `
    -Artifacts @($DiagnosticsScriptPath, (Join-Path $PSScriptRoot 'codex-scratch\rc-diagnostics')) `
    -Action { Invoke-DiagnosticsBundleCheck }

Invoke-RcChecklistStep `
    -Name 'Release executable version' `
    -Command "check version metadata for $(Join-Path $resolvedReleaseDir $ExecutableFileName)" `
    -Artifacts @((Join-Path $resolvedReleaseDir $ExecutableFileName)) `
    -Action {
        Assert-ExecutableVersion `
            -Path (Join-Path $resolvedReleaseDir $ExecutableFileName) `
            -ExpectedVersion $projectVersion `
            -Description 'Release executable'
    }

Invoke-RcChecklistStep `
    -Name 'published executable version' `
    -Command "check version metadata for $(Join-Path $resolvedPublishDir $ExecutableFileName)" `
    -Artifacts @((Join-Path $resolvedPublishDir $ExecutableFileName)) `
    -Action {
        Assert-ExecutableVersion `
            -Path (Join-Path $resolvedPublishDir $ExecutableFileName) `
            -ExpectedVersion $projectVersion `
            -Description 'published executable'
    }

Invoke-RcChecklistStep `
    -Name 'release command plan' `
    -Command 'print RC commit, tag, and push commands' `
    -Artifacts @() `
    -Action { Write-ReleaseCommands -Tag $RcTag -Version $projectVersion.Version }

if (![string]::IsNullOrWhiteSpace($DryRunJson)) {
    Write-RcDryRunJson `
        -Path $DryRunJson `
        -ProjectVersion $projectVersion `
        -ResolvedReleaseDir $resolvedReleaseDir `
        -ResolvedPublishDir $resolvedPublishDir

    if ($RcDryRunFailed) {
        Write-Output ''
        Write-Output 'RC checklist dry-run failed.'
        exit 1
    }
}

Write-Output ''
Write-Output 'RC checklist passed.'
