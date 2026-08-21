<?php

declare(strict_types=1);

function installerAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

function installerRemoveTree(string $path): void
{
    if (is_file($path) || is_link($path)) {
        @unlink($path);
        return;
    }
    if (!is_dir($path)) {
        return;
    }
    $iterator = new RecursiveIteratorIterator(
        new RecursiveDirectoryIterator($path, FilesystemIterator::SKIP_DOTS),
        RecursiveIteratorIterator::CHILD_FIRST);
    foreach ($iterator as $entry) {
        $entry->isDir() && !$entry->isLink() ? @rmdir($entry->getPathname()) : @unlink($entry->getPathname());
    }
    @rmdir($path);
}

function installerRemoveLink(string $path): void
{
    if (is_dir($path)) {
        @rmdir($path);
    }
    if (file_exists($path) || is_link($path)) {
        @unlink($path);
    }
}

$root = dirname(__DIR__);
$installer = $root . '/install-player-assistant-web.php';
$installerSource = (string)file_get_contents($installer);
$runInstallOffset = strpos($installerSource, 'function runInstall');
$maintenanceOffset = strpos($installerSource, 'activateApiMaintenance($apiTarget);', $runInstallOffset);
$snapshotOffset = strpos($installerSource, 'snapshotDatabase($databasePath', $runInstallOffset);
installerAssert(
    is_int($maintenanceOffset) && is_int($snapshotOffset) && $maintenanceOffset < $snapshotOffset,
    'The installer snapshots SQLite before quiescing API writes.');
installerAssert(
    str_contains($installerSource, 'snapshotFileVerified($target, $transactionDirectory'),
    'Private-runtime rollback evidence is not captured through the verified snapshot helper.');
installerAssert(
    substr_count($installerSource, 'verifyRollbackEvidenceAgainstLive(') >= 2,
    'Private-runtime rollback evidence is not reverified against the live target before promotion.');
foreach (['install-player-assistant-web.php', 'config.template.php', 'README.md'] as $distributedFile) {
    installerAssert(
        is_file($root . '/dist/' . $distributedFile)
            && hash_equals(
                (string)hash_file('sha256', $root . '/' . $distributedFile),
                (string)hash_file('sha256', $root . '/dist/' . $distributedFile)),
        "The checked-in distributable is stale: $distributedFile");
}
$command = escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer) . ' --help 2>&1';
$output = [];
$exitCode = 0;
exec($command, $output, $exitCode);

installerAssert($exitCode === 0, 'The installer help command failed: ' . implode("\n", $output));
$help = implode("\n", $output);
installerAssert(str_contains($help, '--package='), 'Installer help omitted the package argument.');
installerAssert(str_contains($help, '--origin=https://example.com'), 'Installer help omitted the HTTPS origin contract.');
installerAssert(str_contains($help, '/scarlethorizons/pwa'), 'Installer help omitted the fixed PWA URL layout.');
installerAssert(str_contains($help, '--config-source='), 'Installer help omitted the private configuration input.');

$invalidOriginOutput = [];
$invalidOriginExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --package=' . escapeshellarg($root . '/missing.tar')
        . ' --origin=' . escapeshellarg('http://example.com')
        . ' --public-root=' . escapeshellarg(sys_get_temp_dir() . '/example.com')
        . ' --private-root=' . escapeshellarg(sys_get_temp_dir() . '/player-assistant-broker-example')
        . ' --config-source=' . escapeshellarg($root . '/missing-config.php')
        . ' 2>&1',
    $invalidOriginOutput,
    $invalidOriginExit);
installerAssert($invalidOriginExit !== 0, 'An insecure target origin was accepted.');
installerAssert(
    str_contains(implode("\n", $invalidOriginOutput), 'origin must be an HTTPS origin without a path'),
    'The insecure-origin failure was not explicit.');

$unknownOutput = [];
$unknownExit = 0;
exec(escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer) . ' --unknown-option=value 2>&1', $unknownOutput, $unknownExit);
installerAssert($unknownExit !== 0 && str_contains(implode("\n", $unknownOutput), 'Unknown installer option'), 'An unknown installer option was not rejected.');

$duplicateOutput = [];
$duplicateExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --origin=https://example.test --origin=https://example.test 2>&1',
    $duplicateOutput,
    $duplicateExit);
installerAssert($duplicateExit !== 0 && str_contains(implode("\n", $duplicateOutput), 'Duplicate installer option'), 'A duplicate installer option was not rejected.');

$buildScript = $root . '/build-package.ps1';
$buildOutputDirectory = sys_get_temp_dir() . '/pa-online-installer-build-' . bin2hex(random_bytes(6));
$buildOutput = [];
$buildExit = 0;
exec(
    'powershell.exe -NoProfile -ExecutionPolicy Bypass -File ' . escapeshellarg($buildScript)
        . ' -OutputDirectory ' . escapeshellarg($buildOutputDirectory) . ' 2>&1',
    $buildOutput,
    $buildExit);
