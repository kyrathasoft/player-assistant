<?php

declare(strict_types=1);

final class XpTrackingService
{
    private const CACHE_KEY = 'current';
    private const MAXIMUM_CHARACTERS = 200;
    private const MAXIMUM_AWARD_PROGRESSION_ENTRIES = 1000;
    private const CHARACTER_KEY_ALIASES = [
        'max' => 'maximilian',
    ];
    private const CHARACTER_DISPLAY_NAME_ALIASES = [
        'maximilian' => 'Maximilian',
    ];

    private array $xpConfig;
    private $markdownFetcher;
    private $awardFilePromoter;

    public function __construct(
        private readonly PDO $database,
        array $xpConfig,
        ?callable $markdownFetcher = null,
        ?callable $awardFilePromoter = null)
    {
        $this->xpConfig = array_replace([
            'source_url' => '',
            'character_source_url' => '',
            'class_progression_index_url' => '',
            'connect_timeout_seconds' => 3,
            'timeout_seconds' => 8,
            'maximum_response_bytes' => 524288,
            'maximum_stale_seconds' => 86400,
            'awards_directory' => '',
            'awards_root' => '',
            'award_groups' => [],
        ], $xpConfig);
        if ((string)$this->xpConfig['character_source_url'] === ''
            && (string)$this->xpConfig['source_url'] !== '') {
            $this->xpConfig['character_source_url'] = $this->deriveCharacterSourceUrl(
                (string)$this->xpConfig['source_url']);
        }
        if ((string)$this->xpConfig['class_progression_index_url'] === ''
            && (string)$this->xpConfig['source_url'] !== '') {
            $this->xpConfig['class_progression_index_url'] =
                $this->deriveClassProgressionIndexUrl((string)$this->xpConfig['source_url']);
        }
        $this->markdownFetcher = $markdownFetcher;
        $this->awardFilePromoter = $awardFilePromoter;
        $this->validateConfiguration();
        $this->ensureSchema();
    }

    public function getForAccount(array $account): array
    {
        $role = (string)($account['role'] ?? '');
        $characterKey = (string)($account['character_key'] ?? '');
        if (!in_array($role, ['player', 'dm'], true)
            || preg_match('/^[a-z0-9][a-z0-9._:-]{0,99}$/', $characterKey) !== 1) {
            throw new BrokerHttpException(
                403,
                'xp_not_authorized',
                'XP access is not authorized for this account.');
        }

        $snapshot = $this->loadCurrentSnapshot();
        $baseResponse = [
            'schema_version' => 1,
            'date_label' => $snapshot['date_label'],
            'fetched_at' => gmdate(DATE_ATOM, $snapshot['fetched_at']),
            'stale' => $snapshot['stale'],
        ];

        if ($role === 'dm') {
            return $baseResponse + [
                'scope' => 'party',
                'characters' => $snapshot['characters'],
            ];
        }

        $matches = array_values(array_filter(
            $snapshot['characters'],
            fn(array $character): bool => hash_equals(
                $characterKey,
                $this->characterKeyForName((string)$character['character_name']))));
        if (count($matches) !== 1) {
            throw new BrokerHttpException(
                403,
                'xp_not_authorized',
                'No unambiguous XP total is authorized for this account.');
        }

        $character = $matches[0];
        $character['character_name'] = $this->canonicalCharacterDisplayName(
            (string)$character['character_name']);
        $authorizedCharacters = [];
        $configuredGroups = $this->xpConfig['award_groups'] ?? [];
        $progressionKeys = is_array($configuredGroups)
            && is_array($configuredGroups[$characterKey] ?? null)
            ? $this->validatedAwardGroups()[$characterKey]
            : [$characterKey . '-xp'];
        foreach ($progressionKeys as $progressionKey) {
            $progressionCharacterKey = $this->characterKeyForProgression($progressionKey);
            $progressionMatches = array_values(array_filter(
                $snapshot['characters'],
                fn(array $candidate): bool => hash_equals(
                    $progressionCharacterKey,
                    $this->characterKeyForName((string)$candidate['character_name']))));
            if (count($progressionMatches) !== 1) {
                continue;
            }
            $authorizedCharacter = $progressionMatches[0];
            $authorizedCharacter['character_name'] = $this->canonicalCharacterDisplayName(
                (string)$authorizedCharacter['character_name']);
            $authorizedCharacters[] = [
                'character_key' => $progressionKey,
                'character' => $authorizedCharacter,
            ];
        }
        return $baseResponse + [
            'scope' => 'character',
            'character' => $character,
            'authorized_characters' => $authorizedCharacters,
        ];
    }

    public function isConfigured(): bool
    {
        return (string)$this->xpConfig['source_url'] !== '';
    }

    public function getAwardsForAccount(array $account): array
    {
        $role = (string)($account['role'] ?? '');
        $characterKey = (string)($account['character_key'] ?? '');
        if (!in_array($role, ['player', 'dm'], true)
            || preg_match('/^[a-z0-9][a-z0-9._:-]{0,99}$/', $characterKey) !== 1) {
            throw new BrokerHttpException(
                403,
                'xp_awards_not_authorized',
                'XP award access is not authorized for this account.');
        }

        $groups = $this->validatedAwardGroups();
        if ($role === 'dm') {
            $progressionKeys = array_values(array_unique(array_merge(...array_values($groups))));
            $scope = 'party';
        } else {
            $progressionKeys = $groups[$characterKey] ?? [];
            $scope = 'character';
        }
        if ($progressionKeys === []) {
            throw new BrokerHttpException(
                403,
                'xp_awards_not_authorized',
                'No XP award progression is authorized for this account.');
        }

        $progressions = $this->loadAwardProgressionsWithLock($progressionKeys);
        try {
            $snapshots = $this->parseSnapshots($this->fetchMarkdown(
                (string)$this->xpConfig['source_url']));
            $progressions = $this->withAwardLock(
                $progressionKeys,
                LOCK_EX,
                function () use ($progressionKeys, $snapshots): array {
                    $awardState = $this->loadAwardState();
                    $progressions = array_map(
                        fn(string $progressionKey): array => [
                            'character_key' => $progressionKey,
                            'entries' => $this->loadAwardProgression($progressionKey),
                        ],
                        $progressionKeys);
                    foreach ($progressions as &$progression) {
                        $refreshed = $this->refreshAwardProgression(
                            $progression['character_key'],
                            $progression['entries'],
                            $snapshots,
                            $awardState['progressions'][$progression['character_key']] ?? null);
                        $progression['entries'] = $refreshed['entries'];
                        $awardState['progressions'][$progression['character_key']] =
                            $refreshed['state'];
                    }
                    unset($progression);
                    $this->storeAwardProgressions($progressions, $awardState);
                    return $progressions;
                });
        } catch (Throwable) {
            $progressions = $this->loadAwardProgressionsWithLock($progressionKeys);
        }

        if ($scope === 'character') {
            foreach ($progressions as $index => &$progression) {
                $progression['is_account_character'] = $index === 0;
            }
            unset($progression);
        }

        return [
            'schema_version' => 1,
            'scope' => $scope,
            'progressions' => $progressions,
        ];
    }

    private function loadAwardProgressionsWithLock(array $progressionKeys): array
    {
        return $this->withAwardLock(
            $progressionKeys,
            LOCK_EX,
            fn(): array => array_map(
                fn(string $progressionKey): array => [
                    'character_key' => $progressionKey,
                    'entries' => $this->loadAwardProgression($progressionKey),
                ],
                $progressionKeys));
    }

    private function withAwardLock(array $progressionKeys, int $lockType, callable $action): mixed
    {
        $lockPath = $this->awardDirectoryPath() . DIRECTORY_SEPARATOR . '.xp-refresh.lock';
        $lockHandle = fopen($lockPath, 'c');
        if ($lockHandle === false) {
            throw new RuntimeException('The XP award refresh lock could not be opened.');
        }
        @chmod($lockPath, 0600);
        try {
            if (!flock($lockHandle, $lockType)) {
                throw new RuntimeException('The XP award refresh lock could not be acquired.');
            }
            if ($lockType === LOCK_EX) {
                $this->recoverAwardTransaction();
            }
            return $action();
        } finally {
            @flock($lockHandle, LOCK_UN);
            fclose($lockHandle);
        }
    }

