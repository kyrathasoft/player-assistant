param(
    [string]$ReleaseDir = (Join-Path $PSScriptRoot 'Release'),
    [string]$PublishDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [string]$RcTag = 'v0.9.1-hardening.1-rc1',
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
    [switch]$SkipGitDiffCheck,
    [switch]$SkipSelfTests,
    [switch]$SkipTests,
    [switch]$SkipReleasePublishParity,
    [switch]$SkipPublishedHealth,
    [switch]$SkipPublishRuntimeIntegrity,
    [switch]$SkipDiagnostics,
    [switch]$SkipDependencyChecks,
    [switch]$SkipRuntimeSidecarChecks,
    [switch]$SkipCodeSigning,
    [string]$ExpectedSignerSubject = $env:PLAYER_ASSISTANT_RELEASE_SIGNER_SUBJECT,
    [string]$ExpectedSignerThumbprint = $env:PLAYER_ASSISTANT_RELEASE_SIGNER_THUMBPRINT,
    [string]$InstallerPath,
    [string]$DependencyVulnerabilityOutputFixture,
    [string]$DependencyFreshnessMetadataFixture,
    [int]$DependencyFreshnessMaxAgeDays = 365,
    [switch]$WarnOnlyDependencyFreshness
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
$RuntimeSidecarScriptPath = Join-Path $PSScriptRoot 'verify-runtime-sidecars.ps1'
$DependencyInventoryPath = Join-Path $PSScriptRoot 'codex-scratch\rc-dependency-inventory.json'
$ReleaseProvenanceFileName = 'release-provenance.json'
$RcDryRunSteps = [System.Collections.Generic.List[object]]::new()
$RcDryRunFailed = $false

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [System.IO.Path]::GetFullPath($Path)
}

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

    $resolvedFileName = if ($FileName -ieq 'powershell.exe') { Get-PowerShellExecutable } else { $FileName }
    $displayCommand = Format-Command -FileName $resolvedFileName -Arguments $Arguments
    Write-Host "Running: $displayCommand"

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $resolvedFileName
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

    if ($SkipGitDiffCheck) {
        Write-Output "Skipping git diff --check because -SkipGitDiffCheck was supplied."
    }
    else {
        Write-Output "Checking working tree diff hygiene..."
        [void](Invoke-ExternalCommand -FileName 'git' -Arguments @('diff', '--check'))
    }

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

function Get-InstallerVersion {
    param([Parameter(Mandatory = $true)][string]$Version)

    if ($Version -match '^(\d+\.\d+\.\d+)') {
        return $Matches[1]
    }

    throw "Version '$Version' does not start with a numeric major.minor.patch segment for installer naming."
}

function Get-AuthenticodeSignatureSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    return [pscustomobject]@{
        status = [string]$signature.Status
        signer_subject = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
        thumbprint = if ($signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { $null }
    }
}

function Assert-CodeSigningPolicyConfigured {
    if ([string]::IsNullOrWhiteSpace($ExpectedSignerSubject) -and
        [string]::IsNullOrWhiteSpace($ExpectedSignerThumbprint)) {
        throw 'Code-signing enforcement requires -ExpectedSignerSubject or -ExpectedSignerThumbprint, or PLAYER_ASSISTANT_RELEASE_SIGNER_SUBJECT / PLAYER_ASSISTANT_RELEASE_SIGNER_THUMBPRINT.'
    }
}

function Assert-AuthenticodeSignatureMatchesPolicy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    Assert-RequiredFile -Path $Path -Description $Description
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
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

function Assert-ProvenanceSignatureMatches {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [object]$ActualSignature,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $provenancePath = Join-Path $Directory $ReleaseProvenanceFileName
    $provenance = ConvertFrom-JsonFileIfPresent -Path $provenancePath -Description "$Description $ReleaseProvenanceFileName"
    if ($null -eq $provenance.PSObject.Properties['executable_signature']) {
        throw "$Description $ReleaseProvenanceFileName is missing executable_signature."
    }

    if ([string]$provenance.executable_signature.status -ne [string]$ActualSignature.status) {
        throw "$Description $ReleaseProvenanceFileName executable_signature.status does not match the executable."
    }

    if ([string]$provenance.executable_signature.thumbprint -ne [string]$ActualSignature.thumbprint) {
        throw "$Description $ReleaseProvenanceFileName executable_signature.thumbprint does not match the executable."
    }
}

