[CmdletBinding()]
param(
    [string]$DreamHostTarget = 'player-assistant-dreamhost',
    [string]$SshKeyPath = (Join-Path $HOME '.ssh\dreamhost_player_assistant'),
    [string]$PrivateDirectory = '/home/dh_4gg2za/player-assistant-broker',
    [uri]$SourceUrl = 'https://bryanmiller.us/scarlethorizons/data/word-counts.json',
    [uri]$HealthUrl = 'https://bryanmiller.us/scarlethorizons/api/v1/health',
    [string]$SigningMetadataPath = (Join-Path $PSScriptRoot 'word-count-signing-public.json'),
    [string]$PhpPath = 'C:\php-8.4.23-Win32-vs17-x64\php.exe',
    [int]$KeepBackups = 5
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'word-count-publishing.ps1')

if ($DreamHostTarget -notmatch '^(?:[A-Za-z0-9][A-Za-z0-9._-]{0,127}|[A-Za-z0-9._-]+@[A-Za-z0-9][A-Za-z0-9.-]{0,126})$') {
    throw 'The DreamHost target must be a simple SSH host alias/hostname or user@hostname.'
}
if ($PrivateDirectory -notmatch '^/home/[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)+$') {
    throw 'The private directory must be an absolute path containing only safe path characters.'
}

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [ValidateRange(1, 5)][int]$Attempts = 3
    )
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $output = & $Action
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) {
            return $output
        }
        if ($attempt -lt $Attempts) {
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
    throw "Native command failed with exit code $exitCode after $Attempts attempts."
}

function Invoke-RemotePhp {
    param(
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter(Mandatory = $true)][string]$ExpectedOperation,
        [ValidateRange(1, 5)][int]$Attempts = 3
    )
    if ([string]::IsNullOrWhiteSpace($Code)) {
        throw 'Remote PHP code cannot be empty.'
    }
    $body = $Code.TrimStart([char]0xFEFF, [char]0x00A0, [char]0x20, [char]0x09, [char]0x0D, [char]0x0A)
    if ($body -match '^<\?php\b') {
        throw 'Remote PHP code must be supplied without an opening PHP tag.'
    }
    if ($body -match '(?i)__[A-Z0-9_]+__|\b(?:placeholder|todo)\b') {
        throw 'Remote PHP code contains an unresolved placeholder.'
    }
    $scriptId = [Guid]::NewGuid().ToString('N')
    $localScript = Join-Path ([IO.Path]::GetTempPath()) "player-assistant-verify-$scriptId.php"
    $remoteScript = "$PrivateDirectory/.player-assistant-verify-$scriptId.php"
    $payload = "<?php`n" + $Code
    if ($payload -notmatch '^<\?php\b') {
        throw 'Remote PHP payload did not receive an opening PHP tag.'
    }
    [IO.File]::WriteAllText($localScript, $payload, [Text.UTF8Encoding]::new($false))
    try {
        Invoke-CheckedNative -Attempts $Attempts {
            & scp -q -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
                -- $localScript "${DreamHostTarget}:$remoteScript"
        } | Out-Null
        $rawOutput = Invoke-CheckedNative -Attempts $Attempts {
            & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
                $DreamHostTarget "/usr/bin/php '$remoteScript'"
        }
        $jsonText = ($rawOutput | ForEach-Object { [string]$_ }) -join "`n"
        try {
            $response = $jsonText | ConvertFrom-Json
        }
        catch {
            throw "Remote PHP operation '$ExpectedOperation' did not return valid JSON."
        }
        if ($null -eq $response -or $response.ok -isnot [bool] -or -not $response.ok) {
            throw "Remote PHP operation '$ExpectedOperation' did not report semantic success."
        }
        if ([string]$response.operation -cne $ExpectedOperation) {
            throw "Remote PHP operation returned unexpected operation '$($response.operation)'."
        }
        return $response
    }
    finally {
        Remove-Item -LiteralPath $localScript -Force -ErrorAction SilentlyContinue
        try {
            & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
                $DreamHostTarget "rm -f -- '$remoteScript'" | Out-Null
        }
        catch {
            Write-Warning "The temporary remote PHP script could not be removed: $remoteScript"
        }
    }
}

