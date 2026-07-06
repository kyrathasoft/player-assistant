param(
    [string]$ReleaseDir = (Join-Path $PSScriptRoot 'Release'),
    [string]$PublishDir = (Join-Path $PSScriptRoot 'Release\publish'),
    [string]$OutputDir = (Join-Path $PSScriptRoot 'codex-scratch\diagnostics'),
    [string]$VerifyOnly,
    [switch]$NoPublishVerification,
    [switch]$NoPlanOutputs,
    [switch]$NoRetentionCleanup,
    [switch]$KeepStaging,
    [int]$ChildCommandTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'

$ExecutableFileName = 'player-assistant.exe'
$StartupHealthFileName = 'startup-health.json'
$StartupLogFileName = 'startup-errors.log'
$LastCrashFileName = 'last-crash.json'
$StartupRemediationFileName = 'startup-remediation.txt'
$SettingsFileName = 'settings.json'
$RuntimeInventoryFileName = 'release-runtime-inventory.json'
$ReleaseProvenanceFileName = 'release-provenance.json'
$OutboundNetworkDiagnosticsFileName = 'outbound-network-diagnostics.json'
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

function Write-StepLog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $line = "[$((Get-Date).ToString('O'))] $Message"
    Write-Output $line
    if (![string]::IsNullOrWhiteSpace($Script:DiagnosticTracePath)) {
        try {
            Add-Content -LiteralPath $Script:DiagnosticTracePath -Value $line -Encoding UTF8
        }
        catch {
        }
    }
}

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