function Invoke-CodeSigningCheck {
    if ($SkipCodeSigning) {
        Write-Output "Skipping code-signing checks because -SkipCodeSigning was supplied."
        return
    }

    Assert-CodeSigningPolicyConfigured

    $releaseExecutablePath = Join-Path $resolvedReleaseDir $ExecutableFileName
    $publishExecutablePath = Join-Path $resolvedPublishDir $ExecutableFileName
    $releaseSignature = Assert-AuthenticodeSignatureMatchesPolicy -Path $releaseExecutablePath -Description 'Release executable'
    $publishSignature = Assert-AuthenticodeSignatureMatchesPolicy -Path $publishExecutablePath -Description 'published executable'
    Assert-ProvenanceSignatureMatches -Directory $resolvedReleaseDir -ActualSignature $releaseSignature -Description 'Release'
    Assert-ProvenanceSignatureMatches -Directory $resolvedPublishDir -ActualSignature $publishSignature -Description 'publish'

    $resolvedInstallerPath = if (![string]::IsNullOrWhiteSpace($InstallerPath)) {
        Resolve-FullPath $InstallerPath
    }
    else {
        Join-Path $PSScriptRoot "Release\installer\p-assist-$(Get-InstallerVersion -Version $projectVersion.Version).exe"
    }

    Assert-AuthenticodeSignatureMatchesPolicy -Path $resolvedInstallerPath -Description 'Inno Setup installer'
    Write-Output "Code-signing verified: subject='$ExpectedSignerSubject', thumbprint='$ExpectedSignerThumbprint'"
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

function ConvertFrom-JsonFileIfPresent {
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
        throw "Unable to parse $Description as JSON: $Path. $($_.Exception.Message)"
    }
}

function Write-DependencyTrace {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ([string]::IsNullOrWhiteSpace($env:PLAYER_ASSISTANT_RC_TRACE)) {
        return
    }

    $tracePath = Join-Path $PSScriptRoot 'codex-scratch\rc-dependency-trace.txt'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $tracePath) | Out-Null
    Add-Content -LiteralPath $tracePath -Value "$(Get-Date -Format O) $Message" -Encoding UTF8
}

function ConvertTo-SimpleJsonString {
    param(
        [AllowNull()]
        [object]$Value
    )

    if ($null -eq $Value) {
        return 'null'
    }

    if ($Value -is [string]) {
        $builder = [System.Text.StringBuilder]::new()
        [void]$builder.Append('"')
        foreach ($character in $Value.ToCharArray()) {
            if ($character -eq '"') {
                [void]$builder.Append('\"')
            }
            elseif ($character -eq '\') {
                [void]$builder.Append('\\')
            }
            elseif ($character -eq "`b") {
                [void]$builder.Append('\b')
            }
            elseif ($character -eq "`f") {
                [void]$builder.Append('\f')
            }
            elseif ($character -eq "`n") {
                [void]$builder.Append('\n')
            }
            elseif ($character -eq "`r") {
                [void]$builder.Append('\r')
            }
            elseif ($character -eq "`t") {
                [void]$builder.Append('\t')
            }
            elseif ([int][char]$character -lt 32) {
                [void]$builder.Append('\u')
                [void]$builder.Append(([int][char]$character).ToString('x4'))
            }
            else {
                [void]$builder.Append($character)
            }
        }

        [void]$builder.Append('"')
        return $builder.ToString()
    }

    if ($Value -is [bool]) {
        return $(if ($Value) { 'true' } else { 'false' })
    }

    if ($Value -is [int] -or
        $Value -is [long] -or
        $Value -is [double] -or
        $Value -is [decimal]) {
        return [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, '{0}', $Value)
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $properties = @()
        foreach ($key in $Value.Keys) {
            $properties += "$(ConvertTo-SimpleJsonString ([string]$key)):$(ConvertTo-SimpleJsonString $Value[$key])"
        }

        return '{' + ($properties -join ',') + '}'
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        $items = @()
        foreach ($item in $Value) {
            $items += ConvertTo-SimpleJsonString $item
        }

        return '[' + ($items -join ',') + ']'
    }

    $objectProperties = @()
    foreach ($property in $Value.PSObject.Properties) {
        $objectProperties += "$(ConvertTo-SimpleJsonString $property.Name):$(ConvertTo-SimpleJsonString $property.Value)"
    }

    return '{' + ($objectProperties -join ',') + '}'
}