$metadata = Get-Content -Raw -LiteralPath $SigningMetadataPath | ConvertFrom-Json
if (-not (Test-Path -LiteralPath $PhpPath -PathType Leaf)) {
    throw "PHP signing runtime not found: $PhpPath"
}
$deployFiles = @('BrokerService.php', 'BrokerAlertService.php', 'StructuredCorrelation.php', 'BrokerOperations.php', 'DatabaseMigrationService.php', 'IdempotencyLedger.php', 'QuestService.php', 'WordCountService.php', 'refresh-word-counts.php', 'broker-maintenance.php')
$localHashes = @{}
foreach ($file in $deployFiles) {
    $localHashes[$file] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $PSScriptRoot "player-assistant-broker\$file")).Hash.ToLowerInvariant()
}

$remoteCode = @'
$directory = '__PRIVATE_DIRECTORY__';
$files = ['BrokerService.php', 'BrokerAlertService.php', 'StructuredCorrelation.php', 'BrokerOperations.php', 'DatabaseMigrationService.php', 'IdempotencyLedger.php', 'QuestService.php', 'WordCountService.php', 'refresh-word-counts.php', 'broker-maintenance.php'];
$result = ['files' => [], 'config' => [], 'backups' => [], 'cron' => ''];
foreach ($files as $file) {
    $path = $directory . '/' . $file;
    $result['files'][$file] = [
        'sha256' => is_file($path) ? hash_file('sha256', $path) : null,
        'mode' => is_file($path) ? substr(sprintf('%o', fileperms($path)), -4) : null,
    ];
}
$configPath = $directory . '/config.php';
$config = is_file($configPath) ? require $configPath : [];
$wordCounts = is_array($config['word_counts'] ?? null) ? $config['word_counts'] : [];
$result['config'] = [
    'source_url' => $wordCounts['source_url'] ?? null,
    'status_path' => $wordCounts['status_path'] ?? null,
    'signature_key_id' => $wordCounts['signature_key_id'] ?? null,
    'signature_public_key' => $wordCounts['signature_public_key'] ?? null,
    'mode' => is_file($configPath) ? substr(sprintf('%o', fileperms($configPath)), -4) : null,
];
$operations = is_array($config['operations'] ?? null) ? $config['operations'] : [];
$offsite = is_array($operations['offsite'] ?? null) ? $operations['offsite'] : [];
$offsiteTransport = strtolower((string)($offsite['transport'] ?? 'ssh'));
$ftpsPort = (int)(getenv('BACKUP_FTPS_PORT') ?: ($offsite['port'] ?? 21));
$offsiteConfigured = $offsiteTransport === 'ftps'
    ? trim((string)getenv('BACKUP_FTPS_HOST')) !== ''
        && $ftpsPort > 0
        && $ftpsPort <= 65535
        && trim((string)getenv('BACKUP_FTPS_USERNAME')) !== ''
        && (string)getenv('BACKUP_FTPS_PASSWORD') !== ''
        && trim((string)getenv('BACKUP_FTPS_REMOTE_PATH')) !== ''
        && strlen((string)getenv('BACKUP_ENCRYPTION_KEY')) >= 32
    : !empty($offsite['ssh_target'])
        && !empty($offsite['directory'])
        && strlen((string)getenv('BACKUP_ENCRYPTION_KEY')) >= 32;
$result['operations'] = [
    'backup_directory' => $operations['backup_directory'] ?? null,
    'restore_test_directory' => $operations['restore_test_directory'] ?? null,
    'status_path' => $operations['status_path'] ?? null,
    'offsite_transport' => $offsiteTransport,
    'offsite_configured' => $offsiteConfigured,
    'curl_loaded' => extension_loaded('curl'),
    'openssl_loaded' => extension_loaded('openssl'),
];
$statusPath = $directory . '/word-count-refresh-status.json';
$result['status'] = [
    'exists' => is_file($statusPath),
    'mode' => is_file($statusPath) ? substr(sprintf('%o', fileperms($statusPath)), -4) : null,
];
$patterns = [
    'config.php.bak-deploy-*',
    'config.php.bak-word-count-refresh-*',
    'BrokerService.php.bak-deploy-*',
    'BrokerAlertService.php.bak-deploy-*',
    'DatabaseMigrationService.php.bak-deploy-*',
    'QuestService.php.bak-deploy-*',
    'WordCountService.php.bak-deploy-*',
    'WordCountService.php.bak-source-refresh-*',
    'refresh-word-counts.php.bak-deploy-*',
    'BrokerOperations.php.bak-deploy-*',
    'broker-maintenance.php.bak-deploy-*',
];
foreach ($patterns as $pattern) {
    $matches = glob($directory . '/' . $pattern) ?: [];
    $result['backups'][$pattern] = count($matches);
}
$cron = shell_exec('crontab -l 2>/dev/null');
$result['cron'] = is_string($cron) ? $cron : '';
$result['ok'] = true;
$result['operation'] = 'word-count-verify';
$result['state_mutation'] = false;
echo json_encode($result, JSON_UNESCAPED_SLASHES);
'@.Replace('__PRIVATE_DIRECTORY__', $PrivateDirectory.Replace("'", "\'"))

