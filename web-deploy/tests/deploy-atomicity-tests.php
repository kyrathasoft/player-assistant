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
deployAtomicityAssert($deploymentScript !== '' && $deploymentVerifier !== '', 'Unable to read word-count deployment controllers.');
deployAtomicityAssert(
    str_contains($deploymentScript, '$payload = "<?php`n" + $Code')
        && str_contains($deploymentVerifier, '$payload = "<?php`n" + $Code')
        && str_contains($deploymentScript, '[IO.File]::WriteAllText($localScript, $payload')
        && str_contains($deploymentVerifier, '[IO.File]::WriteAllText($localScript, $payload'),
    'Both remote PHP controllers must prepend an opening PHP tag.');
deployAtomicityAssert(
    str_contains($deploymentScript, 'Remote PHP code contains an unresolved placeholder')
        && str_contains($deploymentVerifier, 'Remote PHP code contains an unresolved placeholder'),
    'Both remote PHP controllers must reject unresolved placeholders.');
deployAtomicityAssert(
    str_contains($deploymentScript, 'ConvertFrom-Json')
        && str_contains($deploymentVerifier, 'ConvertFrom-Json')
        && str_contains($deploymentScript, 'did not report semantic success')
        && str_contains($deploymentVerifier, 'did not report semantic success'),
    'Both remote PHP controllers must require structured semantic success responses.');
deployAtomicityAssert(
    str_contains($deploymentScript, "-ExpectedOperation 'word-count-install' -RequireStateMutation -Attempts 1")
        && str_contains($deploymentScript, "-ExpectedOperation 'word-count-cron-install' -RequireStateMutation -Attempts 1")
        && str_contains($deploymentVerifier, "-ExpectedOperation 'word-count-verify'"),
    'Mutating controllers must require expected state mutation and the verifier must require its operation identity.');
deployAtomicityAssert(
    str_contains($deploymentScript, "'operation' => 'word-count-install'")
        && str_contains($deploymentScript, "'operation' => 'word-count-cron-install'")
        && str_contains($deploymentVerifier, "\$result['operation'] = 'word-count-verify';")
        && str_contains($deploymentScript, "'state_mutation' => true")
        && str_contains($deploymentVerifier, "\$result['state_mutation'] = false;"),
    'Generated controllers must report operation identity and mutation semantics.');

$installerPattern = '~\$installCode = @\'\R(.*?)\R\'@\.Replace\(\'__INSTALL_DATA__\'~s';
$cronPattern = '~\$cronCode = @\'\R(.*?)\R\'@\.Replace\(\'__CRON_LINE__\'~s';
$verifierPattern = '~\$remoteCode = @\'\R(.*?)\R\'@\.Replace\(\'__PRIVATE_DIRECTORY__\'~s';
deployAtomicityAssert(preg_match($installerPattern, $deploymentScript, $installerMatches) === 1, 'Unable to extract the generated install controller.');
deployAtomicityAssert(preg_match($cronPattern, $deploymentScript, $cronMatches) === 1, 'Unable to extract the generated cron controller.');
deployAtomicityAssert(preg_match($verifierPattern, $deploymentVerifier, $verifierMatches) === 1, 'Unable to extract the generated verification controller.');

$root = sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'pa-deploy-payload-' . bin2hex(random_bytes(6));
$stage = $root . DIRECTORY_SEPARATOR . '.word-count-deploy-fixture';
mkdir($stage, 0700, true);
$files = ['AuthorizationPolicy.php', 'BrokerService.php', 'BrokerAlertService.php', 'BrokerOperations.php', 'DatabaseMigrationService.php', 'QuestService.php', 'WordCountService.php', 'refresh-word-counts.php', 'broker-maintenance.php'];
foreach ($files as $file) {
    file_put_contents($stage . DIRECTORY_SEPARATOR . $file, "<?php\n// candidate fixture\n");
}
file_put_contents($root . DIRECTORY_SEPARATOR . 'config.php', "<?php\nreturn [];\n");
$fixtureData = [
    'private_directory' => $root,
    'source_url' => 'https://example.invalid/word-counts.json',
    'status_path' => $root . DIRECTORY_SEPARATOR . 'status.json',
    'key_id' => 'fixture-key',
    'public_key' => 'fixture-public-key',
    'keep_backups' => 5,
    'deploy_id' => 'fixture',
    'files' => array_combine($files, array_map(static fn(string $file): string => $stage . DIRECTORY_SEPARATOR . $file, $files)),
];
$installCode = str_replace(
    '__INSTALL_DATA__',
    base64_encode(json_encode($fixtureData, JSON_THROW_ON_ERROR)),
    $installerMatches[1]
);
$cronCode = str_replace('__CRON_LINE__', base64_encode("17 */6 * * * /usr/bin/php refresh-word-counts.php\n"), $cronMatches[1]);
$verifyCode = str_replace('__PRIVATE_DIRECTORY__', str_replace("'", "\\'", $root), $verifierMatches[1]);
$phpBinary = PHP_BINARY;
$installCode = str_replace('/usr/bin/php', $phpBinary, $installCode);
$payloads = [
    'install.php' => $installCode,
    'cron.php' => $cronCode,
    'verify.php' => $verifyCode,
];
try {
    foreach ($payloads as $name => $code) {
        deployAtomicityAssert(!str_contains($code, '__INSTALL_DATA__') && !str_contains($code, '__CRON_LINE__') && !str_contains($code, '__PRIVATE_DIRECTORY__'), "$name retained an unresolved placeholder.");
        $path = $root . DIRECTORY_SEPARATOR . $name;
        file_put_contents($path, "<?php\n" . $code);
        $output = [];
        $exitCode = 0;
        exec(escapeshellarg($phpBinary) . ' -l ' . escapeshellarg($path) . ' 2>&1', $output, $exitCode);
        deployAtomicityAssert($exitCode === 0, "$name is not executable PHP: " . implode("\n", $output));
    }

    $output = [];
    $exitCode = 0;
    exec(escapeshellarg($phpBinary) . ' ' . escapeshellarg($root . DIRECTORY_SEPARATOR . 'install.php') . ' 2>&1', $output, $exitCode);
    deployAtomicityAssert($exitCode === 0, 'Generated install controller failed: ' . implode("\n", $output));
    $response = json_decode(implode("\n", $output), true, 32, JSON_THROW_ON_ERROR);
    deployAtomicityAssert(($response['ok'] ?? false) === true, 'Generated install controller did not report semantic success.');
    deployAtomicityAssert(($response['operation'] ?? null) === 'word-count-install', 'Generated install controller returned the wrong operation.');
    deployAtomicityAssert(($response['state_mutation'] ?? false) === true, 'Generated install controller did not report a state mutation.');
    foreach ($files as $file) {
        deployAtomicityAssert(is_file($root . DIRECTORY_SEPARATOR . $file), "Generated install controller did not promote $file.");
    }
} finally {
    removeDeploymentFixture($root);
}

fwrite(STDOUT, "PASS deploy atomicity remote PHP payload contracts\n");