installerAssert($buildExit === 0, 'The installer package build failed: ' . implode("\n", $buildOutput));
$archives = glob($buildOutputDirectory . '/player-assistant-web-payload-*.tar') ?: [];
installerAssert(count($archives) === 1, 'The package builder did not create exactly one payload archive.');
installerAssert(is_file($buildOutputDirectory . '/install-player-assistant-web.php'), 'The distributable installer was not copied beside the payload.');
installerAssert(is_file($archives[0] . '.sha256'), 'The payload archive checksum file is missing.');
$archiveHash = strtolower(hash_file('sha256', $archives[0]));
installerAssert(
    trim((string)file_get_contents($archives[0] . '.sha256')) === $archiveHash . '  ' . basename($archives[0]),
    'The payload archive checksum file is invalid.');

$fixtureRoot = sys_get_temp_dir() . '/pa-online-installer-fixture-' . bin2hex(random_bytes(6));
$accountHome = $fixtureRoot . '/account';
$publicRoot = $accountHome . '/example.test';
$privateRoot = $accountHome . '/player-assistant-broker-example-test';
$configSource = $fixtureRoot . '/config.php';
mkdir($publicRoot, 0700, true);
$config = (string)file_get_contents($root . '/config.template.php');
$config = str_replace(
    [
        'CHANGE_ME_RANDOM_ADMIN_KEY_AT_LEAST_32_CHARACTERS',
        'CHANGE_ME_BASE64_32_BYTE_SIGNING_KEY',
        'CHANGE_ME_RANDOM_AUDIT_HASH_KEY',
        'CHANGE_ME_RPOL_USERNAME',
        'CHANGE_ME_RPOL_PASSWORD',
        'CHANGE_ME_RPOL_GAME_ID',
        'CHANGE_ME_HTTPS_XP_TRACKING_URL',
        'CHANGE_ME_HTTPS_CHARACTER_LISTING_URL',
        'CHANGE_ME_HTTPS_CLASS_PROGRESSION_URL',
        'CHANGE_ME_HTTPS_WORD_COUNT_SOURCE_URL',
        'CHANGE_ME_WORD_COUNT_KEY_ID',
        'CHANGE_ME_BASE64_ED25519_PUBLIC_KEY',
        'CHANGE_ME_ALERT_RECIPIENT',
        'CHANGE_ME_SENDER_ON_TARGET_DOMAIN',
        'CHANGE_ME_MONITOR_CHARACTER_NAME',
        'CHANGE_ME_MONITOR_CHARACTER_PASSWORD',
    ],
    [
        str_repeat('a', 48),
        base64_encode(str_repeat('s', 32)),
        str_repeat('h', 48),
        'fixture-user',
        'fixture-password',
        '80170',
        'https://publish.obsidian.md/example/XP',
        'https://publish.obsidian.md/example/PCs/Player+Characters+Listing',
        'https://publish.obsidian.md/example/Classes/Class+Level+Progression',
        'https://example-source.test/word-counts.json',
        'fixture-word-count-key',
        base64_encode(str_repeat('p', 32)),
        'alerts@example.test',
        'player-assistant@example.test',
        'Monitor Character',
        'fixture-monitor-password',
    ],
    $config);
file_put_contents($configSource, $config, LOCK_EX);

$lockPath = $accountHome . '/.player-assistant-installer.lock';
$lockHandle = fopen($lockPath, 'c+');
installerAssert($lockHandle !== false && flock($lockHandle, LOCK_EX | LOCK_NB), 'Unable to create the concurrent-install lock fixture.');
$lockOutput = [];
$lockExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --package=' . escapeshellarg($archives[0])
        . ' --origin=https://example.test'
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' --config-source=' . escapeshellarg($configSource)
        . ' --verification=local --skip-cron 2>&1',
    $lockOutput,
    $lockExit);
installerAssert($lockExit !== 0, 'A concurrent installer invocation acquired an already-held target lock.');
installerAssert(
    str_contains(implode("\n", $lockOutput), 'installer operation is already running'),
    'Concurrent installer lock rejection was not explicit.');
flock($lockHandle, LOCK_UN);
fclose($lockHandle);

$aliasTarget = $fixtureRoot . '/aliased-scarlet-root';
mkdir($aliasTarget, 0700, true);
$scarletAlias = $publicRoot . '/scarlethorizons';
if (@symlink($aliasTarget, $scarletAlias)) {
    $aliasOutput = [];
    $aliasExit = 0;
    exec(
        escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
            . ' --package=' . escapeshellarg($archives[0])
            . ' --origin=https://example.test'
            . ' --public-root=' . escapeshellarg($publicRoot)
            . ' --private-root=' . escapeshellarg($privateRoot)
            . ' --config-source=' . escapeshellarg($configSource)
            . ' --verification=local --skip-cron 2>&1',
        $aliasOutput,
        $aliasExit);
    installerAssert($aliasExit !== 0, 'A symbolic-link scarlethorizons ancestor was accepted.');
    installerAssert(
        str_contains(implode("\n", $aliasOutput), 'cannot traverse symbolic links'),
        'Ancestor symbolic-link rejection was not explicit.');
    installerRemoveLink($scarletAlias);
}