    private function loadAwardState(): array
    {
        $path = $this->awardStatePath();
        if (!is_file($path)) {
            return ['schema_version' => 1, 'progressions' => []];
        }
        $resolvedPath = realpath($path);
        $directory = $this->awardDirectoryPath();
        if ($resolvedPath === false
            || is_link($path)
            || !$this->pathIsWithin($resolvedPath, $directory)) {
            throw new RuntimeException('The cumulative XP award state path is invalid.');
        }
        $size = filesize($resolvedPath);
        if (!is_int($size) || $size < 2 || $size > 1048576) {
            throw new RuntimeException('The cumulative XP award state is invalid.');
        }
        $contents = file_get_contents($resolvedPath);
        $state = is_string($contents)
            ? json_decode($contents, true, 16, JSON_THROW_ON_ERROR)
            : null;
        if (!is_array($state)
            || array_keys($state) !== ['schema_version', 'progressions']
            || $state['schema_version'] !== 1
            || !is_array($state['progressions'])) {
            throw new RuntimeException('The cumulative XP award state is invalid.');
        }
        $configuredProgressionKeys = [];
        foreach ($this->validatedAwardGroups() as $groupProgressionKeys) {
            foreach ($groupProgressionKeys as $configuredProgressionKey) {
                $configuredProgressionKeys[$configuredProgressionKey] = true;
            }
        }
        foreach ($state['progressions'] as $progressionKey => $progressionState) {
            if (!is_string($progressionKey)
                || preg_match('/^[a-z0-9]+(?:-[a-z0-9]+)*$/', $progressionKey) !== 1
                || !isset($configuredProgressionKeys[$progressionKey])
                || !is_array($progressionState)
                || array_keys($progressionState) !== [
                    'source_date',
                    'xp_total',
                    'level',
                    'state_digest',
                ]
                || !is_string($progressionState['source_date'])
                || !is_int($progressionState['xp_total'])
                || $progressionState['xp_total'] < 0
                || !is_int($progressionState['level'])
                || $progressionState['level'] < 0
                || !is_string($progressionState['state_digest'])
                || preg_match('/^[a-f0-9]{64}$/', $progressionState['state_digest']) !== 1) {
                throw new RuntimeException('The cumulative XP award state is invalid.');
            }
            $this->parseXpDate($progressionState['source_date']);
        }
        return $state;
    }

    private function awardStatePath(): string
    {
        return $this->awardDirectoryPath() . DIRECTORY_SEPARATOR . '.xp-award-state.json';
    }

    private function pathIsWithin(string $path, string $directory): bool
    {
        $prefix = rtrim($directory, '/\\') . DIRECTORY_SEPARATOR;
        if (DIRECTORY_SEPARATOR === '\\') {
            return str_starts_with(strtolower($path), strtolower($prefix));
        }
        return str_starts_with($path, $prefix);
    }

    private function validatedAwardGroups(): array
    {
        $groups = $this->xpConfig['award_groups'];
        if (!is_array($groups) || $groups === []) {
            throw new BrokerHttpException(
                503,
                'xp_awards_unavailable',
                'XP award progressions are not configured on the server.');
        }

        $validated = [];
        foreach ($groups as $characterKey => $progressionKeys) {
            if (!is_string($characterKey)
                || preg_match('/^[a-z0-9][a-z0-9._:-]{0,99}$/', $characterKey) !== 1
                || !is_array($progressionKeys)
                || $progressionKeys === []) {
                throw new BrokerHttpException(
                    503,
                    'xp_awards_unavailable',
                    'The XP award authorization configuration is invalid.');
            }
            $keys = [];
            foreach ($progressionKeys as $progressionKey) {
                if (!is_string($progressionKey)
                    || preg_match('/^[a-z0-9]+(?:-[a-z0-9]+)*$/', $progressionKey) !== 1
                    || $progressionKey === 'cumulative-state'
                    || in_array($progressionKey, $keys, true)) {
                    throw new BrokerHttpException(
                        503,
                        'xp_awards_unavailable',
                        'The XP award authorization configuration is invalid.');
                }
                $keys[] = $progressionKey;
            }
            $validated[$characterKey] = $keys;
        }
        return $validated;
    }

    private function loadAwardProgression(string $progressionKey): array
    {
        $directory = $this->awardDirectoryPath();
        $path = rtrim($directory, '/\\') . DIRECTORY_SEPARATOR . $progressionKey . '.json';
        $resolvedPath = realpath($path);
        if ($resolvedPath === false
            || !is_file($resolvedPath)
            || !$this->pathIsWithin($resolvedPath, $directory)) {
            throw new BrokerHttpException(
                503,
                'xp_awards_unavailable',
                'An XP award progression is unavailable.');
        }
        $size = filesize($resolvedPath);
        if (!is_int($size) || $size < 2 || $size > 1048576) {
            throw new BrokerHttpException(
                503,
                'xp_awards_unavailable',
                'An XP award progression is invalid.');
        }
        $contents = file_get_contents($resolvedPath);
        if (!is_string($contents)) {
            throw new BrokerHttpException(
                503,
                'xp_awards_unavailable',
                'An XP award progression could not be read.');
        }
        try {
            $entries = json_decode($contents, true, 16, JSON_THROW_ON_ERROR);
        } catch (JsonException $exception) {
            throw new BrokerHttpException(
                503,
                'xp_awards_unavailable',
                'An XP award progression is invalid.',
                $exception);
        }
        if (!is_array($entries)
            || $entries === []
            || count($entries) > self::MAXIMUM_AWARD_PROGRESSION_ENTRIES) {
            throw new BrokerHttpException(
                503,
                'xp_awards_unavailable',
                'An XP award progression is invalid.');
        }

        $expectedCharacter = null;
        foreach ($entries as $entry) {
            if (!is_array($entry)
                || array_keys($entry) !== [
                    'character_name',
                    'character_class',
                    'level_before_award',
                    'xp_award',
                    'xp_award_date',
                    'level_after_award',
                ]
                || !$this->validAwardText($entry['character_name'], 100)
                || !$this->validAwardText($entry['character_class'], 100)
                || !$this->validAwardText($entry['xp_award_date'], 200)
                || !is_int($entry['level_before_award'])
                || $entry['level_before_award'] < 0
                || !is_int($entry['level_after_award'])
                || $entry['level_after_award'] < 0
                || !is_int($entry['xp_award'])
                || $entry['xp_award'] < 0) {
                throw new BrokerHttpException(
                    503,
                    'xp_awards_unavailable',
                    'An XP award progression is invalid.');
            }
            $expectedCharacter ??= $entry['character_name'];
            if (!hash_equals($expectedCharacter, $entry['character_name'])) {
                throw new BrokerHttpException(
                    503,
                    'xp_awards_unavailable',
                    'An XP award progression contains multiple characters.');
            }
        }
        $expectedProgressionCharacterKey = preg_replace('/-xp$/', '', $progressionKey);
        if (!is_string($expectedProgressionCharacterKey)
            || !hash_equals(
                $expectedProgressionCharacterKey,
                $this->characterKeyForName((string)$expectedCharacter))) {
            throw new BrokerHttpException(
                503,
                'xp_awards_unavailable',
                'An XP award progression does not match its configured character.');
        }
        return $entries;
    }

    private function validAwardText(mixed $value, int $maximumLength): bool
    {
        return is_string($value)
            && $value !== ''
            && strlen($value) <= $maximumLength
            && preg_match('//u', $value) === 1
            && preg_match('/[\x00-\x1F\x7F]/u', $value) !== 1;
    }

