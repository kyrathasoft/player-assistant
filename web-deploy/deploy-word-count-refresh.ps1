[CmdletBinding()]
param(
    [string]$DreamHostTarget = 'player-assistant-dreamhost',
    [string]$SshKeyPath = (Join-Path $HOME '.ssh\dreamhost_player_assistant'),
    [string]$PrivateDirectory = '/home/dh_4gg2za/player-assistant-broker',
    [uri]$SourceUrl = 'https://bryanmiller.us/scarlethorizons/data/word-counts.json',
    [string]$SigningMetadataPath = (Join-Path $PSScriptRoot 'word-count-signing-public.json'),
    [int]$KeepBackups = 5,
    [string]$CronSchedule = '17 */6 * * *'
)

$ErrorActionPreference = 'Stop'

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

if (-not (Test-Path -LiteralPath $SshKeyPath -PathType Leaf)) {
    throw "SSH key not found: $SshKeyPath"
}
$metadata = Get-Content -Raw -LiteralPath $SigningMetadataPath | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace([string]$metadata.key_id) -or [string]::IsNullOrWhiteSpace([string]$metadata.public_key)) {
    throw 'Signing metadata is incomplete.'
}

$deployId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$deployFiles = @('BrokerService.php', 'BrokerAlertService.php', 'DatabaseMigrationService.php', 'QuestService.php', 'WordCountService.php', 'refresh-word-counts.php')
$remoteStage = "$PrivateDirectory/.word-count-deploy-$deployId"
$remoteArchive = "$PrivateDirectory/.word-count-deploy-$deployId.tar"
$localArchive = Join-Path ([IO.Path]::GetTempPath()) "player-assistant-word-count-$deployId.tar"
$remoteTemps = @{}
foreach ($file in $deployFiles) {
    $remoteTemp = "$remoteStage/$file"
    $remoteTemps[$file] = $remoteTemp
}

try {
    $brokerDirectory = Join-Path $PSScriptRoot 'player-assistant-broker'
    Invoke-CheckedNative {
        & tar -cf $localArchive -C $brokerDirectory -- @deployFiles
    } | Out-Null
    Invoke-CheckedNative {
        & scp -q -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 -- `
            $localArchive "${DreamHostTarget}:$remoteArchive"
    } | Out-Null
    $extractCommand = "rm -rf -- '$remoteStage' && mkdir '$remoteStage' && tar -xf '$remoteArchive' -C '$remoteStage' && rm -f -- '$remoteArchive'"
    Invoke-CheckedNative {
        & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
            -o ConnectionAttempts=1 -o ServerAliveInterval=5 -o ServerAliveCountMax=3 `
            $DreamHostTarget $extractCommand
    } | Out-Null
}
finally {
    Remove-Item -LiteralPath $localArchive -Force -ErrorAction SilentlyContinue
}

$installData = @{
    private_directory = $PrivateDirectory
    source_url = $SourceUrl.AbsoluteUri
    status_path = "$PrivateDirectory/word-count-refresh-status.json"
    key_id = [string]$metadata.key_id
    public_key = [string]$metadata.public_key
    keep_backups = $KeepBackups
    deploy_id = $deployId
    files = $remoteTemps
} | ConvertTo-Json -Compress
$installData64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($installData))

$installCode = @'
$data = json_decode(base64_decode('__INSTALL_DATA__'), true, 32, JSON_THROW_ON_ERROR);
$directory = $data['private_directory'];
foreach ($data['files'] as $file => $temporaryPath) {
    $lintOutput = [];
    $lintExit = 0;
    exec('/usr/bin/php -l ' . escapeshellarg($temporaryPath) . ' 2>&1', $lintOutput, $lintExit);
    if ($lintExit !== 0) {
        throw new RuntimeException('PHP lint failed for ' . $file . ': ' . implode("\n", $lintOutput));
    }
    $target = $directory . '/' . $file;
    if (is_file($target) && hash_file('sha256', $target) === hash_file('sha256', $temporaryPath)) {
        unlink($temporaryPath);
        chmod($target, 0600);
        continue;
    }
    if (is_file($target)) {
        copy($target, $target . '.bak-deploy-' . $data['deploy_id']);
        chmod($target . '.bak-deploy-' . $data['deploy_id'], 0600);
    }
    chmod($temporaryPath, 0600);
    if (!rename($temporaryPath, $target)) {
        throw new RuntimeException('Unable to install ' . $file);
    }
}

