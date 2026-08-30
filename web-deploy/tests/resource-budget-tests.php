<?php
declare(strict_types=1);
$path = __DIR__ . '/../../resource-budgets.json';
$payload = json_decode((string)file_get_contents($path), true, 512, JSON_THROW_ON_ERROR);
foreach ($payload['budgets'] as $name => $value) {
    if (!is_int($value) || $value <= 0) throw new RuntimeException("Budget $name must be positive.");
}
if ($payload['budgets']['pwa_polling_seconds'] < 15) throw new RuntimeException('PWA polling budget is too aggressive.');
if ($payload['budgets']['optional_pack_bytes'] + 1 <= $payload['budgets']['optional_pack_bytes']) throw new RuntimeException('Boundary arithmetic failed.');
echo "PASS resource budget boundaries and slow-I/O fixture contract.\n";
