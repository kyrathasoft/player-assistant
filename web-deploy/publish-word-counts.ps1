param(
    [Parameter(Mandatory = $true)][ValidateRange(1, 1000000000)][int]$WikiPages,
    [Parameter(Mandatory = $true)][ValidateRange(0, 1000000000)][long]$WikiWords,
    [Parameter(Mandatory = $true)][ValidateRange(1, 1000000000)][int]$IcFiles,
    [Parameter(Mandatory = $true)][ValidateRange(0, 1000000000)][long]$IcWords,
    [Parameter(Mandatory = $true)][ValidateRange(1, 1000000000)][int]$OocFiles,
    [Parameter(Mandatory = $true)][ValidateRange(0, 1000000000)][long]$OocWords,
    [DateTimeOffset]$ObservedAt = [DateTimeOffset]::UtcNow,
    [ValidateLength(1, 100)][string]$CountingRuleVersion = 'obsidian-publish-word-count-v1',
    [uri]$ApiUrl = 'https://bryanmiller.us/scarlethorizons/api/v1/admin/word-counts',
    [uri]$SourceUrl = 'https://bryanmiller.us/scarlethorizons/data/word-counts.json',
    [string]$SourceSshTarget = 'dh_4gg2za@pdx1-shared-a1-13.dreamhost.com',
    [string]$SourceSshKeyPath = (Join-Path $env:USERPROFILE '.ssh\dreamhost_player_assistant'),
    [ValidatePattern('^/home/dh_4gg2za/bryanmiller\.us/scarlethorizons/data/word-counts\.json$')]
    [string]$SourceRemotePath = '/home/dh_4gg2za/bryanmiller.us/scarlethorizons/data/word-counts.json',
    [string]$PhpPath = 'C:\php-8.4.23-Win32-vs17-x64\php.exe',
    [string]$SigningCredentialTarget = 'PlayerAssistant/WordCounts/SigningPrivateKey',
    [string]$SigningMetadataPath = (Join-Path $PSScriptRoot 'word-count-signing-public.json'),
    [string]$AdminCredentialTarget = 'PlayerAssistant/Broker/AdminKey',
    [switch]$SkipSourceUpload,
    [Security.SecureString]$AdminKey
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'word-count-publishing.ps1')
$nativeSshPath = Join-Path $env:WINDIR 'System32\OpenSSH\ssh.exe'
$nativeScpPath = Join-Path $env:WINDIR 'System32\OpenSSH\scp.exe'
$sshCommand = if (Test-Path -LiteralPath $nativeSshPath -PathType Leaf) {
    $nativeSshPath
} else {
    (Get-Command 'ssh' -ErrorAction SilentlyContinue).Source
}
$scpCommand = if (Test-Path -LiteralPath $nativeScpPath -PathType Leaf) {
    $nativeScpPath
} else {
    (Get-Command 'scp' -ErrorAction SilentlyContinue).Source
}

if ($ApiUrl.Scheme -ne [Uri]::UriSchemeHttps -or
    !$ApiUrl.IsDefaultPort -or
    ![string]::IsNullOrEmpty($ApiUrl.UserInfo)) {
    throw 'The word-count API URL must be a default-port HTTPS URL without embedded credentials.'
}
if ($SourceUrl.Scheme -ne [Uri]::UriSchemeHttps -or
    !$SourceUrl.IsDefaultPort -or
    ![string]::IsNullOrEmpty($SourceUrl.UserInfo)) {
    throw 'The word-count source URL must be a default-port HTTPS URL without embedded credentials.'
}
if (!$SkipSourceUpload) {
    if (-not (Test-Path -LiteralPath $SourceSshKeyPath -PathType Leaf)) {
        throw "The DreamHost SSH key was not found at '$SourceSshKeyPath'."
    }
    if ([string]::IsNullOrWhiteSpace($sshCommand) -or [string]::IsNullOrWhiteSpace($scpCommand)) {
        throw 'Required OpenSSH commands were not found.'
    }
    if (-not (Test-Path -LiteralPath $SigningMetadataPath -PathType Leaf)) {
        throw "The word-count signing metadata was not found at '$SigningMetadataPath'."
    }
    if (-not (Test-Path -LiteralPath $PhpPath -PathType Leaf)) {
        throw "The PHP signing runtime was not found at '$PhpPath'."
    }
}

