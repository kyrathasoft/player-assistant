param(
    [string] $PhpPath,
    [string] $PhpIniPath,
    [string] $CaBundlePath,
    [string] $CaBundleUrl = 'https://curl.se/ca/cacert.pem',
    [string[]] $AdditionalWindowsRootThumbprint = @(
        'D20D34289874256AAA333843BCCC2745F015E214'
    ),
    [string[]] $ValidationUrl = @(
        'https://example.com',
        'https://rpol.net',
        'https://publish.obsidian.md'
    ),
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($PhpPath)) {
    $phpFromPath = Get-Command php -ErrorAction SilentlyContinue
    if ($null -eq $phpFromPath) {
        throw 'Unable to locate php.exe. Pass -PhpPath explicitly.'
    }
    $PhpPath = $phpFromPath.Source
}

function Resolve-PhpIniPath {
    param(
        [string] $Executable,
        [string] $ExplicitIni,
        [string] $DefaultRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitIni)) {
        return (Resolve-Path -LiteralPath $ExplicitIni).Path
    }

    $iniOutput = & $Executable --ini
    $loadedIni = (
        $iniOutput |
        Select-String -Pattern 'Loaded Configuration File'
    ) |
    ForEach-Object { $_.Line } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object {
        if ($_ -match 'Loaded Configuration File.*=>\s*(.+)$') { $Matches[1].Trim() } else { '' }
    } |
    Select-Object -First 1

    if (-not [string]::IsNullOrWhiteSpace($loadedIni) -and $loadedIni -ne '(none)') {
        return $loadedIni
    }

    $phpIni = Join-Path $DefaultRoot 'php.ini'
    if (-not (Test-Path -LiteralPath $phpIni)) {
        $devIni = Join-Path $DefaultRoot 'php.ini-development'
        $prodIni = Join-Path $DefaultRoot 'php.ini-production'
        $template = $null
        if (Test-Path -LiteralPath $devIni) {
            $template = $devIni
        } elseif (Test-Path -LiteralPath $prodIni) {
            $template = $prodIni
        } else {
            throw 'No php.ini exists and no php.ini-development/php.ini-production template was found.'
        }
        Copy-Item -LiteralPath $template -Destination $phpIni -Force
    }
    return $phpIni
}

