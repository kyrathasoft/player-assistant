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
$authorizationPolicyPath = $root . '/player-assistant-broker/AuthorizationPolicy.php';
$broker = (string)file_get_contents($brokerPath);
$index = (string)file_get_contents($indexPath);
$authorizationPolicy = (string)file_get_contents($authorizationPolicyPath);

startupAssert(is_file($migrationPath), 'The deployment migration entry point is missing.');
startupAssert(!str_contains($broker, '->migrate()'), 'Request startup still runs database migrations.');
startupAssert(str_contains($broker, 'private ?CharacterAuthService $characterAuth = null;'), 'Character authentication is not lazy.');
startupAssert(str_contains($broker, 'private function characterAuth(): CharacterAuthService'), 'The lazy character-authentication factory is missing.');
startupAssert(str_contains($broker, 'private function verifySchemaVersion(): void'), 'The broker schema version guard is missing.');

$healthMarker = "if (\$method === 'GET' && \$healthRoute === '/v1/health')";
$serviceMarker = '$service = new BrokerService(';
$healthPosition = strpos($index, $healthMarker);
$servicePosition = strpos($index, $serviceMarker);
$httpsPosition = strpos($index, 'requireHttps();');
$configPosition = strpos($index, '$config = require $configPath;');
startupAssert($healthPosition !== false && $servicePosition !== false && $healthPosition < $servicePosition, 'The public health route still constructs BrokerService first.');
startupAssert($healthPosition !== false && $httpsPosition !== false && $healthPosition < $httpsPosition, 'The public health route still performs HTTPS/private-subsystem startup first.');
startupAssert($healthPosition !== false && $configPosition !== false && $healthPosition < $configPosition, 'The public health route still loads private configuration first.');
startupAssert(str_contains($index, "sendJson(200, [
            'service' => 'player-assistant-broker'"), 'The public health route is not handled without BrokerService.');
$sessionRouteList = substr($index, strpos($index, 'function isCharacterSessionRoute'));
startupAssert(str_contains($sessionRouteList, 'AuthorizationPolicy::isCharacterSessionRoute'), 'The HTTP boundary does not use the canonical session policy.');
startupAssert(str_contains($authorizationPolicy, "'/v1/magic-items'"), 'The canonical policy omits the magic-item route.');
startupAssert(str_contains($authorizationPolicy, "'/v1/revisions'"), 'The canonical policy omits the revision route.');
startupAssert(str_contains($authorizationPolicy, "'/v1/xp-level-up-notifications/claim'"), 'The canonical policy omits the level-up claim route.');
startupAssert(str_contains($authorizationPolicy, "'/v1/xp-level-up-notifications/acknowledge'"), 'The canonical policy omits the level-up acknowledgement route.');
startupAssert(!str_contains(substr($index, 0, $healthPosition), 'new BrokerOperations'), 'The public health route still initializes broker operations first.');

foreach (['CharacterAuthService.php', 'MessageService.php', 'QuestService.php', 'XpTrackingService.php', 'WordCountService.php'] as $file) {
    $source = (string)file_get_contents($root . '/player-assistant-broker/' . $file);
    startupAssert(!str_contains($source, '$this->ensureSchema();'), "$file still performs schema creation during service construction.");
}

echo "Broker startup tests passed.\n";