$snapshot = [ordered]@{
    schema_version = 1
    observed_at = $ObservedAt.UtcDateTime.ToString(
        'yyyy-MM-ddTHH:mm:ss.fffZ',
        [Globalization.CultureInfo]::InvariantCulture)
    counting_rule_version = $CountingRuleVersion
    wiki = [ordered]@{ pages = $WikiPages; words = $WikiWords }
    ic = [ordered]@{ files = $IcFiles; words = $IcWords }
    ooc = [ordered]@{ files = $OocFiles; words = $OocWords }
}

function New-BrokerAdminHeaders {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Route,
        [Parameter(Mandatory = $true)][string]$BodyJson,
        [Parameter(Mandatory = $true)][string]$Key
    )
    $timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
    $nonce = [Guid]::NewGuid().ToString('N')
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bodyHashBytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($BodyJson))
    }
    finally {
        $sha.Dispose()
    }
    $bodyHash = [BitConverter]::ToString($bodyHashBytes).Replace('-', '').ToLowerInvariant()
    $canonical = "$timestamp`n$nonce`n$($Method.ToUpperInvariant())`n$Route`n$bodyHash"
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Key))
    try {
        $signature = [BitConverter]::ToString(
            $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hmac.Dispose()
    }
    return @{
        'X-Broker-Admin-Timestamp' = $timestamp
        'X-Broker-Admin-Nonce' = $nonce
        'X-Broker-Admin-Signature' = $signature
    }
}

