<?php
declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/CorrelationContext.php';

$canaries = [
    'fixture-password-7f2d', 'fixture-bearer-8a31', 'fixture-admin-key-9c42',
    'fixture-cookie-ad3e', 'fixture-storage-state-be54', 'fixture-private-path-cf65',
    'fixture-response-body-dg76',
];
$artifacts = [
    'logging' => 'password=fixture-password-7f2d',
    'errors' => 'Authorization: Bearer fixture-bearer-8a31',
    'metrics' => 'admin_key=fixture-admin-key-9c42',
    'diagnostics' => 'Cookie: session=fixture-cookie-ad3e storage_state=fixture-storage-state-be54',
    'crash_reports' => 'path=C:\\Users\\Bryan\\fixture-private-path-cf65',
    'http_responses' => 'body=fixture-response-body-dg76',
    'browser_console' => 'token=fixture-bearer-8a31',
    'ci_artifacts' => 'private_path=C:\\Users\\Bryan\\fixture-private-path-cf65',
    'generated_bundles' => 'password=fixture-password-7f2d',
];
foreach ($artifacts as $kind => $raw) {
    $redacted = CorrelationContext::redact($raw);
    foreach ($canaries as $canary) {
        if (str_contains($redacted, $canary)) throw new RuntimeException("$kind leaked protected fixture data");
    }
    if (!str_contains($redacted, '[REDACTED]')) throw new RuntimeException("$kind lacked redaction marker");
}
$safe = 'correlation_id=fixture-correlation-001 endpoint=/v1/health status=401';
if (CorrelationContext::redact($safe) !== $safe) throw new RuntimeException('safe identifiers changed');
echo "Protected-data negative-space PHP tests passed.\n";