$configPath = $directory . '/config.php';
$config = is_file($configPath) ? require $configPath : [];
if (!is_array($config)) {
    throw new RuntimeException('Private config did not return an array.');
}
$config['word_counts'] = array_merge(
    is_array($config['word_counts'] ?? null) ? $config['word_counts'] : [],
    [
        'source_url' => $data['source_url'],
        'max_stale_seconds' => 604800,
        'status_path' => $data['status_path'],
        'signature_key_id' => $data['key_id'],
        'signature_public_key' => $data['public_key'],
    ]
);
$newConfig = "<?php\nreturn " . var_export($config, true) . ";\n";
$oldConfig = is_file($configPath) ? file_get_contents($configPath) : '';
if ($oldConfig !== $newConfig) {
    if (is_file($configPath)) {
        copy($configPath, $configPath . '.bak-deploy-' . $data['deploy_id']);
        chmod($configPath . '.bak-deploy-' . $data['deploy_id'], 0600);
    }
    $temporaryConfig = $configPath . '.tmp-' . $data['deploy_id'];
    file_put_contents($temporaryConfig, $newConfig, LOCK_EX);
    chmod($temporaryConfig, 0600);
    if (!rename($temporaryConfig, $configPath)) {
        throw new RuntimeException('Unable to install private config.');
    }
}
chmod($configPath, 0600);

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
];
foreach ($patterns as $pattern) {
    $matches = glob($directory . '/' . $pattern) ?: [];
    usort($matches, static fn(string $left, string $right): int => filemtime($right) <=> filemtime($left));
    foreach (array_slice($matches, $data['keep_backups']) as $obsolete) {
        $resolved = realpath($obsolete);
        if ($resolved !== false && dirname($resolved) === realpath($directory) && is_file($resolved)) {
            unlink($resolved);
        }
    }
}
foreach (['.BrokerService.php.deploy-*', '.BrokerAlertService.php.deploy-*', '.DatabaseMigrationService.php.deploy-*', '.QuestService.php.deploy-*', '.WordCountService.php.deploy-*', '.refresh-word-counts.php.deploy-*'] as $pattern) {
    foreach (glob($directory . '/' . $pattern) ?: [] as $abandonedTemporaryFile) {
        if (is_file($abandonedTemporaryFile)) {
            unlink($abandonedTemporaryFile);
        }
    }
}
@rmdir($directory . '/.word-count-deploy-' . $data['deploy_id']);
'@.Replace('__INSTALL_DATA__', $installData64)

Invoke-RemotePhp $installCode | Out-Null

$cronLine = "$CronSchedule /usr/bin/php $PrivateDirectory/refresh-word-counts.php >> $PrivateDirectory/word-count-refresh-cron.log 2>&1"
$cronData = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($cronLine))
$cronCode = @'
$line = base64_decode('__CRON_LINE__');
$output = [];
$listExit = 0;
exec('timeout 10 /usr/bin/crontab -l 2>/dev/null', $output, $listExit);
if ($listExit !== 0 && $listExit !== 1) {
    throw new RuntimeException('Unable to read existing cron within the timeout.');
}
$existing = implode("\n", $output);
$lines = preg_split('/\R/', trim($existing));
$lines = array_values(array_filter($lines, static function ($candidate): bool {
    return $candidate !== '' && !str_contains($candidate, '/player-assistant-broker/refresh-word-counts.php');
}));
$lines[] = $line;
$temporary = tempnam(sys_get_temp_dir(), 'pa-cron-');
file_put_contents($temporary, implode("\n", $lines) . "\n");
$output = [];
$exit = 0;
exec('timeout 10 /usr/bin/crontab ' . escapeshellarg($temporary) . ' 2>&1', $output, $exit);
unlink($temporary);
if ($exit !== 0) {
    throw new RuntimeException('Unable to install cron within the timeout: ' . implode("\n", $output));
}
'@.Replace('__CRON_LINE__', $cronData)
Invoke-RemotePhp $cronCode | Out-Null

$runnerCommand = "/usr/bin/php $PrivateDirectory/refresh-word-counts.php"
Invoke-CheckedNative {
    & ssh -i $SshKeyPath -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 `
        $DreamHostTarget $runnerCommand
} | Out-Null

& (Join-Path $PSScriptRoot 'test-word-count-refresh-deployment.ps1') `
    -DreamHostTarget $DreamHostTarget `
    -SshKeyPath $SshKeyPath `
    -PrivateDirectory $PrivateDirectory `
    -SourceUrl $SourceUrl `
    -SigningMetadataPath $SigningMetadataPath `
    -KeepBackups $KeepBackups

Write-Output 'Word-count refresh deployment passed.'
