param(
    [string]$ReleaseDir = (Join-Path $PSScriptRoot 'Release'),
    [string]$PublishDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [string]$OutputDir = (Join-Path $PSScriptRoot 'codex-scratch\diagnostics'),
    [string]$VerifyOnly,
    [switch]$NoPublishVerification,
    [switch]$NoPlanOutputs,
    [switch]$NoRetentionCleanup,
    [switch]$KeepStaging
)

$ErrorActionPreference = 'Stop'

$ExecutableFileName = 'player-assistant.exe'
$StartupHealthFileName = 'startup-health.json'
$StartupLogFileName = 'startup-errors.log'
$LastCrashFileName = 'last-crash.json'
$StartupRemediationFileName = 'startup-remediation.txt'
$SettingsFileName = 'settings.json'
$SettingsLocalFileName = 'settings.local.json'
$RuntimeInventoryFileName = 'release-runtime-inventory.json'
$ReleaseProvenanceFileName = 'release-provenance.json'
$ForbiddenFileNames = @(
    'rpol-storage-state.json',
    'cookies.json',
    'storage-state.json'
)
$ForbiddenDirectoryNames = @(
    '.playwright',
    'temp'
)
$RuntimeSidecars = @(
    'release-manifest.json',
    'release-runtime-inventory.json',
    'release-provenance.json',
    'keyword-index.json',
    'game-posts-key-terms.md',
    'sitemap.xml',
    'sitemap-keyword-urls.json',
    'game-forum-chapter-prefixes.txt',
    'game-forum-chapter-downloads.txt',
    'game-forum-aside-downloads.txt',
    'game-forum-ooc-downloads.txt'
)
$SensitiveKeyPattern = '(?i)(password|user\s*name|username|credential|secret|token|cookie|payload|storage\s*state)'
$UnredactedCredentialPatterns = @(
    '"RPOL password"\s*:\s*"(?!\[REDACTED\])[^"]+',
    '"RPOL user name"\s*:\s*"(?!\[REDACTED\])[^"]+',
    '(?i)RPOL password\s*[:=]\s*(?!\[REDACTED\])\S+',
    '(?i)RPOL user name\s*[:=]\s*(?!\[REDACTED\])\S+',
    '(?i)Authorization\s*:\s*Bearer\s+(?!\[REDACTED\])\S+',
    '(?i)Cookie\s*:\s*(?!\[REDACTED\])\S+',
    '(?i)(password|token|secret)=((?!\[REDACTED\])[^&\s]+)',
    'https?://(?!\[REDACTED\]:\[REDACTED\]@)[^/\s:@]+:[^/\s@]+@'
)

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

function Write-Utf8File {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [AllowNull()]
        [string]$Contents
    )

    $parent = Split-Path -Parent $Path
    if (![string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    $resolvedContents = ''
    if ($null -ne $Contents) {
        $resolvedContents = $Contents
    }

    [System.IO.File]::WriteAllText(
        $Path,
        $resolvedContents,
        [System.Text.UTF8Encoding]::new($false))
}

function ConvertTo-PlainObject {
    param(
        [AllowNull()]
        [object]$Value
    )

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [string] -or $Value -is [bool] -or $Value -is [int] -or
        $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) {
        return $Value
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in $Value.Keys) {
            $result[[string]$key] = ConvertTo-PlainObject -Value $Value[$key]
        }

        return $result
    }

    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $items = @()
        foreach ($item in $Value) {
            $items += ConvertTo-PlainObject -Value $item
        }

        return $items
    }

    if ($Value.PSObject -and $Value.PSObject.Properties.Count -gt 0) {
        $result = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            $result[$property.Name] = ConvertTo-PlainObject -Value $property.Value
        }

        return $result
    }

    return [string]$Value
}

function Redact-Object {
    param(
        [AllowNull()]
        [object]$Value
    )

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in $Value.Keys) {
            $keyText = [string]$key
            if ($keyText -match $SensitiveKeyPattern) {
                $result[$keyText] = '[REDACTED]'
            }
            else {
                $result[$keyText] = Redact-Object -Value $Value[$key]
            }
        }

        return $result
    }

    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $items = @()
        foreach ($item in $Value) {
            $items += Redact-Object -Value $item
        }

        return $items
    }

    return $Value
}