    private function refreshAwardProgression(
        string $progressionKey,
        array $entries,
        array $snapshots,
        ?array $state): array
    {
        $lastEntry = $entries[array_key_last($entries)];
        $characterKey = $this->characterKeyForName((string)$lastEntry['character_name']);
        $lastAwardDate = $this->parseXpDate((string)$lastEntry['xp_award_date']);
        if ($state !== null
            && !hash_equals(
                (string)$state['state_digest'],
                $this->awardStateDigest($progressionKey, $state, $entries))) {
            throw new RuntimeException(
                "The cumulative XP state does not match progression '$progressionKey'.");
        }
        $cursorDate = $state === null
            ? $lastAwardDate
            : $this->parseXpDate((string)$state['source_date']);
        if ($cursorDate < $lastAwardDate) {
            throw new RuntimeException(
                "The cumulative XP state predates progression '$progressionKey'.");
        }

        $previousCharacter = null;
        $cursorFound = false;
        $nextState = $state;
        foreach ($snapshots as $snapshot) {
            $currentCharacter = null;
            foreach ($snapshot['characters'] as $character) {
                if ($this->characterKeyForName((string)$character['character_name']) === $characterKey) {
                    $currentCharacter = $character;
                    break;
                }
            }
            if ($snapshot['date'] < $cursorDate) {
                continue;
            }
            if ($snapshot['date'] == $cursorDate) {
                if ($currentCharacter === null) {
                    throw new RuntimeException(
                        "The cached XP progression '$progressionKey' has no live-source baseline.");
                }
                if ($state !== null) {
                    $sameDateAward = (int)$currentCharacter['xp_total'] - (int)$state['xp_total'];
                    if ($sameDateAward < 0) {
                        throw new RuntimeException(
                            "The live XP total decreased for progression '$progressionKey'.");
                    }
                    if ($sameDateAward > 0) {
                        $entries[] = [
                            'character_name' => (string)$lastEntry['character_name'],
                            'character_class' => (string)$currentCharacter['character_class'],
                            'level_before_award' => (int)$state['level'],
                            'xp_award' => $sameDateAward,
                            'xp_award_date' => (string)$snapshot['date_text'],
                            'level_after_award' => (int)$currentCharacter['level'],
                        ];
                    }
                }
                $previousCharacter = $currentCharacter;
                $cursorFound = true;
                $nextState = [
                    'source_date' => (string)$snapshot['date_text'],
                    'xp_total' => (int)$currentCharacter['xp_total'],
                    'level' => (int)$currentCharacter['level'],
                ];
                continue;
            }
            if ($currentCharacter === null) {
                continue;
            }
            if (!$cursorFound || $previousCharacter === null) {
                throw new RuntimeException(
                    "The cached XP progression '$progressionKey' has no live-source baseline.");
            }
            $award = (int)$currentCharacter['xp_total'] - (int)$previousCharacter['xp_total'];
            if ($award < 0) {
                throw new RuntimeException(
                    "The live XP total decreased for progression '$progressionKey'.");
            }
            if ($award > 0) {
                $entries[] = [
                    'character_name' => (string)$lastEntry['character_name'],
                    'character_class' => (string)$currentCharacter['character_class'],
                    'level_before_award' => (int)$previousCharacter['level'],
                    'xp_award' => $award,
                    'xp_award_date' => (string)$snapshot['date_text'],
                    'level_after_award' => (int)$currentCharacter['level'],
                ];
            }
            $previousCharacter = $currentCharacter;
            $nextState = [
                'source_date' => (string)$snapshot['date_text'],
                'xp_total' => (int)$currentCharacter['xp_total'],
                'level' => (int)$currentCharacter['level'],
            ];
        }
        if (!$cursorFound || $nextState === null) {
            throw new RuntimeException(
                "The cached XP progression '$progressionKey' has no live-source baseline.");
        }
        if (count($entries) > self::MAXIMUM_AWARD_PROGRESSION_ENTRIES) {
            throw new RuntimeException(
                "The refreshed XP progression '$progressionKey' contains too many entries.");
        }
        $nextState['state_digest'] = $this->awardStateDigest(
            $progressionKey,
            $nextState,
            $entries);
        return ['entries' => $entries, 'state' => $nextState];
    }

    private function awardStateDigest(
        string $progressionKey,
        array $state,
        array $entries): string
    {
        return hash('sha256', json_encode(
            [
                'progression_key' => $progressionKey,
                'source_date' => $state['source_date'],
                'xp_total' => $state['xp_total'],
                'level' => $state['level'],
                'entries' => $entries,
            ],
            JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR));
    }

    private function storeAwardProgressions(array $progressions, array $awardState): void
    {
        $transactionId = bin2hex(random_bytes(16));
        $items = [];
        $journalWritten = false;
        $journalWasWritten = false;
        $commitSynchronized = false;
        try {
            foreach ($progressions as $index => $progression) {
                $progressionKey = (string)$progression['character_key'];
                $items[] = $this->stageAwardTransactionItem(
                    $transactionId,
                    (int)$index,
                    $progressionKey,
                    $this->awardProgressionPath($progressionKey),
                    json_encode(
                        $progression['entries'],
                        JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE
                            | JSON_THROW_ON_ERROR) . "\n");
            }

            $statePath = $this->awardStatePath();
            $items[] = $this->stageAwardTransactionItem(
                $transactionId,
                count($items),
                'cumulative-state',
                $statePath,
                json_encode(
                    $awardState,
                    JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE
                        | JSON_THROW_ON_ERROR) . "\n");
            $this->writeAwardTransactionJournal(
                $transactionId,
                array_map(
                    static fn(array $item): string => (string)$item['progression_key'],
                    $items),
                array_map(
                    static fn(array $item): bool => (bool)$item['had_original'],
                    $items));
            $journalWritten = true;
            $journalWasWritten = true;

            foreach ($items as $item) {
                if (is_file($item['path']) && !unlink($item['path'])) {
                    throw new RuntimeException(
                        "The XP cache item '{$item['progression_key']}' could not be replaced.");
                }
                if (!$this->promoteAwardFile($item['temporary_path'], $item['path'])) {
                    throw new RuntimeException(
                        "The XP cache item '{$item['progression_key']}' could not be promoted.");
                }
                @chmod($item['path'], 0600);
            }
            $this->synchronizeAwardDirectory();

            $journalPath = $this->awardTransactionJournalPath();
            if (!unlink($journalPath)) {
                throw new RuntimeException('The XP cache transaction could not be committed.');
            }
            // Journal deletion plus directory sync is the commit point. A surviving journal
            // intentionally rolls the generation back so the next live refresh can retry it.
            // Every promoted target was synchronized before deletion, so a sync error here
            // leaves either the complete new generation or a recoverable old generation.
            $journalWritten = false;
            $this->synchronizeAwardDirectory();
            $commitSynchronized = true;
            foreach ($items as $item) {
                if ($item['had_original']) {
                    @unlink($item['backup_path']);
                }
            }
            $this->synchronizeAwardDirectory();
        } catch (Throwable $exception) {
            if ($journalWritten) {
                try {
                    $this->recoverAwardTransaction();
                } catch (Throwable $rollbackException) {
                    throw new RuntimeException(
                        'The XP cache transaction could not be rolled back.',
                        0,
                        $rollbackException);
                }
            }
            throw $exception;
        } finally {
            foreach ($items as $item) {
                if (is_file($item['temporary_path'])) {
                    @unlink($item['temporary_path']);
                }
                if ((!$journalWasWritten || $commitSynchronized)
                    && is_file($item['backup_path'])) {
                    @unlink($item['backup_path']);
                }
            }
        }
    }

    private function stageAwardTransactionItem(
        string $transactionId,
        int $index,
        string $progressionKey,
        string $path,
        string $contents): array
    {
        $directory = $this->awardDirectoryPath();
        $temporaryPath = $directory . DIRECTORY_SEPARATOR
            . '.xp-cache-' . $transactionId . '-' . $index;
        $backupPath = $path . '.rollback-' . $transactionId;
        $hadOriginal = is_file($path);
        if (file_exists($temporaryPath) || file_exists($backupPath)) {
            throw new RuntimeException('The XP cache transaction paths already exist.');
        }
        try {
            $written = file_put_contents($temporaryPath, $contents, LOCK_EX);
            if ($written !== strlen($contents)) {
                throw new RuntimeException(
                    "The XP cache item '$progressionKey' could not be staged.");
            }
            @chmod($temporaryPath, 0600);
            $this->synchronizeAwardFile($temporaryPath);
            if ($hadOriginal) {
                if (!copy($path, $backupPath)) {
                    throw new RuntimeException(
                        "The XP cache item '$progressionKey' could not be backed up.");
                }
                @chmod($backupPath, 0600);
                $this->synchronizeAwardFile($backupPath);
            }
        } catch (Throwable $exception) {
            @unlink($temporaryPath);
            @unlink($backupPath);
            throw $exception;
        }
        return [
            'progression_key' => $progressionKey,
            'path' => $path,
            'temporary_path' => $temporaryPath,
            'backup_path' => $backupPath,
            'had_original' => $hadOriginal,
        ];
    }

    private function synchronizeAwardFile(string $path): void
    {
        if (!function_exists('fsync')) {
            throw new RuntimeException('XP cache synchronization is unavailable.');
        }
        $handle = fopen($path, 'r+b');
        if ($handle === false) {
            throw new RuntimeException('An XP cache transaction file could not be synchronized.');
        }
        try {
            if (!fflush($handle) || !fsync($handle)) {
                throw new RuntimeException('An XP cache transaction file could not be synchronized.');
            }
        } finally {
            fclose($handle);
        }
    }

    private function synchronizeAwardDirectory(): void
    {
        if (DIRECTORY_SEPARATOR === '\\') {
            return;
        }
        if (!function_exists('fsync')) {
            throw new RuntimeException('XP cache directory synchronization is unavailable.');
        }
        $handle = fopen($this->awardDirectoryPath(), 'r');
        if ($handle === false) {
            throw new RuntimeException('The XP cache directory could not be synchronized.');
        }
        try {
            if (!fsync($handle)) {
                throw new RuntimeException('The XP cache directory could not be synchronized.');
            }
        } finally {
            fclose($handle);
        }
    }

