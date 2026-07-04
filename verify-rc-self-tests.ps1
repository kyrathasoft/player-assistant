param(
    [string]$ReleaseDir = (Join-Path $PSScriptRoot 'Release'),
    [string]$PublishDir = (Join-Path $PSScriptRoot 'Release\publish')
)

$ErrorActionPreference = 'Stop'

$SecretScanScriptPath = Join-Path $PSScriptRoot 'verify-secret-scan.ps1'
$RcChecklistScriptPath = Join-Path $PSScriptRoot 'verify-rc-checklist.ps1'
$PublishScriptPath = Join-Path $PSScriptRoot 'publish-player-assistant.ps1'
$PublishRuntimeIntegrityScriptPath = Join-Path $PSScriptRoot 'verify-publish-runtime-integrity.ps1'
$SelfTestRoot = Join-Path $PSScriptRoot '.rc-self-tests'

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

        [string]$WorkingDirectory = $PSScriptRoot
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.Arguments = ConvertTo-ProcessArguments -Arguments $Arguments
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Unable to start command: $FileName"
    }

    $standardOutput = $process.StandardOutput.ReadToEnd()
    $standardError = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = (($standardOutput, $standardError) -join [Environment]::NewLine).TrimEnd()
    }
}

function Assert-CommandFailsWith {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$FileName,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedText,

        [string]$WorkingDirectory = $PSScriptRoot
    )

    $result = Invoke-ExternalCommand -FileName $FileName -Arguments $Arguments -WorkingDirectory $WorkingDirectory
    if ($result.ExitCode -eq 0) {
        throw "$Name self-test expected failure, but command passed."
    }

    if ($result.Output -notlike "*$ExpectedText*") {
        throw "$Name self-test failed for the wrong reason. Expected '$ExpectedText'. Output: $($result.Output)"
    }

    Write-Output "RC self-test passed: $Name"
}

function Assert-CommandPasses {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$FileName,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedText,

        [string]$WorkingDirectory = $PSScriptRoot
    )

    $result = Invoke-ExternalCommand -FileName $FileName -Arguments $Arguments -WorkingDirectory $WorkingDirectory
    if ($result.ExitCode -ne 0) {
        throw "$Name self-test expected success, but command failed. Output: $($result.Output)"
    }

    if ($result.Output -notlike "*$ExpectedText*") {
        throw "$Name self-test did not report expected text '$ExpectedText'. Output: $($result.Output)"
    }

    Write-Output "RC self-test passed: $Name"
}

function Assert-RcDryRunSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedStatus
    )

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Expected RC dry-run JSON was not written: $Path"
    }

    $summary = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    if ($summary.schema_version -ne 1) {
        throw "RC dry-run JSON schema_version should be 1."
    }

    if ($summary.status -ne $ExpectedStatus) {
        throw "RC dry-run JSON status '$($summary.status)' did not match expected '$ExpectedStatus'."
    }

    if ($summary.step_count -le 0 -or @($summary.steps).Count -le 0) {
        throw "RC dry-run JSON should include recorded checklist steps."
    }

    foreach ($step in @($summary.steps)) {
        foreach ($propertyName in @('name', 'status', 'elapsed_ms', 'command', 'artifacts')) {
            if (!$step.PSObject.Properties[$propertyName]) {
                throw "RC dry-run JSON step is missing '$propertyName'."
            }
        }
    }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory
    )

    $result = Invoke-ExternalCommand -FileName 'git' -Arguments $Arguments -WorkingDirectory $WorkingDirectory
    if ($result.ExitCode -ne 0) {
        throw "git $($Arguments -join ' ') failed in $WorkingDirectory. $($result.Output)"
    }
}

