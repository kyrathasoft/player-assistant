param(
    [string]$PackagePath,
    [string]$UpdateVersion,
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'version-metadata.ps1')
if ([string]::IsNullOrWhiteSpace($UpdateVersion)) {
    $UpdateVersion = (Get-PlayerAssistantVersionMetadata -RepoRoot $PSScriptRoot).Version
}
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Join-Path $PSScriptRoot "Release\installer\player-assistant-$UpdateVersion-installer.zip"
}

$HostedSettingsOverrideEnvironmentVariable = 'PLAYER_ASSISTANT_HOSTED_LOCAL_SETTINGS_URL_OVERRIDE'
$HostedSettingsPublicKeyEnvironmentVariable = 'PLAYER_ASSISTANT_HOSTED_SETTINGS_PUBLIC_KEY_PEM'
$UpdateBaseUrlOverrideEnvironmentVariable = 'PLAYER_ASSISTANT_UPDATE_BASE_URL'
$UpdatePublicKeyEnvironmentVariable = 'PLAYER_ASSISTANT_UPDATE_MANIFEST_PUBLIC_KEY_PEM'
$HostedSettingsRelativePath = 'scarlethorizons/settings.local.json'
$UpdateManifestRelativePath = 'scarlethorizons/p-assist-updates.json'
$UpdateSignatureRelativePath = 'scarlethorizons/p-assist-updates.json.sig'
$installerVersion = ($UpdateVersion -split '[-+]')[0]
$UpdateArchiveFileName = "p-assist-$installerVersion.zip"
$UpdateInstallerFileName = "p-assist-$installerVersion.exe"
$CredentialTargets = @(
    'PlayerAssistant/RPOL/UserName',
    'PlayerAssistant/RPOL/Password',
    'PlayerAssistant/RPOL/StorageState'
)
$UserDataRelativeRoot = 'KyrathaSoft\player-assistant'
$SettingsEncryptionSeed = 'PlayerAssistant.LocalSettings.v1'

function Assert-RequiredFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required $Description is missing: $Path"
    }

    if ((Get-Item -LiteralPath $Path).Length -le 0) {
        throw "Required $Description is empty: $Path"
    }
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function ConvertTo-PortableEncryptedSettingsJson {
    param([Parameter(Mandatory = $true)][hashtable]$Settings)

    $plaintextJson = $Settings | ConvertTo-Json -Depth 10
    $plaintextBytes = [System.Text.Encoding]::UTF8.GetBytes($plaintextJson)
    $iv = [byte[]]::new(16)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($iv)
    }
    finally {
        $rng.Dispose()
    }

    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.Key = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($SettingsEncryptionSeed))
        $aes.IV = $iv
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $encryptor = $aes.CreateEncryptor()
        try {
            $ciphertext = $encryptor.TransformFinalBlock($plaintextBytes, 0, $plaintextBytes.Length)
        }
        finally {
            $encryptor.Dispose()
        }
    }
    finally {
        $aes.Dispose()
    }

    $protectedContent = [byte[]]::new($iv.Length + $ciphertext.Length)
    [System.Buffer]::BlockCopy($iv, 0, $protectedContent, 0, $iv.Length)
    [System.Buffer]::BlockCopy($ciphertext, 0, $protectedContent, $iv.Length, $ciphertext.Length)
    $hmacKey = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes("$SettingsEncryptionSeed.hmac"))
    $hmac = [System.Security.Cryptography.HMACSHA256]::new($hmacKey)
    try {
        $tag = $hmac.ComputeHash($protectedContent)
    }
    finally {
        $hmac.Dispose()
    }

    $payloadBytes = [byte[]]::new($protectedContent.Length + $tag.Length)
    [System.Buffer]::BlockCopy($protectedContent, 0, $payloadBytes, 0, $protectedContent.Length)
    [System.Buffer]::BlockCopy($tag, 0, $payloadBytes, $protectedContent.Length, $tag.Length)

    return ([ordered]@{
        schema_version = 1
        format = 'app-protected-v2'
        payload = [Convert]::ToBase64String($payloadBytes)
    } | ConvertTo-Json -Depth 5)
}