$tamperedPackage = $buildOutputDirectory . '/tampered-payload.tar';
copy($archives[0], $tamperedPackage);
$tamperedHandle = fopen($tamperedPackage, 'r+b');
fseek($tamperedHandle, 1024);
$originalByte = fread($tamperedHandle, 1);
fseek($tamperedHandle, 1024);
fwrite($tamperedHandle, $originalByte === "X" ? "Y" : "X");
fclose($tamperedHandle);
file_put_contents($tamperedPackage . '.sha256', $archiveHash . '  ' . basename($tamperedPackage) . "\n");
$tamperOutput = [];
$tamperExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --package=' . escapeshellarg($tamperedPackage)
        . ' --origin=https://example.test'
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' --config-source=' . escapeshellarg($configSource)
        . ' --verification=local --skip-cron 2>&1',
    $tamperOutput,
    $tamperExit);
installerAssert($tamperExit !== 0, 'A payload with a mismatched archive checksum was accepted.');
installerAssert(str_contains(implode("\n", $tamperOutput), 'checksum is invalid'), 'Checksum rejection was not explicit.');

$placeholderOutput = [];
$placeholderExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --package=' . escapeshellarg($archives[0])
        . ' --origin=https://example.test'
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' --config-source=' . escapeshellarg($root . '/config.template.php')
        . ' --verification=local --skip-cron 2>&1',
    $placeholderOutput,
    $placeholderExit);
installerAssert($placeholderExit !== 0, 'A private configuration with unresolved placeholders was accepted.');
installerAssert(str_contains(implode("\n", $placeholderOutput), 'unresolved placeholders'), 'Placeholder rejection was not explicit.');

$undeclaredPackage = $buildOutputDirectory . '/undeclared-payload.tar';
copy($archives[0], $undeclaredPackage);
$undeclaredArchive = new PharData($undeclaredPackage);
$undeclaredArchive->addFromString('payload/public/scarlethorizons/pwa/undeclared.txt', 'not declared');
$undeclaredArchive = null;
$undeclaredHash = hash_file('sha256', $undeclaredPackage);
file_put_contents($undeclaredPackage . '.sha256', $undeclaredHash . '  ' . basename($undeclaredPackage) . "\n");
$undeclaredOutput = [];
$undeclaredExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --package=' . escapeshellarg($undeclaredPackage)
        . ' --origin=https://example.test'
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' --config-source=' . escapeshellarg($configSource)
        . ' --verification=local --skip-cron 2>&1',
    $undeclaredOutput,
    $undeclaredExit);
installerAssert($undeclaredExit !== 0, 'An undeclared archive entry was accepted.');
installerAssert(str_contains(implode("\n", $undeclaredOutput), 'undeclared or missing files'), 'Undeclared-entry rejection was not explicit.');

$failingMigrationPackage = $buildOutputDirectory . '/failing-migration-payload.tar';
copy($archives[0], $failingMigrationPackage);
$failingArchive = new PharData($failingMigrationPackage);
$failingManifest = json_decode($failingArchive['manifest.json']->getContent(), true, 32, JSON_THROW_ON_ERROR);
$failingMigration = "<?php\ndeclare(strict_types=1);\nfwrite(STDERR, 'forced migration failure');\nexit(41);\n";
$failingMigrationPath = 'payload/private/migrate-broker.php';
$failingArchive->addFromString($failingMigrationPath, $failingMigration);
foreach ($failingManifest['files'] as &$failingEntry) {
    if ($failingEntry['path'] === $failingMigrationPath) {
        $failingEntry['sha256'] = hash('sha256', $failingMigration);
        $failingEntry['bytes'] = strlen($failingMigration);
    }
}
unset($failingEntry);
$failingArchive->addFromString(
    'manifest.json',
    json_encode($failingManifest, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR));
$failingArchive = null;
$failingMigrationHash = hash_file('sha256', $failingMigrationPackage);
file_put_contents(
    $failingMigrationPackage . '.sha256',
    $failingMigrationHash . '  ' . basename($failingMigrationPackage) . "\n");

$installOutput = [];
$installExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --package=' . escapeshellarg($archives[0])
        . ' --origin=' . escapeshellarg('https://example.test')
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' --config-source=' . escapeshellarg($configSource)
        . ' --verification=local --skip-cron 2>&1',
    $installOutput,
    $installExit);
