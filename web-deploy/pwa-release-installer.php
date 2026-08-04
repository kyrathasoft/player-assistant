<?php
declare(strict_types=1);

function fail(string $message): never
{
    throw new RuntimeException($message);
}

function statePath(array $data): string
{
    return $data['release_root'] . '/transaction.json';
}

function temporaryLinkPath(array $data): string
{
    return dirname($data['directory']) . '/.' . basename($data['directory'])
        . '-link-' . $data['release_id'];
}

function previousReleasePath(array $data): string
{
    return $data['release_root'] . '/previous-' . $data['release_id'];
}

function saveState(array $data, array $state): void
{
    $path = statePath($data);
    $temporary = $path . '.tmp';
    $encoded = json_encode($state, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
    if (file_put_contents($temporary, $encoded, LOCK_EX) === false || !rename($temporary, $path)) {
        @unlink($temporary);
        fail('Unable to persist PWA release transaction state.');
    }
}

function loadState(array $data): array
{
    $path = statePath($data);
    if (!is_file($path)) {
        fail('PWA release transaction state is missing.');
    }
    return json_decode((string) file_get_contents($path), true, 32, JSON_THROW_ON_ERROR);
}

function validateState(array $data, array $state): void
{
    $activeRelease = $state['active_release'] ?? null;
    $previousRelease = $state['previous_release'] ?? null;
    if (!is_string($activeRelease)
        || !is_string($previousRelease)
        || $activeRelease !== $data['stage']
        || dirname($previousRelease) !== $data['release_root']
        || preg_match('/^(previous|release)-[a-f0-9]{32}$/', basename($previousRelease)) !== 1) {
        fail('Invalid PWA release transaction state.');
    }
}

function removeTree(string $path): void
{
    if (is_link($path) || is_file($path)) {
        if (!unlink($path)) {
            fail('Unable to remove release path: ' . $path);
        }
        return;
    }
    if (!is_dir($path)) {
        return;
    }
    $entries = scandir($path);
    if ($entries === false) {
        fail('Unable to enumerate release path: ' . $path);
    }
    foreach ($entries as $entry) {
        if ($entry !== '.' && $entry !== '..') {
            removeTree($path . '/' . $entry);
        }
    }
    if (!rmdir($path)) {
        fail('Unable to remove release directory: ' . $path);
    }
}

function activateReleaseLink(array $data, string $releasePath): void
{
    $temporaryLink = temporaryLinkPath($data);
    @unlink($temporaryLink);
    if (!symlink($releasePath, $temporaryLink)) {
        fail('Unable to create the PWA release symlink.');
    }
    if (!rename($temporaryLink, $data['directory'])) {
        @unlink($temporaryLink);
        fail('Unable to activate the PWA release symlink.');
    }
}

function atomicExchangePaths(string $firstPath, string $secondPath): void
{
    $script = <<<'PYTHON'
import ctypes
import os
import sys

AT_FDCWD = -100
RENAME_EXCHANGE = 2
libc = ctypes.CDLL(None, use_errno=True)
renameat2 = libc.renameat2
renameat2.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
renameat2.restype = ctypes.c_int
result = renameat2(
    AT_FDCWD,
    os.fsencode(sys.argv[1]),
    AT_FDCWD,
    os.fsencode(sys.argv[2]),
    RENAME_EXCHANGE,
)
if result != 0:
    error = ctypes.get_errno()
    raise OSError(error, os.strerror(error))
PYTHON;
    $pipes = [];
    $process = proc_open(
        ['/usr/bin/python3', '-c', $script, $firstPath, $secondPath],
        [1 => ['pipe', 'w'], 2 => ['pipe', 'w']],
        $pipes,
    );
    if (!is_resource($process)) {
        fail('Unable to start atomic PWA path exchange.');
    }
    $standardOutput = stream_get_contents($pipes[1]);
    $standardError = stream_get_contents($pipes[2]);
    fclose($pipes[1]);
    fclose($pipes[2]);
    $exitCode = proc_close($process);
    if ($exitCode !== 0) {
        fail('Atomic PWA path exchange failed: ' . trim($standardError . "\n" . $standardOutput));
    }
}

function migrateDirectoryAtomically(array $data, string $previousRelease): void
{
    $temporaryLink = temporaryLinkPath($data);
    @unlink($temporaryLink);
    if (!symlink($data['stage'], $temporaryLink)) {
        fail('Unable to create the initial PWA release symlink.');
    }
    try {
        atomicExchangePaths($data['directory'], $temporaryLink);
    } catch (Throwable $error) {
        @unlink($temporaryLink);
        throw $error;
    }
    if (!rename($temporaryLink, $previousRelease)) {
        try {
            atomicExchangePaths($data['directory'], $temporaryLink);
            @unlink($temporaryLink);
        } catch (Throwable $rollbackError) {
            fail('Unable to preserve the original PWA release after atomic activation. '
                . 'Atomic rollback also failed: ' . $rollbackError->getMessage());
        }
        fail('Unable to preserve the original PWA release after atomic activation.');
    }
}

function scheduleRollbackWatchdog(array $data): void
{
    $manifest = base64_encode(json_encode($data, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR));
    $watchdogScript = <<<'PYTHON'
import os
import subprocess
import sys
import time

if os.fork() > 0:
    os._exit(0)
os.setsid()
if os.fork() > 0:
    os._exit(0)
with open(os.devnull, "r+b", buffering=0) as devnull:
    os.dup2(devnull.fileno(), 0)
    os.dup2(devnull.fileno(), 1)
    os.dup2(devnull.fileno(), 2)
time.sleep(int(sys.argv[1]))
if os.path.isfile(sys.argv[6]):
    subprocess.run(sys.argv[2:6], check=False)
PYTHON;
    $pipes = [];
    $process = proc_open(
        [
            '/usr/bin/python3',
            '-c',
            $watchdogScript,
            (string) $data['watchdog_seconds'],
            PHP_BINARY,
            __FILE__,
            'rollback',
            $manifest,
            statePath($data),
        ],
        [0 => ['file', '/dev/null', 'r'], 1 => ['file', '/dev/null', 'a'], 2 => ['file', '/dev/null', 'a']],
        $pipes,
    );
    if (!is_resource($process) || proc_close($process) !== 0) {
        fail('Unable to schedule automatic rollback for the pending PWA release.');
    }
}

function rollbackRelease(array $data, array $state): void
{
    validateState($data, $state);
    $previousRelease = (string) ($state['previous_release'] ?? '');
    $activeRelease = (string) ($state['active_release'] ?? '');
    if (!($state['installed'] ?? false)
        && !is_link($data['directory'])
        && is_dir($data['directory'])
        && !is_dir($previousRelease)) {
        @unlink(statePath($data));
        return;
    }
    if ($previousRelease === '' || !is_dir($previousRelease)) {
        fail('Previous PWA release is unavailable for rollback.');
    }

    if (is_link($data['directory'])) {
        activateReleaseLink($data, $previousRelease);
    } elseif (!file_exists($data['directory'])) {
        if (!rename($previousRelease, $data['directory'])) {
            fail('Unable to restore the original PWA directory.');
        }
        $previousRelease = $data['directory'];
    } else {
        fail('PWA release path is not a managed symlink.');
    }

    if ($activeRelease !== '' && $activeRelease !== $previousRelease) {
        removeTree($activeRelease);
    }
    @unlink(statePath($data));
}

function installRelease(array $data): void
{
    $existingStatePath = statePath($data);
    if (is_file($existingStatePath)) {
        $state = loadState($data);
        validateState($data, $state);
        $activeRelease = realpath($data['directory']);
        if (($state['active_release'] ?? null) === $data['stage']
            && is_link($data['directory'])
            && $activeRelease !== false
            && $activeRelease === realpath((string) $state['active_release'])) {
            $previousRelease = (string) $state['previous_release'];
            $temporaryLink = temporaryLinkPath($data);
            if (!is_dir($previousRelease) && is_dir($temporaryLink) && !is_link($temporaryLink)) {
                if (!rename($temporaryLink, $previousRelease)) {
                    fail('Unable to finish preserving the original PWA release.');
                }
            }
            if (!is_dir($previousRelease)) {
                fail('The active PWA release has no recoverable previous release.');
            }
            $state['installed'] = true;
            saveState($data, $state);
            return;
        }
        if (!($state['installed'] ?? false)
            && !is_link($data['directory'])
            && is_dir($data['directory'])
            && !is_dir((string) $state['previous_release'])) {
            @unlink(temporaryLinkPath($data));
            @unlink($existingStatePath);
        } else {
            fail('An incomplete PWA release transaction already exists.');
        }
    }

    foreach ($data['files'] as $file) {
        $staged = $data['stage'] . '/' . $file;
        if (!is_file($staged) || hash_file('sha256', $staged) !== $data['hashes'][$file]) {
            fail('Release hash mismatch: ' . $file);
        }
        if (!chmod($staged, 0644)) {
            fail('Unable to set release file permissions: ' . $file);
        }
    }

    $previousRelease = is_link($data['directory'])
        ? realpath($data['directory'])
        : previousReleasePath($data);
    if ($previousRelease === false || $previousRelease === '') {
        fail('Unable to resolve the current PWA release.');
    }
    if (!is_link($data['directory'])
        && (!is_dir($data['directory']) || file_exists($previousRelease))) {
        fail('PWA release directory cannot be migrated to managed releases.');
    }
    $publicParent = stat(dirname($data['directory']));
    $releaseRoot = stat($data['release_root']);
    if ($publicParent === false || $releaseRoot === false || $publicParent['dev'] !== $releaseRoot['dev']) {
        fail('Public and private PWA release paths must share one filesystem.');
    }
    $state = [
        'previous_release' => $previousRelease,
        'active_release' => $data['stage'],
        'installed' => false,
    ];
    saveState($data, $state);

    try {
        scheduleRollbackWatchdog($data);
        if (!is_link($data['directory'])) {
            migrateDirectoryAtomically($data, $previousRelease);
        } else {
            activateReleaseLink($data, $data['stage']);
        }
        $state['installed'] = true;
        saveState($data, $state);
    } catch (Throwable $error) {
        try {
            rollbackRelease($data, $state);
        } catch (Throwable $rollbackError) {
            fail($error->getMessage() . "\nRollback failed: " . $rollbackError->getMessage());
        }
        throw $error;
    }
}

function commitRelease(array $data): void
{
    $state = loadState($data);
    validateState($data, $state);
    $activeRelease = realpath($data['directory']);
    if (!is_link($data['directory'])
        || $activeRelease === false
        || $activeRelease !== realpath((string) $state['active_release'])) {
        fail('The expected PWA release is not active.');
    }
    if (!unlink(statePath($data))) {
        fail('Unable to remove PWA release transaction state.');
    }
    try {
        removeTree((string) $state['previous_release']);
    } catch (Throwable $error) {
        fwrite(STDERR, 'Warning: verified release committed, but previous release cleanup failed: '
            . $error->getMessage() . "\n");
    }
}

if (PHP_SAPI !== 'cli' || $argc !== 3) {
    fail('Usage: pwa-release-installer.php <install|commit|rollback> <manifest-base64>');
}

$action = $argv[1];
$data = json_decode(base64_decode($argv[2], true), true, 32, JSON_THROW_ON_ERROR);
if (!is_array($data)) {
    fail('Invalid PWA release manifest.');
}
foreach (['directory', 'release_root', 'stage', 'release_id', 'watchdog_seconds', 'files', 'hashes'] as $required) {
    if (!array_key_exists($required, $data)) {
        fail('PWA release manifest is missing: ' . $required);
    }
}
if (!is_string($data['directory'])
    || !is_string($data['release_root'])
    || !is_string($data['stage'])
    || !is_string($data['release_id'])
    || preg_match('#^/#', $data['directory']) !== 1
    || preg_match('#^/#', $data['release_root']) !== 1
    || preg_match('#^/#', $data['stage']) !== 1
    || preg_match('#(^|/)\.\.($|/)#', $data['directory']) === 1
    || preg_match('#(^|/)\.\.($|/)#', $data['release_root']) === 1
    || preg_match('#(^|/)\.\.($|/)#', $data['stage']) === 1) {
    fail('Invalid PWA release manifest paths or release identifier.');
}
$releaseBase = basename($data['directory']);
if ($releaseBase === ''
    || preg_match('/^[a-f0-9]{32}$/', $data['release_id']) !== 1
    || !is_int($data['watchdog_seconds'])
    || $data['watchdog_seconds'] < 1
    || $data['watchdog_seconds'] > 1800
    || !is_array($data['files'])
    || $data['files'] === []
    || !is_array($data['hashes'])
    || dirname($data['stage']) !== $data['release_root']
    || basename($data['stage']) !== 'release-' . $data['release_id']) {
    fail('Invalid PWA release manifest paths or release identifier.');
}
$seenFiles = [];
foreach ($data['files'] as $file) {
    if (!is_string($file)
        || ($file !== '.htaccess' && preg_match('#^[A-Za-z0-9][A-Za-z0-9._/-]*$#', $file) !== 1)
        || preg_match('#(^|/)\.\.($|/)#', $file) === 1
        || !isset($data['hashes'][$file])
        || !is_string($data['hashes'][$file])
        || preg_match('/^[a-f0-9]{64}$/', $data['hashes'][$file]) !== 1
        || isset($seenFiles[$file])) {
        fail('Unsafe PWA release file: ' . (string) $file);
    }
    $seenFiles[$file] = true;
}

match ($action) {
    'install' => installRelease($data),
    'commit' => commitRelease($data),
    'rollback' => rollbackRelease($data, loadState($data)),
    default => fail('Unknown PWA release action: ' . $action),
};

echo "PWA release {$action} completed.\n";