function Write-FramedString {
    param(
        [Parameter(Mandatory = $true)][System.IO.BinaryWriter]$Writer,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $Writer.Write([int]$bytes.Length)
    $Writer.Write($bytes)
}

function New-SignedHostedSettingsJson {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Settings,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][System.Security.Cryptography.RSA]$SigningKey
    )

    $portableEncryptedSettingsJson = ConvertTo-PortableEncryptedSettingsJson -Settings $Settings
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream, [System.Text.Encoding]::UTF8, $true)
    try {
        Write-FramedString -Writer $writer -Value 'signed-hosted-settings-v1'
        Write-FramedString -Writer $writer -Value 'player-assistant-hosted-settings'
        Write-FramedString -Writer $writer -Value $Version
        Write-FramedString -Writer $writer -Value $portableEncryptedSettingsJson
        $writer.Flush()
        $payloadBytes = $stream.ToArray()
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }

    $signature = [Convert]::ToBase64String(
        $SigningKey.SignData(
            $payloadBytes,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1))

    return ([ordered]@{
        schema_version = 1
        format = 'signed-hosted-settings-v1'
        content_id = 'player-assistant-hosted-settings'
        version = $Version
        encrypted_settings = $portableEncryptedSettingsJson
        signature = $signature
    } | ConvertTo-Json -Depth 10)
}

function New-SignedUpdateManifest {
    param(
        [Parameter(Mandatory = $true)][string]$ArchiveSha256,
        [Parameter(Mandatory = $true)][string]$InstallerSha256,
        [Parameter(Mandatory = $true)][System.Security.Cryptography.RSA]$SigningKey
    )

    $manifest = [ordered]@{
        schema_version = 1
        updates = @(
            [ordered]@{
                version = $UpdateVersion
                url = $UpdateArchiveFileName
                sha256 = $ArchiveSha256
                installer_url = $UpdateInstallerFileName
                installer_sha256 = $InstallerSha256
            }
        )
    } | ConvertTo-Json -Depth 10
    $manifestBytes = [System.Text.Encoding]::UTF8.GetBytes($manifest)
    $signature = [Convert]::ToBase64String(
        $SigningKey.SignData(
            $manifestBytes,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1))

    return [pscustomobject]@{
        ManifestJson = $manifest
        SignatureText = $signature
    }
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Start-FixtureServer {
    param(
        [Parameter(Mandatory = $true)][string]$RootDirectory,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    $serverScriptPath = Join-Path $RootDirectory 'fixture-server.ps1'
    @'
param(
    [string]$RootDirectory,
    [int]$Port,
    [string]$LogPath
)

$ErrorActionPreference = 'Stop'
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        try {
            $requestPath = $context.Request.Url.AbsolutePath.TrimStart('/')
            Add-Content -LiteralPath $LogPath -Value $requestPath -Encoding UTF8
            $localPath = Join-Path $RootDirectory ($requestPath -replace '/', '\')
            if (Test-Path -LiteralPath $localPath -PathType Leaf) {
                $bytes = [System.IO.File]::ReadAllBytes($localPath)
                switch ([System.IO.Path]::GetExtension($localPath).ToLowerInvariant()) {
                    '.json' { $context.Response.ContentType = 'application/json' }
                    '.sig' { $context.Response.ContentType = 'text/plain' }
                    '.zip' { $context.Response.ContentType = 'application/zip' }
                    '.exe' { $context.Response.ContentType = 'application/octet-stream' }
                    default { $context.Response.ContentType = 'application/octet-stream' }
                }
                $context.Response.StatusCode = 200
                $context.Response.ContentLength64 = $bytes.Length
                $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            }
            else {
                $context.Response.StatusCode = 404
            }
        }
        finally {
            $context.Response.OutputStream.Close()
        }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
}
'@ | Set-Content -LiteralPath $serverScriptPath -Encoding UTF8

    return Start-Process `
        -FilePath 'powershell.exe' `
        -ArgumentList @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', $serverScriptPath,
            '-RootDirectory', $RootDirectory,
            '-Port', $Port,
            '-LogPath', $LogPath
        ) `
        -PassThru `
        -WindowStyle Hidden
}

function Invoke-AppCommand {
    param(
        [Parameter(Mandatory = $true)][string]$ExecutablePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][hashtable]$EnvironmentVariables,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ExecutablePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    foreach ($entry in $EnvironmentVariables.GetEnumerator()) {
        $startInfo.Environment[$entry.Key] = [string]$entry.Value
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Unable to start $ExecutablePath."
    }

    if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            $process.Kill($true)
            $process.WaitForExit()
        }
        catch {
        }

        throw "Timed out after $TimeoutSeconds seconds waiting for $ExecutablePath $($Arguments -join ' ')."
    }

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = (($process.StandardOutput.ReadToEnd(), $process.StandardError.ReadToEnd()) -join [Environment]::NewLine).Trim()
    }
}

