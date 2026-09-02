<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/CapabilityPolicy.php';

function capabilityAssert(bool $condition, string $message): void
{
    if (!$condition) throw new RuntimeException($message);
}

capabilityAssert(CapabilityPolicy::forRoute('PUT', '/v1/snapshots/page') === 'snapshots.publish', 'snapshot route mapping missing');
capabilityAssert(CapabilityPolicy::forRoute('GET', '/v1/rpol/page') === 'rpol.read', 'RPOL route mapping missing');
capabilityAssert(CapabilityPolicy::forRoute('POST', '/v1/admin/unknown') === null, 'unknown route was mapped');

$resource = 'https://rpol.net/display.cgi?gi=80170&ti=3';
$read = ['name' => 'snapshots.read', 'resource' => $resource];
capabilityAssert(CapabilityPolicy::permits($read, 'snapshots.read', $resource), 'intended resource capability denied');
capabilityAssert(!CapabilityPolicy::permits($read, 'rpol.read', $resource), 'capability confusion was accepted');
capabilityAssert(!CapabilityPolicy::permits($read, 'snapshots.read', $resource . '&other=1'), 'cross-resource capability was accepted');
capabilityAssert(!CapabilityPolicy::permits(['name' => 'snapshots.read'], 'snapshots.read', $resource), 'unscoped resource capability was accepted');
capabilityAssert(!CapabilityPolicy::permits(['name' => 'snapshots.read', 'resource' => $resource, 'account_id' => str_repeat('b', 32)], 'snapshots.read', $resource, str_repeat('a', 32)), 'cross-account capability was accepted');

$grants = CapabilityPolicy::validateGrants([
    ['name' => 'word-counts.publish', 'resource' => 'campaign-search.json'],
    ['name' => 'snapshots.publish', 'resource' => $resource],
]);
capabilityAssert(count($grants) === 2, 'valid least-privilege grants were not retained');
foreach ([[], [['name' => 'unknown.operation']], [['name' => 'snapshots.read', 'resource' => '*']], [['name' => 'snapshots.read']]] as $invalid) {
    try { CapabilityPolicy::validateGrants($invalid); } catch (InvalidArgumentException) { continue; }
    throw new RuntimeException('overbroad, unknown, or unscoped grant was accepted');
}
try { CapabilityPolicy::validateGrants([['name' => 'rpol.read', 'resource' => $resource], ['name' => 'rpol.read', 'resource' => $resource]]); } catch (InvalidArgumentException) { echo "Capability policy tests passed.\n"; return; }
throw new RuntimeException('duplicate grant replay was accepted');