function Redact-Text {
    param(
        [AllowNull()]
        [string]$Text
    )

    if ($null -eq $Text) {
        return ''
    }

    $redacted = $Text
    $redacted = $redacted -replace '(?i)("RPOL password"\s*:\s*)"[^"]*"', '$1"[REDACTED]"'
    $redacted = $redacted -replace '(?i)("RPOL user name"\s*:\s*)"[^"]*"', '$1"[REDACTED]"'
    $redacted = $redacted -replace '(?i)(RPOL password\s*[:=]\s*)\S+', '$1[REDACTED]'
    $redacted = $redacted -replace '(?i)(RPOL user name\s*[:=]\s*)\S+', '$1[REDACTED]'
    $redacted = $redacted -replace '(?i)(Authorization\s*:\s*Bearer\s+)\S+', '$1[REDACTED]'
    $redacted = $redacted -replace '(?i)(Cookie\s*:\s*).+', '$1[REDACTED]'
    $redacted = $redacted -replace '(?i)([?&](?:password|token|secret)=)[^&\s]+', '$1[REDACTED]'
    $redacted = $redacted -replace '(https?://)([^/\s:@]+):([^/\s@]+)@', '$1[REDACTED]:[REDACTED]@'
    $redacted = $redacted -replace '(?i)("payload"\s*:\s*)"[^"]*"', '$1"[REDACTED]"'
    $redacted = $redacted -replace '(?i)("cookie"\s*:\s*)"[^"]*"', '$1"[REDACTED]"'
    $redacted = $redacted -replace '(?i)("token"\s*:\s*)"[^"]*"', '$1"[REDACTED]"'
    $redacted = $redacted -replace '(?i)("authorization"\s*:\s*)"Bearer\s+[^"]*"', '$1"[REDACTED]"'
    return $redacted
}

function Write-RedactedTextCopy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    if (!(Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        Write-Utf8File -Path $DestinationPath -Contents "Missing: $SourcePath`r`n"
        return
    }

    $contents = Get-Content -Raw -LiteralPath $SourcePath
    Write-Utf8File -Path $DestinationPath -Contents (Redact-Text -Text $contents)
}

function Write-RedactedJsonCopy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    if (!(Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        Write-Utf8File -Path $DestinationPath -Contents "Missing: $SourcePath`r`n"
        return
    }

    try {
        $json = Get-Content -Raw -LiteralPath $SourcePath | ConvertFrom-Json
        $plain = ConvertTo-PlainObject -Value $json
        $redacted = Redact-Object -Value $plain
        Write-Utf8File -Path $DestinationPath -Contents (($redacted | ConvertTo-Json -Depth 20) + "`r`n")
    }
    catch {
        Write-RedactedTextCopy -SourcePath $SourcePath -DestinationPath $DestinationPath
    }
}