installerAssert($installExit === 0, 'The local fixture installation failed: ' . implode("\n", $installOutput));
$installResult = json_decode((string)end($installOutput), true, 16, JSON_THROW_ON_ERROR);
installerAssert($installResult['status'] === 'installed_pending_https_verification', 'The local-only install returned the wrong status.');
installerAssert(is_file($installResult['report_path'] ?? ''), 'The machine-readable installation report is missing.');
$pendingReport = json_decode((string)file_get_contents($installResult['report_path']), true, 32, JSON_THROW_ON_ERROR);
installerAssert($pendingReport['migration_version'] === 4, 'The installation report omitted the migrated schema version.');
installerAssert(($pendingReport['verification']['local'] ?? null) === true, 'The installation report omitted local verification success.');
installerAssert(count($pendingReport['promoted_file_sha256'] ?? []) > 30, 'The installation report omitted promoted file hashes.');
installerAssert(is_file($publicRoot . '/scarlethorizons/pwa/index.html'), 'The complete PWA was not promoted.');
installerAssert(is_file($publicRoot . '/scarlethorizons/api/.htaccess'), 'The API rewrite configuration was not promoted.');
installerAssert(is_file($publicRoot . '/scarlethorizons/api/index.php'), 'The API entry point was not materialized.');
$installedApi = (string)file_get_contents($publicRoot . '/scarlethorizons/api/index.php');
installerAssert(!str_contains($installedApi, '__PLAYER_ASSISTANT_PRIVATE_ROOT__'), 'The API private-root placeholder was not replaced.');
installerAssert(str_contains($installedApi, var_export(str_replace('\\', '/', $privateRoot), true)), 'The API entry point contains the wrong private root.');
installerAssert(is_file($privateRoot . '/BrokerService.php'), 'The private broker runtime was not promoted.');
installerAssert(is_file($privateRoot . '/config.php'), 'The private configuration was not installed.');
installerAssert(is_file($privateRoot . '/broker.sqlite'), 'The migrated broker database was not created.');
$installedDatabase = new PDO('sqlite:' . $privateRoot . '/broker.sqlite');
installerAssert((int)$installedDatabase->query('PRAGMA user_version')->fetchColumn() === 4, 'The installed broker database has the wrong schema version.');
$installedDatabase = null;
installerAssert(is_string($installResult['transaction_id'] ?? null), 'The pending install did not return a transaction ID.');
$transactionManifestPath = $accountHome . '/.player-assistant-installer-transactions/' . $installResult['transaction_id'] . '/manifest.json';
installerAssert(is_file($transactionManifestPath), 'The pending rollback transaction was not preserved.');

$lockedOutput = [];
$lockedExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --package=' . escapeshellarg($archives[0])
        . ' --origin=https://example.test'
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' --config-source=' . escapeshellarg($configSource)
        . ' --verification=local --skip-cron 2>&1',
    $lockedOutput,
    $lockedExit);
installerAssert($lockedExit !== 0, 'A second installation was allowed while a transaction was pending.');
installerAssert(str_contains(implode("\n", $lockedOutput), 'unresolved installer transaction'), 'The pending-transaction rejection was not explicit.');

$privateAliasTarget = $accountHome . '/private-root-before-alias';
if (@rename($privateRoot, $privateAliasTarget) && @symlink($privateAliasTarget, $privateRoot)) {
    $transactionAliasOutput = [];
    $transactionAliasExit = 0;
    exec(
        escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
            . ' --rollback-transaction=' . escapeshellarg($installResult['transaction_id'])
            . ' --origin=' . escapeshellarg('https://example.test')
            . ' --public-root=' . escapeshellarg($publicRoot)
            . ' --private-root=' . escapeshellarg($privateRoot)
            . ' 2>&1',
        $transactionAliasOutput,
        $transactionAliasExit);
    installerAssert($transactionAliasExit !== 0, 'A transaction action accepted a symbolic-link private root.');
    installerAssert(
        str_contains(implode("\n", $transactionAliasOutput), 'cannot traverse symbolic links'),
        'Transaction-action symbolic-link rejection was not explicit.');
    installerRemoveLink($privateRoot);
    rename($privateAliasTarget, $privateRoot);
} elseif (is_dir($privateAliasTarget) && !is_dir($privateRoot)) {
    rename($privateAliasTarget, $privateRoot);
}

$interruptedManifestPath = $installResult['transaction_directory'] . '/manifest.json';
$interruptedManifest = json_decode((string)file_get_contents($interruptedManifestPath), true, 32, JSON_THROW_ON_ERROR);
$interruptedManifest['status'] = 'promoted';
file_put_contents(
    $interruptedManifestPath,
    json_encode($interruptedManifest, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR),
    LOCK_EX);

$rollbackOutput = [];
$rollbackExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --rollback-transaction=' . escapeshellarg($installResult['transaction_id'])
        . ' --origin=' . escapeshellarg('https://example.test')
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' 2>&1',
    $rollbackOutput,
    $rollbackExit);
installerAssert($rollbackExit === 0, 'The pending installation could not be rolled back: ' . implode("\n", $rollbackOutput));
$rollbackResult = json_decode((string)end($rollbackOutput), true, 16, JSON_THROW_ON_ERROR);
installerAssert($rollbackResult['status'] === 'rolled_back', 'The rollback command returned the wrong status.');
installerAssert(!file_exists($publicRoot . '/scarlethorizons/pwa'), 'Rollback left the newly installed PWA active.');
installerAssert(!file_exists($publicRoot . '/scarlethorizons/api'), 'Rollback left the newly installed API active.');
installerAssert(
    !is_file($privateRoot . '/BrokerService.php')
        && !is_file($privateRoot . '/config.php')
        && !is_file($privateRoot . '/broker.sqlite'),
    'Rollback left the newly installed private runtime behind.');
