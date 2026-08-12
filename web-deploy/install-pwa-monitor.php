<?php

declare(strict_types=1);

if (PHP_SAPI !== 'cli') {
    fwrite(STDERR, "CLI only.\n");
    exit(1);
}

[$script, $stageDirectory, $privateDirectory, $credentialPath, $cronSchedule] = array_pad($argv, 5, null);
if (!is_string($stageDirectory) || !is_string($privateDirectory) || !is_string($credentialPath)
    || preg_match('#^/home/[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)+$#', $stageDirectory) !== 1
    || preg_match('#^/home/[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)+$#', $privateDirectory) !== 1
    || !is_string($cronSchedule) || preg_match('/^[0-9*,\/-]+ [0-9*,\/-]+ [0-9*,\/-]+ [0-9*,\/-]+ [0-9*,\/-]+$/', $cronSchedule) !== 1) {
    throw new RuntimeException('The monitor installer arguments are invalid.');
}
if (dirname($stageDirectory) !== $privateDirectory || !is_dir($stageDirectory) || !is_file($credentialPath)) {
    throw new RuntimeException('The private monitor stage or credential file is missing.');
}

$credentialBytes = (string)file_get_contents($credentialPath);
if (!unlink($credentialPath)) {
    throw new RuntimeException('Unable to remove the transient monitor credential file.');
}
$credentials = json_decode($credentialBytes, true, 8, JSON_THROW_ON_ERROR);
$credentialBytes = '';
$characterName = trim((string)($credentials['character_name'] ?? ''));
$password = (string)($credentials['password'] ?? '');
if ($characterName === '' || $password === '') {
    throw new RuntimeException('The monitor credentials are incomplete.');
}

$files = ['PwaSyntheticMonitor.php', 'run-pwa-monitor.php', 'BrokerService.php'];
foreach ($files as $file) {
    $path = $stageDirectory . '/' . $file;
    if (!is_file($path)) {
        throw new RuntimeException('A staged monitor file is missing.');
    }
    $output = [];
    $exit = 0;
    exec('/usr/bin/php -l ' . escapeshellarg($path) . ' 2>&1', $output, $exit);
    if ($exit !== 0) {
        throw new RuntimeException('PHP lint failed for ' . $file . '.');
    }
}

$configPath = $privateDirectory . '/config.php';
$config = is_file($configPath) ? require $configPath : [];
if (!is_array($config)) {
    throw new RuntimeException('The private broker configuration is invalid.');
}
$operations = is_array($config['operations'] ?? null) ? $config['operations'] : [];
$observability = is_array($config['observability'] ?? null) ? $config['observability'] : [];
$config['pwa_monitor'] = [
    'base_url' => 'https://bryanmiller.us/scarlethorizons',
    'character_name' => $characterName,
    'password' => $password,
    'status_path' => $privateDirectory . '/pwa-monitor-status.json',
    'maximum_xp_age_seconds' => 86400,
    'maximum_word_count_age_seconds' => 604800,
    'alert_cooldown_seconds' => max(3600, (int)($operations['alert_cooldown_seconds'] ?? $observability['alert_cooldown_seconds'] ?? 3600)),
    'alert_email' => trim((string)($operations['alert_email'] ?? $observability['alert_email'] ?? '')),
    'alert_from' => trim((string)($operations['alert_from'] ?? $observability['from_email'] ?? '')),
];

$deployId = gmdate('Ymd\THis\Z') . '-' . bin2hex(random_bytes(4));
$backupDirectory = '/home/dh_4gg2za/deploy-backups/pwa-monitor-' . $deployId;
if (!mkdir($backupDirectory, 0700, true)) {
    throw new RuntimeException('Unable to create the private deployment backup.');
}
chmod($backupDirectory, 0700);
$installed = [];
try {
    foreach ($files as $file) {
        $target = $privateDirectory . '/' . $file;
        if (is_file($target)) {
            if (!copy($target, $backupDirectory . '/' . $file)) {
                throw new RuntimeException('Unable to back up ' . $file . '.');
            }
            chmod($backupDirectory . '/' . $file, 0600);
        }
        $temporary = $privateDirectory . '/.' . $file . '.deploy-' . $deployId;
        if (!copy($stageDirectory . '/' . $file, $temporary)) {
            throw new RuntimeException('Unable to stage ' . $file . '.');
        }
        chmod($temporary, 0600);
        if (!rename($temporary, $target)) {
            throw new RuntimeException('Unable to promote ' . $file . '.');
        }
        chmod($target, 0600);
        $installed[] = $file;
    }

    if (is_file($configPath)) {
        copy($configPath, $backupDirectory . '/config.php');
        chmod($backupDirectory . '/config.php', 0600);
    }
    $newConfig = "<?php\nreturn " . var_export($config, true) . ";\n";
    $temporaryConfig = $privateDirectory . '/.config.php.deploy-' . $deployId;
    if (file_put_contents($temporaryConfig, $newConfig, LOCK_EX) === false) {
        throw new RuntimeException('Unable to stage private monitor configuration.');
    }
    chmod($temporaryConfig, 0600);
    if (!rename($temporaryConfig, $configPath)) {
        throw new RuntimeException('Unable to promote private monitor configuration.');
    }
    chmod($configPath, 0600);

    $output = [];
    $listExit = 0;
    exec('timeout 10 /usr/bin/crontab -l 2>/dev/null', $output, $listExit);
    if ($listExit !== 0 && $listExit !== 1) {
        throw new RuntimeException('Unable to read the existing crontab.');
    }
    file_put_contents($backupDirectory . '/crontab.txt', implode("\n", $output) . "\n", LOCK_EX);
    chmod($backupDirectory . '/crontab.txt', 0600);
    $lines = array_values(array_filter($output, static fn(string $line): bool =>
        trim($line) !== '' && !str_contains($line, '/player-assistant-broker/run-pwa-monitor.php')));
    $lines[] = $cronSchedule . ' /usr/bin/php ' . $privateDirectory . '/run-pwa-monitor.php >> ' . $privateDirectory . '/pwa-monitor-cron.log 2>&1';
    $temporaryCron = tempnam(sys_get_temp_dir(), 'pa-monitor-cron-');
    file_put_contents($temporaryCron, implode("\n", $lines) . "\n", LOCK_EX);
    $cronOutput = [];
    $cronExit = 0;
    exec('timeout 10 /usr/bin/crontab ' . escapeshellarg($temporaryCron) . ' 2>&1', $cronOutput, $cronExit);
    @unlink($temporaryCron);
    if ($cronExit !== 0) {
        throw new RuntimeException('Unable to install the monitor cron job.');
    }
} catch (Throwable $error) {
    foreach ($installed as $file) {
        $backup = $backupDirectory . '/' . $file;
        $target = $privateDirectory . '/' . $file;
        if (is_file($backup)) {
            copy($backup, $target);
            chmod($target, 0600);
        } else {
            @unlink($target);
        }
    }
    if (is_file($backupDirectory . '/config.php')) {
        copy($backupDirectory . '/config.php', $configPath);
        chmod($configPath, 0600);
    }
    throw $error;
} finally {
    foreach (glob($stageDirectory . '/*') ?: [] as $staged) {
        @unlink($staged);
    }
    @rmdir($stageDirectory);
}

fwrite(STDOUT, json_encode([
    'status' => 'installed',
    'files' => $files,
    'cron_schedule' => $cronSchedule,
    'backup_directory' => $backupDirectory,
], JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR) . PHP_EOL);