    private function writeAwardTransactionJournal(
        string $transactionId,
        array $progressionKeys,
        array $hadOriginal): void
    {
        $path = $this->awardTransactionJournalPath();
        $temporaryPath = $path . '.tmp-' . $transactionId;
        $contents = json_encode([
            'schema_version' => 1,
            'transaction_id' => $transactionId,
            'progression_keys' => $progressionKeys,
            'had_original' => $hadOriginal,
        ], JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR) . "\n";
        $handle = fopen($temporaryPath, 'x+b');
        if ($handle === false) {
            throw new RuntimeException('The XP cache transaction journal could not be staged.');
        }
        try {
            if (fwrite($handle, $contents) !== strlen($contents) || !fflush($handle)) {
                throw new RuntimeException('The XP cache transaction journal could not be written.');
            }
            if (function_exists('fsync') && !fsync($handle)) {
                throw new RuntimeException('The XP cache transaction journal could not be synchronized.');
            }
        } catch (Throwable $exception) {
            fclose($handle);
            @unlink($temporaryPath);
            throw $exception;
        } finally {
            if (is_resource($handle)) {
                fclose($handle);
            }
        }
        @chmod($temporaryPath, 0600);
        if (!rename($temporaryPath, $path)) {
            @unlink($temporaryPath);
            throw new RuntimeException('The XP cache transaction journal could not be promoted.');
        }
        @chmod($path, 0600);
        $this->synchronizeAwardDirectory();
    }

    private function recoverAwardTransaction(): void
    {
        $journalPath = $this->awardTransactionJournalPath();
        if (!is_file($journalPath)) {
            $this->cleanupStaleAwardTransactionArtifacts();
            return;
        }
        if (is_link($journalPath)) {
            throw new RuntimeException('The XP cache transaction journal path is invalid.');
        }
        $journalSize = filesize($journalPath);
        if (!is_int($journalSize) || $journalSize < 2 || $journalSize > 1048576) {
            throw new RuntimeException('The XP cache transaction journal is invalid.');
        }
        $contents = file_get_contents($journalPath);
        $journal = is_string($contents)
            ? json_decode($contents, true, 8, JSON_THROW_ON_ERROR)
            : null;
        if (!is_array($journal)
            || array_keys($journal) !== [
                'schema_version',
                'transaction_id',
                'progression_keys',
                'had_original',
            ]
            || $journal['schema_version'] !== 1
            || !is_string($journal['transaction_id'])
            || preg_match('/^[a-f0-9]{32}$/', $journal['transaction_id']) !== 1
            || !is_array($journal['progression_keys'])
            || !is_array($journal['had_original'])
            || count($journal['progression_keys']) !== count($journal['had_original'])
            || $journal['progression_keys'] === []) {
            throw new RuntimeException('The XP cache transaction journal is invalid.');
        }

        $configuredProgressionKeys = [];
        foreach ($this->validatedAwardGroups() as $groupProgressionKeys) {
            foreach ($groupProgressionKeys as $configuredProgressionKey) {
                $configuredProgressionKeys[$configuredProgressionKey] = true;
            }
        }
        if (count(array_unique($journal['progression_keys']))
                !== count($journal['progression_keys'])
            || end($journal['progression_keys']) !== 'cumulative-state') {
            throw new RuntimeException('The XP cache transaction journal is invalid.');
        }
        $directory = $this->awardDirectoryPath();
        foreach ($journal['progression_keys'] as $index => $progressionKey) {
            $isState = $progressionKey === 'cumulative-state';
            if (!is_string($progressionKey)
                || (!$isState && !isset($configuredProgressionKeys[$progressionKey]))
                || !is_bool($journal['had_original'][$index])) {
                throw new RuntimeException('The XP cache transaction journal is invalid.');
            }
            $path = $isState
                ? $this->awardStatePath()
                : $directory . DIRECTORY_SEPARATOR . $progressionKey . '.json';
            $backupPath = $path . '.rollback-' . $journal['transaction_id'];
            $temporaryPath = $directory . DIRECTORY_SEPARATOR
                . '.xp-cache-' . $journal['transaction_id'] . '-' . $index;
            if ($journal['had_original'][$index]) {
                if (!is_file($backupPath)) {
                    throw new RuntimeException('The XP cache transaction backup is unavailable.');
                }
                if (is_file($path) && !unlink($path)) {
                    throw new RuntimeException('The XP cache transaction target could not be restored.');
                }
                if (!copy($backupPath, $path)) {
                    throw new RuntimeException('The XP cache transaction backup could not be restored.');
                }
                @chmod($path, 0600);
                $this->synchronizeAwardFile($path);
            } elseif (is_file($path) && !unlink($path)) {
                throw new RuntimeException('The XP cache transaction target could not be removed.');
            }
            if (is_file($temporaryPath)) {
                @unlink($temporaryPath);
            }
        }
        $this->synchronizeAwardDirectory();
        if (!unlink($journalPath)) {
            throw new RuntimeException('The XP cache transaction journal could not be cleared.');
        }
        $this->synchronizeAwardDirectory();
        $this->cleanupStaleAwardTransactionArtifacts();
    }

    private function cleanupStaleAwardTransactionArtifacts(): void
    {
        $directory = $this->awardDirectoryPath();
        $patterns = [
            $directory . DIRECTORY_SEPARATOR . '.xp-cache-*',
            $directory . DIRECTORY_SEPARATOR . '*.json.rollback-*',
            $directory . DIRECTORY_SEPARATOR . '.xp-award-state.json.rollback-*',
            $directory . DIRECTORY_SEPARATOR . '.xp-award-transaction.json.tmp-*',
        ];
        foreach ($patterns as $pattern) {
            foreach (glob($pattern) ?: [] as $path) {
                if (is_file($path) && !is_link($path)) {
                    @unlink($path);
                }
            }
        }
    }

    private function awardTransactionJournalPath(): string
    {
        return $this->awardDirectoryPath()
            . DIRECTORY_SEPARATOR . '.xp-award-transaction.json';
    }

    private function promoteAwardFile(string $temporaryPath, string $targetPath): bool
    {
        if ($this->awardFilePromoter !== null) {
            return (bool)($this->awardFilePromoter)($temporaryPath, $targetPath);
        }
        return rename($temporaryPath, $targetPath);
    }

    private function awardProgressionPath(string $progressionKey): string
    {
        $directory = $this->awardDirectoryPath();
        $resolvedPath = realpath(
            rtrim($directory, '/\\') . DIRECTORY_SEPARATOR . $progressionKey . '.json');
        if ($resolvedPath === false
            || !is_file($resolvedPath)
            || !$this->pathIsWithin($resolvedPath, $directory)) {
            throw new BrokerHttpException(
                503,
                'xp_awards_unavailable',
                'An XP award progression is unavailable.');
        }
        return $resolvedPath;
    }

    private function awardDirectoryPath(): string
    {
        $directory = realpath((string)$this->xpConfig['awards_directory']);
        $root = realpath((string)$this->xpConfig['awards_root']);
        if ($directory === false
            || $root === false
            || !is_dir($directory)
            || !is_dir($root)
            || $directory === rtrim($root, '/\\')
            || !$this->pathIsWithin($directory, $root)) {
            throw new BrokerHttpException(
                503,
                'xp_awards_unavailable',
                'XP award progressions are unavailable.');
        }
        return $directory;
    }

    private function parseXpDate(string $dateText): DateTimeImmutable
    {
        $date = DateTimeImmutable::createFromFormat('!n.j.Y', trim($dateText));
        $dateErrors = DateTimeImmutable::getLastErrors();
        if ($date === false
            || ($dateErrors !== false
                && ($dateErrors['warning_count'] > 0 || $dateErrors['error_count'] > 0))) {
            throw new RuntimeException('The XP date label was invalid.');
        }
        return $date;
    }

    private function snapshotDate(array $snapshot): DateTimeImmutable
    {
        if (preg_match(
            '/^As of (?<date>\d{1,2}\.\d{1,2}\.\d{4})(?:\s|:|$)/i',
            (string)($snapshot['date_label'] ?? ''),
            $dateMatch) !== 1) {
            throw new RuntimeException('The XP date label was invalid.');
        }
        return $this->parseXpDate((string)$dateMatch['date']);
    }

