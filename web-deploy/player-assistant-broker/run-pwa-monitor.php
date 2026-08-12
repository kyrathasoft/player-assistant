<?php

declare(strict_types=1);

require_once __DIR__ . '/PwaSyntheticMonitor.php';

$configPath = __DIR__ . '/config.php';
try {
    if (!is_file($configPath)) {
        throw new PwaMonitorFailure('configuration_missing', 'The private monitor configuration file is missing.');
    }
    $config = require $configPath;
    if (!is_array($config) || !is_array($config['pwa_monitor'] ?? null)) {
        throw new PwaMonitorFailure('configuration_invalid', 'The private monitor configuration is incomplete.');
    }
    $result = (new PwaSyntheticMonitor($config['pwa_monitor']))->run();
    fwrite(STDOUT, json_encode($result, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR) . PHP_EOL);
    exit(0);
} catch (Throwable $error) {
    $code = $error instanceof PwaMonitorFailure ? $error->errorCode : 'monitor_internal_error';
    fwrite(STDERR, 'PWA monitor failed: ' . preg_replace('/[^a-z0-9_]+/i', '_', $code) . PHP_EOL);
    exit(1);
}
