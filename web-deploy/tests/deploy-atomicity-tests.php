<?php

declare(strict_types=1);

function deployAtomicityAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

function removeDeploymentFixture(string $path): void
{
    if (!is_dir($path)) {
        @unlink($path);
        return;
    }
    foreach (array_diff(scandir($path) ?: [], ['.', '..']) as $entry) {
        removeDeploymentFixture($path . DIRECTORY_SEPARATOR . $entry);
    }
    @rmdir($path);
}

$deploymentScript = (string)file_get_contents(__DIR__ . '/../deploy-word-count-refresh.ps1');
$deploymentVerifier = (string)file_get_contents(__DIR__ . '/../test-word-count-refresh-deployment.ps1');
deployAtomicityAssert(
    str_contains($deploymentScript, '[IO.File]::WriteAllText($localScript, "<?php`n" + $Code')
        && str_contains($deploymentVerifier, '[IO.File]::WriteAllText($localScript, "<?php`n" + $Code'),
    'Remote PHP transaction scripts must include an opening PHP tag.');
deployAtomicityAssert(
    !preg_match('/^\s+[.]Replace\(/m', $deploymentVerifier),
    'The production verifier must remain callable under inherited strict mode.');
deployAtomicityAssert(
    str_contains($deploymentScript, "if (\$PrivateDirectory -cne '/home/dh_4gg2za/player-assistant-broker')")
        && str_contains($deploymentScript, "if (\$PublicApiPath -cne '/home/dh_4gg2za/bryanmiller.us/scarlethorizons/api/index.php')"),
    'Deployment paths must be pinned to the approved private root and public API target.');
deployAtomicityAssert(
    str_contains($deploymentScript, 'Invoke-RemotePhp $installCode -Attempts 1')
        && str_contains($deploymentScript, 'Invoke-RemotePhp $publicInstallCode -Attempts 1')
        && str_contains($deploymentScript, 'Invoke-RemotePhp $cronCode -Attempts 1'),
    'Mutating remote transactions must not be retried after ambiguous completion.');
deployAtomicityAssert(
    str_contains($deploymentScript, "if (is_file(\$rollbackDirectory . '/manifest.json'))"),
    'The private installer must preserve a write-once rollback snapshot.');