$keyPointer = [IntPtr]::Zero
$plainAdminKey = $null
$localSourceTemp = $null
$remoteSourceTemp = $null
$remoteSourceBackup = $null
try {
    if ($null -eq $AdminKey) {
        try {
            $plainAdminKey = Get-WordCountCredentialSecret -TargetName $AdminCredentialTarget
        }
        catch {
            $AdminKey = Read-Host 'Broker administrator key' -AsSecureString
        }
    }
    if ($null -ne $AdminKey) {
        $keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($AdminKey)
        $plainAdminKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPointer)
    }
    if ([string]::IsNullOrWhiteSpace($plainAdminKey)) {
        throw 'The broker administrator key is required.'
    }

    $snapshotJson = $snapshot | ConvertTo-Json -Depth 4 -Compress
    $publishBroker = {
        $brokerResponse = Invoke-RestMethod `
            -Method Put `
            -Uri $ApiUrl `
            -ContentType 'application/json' `
            -Headers (New-BrokerAdminHeaders -Method 'PUT' -Route '/v1/admin/word-counts' -BodyJson $snapshotJson -Key $plainAdminKey) `
            -MaximumRedirection 0 `
            -TimeoutSec 30 `
            -Body $snapshotJson
        if ([int]$brokerResponse.schema_version -ne 1 -or
            [DateTimeOffset]$brokerResponse.observed_at -ne [DateTimeOffset]$snapshot.observed_at -or
            [int]$brokerResponse.wiki.pages -ne $WikiPages -or
            [long]$brokerResponse.wiki.words -ne $WikiWords -or
            [int]$brokerResponse.ic.files -ne $IcFiles -or
            [long]$brokerResponse.ic.words -ne $IcWords -or
            [int]$brokerResponse.ooc.files -ne $OocFiles -or
            [long]$brokerResponse.ooc.words -ne $OocWords) {
            throw 'The broker did not return the exact uploaded word-count snapshot.'
        }
        return $brokerResponse
    }

    $sourcePublished = $false
    if ($SkipSourceUpload) {
        $response = & $publishBroker
    }
    else {
        $metadata = Get-Content -Raw -LiteralPath $SigningMetadataPath | ConvertFrom-Json
        if ([string]$metadata.algorithm -ne 'Ed25519') {
            throw 'The word-count signing metadata algorithm is invalid.'
        }
        $privateKey = Get-WordCountCredentialSecret -TargetName $SigningCredentialTarget
        try {
            $sourceDocumentJson = New-WordCountSignedEnvelope `
                -SnapshotJson $snapshotJson `
                -PrivateKeyBase64 $privateKey `
                -PublicKeyBase64 ([string]$metadata.public_key) `
                -KeyId ([string]$metadata.key_id) `
                -PhpPath $PhpPath
        }
        finally {
            $privateKey = $null
        }

        $localSourceTemp = [IO.Path]::GetTempFileName()
        [IO.File]::WriteAllText(
            $localSourceTemp,
            $sourceDocumentJson,
            [Text.UTF8Encoding]::new($false))
        $transactionId = [Guid]::NewGuid().ToString('N')
        $remoteSourceTemp = "$SourceRemotePath.tmp-$transactionId"
        $remoteSourceBackup = "$SourceRemotePath.rollback-$transactionId"

        $stageSource = {
            & $scpCommand -i $SourceSshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 -- `
                $localSourceTemp "${SourceSshTarget}:$remoteSourceTemp" | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw 'The canonical word-count source could not be staged on DreamHost.'
            }
        }
        $publishSource = {
            $command = "if [ -f '$SourceRemotePath' ]; then cp '$SourceRemotePath' '$remoteSourceBackup'; else rm -f -- '$remoteSourceBackup'; fi && chmod 0644 '$remoteSourceTemp' && mv -f '$remoteSourceTemp' '$SourceRemotePath'"
            & $sshCommand -i $SourceSshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
                $SourceSshTarget $command | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw 'The canonical word-count source could not be published atomically.'
            }
        }
        $verifySource = {
            $verificationUri = [UriBuilder]::new($SourceUrl)
            $verificationUri.Query = "v=$transactionId"
            $sourceHttpResponse = Invoke-WebRequest -Method Get -Uri $verificationUri.Uri `
                -Headers @{ 'Cache-Control' = 'no-cache' } -MaximumRedirection 0 -TimeoutSec 30
            $sourcePayload = Test-WordCountSignedEnvelope `
                -EnvelopeJson ([string]$sourceHttpResponse.Content) `
                -PublicKeyBase64 ([string]$metadata.public_key) `
                -KeyId ([string]$metadata.key_id) `
                -PhpPath $PhpPath
            if ([DateTimeOffset]$sourcePayload.observed_at -ne [DateTimeOffset]$snapshot.observed_at -or
                [long]$sourcePayload.wiki.words -ne $WikiWords -or
                [long]$sourcePayload.ic.words -ne $IcWords -or
                [long]$sourcePayload.ooc.words -ne $OocWords) {
                throw 'The canonical word-count source did not return the exact published snapshot.'
            }
        }
        $rollbackSource = {
            $command = "if [ -f '$remoteSourceBackup' ]; then mv -f '$remoteSourceBackup' '$SourceRemotePath'; else rm -f -- '$SourceRemotePath'; fi; rm -f -- '$remoteSourceTemp'"
            & $sshCommand -i $SourceSshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
                $SourceSshTarget $command | Out-Null
        }
        $cleanupSource = {
            & $sshCommand -i $SourceSshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
                $SourceSshTarget "rm -f -- '$remoteSourceTemp' '$remoteSourceBackup'" | Out-Null
        }
        $response = Invoke-WordCountPublishTransaction `
            -StageSource $stageSource `
            -PublishSource $publishSource `
            -VerifySource $verifySource `
            -PublishBroker $publishBroker `
            -RollbackSource $rollbackSource `
            -CleanupSource $cleanupSource
        $sourcePublished = $true
    }

    [pscustomobject]@{
        Published       = $true
        SourcePublished = $sourcePublished
        ObservedAt      = ([DateTimeOffset]$response.observed_at).ToString('o')
        WikiPages       = [int]$response.wiki.pages
        WikiWords       = [long]$response.wiki.words
        IcFiles         = [int]$response.ic.files
        IcWords         = [long]$response.ic.words
        OocFiles        = [int]$response.ooc.files
        OocWords        = [long]$response.ooc.words
    }
}
finally {
    if ($null -ne $localSourceTemp -and (Test-Path -LiteralPath $localSourceTemp)) {
        Remove-Item -LiteralPath $localSourceTemp -Force
    }
    if ($keyPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer)
    }
    $plainAdminKey = $null
}