    private function assertSnapshotDoesNotRegress(array $live, array $cached): void
    {
        $liveDate = $this->snapshotDate($live);
        $cachedDate = $this->snapshotDate($cached);
        if ($liveDate < $cachedDate) {
            throw new RuntimeException(
                'The live XP snapshot is older than the last-known-good snapshot.');
        }

        $liveByCharacterKey = [];
        foreach ($live['characters'] as $character) {
            $liveByCharacterKey[$this->characterKeyForName(
                (string)$character['character_name'])] = $character;
        }
        foreach ($cached['characters'] as $cachedCharacter) {
            $characterKey = $this->characterKeyForName(
                (string)$cachedCharacter['character_name']);
            $liveCharacter = $liveByCharacterKey[$characterKey] ?? null;
            if ($liveCharacter === null) {
                if ($liveDate == $cachedDate) {
                    throw new RuntimeException(
                        'The live XP snapshot omitted a same-date cached character.');
                }
                continue;
            }
            if ((int)$liveCharacter['xp_total'] < (int)$cachedCharacter['xp_total']) {
                throw new RuntimeException(
                    'A live XP total is lower than its last-known-good value.');
            }
        }
    }

    private function loadCurrentSnapshot(): array
    {
        if (!$this->isConfigured()) {
            throw new BrokerHttpException(
                503,
                'xp_unavailable',
                'XP totals are not configured on the server.');
        }

        $cached = $this->loadCachedSnapshot();
        $now = time();
        try {
            $parsed = $this->parseSnapshot($this->fetchMarkdown(
                (string)$this->xpConfig['source_url']));
            if ($cached !== null) {
                $this->assertSnapshotDoesNotRegress($parsed, $cached);
            }
            try {
                $hitPointsByCharacterKey = $this->parseCharacterHitPoints(
                    $this->fetchMarkdown((string)$this->xpConfig['character_source_url']));
            } catch (Throwable) {
                $hitPointsByCharacterKey = [];
            }
            try {
                $classLinks = $this->parseClassProgressionLinks(
                    $this->fetchMarkdown((string)$this->xpConfig['class_progression_index_url']));
            } catch (Throwable) {
                $classLinks = [];
            }
            $cachedByCharacterKey = [];
            foreach ($cached['characters'] ?? [] as $cachedCharacter) {
                $cachedByCharacterKey[$this->characterKeyForName(
                    (string)$cachedCharacter['character_name'])] = $cachedCharacter;
            }
            $progressionByPage = [];
            foreach ($parsed['characters'] as &$character) {
                $characterKey = $this->characterKeyForName(
                    (string)$character['character_name']);
                $hasLiveHitPoints = array_key_exists($characterKey, $hitPointsByCharacterKey);
                $character['hit_points'] = $hitPointsByCharacterKey[$characterKey] ?? 0;
                $character['xp_to_next_level'] = null;
                $tnlResolved = false;
                try {
                    $classLink = $this->resolveClassProgressionLink(
                        (string)$character['character_class'],
                        $classLinks);
                    $pageKey = $this->normalizeClassName($classLink);
                    if (!array_key_exists($pageKey, $progressionByPage)) {
                        $pageUrl = $this->deriveClassProgressionPageUrl(
                            (string)$this->xpConfig['class_progression_index_url'],
                            $classLink);
                        $progressionByPage[$pageKey] = $this->parseClassProgression(
                            $this->fetchMarkdown($pageUrl));
                    }
                    $nextLevel = (int)$character['level'] + 1;
                    $nextLevelXp = $progressionByPage[$pageKey][$nextLevel] ?? null;
                    $character['xp_to_next_level'] = is_int($nextLevelXp)
                        ? max(0, $nextLevelXp - (int)$character['xp_total'])
                        : null;
                    $tnlResolved = true;
                } catch (Throwable) {
                    // XP totals remain authoritative when optional TNL enrichment is unavailable.
                }
                $cachedCharacter = $cachedByCharacterKey[$characterKey] ?? null;
                if (is_array($cachedCharacter)) {
                    if (!$hasLiveHitPoints && array_key_exists('hit_points', $cachedCharacter)) {
                        $character['hit_points'] = (int)$cachedCharacter['hit_points'];
                    }
                    if (!$tnlResolved
                        && (int)$cachedCharacter['xp_total'] === (int)$character['xp_total']
                        && (int)$cachedCharacter['level'] === (int)$character['level']
                        && array_key_exists('xp_to_next_level', $cachedCharacter)) {
                        $character['xp_to_next_level'] = $cachedCharacter['xp_to_next_level'];
                    }
                }
            }
            unset($character);
            $snapshot = [
                'date_label' => $parsed['date_label'],
                'characters' => $parsed['characters'],
                'fetched_at' => $now,
            ];
            $this->storeCachedSnapshot($snapshot);
            return $snapshot + ['stale' => false];
        } catch (Throwable $exception) {
            if ($cached !== null
                && $now - $cached['fetched_at'] <= (int)$this->xpConfig['maximum_stale_seconds']) {
                return $cached + ['stale' => true];
            }
            throw new BrokerHttpException(
                502,
                'xp_unavailable',
                'Current XP totals could not be loaded.',
                $exception);
        }
    }

    private function fetchMarkdown(string $sourceUrl): string
    {
        if ($this->markdownFetcher !== null) {
            $markdown = ($this->markdownFetcher)($sourceUrl);
            if (!is_string($markdown)
                || strlen($markdown) === 0
                || strlen($markdown) > (int)$this->xpConfig['maximum_response_bytes']) {
                throw new RuntimeException('The XP markdown fixture was invalid.');
            }
            return $markdown;
        }

        $page = $this->fetchUrl(
            $sourceUrl,
            ['text/html', 'text/markdown', 'text/plain']);
        if ($page['content_type'] === 'text/markdown' || $page['content_type'] === 'text/plain') {
            return $page['content'];
        }

        if (preg_match(
            '/window\.preloadPage=f\("(?<url>https:\/\/[^"]+\.md)"\)/',
            $page['content'],
            $matches) !== 1) {
            throw new RuntimeException('The XP page did not expose a markdown document.');
        }
        $markdownUrl = (string)$matches['url'];
        $this->validateMarkdownUrl($markdownUrl);
        $markdown = $this->fetchUrl($markdownUrl, ['text/markdown', 'text/plain']);
        return $markdown['content'];
    }

    private function fetchUrl(string $url, array $acceptedContentTypes): array
    {
        if (!extension_loaded('curl')) {
            throw new RuntimeException('The PHP cURL extension is required for XP tracking.');
        }

        $content = '';
        $tooLarge = false;
        $maximumBytes = (int)$this->xpConfig['maximum_response_bytes'];
        $handle = curl_init($url);
        if ($handle === false) {
            throw new RuntimeException('The XP request could not be initialized.');
        }

        curl_setopt_array($handle, [
            CURLOPT_RETURNTRANSFER => false,
            CURLOPT_FOLLOWLOCATION => false,
            CURLOPT_PROTOCOLS => CURLPROTO_HTTPS,
            CURLOPT_REDIR_PROTOCOLS => CURLPROTO_HTTPS,
            CURLOPT_CONNECTTIMEOUT => (int)$this->xpConfig['connect_timeout_seconds'],
            CURLOPT_TIMEOUT => (int)$this->xpConfig['timeout_seconds'],
            CURLOPT_ENCODING => '',
            CURLOPT_HTTPHEADER => [
                'Accept: ' . implode(', ', $acceptedContentTypes),
                'Cache-Control: no-cache',
            ],
            CURLOPT_USERAGENT => 'PlayerAssistant-XpBroker/1.0',
            CURLOPT_WRITEFUNCTION => static function ($curl, string $chunk) use (
                &$content,
                &$tooLarge,
                $maximumBytes): int {
                if (strlen($content) + strlen($chunk) > $maximumBytes) {
                    $tooLarge = true;
                    return 0;
                }
                $content .= $chunk;
                return strlen($chunk);
            },
        ]);

        try {
            $success = curl_exec($handle);
            $status = (int)curl_getinfo($handle, CURLINFO_RESPONSE_CODE);
            $contentType = strtolower(trim(explode(
                ';',
                (string)curl_getinfo($handle, CURLINFO_CONTENT_TYPE),
                2)[0]));
            if ($success === false || $tooLarge || $status !== 200 || $content === '') {
                throw new RuntimeException('The XP source returned an unusable response.');
            }
            if (!in_array($contentType, $acceptedContentTypes, true)) {
                throw new RuntimeException('The XP source returned an unexpected content type.');
            }
            return ['content' => $content, 'content_type' => $contentType];
        } finally {
            curl_close($handle);
        }
    }

    private function parseSnapshot(string $markdown): array
    {
        $candidates = $this->parseSnapshots($markdown);
        $latest = $candidates[array_key_last($candidates)];
        return [
            'date_label' => $latest['date_label'],
            'characters' => $latest['characters'],
        ];
    }

