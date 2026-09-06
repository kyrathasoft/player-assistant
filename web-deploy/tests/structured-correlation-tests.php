<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/StructuredCorrelation.php';
require_once __DIR__ . '/../player-assistant-broker/BrokerAlertService.php';

function correlationAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$correlationId = '0123456789abcdef0123456789abcdef';
correlationAssert(StructuredCorrelation::sanitizeId($correlationId) === $correlationId, 'A valid correlation ID was not preserved.');
correlationAssert(StructuredCorrelation::sanitizeId('not safe') === null, 'An invalid correlation ID was accepted.');

$record = StructuredCorrelation::redact([
    'correlation_id' => $correlationId,
    'operation' => 'synthetic-monitor',
    'password' => 'fixture-password',
    'cookie' => 'session=fixture-cookie',
    'authorization' => 'Bearer fixture-token',
    'csrf_token' => 'fixture-csrf',
    'protected_response_body' => '{"secret":"fixture-protected-body"}',
]);
$encoded = json_encode($record, JSON_THROW_ON_ERROR);
correlationAssert(str_contains($encoded, $correlationId), 'The correlation ID was not retained in a safe record.');
foreach (['fixture-password', 'fixture-cookie', 'fixture-token', 'fixture-csrf', 'fixture-protected-body'] as $secret) {
    correlationAssert(!str_contains($encoded, $secret), "Redaction leaked $secret.");
}
foreach (['password', 'cookie', 'authorization', 'csrf_token', 'protected_response_body'] as $field) {
    correlationAssert($record[$field] === '[REDACTED]', "Redaction did not reject $field.");
}

$context = StructuredCorrelation::context($correlationId, ['operation' => 'synthetic-monitor']);
correlationAssert($context['correlation_id'] === $correlationId, 'The operation context lost its correlation ID.');
correlationAssert($context['operation'] === 'synthetic-monitor', 'The operation context lost its safe operation name.');

$database = new PDO('sqlite::memory:', null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
$database->exec('CREATE TABLE broker_alert_events (id INTEGER PRIMARY KEY AUTOINCREMENT, alert_type TEXT, occurred_at INTEGER, error_code TEXT, message TEXT, alert_sent_at INTEGER NULL)');
$alerts = new BrokerAlertService($database, ['server_error_threshold' => 1]);
$alerts->recordServerError('synthetic_failure', 'safe failure', $correlationId);
$stored = $database->query('SELECT correlation_id FROM broker_alert_events')->fetchColumn();
correlationAssert($stored === $correlationId, 'The alert record did not retain the correlation ID.');

$api = file_get_contents(__DIR__ . '/../bryanmiller.us/scarlethorizons/api/index.php');
$app = file_get_contents(__DIR__ . '/../../pwa/app.js');
$worker = file_get_contents(__DIR__ . '/../../pwa/translator-worker.js');
foreach ([[$api, "header('X-Correlation-ID: ' . \$correlationId);"] , [$api, 'CorrelationContext::create'], [$app, 'correlationHeaders(CORRELATION_CONTEXT)'], [$worker, 'correlationId: message.correlationId']] as [$source, $marker]) {
    correlationAssert(is_string($source) && str_contains($source, $marker), "Missing correlation boundary marker: $marker");
}

echo "Structured correlation tests passed.\n";