$installerPattern = <<<'REGEX'
~\$installCode = @'\R(.*?)\R'@\.Replace\('__INSTALL_DATA__'~s
REGEX;
deployAtomicityAssert(
    preg_match($installerPattern, $deploymentScript, $matches) === 1,
    'Unable to extract the inline broker installer.');

$root = sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'pa-deploy-atomicity-' . bin2hex(random_bytes(6));
$stage = $root . DIRECTORY_SEPARATOR . '.word-count-deploy-fixture';
mkdir($stage, 0700, true);
file_put_contents($root . DIRECTORY_SEPARATOR . 'RevisionService.php', "<?php\n// original revision service\n");
file_put_contents($root . DIRECTORY_SEPARATOR . 'config.php', "<?php\nreturn [];\n");
file_put_contents($stage . DIRECTORY_SEPARATOR . 'RevisionService.php', "<?php\n// candidate revision service\n");
file_put_contents($stage . DIRECTORY_SEPARATOR . 'BrokerService.php', "<?php\n// candidate broker service\n");
mkdir($root . DIRECTORY_SEPARATOR . 'BrokerService.php', 0700);
file_put_contents($root . DIRECTORY_SEPARATOR . 'BrokerService.php' . DIRECTORY_SEPARATOR . 'blocker', 'block replacement');

$data = [
    'private_directory' => $root,
    'source_url' => 'https://example.invalid/word-counts.json',
    'status_path' => $root . DIRECTORY_SEPARATOR . 'status.json',
    'key_id' => 'fixture-key',
    'public_key' => 'fixture-public-key',
    'keep_backups' => 5,
    'deploy_id' => 'fixture',
    'rollback_directory' => $root . DIRECTORY_SEPARATOR . '.word-count-rollback-fixture',
    'files' => [
        'RevisionService.php' => $stage . DIRECTORY_SEPARATOR . 'RevisionService.php',
        'BrokerService.php' => $stage . DIRECTORY_SEPARATOR . 'BrokerService.php',
    ],
];
$payload = base64_encode(json_encode($data, JSON_THROW_ON_ERROR));
$installer = str_replace(
    ["__INSTALL_DATA__", '/usr/bin/php -l '],
    [$payload, escapeshellarg(PHP_BINARY) . ' -l '],
    $matches[1]);
$installerPath = $root . DIRECTORY_SEPARATOR . 'installer.php';
file_put_contents($installerPath, "<?php\n" . $installer);

try {
    $output = [];
    $exitCode = 0;
    exec(escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installerPath) . ' 2>&1', $output, $exitCode);
    deployAtomicityAssert($exitCode !== 0, 'The forced partial deployment unexpectedly succeeded.');
    deployAtomicityAssert(
        file_get_contents($root . DIRECTORY_SEPARATOR . 'RevisionService.php') === "<?php\n// original revision service\n",
        'A failed later promotion did not restore the previously replaced RevisionService.');
    deployAtomicityAssert(
        is_dir($root . DIRECTORY_SEPARATOR . 'BrokerService.php'),
        'The forced failure target was unexpectedly replaced.');
    deployAtomicityAssert(
        file_get_contents($root . DIRECTORY_SEPARATOR . 'config.php') === "<?php\nreturn [];\n",
        'A failed file promotion changed private configuration.');
    $rollbackRevision = (string)file_get_contents(
        $data['rollback_directory'] . DIRECTORY_SEPARATOR . 'RevisionService.php');
    $secondOutput = [];
    $secondExitCode = 0;
    exec(escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($installerPath) . ' 2>&1', $secondOutput, $secondExitCode);
    deployAtomicityAssert($secondExitCode !== 0, 'A repeated private installer transaction unexpectedly succeeded.');
    deployAtomicityAssert(
        file_get_contents($data['rollback_directory'] . DIRECTORY_SEPARATOR . 'RevisionService.php') === $rollbackRevision,
        'A repeated private installer transaction overwrote the original rollback snapshot.');
} finally {
    removeDeploymentFixture($root);
}

