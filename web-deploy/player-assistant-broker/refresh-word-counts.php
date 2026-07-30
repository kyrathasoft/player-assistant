<?php

declare(strict_types=1);

if (PHP_SAPI !== 'cli') {
    http_response_code(404);
    exit(1);
}

require_once __DIR__ . '/BrokerHttpException.php';
require_once __DIR__ . '/WordCountService.php';

try {
    $configPath = $argv[1] ?? (__DIR__ . '/config.php');
    if (!is_string($configPath) || !is_file($configPath)) {
        throw new RuntimeException('The broker configuration file was not found.');
    }

    $config = require $configPath;
    if (!is_array($config)) {
        throw new RuntimeException('The broker configuration is invalid.');
    }

    $databasePath = (string)($config['api']['database_path'] ?? '');
    if ($databasePath === '') {
        throw new RuntimeException('The broker database path is not configured.');
    }

    $database = new PDO('sqlite:' . $databasePath, null, null, [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
        PDO::ATTR_EMULATE_PREPARES => false,
    ]);
    $database->exec('PRAGMA busy_timeout = 5000');

    $service = new WordCountService(
        $database,
        is_array($config['word_counts'] ?? null) ? $config['word_counts'] : []);
    $snapshot = $service->refreshNow();
    $refreshStatus = $service->refreshStatus();
    if ($refreshStatus['healthy'] === false) {
        $service->recordSchedulerRun(false, (string)$refreshStatus['last_error_code']);
        throw new RuntimeException(
            'The latest source-backed refresh attempt failed: '
            . (string)$refreshStatus['last_error_code']);
    }
    $service->recordSchedulerRun(true);

    echo json_encode([
        'status' => 'ok',
        'observed_at' => $snapshot['observed_at'],
        'uploaded_at' => $snapshot['uploaded_at'],
        'refresh' => $service->refreshStatus(),
    ], JSON_UNESCAPED_SLASHES) . PHP_EOL;
} catch (Throwable $error) {
    if (isset($service) && $service instanceof WordCountService) {
        $service->recordSchedulerRun(false, 'scheduler_failed');
    }
    fwrite(STDERR, 'Word-count refresh failed: ' . $error->getMessage() . PHP_EOL);
    exit(1);
}