function Get-NuGetPackageInventory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    $directOutput = (Invoke-ExternalCommand `
        -FileName 'dotnet' `
        -Arguments @('list', $ProjectPath, 'package')).Output

    $transitiveOutput = (Invoke-ExternalCommand `
        -FileName 'dotnet' `
        -Arguments @('list', $ProjectPath, 'package', '--include-transitive')).Output

    return [pscustomobject]@{
        direct = $directOutput
        transitive = $transitiveOutput
    }
}

function Get-ProjectPackageReferences {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    [xml]$project = Get-Content -Raw -LiteralPath $ProjectPath
    return @($project.Project.ItemGroup |
        ForEach-Object { $_.PackageReference } |
        Where-Object { $_ -and $_.Include } |
        ForEach-Object {
            [pscustomobject]@{
                name = [string]$_.Include
                version = [string]$_.Version
                source = 'project'
            }
        })
}

function ConvertTo-ComparableVersion {
    param([string]$Version)

    if ([string]::IsNullOrWhiteSpace($Version)) {
        return $null
    }

    $versionText = ($Version -replace '\+.*$', '' -replace '-.*$', '')
    try {
        return [version]$versionText
    }
    catch {
        return $null
    }
}

function Get-NuGetPackageMetadataFromFeed {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageName
    )

    $packageId = $PackageName.ToLowerInvariant()
    $registrationUri = "https://api.nuget.org/v3/registration5-semver1/$packageId/index.json"
    $registration = Invoke-RestMethod -Uri $registrationUri -Method Get -TimeoutSec 30
    $entries = @()

    foreach ($page in @($registration.items)) {
        $pageItems = @($page.items)
        if ($pageItems.Count -eq 0 -and $page.'@id') {
            $pageItems = @((Invoke-RestMethod -Uri $page.'@id' -Method Get -TimeoutSec 30).items)
        }

        foreach ($item in $pageItems) {
            $catalogEntry = $item.catalogEntry
            if ($null -eq $catalogEntry) {
                continue
            }

            $versionText = [string]$catalogEntry.version
            $comparableVersion = ConvertTo-ComparableVersion -Version $versionText
            if ($null -eq $comparableVersion) {
                continue
            }

            $isPrerelease = $versionText.Contains('-')
            $listed = if ($null -ne $catalogEntry.PSObject.Properties['listed']) { [bool]$catalogEntry.listed } else { $true }
            $published = $null
            if (![string]::IsNullOrWhiteSpace([string]$catalogEntry.published)) {
                $published = ([datetimeoffset]::Parse([string]$catalogEntry.published)).ToString('O')
            }

            $entries += [pscustomobject]@{
                version = $versionText
                comparable_version = $comparableVersion
                published = $published
                listed = $listed
                prerelease = $isPrerelease
            }
        }
    }

    $latestStable = @($entries |
        Where-Object { $_.listed -and !$_.prerelease } |
        Sort-Object -Property comparable_version -Descending |
        Select-Object -First 1)

    return [pscustomobject]@{
        source = $registrationUri
        versions = $entries
        latest_stable = if ($latestStable.Count -gt 0) { $latestStable[0] } else { $null }
    }
}

function Get-DependencyFreshnessMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$PackageNames
    )

    $metadataByName = @{}
    if (![string]::IsNullOrWhiteSpace($DependencyFreshnessMetadataFixture)) {
        Assert-RequiredFile -Path $DependencyFreshnessMetadataFixture -Description 'dependency freshness metadata fixture'
        $fixture = Get-Content -Raw -LiteralPath $DependencyFreshnessMetadataFixture | ConvertFrom-Json
        foreach ($package in @($fixture.packages)) {
            $metadataByName[[string]$package.name] = [pscustomobject]@{
                source = 'fixture'
                versions = @()
                latest_stable = [pscustomobject]@{
                    version = [string]$package.latest_version
                    comparable_version = ConvertTo-ComparableVersion -Version ([string]$package.latest_version)
                    published = [string]$package.latest_published
                    listed = $true
                    prerelease = $false
                }
                current_published = [string]$package.current_published
            }
        }

        return $metadataByName
    }

    foreach ($packageName in $PackageNames | Sort-Object -Unique) {
        $metadataByName[$packageName] = Get-NuGetPackageMetadataFromFeed -PackageName $packageName
    }

    return $metadataByName
}