$rolledBackManifest = json_decode((string)file_get_contents($transactionManifestPath), true, 16, JSON_THROW_ON_ERROR);
installerAssert($rolledBackManifest['status'] === 'rolled_back', 'Rollback evidence was not recorded.');
$rolledBackReport = json_decode((string)file_get_contents($installResult['report_path']), true, 32, JSON_THROW_ON_ERROR);
installerAssert($rolledBackReport['status'] === 'rolled_back', 'The installation report was not updated after rollback.');
installerAssert($rolledBackReport['cleanup_complete'] === true, 'The installation report did not confirm rollback cleanup.');

$rolledBackManifest['status'] = 'rollback_cleanup';
$rolledBackManifest['cleanup_complete'] = false;
file_put_contents(
    $transactionManifestPath,
    json_encode($rolledBackManifest, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR),
    LOCK_EX);
$retryRollbackOutput = [];
$retryRollbackExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --rollback-transaction=' . escapeshellarg($installResult['transaction_id'])
        . ' --origin=' . escapeshellarg('https://example.test')
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' 2>&1',
    $retryRollbackOutput,
    $retryRollbackExit);
installerAssert($retryRollbackExit === 0, 'Rollback cleanup was not restartable: ' . implode("\n", $retryRollbackOutput));
$retriedManifest = json_decode((string)file_get_contents($transactionManifestPath), true, 16, JSON_THROW_ON_ERROR);
installerAssert($retriedManifest['status'] === 'rolled_back', 'Retried rollback cleanup did not complete.');

$baselineOutput = [];
$baselineExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --package=' . escapeshellarg($archives[0])
        . ' --origin=https://example.test'
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' --config-source=' . escapeshellarg($configSource)
        . ' --verification=local --skip-cron 2>&1',
    $baselineOutput,
    $baselineExit);
installerAssert($baselineExit === 0, 'The upgrade baseline installation failed: ' . implode("\n", $baselineOutput));
$baselineResult = json_decode((string)end($baselineOutput), true, 16, JSON_THROW_ON_ERROR);
$baselineManifestPath = $baselineResult['transaction_directory'] . '/manifest.json';
$baselinePackageManifestPath = $baselineResult['transaction_directory'] . '/package-manifest.json';
$baselineManifest = json_decode((string)file_get_contents($baselineManifestPath), true, 32, JSON_THROW_ON_ERROR);
$validBaselinePackageManifest = json_decode(
    (string)file_get_contents($baselinePackageManifestPath),
    true,
    32,
    JSON_THROW_ON_ERROR);
$baselineManifest['status'] = 'finalize_cleanup';
$baselineManifest['package_manifest'] = [
    'schema_version' => 1,
    'product' => 'player-assistant-web',
    'version' => $validBaselinePackageManifest['version'],
    'fixed_url_layout' => ['pwa' => '/scarlethorizons/pwa/', 'api' => '/scarlethorizons/api/'],
    'files' => [],
];
file_put_contents(
    $baselineManifestPath,
    json_encode($baselineManifest, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR),
    LOCK_EX);
$invalidFinalizeOutput = [];
$invalidFinalizeExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --finalize-transaction=' . escapeshellarg($baselineResult['transaction_id'])
        . ' --origin=' . escapeshellarg('https://example.test')
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' 2>&1',
    $invalidFinalizeOutput,
    $invalidFinalizeExit);
installerAssert($invalidFinalizeExit !== 0, 'Finalization accepted an empty persisted package manifest.');
installerAssert(
    str_contains(implode("\n", $invalidFinalizeOutput), 'package manifest contract is invalid'),
    'The invalid persisted package-manifest rejection was not explicit.');
$baselineManifest['status'] = 'finalize_cleanup';
$baselineManifest['package_manifest'] = $validBaselinePackageManifest;
file_put_contents(
    $baselineManifestPath,
    json_encode($baselineManifest, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR),
    LOCK_EX);
$cleanupFaultOutput = [];
$cleanupFaultExit = 0;
putenv('PLAYER_ASSISTANT_TEST_FINALIZE_AFTER_DURABLE_STATE=1');
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --finalize-transaction=' . escapeshellarg($baselineResult['transaction_id'])
        . ' --origin=' . escapeshellarg('https://example.test')
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' 2>&1',
    $cleanupFaultOutput,
    $cleanupFaultExit);
