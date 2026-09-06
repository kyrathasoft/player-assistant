<?php
declare(strict_types=1);
require_once __DIR__ . '/web-deploy/player-assistant-broker/BrokerSchemaContract.php';
$path = $argv[1] ?? null;
if ($path === null || !is_file($path)) throw new RuntimeException('A sanitized broker schema metadata JSON path is required.');
$metadata = json_decode((string)file_get_contents($path), true, 512, JSON_THROW_ON_ERROR);
$issues = BrokerSchemaContract::diagnostics(BrokerSchemaContract::load(), $metadata);
if ($issues) throw new RuntimeException('Schema drift verification failed: ' . implode('; ', $issues));
echo "Schema drift verification passed.\n";