    private function parseSnapshots(string $markdown): array
    {
        $lines = preg_split('/\r\n|\r|\n/', $markdown);
        if (!is_array($lines)) {
            throw new RuntimeException('The XP markdown could not be read.');
        }

        $candidates = [];
        foreach ($lines as $index => $line) {
            $dateLabel = trim(ltrim(trim((string)$line), '#'));
            if (!str_starts_with(strtolower($dateLabel), 'as of ')) {
                continue;
            }
            if ($dateLabel === ''
                || strlen($dateLabel) > 200
                || preg_match('//u', $dateLabel) !== 1
                || preg_match('/[\x00-\x1F\x7F]/u', $dateLabel) === 1) {
                throw new RuntimeException('The XP date label was invalid.');
            }

            if (preg_match(
                '/^As of (?<date>\d{1,2}\.\d{1,2}\.\d{4})(?:\s|:|$)/i',
                $dateLabel,
                $dateMatch) !== 1) {
                throw new RuntimeException('The XP date label was invalid.');
            }
            $dateText = (string)$dateMatch['date'];
            $date = $this->parseXpDate($dateText);

            $headerIndex = $this->findTableHeader($lines, $index + 1);
            if ($headerIndex < 0) {
                continue;
            }
            $candidates[] = [
                'date' => $date,
                'date_text' => $dateText,
                'date_label' => $dateLabel,
                'characters' => $this->parseTable($lines, $headerIndex, $dateLabel),
            ];
        }

        if ($candidates === []) {
            throw new RuntimeException('The XP markdown did not contain a valid As of section.');
        }
        usort($candidates, static fn(array $left, array $right): int => $left['date'] <=> $right['date']);
        for ($index = 1; $index < count($candidates); $index++) {
            if ($candidates[$index - 1]['date'] == $candidates[$index]['date']) {
                throw new RuntimeException('The XP markdown contained duplicate snapshot dates.');
            }
        }
        return $candidates;
    }

    private function findTableHeader(array $lines, int $startIndex): int
    {
        for ($index = $startIndex; $index < count($lines); $index++) {
            $cells = $this->splitTableRow((string)$lines[$index]);
            if ($cells !== []
                && $this->findCellIndex($cells, 'Name') >= 0
                && $this->findCellIndex($cells, 'Class') >= 0
                && $this->findCellIndex($cells, 'Level') >= 0
                && $this->findCellIndex($cells, 'XP Total') >= 0) {
                return $index;
            }
            if (str_starts_with(strtolower(trim(ltrim(trim((string)$lines[$index]), '#'))), 'as of ')) {
                return -1;
            }
        }
        return -1;
    }

    private function parseTable(array $lines, int $headerIndex, string $asOfDateLabel): array
    {
        $headers = $this->splitTableRow((string)$lines[$headerIndex]);
        $nameIndex = $this->findCellIndex($headers, 'Name');
        $classIndex = $this->findCellIndex($headers, 'Class');
        $levelIndex = $this->findCellIndex($headers, 'Level');
        $xpIndex = $this->findCellIndex($headers, 'XP Total');
        $hitPointsIndex = $this->findCellIndex($headers, 'HP');
        $tnlIndex = $this->findCellIndex($headers, 'TNL');
        $characters = [];
        $seenNames = [];

        for ($index = $headerIndex + 1; $index < count($lines); $index++) {
            $line = (string)$lines[$index];
            if (!str_starts_with(ltrim($line), '|')) {
                break;
            }
            $cells = $this->splitTableRow($line);
            if ($cells === [] || $this->isSeparatorRow($cells)) {
                continue;
            }
            if (count($cells) <= max($nameIndex, $classIndex, $levelIndex, $xpIndex)) {
                throw new RuntimeException('An XP table row was incomplete.');
            }

            $name = $this->cleanMarkdownCell($cells[$nameIndex]);
            $characterClass = $this->cleanMarkdownCell($cells[$classIndex]);
            $levelDigits = preg_replace('/\D+/', '', $cells[$levelIndex]);
            $xpDigits = preg_replace('/\D+/', '', $cells[$xpIndex]);
            if ($name === ''
                || strlen($name) > 100
                || preg_match('//u', $name) !== 1
                || preg_match('/[\x00-\x1F\x7F]/u', $name) === 1
                || $characterClass === ''
                || strlen($characterClass) > 100
                || str_contains($characterClass, '|')
                || preg_match('//u', $characterClass) !== 1
                || preg_match('/[\x00-\x1F\x7F]/u', $characterClass) === 1
                || !is_string($levelDigits)
                || $levelDigits === ''
                || filter_var(
                    $levelDigits,
                    FILTER_VALIDATE_INT,
                    ['options' => ['min_range' => 0, 'max_range' => 1000]]) === false
                || !is_string($xpDigits)
                || $xpDigits === ''
                || filter_var(
                    $xpDigits,
                    FILTER_VALIDATE_INT,
                    ['options' => ['min_range' => 0, 'max_range' => PHP_INT_MAX]]) === false) {
                throw new RuntimeException('An XP table row was invalid.');
            }
            $normalizedName = strtolower($name);
            if (isset($seenNames[$normalizedName])) {
                throw new RuntimeException('The XP table contained a duplicate character.');
            }
            $seenNames[$normalizedName] = true;
            $level = (int)$levelDigits;
            $xpAward = (int)$xpDigits;
            $characters[] = [
                'character_name' => $name,
                'character_class' => $characterClass,
                'level' => $level,
                'xp_total' => $xpAward,
                'level_before_award' => $level,
                'xp_award' => $xpAward,
                'xp_award_date' => $asOfDateLabel,
                'level_after_award' => $level,
            ];
            if ($hitPointsIndex >= 0) {
                if (count($cells) <= $hitPointsIndex) {
                    throw new RuntimeException('A cached XP table row was incomplete.');
                }
                $hitPointDigits = preg_replace('/\D+/', '', $cells[$hitPointsIndex]);
                if (!is_string($hitPointDigits)
                    || $hitPointDigits === ''
                    || filter_var(
                        $hitPointDigits,
                        FILTER_VALIDATE_INT,
                        ['options' => ['min_range' => 0, 'max_range' => 1000000]]) === false) {
                    throw new RuntimeException('A cached XP hit-point total was invalid.');
                }
                $characters[array_key_last($characters)]['hit_points'] = (int)$hitPointDigits;
            }
            if ($tnlIndex >= 0) {
                if (count($cells) <= $tnlIndex) {
                    throw new RuntimeException('A cached XP table row was incomplete.');
                }
                $tnlValue = trim((string)$cells[$tnlIndex]);
                if ($tnlValue === '—') {
                    $characters[array_key_last($characters)]['xp_to_next_level'] = null;
                } else {
                    $tnlDigits = preg_replace('/\D+/', '', $tnlValue);
                    if (!is_string($tnlDigits)
                        || $tnlDigits === ''
                        || filter_var(
                            $tnlDigits,
                            FILTER_VALIDATE_INT,
                            ['options' => ['min_range' => 0, 'max_range' => PHP_INT_MAX]]) === false) {
                        throw new RuntimeException('A cached TNL value was invalid.');
                    }
                    $characters[array_key_last($characters)]['xp_to_next_level'] =
                        (int)$tnlDigits;
                }
            }
            if (count($characters) > self::MAXIMUM_CHARACTERS) {
                throw new RuntimeException('The XP table contained too many characters.');
            }
        }

        if ($characters === []) {
            throw new RuntimeException('The XP table did not contain any character totals.');
        }
        return $characters;
    }

    private function parseCharacterHitPoints(string $markdown): array
    {
        $lines = preg_split('/\r\n|\r|\n/', $markdown);
        if (!is_array($lines)) {
            throw new RuntimeException('The active character listing could not be read.');
        }

        foreach ($lines as $headerIndex => $line) {
            $headers = $this->splitTableRow((string)$line);
            $nameIndex = $this->findCellIndex($headers, 'Name');
            $hitPointsIndex = $this->findCellIndex($headers, 'HP');
            if ($nameIndex < 0 || $hitPointsIndex < 0) {
                continue;
            }

            $hitPointsByCharacterKey = [];
            for ($index = $headerIndex + 1; $index < count($lines); $index++) {
                $row = (string)$lines[$index];
                if (!str_starts_with(ltrim($row), '|')) {
                    break;
                }
                $cells = $this->splitTableRow($row);
                if ($cells === [] || $this->isSeparatorRow($cells)) {
                    continue;
                }
                if (count($cells) <= max($nameIndex, $hitPointsIndex)) {
                    throw new RuntimeException('An active character row was incomplete.');
                }

                $name = $this->cleanMarkdownCell($cells[$nameIndex]);
                $hitPointDigits = preg_replace('/\D+/', '', $cells[$hitPointsIndex]);
                $characterKey = $this->characterKeyForName($name);
                if ($name === ''
                    || $characterKey === ''
                    || !is_string($hitPointDigits)
                    || $hitPointDigits === ''
                    || filter_var(
                        $hitPointDigits,
                        FILTER_VALIDATE_INT,
                        ['options' => ['min_range' => 0, 'max_range' => 1000000]]) === false
                    || array_key_exists($characterKey, $hitPointsByCharacterKey)) {
                    throw new RuntimeException(
                        'The active character listing contained an invalid or ambiguous row.');
                }
                $hitPointsByCharacterKey[$characterKey] = (int)$hitPointDigits;
            }

            if ($hitPointsByCharacterKey !== []) {
                return $hitPointsByCharacterKey;
            }
        }

        throw new RuntimeException(
            'The active character listing did not contain a Name and HP table.');
    }

