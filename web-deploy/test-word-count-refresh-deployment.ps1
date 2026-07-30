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
    param([Parameter(Mandatory = $true)][string]$Code)
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Code))
    $command = "/usr/bin/php -r `"eval(base64_decode('$encoded'));`""
    return Invoke-CheckedNative {
        & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 $DreamHostTarget $command
    }
}

$metadata = Get-Content -Raw -LiteralPath $SigningMetadataPath | ConvertFrom-Json
if (-not (Test-Path -LiteralPath $PhpPath -PathType Leaf)) {
    throw "PHP signing runtime not found: $PhpPath"
}
$deployFiles = @('BrokerService.php', 'WordCountService.php', 'refresh-word-counts.php')
$localHashes = @{}
foreach ($file in $deployFiles) {
    $localHashes[$file] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $PSScriptRoot "player-assistant-broker\$file")).Hash.ToLowerInvariant()
}

$remoteCode = @'
$directory = '__PRIVATE_DIRECTORY__';
$files = ['BrokerService.php', 'WordCountService.php', 'refresh-word-counts.php'];
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
$statusPath = $directory . '/word-count-refresh-status.json';
$result['status'] = [
    'exists' => is_file($statusPath),
    'mode' => is_file($statusPath) ? substr(sprintf('%o', fileperms($statusPath)), -4) : null,
];
$patterns = [
    'config.php.bak-deploy-*',
    'config.php.bak-word-count-refresh-*',
    'BrokerService.php.bak-deploy-*',
    'WordCountService.php.bak-deploy-*',
    'WordCountService.php.bak-source-refresh-*',
    'refresh-word-counts.php.bak-deploy-*',
];
foreach ($patterns as $pattern) {
    $matches = glob($directory . '/' . $pattern) ?: [];
    $result['backups'][$pattern] = count($matches);
}
$cron = shell_exec('crontab -l 2>/dev/null');
$result['cron'] = is_string($cron) ? $cron : '';
echo json_encode($result, JSON_UNESCAPED_SLASHES);
'@.Replace('__PRIVATE_DIRECTORY__', $PrivateDirectory.Replace("'", "\'"))

$remote = (Invoke-RemotePhp $remoteCode) | Out-String | ConvertFrom-Json
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

$cronNeedle = "/usr/bin/php $PrivateDirectory/refresh-word-counts.php"
if ($remote.cron -notmatch [regex]::Escape($cronNeedle)) {
    throw 'Production refresh cron entry is missing.'
}
foreach ($property in $remote.backups.PSObject.Properties) {
    if ([int]$property.Value -gt $KeepBackups) {
        throw "Backup retention exceeded for $($property.Name)."
    }
}

$sourceResponse = Invoke-WebRequest -Uri $SourceUrl -UseBasicParsing -TimeoutSec 30
$null = Test-WordCountSignedEnvelope -EnvelopeJson $sourceResponse.Content -PublicKeyBase64 $metadata.public_key -KeyId $metadata.key_id -PhpPath $PhpPath

$health = Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 30
if (-not $health.word_count_refresh.configured -or -not $health.word_count_refresh.signing_configured) {
    throw 'Public health does not report configured signed refreshes.'
}
if ($health.word_count_refresh.healthy -ne $true) {
    throw "Public health reports an unhealthy refresh: $($health.word_count_refresh.last_error_code)"
}
if ([string]::IsNullOrWhiteSpace([string]$health.word_count_refresh.last_scheduler_run_at)) {
    throw 'Public health does not report a scheduler run.'
}

Write-Output 'Word-count production drift check passed.'