putenv('PLAYER_ASSISTANT_TEST_FINALIZE_AFTER_DURABLE_STATE');
installerAssert($cleanupFaultExit !== 0, 'The finalization durable-state interruption fixture did not fail.');
installerAssert(is_file($baselineManifestPath), 'The durable finalized-state interruption did not preserve its transaction manifest.');
$cleanupFaultManifest = json_decode((string)file_get_contents($baselineManifestPath), true, 32, JSON_THROW_ON_ERROR);
installerAssert($cleanupFaultManifest['status'] === 'finalized', 'The durable finalized-state interruption lost its finalized state.');
installerAssert($cleanupFaultManifest['rollback_forbidden'] === true && $cleanupFaultManifest['cleanup_complete'] === false, 'The durable finalized-state interruption did not preserve rollback-forbidden cleanup state.');
$finalizeCleanupOutput = [];
$finalizeCleanupExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --finalize-transaction=' . escapeshellarg($baselineResult['transaction_id'])
        . ' --origin=' . escapeshellarg('https://example.test')
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' 2>&1',
    $finalizeCleanupOutput,
    $finalizeCleanupExit);
installerAssert($finalizeCleanupExit === 0, 'Finalization cleanup was not restartable: ' . implode("\n", $finalizeCleanupOutput));
installerAssert(is_file($baselineManifestPath), 'Finalization cleanup removed the durable finalized-state manifest.');
$finalizedManifest = json_decode((string)file_get_contents($baselineManifestPath), true, 32, JSON_THROW_ON_ERROR);
installerAssert($finalizedManifest['status'] === 'finalized', 'Finalization cleanup did not persist finalized state.');
installerAssert($finalizedManifest['rollback_forbidden'] === true && $finalizedManifest['cleanup_complete'] === true, 'Finalization cleanup did not persist rollback-forbidden completion.');
$rollbackFinalizedOutput = [];
$rollbackFinalizedExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --rollback-transaction=' . escapeshellarg($baselineResult['transaction_id'])
        . ' --origin=https://example.test'
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' 2>&1',
    $rollbackFinalizedOutput,
    $rollbackFinalizedExit);
installerAssert($rollbackFinalizedExit !== 0, 'A finalized transaction accepted rollback.');
installerAssert(str_contains(implode("\n", $rollbackFinalizedOutput), 'rollback is forbidden'), 'Finalized rollback rejection was not explicit.');
$finalizedReport = json_decode((string)file_get_contents($baselineResult['report_path']), true, 32, JSON_THROW_ON_ERROR);
installerAssert($finalizedReport['status'] === 'finalized', 'Retried finalization cleanup did not update its report.');

$privateRuntimeTarget = $privateRoot . '/BrokerService.php';
$privateRuntimeAliasTarget = $privateRoot . '/BrokerService.php.before-alias';
if (@rename($privateRuntimeTarget, $privateRuntimeAliasTarget)
    && @symlink($privateRuntimeAliasTarget, $privateRuntimeTarget)) {
    $privateRuntimeAliasOutput = [];
    $privateRuntimeAliasExit = 0;
    exec(
        escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
            . ' --package=' . escapeshellarg($archives[0])
            . ' --origin=https://example.test'
            . ' --public-root=' . escapeshellarg($publicRoot)
            . ' --private-root=' . escapeshellarg($privateRoot)
            . ' --config-source=' . escapeshellarg($configSource)
            . ' --verification=local --skip-cron 2>&1',
        $privateRuntimeAliasOutput,
        $privateRuntimeAliasExit);
    installerAssert($privateRuntimeAliasExit !== 0, 'An upgrade accepted a symbolic-link private-runtime target.');
    installerAssert(
        str_contains(implode("\n", $privateRuntimeAliasOutput), 'private runtime target cannot be a symbolic link'),
        'Private-runtime symbolic-link rejection was not explicit.');
    installerRemoveLink($privateRuntimeTarget);
    rename($privateRuntimeAliasTarget, $privateRuntimeTarget);
} elseif (is_file($privateRuntimeAliasTarget) && !is_file($privateRuntimeTarget)) {
    rename($privateRuntimeAliasTarget, $privateRuntimeTarget);
}

$malformedId = '20000101T000000Z-deadbea0';
$malformedDirectory = $accountHome . '/.player-assistant-installer-transactions/' . $malformedId;
mkdir($malformedDirectory, 0700, true);
$malformedManifest = [
    'schema_version' => 1,
    'transaction_id' => $malformedId,
    'transaction_directory' => str_replace('\\', '/', $malformedDirectory),
    'report_path' => str_replace('\\', '/', $accountHome . '/.player-assistant-install-reports/' . $malformedId . '.json'),
    'status' => 'preparing',
    'origin' => 'https://example.test',
    'public_root' => str_replace('\\', '/', $publicRoot),
    'private_root' => str_replace('\\', '/', $privateRoot),
    'private_root_existed' => true,
    'pwa_existed' => true,
    'api_existed' => true,
    'pwa_promoted' => false,
    'api_promoted' => false,
    'pwa_promotion_started' => false,
    'api_promotion_started' => false,
    'pwa_backup_moved' => false,
    'api_backup_moved' => false,
    'pwa_backup_move_started' => false,
    'api_backup_move_started' => false,
    'api_maintenance_active' => false,
    'config_existed' => true,
    'config_promoted' => false,
    'config_promotion_started' => false,
    'database_existed' => true,
    'database_mutation_started' => false,
    'private_files' => ['../outside-private-root' => false],
    'private_promoted_files' => [],
    'private_file_in_progress' => null,
    'cron' => ['managed' => false, 'original_existed' => false],
];
file_put_contents(
    $malformedDirectory . '/manifest.json',
    json_encode($malformedManifest, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR),
    LOCK_EX);