$remote = (Invoke-RemotePhp $remoteCode -ExpectedOperation 'word-count-verify') | Out-String | ConvertFrom-Json
foreach ($file in $deployFiles) {
    if ($remote.files.$file.sha256 -ne $localHashes[$file]) {
        throw "Production drift detected for $file."
    }
    if ($remote.files.$file.mode -ne '0600') {
        throw "Unexpected production mode for ${file}: $($remote.files.$file.mode)."
    }
}

$expectedStatusPath = "$PrivateDirectory/word-count-refresh-status.json"
if ($remote.config.source_url -ne $SourceUrl.AbsoluteUri) {
    throw 'Production source URL does not match.'
}
if ($remote.config.status_path -ne $expectedStatusPath) {
    throw 'Production refresh status path does not match.'
}
if ($remote.config.signature_key_id -ne $metadata.key_id -or $remote.config.signature_public_key -ne $metadata.public_key) {
    throw 'Production signing metadata does not match.'
}
if ($remote.config.mode -ne '0600' -or -not $remote.status.exists -or $remote.status.mode -ne '0600') {
    throw 'Production config or refresh status permissions are incorrect.'
}
$expectedOperationsDirectory = "$PrivateDirectory/broker-backups"
$expectedRestoreDirectory = "$PrivateDirectory/broker-restore-tests"
$expectedOperationsStatusPath = "$PrivateDirectory/broker-operations-status.json"
if ($remote.operations.backup_directory -ne $expectedOperationsDirectory -or
    $remote.operations.restore_test_directory -ne $expectedRestoreDirectory -or
    $remote.operations.status_path -ne $expectedOperationsStatusPath) {
    throw 'Production broker operations paths do not match.'
}
if ($remote.operations.offsite_transport -eq 'ftps' -and -not $remote.operations.curl_loaded) {
    throw 'Production PHP does not provide the cURL extension required for FTPS backups.'
}
if (-not $remote.operations.openssl_loaded) {
    throw 'Production PHP does not provide the OpenSSL extension required for encrypted backups.'
}

$cronNeedle = "/usr/bin/php $PrivateDirectory/refresh-word-counts.php"
if ($remote.cron -notmatch [regex]::Escape($cronNeedle)) {
    throw 'Production refresh cron entry is missing.'
}
$maintenanceCronNeedle = "/usr/bin/php $PrivateDirectory/broker-maintenance.php"
if ($remote.cron -notmatch [regex]::Escape($maintenanceCronNeedle)) {
    throw 'Production broker maintenance cron entry is missing.'
}
foreach ($property in $remote.backups.PSObject.Properties) {
    if ($property.Name -like 'config.php.bak-*' -and [int]$property.Value -ne 0) {
        throw "Credential-bearing config backup remains for $($property.Name)."
    }
    if ([int]$property.Value -gt $KeepBackups) {
        throw "Backup retention exceeded for $($property.Name)."
    }
}

$sourceResponse = Invoke-WebRequest -Uri $SourceUrl -UseBasicParsing -TimeoutSec 30
$null = Test-WordCountSignedEnvelope -EnvelopeJson $sourceResponse.Content -PublicKeyBase64 $metadata.public_key -KeyId $metadata.key_id -PhpPath $PhpPath

$health = Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 30
if ([int]$health.schema_version -ne 7 -or $health.status -ne 'ok') {
    throw 'The public broker liveness endpoint is unavailable or unhealthy.'
}
if ($null -ne $health.word_count_refresh -or $null -ne $health.operations) {
    throw 'The public broker liveness endpoint disclosed operational details.'
}

Write-Output 'Word-count production drift check passed.'