function Write-LocalSettingsShape {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    if (!(Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        Write-Utf8File -Path $DestinationPath -Contents "Missing: $SourcePath`r`n"
        return
    }

    $item = Get-Item -LiteralPath $SourcePath
    $shape = [ordered]@{
        file_name = $SettingsLocalFileName
        exists = $true
        length = $item.Length
        last_write_time_utc = $item.LastWriteTimeUtc.ToString('O')
        sha256 = (Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash
    }

    try {
        $json = Get-Content -Raw -LiteralPath $SourcePath | ConvertFrom-Json
        $properties = @($json.PSObject.Properties.Name)
        $shape['json_valid'] = $true
        $shape['property_count'] = $properties.Count
        $shape['schema_version'] = if ($json.PSObject.Properties['schema_version']) { $json.schema_version } else { $null }
        $shape['encrypted_format'] = if ($json.PSObject.Properties['format']) { [string]$json.format } else { $null }
        $shape['has_payload'] = [bool]$json.PSObject.Properties['payload']
        $shape['payload_length'] = if ($json.PSObject.Properties['payload']) { ([string]$json.payload).Length } else { $null }
        $shape['key_scope'] = if ($json.PSObject.Properties['key_scope']) {
            [ordered]@{
                machine_bound = [bool]$json.key_scope.machine_bound
                user_bound = [bool]$json.key_scope.user_bound
                install_path_bound = [bool]$json.key_scope.install_path_bound
                has_scope_hash = ![string]::IsNullOrWhiteSpace([string]$json.key_scope.scope_hash)
            }
        } else {
            $null
        }
        $shape['plaintext_key_names'] = if ($json.PSObject.Properties['payload']) { @() } else { '[REDACTED]' }
    }
    catch {
        $shape['json_valid'] = $false
        $shape['parse_error'] = $_.Exception.Message
    }

    Write-Utf8File -Path $DestinationPath -Contents (($shape | ConvertTo-Json -Depth 10) + "`r`n")
}

function Get-FileSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseDirectory,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $path = Join-Path $BaseDirectory $RelativePath
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        return [pscustomobject]@{
            relative_path = $RelativePath
            exists = $false
            length = $null
            last_write_time_utc = $null
            sha256 = $null
        }
    }

    $item = Get-Item -LiteralPath $path
    return [pscustomobject]@{
        relative_path = $RelativePath
        exists = $true
        length = $item.Length
        last_write_time_utc = $item.LastWriteTimeUtc.ToString('O')
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
}

function Get-ExecutableVersionSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [pscustomobject]@{
            label = $Label
            path = $Path
            exists = $false
        }
    }

    $item = Get-Item -LiteralPath $Path
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    return [pscustomobject]@{
        label = $Label
        path = $Path
        exists = $true
        length = $item.Length
        last_write_time_utc = $item.LastWriteTimeUtc.ToString('O')
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
        file_version = $version.FileVersion
        product_version = $version.ProductVersion
        product_name = $version.ProductName
        original_file_name = $version.OriginalFilename
        authenticode_signature = [ordered]@{
            status = [string]$signature.Status
            signer_subject = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
            thumbprint = if ($signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { $null }
            issuer = if ($signature.SignerCertificate) { $signature.SignerCertificate.Issuer } else { $null }
            not_before = if ($signature.SignerCertificate) { $signature.SignerCertificate.NotBefore.ToString('O') } else { $null }
            not_after = if ($signature.SignerCertificate) { $signature.SignerCertificate.NotAfter.ToString('O') } else { $null }
            timestamp_subject = if ($signature.TimeStamperCertificate) { $signature.TimeStamperCertificate.Subject } else { $null }
        }
    }
}

function Invoke-CapturedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string]$OutputPath
    )

    try {
        $resolvedFileName = if ($FileName -ieq 'powershell.exe') { Get-PowerShellExecutable } else { $FileName }
        Push-Location $WorkingDirectory
        try {
            $combinedOutput = & $resolvedFileName @Arguments 2>&1 | Out-String
            $exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
        }
        finally {
            Pop-Location
        }

        $report = [ordered]@{
            command = "$resolvedFileName $($Arguments -join ' ')"
            exit_code = $exitCode
            stdout = Redact-Text -Text $combinedOutput
            stderr = ''
        }

        Write-Utf8File -Path $OutputPath -Contents (($report | ConvertTo-Json -Depth 6) + "`r`n")
    }
    catch {
        $report = [ordered]@{
            command = "$resolvedFileName $($Arguments -join ' ')"
            failed_to_start = $true
            error = Redact-Text -Text $_.Exception.Message
        }
        Write-Utf8File -Path $OutputPath -Contents (($report | ConvertTo-Json -Depth 6) + "`r`n")
    }
}

function Assert-DiagnosticStagingIsRedacted {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    foreach ($fileName in $ForbiddenFileNames) {
        $matches = @(Get-ChildItem -LiteralPath $Directory -Recurse -Force -File -Filter $fileName -ErrorAction SilentlyContinue)
        if ($matches.Count -gt 0) {
            throw "Diagnostic staging contains forbidden sensitive file '$fileName'."
        }
    }

    foreach ($directoryName in $ForbiddenDirectoryNames) {
        $matches = @(Get-ChildItem -LiteralPath $Directory -Recurse -Force -Directory -Filter $directoryName -ErrorAction SilentlyContinue)
        if ($matches.Count -gt 0) {
            throw "Diagnostic staging contains forbidden sensitive directory '$directoryName'."
        }
    }

    $filesToScan = Get-ChildItem -LiteralPath $Directory -Recurse -Force -File |
        Where-Object { $_.Extension -in @('.json', '.txt', '.log', '.md', '.xml', '.ps1') }
    foreach ($file in $filesToScan) {
        $content = Get-Content -Raw -LiteralPath $file.FullName
        foreach ($pattern in $UnredactedCredentialPatterns) {
            if ($content -match $pattern) {
                throw "Diagnostic staging contains an unredacted credential marker in $($file.FullName)."
            }
        }
    }
}

