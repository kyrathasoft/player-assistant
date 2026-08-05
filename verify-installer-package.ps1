param(
    [string]$PackagePath,
    [string]$ExpectedVersion,
    [string]$ExpectedSignerSubject = $env:PLAYER_ASSISTANT_RELEASE_SIGNER_SUBJECT,
    [string]$ExpectedSignerThumbprint = $env:PLAYER_ASSISTANT_RELEASE_SIGNER_THUMBPRINT,
    [switch]$RequireCodeSigning
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'version-metadata.ps1')
$versionMetadata = Get-PlayerAssistantVersionMetadata -RepoRoot $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    $ExpectedVersion = $versionMetadata.Version
}
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Join-Path $PSScriptRoot "Release\installer\player-assistant-$ExpectedVersion-installer.zip"
}

$RuntimeSidecarVerificationScriptPath = Join-Path $PSScriptRoot 'verify-runtime-sidecars.ps1'

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

function Assert-RequiredDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (!(Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Required $Description is missing: $Path"
    }
}

function Assert-EncryptedEnvelope {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Assert-RequiredFile -Path $Path -Description $Description
    $raw = Get-Content -Raw -LiteralPath $Path
    $json = $raw | ConvertFrom-Json

    if ($json.schema_version -ne 1) {
        throw "$Description must declare schema_version 1."
    }

    if ($json.format -ne 'app-protected-v2') {
        throw "$Description must use portable encrypted format app-protected-v2 for installer payloads."
    }

    if ([string]::IsNullOrWhiteSpace([string]$json.payload)) {
        throw "$Description has an empty encrypted payload."
    }

    foreach ($marker in @('"RPOL password"', '"RPOL user name"', '"Dungeon Master"', '"Kelpie"', 'Lucian99!', 'gemstone')) {
        if ($raw.Contains($marker)) {
            throw "$Description contains plaintext sensitive marker '$marker'."
        }
    }
}

function Test-CodeSigningPolicyConfigured {
    return $RequireCodeSigning -or
        ![string]::IsNullOrWhiteSpace($ExpectedSignerSubject) -or
        ![string]::IsNullOrWhiteSpace($ExpectedSignerThumbprint)
}

function Assert-AuthenticodeSignatureMatchesPolicy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (!(Test-CodeSigningPolicyConfigured)) {
        return
    }

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
}

function Test-InstallerDirectory {
    param([Parameter(Mandatory = $true)][string]$Directory)

    Assert-RequiredFile -Path (Join-Path $Directory 'install-player-assistant.ps1') -Description 'installer script'
    Assert-RequiredFile -Path (Join-Path $Directory 'install-player-assistant.cmd') -Description 'installer launcher'
    Assert-RequiredFile -Path $RuntimeSidecarVerificationScriptPath -Description 'runtime sidecar verification script'

    $installerScript = Get-Content -Raw -LiteralPath (Join-Path $Directory 'install-player-assistant.ps1')
    if (!$installerScript.Contains("kyrathasoft\player-assistant")) {
        throw "Installer script does not target Program Files\kyrathasoft\player-assistant."
    }

    $payloadDirectory = Join-Path $Directory 'payload'
    Assert-RequiredDirectory -Path $payloadDirectory -Description 'installer payload directory'

    $requiredPayloadFiles = @(
        'player-assistant.exe',
        'settings.json',
        'magic-items.json',
        'settings.local.json',
        'xp-passwords.json',
        'keyword-index.json',
        'game-posts-key-terms.md',
        'sitemap.xml',
        'sitemap-keyword-urls.json',
        'release-manifest.json',
        'release-runtime-inventory.json',
        'release-provenance.json'
    )

    foreach ($relativePath in $requiredPayloadFiles) {
        Assert-RequiredFile -Path (Join-Path $payloadDirectory $relativePath) -Description "payload $relativePath"
    }

    Assert-RequiredFile -Path (Join-Path $payloadDirectory '.playwright\node\win32_x64\node.exe') -Description 'payload Playwright node.exe'
    Assert-RequiredFile -Path (Join-Path $payloadDirectory '.playwright\package\package.json') -Description 'payload Playwright package.json'
    Assert-RequiredFile -Path (Join-Path $payloadDirectory '.playwright\package\browsers.json') -Description 'payload Playwright browsers.json'

    Assert-EncryptedEnvelope -Path (Join-Path $payloadDirectory 'settings.local.json') -Description 'payload settings.local.json'
    & powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $RuntimeSidecarVerificationScriptPath `
        -AppDir $payloadDirectory `
        -RequireInstallerScriptProtection `
        -InstallerScriptPath (Join-Path $Directory 'install-player-assistant.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "Installer package runtime sidecar verification failed."
    }

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payloadDirectory 'player-assistant.exe'))
    if ($versionInfo.ProductVersion -ne $ExpectedVersion) {
        throw "Payload executable product version '$($versionInfo.ProductVersion)' did not match expected version $ExpectedVersion."
    }

    Assert-AuthenticodeSignatureMatchesPolicy -Path (Join-Path $payloadDirectory 'player-assistant.exe') -Description 'Payload executable'

    foreach ($forbiddenName in @('startup-errors.log', 'startup-health.json', 'last-crash.json', 'startup-remediation.txt')) {
        $matches = Get-ChildItem -LiteralPath $payloadDirectory -Recurse -Force -File -Filter $forbiddenName
        if ($matches) {
            throw "Installer payload contains forbidden runtime diagnostic artifact '$forbiddenName'."
        }
    }
}

$resolvedPackagePath = [System.IO.Path]::GetFullPath($PackagePath)
Assert-RequiredFile -Path $resolvedPackagePath -Description 'installer package'

$scratchDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("player-assistant-installer-verify-{0}" -f ([Guid]::NewGuid().ToString('N')))
try {
    New-Item -ItemType Directory -Force -Path $scratchDirectory | Out-Null
    Expand-Archive -LiteralPath $resolvedPackagePath -DestinationPath $scratchDirectory -Force

    $roots = @(Get-ChildItem -LiteralPath $scratchDirectory -Directory)
    if ($roots.Count -ne 1) {
        throw "Installer package should contain one root directory, found $($roots.Count)."
    }

    Test-InstallerDirectory -Directory $roots[0].FullName
    Write-Output "Installer package verification passed: $resolvedPackagePath"
}
finally {
    if (Test-Path -LiteralPath $scratchDirectory) {
        for ($attempt = 1; $attempt -le 5; $attempt++) {
            try {
                Remove-Item -LiteralPath $scratchDirectory -Recurse -Force
                break
            }
            catch {
                if ($attempt -eq 5) {
                    Write-Warning "Unable to remove temporary verification directory: $scratchDirectory"
                    break
                }

                Start-Sleep -Milliseconds (250 * $attempt)
            }
        }
    }
}
