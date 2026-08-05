<?php

declare(strict_types=1);

if (PHP_SAPI !== 'cli') {
    http_response_code(404);
    exit(1);
}

require_once __DIR__ . '/BrokerOperations.php';

try {
    $configPath = $argv[1] ?? (__DIR__ . '/config.php');
    if (!is_string($configPath) || !is_file($configPath)) {
        throw new RuntimeException('The broker configuration file was not found.');
    }

    $config = require $configPath;
    if (!is_array($config)) {
        throw new RuntimeException('The broker configuration is invalid.');
    }

    $operations = new BrokerOperations($config);
    $result = $operations->runMaintenance();
    unset($result['backup']['path']);
    echo json_encode($result, JSON_UNESCAPED_SLASHES) . PHP_EOL;
} catch (Throwable $error) {
    fwrite(STDERR, 'Broker maintenance failed: ' . $error->getMessage() . PHP_EOL);
    exit(1);
}