function Get-PackageCurrentPublishedDate {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Metadata,

        [Parameter(Mandatory = $true)]
        [string]$CurrentVersion
    )

    if ($Metadata.PSObject.Properties['current_published'] -and
        ![string]::IsNullOrWhiteSpace([string]$Metadata.current_published)) {
        return [string]$Metadata.current_published
    }

    $currentComparableVersion = ConvertTo-ComparableVersion -Version $CurrentVersion
    $currentEntry = @($Metadata.versions |
        Where-Object {
            [string]$_.version -eq $CurrentVersion -or
            ($null -ne $currentComparableVersion -and $null -ne $_.comparable_version -and $_.comparable_version -eq $currentComparableVersion)
        } |
        Select-Object -First 1)

    if ($currentEntry.Count -eq 0) {
        return $null
    }

    return [string]$currentEntry[0].published
}

function Get-DependencyFreshnessFindings {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$PackageReferences,

        [object]$PlaywrightInventory
    )

    if ($DependencyFreshnessMaxAgeDays -le 0) {
        throw "DependencyFreshnessMaxAgeDays must be greater than zero."
    }

    $freshnessInputs = @($PackageReferences)
    if ($null -ne $PlaywrightInventory -and
        ![string]::IsNullOrWhiteSpace([string]$PlaywrightInventory.playwright_package_version)) {
        $freshnessInputs += [pscustomobject]@{
            name = 'Microsoft.Playwright'
            version = [string]$PlaywrightInventory.playwright_package_version
            source = 'published Playwright runtime'
        }
    }

    $metadataByName = Get-DependencyFreshnessMetadata -PackageNames @($freshnessInputs | ForEach-Object { [string]$_.name })
    $now = [datetimeoffset]::UtcNow
    $findings = @()

    foreach ($package in $freshnessInputs) {
        $name = [string]$package.name
        $version = [string]$package.version
        $metadata = $metadataByName[$name]
        if ($null -eq $metadata) {
            $findings += [pscustomobject]@{
                name = $name
                current_version = $version
                source = [string]$package.source
                status = 'failed'
                failure_summary = 'No package metadata was available for freshness comparison.'
            }
            continue
        }

        $latest = $metadata.latest_stable
        $currentPublished = Get-PackageCurrentPublishedDate -Metadata $metadata -CurrentVersion $version
        $currentComparableVersion = ConvertTo-ComparableVersion -Version $version
        $latestComparableVersion = if ($null -ne $latest) { ConvertTo-ComparableVersion -Version ([string]$latest.version) } else { $null }
        $ageDays = $null
        if (![string]::IsNullOrWhiteSpace($currentPublished)) {
            $ageDays = [math]::Floor(($now - [datetimeoffset]::Parse($currentPublished)).TotalDays)
        }

        $updateAvailable = $false
        if ($null -ne $currentComparableVersion -and $null -ne $latestComparableVersion) {
            $updateAvailable = $latestComparableVersion -gt $currentComparableVersion
        }

        $status = 'passed'
        $failureSummary = $null
        if ($null -eq $latest -or $null -eq $latestComparableVersion) {
            $status = 'failed'
            $failureSummary = 'No latest stable package version was available for freshness comparison.'
        }
        elseif ([string]::IsNullOrWhiteSpace($currentPublished) -or $null -eq $ageDays) {
            $status = 'failed'
            $failureSummary = "No published date was available for $name $version."
        }
        elseif ($updateAvailable -and $ageDays -gt $DependencyFreshnessMaxAgeDays) {
            $status = 'failed'
            $failureSummary = "$name $version is $ageDays days old and latest stable is $([string]$latest.version); approved maximum age is $DependencyFreshnessMaxAgeDays days."
        }

        $findings += [pscustomobject]@{
            name = $name
            current_version = $version
            current_published = $currentPublished
            latest_stable_version = if ($null -ne $latest) { [string]$latest.version } else { $null }
            latest_stable_published = if ($null -ne $latest) { [string]$latest.published } else { $null }
            update_available = $updateAvailable
            age_days = $ageDays
            max_age_days = $DependencyFreshnessMaxAgeDays
            source = [string]$package.source
            metadata_source = [string]$metadata.source
            status = $status
            failure_summary = $failureSummary
        }
    }

    return $findings
}