function Add-CredentialReaderType {
    if ('PlayerAssistantSmoke.CredentialReader' -as [type]) {
        return
    }

    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PlayerAssistantSmoke
{
    public static class CredentialReader
    {
        private const int CredentialTypeGeneric = 1;
        private const int ErrorNotFound = 1168;

        public static string ReadSecretUtf8(string targetName)
        {
            if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound)
                {
                    return null;
                }

                throw new Win32Exception(error, $"Unable to read credential '{targetName}'.");
            }

            try
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPointer);
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0)
                {
                    return string.Empty;
                }

                var secretBytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, secretBytes, 0, credential.CredentialBlobSize);
                return Encoding.UTF8.GetString(secretBytes);
            }
            finally
            {
                CredFree(credentialPointer);
            }
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredReadW", SetLastError = true)]
        private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = false)]
        private static extern void CredFree(IntPtr credentialPtr);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public Win32FileTime LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32FileTime
        {
            public int dwLowDateTime;
            public int dwHighDateTime;
        }
    }
}
'@
}

function Remove-CredentialTargets {
    $previousNativeErrorPreference = $PSNativeCommandUseErrorActionPreference
    try {
        $PSNativeCommandUseErrorActionPreference = $false
        foreach ($target in $CredentialTargets) {
            & cmdkey.exe /delete:$target 2>$null | Out-Null
            $global:LASTEXITCODE = 0
        }
    }
    finally {
        $PSNativeCommandUseErrorActionPreference = $previousNativeErrorPreference
    }
}

function Read-CredentialSecret {
    param([Parameter(Mandatory = $true)][string]$Target)
    Add-CredentialReaderType
    return [PlayerAssistantSmoke.CredentialReader]::ReadSecretUtf8($Target)
}

Assert-RequiredFile -Path $PackagePath -Description 'installer package'

$resolvedPackagePath = [System.IO.Path]::GetFullPath($PackagePath)
$scratchRoot = Join-Path $PSScriptRoot ("codex-scratch\installer-smoke-{0}" -f [Guid]::NewGuid().ToString('N'))
$installerExtractRoot = Join-Path $scratchRoot 'installer'
$fixtureRoot = Join-Path $scratchRoot 'fixture-root'
$logPath = Join-Path $scratchRoot 'fixture-requests.log'
$installedRoot = Join-Path $scratchRoot 'installed-app'
$runtimeUserDataRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) $UserDataRelativeRoot
$runtimeSharedDataRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) $UserDataRelativeRoot
$backupRoot = Join-Path $scratchRoot 'runtime-backup'
$serverProcess = $null
$verificationSucceeded = $false