function Assert-DiagnosticZipIsRedacted {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ZipPath
    )

    if (!(Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
        throw "Diagnostic bundle is missing: $ZipPath"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $archive.Entries) {
            $entryName = [System.IO.Path]::GetFileName($entry.FullName)
            if ($ForbiddenFileNames -contains $entryName) {
                throw "Diagnostic bundle contains forbidden sensitive file '$entryName'."
            }

            $entryParts = $entry.FullName -split '[\\/]'
            foreach ($directoryName in $ForbiddenDirectoryNames) {
                if ($entryParts -contains $directoryName) {
                    throw "Diagnostic bundle contains forbidden sensitive directory '$directoryName'."
                }
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $verifyDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "player-assistant-diagnostics-verify-$([Guid]::NewGuid().ToString('N'))"
    try {
        Expand-Archive -LiteralPath $ZipPath -DestinationPath $verifyDirectory -Force
        Assert-DiagnosticStagingIsRedacted -Directory $verifyDirectory
    }
    finally {
        if (Test-Path -LiteralPath $verifyDirectory) {
            Remove-Item -LiteralPath $verifyDirectory -Recurse -Force
        }
    }
}

$resolvedOutputDir = Resolve-FullPath $OutputDir
Assert-PathInsideRepo -Path $resolvedOutputDir -Description 'diagnostic output directory'

if (![string]::IsNullOrWhiteSpace($VerifyOnly)) {
    $resolvedVerifyOnly = Resolve-FullPath $VerifyOnly
    Assert-PathInsideRepo -Path $resolvedVerifyOnly -Description 'diagnostic bundle'
    Assert-DiagnosticZipIsRedacted -ZipPath $resolvedVerifyOnly
    Write-Output "Diagnostic bundle verification passed: $resolvedVerifyOnly"
    return
}

$resolvedReleaseDir = Resolve-FullPath $ReleaseDir
$resolvedPublishDir = Resolve-FullPath $PublishDir
Assert-PathInsideRepo -Path $resolvedReleaseDir -Description 'Release directory'
Assert-PathInsideRepo -Path $resolvedPublishDir -Description 'publish directory'

if (!$NoRetentionCleanup) {
    $retentionScriptPath = Join-Path $PSScriptRoot 'clean-diagnostics-retention.ps1'
    if (Test-Path -LiteralPath $retentionScriptPath -PathType Leaf) {
        & (Get-PowerShellExecutable) -NoProfile -ExecutionPolicy Bypass -File $retentionScriptPath -ScratchDir (Join-Path $PSScriptRoot 'codex-scratch')
    }
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stagingDirectory = Join-Path $resolvedOutputDir "player-assistant-diagnostics-$timestamp"
$zipPath = Join-Path $resolvedOutputDir "player-assistant-diagnostics-$timestamp.zip"
New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null
New-DirectoryClean -Path $stagingDirectory

try {
    $metadata = [ordered]@{
        collected_at = (Get-Date).ToString('O')
        repo_root = (Resolve-FullPath $PSScriptRoot)
        release_dir = $resolvedReleaseDir
        publish_dir = $resolvedPublishDir
        host = [Environment]::MachineName
        user = '[REDACTED]'
        powershell = $PSVersionTable.PSVersion.ToString()
        os = [Environment]::OSVersion.VersionString
    }
    Write-Utf8File -Path (Join-Path $stagingDirectory 'metadata.json') -Contents (($metadata | ConvertTo-Json -Depth 6) + "`r`n")

    $versionSummary = @(
        Get-ExecutableVersionSummary -Path (Join-Path $resolvedReleaseDir $ExecutableFileName) -Label 'Release'
        Get-ExecutableVersionSummary -Path (Join-Path $resolvedPublishDir $ExecutableFileName) -Label 'Publish'
    )
    Write-Utf8File -Path (Join-Path $stagingDirectory 'version-metadata.json') -Contents (($versionSummary | ConvertTo-Json -Depth 6) + "`r`n")

    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedReleaseDir $StartupHealthFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\startup-health.json')
    Write-RedactedTextCopy -SourcePath (Join-Path $resolvedReleaseDir $StartupLogFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\startup-errors.log')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedReleaseDir $LastCrashFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\last-crash.json')
    Write-RedactedTextCopy -SourcePath (Join-Path $resolvedReleaseDir $StartupRemediationFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\startup-remediation.txt')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedPublishDir $StartupHealthFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\startup-health.json')
    Write-RedactedTextCopy -SourcePath (Join-Path $resolvedPublishDir $StartupLogFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\startup-errors.log')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedPublishDir $LastCrashFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\last-crash.json')
    Write-RedactedTextCopy -SourcePath (Join-Path $resolvedPublishDir $StartupRemediationFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\startup-remediation.txt')

    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedReleaseDir $SettingsFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\settings.redacted.json')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedPublishDir $SettingsFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\settings.redacted.json')
    Write-LocalSettingsShape -SourcePath (Join-Path $resolvedReleaseDir $SettingsLocalFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\settings.local.shape.json')
    Write-LocalSettingsShape -SourcePath (Join-Path $resolvedPublishDir $SettingsLocalFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\settings.local.shape.json')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedReleaseDir $RuntimeInventoryFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\release-runtime-inventory.json')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedPublishDir $RuntimeInventoryFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\release-runtime-inventory.json')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedReleaseDir $ReleaseProvenanceFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\release-provenance.json')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedPublishDir $ReleaseProvenanceFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\release-provenance.json')

    $sidecarSummary = [ordered]@{
        release = @($RuntimeSidecars | ForEach-Object { Get-FileSummary -BaseDirectory $resolvedReleaseDir -RelativePath $_ })
        publish = @($RuntimeSidecars | ForEach-Object { Get-FileSummary -BaseDirectory $resolvedPublishDir -RelativePath $_ })
        publish_playwright_runtime = @(
            Get-FileSummary -BaseDirectory $resolvedPublishDir -RelativePath '.playwright\node\win32_x64\node.exe'
            Get-FileSummary -BaseDirectory $resolvedPublishDir -RelativePath '.playwright\package\package.json'
            Get-FileSummary -BaseDirectory $resolvedPublishDir -RelativePath '.playwright\package\browsers.json'
        )
    }
    Write-Utf8File -Path (Join-Path $stagingDirectory 'runtime-sidecars.json') -Contents (($sidecarSummary | ConvertTo-Json -Depth 8) + "`r`n")

    if (!$NoPublishVerification) {
        Invoke-CapturedCommand `
            -FileName 'powershell.exe' `
            -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'publish-player-assistant.ps1'), '-OutputDir', $resolvedPublishDir, '-VerifyOnly') `
            -WorkingDirectory $PSScriptRoot `
            -OutputPath (Join-Path $stagingDirectory 'verification\publish-verify.json')
    }

    if (!$NoPlanOutputs) {
        Invoke-CapturedCommand `
            -FileName 'powershell.exe' `
            -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'verify-release-startup-smoke.ps1'), '-ReleaseDir', $resolvedReleaseDir, '-PlanOnly') `
            -WorkingDirectory $PSScriptRoot `
            -OutputPath (Join-Path $stagingDirectory 'verification\release-startup-smoke-plan.json')

        Invoke-CapturedCommand `
            -FileName 'powershell.exe' `
            -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'verify-publish-runtime-integrity.ps1'), '-ReleaseDir', $resolvedReleaseDir, '-PublishDir', $resolvedPublishDir, '-PlanOnly') `
            -WorkingDirectory $PSScriptRoot `
            -OutputPath (Join-Path $stagingDirectory 'verification\publish-runtime-integrity-plan.json')
    }

    Assert-DiagnosticStagingIsRedacted -Directory $stagingDirectory

    if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $zipPath -Force
    if (!(Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        throw "Diagnostic bundle was not created: $zipPath"
    }

    Assert-DiagnosticZipIsRedacted -ZipPath $zipPath
    Assert-DiagnosticStagingIsRedacted -Directory $stagingDirectory

    Write-Output "Diagnostic bundle created: $zipPath"
}
finally {
    if (!$KeepStaging -and (Test-Path -LiteralPath $stagingDirectory)) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
