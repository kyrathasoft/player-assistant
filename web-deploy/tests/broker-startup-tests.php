<?php

declare(strict_types=1);

function startupAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$root = dirname(__DIR__);
$brokerPath = $root . '/player-assistant-broker/BrokerService.php';
$indexPath = $root . '/bryanmiller.us/scarlethorizons/api/index.php';
$migrationPath = $root . '/player-assistant-broker/migrate-broker.php';
$broker = (string)file_get_contents($brokerPath);
$index = (string)file_get_contents($indexPath);

startupAssert(is_file($migrationPath), 'The deployment migration entry point is missing.');
startupAssert(!str_contains($broker, '->migrate()'), 'Request startup still runs database migrations.');
startupAssert(str_contains($broker, 'private ?CharacterAuthService $characterAuth = null;'), 'Character authentication is not lazy.');
startupAssert(str_contains($broker, 'private function characterAuth(): CharacterAuthService'), 'The lazy character-authentication factory is missing.');
startupAssert(str_contains($broker, 'private function verifySchemaVersion(): void'), 'The broker schema version guard is missing.');

$healthMarker = "if (\$method === 'GET' && \$route === '/v1/health')";
$serviceMarker = '$service = new BrokerService(';
$healthPosition = strpos($index, $healthMarker);
$servicePosition = strpos($index, $serviceMarker);
startupAssert($healthPosition !== false && $servicePosition !== false && $healthPosition < $servicePosition, 'The public health route still constructs BrokerService first.');
startupAssert(str_contains($index, "sendJson(200, [
            'service' => 'player-assistant-broker'"), 'The public health route is not handled without BrokerService.');
startupAssert(!str_contains(substr($index, 0, $healthPosition), 'new BrokerOperations'), 'The public health route still initializes broker operations first.');

foreach (['CharacterAuthService.php', 'MessageService.php', 'QuestService.php', 'XpTrackingService.php', 'WordCountService.php'] as $file) {
    $source = (string)file_get_contents($root . '/player-assistant-broker/' . $file);
    startupAssert(!str_contains($source, '$this->ensureSchema();'), "$file still performs schema creation during service construction.");
}

echo "Broker startup tests passed.\n";