    private function parseClassProgressionLinks(string $markdown): array
    {
        $matchCount = preg_match_all(
            '/\[\[(?<target>[^\]|]+)(?:\|[^\]]+)?\]\]/u',
            $markdown,
            $matches,
            PREG_SET_ORDER);
        if (!is_int($matchCount) || $matchCount < 1) {
            throw new RuntimeException(
                'The class progression index did not contain any class links.');
        }

        $links = [];
        foreach ($matches as $match) {
            $target = trim((string)($match['target'] ?? ''));
            if ($target === ''
                || strlen($target) > 100
                || preg_match('/^[A-Za-z0-9][A-Za-z0-9 _-]*$/', $target) !== 1) {
                throw new RuntimeException(
                    'The class progression index contained an invalid class link.');
            }
            $key = $this->normalizeClassName($target);
            if ($key === '' || array_key_exists($key, $links)) {
                throw new RuntimeException(
                    'The class progression index contained an ambiguous class link.');
            }
            $links[$key] = $target;
        }
        return $links;
    }

    private function resolveClassProgressionLink(
        string $characterClass,
        array $links): string
    {
        $classKey = $this->normalizeClassName($characterClass);
        if ($classKey === '') {
            throw new RuntimeException('The XP row contained an invalid class name.');
        }
        if (array_key_exists($classKey, $links)) {
            return (string)$links[$classKey];
        }

        $matches = [];
        foreach ($links as $linkKey => $target) {
            if (str_ends_with((string)$linkKey, ' ' . $classKey)) {
                $matches[] = (string)$target;
            }
        }
        if (count($matches) !== 1) {
            throw new RuntimeException(
                'The class progression index did not unambiguously match an XP class.');
        }
        return $matches[0];
    }

    private function parseClassProgression(string $markdown): array
    {
        $lines = preg_split('/\r\n|\r|\n/', $markdown);
        if (!is_array($lines)) {
            throw new RuntimeException('The class progression page could not be read.');
        }

        $progression = [];
        foreach ($lines as $line) {
            if (preg_match(
                '/^\|\s*(?<level>\d{1,3})\s*\|\s*(?<xp>\d[\d,]*)\s*\|$/',
                trim((string)$line),
                $matches) !== 1) {
                continue;
            }
            $this->addClassProgressionEntry(
                $progression,
                (string)$matches['level'],
                (string)$matches['xp']);
        }
        if ($progression !== []) {
            return $this->validateClassProgression($progression);
        }

        $xpColumn = null;
        $levelColumn = null;
        foreach ($lines as $line) {
            $cells = $this->splitTableRow((string)$line);
            if ($cells === []) {
                continue;
            }
            $normalizedCells = array_map(
                fn(string $cell): string => $this->normalizeClassName($cell),
                $cells);
            if ($levelColumn === null || $xpColumn === null) {
                $levelIndex = array_search('level', $normalizedCells, true);
                $xpIndex = array_search('xp', $normalizedCells, true);
                if ($levelIndex !== false && $xpIndex !== false && $levelIndex !== $xpIndex) {
                    $levelColumn = $levelIndex;
                    $xpColumn = $xpIndex;
                }
                continue;
            }
            if (!array_key_exists($levelColumn, $cells)
                || !array_key_exists($xpColumn, $cells)
                || preg_match('/^\d{1,3}$/', trim($cells[$levelColumn])) !== 1
                || preg_match('/^\d[\d,]*$/', trim($cells[$xpColumn])) !== 1) {
                continue;
            }
            $this->addClassProgressionEntry(
                $progression,
                trim($cells[$levelColumn]),
                trim($cells[$xpColumn]));
        }
        if ($progression !== []) {
            return $this->validateClassProgression($progression);
        }

        $inProgression = false;
        foreach ($lines as $line) {
            $normalizedLine = trim(str_replace("\u{00A0}", ' ', (string)$line));
            if (stripos($normalizedLine, 'XP and Level Progression') !== false) {
                $inProgression = true;
                continue;
            }
            if (!$inProgression) {
                continue;
            }
            if (stripos($normalizedLine, 'Spellcasting:') === 0) {
                break;
            }
            $tokens = preg_split('/\s+/u', $normalizedLine);
            if (!is_array($tokens)
                || count($tokens) < 14
                || preg_match('/^\d{1,3}$/', (string)$tokens[0]) !== 1
                || (string)$tokens[7] !== (string)$tokens[0]
                || preg_match('/^\d[\d,]*$/', (string)$tokens[13]) !== 1) {
                continue;
            }
            $this->addClassProgressionEntry(
                $progression,
                (string)$tokens[0],
                (string)$tokens[13]);
        }
        return $this->validateClassProgression($progression);
    }

    private function addClassProgressionEntry(
        array &$progression,
        string $levelText,
        string $xpText): void
    {
        $xpDigits = str_replace(',', '', $xpText);
        if (filter_var(
            $levelText,
            FILTER_VALIDATE_INT,
            ['options' => ['min_range' => 1, 'max_range' => 1000]]) === false
            || filter_var(
                $xpDigits,
                FILTER_VALIDATE_INT,
                ['options' => ['min_range' => 0, 'max_range' => PHP_INT_MAX]]) === false) {
            throw new RuntimeException(
                'The class progression page contained an invalid level or XP value.');
        }
        $level = (int)$levelText;
        if (array_key_exists($level, $progression)) {
            throw new RuntimeException(
                'The class progression page contained a duplicate level.');
        }
        $progression[$level] = (int)$xpDigits;
    }

    private function validateClassProgression(array $progression): array
    {
        if (count($progression) < 2) {
            throw new RuntimeException(
                'The class progression page did not contain enough levels.');
        }
        ksort($progression, SORT_NUMERIC);
        $expectedLevel = 1;
        $previousXp = -1;
        foreach ($progression as $level => $xp) {
            if ((int)$level !== $expectedLevel
                || !is_int($xp)
                || $xp < $previousXp) {
                throw new RuntimeException(
                    'The class progression page contained an invalid progression sequence.');
            }
            $expectedLevel++;
            $previousXp = $xp;
        }
        if (($progression[1] ?? -1) !== 0) {
            throw new RuntimeException(
                'The class progression page did not begin at level 1 with zero XP.');
        }
        return $progression;
    }

    private function normalizeClassName(string $className): string
    {
        $normalized = strtolower((string)preg_replace(
            '/[^A-Za-z0-9]+/',
            ' ',
            trim($className)));
        return trim((string)preg_replace('/\s+/', ' ', $normalized));
    }

    private function splitTableRow(string $line): array
    {
        $trimmed = trim($line);
        if (!str_starts_with($trimmed, '|') || !str_ends_with($trimmed, '|')) {
            return [];
        }

        $cells = [];
        $current = '';
        $escaped = false;
        $content = trim($trimmed, '|');
        $length = strlen($content);
        for ($index = 0; $index < $length; $index++) {
            $character = $content[$index];
            if ($escaped) {
                $current .= $character;
                $escaped = false;
                continue;
            }
            if ($character === '\\') {
                $escaped = true;
                continue;
            }
            if ($character === '|') {
                $cells[] = trim($current);
                $current = '';
                continue;
            }
            $current .= $character;
        }
        if ($escaped) {
            $current .= '\\';
        }
        $cells[] = trim($current);
        return $cells;
    }

    private function findCellIndex(array $cells, string $expected): int
    {
        foreach ($cells as $index => $cell) {
            if (strcasecmp((string)$cell, $expected) === 0) {
                return $index;
            }
        }
        return -1;
    }

    private function isSeparatorRow(array $cells): bool
    {
        foreach ($cells as $cell) {
            if ((string)$cell === '' || preg_match('/^[-: ]+$/', (string)$cell) !== 1) {
                return false;
            }
        }
        return true;
    }