try {
    New-Item -ItemType Directory -Force -Path $scratchRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $installerExtractRoot | Out-Null
    Expand-Archive -LiteralPath $resolvedPackagePath -DestinationPath $installerExtractRoot -Force
    $installerRoot = Get-ChildItem -LiteralPath $installerExtractRoot -Directory | Select-Object -First 1
    if ($null -eq $installerRoot) {
        throw "Installer package did not contain a root directory."
    }

    $payloadRoot = Join-Path $installerRoot.FullName 'payload'
    Assert-RequiredFile -Path (Join-Path $payloadRoot 'player-assistant.exe') -Description 'installer payload executable'

    New-Item -ItemType Directory -Force -Path $installedRoot | Out-Null
    Get-ChildItem -LiteralPath $payloadRoot -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $installedRoot -Recurse -Force
    }

    $localSettingsJson = ConvertTo-PortableEncryptedSettingsJson -Settings @{
        'XP Tracking' = 'https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking'
    }
    Set-Content -LiteralPath (Join-Path $installedRoot 'settings.local.json') -Value $localSettingsJson -Encoding UTF8

    if (Test-Path -LiteralPath $runtimeUserDataRoot) {
        New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
        Copy-Item -LiteralPath $runtimeUserDataRoot -Destination (Join-Path $backupRoot 'user-data') -Recurse -Force
        Remove-Item -LiteralPath $runtimeUserDataRoot -Recurse -Force
    }

    if (Test-Path -LiteralPath $runtimeSharedDataRoot) {
        New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
        Copy-Item -LiteralPath $runtimeSharedDataRoot -Destination (Join-Path $backupRoot 'shared-data') -Recurse -Force
        Remove-Item -LiteralPath $runtimeSharedDataRoot -Recurse -Force
    }

    Remove-CredentialTargets

    $port = Get-FreeTcpPort
    $baseUrl = "http://127.0.0.1:$port/scarlethorizons/"
    $hostedSettingsSigningKey = [System.Security.Cryptography.RSA]::Create(2048)
    $updateSigningKey = [System.Security.Cryptography.RSA]::Create(2048)
    try {
        $hostedSettingsJson = New-SignedHostedSettingsJson `
            -Settings @{
                'RPOL user name' = 'fixture-user'
                'RPOL password' = 'fixture-password'
                'XP Tracking' = 'https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking'
            } `
            -Version '1.0.0' `
            -SigningKey $hostedSettingsSigningKey

        $fixtureContentRoot = Join-Path $fixtureRoot 'scarlethorizons'
        New-Item -ItemType Directory -Force -Path $fixtureContentRoot | Out-Null
        [System.IO.File]::WriteAllText(
            (Join-Path $fixtureRoot ($HostedSettingsRelativePath -replace '/', '\')),
            $hostedSettingsJson,
            [System.Text.UTF8Encoding]::new($false))

        Copy-Item -LiteralPath $resolvedPackagePath -Destination (Join-Path $fixtureContentRoot $UpdateArchiveFileName) -Force
        Copy-Item -LiteralPath (Join-Path $installedRoot 'player-assistant.exe') -Destination (Join-Path $fixtureContentRoot $UpdateInstallerFileName) -Force
        $signedManifest = New-SignedUpdateManifest `
            -ArchiveSha256 (Get-FileSha256 (Join-Path $fixtureContentRoot $UpdateArchiveFileName)) `
            -InstallerSha256 (Get-FileSha256 (Join-Path $fixtureContentRoot $UpdateInstallerFileName)) `
            -SigningKey $updateSigningKey
        [System.IO.File]::WriteAllText(
            (Join-Path $fixtureRoot ($UpdateManifestRelativePath -replace '/', '\')),
            $signedManifest.ManifestJson,
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText(
            (Join-Path $fixtureRoot ($UpdateSignatureRelativePath -replace '/', '\')),
            $signedManifest.SignatureText,
            [System.Text.UTF8Encoding]::new($false))

        $serverProcess = Start-FixtureServer -RootDirectory $fixtureRoot -Port $port -LogPath $logPath
        Start-Sleep -Milliseconds 500

        $environmentVariables = @{
            $HostedSettingsOverrideEnvironmentVariable = "$baseUrl" + 'settings.local.json'
            $HostedSettingsPublicKeyEnvironmentVariable = $hostedSettingsSigningKey.ExportSubjectPublicKeyInfoPem()
            $UpdateBaseUrlOverrideEnvironmentVariable = $baseUrl
            $UpdatePublicKeyEnvironmentVariable = $updateSigningKey.ExportSubjectPublicKeyInfoPem()
        }

        $healthResult = Invoke-AppCommand `
            -ExecutablePath (Join-Path $installedRoot 'player-assistant.exe') `
            -Arguments @('--health') `
            -WorkingDirectory $installedRoot `
            -EnvironmentVariables $environmentVariables `
            -TimeoutSeconds $TimeoutSeconds
        if ($healthResult.ExitCode -ne 0) {
            throw "Installed app health command failed. Output: $($healthResult.Output)"
        }

        if ($healthResult.Output -notmatch '(?m)^status:\s+(ok|warning)\b') {
            throw "Installed app health output did not report ok/warning. Output: $($healthResult.Output)"
        }

        $requestLog = @()
        if (Test-Path -LiteralPath $logPath) {
            $requestLog = Get-Content -LiteralPath $logPath
        }
        if ((@($requestLog | Where-Object { $_ -eq $HostedSettingsRelativePath }).Count) -lt 1) {
            throw "Installed app did not fetch hosted settings from the fixture server."
        }

        $storedCredentialRecordJson = Read-CredentialSecret -Target 'PlayerAssistant/RPOL/Credentials'
        try {
            $storedCredentialRecord = $storedCredentialRecordJson | ConvertFrom-Json
        }
        catch {
            throw "Hosted settings credential migration did not store a valid versioned RPOL credential record."
        }
        if ($storedCredentialRecord.version -ne 1 -or
            $storedCredentialRecord.user_name -ne 'fixture-user' -or
            $storedCredentialRecord.password -ne 'fixture-password') {
            throw "Hosted settings credential migration did not store the expected versioned RPOL credentials."
        }
        if ($null -ne (Read-CredentialSecret -Target 'PlayerAssistant/RPOL/UserName') -or
            $null -ne (Read-CredentialSecret -Target 'PlayerAssistant/RPOL/Password')) {
            throw 'Hosted settings credential migration retained legacy split RPOL credentials.'
        }

        $updatePreflightResult = Invoke-AppCommand `
            -ExecutablePath (Join-Path $installedRoot 'player-assistant.exe') `
            -Arguments @('--update-preflight') `
            -WorkingDirectory $installedRoot `
            -EnvironmentVariables $environmentVariables `
            -TimeoutSeconds $TimeoutSeconds
        if ($updatePreflightResult.ExitCode -ne 0) {
            throw "Installed app update preflight failed. Output: $($updatePreflightResult.Output)"
        }

        if ($updatePreflightResult.Output -notmatch '(?m)^status:\s+(current|update-available|no-update)\b') {
            throw "Installed app update preflight did not report updater status. Output: $($updatePreflightResult.Output)"
        }

        $requestLog = @()
        if (Test-Path -LiteralPath $logPath) {
            $requestLog = Get-Content -LiteralPath $logPath
        }
        if ((@($requestLog | Where-Object { $_ -eq $UpdateManifestRelativePath }).Count) -lt 1) {
            throw "Installed app did not fetch the signed update manifest from the fixture server."
        }

        if ((@($requestLog | Where-Object { $_ -eq $UpdateSignatureRelativePath }).Count) -lt 1) {
            throw "Installed app did not fetch the signed update manifest signature from the fixture server."
        }

        Write-Output "Installer clean-machine smoke verification passed."
        Write-Output "  Package: $resolvedPackagePath"
        Write-Output "  InstalledDir: $installedRoot"
        Write-Output "  Hosted settings requests: $(@($requestLog | Where-Object { $_ -eq $HostedSettingsRelativePath }).Count)"
        Write-Output "  Update manifest requests: $(@($requestLog | Where-Object { $_ -eq $UpdateManifestRelativePath }).Count)"
        Write-Output "  Update signature requests: $(@($requestLog | Where-Object { $_ -eq $UpdateSignatureRelativePath }).Count)"
        $verificationSucceeded = $true
    }
    finally {
        $hostedSettingsSigningKey.Dispose()
        $updateSigningKey.Dispose()
    }
}
finally {
    if ($serverProcess -and -not $serverProcess.HasExited) {
        try {
            $serverProcess.Kill()
            $serverProcess.WaitForExit()
        }
        catch {
        }
    }

    Remove-CredentialTargets

    if (Test-Path -LiteralPath $runtimeUserDataRoot) {
        Remove-Item -LiteralPath $runtimeUserDataRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $runtimeSharedDataRoot) {
        Remove-Item -LiteralPath $runtimeSharedDataRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath (Join-Path $backupRoot 'user-data')) {
        Copy-Item -LiteralPath (Join-Path $backupRoot 'user-data') -Destination $runtimeUserDataRoot -Recurse -Force
    }

    if (Test-Path -LiteralPath (Join-Path $backupRoot 'shared-data')) {
        Copy-Item -LiteralPath (Join-Path $backupRoot 'shared-data') -Destination $runtimeSharedDataRoot -Recurse -Force
    }

    if (Test-Path -LiteralPath $scratchRoot) {
        Remove-Item -LiteralPath $scratchRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($verificationSucceeded) {
        $global:LASTEXITCODE = 0
    }
}
