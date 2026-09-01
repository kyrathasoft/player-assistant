<?php

declare(strict_types=1);

const INSTALLER_SCHEMA_VERSION = 1;
const PWA_PATH = '/scarlethorizons/pwa/';
const API_PATH = '/scarlethorizons/api/';

if (PHP_SAPI !== 'cli') {
    http_response_code(404);
    exit(1);
}

function installerUsage(): string
{
    return <<<'TEXT'
Player Assistant online PWA installer

Install or upgrade:
  php install-player-assistant-web.php \
    --package=/private/path/player-assistant-web-payload.tar \
    --origin=https://example.com \
    --public-root=/home/account/example.com \
    --private-root=/home/account/player-assistant-broker-example \
    --config-source=/private/path/config.php

Installs these fixed URL paths:
  /scarlethorizons/pwa/
  /scarlethorizons/api/

Options:
  --package=         Payload archive produced by build-package.ps1.
  --origin=          HTTPS origin only, for example --origin=https://example.com.
  --public-root=     Absolute document root for the target domain.
  --private-root=    Absolute broker directory outside the document root.
  --config-source=   Completed private PHP configuration. Required for a new install.
  --verification=    https (default) or local. Local preserves rollback state.
  --skip-cron        Do not install refresh, maintenance, and recovery cron entries.
  --retain-backup    Preserve rollback evidence after successful HTTPS verification.
  --rollback-transaction=ID  Roll back a pending local-verification transaction.
  --finalize-transaction=ID  Run HTTPS verification and finalize a pending transaction.
  --help             Show this help.
TEXT;
}

function reject(string $message): never
{
    throw new InvalidArgumentException($message);
}

function validateRawArguments(array $arguments): void
{
    $valueOptions = [
        'package', 'origin', 'public-root', 'private-root', 'config-source',
        'verification', 'rollback-transaction', 'finalize-transaction',
    ];
    $flagOptions = ['skip-cron', 'retain-backup', 'help'];
    $seen = [];
    foreach (array_slice($arguments, 1) as $argument) {
        if (!is_string($argument) || !str_starts_with($argument, '--')) {
            reject('Installer arguments must use the --name=value form.');
        }
        $parts = explode('=', substr($argument, 2), 2);
        $name = $parts[0];
        if (isset($seen[$name])) {
            reject("Duplicate installer option: --$name.");
        }
        $seen[$name] = true;
        if (in_array($name, $valueOptions, true)) {
            if (count($parts) !== 2 || trim($parts[1]) === '') {
                reject("Installer option --$name requires a non-empty value.");
            }
        } elseif (in_array($name, $flagOptions, true)) {
            if (count($parts) !== 1) {
                reject("Installer flag --$name does not accept a value.");
            }
        } else {
            reject("Unknown installer option: --$name.");
        }
    }
}

function normalizePath(string $path): string
{
    if ($path === '' || str_contains($path, "\0")) {
        reject('Installer paths must be non-empty absolute paths.');
    }
    $path = str_replace('\\', '/', trim($path));
    if (!str_starts_with($path, '/') && preg_match('#^[A-Za-z]:/#', $path) !== 1) {
        reject('Installer paths must be absolute.');
    }
    $prefix = str_starts_with($path, '/') ? '/' : substr($path, 0, 3);
    $rest = str_starts_with($path, '/') ? substr($path, 1) : substr($path, 3);
    $segments = [];
    foreach (explode('/', $rest) as $segment) {
        if ($segment === '') {
            continue;
        }
        if ($segment === '.' || $segment === '..') {
            reject('Installer paths cannot contain dot segments.');
        }
        if (preg_match('/[\x00-\x1F\x7F]/', $segment) === 1) {
            reject('Installer paths cannot contain control characters.');
        }
        if (preg_match('/^[A-Za-z0-9._ -]+$/D', $segment) !== 1) {
            reject('Installer paths contain unsupported characters.');
        }
        $segments[] = $segment;
    }
    if ($segments === []) {
        reject('Installer paths cannot resolve to a filesystem root.');
    }
    return rtrim($prefix . implode('/', $segments), '/');
}

function canonicalExistingPath(string $path, string $label, bool $directory): string
{
    $normalized = normalizePath($path);
    if (is_link($normalized)) {
        reject("The $label cannot be a symbolic link.");
    }
    if ($directory ? !is_dir($normalized) : !is_file($normalized)) {
        reject("The $label does not exist or has the wrong type.");
    }
    $real = realpath($normalized);
    if (!is_string($real)) {
        reject("The $label cannot be canonicalized.");
    }
    $canonical = normalizePath($real);
    $same = DIRECTORY_SEPARATOR === '\\'
        ? strcasecmp($canonical, $normalized) === 0
        : $canonical === $normalized;
    if (!$same) {
        reject("The $label cannot pass through symbolic-link or alias components.");
    }
    return $canonical;
}

function normalizeOrigin(string $origin): string
{
    $origin = rtrim(trim($origin), '/');
    $parts = parse_url($origin);
    if (!is_array($parts)
        || ($parts['scheme'] ?? '') !== 'https'
        || !is_string($parts['host'] ?? null)
        || $parts['host'] === ''
        || isset($parts['path'])
        || isset($parts['query'])
        || isset($parts['fragment'])
        || isset($parts['user'])
        || isset($parts['pass'])) {
        reject('The origin must be an HTTPS origin without a path, query, fragment, or credentials.');
    }
    return 'https://' . strtolower($parts['host']) . (isset($parts['port']) ? ':' . (int)$parts['port'] : '');
}

function assertNoSymlinkComponents(string $path): void
{
    $probe = rtrim(str_replace('\\', '/', $path), '/');
    while ($probe !== '' && $probe !== '.') {
        if (is_link($probe)) {
            reject("Installer paths cannot traverse symbolic links: $probe");
        }
        $parent = rtrim(str_replace('\\', '/', dirname($probe)), '/');
        if ($parent === '' || $parent === '.' || $parent === $probe) {
            break;
        }
        $probe = $parent;
    }
}

function ensureDirectory(string $path, int $mode): void
{
    assertNoSymlinkComponents($path);
    if (is_link($path)) {
        throw new RuntimeException("Directory paths cannot be symbolic links: $path");
    }
    if (!is_dir($path) && !mkdir($path, $mode, true) && !is_dir($path)) {
        throw new RuntimeException("Unable to create directory: $path");
    }
    @chmod($path, $mode);
}

function acquireInstallerLock(string $accountHome)
{
    $lockPath = $accountHome . '/.player-assistant-installer.lock';
    if (is_link($lockPath)) {
        reject('The installer lock file cannot be a symbolic link.');
    }
    $handle = fopen($lockPath, 'c+');
    if ($handle === false) {
        throw new RuntimeException('Unable to open the installer lock file.');
    }
    @chmod($lockPath, 0600);
    if (!flock($handle, LOCK_EX | LOCK_NB)) {
        fclose($handle);
        reject('Another Player Assistant installer operation is already running.');
    }
    if (!ftruncate($handle, 0)
        || fwrite($handle, (string)getmypid() . "\n") === false
        || !fflush($handle)) {
        flock($handle, LOCK_UN);
        fclose($handle);
        throw new RuntimeException('Unable to record the installer lock owner.');
    }
    return $handle;
}

function removeTree(string $path): void
{
    if (is_link($path) || is_file($path)) {
        if (!unlink($path)) {
            throw new RuntimeException("Unable to remove file: $path");
        }
        return;
    }
    if (!is_dir($path)) {
        return;
    }
    $iterator = new RecursiveIteratorIterator(
        new RecursiveDirectoryIterator($path, FilesystemIterator::SKIP_DOTS),
        RecursiveIteratorIterator::CHILD_FIRST);
    foreach ($iterator as $entry) {
        $entryPath = $entry->getPathname();
        if ($entry->isLink() || $entry->isFile()) {
            if (!unlink($entryPath)) {
                throw new RuntimeException("Unable to remove file: $entryPath");
            }
        } elseif (!rmdir($entryPath)) {
            throw new RuntimeException("Unable to remove directory: $entryPath");
        }
    }
    if (!rmdir($path)) {
        throw new RuntimeException("Unable to remove directory: $path");
    }
}

function copyTree(string $source, string $destination, int $fileMode, int $directoryMode): void
{
    ensureDirectory($destination, $directoryMode);
    $iterator = new RecursiveIteratorIterator(
        new RecursiveDirectoryIterator($source, FilesystemIterator::SKIP_DOTS),
        RecursiveIteratorIterator::SELF_FIRST);
    foreach ($iterator as $entry) {
        $relative = substr(str_replace('\\', '/', $entry->getPathname()), strlen(str_replace('\\', '/', $source)) + 1);
        $target = $destination . '/' . $relative;
        if ($entry->isLink()) {
            throw new RuntimeException('Symbolic links are not allowed in the installer payload.');
        }
        if ($entry->isDir()) {
            ensureDirectory($target, $directoryMode);
        } else {
            ensureDirectory(dirname($target), $directoryMode);
            if (!copy($entry->getPathname(), $target)) {
                throw new RuntimeException("Unable to copy payload file: $relative");
            }
            @chmod($target, $fileMode);
        }
    }
}

function applyTreePermissions(string $root, int $fileMode, int $directoryMode): void
{
    if (!is_dir($root) || is_link($root)) {
        throw new RuntimeException("The permission root is invalid: $root");
    }
    @chmod($root, $directoryMode);
    $iterator = new RecursiveIteratorIterator(
        new RecursiveDirectoryIterator($root, FilesystemIterator::SKIP_DOTS),
        RecursiveIteratorIterator::SELF_FIRST);
    foreach ($iterator as $entry) {
        if ($entry->isLink()) {
            throw new RuntimeException('Symbolic links are not allowed in staged runtime trees.');
        }
        @chmod($entry->getPathname(), $entry->isDir() ? $directoryMode : $fileMode);
    }
}

function verifyMode(string $path, int $expected): void
{
    if (DIRECTORY_SEPARATOR === '\\') {
        return;
    }
    $mode = fileperms($path);
    if ($mode === false || ($mode & 0777) !== $expected) {
        throw new RuntimeException(sprintf('Installed mode mismatch for %s: expected %04o.', $path, $expected));
    }
}

function interruptAtCommitBoundary(string $point): void
{
    $requested = getenv('PLAYER_ASSISTANT_FAULT_STAGE');
    if ($requested === $point) {
        throw new RuntimeException('Deterministic transaction interruption at ' . $point . '.');
    }
}

function writeJson(string $path, array $value, int $mode = 0600): void
{
    $temporary = $path . '.tmp-' . bin2hex(random_bytes(4));
    $bytes = json_encode($value, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR) . PHP_EOL;
    if (file_put_contents($temporary, $bytes, LOCK_EX) === false) {
        throw new RuntimeException("Unable to write JSON file: $path");
    }
    @chmod($temporary, $mode);
    if (!rename($temporary, $path)) {
        @unlink($temporary);
        throw new RuntimeException("Unable to promote JSON file: $path");
    }
}

