<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/DatabaseMigrationService.php';
require_once __DIR__ . '/../player-assistant-broker/BrokerSchemaContract.php';

function driftAssert(bool $condition, string $message): void { if (!$condition) throw new RuntimeException($message); }
function fixtureDb(): PDO {
    $db = new PDO('sqlite::memory:', null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    (new DatabaseMigrationService($db, sys_get_temp_dir() . '/pa-schema-drift-' . bin2hex(random_bytes(4))))->migrate();
    return $db;
}
$manifest = BrokerSchemaContract::load();
$db = fixtureDb();
try {
    $actual = BrokerSchemaContract::inspect($db);
    driftAssert(BrokerSchemaContract::diagnostics($manifest, $actual) === [], 'The generated migration fixture drifted from the contract.');
    $missing = $actual; $missing['objects'] = array_values(array_filter($missing['objects'], fn(array $o): bool => $o['name'] !== 'message_send_rate_limits'));
    driftAssert(str_contains(implode(' ', BrokerSchemaContract::diagnostics($manifest, $missing)), 'missing object table:message_send_rate_limits'), 'Missing table was not detected.');
    $extra = $actual; $extra['objects'][] = ['type' => 'table', 'name' => 'unexpected_fixture_table', 'columns' => []];
    driftAssert(str_contains(implode(' ', BrokerSchemaContract::diagnostics($manifest, $extra)), 'extra object table:unexpected_fixture_table'), 'Extra table was not detected.');
    $field = $actual; foreach ($field['objects'] as &$object) if ($object['name'] === 'api_tokens') $object['columns'][] = ['name'=>'unexpected_field','type'=>'TEXT','notnull'=>0,'pk'=>0]; unset($object);
    driftAssert(str_contains(implode(' ', BrokerSchemaContract::diagnostics($manifest, $field)), 'extra column table:api_tokens.unexpected_field'), 'Extra field was not detected.');
    $version = $actual; $version['migration_version'] = 7;
    driftAssert(str_contains(implode(' ', BrokerSchemaContract::diagnostics($manifest, $version)), 'migration version mismatch'), 'Version mismatch was not detected.');
    $definition = $actual; foreach ($definition['objects'] as &$object) if ($object['type'] === 'trigger') { $object['definition'] .= ' weakened'; break; } unset($object);
    driftAssert(str_contains(implode(' ', BrokerSchemaContract::diagnostics($manifest, $definition)), 'definition mismatch'), 'Trigger definition drift was not detected.');
    $constraint = $actual; foreach ($constraint['objects'] as &$object) if ($object['type'] === 'table' && $object['name'] === 'character_accounts') { $object['definition'] = str_replace('CHECK', 'CHECK_DISABLED', $object['definition']); break; } unset($object);
    driftAssert(str_contains(implode(' ', BrokerSchemaContract::diagnostics($manifest, $constraint)), 'definition mismatch'), 'Constraint drift was not detected.');
    $approved = $manifest; $approved['compatibility_exceptions']['extra_objects'][] = 'table:temporary_compatibility_view'; $approvedActual=$actual; $approvedActual['objects'][]=['type'=>'table','name'=>'temporary_compatibility_view','columns'=>[]];
    driftAssert(BrokerSchemaContract::diagnostics($approved, $approvedActual) === [], 'Approved compatibility exception was not honored.');
    $packaged = $manifest;
    $packaged['migration_version'] = $manifest['migration_version'] - 1;
    driftAssert(str_contains(implode(' ', BrokerSchemaContract::diagnostics($manifest, $packaged)), 'migration version mismatch'), 'Generated/package version drift was not detected.');
    $packageLayout = json_decode((string)file_get_contents(__DIR__ . '/../../pwa/online-installer-for-pwa/package-layout.json'), true, 512, JSON_THROW_ON_ERROR);
    driftAssert(in_array('BrokerSchemaContract.php', $packageLayout['private_runtime_files'], true) && in_array('schema-contract.json', $packageLayout['private_runtime_files'], true), 'The release package omits schema contract files.');
    $rows = $db->query("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")->fetchColumn();
    driftAssert((int)$rows > 0, 'Fixture did not contain schema objects.');
    echo "Schema drift tests passed.\n";
} finally { $db = null; }