function New-DirectoryClean {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Invoke-SecretScanSelfTest {
    $repoPath = Join-Path $SelfTestRoot 'secret-repo'
    New-DirectoryClean -Path $repoPath
    Invoke-Git -Arguments @('init') -WorkingDirectory $repoPath
    Set-Content -LiteralPath (Join-Path $repoPath 'fixture.txt') -Value 'OPENAI_API_KEY=sk-test-abcdefghijklmnopqrstuvwxyz123456' -Encoding UTF8
    Invoke-Git -Arguments @('add', 'fixture.txt') -WorkingDirectory $repoPath

    Assert-CommandFailsWith `
        -Name 'secret scan catches tracked secret fixture' `
        -FileName 'powershell.exe' `
        -Arguments @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $SecretScanScriptPath,
            '-RepoRoot',
            $repoPath
        ) `
        -ExpectedText 'Secret scan failed.'
}

function Invoke-StartupHealthSelfTest {
    $healthPath = Join-Path $SelfTestRoot 'bad-startup-health.json'
    Set-Content -LiteralPath $healthPath -Encoding UTF8 -Value @'
{
  "schema_version": 1,
  "phases": [
    {
      "phase": "settings load",
      "status": "failed",
      "last_exception": {
        "message": "synthetic startup health failure"
      }
    },
    {
      "phase": "runtime housekeeping",
      "status": "succeeded"
    },
    {
      "phase": "configuration validation",
      "status": "succeeded"
    }
  ]
}
'@

    Assert-CommandFailsWith `
        -Name 'startup health verifier catches failed phase fixture' `
        -FileName 'powershell.exe' `
        -Arguments @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $PublishRuntimeIntegrityScriptPath,
            '-VerifyHealthFileOnly',
            $healthPath
        ) `
        -ExpectedText "Startup phase 'settings load' was 'failed'"
}

function Invoke-ReleaseManifestSelfTest {
    $resolvedPublishDir = Resolve-FullPath $PublishDir
    Assert-PathInsideRepo -Path $resolvedPublishDir -Description 'publish directory'

    $keywordTermsPath = Join-Path $resolvedPublishDir 'game-posts-key-terms.md'
    if (!(Test-Path -LiteralPath $keywordTermsPath -PathType Leaf)) {
        throw "Cannot run manifest self-test because game-posts-key-terms.md is missing: $keywordTermsPath"
    }

    $originalBytes = [System.IO.File]::ReadAllBytes($keywordTermsPath)
    try {
        $tamperedBytes = [byte[]]::new($originalBytes.Length)
        [System.Buffer]::BlockCopy($originalBytes, 0, $tamperedBytes, 0, $originalBytes.Length)
        $tamperedBytes[0] = $tamperedBytes[0] -bxor 1
        [System.IO.File]::WriteAllBytes($keywordTermsPath, $tamperedBytes)
        Assert-CommandFailsWith `
            -Name 'publish verification catches release manifest hash mismatch' `
            -FileName 'powershell.exe' `
            -Arguments @(
                '-NoProfile',
                '-ExecutionPolicy',
                'Bypass',
                '-File',
                $PublishScriptPath,
                '-OutputDir',
                $resolvedPublishDir,
                '-VerifyOnly'
            ) `
            -ExpectedText 'release-manifest.json SHA256 mismatch'
    }
    finally {
        [System.IO.File]::WriteAllBytes($keywordTermsPath, $originalBytes)
    }
}

function Invoke-ExpectedPathSelfTest {
    Assert-CommandFailsWith `
        -Name 'RC checklist catches expected-path mismatch' `
        -FileName 'powershell.exe' `
        -Arguments @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $RcChecklistScriptPath,
            '-ReleaseDir',
            (Resolve-FullPath $ReleaseDir),
            '-PublishDir',
            (Resolve-FullPath $PublishDir),
            '-ExpectedChangedPath',
            '__missing_expected_path_for_rc_self_test__.txt',
            '-SkipSelfTests',
            '-SkipSecretScan',
            '-SkipTests',
            '-SkipReleasePublishParity',
            '-SkipPublishedHealth',
            '-SkipPublishRuntimeIntegrity',
            '-SkipDiagnostics'
        ) `
        -ExpectedText 'Git status does not match ExpectedChangedPath.'
}

function Invoke-DryRunJsonPassingSelfTest {
    $summaryPath = Join-Path $SelfTestRoot 'passing-rc-dry-run.json'
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $RcChecklistScriptPath,
        '-ReleaseDir',
        (Resolve-FullPath $ReleaseDir),
        '-PublishDir',
        (Resolve-FullPath $PublishDir),
        '-DryRunJson',
        $summaryPath,
        '-SkipGit',
        '-SkipSelfTests',
        '-SkipSecretScan',
        '-SkipTests',
        '-SkipReleasePublishParity',
        '-SkipPublishedHealth',
        '-SkipPublishRuntimeIntegrity',
        '-SkipDiagnostics'
    )

    Assert-CommandPasses `
        -Name 'RC checklist writes passing dry-run JSON summary' `
        -FileName 'powershell.exe' `
        -Arguments $arguments `
        -ExpectedText 'RC dry-run JSON written:'

    Assert-RcDryRunSummary -Path $summaryPath -ExpectedStatus 'passed'
}

function Invoke-DryRunJsonFailingSelfTest {
    $summaryPath = Join-Path $SelfTestRoot 'failing-rc-dry-run.json'
    Assert-CommandFailsWith `
        -Name 'RC checklist writes failing dry-run JSON summary' `
        -FileName 'powershell.exe' `
        -Arguments @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $RcChecklistScriptPath,
            '-ReleaseDir',
            (Resolve-FullPath $ReleaseDir),
            '-PublishDir',
            (Resolve-FullPath $PublishDir),
            '-ExpectedChangedPath',
            '__missing_expected_path_for_rc_dry_run_self_test__.txt',
            '-DryRunJson',
            $summaryPath,
            '-SkipSelfTests',
            '-SkipSecretScan',
            '-SkipTests',
            '-SkipReleasePublishParity',
            '-SkipPublishedHealth',
            '-SkipPublishRuntimeIntegrity',
            '-SkipDiagnostics'
        ) `
        -ExpectedText 'RC checklist dry-run failed.'

    Assert-RcDryRunSummary -Path $summaryPath -ExpectedStatus 'failed'
}

Assert-PathInsideRepo -Path $SelfTestRoot -Description 'RC self-test workspace'

try {
    New-DirectoryClean -Path $SelfTestRoot
    Invoke-SecretScanSelfTest
    Invoke-StartupHealthSelfTest
    Invoke-ReleaseManifestSelfTest
    Invoke-ExpectedPathSelfTest
    Invoke-DryRunJsonPassingSelfTest
    Invoke-DryRunJsonFailingSelfTest
}
finally {
    if (Test-Path -LiteralPath $SelfTestRoot) {
        Remove-Item -LiteralPath $SelfTestRoot -Recurse -Force
    }
}

Write-Output 'RC checklist self-tests passed.'