$malformedOutput = [];
$malformedExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --rollback-transaction=' . escapeshellarg($malformedId)
        . ' --origin=' . escapeshellarg('https://example.test')
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' 2>&1',
    $malformedOutput,
    $malformedExit);
installerAssert($malformedExit !== 0, 'A transaction manifest containing an unsafe private-runtime path was accepted.');
installerAssert(
    str_contains(implode("\n", $malformedOutput), 'invalid private-runtime path'),
    'The malformed transaction rejection was not explicit.');
installerRemoveTree($malformedDirectory);

$missingStateId = '20000101T000000Z-deadbea1';
$missingStateDirectory = $accountHome . '/.player-assistant-installer-transactions/' . $missingStateId;
mkdir($missingStateDirectory, 0700, true);
$missingStateManifest = $malformedManifest;
$missingStateManifest['transaction_id'] = $missingStateId;
$missingStateManifest['transaction_directory'] = str_replace('\\', '/', $missingStateDirectory);
$missingStateManifest['report_path'] = str_replace('\\', '/', $accountHome . '/.player-assistant-install-reports/' . $missingStateId . '.json');
$missingStateManifest['private_files'] = [];
unset($missingStateManifest['pwa_existed']);
file_put_contents(
    $missingStateDirectory . '/manifest.json',
    json_encode($missingStateManifest, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR),
    LOCK_EX);
$missingStateOutput = [];
$missingStateExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --rollback-transaction=' . escapeshellarg($missingStateId)
        . ' --origin=' . escapeshellarg('https://example.test')
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' 2>&1',
    $missingStateOutput,
    $missingStateExit);
installerAssert($missingStateExit !== 0, 'A transaction manifest missing destructive state flags was accepted.');
installerAssert(
    str_contains(implode("\n", $missingStateOutput), 'invalid destructive state'),
    'The missing transaction-state rejection was not explicit.');
installerRemoveTree($missingStateDirectory);

$walDatabase = new PDO('sqlite:' . $privateRoot . '/broker.sqlite', null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
$walDatabase->exec('PRAGMA journal_mode=WAL');
$walDatabase->exec('CREATE TABLE IF NOT EXISTS installer_wal_probe (value TEXT NOT NULL)');
$walDatabase->exec("INSERT INTO installer_wal_probe(value) VALUES ('committed-wal-state')");
installerAssert(is_file($privateRoot . '/broker.sqlite-wal'), 'The WAL rollback fixture was not created.');
$preMutationId = '20000101T000000Z-deadbeef';
$preMutationDirectory = $accountHome . '/.player-assistant-installer-transactions/' . $preMutationId;
mkdir($preMutationDirectory, 0700, true);
$preMutationManifest = [
    'schema_version' => 1,
    'transaction_id' => $preMutationId,
    'transaction_directory' => str_replace('\\', '/', $preMutationDirectory),
    'report_path' => str_replace('\\', '/', $accountHome . '/.player-assistant-install-reports/' . $preMutationId . '.json'),
    'status' => 'preparing',
    'origin' => 'https://example.test',
    'public_root' => str_replace('\\', '/', $publicRoot),
    'private_root' => str_replace('\\', '/', $privateRoot),
    'private_root_existed' => true,
    'pwa_existed' => true,
    'api_existed' => true,
    'pwa_promoted' => false,
    'api_promoted' => false,
    'pwa_promotion_started' => false,
    'api_promotion_started' => false,
    'pwa_backup_moved' => false,
    'api_backup_moved' => false,
    'pwa_backup_move_started' => false,
    'api_backup_move_started' => false,
    'api_maintenance_active' => false,
    'config_existed' => true,
    'config_promoted' => false,
    'config_promotion_started' => false,
    'database_existed' => true,
    'database_mutation_started' => false,
    'private_files' => [],
    'private_promoted_files' => [],
    'private_file_in_progress' => null,
    'cron' => ['managed' => false, 'original_existed' => false],
];
file_put_contents(
    $preMutationDirectory . '/manifest.json',
    json_encode($preMutationManifest, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR),
    LOCK_EX);
$preMutationOutput = [];
$preMutationExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --rollback-transaction=' . escapeshellarg($preMutationId)
        . ' --origin=' . escapeshellarg('https://example.test')
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' 2>&1',
    $preMutationOutput,
    $preMutationExit);
installerAssert($preMutationExit === 0, 'Pre-migration rollback failed: ' . implode("\n", $preMutationOutput));
installerAssert(
    (int)$walDatabase->query("SELECT COUNT(*) FROM installer_wal_probe WHERE value = 'committed-wal-state'")->fetchColumn() === 1,
    'Pre-migration rollback discarded committed WAL state.');