function Assert-DependencyFreshness {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Findings
    )

    $failedFindings = @($Findings | Where-Object { [string]$_.status -ne 'passed' })
    if ($failedFindings.Count -eq 0) {
        return
    }

    $summary = (($failedFindings | ForEach-Object { [string]$_.failure_summary }) -join ' ')
    if ($WarnOnlyDependencyFreshness) {
        Write-Warning "Dependency freshness policy warnings: $summary"
        return
    }

    throw "Dependency freshness policy failed. $summary"
}

function Assert-NoVulnerablePackages {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VulnerabilityOutput
    )

    $outputLines = @($VulnerabilityOutput -split "`r?`n")
    $reportedVulnerabilitySummary = $false
    $reportedVulnerabilityRow = $false
    $reportedCleanResult = $false
    foreach ($rawLine in $outputLines) {
        if ($rawLine.IndexOf('has the following vulnerable packages', [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $reportedVulnerabilitySummary = $true
        }

        if ($rawLine.IndexOf('has no vulnerable packages', [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $reportedCleanResult = $true
        }

        $line = $rawLine.Trim()
        if ($line.StartsWith('> ', [System.StringComparison]::Ordinal) -and
            ($line.IndexOf(' Low ', [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $line.IndexOf(' Moderate ', [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $line.IndexOf(' High ', [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $line.IndexOf(' Critical ', [System.StringComparison]::OrdinalIgnoreCase) -ge 0)) {
            $reportedVulnerabilityRow = $true
        }
    }

    if ($reportedVulnerabilitySummary -or $reportedVulnerabilityRow) {
        throw "Dependency vulnerability check reported vulnerable packages."
    }

    if (!$reportedCleanResult) {
        throw "Dependency vulnerability check did not confirm that packages have no known vulnerabilities."
    }
}

function Get-PlaywrightRuntimeInventory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory
    )

    $playwrightRoot = Join-Path $PublishDirectory '.playwright'
    $packageJsonPath = Join-Path $playwrightRoot 'package\package.json'
    $browsersJsonPath = Join-Path $playwrightRoot 'package\browsers.json'
    $nodeExePath = Join-Path $playwrightRoot 'node\win32_x64\node.exe'

    $packageJson = ConvertFrom-JsonFileIfPresent -Path $packageJsonPath -Description 'Playwright package metadata'
    $browsersJson = ConvertFrom-JsonFileIfPresent -Path $browsersJsonPath -Description 'Playwright browser metadata'
    Assert-RequiredFile -Path $nodeExePath -Description 'bundled Playwright Node runtime'

    $nodeVersion = (Invoke-ExternalCommand `
        -FileName $nodeExePath `
        -Arguments @('--version') `
        -WorkingDirectory $PublishDirectory).Output.Trim()

    $browsers = @()
    if ($browsersJson.browsers) {
        $browsers = @($browsersJson.browsers | ForEach-Object {
            [pscustomobject]@{
                name = [string]$_.name
                revision = [string]$_.revision
                browser_version = [string]$_.browserVersion
            }
        })
    }

    return [pscustomobject]@{
        playwright_package_path = $packageJsonPath
        playwright_package_version = [string]$packageJson.version
        browsers_path = $browsersJsonPath
        browsers = $browsers
        node_path = $nodeExePath
        node_version = $nodeVersion
    }
}

function Invoke-DependencyFreshnessAndVulnerabilityCheck {
    if ($SkipDependencyChecks) {
        Write-Output "Skipping dependency checks because -SkipDependencyChecks was supplied."
        return
    }

    $projectPath = Join-Path $PSScriptRoot $ProjectFileName
    Write-DependencyTrace -Message 'dependency check started'
    Assert-RequiredFile -Path $projectPath -Description $ProjectFileName
    Assert-PathInsideRepo -Path $DependencyInventoryPath -Description 'dependency inventory output'
    Write-DependencyTrace -Message 'dependency paths validated'

    $inventoryDirectory = Split-Path -Parent $DependencyInventoryPath
    if (![string]::IsNullOrWhiteSpace($inventoryDirectory)) {
        New-Item -ItemType Directory -Force -Path $inventoryDirectory | Out-Null
    }

    $packageReferences = Get-ProjectPackageReferences -ProjectPath $projectPath

    if (![string]::IsNullOrWhiteSpace($DependencyVulnerabilityOutputFixture)) {
        Write-DependencyTrace -Message 'fixture mode selected'
        Assert-RequiredFile -Path $DependencyVulnerabilityOutputFixture -Description 'dependency vulnerability output fixture'
        $dotnetSdkVersion = 'Skipped because -DependencyVulnerabilityOutputFixture was supplied.'
        $dotnetRuntimes = @()
        $packageInventory = [pscustomobject]@{
            direct = 'Skipped because -DependencyVulnerabilityOutputFixture was supplied.'
            transitive = 'Skipped because -DependencyVulnerabilityOutputFixture was supplied.'
        }
        $vulnerabilityOutput = Get-Content -Raw -LiteralPath $DependencyVulnerabilityOutputFixture
        Write-DependencyTrace -Message 'fixture vulnerability output loaded'
    }
    else {
        Write-DependencyTrace -Message 'live dependency inventory selected'
        $dotnetSdkVersion = (Invoke-ExternalCommand `
            -FileName 'dotnet' `
            -Arguments @('--version')).Output.Trim()

        $runtimeOutput = (Invoke-ExternalCommand `
            -FileName 'dotnet' `
            -Arguments @('--list-runtimes')).Output

        $dotnetRuntimes = @($runtimeOutput -split "`r?`n" |
            Where-Object { ![string]::IsNullOrWhiteSpace($_) })

        $packageInventory = Get-NuGetPackageInventory -ProjectPath $projectPath
        $vulnerabilityOutput = (Invoke-ExternalCommand `
            -FileName 'dotnet' `
            -Arguments @('list', $projectPath, 'package', '--vulnerable', '--include-transitive')).Output
    }

    $vulnerabilityStatus = 'passed'
    $vulnerabilityFailure = $null
    try {
        Write-DependencyTrace -Message 'vulnerability assertion started'
        Assert-NoVulnerablePackages -VulnerabilityOutput $vulnerabilityOutput
        Write-DependencyTrace -Message 'vulnerability assertion passed'
    }
    catch {
        $vulnerabilityStatus = 'failed'
        $vulnerabilityFailure = $_.Exception.Message
        Write-DependencyTrace -Message "vulnerability assertion failed: $vulnerabilityFailure"
    }

    Write-DependencyTrace -Message 'playwright inventory decision started'
    $playwrightInventory = if ($vulnerabilityStatus -eq 'passed' -and [string]::IsNullOrWhiteSpace($DependencyVulnerabilityOutputFixture)) {
        Get-PlaywrightRuntimeInventory -PublishDirectory $resolvedPublishDir
    }
    else {
        [pscustomobject]@{
            skipped = 'Playwright runtime inventory skipped because dependency vulnerability fixture mode was used or vulnerability verification failed.'
        }
    }
    Write-DependencyTrace -Message 'playwright inventory decision completed'

    $freshnessStatus = 'passed'
    $freshnessFailure = $null
    $freshnessFindings = @()
    if ($vulnerabilityStatus -ne 'passed') {
        $freshnessStatus = 'skipped'
        $freshnessFailure = 'Dependency freshness verification skipped because dependency vulnerability verification failed.'
        Write-DependencyTrace -Message 'freshness assertion skipped after vulnerability failure'
    }
    else {
        try {
            Write-DependencyTrace -Message 'freshness assertion started'
            $freshnessFindings = @(Get-DependencyFreshnessFindings -PackageReferences $packageReferences -PlaywrightInventory $playwrightInventory)
            Assert-DependencyFreshness -Findings $freshnessFindings
            Write-DependencyTrace -Message 'freshness assertion passed'
        }
        catch {
            $freshnessStatus = if ($WarnOnlyDependencyFreshness) { 'warning' } else { 'failed' }
            $freshnessFailure = $_.Exception.Message
            Write-DependencyTrace -Message "freshness assertion failed: $freshnessFailure"
        }
    }

    $inventory = [pscustomobject]@{
        schema_version = 1
        generated_at = (Get-Date).ToString('O')
        project = $projectPath
        dotnet = [pscustomobject]@{
            sdk_version = $dotnetSdkVersion
            runtimes = $dotnetRuntimes
        }
        nuget = [pscustomobject]@{
            package_references = $packageReferences
            package_list = $packageInventory
            vulnerability_check = [pscustomobject]@{
                source = if (![string]::IsNullOrWhiteSpace($DependencyVulnerabilityOutputFixture)) { 'fixture' } else { 'dotnet list package --vulnerable --include-transitive' }
                status = $vulnerabilityStatus
                failure_summary = $vulnerabilityFailure
                output = $vulnerabilityOutput
            }
            freshness_policy = [pscustomobject]@{
                source = if (![string]::IsNullOrWhiteSpace($DependencyFreshnessMetadataFixture)) { 'fixture' } else { 'nuget.org registration metadata' }
                status = $freshnessStatus
                failure_summary = $freshnessFailure
                max_age_days = $DependencyFreshnessMaxAgeDays
                warn_only = [bool]$WarnOnlyDependencyFreshness
                findings = $freshnessFindings
            }
        }
        playwright = $playwrightInventory
    }

    ConvertTo-SimpleJsonString $inventory | Set-Content -LiteralPath $DependencyInventoryPath -Encoding UTF8
    Write-DependencyTrace -Message 'dependency inventory written'
    Write-Output "Dependency inventory written: $DependencyInventoryPath"

    if ($vulnerabilityStatus -ne 'passed') {
        Write-DependencyTrace -Message 'throwing dependency vulnerability failure'
        throw $vulnerabilityFailure
    }

    if ($freshnessStatus -eq 'failed') {
        Write-DependencyTrace -Message 'throwing dependency freshness failure'
        throw $freshnessFailure
    }
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

function Invoke-RuntimeSidecarCheck {
    if ($SkipRuntimeSidecarChecks) {
        Write-Output "Skipping runtime sidecar checks because -SkipRuntimeSidecarChecks was supplied."
        return
    }

    Assert-RequiredFile -Path $RuntimeSidecarScriptPath -Description 'runtime sidecar verification script'
    [void](Invoke-ExternalCommand `
        -FileName 'powershell.exe' `
        -Arguments @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $RuntimeSidecarScriptPath,
            '-AppDir',
            $resolvedPublishDir,
            '-RequireReadOnlyAttribute',
            '-RequireInstallerScriptProtection',
            '-InstallerScriptPath',
            (Join-Path $PSScriptRoot 'Installer\install-player-assistant.ps1')
        ))
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
    -Name 'dependency freshness and vulnerability checks' `
    -Command 'dotnet --version; dotnet --list-runtimes; dotnet list package; dotnet list package --vulnerable --include-transitive; compare NuGet/Playwright versions with latest metadata; inspect Playwright runtime' `
    -Artifacts @(
        (Join-Path $PSScriptRoot $ProjectFileName),
        (Join-Path $resolvedPublishDir '.playwright\package\package.json'),
        (Join-Path $resolvedPublishDir '.playwright\package\browsers.json'),
        (Join-Path $resolvedPublishDir '.playwright\node\win32_x64\node.exe'),
        $DependencyInventoryPath
    ) `
    -Action { Invoke-DependencyFreshnessAndVulnerabilityCheck }

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
    -Name 'runtime sidecar ACL and path validation' `
    -Command "powershell.exe -NoProfile -ExecutionPolicy Bypass -File $RuntimeSidecarScriptPath -AppDir $resolvedPublishDir -RequireReadOnlyAttribute -RequireInstallerScriptProtection" `
    -Artifacts @(
        $RuntimeSidecarScriptPath,
        (Join-Path $resolvedPublishDir 'xp-passwords.json'),
        (Join-Path $PSScriptRoot 'Installer\install-player-assistant.ps1')
    ) `
    -Action { Invoke-RuntimeSidecarCheck }

Invoke-RcChecklistStep `
    -Name 'code-signing and Authenticode verification' `
    -Command 'verify Authenticode signatures for Release exe, published exe, release provenance, and setup.exe' `
    -Artifacts @(
        (Join-Path $resolvedReleaseDir $ExecutableFileName),
        (Join-Path $resolvedPublishDir $ExecutableFileName),
        (Join-Path $resolvedReleaseDir $ReleaseProvenanceFileName),
        (Join-Path $resolvedPublishDir $ReleaseProvenanceFileName),
        $(if (![string]::IsNullOrWhiteSpace($InstallerPath)) { $InstallerPath } else { Join-Path $PSScriptRoot "Release\installer\p-assist-$(Get-InstallerVersion -Version $projectVersion.Version).exe" })
    ) `
    -Action { Invoke-CodeSigningCheck }

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