    private function cleanMarkdownCell(string $value): string
    {
        $cleaned = trim($value);
        if (str_starts_with($cleaned, '[[') && str_ends_with($cleaned, ']]')) {
            $cleaned = substr($cleaned, 2, -2);
            $aliasIndex = strrpos($cleaned, '|');
            if ($aliasIndex !== false) {
                $cleaned = substr($cleaned, $aliasIndex + 1);
            }
        }
        return trim($cleaned);
    }

    private function canonicalCharacterDisplayName(string $name): string
    {
        $characterKey = $this->characterKeyForName($name);
        return self::CHARACTER_DISPLAY_NAME_ALIASES[$characterKey] ?? $name;
    }

    private function characterKeyForName(string $name): string
    {
        $firstName = explode(' ', trim($name), 2)[0];
        $key = strtolower((string)preg_replace('/[^A-Za-z0-9]+/', '-', $firstName));
        $key = trim($key, '-');
        return self::CHARACTER_KEY_ALIASES[$key] ?? $key;
    }

    private function characterKeyForProgression(string $progressionKey): string
    {
        return str_ends_with($progressionKey, '-xp')
            ? substr($progressionKey, 0, -3)
            : $progressionKey;
    }

    private function loadCachedSnapshot(): ?array
    {
        $statement = $this->database->prepare(
            'SELECT fetched_at, payload_json, content_sha256
             FROM xp_tracking_cache WHERE cache_key = ? LIMIT 1');
        $statement->execute([self::CACHE_KEY]);
        $row = $statement->fetch();
        if (!is_array($row)) {
            return null;
        }

        try {
            if (!hash_equals(
                strtolower((string)$row['content_sha256']),
                hash('sha256', (string)$row['payload_json']))) {
                return null;
            }
            $payload = json_decode((string)$row['payload_json'], true, 32, JSON_THROW_ON_ERROR);
            if (!is_array($payload)
                || !is_string($payload['date_label'] ?? null)
                || !is_array($payload['characters'] ?? null)
                || (int)$row['fetched_at'] <= 0) {
                return null;
            }
            $validated = $this->parseSnapshot($this->snapshotToMarkdown($payload));
            foreach ($validated['characters'] as $character) {
                if (!array_key_exists('xp_to_next_level', $character)) {
                    return null;
                }
            }
            return [
                'date_label' => $validated['date_label'],
                'characters' => $validated['characters'],
                'fetched_at' => (int)$row['fetched_at'],
            ];
        } catch (Throwable) {
            return null;
        }
    }

    private function snapshotToMarkdown(array $payload): string
    {
        $lines = [
            (string)$payload['date_label'],
            '',
            '| Name | Class | Level | XP Total | HP | TNL |',
            '| --- | --- | ---: | ---: | ---: | ---: |',
        ];
        foreach ($payload['characters'] as $character) {
            if (!is_array($character)) {
                throw new RuntimeException('The cached XP snapshot was invalid.');
            }
            $lines[] = sprintf(
                '| %s | %s | %s | %s | %s | %s |',
                (string)($character['character_name'] ?? ''),
                (string)($character['character_class'] ?? ''),
                (string)($character['level'] ?? ''),
                (string)($character['xp_total'] ?? ''),
                (string)($character['hit_points'] ?? ''),
                array_key_exists('xp_to_next_level', $character)
                    && $character['xp_to_next_level'] === null
                        ? '—'
                        : (string)($character['xp_to_next_level'] ?? ''));
        }
        return implode("\n", $lines);
    }

    private function storeCachedSnapshot(array $snapshot): void
    {
        $payload = json_encode([
            'date_label' => $snapshot['date_label'],
            'characters' => $snapshot['characters'],
        ], JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
        $this->database->prepare(
            'INSERT INTO xp_tracking_cache (cache_key, fetched_at, payload_json, content_sha256)
             VALUES (?, ?, ?, ?)
             ON CONFLICT(cache_key) DO UPDATE SET
                fetched_at = excluded.fetched_at,
                payload_json = excluded.payload_json,
                content_sha256 = excluded.content_sha256')
            ->execute([
                self::CACHE_KEY,
                (int)$snapshot['fetched_at'],
                $payload,
                hash('sha256', $payload),
            ]);
    }

    private function validateConfiguration(): void
    {
        if ($this->isConfigured()) {
            $this->validatePageUrl((string)$this->xpConfig['source_url']);
            $this->validatePageUrl((string)$this->xpConfig['character_source_url']);
            $this->validatePageUrl(
                (string)$this->xpConfig['class_progression_index_url']);
        }
        foreach ([
            'connect_timeout_seconds',
            'timeout_seconds',
            'maximum_response_bytes',
            'maximum_stale_seconds',
        ] as $key) {
            if (!is_int($this->xpConfig[$key]) || $this->xpConfig[$key] <= 0) {
                throw new RuntimeException("XP tracking setting '$key' must be a positive integer.");
            }
        }
        if ($this->xpConfig['timeout_seconds'] < $this->xpConfig['connect_timeout_seconds']
            || $this->xpConfig['maximum_response_bytes'] > 2 * 1024 * 1024) {
            throw new RuntimeException('The XP tracking configuration limits are invalid.');
        }
    }

    private function validatePageUrl(string $url): void
    {
        $parts = parse_url($url);
        if (!is_array($parts)
            || (string)($parts['scheme'] ?? '') !== 'https'
            || strtolower((string)($parts['host'] ?? '')) !== 'publish.obsidian.md'
            || isset($parts['user'])
            || isset($parts['pass'])
            || (isset($parts['port']) && (int)$parts['port'] !== 443)
            || (string)($parts['path'] ?? '/') === '/'
            || isset($parts['query'])
            || isset($parts['fragment'])) {
            throw new RuntimeException('The XP source URL must be a fixed Obsidian Publish HTTPS page.');
        }
    }

    private function deriveCharacterSourceUrl(string $xpSourceUrl): string
    {
        $parts = parse_url($xpSourceUrl);
        if (!is_array($parts)) {
            return '';
        }
        $segments = explode(
            '/',
            trim((string)($parts['path'] ?? ''), '/'));
        $vaultName = $segments[0] ?? '';
        if ($vaultName === '') {
            return '';
        }
        return 'https://publish.obsidian.md/'
            . rawurlencode(rawurldecode($vaultName))
            . '/PCs/Player+Characters+Listing';
    }

    private function deriveClassProgressionIndexUrl(string $xpSourceUrl): string
    {
        $parts = parse_url($xpSourceUrl);
        if (!is_array($parts)) {
            return '';
        }
        $segments = explode(
            '/',
            trim((string)($parts['path'] ?? ''), '/'));
        $vaultName = $segments[0] ?? '';
        if ($vaultName === '') {
            return '';
        }
        return 'https://publish.obsidian.md/'
            . rawurlencode(rawurldecode($vaultName))
            . '/Classes/Class+Level+Progression';
    }

    private function deriveClassProgressionPageUrl(
        string $indexUrl,
        string $classLink): string
    {
        $parts = parse_url($indexUrl);
        if (!is_array($parts)
            || preg_match('/^[A-Za-z0-9][A-Za-z0-9 _-]{0,99}$/', $classLink) !== 1) {
            throw new RuntimeException('The class progression link was invalid.');
        }
        $directory = str_replace(
            '\\',
            '/',
            dirname((string)($parts['path'] ?? '/')));
        $url = 'https://publish.obsidian.md'
            . rtrim($directory, '/')
            . '/'
            . rawurlencode($classLink);
        $this->validatePageUrl($url);
        return $url;
    }

    private function validateMarkdownUrl(string $url): void
    {
        $parts = parse_url($url);
        $host = strtolower((string)($parts['host'] ?? ''));
        $path = (string)($parts['path'] ?? '');
        if (!is_array($parts)
            || (string)($parts['scheme'] ?? '') !== 'https'
            || preg_match('/^publish-\d+\.obsidian\.md$/', $host) !== 1
            || !str_starts_with($path, '/access/')
            || !str_ends_with(strtolower($path), '.md')
            || isset($parts['user'])
            || isset($parts['pass'])
            || (isset($parts['port']) && (int)$parts['port'] !== 443)
            || isset($parts['query'])
            || isset($parts['fragment'])) {
            throw new RuntimeException('The XP markdown URL was not allowlisted.');
        }
    }

    private function ensureSchema(): void
    {
        $this->database->exec(
            'CREATE TABLE IF NOT EXISTS xp_tracking_cache (
                cache_key TEXT PRIMARY KEY,
                fetched_at INTEGER NOT NULL,
                payload_json TEXT NOT NULL,
                content_sha256 TEXT NOT NULL
            )');
    }
}