$walDatabase = null;

file_put_contents($publicRoot . '/scarlethorizons/pwa/index.html', 'legacy-pwa', LOCK_EX);
file_put_contents($publicRoot . '/scarlethorizons/api/index.php', "<?php\n// legacy-api\n", LOCK_EX);
file_put_contents($privateRoot . '/BrokerService.php', "<?php\n// legacy-broker\n", LOCK_EX);

$forcedFailureOutput = [];
$forcedFailureExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --package=' . escapeshellarg($failingMigrationPackage)
        . ' --origin=https://example.test'
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' --config-source=' . escapeshellarg($configSource)
        . ' --verification=local --skip-cron 2>&1',
    $forcedFailureOutput,
    $forcedFailureExit);
installerAssert($forcedFailureExit !== 0, 'A forced migration failure unexpectedly succeeded.');
installerAssert(str_contains(implode("\n", $forcedFailureOutput), 'Broker database migration failed'), 'The forced migration failure was not explicit.');
installerAssert((string)file_get_contents($publicRoot . '/scarlethorizons/pwa/index.html') === 'legacy-pwa', 'Migration-failure rollback changed the prior PWA.');
installerAssert(str_contains((string)file_get_contents($publicRoot . '/scarlethorizons/api/index.php'), 'legacy-api'), 'Migration-failure rollback did not restore the prior API after maintenance mode.');
installerAssert(str_contains((string)file_get_contents($privateRoot . '/BrokerService.php'), 'legacy-broker'), 'Migration-failure rollback changed private runtime code.');
$failureDatabase = new PDO('sqlite:' . $privateRoot . '/broker.sqlite');
installerAssert((string)$failureDatabase->query('PRAGMA integrity_check')->fetchColumn() === 'ok', 'Migration-failure rollback damaged the database.');
installerAssert((int)$failureDatabase->query('PRAGMA user_version')->fetchColumn() === 4, 'Migration-failure rollback restored the wrong database version.');
installerAssert(strtolower((string)$failureDatabase->query('PRAGMA journal_mode')->fetchColumn()) === 'wal', 'Migration-failure rollback did not restore WAL journal mode.');
$failureDatabase = null;

$upgradeOutput = [];
$upgradeExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --package=' . escapeshellarg($archives[0])
        . ' --origin=https://example.test'
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' --config-source=' . escapeshellarg($configSource)
        . ' --verification=local --skip-cron 2>&1',
    $upgradeOutput,
    $upgradeExit);
installerAssert($upgradeExit === 0, 'The upgrade fixture failed: ' . implode("\n", $upgradeOutput));
$upgradeResult = json_decode((string)end($upgradeOutput), true, 16, JSON_THROW_ON_ERROR);
installerAssert((string)file_get_contents($publicRoot . '/scarlethorizons/pwa/index.html') !== 'legacy-pwa', 'Upgrade did not replace the PWA release.');
installerAssert(!str_contains((string)file_get_contents($privateRoot . '/BrokerService.php'), 'legacy-broker'), 'Upgrade did not replace private runtime code.');

$upgradeRollbackOutput = [];
$upgradeRollbackExit = 0;
exec(
    escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installer)
        . ' --rollback-transaction=' . escapeshellarg($upgradeResult['transaction_id'])
        . ' --origin=https://example.test'
        . ' --public-root=' . escapeshellarg($publicRoot)
        . ' --private-root=' . escapeshellarg($privateRoot)
        . ' 2>&1',
    $upgradeRollbackOutput,
    $upgradeRollbackExit);
installerAssert($upgradeRollbackExit === 0, 'The upgrade rollback failed: ' . implode("\n", $upgradeRollbackOutput));
installerAssert((string)file_get_contents($publicRoot . '/scarlethorizons/pwa/index.html') === 'legacy-pwa', 'Upgrade rollback did not restore the previous PWA release.');
installerAssert(str_contains((string)file_get_contents($publicRoot . '/scarlethorizons/api/index.php'), 'legacy-api'), 'Upgrade rollback did not restore the previous API release.');
installerAssert(str_contains((string)file_get_contents($privateRoot . '/BrokerService.php'), 'legacy-broker'), 'Upgrade rollback did not restore the previous private runtime.');
$restoredDatabase = new PDO('sqlite:' . $privateRoot . '/broker.sqlite');
installerAssert((string)$restoredDatabase->query('PRAGMA integrity_check')->fetchColumn() === 'ok', 'Upgrade rollback damaged the broker database.');
installerAssert(strtolower((string)$restoredDatabase->query('PRAGMA journal_mode')->fetchColumn()) === 'wal', 'Upgrade rollback did not restore WAL journal mode.');
$restoredDatabase = null;

installerRemoveTree($fixtureRoot);
installerRemoveTree($buildOutputDirectory);

fwrite(STDOUT, "Online PWA installer package, install, migration, and rollback contracts passed.\n");