function Add-WindowsRootCertificateToBundle {
    param(
        [string] $BundlePath,
        [string] $Thumbprint
    )

    $normalizedThumbprint = ($Thumbprint -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
    if ($normalizedThumbprint.Length -ne 40) {
        throw "Invalid Windows root certificate thumbprint '$Thumbprint'."
    }

    $beginMarker = "## BEGIN WINDOWS ROOT CERTIFICATE $normalizedThumbprint"
    $bundleText = Get-Content -LiteralPath $BundlePath -Raw -Encoding ASCII
    if ($bundleText.Contains($beginMarker)) {
        Write-Output "Windows root certificate already present: $normalizedThumbprint"
        return
    }

    $certificate = $null
    foreach ($store in @('CurrentUser', 'LocalMachine')) {
        $certificatePath = "Cert:\$store\Root\$normalizedThumbprint"
        if (Test-Path -LiteralPath $certificatePath) {
            $certificate = Get-Item -LiteralPath $certificatePath
            break
        }
    }

    if ($null -eq $certificate) {
        throw "Trusted Windows root certificate '$normalizedThumbprint' was not found."
    }
    if ($certificate.Subject -ne $certificate.Issuer) {
        throw "Certificate '$normalizedThumbprint' is not a self-signed root certificate."
    }

    $encodedCertificate = [Convert]::ToBase64String($certificate.RawData)
    $pemLines = New-Object System.Collections.Generic.List[string]
    for ($offset = 0; $offset -lt $encodedCertificate.Length; $offset += 64) {
        $lineLength = [Math]::Min(64, $encodedCertificate.Length - $offset)
        $pemLines.Add($encodedCertificate.Substring($offset, $lineLength))
    }

    $certificateBlock = @(
        ''
        $beginMarker
        "## Subject: $($certificate.Subject)"
        '-----BEGIN CERTIFICATE-----'
    ) + $pemLines + @(
        '-----END CERTIFICATE-----'
        "## END WINDOWS ROOT CERTIFICATE $normalizedThumbprint"
    )

    Add-Content -LiteralPath $BundlePath -Value $certificateBlock -Encoding ASCII
    Write-Output "Added trusted Windows root certificate: $normalizedThumbprint"
}

$resolvedPhp = Resolve-Path -LiteralPath $PhpPath
$phpItem = Get-Item -LiteralPath $resolvedPhp.Path
if ($phpItem.PSIsContainer) {
    $phpExe = Join-Path $phpItem.FullName 'php.exe'
} else {
    $phpExe = $phpItem.FullName
}

if (-not (Test-Path -LiteralPath $phpExe)) {
    throw "php executable not found at '$phpExe'."
}

$phpRoot = Split-Path -Parent $phpExe
$phpIni = Resolve-PhpIniPath -Executable $phpExe -ExplicitIni $PhpIniPath -DefaultRoot $phpRoot

if ([string]::IsNullOrWhiteSpace($CaBundlePath)) {
    $caDir = Join-Path $phpRoot 'extras\\ssl'
    if (-not (Test-Path -LiteralPath $caDir)) {
        New-Item -ItemType Directory -Path $caDir -Force | Out-Null
    }
    $CaBundlePath = Join-Path $caDir 'cacert.pem'
}

$resolvedCaBundle = Resolve-Path -LiteralPath $CaBundlePath -ErrorAction SilentlyContinue
if ($resolvedCaBundle -and -not $Force) {
    $CaBundlePath = $resolvedCaBundle.Path
} elseif (-not $resolvedCaBundle -or $Force) {
    Write-Output "Downloading CA bundle to '$CaBundlePath'..."
    Invoke-WebRequest -Uri $CaBundleUrl -OutFile $CaBundlePath
}

if (-not (Test-Path -LiteralPath $CaBundlePath)) {
    throw "CA bundle not found at '$CaBundlePath'."
}

foreach ($thumbprint in $AdditionalWindowsRootThumbprint) {
    Add-WindowsRootCertificateToBundle -BundlePath $CaBundlePath -Thumbprint $thumbprint
}

$lines = Get-Content -LiteralPath $phpIni -Encoding UTF8
$updatedLines = New-Object System.Collections.Generic.List[string]
$caSet = $false
$sslSet = $false
$opensslExtensionSet = $false

foreach ($line in $lines) {
    if ($line -match '^\s*;?\s*curl\.cainfo\s*=') {
        $updatedLines.Add("curl.cainfo = `"$CaBundlePath`"")
        $caSet = $true
        continue
    }
    if ($line -match '^\s*;?\s*openssl\.cafile\s*=') {
        $updatedLines.Add("openssl.cafile = `"$CaBundlePath`"")
        $sslSet = $true
        continue
    }
    if ($line -match '^\s*;?\s*extension\s*=\s*(?:php_)?openssl(?:\.dll)?\s*$') {
        $updatedLines.Add('extension=openssl')
        $opensslExtensionSet = $true
        continue
    }
    $updatedLines.Add($line)
}

if (-not $caSet) {
    $updatedLines.Add("curl.cainfo = `"$CaBundlePath`"")
}
if (-not $sslSet) {
    $updatedLines.Add("openssl.cafile = `"$CaBundlePath`"")
}
if (-not $opensslExtensionSet) {
    $opensslExtensionPath = Join-Path $phpRoot 'ext\php_openssl.dll'
    if (Test-Path -LiteralPath $opensslExtensionPath) {
        $updatedLines.Add('extension=openssl')
    }
}

Set-Content -LiteralPath $phpIni -Value $updatedLines -Encoding UTF8

Write-Output "Configured CA bundle for '$phpIni':"
Write-Output "  curl.cainfo = $CaBundlePath"
Write-Output "  openssl.cafile = $CaBundlePath"

$curlStatus = & $phpExe -c $phpIni -r 'echo "curl_version="; echo (function_exists("curl_version") ? "enabled" : "disabled");'
$opensslStatus = & $phpExe -c $phpIni -r 'echo "openssl_extension="; echo (extension_loaded("openssl") ? "enabled" : "disabled");'

Write-Output "CA_BUNDLE=$CaBundlePath"
Write-Output $curlStatus
Write-Output $opensslStatus

foreach ($url in $ValidationUrl) {
    $curlHttpsStatus = & $phpExe -c $phpIni -r '$url = $argv[1]; $ch = curl_init($url); if ($ch === false) { echo "curl_init_failed"; exit(1); } curl_setopt_array($ch, [CURLOPT_RETURNTRANSFER => true, CURLOPT_NOBODY => true, CURLOPT_CONNECTTIMEOUT => 10, CURLOPT_TIMEOUT => 20]); $ok = curl_exec($ch); $err = curl_error($ch); $code = curl_getinfo($ch, CURLINFO_RESPONSE_CODE); curl_close($ch); if ($ok === false) { echo "curl_https_failed:".$err; exit(2); } echo "curl_https_ok:".$code;' -- $url
    if ($LASTEXITCODE -ne 0) {
        throw "PHP cURL HTTPS validation failed for '$url': $curlHttpsStatus"
    }
    Write-Output "$url $curlHttpsStatus"

    $opensslHttpsStatus = & $phpExe -c $phpIni -r '$url = $argv[1]; set_error_handler(static function (int $severity, string $message): never { throw new ErrorException($message, 0, $severity); }); try { $context = stream_context_create(["http" => ["method" => "HEAD", "timeout" => 20, "ignore_errors" => true], "ssl" => ["verify_peer" => true, "verify_peer_name" => true]]); $stream = fopen($url, "rb", false, $context); if ($stream === false) { echo "openssl_https_failed"; exit(2); } fclose($stream); echo "openssl_https_ok"; } catch (Throwable $error) { echo "openssl_https_failed:".$error->getMessage(); exit(2); }' -- $url
    if ($LASTEXITCODE -ne 0) {
        throw "PHP OpenSSL HTTPS validation failed for '$url': $opensslHttpsStatus"
    }
    Write-Output "$url $opensslHttpsStatus"
}