$postInstallPattern = <<<'REGEX'
~\$rollbackCode = @'\R(.*?)\R'@\.Replace\('__TRANSACTION_DATA__'~s
REGEX;
deployAtomicityAssert(
    preg_match($postInstallPattern, $deploymentScript, $rollbackMatches) === 1,
    'Unable to extract the post-install rollback transaction.');

$postRoot = sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'pa-deploy-postcheck-' . bin2hex(random_bytes(6));
$postRollback = $postRoot . DIRECTORY_SEPARATOR . '.word-count-rollback-fixture';
$postPublic = $postRoot . DIRECTORY_SEPARATOR . 'public' . DIRECTORY_SEPARATOR . 'index.php';
mkdir(dirname($postPublic), 0700, true);
mkdir($postRollback, 0700, true);
file_put_contents($postRoot . DIRECTORY_SEPARATOR . 'RevisionService.php', "<?php\n// active candidate revision\n");
file_put_contents($postRoot . DIRECTORY_SEPARATOR . 'BrokerService.php', "<?php\n// active candidate broker\n");
file_put_contents($postRoot . DIRECTORY_SEPARATOR . 'config.php', "<?php\nreturn ['candidate' => true];\n");
file_put_contents($postPublic, "<?php\n// active candidate public entry\n");
file_put_contents($postRollback . DIRECTORY_SEPARATOR . 'RevisionService.php', "<?php\n// original revision\n");
file_put_contents($postRollback . DIRECTORY_SEPARATOR . 'BrokerService.php', "<?php\n// original broker\n");
file_put_contents($postRollback . DIRECTORY_SEPARATOR . 'config.php', "<?php\nreturn ['original' => true];\n");
file_put_contents($postRollback . DIRECTORY_SEPARATOR . 'public-index.php', "<?php\n// original public entry\n");
file_put_contents($postRollback . DIRECTORY_SEPARATOR . 'manifest.json', json_encode([
    'files' => ['RevisionService.php' => true, 'BrokerService.php' => true],
    'config_originally_existed' => true,
], JSON_THROW_ON_ERROR));
file_put_contents($postRollback . DIRECTORY_SEPARATOR . 'public-index-state.json', json_encode([
    'originally_existed' => true,
], JSON_THROW_ON_ERROR));
$postData = base64_encode(json_encode([
    'private_directory' => $postRoot,
    'public_api_path' => $postPublic,
    'rollback_directory' => $postRollback,
    'files' => ['RevisionService.php', 'BrokerService.php'],
], JSON_THROW_ON_ERROR));
$postInstaller = str_replace('__TRANSACTION_DATA__', $postData, $rollbackMatches[1]);
$postInstallerPath = $postRoot . DIRECTORY_SEPARATOR . 'rollback.php';
file_put_contents($postInstallerPath, "<?php\n" . $postInstaller);

try {
    $output = [];
    $exitCode = 0;
    exec(escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($postInstallerPath) . ' 2>&1', $output, $exitCode);
    deployAtomicityAssert($exitCode === 0, 'The post-install rollback transaction failed: ' . implode("\n", $output));
    deployAtomicityAssert(str_contains((string)file_get_contents($postRoot . DIRECTORY_SEPARATOR . 'RevisionService.php'), 'original revision'), 'Post-check rollback did not restore RevisionService.');
    deployAtomicityAssert(str_contains((string)file_get_contents($postRoot . DIRECTORY_SEPARATOR . 'BrokerService.php'), 'original broker'), 'Post-check rollback did not restore BrokerService.');
    deployAtomicityAssert(str_contains((string)file_get_contents($postRoot . DIRECTORY_SEPARATOR . 'config.php'), "'original' => true"), 'Post-check rollback did not restore private config.');
    deployAtomicityAssert(str_contains((string)file_get_contents($postPublic), 'original public entry'), 'Post-check rollback did not restore the public API entry point.');
} finally {
    removeDeploymentFixture($postRoot);
}

$removalRoot = sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'pa-deploy-removal-' . bin2hex(random_bytes(6));
$removalRollback = $removalRoot . DIRECTORY_SEPARATOR . '.word-count-rollback-fixture';
mkdir($removalRollback, 0700, true);
mkdir($removalRoot . DIRECTORY_SEPARATOR . 'NewService.php', 0700, true);
file_put_contents($removalRollback . DIRECTORY_SEPARATOR . 'manifest.json', json_encode([
    'files' => ['NewService.php' => false],
    'config_originally_existed' => false,
], JSON_THROW_ON_ERROR));
$removalData = base64_encode(json_encode([
    'private_directory' => $removalRoot,
    'public_api_path' => $removalRoot . DIRECTORY_SEPARATOR . 'public-index.php',
    'rollback_directory' => $removalRollback,
    'files' => ['NewService.php'],
], JSON_THROW_ON_ERROR));
$removalInstaller = str_replace('__TRANSACTION_DATA__', $removalData, $rollbackMatches[1]);
$removalInstallerPath = $removalRoot . DIRECTORY_SEPARATOR . 'rollback.php';
file_put_contents($removalInstallerPath, "<?php\n" . $removalInstaller);
try {
    $output = [];
    $exitCode = 0;
    exec(escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($removalInstallerPath) . ' 2>&1', $output, $exitCode);
    deployAtomicityAssert($exitCode !== 0, 'Rollback silently accepted failure to remove a newly introduced target.');
    deployAtomicityAssert(is_dir($removalRollback), 'Rollback evidence was removed after a failed target removal.');
} finally {
    removeDeploymentFixture($removalRoot);
}

echo "Broker deployment atomicity tests passed.\n";