function Invoke-ScriptBlockWithTimeout {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$ScriptBlock,

        [Parameter(Mandatory = $true)]
        [object[]]$ArgumentList = @(),

        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $job = Start-Job -ScriptBlock $ScriptBlock -ArgumentList $ArgumentList
    if ($null -eq $job) {
        throw "Unable to start timed operation: $Description"
    }

    try {
        if (-not (Wait-Job -Job $job -Timeout $TimeoutSeconds)) {
            Stop-Job -Job $job -ErrorAction SilentlyContinue | Out-Null
            throw "$Description timed out after $TimeoutSeconds seconds."
        }

        return Receive-Job -Job $job -ErrorAction Stop
    }
    finally {
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue | Out-Null
    }
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

    if ($Value -is [pscustomobject]) {
        $result = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            $result[$property.Name] = ConvertTo-PlainObject -Value $property.Value
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

    if ($Value -is [string]) {
        return Redact-Text -Text $Value
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

    if ($Value -is [pscustomobject]) {
        $result = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            $propertyName = [string]$property.Name
            if ($propertyName -match $SensitiveKeyPattern) {
                $result[$propertyName] = '[REDACTED]'
            }
            else {
                $result[$propertyName] = Redact-Object -Value $property.Value
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

function Get-Sha256HashText {
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

    Write-StepLog "Redacting text copy: $SourcePath -> $DestinationPath"
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
        Write-StepLog "Redacting JSON copy: $SourcePath -> $DestinationPath"
        $json = Get-Content -Raw -LiteralPath $SourcePath | ConvertFrom-Json
        $plain = ConvertTo-PlainObject -Value $json
        $redacted = Redact-Object -Value $plain
        Write-Utf8File -Path $DestinationPath -Contents (($redacted | ConvertTo-Json -Depth 20) + "`r`n")
    }
    catch {
        Write-StepLog "Falling back to text redaction for: $SourcePath"
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
        sha256 = Get-Sha256HashText -Path $SourcePath
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
        sha256 = Get-Sha256HashText -Path $path
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

    Write-StepLog "Collecting executable version summary for $Label ($Path)"
    $item = Get-Item -LiteralPath $Path
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    try {
        $signature = Invoke-ScriptBlockWithTimeout `
            -ScriptBlock {
                param($ExecutablePath)
                $command = Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue
                if ($null -eq $command) {
                    return [pscustomobject]@{
                        Status = 'Unavailable'
                        SignerCertificate = $null
                        TimeStamperCertificate = $null
                    }
                }

                Get-AuthenticodeSignature -LiteralPath $ExecutablePath |
                    Select-Object Status, SignerCertificate, TimeStamperCertificate
            } `
            -ArgumentList @($Path) `
            -TimeoutSeconds $ChildCommandTimeoutSeconds `
            -Description "Get-AuthenticodeSignature for $Path"
    }
    catch {
        $signature = [pscustomobject]@{
            Status = 'Unavailable'
            SignerCertificate = $null
            TimeStamperCertificate = $null
        }
    }

    return [pscustomobject]@{
        label = $Label
        path = $Path
        exists = $true
        length = $item.Length
        last_write_time_utc = $item.LastWriteTimeUtc.ToString('O')
        sha256 = Get-Sha256HashText -Path $Path
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

    $resolvedFileName = if ($FileName -ieq 'powershell.exe') { Get-PowerShellExecutable } else { $FileName }
    Write-StepLog "Starting child command: $resolvedFileName $($Arguments -join ' ')"

    try {
        $stdoutPath = Join-Path ([System.IO.Path]::GetTempPath()) "player-assistant-diagnostics-stdout-$([Guid]::NewGuid().ToString('N')).txt"
        $stderrPath = Join-Path ([System.IO.Path]::GetTempPath()) "player-assistant-diagnostics-stderr-$([Guid]::NewGuid().ToString('N')).txt"
        try {
            $process = Start-Process `
                -FilePath $resolvedFileName `
                -ArgumentList $Arguments `
                -WorkingDirectory $WorkingDirectory `
                -NoNewWindow `
                -PassThru `
                -RedirectStandardOutput $stdoutPath `
                -RedirectStandardError $stderrPath

            if ($null -eq $process) {
                throw "Unable to start child command: $resolvedFileName"
            }

            if (-not $process.WaitForExit($ChildCommandTimeoutSeconds * 1000)) {
                try {
                    $process.Kill()
                    $process.WaitForExit()
                }
                catch {
                }

                throw "Child command timed out after $ChildCommandTimeoutSeconds seconds: $resolvedFileName $($Arguments -join ' ')"
            }

            $standardOutput = if (Test-Path -LiteralPath $stdoutPath -PathType Leaf) { Get-Content -Raw -LiteralPath $stdoutPath } else { '' }
            $standardError = if (Test-Path -LiteralPath $stderrPath -PathType Leaf) { Get-Content -Raw -LiteralPath $stderrPath } else { '' }
            $combinedOutput = (($standardOutput, $standardError) -join [Environment]::NewLine)
            $exitCode = $process.ExitCode
        }
        finally {
            Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
        }

        $report = [ordered]@{
            command = "$resolvedFileName $($Arguments -join ' ')"
            exit_code = $exitCode
            stdout = Redact-Text -Text $combinedOutput
            stderr = ''
        }

        Write-Utf8File -Path $OutputPath -Contents (($report | ConvertTo-Json -Depth 6) + "`r`n")
        Write-StepLog "Completed child command with exit code ${exitCode}: $resolvedFileName $($Arguments -join ' ')"
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
$Script:DiagnosticTracePath = Join-Path $resolvedOutputDir 'collect-diagnostics-trace.log'
New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null
Remove-Item -LiteralPath $Script:DiagnosticTracePath -Force -ErrorAction SilentlyContinue
Write-StepLog 'Initialized collect-diagnostics trace'

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
    Write-StepLog 'Starting diagnostics retention cleanup'
    $retentionScriptPath = Join-Path $PSScriptRoot 'clean-diagnostics-retention.ps1'
    if (Test-Path -LiteralPath $retentionScriptPath -PathType Leaf) {
        & (Get-PowerShellExecutable) -NoProfile -ExecutionPolicy Bypass -File $retentionScriptPath -ScratchDir (Join-Path $PSScriptRoot 'codex-scratch')
    }
    Write-StepLog 'Completed diagnostics retention cleanup'
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stagingDirectory = Join-Path $resolvedOutputDir "player-assistant-diagnostics-$timestamp"
$zipPath = Join-Path $resolvedOutputDir "player-assistant-diagnostics-$timestamp.zip"
New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null
New-DirectoryClean -Path $stagingDirectory

try {
    Write-StepLog 'Writing metadata.json'
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

    Write-StepLog 'Writing version-metadata.json'
    $versionSummary = @(
        Get-ExecutableVersionSummary -Path (Join-Path $resolvedReleaseDir $ExecutableFileName) -Label 'Release'
        Get-ExecutableVersionSummary -Path (Join-Path $resolvedPublishDir $ExecutableFileName) -Label 'Publish'
    )
    Write-Utf8File -Path (Join-Path $stagingDirectory 'version-metadata.json') -Contents (($versionSummary | ConvertTo-Json -Depth 6) + "`r`n")

    Write-StepLog 'Writing redacted runtime diagnostics'
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedReleaseDir $StartupHealthFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\startup-health.json')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedReleaseDir $OutboundNetworkDiagnosticsFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\outbound-network-diagnostics.json')
    Write-RedactedTextCopy -SourcePath (Join-Path $resolvedReleaseDir $StartupLogFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\startup-errors.log')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedReleaseDir $LastCrashFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\last-crash.json')
    Write-RedactedTextCopy -SourcePath (Join-Path $resolvedReleaseDir $StartupRemediationFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\startup-remediation.txt')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedPublishDir $StartupHealthFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\startup-health.json')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedPublishDir $OutboundNetworkDiagnosticsFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\outbound-network-diagnostics.json')
    Write-RedactedTextCopy -SourcePath (Join-Path $resolvedPublishDir $StartupLogFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\startup-errors.log')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedPublishDir $LastCrashFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\last-crash.json')
    Write-RedactedTextCopy -SourcePath (Join-Path $resolvedPublishDir $StartupRemediationFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\startup-remediation.txt')

    Write-StepLog 'Writing redacted settings files'
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedReleaseDir $SettingsFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\settings.redacted.json')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedPublishDir $SettingsFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\settings.redacted.json')
    Write-StepLog 'Writing runtime inventory and provenance copies'
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedReleaseDir $RuntimeInventoryFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\release-runtime-inventory.json')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedPublishDir $RuntimeInventoryFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\release-runtime-inventory.json')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedReleaseDir $ReleaseProvenanceFileName) -DestinationPath (Join-Path $stagingDirectory 'Release\release-provenance.json')
    Write-RedactedJsonCopy -SourcePath (Join-Path $resolvedPublishDir $ReleaseProvenanceFileName) -DestinationPath (Join-Path $stagingDirectory 'publish\release-provenance.json')

    Write-StepLog 'Writing runtime-sidecars.json'
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

    Write-StepLog 'Validating staged diagnostic contents'
    Assert-DiagnosticStagingIsRedacted -Directory $stagingDirectory

    if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Write-StepLog 'Creating diagnostic zip archive'
    Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $zipPath -Force
    if (!(Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        throw "Diagnostic bundle was not created: $zipPath"
    }

    Write-StepLog 'Validating diagnostic zip archive'
    Assert-DiagnosticZipIsRedacted -ZipPath $zipPath
    Write-StepLog 'Re-validating staged diagnostic contents'
    Assert-DiagnosticStagingIsRedacted -Directory $stagingDirectory

    Write-Output "Diagnostic bundle created: $zipPath"
}
finally {
    if (!$KeepStaging -and (Test-Path -LiteralPath $stagingDirectory)) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