function materializeConfigSource(
    string $source,
    string $destination,
    string $origin,
    string $privateRoot,
    string $accountHome
): void {
    if (!is_file($source)) {
        reject('The private configuration source does not exist.');
    }
    $text = file_get_contents($source);
    if (!is_string($text) || !str_starts_with(ltrim($text), '<?php')) {
        reject('The private configuration source must be a PHP configuration file.');
    }
    $escapeSingleQuotedPhp = static fn(string $value): string => str_replace(
        ['\\', "'"],
        ['\\\\', "\\'"],
        $value);
    $text = str_replace(
        ['__TARGET_ORIGIN__', '__PRIVATE_ROOT__', '__ACCOUNT_HOME__'],
        [
            $escapeSingleQuotedPhp($origin),
            $escapeSingleQuotedPhp($privateRoot),
            $escapeSingleQuotedPhp($accountHome),
        ],
        $text);
    if (preg_match('/__[A-Z0-9_]+__/', $text) === 1) {
        reject('The private configuration contains an unsupported substitution placeholder.');
    }
    if (file_put_contents($destination, $text, LOCK_EX) === false) {
        throw new RuntimeException('Unable to materialize the private configuration.');
    }
    @chmod($destination, 0600);
    lintPhp($destination);
}

function loadConfig(string $path, string $origin, string $privateRoot): array
{
    if (!is_file($path)) {
        reject('The private configuration source does not exist.');
    }
    $validatorPath = $path . '.validator-' . bin2hex(random_bytes(4)) . '.php';
    $validatorCode = <<<'PHP'
<?php
$config = require $argv[1];
if (!is_array($config)) {
    exit(3);
}
echo json_encode($config, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
PHP;
    if (file_put_contents($validatorPath, $validatorCode, LOCK_EX) === false) {
        throw new RuntimeException('Unable to create the isolated configuration validator.');
    }
    @chmod($validatorPath, 0600);
    $output = [];
    $exit = 0;
    try {
        exec(
            escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($validatorPath)
                . ' ' . escapeshellarg($path) . ' 2>&1',
            $output,
            $exit);
    } finally {
        @unlink($validatorPath);
    }
    if ($exit !== 0) {
        reject('The private configuration could not be loaded safely as an array.');
    }
    try {
        $config = json_decode(implode("\n", $output), true, 64, JSON_THROW_ON_ERROR);
    } catch (Throwable) {
        reject('The private configuration emitted output or contains unsupported values.');
    }
    if (!is_array($config)) {
        reject('The private configuration source must return an array.');
    }
    $scan = static function (mixed $value) use (&$scan): void {
        if (is_array($value)) {
            foreach ($value as $nested) {
                $scan($nested);
            }
        } elseif (is_string($value)
            && (str_contains($value, 'CHANGE_ME') || str_contains($value, '__TARGET_')
                || str_contains($value, '__PRIVATE_') || str_contains($value, '__ACCOUNT_'))) {
            reject('The private configuration contains unresolved placeholders.');
        }
    };
    $scan($config);
    $api = is_array($config['api'] ?? null) ? $config['api'] : [];
    $auth = is_array($config['auth'] ?? null) ? $config['auth'] : [];
    $rpol = is_array($config['rpol'] ?? null) ? $config['rpol'] : [];
    $xp = is_array($config['xp'] ?? null) ? $config['xp'] : [];
    $wordCounts = is_array($config['word_counts'] ?? null) ? $config['word_counts'] : [];
    $operations = is_array($config['operations'] ?? null) ? $config['operations'] : [];
    $recovery = is_array($config['database_recovery'] ?? null) ? $config['database_recovery'] : [];
    $pwaMonitor = is_array($config['pwa_monitor'] ?? null) ? $config['pwa_monitor'] : [];
    if (($api['base_path'] ?? null) !== '/scarlethorizons/api') {
        reject("The API base path must be /scarlethorizons/api.");
    }
    if (normalizePath((string)($api['database_path'] ?? '')) !== $privateRoot . '/broker.sqlite') {
        reject('The configured broker database must be private-root/broker.sqlite.');
    }
    if (!is_string($api['admin_key'] ?? null) || strlen($api['admin_key']) < 32) {
        reject('The broker administrator key must contain at least 32 characters.');
    }
    $snapshotKey = base64_decode((string)($api['snapshot_signing_key'] ?? ''), true);
    if (!is_string($snapshotKey) || strlen($snapshotKey) !== 32) {
        reject('The snapshot signing key must encode exactly 32 bytes.');
    }
    if (($auth['expected_origin'] ?? null) !== $origin
        || ($auth['cookie_path'] ?? null) !== '/scarlethorizons/api/') {
        reject('The authentication origin or cookie path does not match the target layout.');
    }
    if (!is_string($auth['audit_address_hash_key'] ?? null)
        || strlen($auth['audit_address_hash_key']) < 32) {
        reject('The authentication audit hash key must contain at least 32 characters.');
    }
    foreach (['username', 'password', 'game_id'] as $key) {
        if (!is_string($rpol[$key] ?? null) || trim($rpol[$key]) === '') {
            reject("The RPOL configuration field '$key' is required.");
        }
    }
    foreach (['source_url', 'character_source_url', 'class_progression_index_url'] as $key) {
        if (!is_string($xp[$key] ?? null) || !str_starts_with($xp[$key], 'https://')) {
            reject("The XP configuration field '$key' must be an HTTPS URL.");
        }
    }
    if (!is_string($wordCounts['source_url'] ?? null)
        || !str_starts_with($wordCounts['source_url'], 'https://')) {
        reject('The word-count source URL must use HTTPS.');
    }
    $wordCountKey = base64_decode((string)($wordCounts['signature_public_key'] ?? ''), true);
    if (!is_string($wordCountKey) || strlen($wordCountKey) !== 32) {
        reject('The word-count signature public key must encode exactly 32 bytes.');
    }
    if (($pwaMonitor['base_url'] ?? null) !== $origin . '/scarlethorizons'
        || !is_string($pwaMonitor['character_name'] ?? null)
        || trim($pwaMonitor['character_name']) === ''
        || !is_string($pwaMonitor['password'] ?? null)
        || $pwaMonitor['password'] === '') {
        reject('The authenticated PWA monitor configuration is incomplete or targets the wrong origin.');
    }
    $privatePaths = [
        'api.snapshot_directory' => $api['snapshot_directory'] ?? null,
        'xp.awards_directory' => $xp['awards_directory'] ?? null,
        'word_counts.status_path' => $wordCounts['status_path'] ?? null,
        'operations.backup_directory' => $operations['backup_directory'] ?? null,
        'operations.restore_test_directory' => $operations['restore_test_directory'] ?? null,
        'operations.status_path' => $operations['status_path'] ?? null,
        'database_recovery.backup_directory' => $recovery['backup_directory'] ?? null,
        'database_recovery.status_path' => $recovery['status_path'] ?? null,
        'pwa_monitor.status_path' => $pwaMonitor['status_path'] ?? null,
    ];
    foreach ($privatePaths as $key => $value) {
        if (!is_string($value) || $value === '') {
            reject("The configured private path '$key' is required.");
        }
        $configuredPath = normalizePath($value);
        if (!str_starts_with($configuredPath . '/', $privateRoot . '/')) {
            reject("The configured path '$key' must remain under the private root.");
        }
    }
    if (($recovery['health_url'] ?? null) !== $origin . '/scarlethorizons/api/v1/health') {
        reject('The database-recovery health URL must target the selected origin.');
    }
    if (($operations['environment_file'] ?? null) !== dirname($privateRoot) . '/.player-assistant-ftps.env') {
        reject('The operations environment file must be the protected account-home FTPS environment file.');
    }
    return $config;
}

function verifyArchiveHash(string $package): void
{
    $checksumPath = $package . '.sha256';
    if (is_link($checksumPath)) {
        reject('The payload package checksum cannot be a symbolic link.');
    }
    if (!is_file($checksumPath)) {
        reject('The payload package checksum file is missing.');
    }
    $line = trim((string)file_get_contents($checksumPath));
    if (preg_match('/^([a-f0-9]{64})  ([A-Za-z0-9._-]+)$/', $line, $match) !== 1
        || $match[2] !== basename($package)
        || !hash_equals($match[1], hash_file('sha256', $package))) {
        reject('The payload package checksum is invalid.');
    }
}

function requiredPackagePaths(): array
{
    return [
        'payload/public/scarlethorizons/pwa/.htaccess',
        'payload/public/scarlethorizons/pwa/index.html',
        'payload/public/scarlethorizons/pwa/manifest.webmanifest',
        'payload/public/scarlethorizons/pwa/service-worker.js',
        'payload/public/scarlethorizons/api/.htaccess',
        'payload/public/scarlethorizons/api/index.php.template',
        'payload/private/AuthorizationPolicy.php',
        'payload/private/DatabaseMigrationService.php',
        'payload/private/migrate-broker.php',
        'payload/private/BrokerService.php',
        'payload/private/CharacterAuthService.php',
    ];
}

function validatePackageManifestContract(array $manifest): void
{
    if (($manifest['schema_version'] ?? null) !== INSTALLER_SCHEMA_VERSION
        || ($manifest['product'] ?? null) !== 'player-assistant-web'
        || !is_string($manifest['version'] ?? null)
        || $manifest['version'] === ''
        || ($manifest['fixed_url_layout']['pwa'] ?? null) !== PWA_PATH
        || ($manifest['fixed_url_layout']['api'] ?? null) !== API_PATH
        || !is_array($manifest['files'] ?? null)
        || $manifest['files'] === []) {
        reject('The package manifest contract is invalid.');
    }
    $declared = [];
    $caseFolded = [];
    $declaredBytes = 0;
    foreach ($manifest['files'] as $entry) {
        if (!is_array($entry)
            || !is_string($entry['path'] ?? null)
            || preg_match('#^payload/(?:public/scarlethorizons/(?:pwa|api)|private)/[A-Za-z0-9._/-]+$#', $entry['path']) !== 1
            || preg_match('#(^|/)\.\.($|/)#', $entry['path']) === 1
            || !is_string($entry['sha256'] ?? null)
            || preg_match('/^[a-f0-9]{64}$/', $entry['sha256']) !== 1
            || !is_int($entry['bytes'] ?? null)
            || $entry['bytes'] < 0
            || $entry['bytes'] > 33554432) {
            reject('The package manifest contract is invalid.');
        }
        $isPublic = str_starts_with($entry['path'], 'payload/public/');
        $isApiTemplate = $entry['path'] === 'payload/public/scarlethorizons/api/index.php.template';
        if (($entry['visibility'] ?? null) !== ($isPublic ? 'public' : 'private')
            || ($entry['mode'] ?? null) !== ($isPublic ? '0644' : '0600')
            || ($entry['substitution'] ?? null) !== ($isApiTemplate ? 'private_root_php_literal' : null)) {
            reject('The package manifest contract is invalid.');
        }
        $caseKey = strtolower($entry['path']);
        if (isset($declared[$entry['path']]) || isset($caseFolded[$caseKey])) {
            reject('The package manifest contract is invalid.');
        }
        $declared[$entry['path']] = true;
        $caseFolded[$caseKey] = true;
        $declaredBytes += $entry['bytes'];
        if ($declaredBytes > 104857600) {
            reject('The package manifest contract is invalid.');
        }
    }
    foreach (requiredPackagePaths() as $path) {
        if (!isset($declared[$path])) {
            reject('The package manifest contract is invalid.');
        }
    }
}

function extractAndVerifyPackage(string $package, string $stage): array
{
    if (PHP_VERSION_ID < 80100) {
        reject('PHP 8.1 or newer is required.');
    }
    if (filesize($package) === false || filesize($package) > 104857600) {
        reject('The payload archive exceeds the 100 MiB safety limit.');
    }
    foreach (['phar', 'pdo_sqlite', 'sodium', 'curl', 'openssl'] as $extension) {
        if (!extension_loaded($extension)) {
            reject("The PHP extension '$extension' is required.");
        }
    }
    verifyArchiveHash($package);
    try {
        $archive = new PharData($package);
    } catch (Throwable $error) {
        throw new RuntimeException('Unable to open the payload archive.', 0, $error);
    }
    if (!isset($archive['manifest.json'])) {
        reject('The payload archive manifest is missing.');
    }
    $manifest = json_decode($archive['manifest.json']->getContent(), true, 32, JSON_THROW_ON_ERROR);
    validatePackageManifestContract($manifest);
    if (($manifest['schema_version'] ?? null) !== INSTALLER_SCHEMA_VERSION
        || ($manifest['product'] ?? null) !== 'player-assistant-web'
        || ($manifest['fixed_url_layout']['pwa'] ?? null) !== PWA_PATH
        || ($manifest['fixed_url_layout']['api'] ?? null) !== API_PATH
        || !is_array($manifest['files'] ?? null)) {
        reject('The payload archive manifest contract is invalid.');
    }
    $declared = ['manifest.json' => true];
    $caseFolded = ['manifest.json' => true];
    $declaredBytes = 0;
    $required = requiredPackagePaths();
    foreach ($manifest['files'] as $entry) {
        if (!is_array($entry)
            || !is_string($entry['path'] ?? null)
            || preg_match('#^payload/(?:public/scarlethorizons/(?:pwa|api)|private)/[A-Za-z0-9._/-]+$#', $entry['path']) !== 1
            || preg_match('#(^|/)\.\.($|/)#', $entry['path']) === 1
            || isset($declared[$entry['path']])
            || !is_string($entry['sha256'] ?? null)
            || preg_match('/^[a-f0-9]{64}$/', $entry['sha256']) !== 1
            || !isset($archive[$entry['path']])) {
            reject('The payload archive contains an invalid manifest entry.');
        }
        $isPublic = str_starts_with($entry['path'], 'payload/public/');
        if (($entry['visibility'] ?? null) !== ($isPublic ? 'public' : 'private')
            || ($entry['mode'] ?? null) !== ($isPublic ? '0644' : '0600')) {
            reject('The payload archive contains an invalid visibility or mode declaration.');
        }
        $isApiTemplate = $entry['path'] === 'payload/public/scarlethorizons/api/index.php.template';
        if (($entry['substitution'] ?? null) !== ($isApiTemplate ? 'private_root_php_literal' : null)) {
            reject('The payload archive contains an invalid substitution declaration.');
        }
        $caseKey = strtolower($entry['path']);
        if (isset($caseFolded[$caseKey])) {
            reject('The payload archive contains duplicate or case-colliding paths.');
        }
        $caseFolded[$caseKey] = true;
        $bytes = $archive[$entry['path']]->getContent();
        $declaredBytes += strlen($bytes);
        if (strlen($bytes) > 33554432 || $declaredBytes > 104857600) {
            reject('The payload archive exceeds an entry or total extraction safety limit.');
        }
        if (!hash_equals($entry['sha256'], hash('sha256', $bytes))
            || (int)($entry['bytes'] ?? -1) !== strlen($bytes)) {
            reject('Payload content does not match its manifest: ' . $entry['path']);
        }
        $declared[$entry['path']] = true;
    }
    foreach ($required as $path) {
        if (!isset($declared[$path])) {
            reject("The payload archive is missing required runtime file: $path");
        }
    }
    $actual = [];
    $iterator = new RecursiveIteratorIterator($archive);
    foreach ($iterator as $file) {
        if ($file->isLink()) {
            reject('The payload archive cannot contain symbolic or hard links.');
        }
        if ($file->isDir()) {
            continue;
        }
        if (!$file->isFile()) {
            reject('The payload archive can contain only regular files and directories.');
        }
        $path = str_replace('\\', '/', $iterator->getSubIterator()->getSubPathname());
        if (preg_match('#(^|/)\.\.($|/)#', $path) === 1 || str_starts_with($path, '/')) {
            reject('The payload archive contains an unsafe path.');
        }
        $actual[$path] = true;
    }
    if (array_keys($actual) !== array_keys($declared)) {
        $actualPaths = array_keys($actual);
        $declaredPaths = array_keys($declared);
        sort($actualPaths);
        sort($declaredPaths);
        if ($actualPaths !== $declaredPaths) {
            reject('The payload archive contains undeclared or missing files.');
        }
    }
    ensureDirectory($stage, 0700);
    $archive->extractTo($stage, array_keys($declared), false);
    foreach (array_keys($declared) as $path) {
        $extracted = $stage . '/' . $path;
        if (!is_file($extracted) || is_link($extracted)) {
            reject('The extracted payload is incomplete or contains a symbolic link.');
        }
        if (str_ends_with($path, '.json')) {
            json_decode((string)file_get_contents($extracted), true, 512, JSON_THROW_ON_ERROR);
        }
    }
    return $manifest;
}

function lintPhp(string $path): void
{
    $output = [];
    $exit = 0;
    exec(escapeshellarg(PHP_BINARY) . ' -l ' . escapeshellarg($path) . ' 2>&1', $output, $exit);
    if ($exit !== 0) {
        throw new RuntimeException('PHP lint failed for ' . $path . ': ' . implode("\n", $output));
    }
}

function runMigration(string $stagedPrivate): void
{
    interruptAtCommitBoundary('before-migration');
    $output = [];
    $exit = 0;
    exec(escapeshellarg(PHP_BINARY) . ' ' . escapeshellarg($stagedPrivate . '/migrate-broker.php') . ' 2>&1', $output, $exit);
    if ($exit !== 0) {
        throw new RuntimeException('Broker database migration failed: ' . implode("\n", $output));
    }
    interruptAtCommitBoundary('after-migration');
}

function snapshotDatabase(string $databasePath, string $backupPath): ?array
{
    if (!is_file($databasePath)) {
        return null;
    }
    $database = new PDO('sqlite:' . $databasePath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    $database->exec('PRAGMA busy_timeout = 5000');
    $checkpoint = $database->query('PRAGMA wal_checkpoint(FULL)')->fetch(PDO::FETCH_NUM);
    if (!is_array($checkpoint) || (int)($checkpoint[0] ?? 1) !== 0) {
        throw new RuntimeException('The broker database still has active writes after API maintenance began.');
    }
    $integrity = (string)$database->query('PRAGMA integrity_check')->fetchColumn();
    $version = (int)$database->query('PRAGMA user_version')->fetchColumn();
    $journalMode = strtolower((string)$database->query('PRAGMA journal_mode')->fetchColumn());
    if (!in_array($journalMode, ['delete', 'truncate', 'persist', 'wal'], true)) {
        throw new RuntimeException('The broker database uses an unsupported journal mode.');
    }
    if ($integrity !== 'ok') {
        throw new RuntimeException('The existing broker database failed integrity verification before backup.');
    }
    if (is_file($backupPath)) {
        unlink($backupPath);
    }
    $database->exec('VACUUM INTO ' . $database->quote($backupPath));
    $database = null;
    @chmod($backupPath, 0600);
    $backup = new PDO('sqlite:' . $backupPath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    if ((string)$backup->query('PRAGMA integrity_check')->fetchColumn() !== 'ok'
        || (int)$backup->query('PRAGMA user_version')->fetchColumn() !== $version) {
        throw new RuntimeException('The broker rollback snapshot failed verification.');
    }
    $backup = null;
    return ['version' => $version, 'journal_mode' => $journalMode];
}

function verifyLocalInstall(array $packageManifest, string $publicRoot, string $privateRoot): void
{
    foreach ($packageManifest['files'] as $entry) {
        $path = (string)$entry['path'];
        if ($path === 'payload/public/scarlethorizons/api/index.php.template') {
            continue;
        }
        if (str_starts_with($path, 'payload/public/')) {
            $target = $publicRoot . '/' . substr($path, strlen('payload/public/'));
        } elseif (str_starts_with($path, 'payload/private/')) {
            $target = $privateRoot . '/' . substr($path, strlen('payload/private/'));
        } else {
            throw new RuntimeException('Unexpected verified payload path.');
        }
        if (!is_file($target) || !hash_equals((string)$entry['sha256'], hash_file('sha256', $target))) {
            throw new RuntimeException('Installed file hash mismatch: ' . $target);
        }
        verifyMode($target, octdec((string)$entry['mode']));
        if (str_ends_with($target, '.php')) {
            lintPhp($target);
        }
    }
    $apiIndex = $publicRoot . '/scarlethorizons/api/index.php';
    if (!is_file($apiIndex)
        || str_contains((string)file_get_contents($apiIndex), '__PLAYER_ASSISTANT_PRIVATE_ROOT__')) {
        throw new RuntimeException('The materialized API entry point is invalid.');
    }
    lintPhp($apiIndex);
    verifyMode($apiIndex, 0644);
    verifyMode($privateRoot, 0700);
    verifyMode($privateRoot . '/config.php', 0600);
    verifyMode($privateRoot . '/broker.sqlite', 0600);
    verifyMode($publicRoot . '/scarlethorizons/pwa', 0755);
    verifyMode($publicRoot . '/scarlethorizons/api', 0755);
    $databasePath = $privateRoot . '/broker.sqlite';
    $database = new PDO('sqlite:' . $databasePath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    if ((int)$database->query('PRAGMA user_version')->fetchColumn() !== DatabaseMigrationService::LATEST_VERSION
        || (string)$database->query('PRAGMA integrity_check')->fetchColumn() !== 'ok') {
        throw new RuntimeException('The installed broker database failed schema or integrity verification.');
    }
}

function writeInstallationReport(
    array $transaction,
    ?array $packageManifest,
    string $status,
    array $verification,
    bool $rollbackRetained,
    bool $cleanupComplete
): void {
    $hashes = [];
    foreach (($packageManifest['files'] ?? []) as $entry) {
        if (is_array($entry) && is_string($entry['path'] ?? null) && is_string($entry['sha256'] ?? null)) {
            $hashes[$entry['path']] = $entry['sha256'];
        }
    }
    $apiIndex = (string)$transaction['public_root'] . '/scarlethorizons/api/index.php';
    if (is_file($apiIndex)) {
        $hashes['materialized/public/scarlethorizons/api/index.php'] = hash_file('sha256', $apiIndex);
    }
    $report = [
        'schema_version' => INSTALLER_SCHEMA_VERSION,
        'transaction_id' => $transaction['transaction_id'],
        'status' => $status,
        'version' => $packageManifest['version'] ?? null,
        'origin' => $transaction['origin'],
        'pwa_url' => $transaction['origin'] . PWA_PATH,
        'api_url' => $transaction['origin'] . API_PATH,
        'public_root' => $transaction['public_root'],
        'private_root' => $transaction['private_root'],
        'migration_version' => class_exists('DatabaseMigrationService', false)
            ? DatabaseMigrationService::LATEST_VERSION
            : ($transaction['migration_version'] ?? null),
        'verification' => $verification,
        'promoted_file_sha256' => $hashes,
        'cron_installed' => (bool)($transaction['cron']['managed'] ?? false),
        'rollback_retained' => $rollbackRetained,
        'backup_location' => $rollbackRetained ? $transaction['transaction_directory'] : null,
        'cleanup_complete' => $cleanupComplete,
    ];
    writeJson((string)$transaction['report_path'], $report);
}

function fetchUrl(string $url): array
{
    $curl = curl_init($url);
    curl_setopt_array($curl, [
        CURLOPT_RETURNTRANSFER => true,
        CURLOPT_HEADER => true,
        CURLOPT_FOLLOWLOCATION => false,
        CURLOPT_CONNECTTIMEOUT => 10,
        CURLOPT_TIMEOUT => 60,
        CURLOPT_USERAGENT => 'PlayerAssistantOnlineInstaller/1.0',
    ]);
    $response = curl_exec($curl);
    if (!is_string($response)) {
        $error = curl_error($curl);
        curl_close($curl);
        throw new RuntimeException('HTTPS verification request failed: ' . $error);
    }
    $status = (int)curl_getinfo($curl, CURLINFO_RESPONSE_CODE);
    $headerBytes = (int)curl_getinfo($curl, CURLINFO_HEADER_SIZE);
    curl_close($curl);
    $headerText = substr($response, 0, $headerBytes);
    $headers = [];
    foreach (preg_split('/\r?\n/', trim($headerText)) ?: [] as $line) {
        if (str_contains($line, ':')) {
            [$name, $value] = explode(':', $line, 2);
            $headers[strtolower(trim($name))] = trim($value);
        }
    }
    return ['status' => $status, 'headers' => $headers, 'body' => substr($response, $headerBytes)];
}

function verifyHttpsInstall(array $packageManifest, string $origin): void
{
    interruptAtCommitBoundary('before-final-https-verification');
    foreach ($packageManifest['files'] as $entry) {
        $path = (string)$entry['path'];
        if (!str_starts_with($path, 'payload/public/scarlethorizons/pwa/')
            || str_ends_with($path, '/.htaccess')) {
            continue;
        }
        $relative = substr($path, strlen('payload/public'));
        $url = $origin . implode('/', array_map('rawurlencode', explode('/', $relative))) . '?installer-verify=' . rawurlencode(bin2hex(random_bytes(4)));
        $response = fetchUrl($url);
        if ($response['status'] !== 200
            || !hash_equals((string)$entry['sha256'], hash('sha256', $response['body']))) {
            throw new RuntimeException('Public HTTPS payload verification failed: ' . $relative);
        }
    }
    $index = fetchUrl($origin . '/scarlethorizons/pwa/index.html?installer-headers=' . bin2hex(random_bytes(4)));
    if (($index['headers']['x-content-type-options'] ?? '') !== 'nosniff'
        || !str_contains((string)($index['headers']['strict-transport-security'] ?? ''), 'max-age=')
        || !str_contains((string)($index['headers']['content-security-policy'] ?? ''), "default-src 'self'")) {
        throw new RuntimeException('The installed PWA security headers are incomplete.');
    }
    $health = fetchUrl($origin . '/scarlethorizons/api/v1/health');
    $healthJson = json_decode($health['body'], true, 16, JSON_THROW_ON_ERROR);
    if ($health['status'] !== 200 || ($healthJson['status'] ?? null) !== 'ok') {
        throw new RuntimeException('The installed broker health endpoint failed verification.');
    }
    $session = fetchUrl($origin . '/scarlethorizons/api/v1/session');
    $sessionJson = json_decode($session['body'], true, 16, JSON_THROW_ON_ERROR);
    if ($session['status'] !== 200 || ($sessionJson['authenticated'] ?? null) !== false
        || !str_contains((string)($session['headers']['cache-control'] ?? ''), 'no-store')) {
        throw new RuntimeException('The installed anonymous session endpoint failed verification.');
    }
    interruptAtCommitBoundary('after-final-https-verification');
}

function installCron(string $privateRoot, string $transactionDirectory, array &$transaction): array
{
    $output = [];
    $exit = 0;
    exec('/usr/bin/crontab -l 2>&1', $output, $exit);
    $originalExisted = $exit === 0;
    if ($exit !== 0 && !str_contains(strtolower(implode("\n", $output)), 'no crontab')) {
        throw new RuntimeException('Unable to read the existing crontab.');
    }
    $original = $originalExisted ? implode("\n", $output) . "\n" : '';
    $cronBackup = $transactionDirectory . '/crontab.txt';
    $written = file_put_contents($cronBackup, $original, LOCK_EX);
    if ($written !== strlen($original) || (string)file_get_contents($cronBackup) !== $original) {
        throw new RuntimeException('Unable to preserve verified crontab rollback evidence.');
    }
    @chmod($cronBackup, 0600);
    $cronState = ['managed' => true, 'original_existed' => $originalExisted];
    $transaction['cron'] = $cronState;
    writeJson($transactionDirectory . '/manifest.json', $transaction);
    $lines = array_values(array_filter(
        preg_split('/\r?\n/', trim($original)) ?: [],
        static fn(string $line): bool => $line !== ''
            && !str_contains($line, $privateRoot . '/refresh-word-counts.php')
            && !str_contains($line, $privateRoot . '/broker-maintenance.php')
            && !str_contains($line, $privateRoot . '/broker-recovery.php')
            && !str_contains($line, $privateRoot . '/run-pwa-monitor.php')));
    $php = escapeshellarg('/usr/bin/php');
    $lines[] = '17 */6 * * * ' . $php . ' ' . escapeshellarg($privateRoot . '/refresh-word-counts.php')
        . ' >> ' . escapeshellarg($privateRoot . '/word-count-refresh-cron.log') . ' 2>&1';
    $lines[] = '47 3 * * * ' . $php . ' ' . escapeshellarg($privateRoot . '/broker-maintenance.php')
        . ' >> ' . escapeshellarg($privateRoot . '/broker-maintenance-cron.log') . ' 2>&1';
    $lines[] = '23 */6 * * * ' . $php . ' ' . escapeshellarg($privateRoot . '/broker-recovery.php')
        . ' >> ' . escapeshellarg($privateRoot . '/broker-recovery.log') . ' 2>&1';
    $lines[] = '*/15 * * * * ' . $php . ' ' . escapeshellarg($privateRoot . '/run-pwa-monitor.php')
        . ' >> ' . escapeshellarg($privateRoot . '/pwa-monitor-cron.log') . ' 2>&1';
    $temporary = tempnam(sys_get_temp_dir(), 'pa-installer-cron-');
    file_put_contents($temporary, implode("\n", $lines) . "\n", LOCK_EX);
    interruptAtCommitBoundary('before-cron');
    $installOutput = [];
    $installExit = 0;
    exec('/usr/bin/crontab ' . escapeshellarg($temporary) . ' 2>&1', $installOutput, $installExit);
    @unlink($temporary);
    if ($installExit !== 0) {
        throw new RuntimeException('Unable to install the Player Assistant cron entries.');
    }
    interruptAtCommitBoundary('after-cron');
    return $cronState;
}

function activateApiMaintenance(string $apiTarget): void
{
    if (file_exists($apiTarget) || is_link($apiTarget)) {
        removeTree($apiTarget);
    }
    ensureDirectory($apiTarget, 0755);
    $rules = "RewriteEngine On\nRewriteRule ^ - [R=503,L]\nErrorDocument 503 \"Player Assistant maintenance\"\n<IfModule mod_headers.c>\n  Header always set Retry-After \"120\"\n</IfModule>\n";
    if (file_put_contents($apiTarget . '/.htaccess', $rules, LOCK_EX) === false) {
        throw new RuntimeException('Unable to activate the API maintenance gate.');
    }
    @chmod($apiTarget . '/.htaccess', 0644);
}

function restoreFileAtomically(string $backup, string $target, int $mode, string $transactionId): void
{
    $backup = canonicalExistingPath($backup, 'rollback evidence for ' . basename($target), false);
    $temporary = dirname($target) . '/.' . basename($target) . '.rollback-' . $transactionId;
    @unlink($temporary);
    if (!copy($backup, $temporary)
        || !hash_equals((string)hash_file('sha256', $backup), (string)hash_file('sha256', $temporary))) {
        @unlink($temporary);
        throw new RuntimeException('Unable to stage verified rollback state for ' . basename($target) . '.');
    }
    @chmod($temporary, $mode);
    if ((file_exists($target) || is_link($target)) && !unlink($target)) {
        @unlink($temporary);
        throw new RuntimeException('Unable to replace ' . basename($target) . ' during rollback.');
    }
    if (!rename($temporary, $target)) {
        @unlink($temporary);
        throw new RuntimeException('Unable to promote rollback state for ' . basename($target) . '.');
    }
}

function snapshotFileVerified(string $source, string $destination, int $mode): void
{
    $source = canonicalExistingPath($source, 'rollback snapshot source', false);
    $sourceBytes = filesize($source);
    $sourceHash = hash_file('sha256', $source);
    if (!is_int($sourceBytes) || !is_string($sourceHash)
        || !copy($source, $destination)) {
        @unlink($destination);
        throw new RuntimeException('Unable to capture rollback evidence for ' . basename($source) . '.');
    }
    $destinationBytes = filesize($destination);
    $destinationHash = hash_file('sha256', $destination);
    if ($destinationBytes !== $sourceBytes
        || !is_string($destinationHash)
        || !hash_equals($sourceHash, $destinationHash)
        || filesize($source) !== $sourceBytes
        || !hash_equals($sourceHash, (string)hash_file('sha256', $source))) {
        @unlink($destination);
        throw new RuntimeException('Rollback evidence verification failed for ' . basename($source) . '.');
    }
    @chmod($destination, $mode);
}

function verifyRollbackEvidenceAgainstLive(string $backup, string $live): void
{
    $backup = canonicalExistingPath($backup, 'private-runtime rollback evidence', false);
    $live = canonicalExistingPath($live, 'live private-runtime target', false);
    $backupBytes = filesize($backup);
    $liveBytes = filesize($live);
    $backupHash = hash_file('sha256', $backup);
    $liveHash = hash_file('sha256', $live);
    if (!is_int($backupBytes) || $backupBytes !== $liveBytes
        || !is_string($backupHash) || !is_string($liveHash)
        || !hash_equals($backupHash, $liveHash)) {
        throw new RuntimeException('Private-runtime rollback evidence no longer matches the live target: '
            . basename($live));
    }
}

function directoryHashes(string $root): array
{
    $hashes = [];
    $iterator = new RecursiveIteratorIterator(
        new RecursiveDirectoryIterator($root, FilesystemIterator::SKIP_DOTS));
    foreach ($iterator as $entry) {
        if ($entry->isLink() || !$entry->isFile()) {
            throw new RuntimeException('Rollback directories may contain only regular files.');
        }
        $relative = str_replace('\\', '/', $iterator->getSubIterator()->getSubPathname());
        $hashes[$relative] = hash_file('sha256', $entry->getPathname());
    }
    ksort($hashes, SORT_STRING);
    return $hashes;
}

function cleanupFinalizedTransaction(string $transactionDirectory, array $transaction): void
{
    $manifestPath = $transactionDirectory . '/manifest.json';
    $transaction['status'] = 'finalized';
    $transaction['rollback_forbidden'] = true;
    $transaction['cleanup_complete'] = false;
    writeJson($manifestPath, $transaction);
    if (getenv('PLAYER_ASSISTANT_TEST_FINALIZE_AFTER_DURABLE_STATE') === '1') {
        throw new RuntimeException('Finalization interrupted after durable rollback-forbidden state.');
    }
    foreach (scandir($transactionDirectory) ?: [] as $entry) {
        if ($entry === '.' || $entry === '..' || $entry === 'manifest.json') {
            continue;
        }
        removeTree($transactionDirectory . '/' . $entry);
    }
    if (getenv('PLAYER_ASSISTANT_TEST_FINALIZE_CLEANUP_FAILURE_AFTER_MANIFEST') === '1') {
        file_put_contents($transactionDirectory . '/.cleanup-race-fixture', 'fault', LOCK_EX);
    }
    if (is_dir($transactionDirectory) && count(scandir($transactionDirectory) ?: []) > 3) {
        $transaction['status'] = 'finalize_cleanup';
        writeJson($manifestPath, $transaction);
        throw new RuntimeException('Unable to remove the finalized transaction evidence.');
    }
    $transaction['status'] = 'finalized';
    $transaction['rollback_forbidden'] = true;
    $transaction['cleanup_complete'] = true;
    writeJson($manifestPath, $transaction);
}

function rollbackTransaction(string $transactionDirectory, array $transaction): void
{
    $publicRoot = (string)$transaction['public_root'];
    $privateRoot = (string)$transaction['private_root'];
    $restorePublic = static function (string $key, string $target) use (&$transaction, $transactionDirectory): void {
        if (($transaction[$key . '_rollback_restored'] ?? false) === true) {
            return;
        }
        $started = ($transaction[$key . '_promotion_started'] ?? false) === true
            || ($transaction[$key . '_backup_move_started'] ?? false) === true
            || ($transaction[$key . '_promoted'] ?? false) === true
            || ($transaction[$key . '_backup_moved'] ?? false) === true;
        if (!$started) {
            return;
        }
        $backup = $transactionDirectory . '/rollback-' . $key;
        if (($transaction[$key . '_existed'] ?? false) === true) {
            if (!file_exists($backup) && !is_link($backup)) {
                if (($transaction[$key . '_backup_moved'] ?? false) !== true
                    && ($transaction[$key . '_promoted'] ?? false) !== true) {
                    return;
                }
                throw new RuntimeException("Rollback evidence is missing for $key.");
            }
            $backup = canonicalExistingPath($backup, "$key rollback directory", true);
            $temporary = dirname($target) . '/.' . basename($target) . '.rollback-'
                . $transaction['transaction_id'];
            if (file_exists($temporary) || is_link($temporary)) {
                removeTree($temporary);
            }
            copyTree($backup, $temporary, 0644, 0755);
            if (directoryHashes($backup) !== directoryHashes($temporary)) {
                removeTree($temporary);
                throw new RuntimeException("Unable to verify staged $key rollback state.");
            }
            if (file_exists($target) || is_link($target)) {
                removeTree($target);
            }
            if (!rename($temporary, $target)) {
                throw new RuntimeException("Unable to restore $key rollback state.");
            }
        } elseif (file_exists($target) || is_link($target)) {
            removeTree($target);
        }
        $transaction[$key . '_rollback_restored'] = true;
        writeJson($transactionDirectory . '/manifest.json', $transaction);
    };
    $pwaTarget = $publicRoot . '/scarlethorizons/pwa';
    $apiTarget = $publicRoot . '/scarlethorizons/api';
    $restorePublic('pwa', $pwaTarget);
    $apiChanged = ($transaction['api_rollback_restored'] ?? false) !== true
        && (($transaction['api_promotion_started'] ?? false) === true
            || ($transaction['api_promoted'] ?? false) === true
            || file_exists($transactionDirectory . '/rollback-api'));
    if ($apiChanged) {
        activateApiMaintenance($apiTarget);
    }
    $privateRollbackFiles = $transaction['private_promoted_files'] ?? [];
    if (is_string($transaction['private_file_in_progress'] ?? null)) {
        $privateRollbackFiles[] = $transaction['private_file_in_progress'];
    }
    if (($transaction['private_rollback_restored'] ?? false) !== true) {
        foreach (array_unique($privateRollbackFiles) as $file) {
            $existed = (bool)($transaction['private_files'][$file] ?? false);
            $target = $privateRoot . '/' . $file;
            $backup = $transactionDirectory . '/private/' . $file;
            if ($existed) {
                restoreFileAtomically($backup, $target, 0600, (string)$transaction['transaction_id']);
            } elseif ((file_exists($target) || is_link($target)) && !unlink($target)) {
                throw new RuntimeException("Unable to remove newly installed private runtime file: $file");
            }
        }
        $transaction['private_rollback_restored'] = true;
        writeJson($transactionDirectory . '/manifest.json', $transaction);
    }
    $configTarget = $privateRoot . '/config.php';
    if (($transaction['config_rollback_restored'] ?? false) !== true) {
        if (($transaction['config_promoted'] ?? false) === true
            || ($transaction['config_promotion_started'] ?? false) === true) {
            if (($transaction['config_existed'] ?? false) === true) {
                restoreFileAtomically(
                    $transactionDirectory . '/config.php',
                    $configTarget,
                    0600,
                    (string)$transaction['transaction_id']);
            } elseif ((file_exists($configTarget) || is_link($configTarget)) && !unlink($configTarget)) {
                throw new RuntimeException('Unable to remove newly installed private configuration.');
            }
        }
        $transaction['config_rollback_restored'] = true;
        writeJson($transactionDirectory . '/manifest.json', $transaction);
    }
    $databaseTarget = $privateRoot . '/broker.sqlite';
    if (($transaction['database_mutation_started'] ?? false) === true
        && ($transaction['database_rollback_restored'] ?? false) !== true) {
        if (($transaction['database_existed'] ?? false) === true) {
            $databaseBackup = canonicalExistingPath(
                $transactionDirectory . '/broker.sqlite',
                'broker database rollback snapshot',
                false);
            $restoreTemporary = $privateRoot . '/.broker.sqlite.rollback-' . $transaction['transaction_id'];
            if (!copy($databaseBackup, $restoreTemporary)) {
                throw new RuntimeException('Unable to stage the broker database rollback.');
            }
            @chmod($restoreTemporary, 0600);
            $restore = new PDO('sqlite:' . $restoreTemporary, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
            $expectedVersion = (int)($transaction['database_previous_version'] ?? -1);
            $expectedJournalMode = strtolower((string)($transaction['database_previous_journal_mode'] ?? ''));
            if (!in_array($expectedJournalMode, ['delete', 'truncate', 'persist', 'wal'], true)) {
                $restore = null;
                @unlink($restoreTemporary);
                throw new RuntimeException('The database rollback lacks a supported prior journal mode.');
            }
            if ((string)$restore->query('PRAGMA integrity_check')->fetchColumn() !== 'ok'
                || (int)$restore->query('PRAGMA user_version')->fetchColumn() !== $expectedVersion) {
                $restore = null;
                @unlink($restoreTemporary);
                throw new RuntimeException('The staged broker database rollback failed verification.');
            }
            $restore = null;
            @unlink($databaseTarget . '-wal');
            @unlink($databaseTarget . '-shm');
            if ((file_exists($databaseTarget) || is_link($databaseTarget)) && !unlink($databaseTarget)) {
                @unlink($restoreTemporary);
                throw new RuntimeException('Unable to replace the migrated broker database.');
            }
            if (!rename($restoreTemporary, $databaseTarget)) {
                @unlink($restoreTemporary);
                throw new RuntimeException('Unable to promote the broker database rollback.');
            }
            @chmod($databaseTarget, 0600);
            $restoredDatabase = new PDO(
                'sqlite:' . $databaseTarget,
                null,
                null,
                [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
            $restoredJournalMode = strtolower((string)$restoredDatabase
                ->query('PRAGMA journal_mode = ' . strtoupper($expectedJournalMode))
                ->fetchColumn());
            if ($restoredJournalMode !== $expectedJournalMode
                || (string)$restoredDatabase->query('PRAGMA integrity_check')->fetchColumn() !== 'ok'
                || (int)$restoredDatabase->query('PRAGMA user_version')->fetchColumn() !== $expectedVersion) {
                $restoredDatabase = null;
                throw new RuntimeException('The promoted broker database rollback failed final verification.');
            }
            $restoredDatabase = null;
            foreach ([$databaseTarget . '-wal', $databaseTarget . '-shm'] as $sidecar) {
                if (is_file($sidecar)) {
                    @chmod($sidecar, 0600);
                }
            }
        } else {
            @unlink($databaseTarget . '-wal');
            @unlink($databaseTarget . '-shm');
            if ((file_exists($databaseTarget) || is_link($databaseTarget)) && !unlink($databaseTarget)) {
                throw new RuntimeException('Unable to remove the newly created broker database.');
            }
        }
    }
    if (($transaction['database_rollback_restored'] ?? false) !== true) {
        $transaction['database_rollback_restored'] = true;
        writeJson($transactionDirectory . '/manifest.json', $transaction);
    }
    if (($transaction['cron_rollback_restored'] ?? false) !== true) {
        if (($transaction['cron']['managed'] ?? false) === true) {
            $cronBackup = $transactionDirectory . '/crontab.txt';
            $output = [];
            $exit = 0;
            if (($transaction['cron']['original_existed'] ?? false) === true) {
                $cronBackup = canonicalExistingPath($cronBackup, 'crontab rollback snapshot', false);
                exec('/usr/bin/crontab ' . escapeshellarg($cronBackup) . ' 2>&1', $output, $exit);
            } else {
                exec('/usr/bin/crontab -r 2>&1', $output, $exit);
                if ($exit !== 0) {
                    $checkOutput = [];
                    $checkExit = 0;
                    exec('/usr/bin/crontab -l 2>&1', $checkOutput, $checkExit);
                    if ($checkExit !== 0
                        && str_contains(strtolower(implode("\n", $checkOutput)), 'no crontab')) {
                        $exit = 0;
                    }
                }
            }
            if ($exit !== 0) {
                throw new RuntimeException('Unable to restore the original crontab.');
            }
        }
        $transaction['cron_rollback_restored'] = true;
        writeJson($transactionDirectory . '/manifest.json', $transaction);
    }
    if ($apiChanged) {
        $restorePublic('api', $apiTarget);
    }
    foreach (['pwa', 'api'] as $component) {
        if (($transaction[$component . '_rollback_restored'] ?? false) !== true) {
            $transaction[$component . '_rollback_restored'] = true;
        }
    }
    $transaction['status'] = 'rollback_cleanup';
    writeJson($transactionDirectory . '/manifest.json', $transaction);
    foreach ([
        $privateRoot . '/.*.install-' . $transaction['transaction_id'],
        $privateRoot . '/.*.rollback-' . $transaction['transaction_id'],
        dirname($pwaTarget) . '/.*.rollback-' . $transaction['transaction_id'],
    ] as $pattern) {
        foreach (glob($pattern) ?: [] as $temporaryArtifact) {
            if (file_exists($temporaryArtifact) || is_link($temporaryArtifact)) {
                removeTree($temporaryArtifact);
            }
        }
    }
    if (($transaction['private_root_existed'] ?? true) === false && is_dir($privateRoot)) {
        removeTree($privateRoot);
    }
    foreach (['stage', 'private', 'rollback-pwa', 'rollback-api'] as $directory) {
        $path = $transactionDirectory . '/' . $directory;
        if (is_dir($path)) {
            removeTree($path);
        }
    }
    foreach (['materialized-config.php', 'config.php', 'broker.sqlite', 'crontab.txt'] as $file) {
        $path = $transactionDirectory . '/' . $file;
        if (is_file($path)) {
            unlink($path);
        }
    }
    $packageManifestPath = $transactionDirectory . '/package-manifest.json';
    if (is_file($packageManifestPath)) {
        $packageManifestPath = canonicalExistingPath(
            $packageManifestPath,
            'rollback package manifest',
            false);
    } elseif (is_link($packageManifestPath)) {
        throw new RuntimeException('The rollback package manifest cannot be a symbolic link.');
    }
    $packageManifest = is_file($packageManifestPath)
        ? json_decode((string)file_get_contents($packageManifestPath), true, 32, JSON_THROW_ON_ERROR)
        : null;
    writeInstallationReport(
        $transaction,
        is_array($packageManifest) ? $packageManifest : null,
        'rolled_back',
        ['local' => false, 'https' => false, 'rollback' => true],
        false,
        true);
    $transaction['status'] = 'rolled_back';
    $transaction['cleanup_complete'] = true;
    writeJson($transactionDirectory . '/manifest.json', $transaction);
}

function assertNoPendingTransactions(string $accountHome): void
{
    $root = $accountHome . '/.player-assistant-installer-transactions';
    if (!is_dir($root)) {
        return;
    }
    foreach (glob($root . '/*/manifest.json') ?: [] as $manifestPath) {
        try {
            $manifest = json_decode((string)file_get_contents($manifestPath), true, 32, JSON_THROW_ON_ERROR);
        } catch (Throwable) {
            reject('An installer transaction manifest is unreadable: ' . $manifestPath);
        }
        if (!is_array($manifest)) {
            reject('An installer transaction manifest is invalid: ' . $manifestPath);
        }
        $status = $manifest['status'] ?? null;
        if (in_array($status, ['preparing', 'promoted', 'pending_https_verification', 'rollback_cleanup', 'finalize_cleanup'], true)
            || ($status === 'finalized' && ($manifest['cleanup_complete'] ?? false) !== true)) {
            reject('An unresolved installer transaction already exists: ' . (string)($manifest['transaction_id'] ?? 'unknown'));
        }
        if (!in_array($status, ['verified', 'rolled_back', 'finalized'], true)) {
            reject('An installer transaction has an unknown state: ' . (string)($manifest['transaction_id'] ?? 'unknown'));
        }
    }
}

function runInstall(array $options): array
{
    $origin = normalizeOrigin((string)$options['origin']);
    $package = canonicalExistingPath((string)$options['package'], 'payload package', false);
    $publicRoot = canonicalExistingPath((string)$options['public-root'], 'public document root', true);
    $privateRoot = normalizePath((string)$options['private-root']);
    if (is_link($privateRoot)) {
        reject('The private root cannot be a symbolic link.');
    }
    if (is_dir($privateRoot)) {
        $privateRoot = canonicalExistingPath($privateRoot, 'private root', true);
    }
    if (!is_dir($publicRoot)) {
        reject('The public document root does not exist.');
    }
    $originHost = (string)parse_url($origin, PHP_URL_HOST);
    if (strtolower(basename($publicRoot)) !== strtolower($originHost)) {
        reject('The public document-root name must match the target origin host.');
    }
    $accountHome = dirname($publicRoot);
    if (dirname($privateRoot) !== $accountHome
        || str_starts_with($privateRoot . '/', $publicRoot . '/')) {
        reject('The private root must be a sibling of the target document root.');
    }
    $installerLock = acquireInstallerLock($accountHome);
    assertNoPendingTransactions($accountHome);
    $configTarget = $privateRoot . '/config.php';
    $configSource = isset($options['config-source']) && is_string($options['config-source'])
        ? canonicalExistingPath($options['config-source'], 'private configuration source', false)
        : $configTarget;
    if (is_link($configTarget)) {
        reject('The installed private configuration cannot be a symbolic link.');
    }
    if (!isset($options['config-source']) && is_file($configTarget)) {
        $configSource = canonicalExistingPath($configTarget, 'installed private configuration', false);
    }
    if (str_starts_with($configSource . '/', $publicRoot . '/')) {
        reject('The private configuration source cannot be inside the document root.');
    }
    $verification = (string)($options['verification'] ?? 'https');
    if (!in_array($verification, ['https', 'local'], true)) {
        reject('The verification mode must be https or local.');
    }

    $transactionId = gmdate('Ymd\THis\Z') . '-' . bin2hex(random_bytes(4));
    $transactionDirectory = $accountHome . '/.player-assistant-installer-transactions/' . $transactionId;
    $reportDirectory = $accountHome . '/.player-assistant-install-reports';
    ensureDirectory($transactionDirectory, 0700);
    ensureDirectory($reportDirectory, 0700);
    $stage = $transactionDirectory . '/stage';
    $transaction = [
        'schema_version' => INSTALLER_SCHEMA_VERSION,
        'transaction_id' => $transactionId,
        'transaction_directory' => $transactionDirectory,
        'report_path' => $reportDirectory . '/' . $transactionId . '.json',
        'status' => 'preparing',
        'origin' => $origin,
        'public_root' => $publicRoot,
        'private_root' => $privateRoot,
        'private_root_existed' => is_dir($privateRoot),
        'pwa_existed' => false,
        'api_existed' => false,
        'pwa_promoted' => false,
        'api_promoted' => false,
        'pwa_promotion_started' => false,
        'api_promotion_started' => false,
        'pwa_backup_moved' => false,
        'api_backup_moved' => false,
        'pwa_backup_move_started' => false,
        'api_backup_move_started' => false,
        'api_maintenance_active' => false,
        'config_existed' => is_file($configTarget),
        'config_promoted' => false,
        'config_promotion_started' => false,
        'database_existed' => false,
        'database_mutation_started' => false,
        'private_files' => [],
        'private_promoted_files' => [],
        'private_file_in_progress' => null,
        'cron' => ['managed' => false, 'original_existed' => false],
    ];
    writeJson($transactionDirectory . '/manifest.json', $transaction);

    try {
        $materializedConfig = $transactionDirectory . '/materialized-config.php';
        materializeConfigSource($configSource, $materializedConfig, $origin, $privateRoot, $accountHome);
        loadConfig($materializedConfig, $origin, $privateRoot);
        if (is_file($configTarget)
            && !hash_equals(hash_file('sha256', $configTarget), hash_file('sha256', $materializedConfig))) {
            reject('An existing private configuration differs from --config-source; update it explicitly before deployment.');
        }
        interruptAtCommitBoundary('before-installer-replacement');
        $packageManifest = extractAndVerifyPackage($package, $stage);
        interruptAtCommitBoundary('after-installer-replacement');
        writeJson($transactionDirectory . '/package-manifest.json', $packageManifest);
        $stagedPublic = $stage . '/payload/public/scarlethorizons';
        $stagedPrivate = $stage . '/payload/private';
        $templatePath = $stagedPublic . '/api/index.php.template';
        $template = (string)file_get_contents($templatePath);
        if (substr_count($template, '__PLAYER_ASSISTANT_PRIVATE_ROOT__') !== 1) {
            reject('The API private-root substitution point is missing or ambiguous.');
        }
        $materialized = str_replace(
            '__PLAYER_ASSISTANT_PRIVATE_ROOT__',
            var_export($privateRoot, true),
            $template);
        file_put_contents($stagedPublic . '/api/index.php', $materialized, LOCK_EX);
        @chmod($stagedPublic . '/api/index.php', 0644);
        unlink($templatePath);
        applyTreePermissions($stagedPublic . '/pwa', 0644, 0755);
        applyTreePermissions($stagedPublic . '/api', 0644, 0755);
        applyTreePermissions($stagedPrivate, 0600, 0700);
        lintPhp($stagedPublic . '/api/index.php');
        foreach (glob($stagedPrivate . '/*.php') ?: [] as $phpFile) {
            lintPhp($phpFile);
        }

        ensureDirectory($transactionDirectory . '/private', 0700);
        ensureDirectory($privateRoot, 0700);
        foreach ($packageManifest['files'] as $entry) {
            $path = (string)$entry['path'];
            if (!str_starts_with($path, 'payload/private/')) {
                continue;
            }
            $file = substr($path, strlen('payload/private/'));
            $target = $privateRoot . '/' . $file;
            if (is_link($target)) {
                reject("The private runtime target cannot be a symbolic link: $file");
            }
            if (file_exists($target) && !is_file($target)) {
                reject("The private runtime target must be a regular file: $file");
            }
            $existed = is_file($target);
            $transaction['private_files'][$file] = $existed;
            if ($existed) {
                snapshotFileVerified($target, $transactionDirectory . '/private/' . $file, 0600);
            }
        }
        if ($transaction['config_existed']) {
            snapshotFileVerified($configTarget, $transactionDirectory . '/config.php', 0600);
        } else {
            if (!copy($materializedConfig, $stagedPrivate . '/config.php')) {
                throw new RuntimeException('Unable to stage private configuration.');
            }
            @chmod($stagedPrivate . '/config.php', 0600);
        }
        if ($transaction['config_existed']) {
            copy($configTarget, $stagedPrivate . '/config.php');
            @chmod($stagedPrivate . '/config.php', 0600);
        }
        $scarletRoot = $publicRoot . '/scarlethorizons';
        ensureDirectory($scarletRoot, 0755);
        $apiTarget = $scarletRoot . '/api';
        if (is_link($apiTarget)) {
            reject('The existing API target cannot be a symbolic link.');
        }
        $transaction['api_existed'] = is_dir($apiTarget);
        if ($transaction['api_existed']) {
            directoryHashes($apiTarget);
            $transaction['api_backup_move_started'] = true;
            writeJson($transactionDirectory . '/manifest.json', $transaction);
            if (!rename($apiTarget, $transactionDirectory . '/rollback-api')) {
                throw new RuntimeException('Unable to snapshot the existing API directory.');
            }
            $transaction['api_backup_moved'] = true;
            writeJson($transactionDirectory . '/manifest.json', $transaction);
            activateApiMaintenance($apiTarget);
            $transaction['api_maintenance_active'] = true;
            writeJson($transactionDirectory . '/manifest.json', $transaction);
        }

        $databasePath = $privateRoot . '/broker.sqlite';
        if (is_link($databasePath)) {
            reject('The broker database cannot be a symbolic link.');
        }
        $databaseSnapshot = snapshotDatabase($databasePath, $transactionDirectory . '/broker.sqlite');
        $transaction['database_existed'] = $databaseSnapshot !== null;
        $transaction['database_previous_version'] = $databaseSnapshot['version'] ?? null;
        $transaction['database_previous_journal_mode'] = $databaseSnapshot['journal_mode'] ?? null;
        $transaction['database_mutation_started'] = true;
        writeJson($transactionDirectory . '/manifest.json', $transaction);

        runMigration($stagedPrivate);

        $privateFiles = array_keys($transaction['private_files']);
        usort($privateFiles, static function (string $left, string $right): int {
            $priority = static function (string $file): int {
                if ($file === 'BrokerService.php') {
                    return 1;
                }
                return in_array($file, [
                    'broker-maintenance.php', 'broker-recovery.php', 'migrate-broker.php',
                    'refresh-word-counts.php', 'run-pwa-monitor.php',
                ], true) ? 2 : 0;
            };
            return [$priority($left), $left] <=> [$priority($right), $right];
        });
        foreach ($privateFiles as $file) {
            $transaction['private_file_in_progress'] = $file;
            writeJson($transactionDirectory . '/manifest.json', $transaction);
            $temporary = $privateRoot . '/.' . $file . '.install-' . $transactionId;
            if (!copy($stagedPrivate . '/' . $file, $temporary)) {
                throw new RuntimeException("Unable to stage private runtime file: $file");
            }
            @chmod($temporary, 0600);
            if (($transaction['private_files'][$file] ?? false) === true) {
                verifyRollbackEvidenceAgainstLive(
                    $transactionDirectory . '/private/' . $file,
                    $privateRoot . '/' . $file);
            }
            if (!rename($temporary, $privateRoot . '/' . $file)) {
                throw new RuntimeException("Unable to promote private runtime file: $file");
            }
            $transaction['private_promoted_files'][] = $file;
            $transaction['private_file_in_progress'] = null;
            writeJson($transactionDirectory . '/manifest.json', $transaction);
        }
        if (!$transaction['config_existed']) {
            $transaction['config_promotion_started'] = true;
            writeJson($transactionDirectory . '/manifest.json', $transaction);
            interruptAtCommitBoundary('before-private-config');
            $temporaryConfig = $privateRoot . '/.config.php.install-' . $transactionId;
            copy($stagedPrivate . '/config.php', $temporaryConfig);
            @chmod($temporaryConfig, 0600);
            if (!rename($temporaryConfig, $configTarget)) {
                throw new RuntimeException('Unable to promote private configuration.');
            }
            $transaction['config_promoted'] = true;
            writeJson($transactionDirectory . '/manifest.json', $transaction);
            interruptAtCommitBoundary('after-private-config');
        }
        @chmod($configTarget, 0600);
        @chmod($databasePath, 0600);

        foreach (['pwa', 'api'] as $component) {
            $target = $scarletRoot . '/' . $component;
            if ($component === 'api' && $transaction['api_maintenance_active']) {
                removeTree($target);
            } else {
                if (is_link($target)) {
                    reject("The existing $component target cannot be a symbolic link.");
                }
                $transaction[$component . '_existed'] = is_dir($target);
                if ($transaction[$component . '_existed']) {
                    directoryHashes($target);
                    $transaction[$component . '_backup_move_started'] = true;
                    writeJson($transactionDirectory . '/manifest.json', $transaction);
                    if (!rename($target, $transactionDirectory . '/rollback-' . $component)) {
                        throw new RuntimeException("Unable to snapshot the existing $component directory.");
                    }
                    $transaction[$component . '_backup_moved'] = true;
                    writeJson($transactionDirectory . '/manifest.json', $transaction);
                }
            }
            $transaction[$component . '_promotion_started'] = true;
            writeJson($transactionDirectory . '/manifest.json', $transaction);
            if ($component === 'pwa') {
                interruptAtCommitBoundary('before-public-loader-pwa');
            } else {
                interruptAtCommitBoundary('before-public-loader-api');
            }
            if (!rename($stagedPublic . '/' . $component, $target)) {
                throw new RuntimeException("Unable to promote the $component directory.");
            }
            $transaction[$component . '_promoted'] = true;
            if ($component === 'api') {
                $transaction['api_maintenance_active'] = false;
            }
            writeJson($transactionDirectory . '/manifest.json', $transaction);
            if ($component === 'pwa') {
                interruptAtCommitBoundary('after-public-loader-pwa');
            } else {
                interruptAtCommitBoundary('after-public-loader-api');
            }
        }
        $transaction['status'] = 'promoted';
        writeJson($transactionDirectory . '/manifest.json', $transaction);

        if (!isset($options['skip-cron'])) {
            $transaction['cron'] = installCron($privateRoot, $transactionDirectory, $transaction);
            writeJson($transactionDirectory . '/manifest.json', $transaction);
        }

        require_once $privateRoot . '/DatabaseMigrationService.php';
        verifyLocalInstall($packageManifest, $publicRoot, $privateRoot);
        $transaction['migration_version'] = DatabaseMigrationService::LATEST_VERSION;
        writeJson($transactionDirectory . '/manifest.json', $transaction);
        if ($verification === 'https') {
            verifyHttpsInstall($packageManifest, $origin);
            $rollbackRetained = isset($options['retain-backup']);
            if ($rollbackRetained) {
                $transaction['status'] = 'verified';
                $transaction['cleanup_complete'] = false;
            } else {
                $transaction['status'] = 'finalized';
                $transaction['rollback_forbidden'] = true;
                $transaction['cleanup_complete'] = false;
                $transaction['package_manifest'] = $packageManifest;
            }
            writeJson($transactionDirectory . '/manifest.json', $transaction);
            if ($rollbackRetained) {
                writeInstallationReport(
                    $transaction,
                    $packageManifest,
                    'installed',
                    ['local' => true, 'https' => true, 'rollback' => false],
                    true,
                    false);
            } else {
                cleanupFinalizedTransaction($transactionDirectory, $transaction);
                writeInstallationReport(
                    $transaction,
                    $packageManifest,
                    'installed',
                    ['local' => true, 'https' => true, 'rollback' => false],
                    false,
                    true);
            }
            return [
                'status' => 'installed',
                'version' => $packageManifest['version'],
                'origin' => $origin,
                'pwa_url' => $origin . PWA_PATH,
                'api_url' => $origin . API_PATH,
                'transaction_id' => $transactionId,
                'report_path' => $transaction['report_path'],
                'rollback_retained' => $rollbackRetained,
                'cron_installed' => !isset($options['skip-cron']),
            ];
        }
        $transaction['status'] = 'pending_https_verification';
        writeJson($transactionDirectory . '/manifest.json', $transaction);
        if (is_dir($stage)) {
            removeTree($stage);
        }
        writeInstallationReport(
            $transaction,
            $packageManifest,
            'installed_pending_https_verification',
            ['local' => true, 'https' => false, 'rollback' => false],
            true,
            false);
        return [
            'status' => 'installed_pending_https_verification',
            'version' => $packageManifest['version'],
            'origin' => $origin,
            'transaction_id' => $transactionId,
            'transaction_directory' => $transactionDirectory,
            'report_path' => $transaction['report_path'],
            'cron_installed' => !isset($options['skip-cron']),
        ];
    } catch (Throwable $error) {
        if (in_array(($transaction['status'] ?? null), ['verified', 'finalized', 'finalize_cleanup'], true)) {
            try {
                writeInstallationReport(
                    $transaction,
                    isset($packageManifest) && is_array($packageManifest) ? $packageManifest : null,
                    'installed_cleanup_failed',
                    ['local' => true, 'https' => true, 'rollback' => false],
                    false,
                    false);
            } catch (Throwable $reportError) {
                throw new RuntimeException(
                    'Installation and HTTPS verification succeeded, but cleanup and cleanup-failure reporting failed. Do not roll back automatically.',
                    0,
                    $error);
            }
            throw new RuntimeException(
                'Installation and HTTPS verification succeeded, but rollback-evidence cleanup failed. Do not roll back automatically.',
                0,
                $error);
        }
        $transactionPath = $transactionDirectory . '/manifest.json';
        if (is_file($transactionPath)) {
            $current = json_decode((string)file_get_contents($transactionPath), true, 32, JSON_THROW_ON_ERROR);
            $mutated = ($current['database_mutation_started'] ?? false)
                || ($current['config_promoted'] ?? false)
                || ($current['config_promotion_started'] ?? false)
                || ($current['pwa_promoted'] ?? false)
                || ($current['api_promoted'] ?? false)
                || ($current['pwa_promotion_started'] ?? false)
                || ($current['api_promotion_started'] ?? false)
                || ($current['pwa_backup_moved'] ?? false)
                || ($current['api_backup_moved'] ?? false)
                || ($current['pwa_backup_move_started'] ?? false)
                || ($current['api_backup_move_started'] ?? false)
                || ($current['private_promoted_files'] ?? []) !== []
                || is_string($current['private_file_in_progress'] ?? null)
                || ($current['cron']['managed'] ?? false);
            if ($mutated) {
                try {
                    rollbackTransaction($transactionDirectory, $current);
                } catch (Throwable $rollbackError) {
                    $current['status'] = 'rollback-failed';
                    $current['rollback_failure'] = $rollbackError->getMessage();
                    $current['rollback_forbidden'] = false;
                    writeJson($transactionPath, $current, 0600);
                    throw new RuntimeException('Transaction rollback failed; recovery evidence preserved.', 0, $rollbackError);
                }
            } else {
                removeTree($transactionDirectory);
            }
        }
        throw $error;
    }
}

function validateTransactionManifest(array $transaction, string $expectedId): void
{
    if (($transaction['schema_version'] ?? null) !== INSTALLER_SCHEMA_VERSION
        || ($transaction['transaction_id'] ?? null) !== $expectedId
        || !is_array($transaction['private_files'] ?? null)
        || !is_array($transaction['private_promoted_files'] ?? null)
        || !is_array($transaction['cron'] ?? null)) {
        reject('The installer transaction manifest has an invalid structure.');
    }
    $requiredBooleanState = [
        'private_root_existed', 'pwa_existed', 'api_existed',
        'pwa_promoted', 'api_promoted',
        'pwa_promotion_started', 'api_promotion_started',
        'pwa_backup_moved', 'api_backup_moved',
        'pwa_backup_move_started', 'api_backup_move_started',
        'api_maintenance_active', 'config_existed', 'config_promoted',
        'config_promotion_started', 'database_existed', 'database_mutation_started',
    ];
    foreach ($requiredBooleanState as $field) {
        if (!array_key_exists($field, $transaction) || !is_bool($transaction[$field])) {
            reject('The installer transaction contains invalid destructive state.');
        }
    }
    foreach ([
        'pwa_rollback_restored', 'api_rollback_restored', 'private_rollback_restored',
        'config_rollback_restored', 'database_rollback_restored', 'cron_rollback_restored',
        'cleanup_complete', 'rollback_forbidden',
    ] as $optionalBooleanState) {
        if (array_key_exists($optionalBooleanState, $transaction)
            && !is_bool($transaction[$optionalBooleanState])) {
            reject('The installer transaction contains invalid destructive state.');
        }
    }
    foreach ($transaction['private_files'] as $file => $existed) {
        if (!is_string($file)
            || preg_match('/^[A-Za-z0-9._-]+$/D', $file) !== 1
            || !is_bool($existed)) {
            reject('The installer transaction contains an invalid private-runtime path.');
        }
    }
    foreach ($transaction['private_promoted_files'] as $file) {
        if (!is_string($file) || !array_key_exists($file, $transaction['private_files'])) {
            reject('The installer transaction contains an invalid promoted private-runtime path.');
        }
    }
    $inProgress = $transaction['private_file_in_progress'] ?? null;
    if ($inProgress !== null
        && (!is_string($inProgress) || !array_key_exists($inProgress, $transaction['private_files']))) {
        reject('The installer transaction contains an invalid in-progress private-runtime path.');
    }
    if (!is_bool($transaction['cron']['managed'] ?? null)
        || !is_bool($transaction['cron']['original_existed'] ?? null)) {
        reject('The installer transaction contains invalid crontab state.');
    }
}

function transactionAction(array $options, string $action): array
{
    foreach (['origin', 'public-root', 'private-root'] as $required) {
        if (!isset($options[$required]) || !is_string($options[$required])) {
            reject("Missing required --$required argument for transaction action.");
        }
    }
    $origin = normalizeOrigin($options['origin']);
    $publicRoot = canonicalExistingPath($options['public-root'], 'public document root', true);
    $privateRoot = normalizePath($options['private-root']);
    assertNoSymlinkComponents($privateRoot);
    if (is_dir($privateRoot)) {
        $privateRoot = canonicalExistingPath($privateRoot, 'private root', true);
    } elseif (file_exists($privateRoot) || is_link($privateRoot)) {
        reject('The private root has the wrong type or is a symbolic link.');
    }
    $accountHome = dirname($publicRoot);
    if (dirname($privateRoot) !== $accountHome) {
        reject('The private root must remain a sibling of the target document root.');
    }
    $installerLock = acquireInstallerLock($accountHome);
    assertNoSymlinkComponents($publicRoot . '/scarlethorizons/api');
    assertNoSymlinkComponents($publicRoot . '/scarlethorizons/pwa');
    $id = (string)$options[$action . '-transaction'];
    if (preg_match('/^[0-9]{8}T[0-9]{6}Z-[a-f0-9]{8}$/', $id) !== 1) {
        reject('The transaction ID is invalid.');
    }
    $transactionRoot = canonicalExistingPath(
        $accountHome . '/.player-assistant-installer-transactions',
        'installer transaction root',
        true);
    $transactionDirectory = canonicalExistingPath(
        $transactionRoot . '/' . $id,
        'installer transaction directory',
        true);
    $manifestPath = canonicalExistingPath(
        $transactionDirectory . '/manifest.json',
        'installer transaction manifest',
        false);
    if (!is_file($manifestPath)) {
        reject('The requested installer transaction does not exist.');
    }
    $transaction = json_decode((string)file_get_contents($manifestPath), true, 32, JSON_THROW_ON_ERROR);
    validateTransactionManifest($transaction, $id);
    if (($transaction['origin'] ?? null) !== $origin
        || ($transaction['public_root'] ?? null) !== $publicRoot
        || ($transaction['private_root'] ?? null) !== $privateRoot
        || ($transaction['transaction_directory'] ?? null) !== $transactionDirectory) {
        reject('The installer transaction does not match the requested target.');
    }
    $reportDirectory = canonicalExistingPath(
        $accountHome . '/.player-assistant-install-reports',
        'installer report directory',
        true);
    $expectedReportPath = $reportDirectory . '/' . $id . '.json';
    assertNoSymlinkComponents($expectedReportPath);
    if (is_link($expectedReportPath)
        || (($transaction['report_path'] ?? null) !== $expectedReportPath)) {
        reject('The installer transaction report path is invalid.');
    }
    if (is_file($expectedReportPath)) {
        canonicalExistingPath($expectedReportPath, 'installer report', false);
    }
    $transactionStatus = (string)($transaction['status'] ?? '');
    if ($action === 'rollback'
        && (($transaction['rollback_forbidden'] ?? false) === true
            || in_array($transactionStatus, ['finalized'], true))) {
        reject('The installer transaction is finalized and rollback is forbidden.');
    }
    if ($action === 'rollback'
        && !in_array($transactionStatus, ['preparing', 'promoted', 'pending_https_verification', 'rollback_cleanup'], true)) {
        reject('The installer transaction is not in a recoverable state.');
    }
    if ($action === 'finalize'
        && !in_array($transactionStatus, ['pending_https_verification', 'finalize_cleanup', 'finalized'], true)) {
        reject('The installer transaction is not pending HTTPS verification.');
    }
    if ($action === 'rollback') {
        rollbackTransaction($transactionDirectory, $transaction);
        return [
            'status' => 'rolled_back',
            'transaction_id' => $id,
            'report_path' => $transaction['report_path'],
        ];
    }
    $packageManifestPath = $transactionDirectory . '/package-manifest.json';
    if (!is_array($transaction['package_manifest'] ?? null) && is_file($packageManifestPath)) {
        $packageManifestPath = canonicalExistingPath(
            $packageManifestPath,
            'pending transaction package manifest',
            false);
    }
    $packageManifest = is_array($transaction['package_manifest'] ?? null)
        ? $transaction['package_manifest']
        : (is_file($packageManifestPath)
            ? json_decode((string)file_get_contents($packageManifestPath), true, 32, JSON_THROW_ON_ERROR)
            : null);
    if (!is_array($packageManifest)) {
        reject('The pending transaction lacks its package manifest.');
    }
    validatePackageManifestContract($packageManifest);
    if ($transactionStatus === 'pending_https_verification') {
        require_once $privateRoot . '/DatabaseMigrationService.php';
        verifyLocalInstall($packageManifest, $publicRoot, $privateRoot);
        verifyHttpsInstall($packageManifest, $origin);
        $transaction['status'] = 'finalize_cleanup';
        $transaction['package_manifest'] = $packageManifest;
        writeJson($manifestPath, $transaction);
    }
    $reportPath = $transaction['report_path'];
    writeInstallationReport(
        $transaction,
        $packageManifest,
        'finalize_cleanup',
        ['local' => true, 'https' => true, 'rollback' => false],
        false,
        false);
    cleanupFinalizedTransaction($transactionDirectory, $transaction);
    writeInstallationReport(
        $transaction,
        $packageManifest,
        'finalized',
        ['local' => true, 'https' => true, 'rollback' => false],
        false,
        true);
    return ['status' => 'finalized', 'transaction_id' => $id, 'report_path' => $reportPath];
}

if (in_array('--help', $argv, true) || count($argv) === 1) {
    fwrite(STDOUT, installerUsage() . PHP_EOL);
    exit(0);
}

try {
    validateRawArguments($argv);
    $options = getopt('', [
        'package:',
        'origin:',
        'public-root:',
        'private-root:',
        'config-source:',
        'verification:',
        'skip-cron',
        'retain-backup',
        'rollback-transaction:',
        'finalize-transaction:',
        'help',
    ]);
    if (isset($options['rollback-transaction']) && isset($options['finalize-transaction'])) {
        reject('Choose only one transaction action.');
    }
    if (isset($options['rollback-transaction'])) {
        $result = transactionAction($options, 'rollback');
    } elseif (isset($options['finalize-transaction'])) {
        $result = transactionAction($options, 'finalize');
    } else {
        foreach (['package', 'origin', 'public-root', 'private-root'] as $required) {
            if (!isset($options[$required]) || !is_string($options[$required]) || trim($options[$required]) === '') {
                reject("Missing required --$required argument.");
            }
        }
        $targetConfig = normalizePath((string)$options['private-root']) . '/config.php';
        if (!is_file($targetConfig)
            && (!isset($options['config-source']) || !is_string($options['config-source']) || trim($options['config-source']) === '')) {
            reject('Missing required --config-source argument for a new installation.');
        }
        $result = runInstall($options);
    }
    fwrite(STDOUT, json_encode($result, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR) . PHP_EOL);
    exit(0);
} catch (Throwable $error) {
    fwrite(STDERR, 'Installation rejected: ' . $error->getMessage() . PHP_EOL);
    exit(2);
}
