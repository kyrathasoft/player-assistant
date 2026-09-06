<?php

declare(strict_types=1);

require_once __DIR__ . '/DataInvariantContract.php';

final class QuestService
{
    private const VISIBILITY_VALUES = [
        'individual-only',
        'party-only',
        'individual-or-party',
    ];

    private const STATE_VALUES = [
        'gated',
        'available',
        'active',
        'available (abandoned)',
        'completed',
        'withdrawn',
    ];

    private const REQUEST_STATUS_VALUES = [
        'pending',
        'approved',
        'denied',
    ];

    private const REQUESTABLE_STATES = [
        'available',
        'available (abandoned)',
    ];

    private const QUEST_FIELDS = [
        'title',
        'summary',
        'giver',
        'visibility',
        'state',
        'objectives',
        'reward',
        'dates',
        'gated-by',
        'unlocked-by',
        'wiki-url',
    ];

    public function __construct(
        private readonly PDO $database,
        private readonly string $dataPath)
    {
        if (trim($dataPath) === '') {
            throw new RuntimeException('The quest data path is not configured.');
        }
    }

    public function forAccount(array $account): array
    {
        $characterKey = strtolower(trim((string)($account['character_key'] ?? '')));
        $accountId = (string)($account['id'] ?? '');
        $isDungeonMaster = (string)($account['role'] ?? '') === 'dm';
        $allQuests = $this->loadQuests();
        $questsById = [];
        foreach ($allQuests as $quest) {
            $questsById[$quest['id']] = $quest;
        }

        $latestRequests = $isDungeonMaster
            ? []
            : $this->latestRequestsByQuest($accountId);
        $visible = array_values(array_filter(
            $allQuests,
            static fn(array $quest): bool =>
                $isDungeonMaster
                || (
                    ($quest['visibility'] !== 'individual-only'
                        || in_array($characterKey, $quest['gated_by'], true))
                )));

        return [
            'schema_version' => 2,
            'status_values' => array_merge(self::VISIBILITY_VALUES, self::STATE_VALUES),
            'request_status_values' => self::REQUEST_STATUS_VALUES,
            'quests' => array_map(
                static function (array $quest) use ($latestRequests, $questsById): array {
                    if (self::isGated($quest, $questsById)) {
                        $quest['state'] = 'gated';
                    }
                    $quest['request_status'] = isset($latestRequests[$quest['id']])
                        ? (string)$latestRequests[$quest['id']]['status']
                        : null;
                    unset($quest['gated_by'], $quest['unlocked_by'], $quest['base_state']);
                    return $quest;
                },
                $visible),
            'pending_requests' => $isDungeonMaster
                ? $this->pendingRequests($questsById)
                : [],
            'notifications' => $isDungeonMaster
                ? []
                : $this->unacknowledgedNotifications($accountId, $questsById),
        ];
    }

    public function requestInterest(array $account, array $body): array
    {
        if ((string)($account['role'] ?? '') !== 'player') {
            throw new BrokerHttpException(
                403,
                'quest_request_not_authorized',
                'Only player characters may request an available quest.');
        }
        if (array_keys($body) !== ['quest_id']
            || !is_string($body['quest_id'])
            || preg_match('/^[a-z0-9]+(?:-[a-z0-9]+)*$/D', $body['quest_id']) !== 1) {
            throw new BrokerHttpException(
                400,
                'invalid_quest_request',
                'A valid quest identifier is required.');
        }

        $quest = $this->questForAccount((string)$body['quest_id'], $account);
        if (!in_array($quest['state'], self::REQUESTABLE_STATES, true)) {
            throw new BrokerHttpException(
                409,
                'quest_not_available',
                'Interest may only be requested for an available quest.');
        }

        $accountId = (string)($account['id'] ?? '');
        $pending = $this->database->prepare(
            'SELECT id FROM quest_requests
             WHERE quest_id = ? AND requester_account_id = ? AND status = \'pending\'
             LIMIT 1');
        $pending->execute([$quest['id'], $accountId]);
        if ($pending->fetch() !== false) {
            throw new BrokerHttpException(
                409,
                'quest_request_pending',
                'This character already has a pending request for that quest.');
        }

        $requestId = bin2hex(random_bytes(16));
        $now = time();
        try {
            $this->database->prepare(
                'INSERT INTO quest_requests (
                    id, quest_id, requester_account_id, status, created_at,
                    decided_at, decided_by_account_id, requester_acknowledged_at
                 ) VALUES (?, ?, ?, \'pending\', ?, NULL, NULL, NULL)')
                ->execute([$requestId, $quest['id'], $accountId, $now]);
        } catch (PDOException $exception) {
            if ((string)$exception->getCode() === '23000') {
                throw new BrokerHttpException(
                    409,
                    'quest_request_pending',
                    'This character already has a pending request for that quest.',
                    $exception);
            }
            throw $exception;
        }

        return [
            'schema_version' => 1,
            'request' => [
                'id' => $requestId,
                'quest_id' => $quest['id'],
                'quest_title' => $quest['title'],
                'requester_character_name' => (string)$account['character_name'],
                'status' => 'pending',
                'created_at' => gmdate(DATE_ATOM, $now),
                'decided_at' => null,
            ],
        ];
    }

    public function decide(array $account, string $requestId, array $body): array
    {
        if ((string)($account['role'] ?? '') !== 'dm') {
            throw new BrokerHttpException(
                403,
                'quest_decision_not_authorized',
                'Only the Dungeon Master may decide quest requests.');
        }
        if (array_keys($body) !== ['decision']
            || !is_string($body['decision'])
            || !in_array($body['decision'], ['approved', 'denied'], true)) {
            throw new BrokerHttpException(
                400,
                'invalid_quest_decision',
                'The quest decision must be approved or denied.');
        }

        $this->database->exec('BEGIN IMMEDIATE');
        try {
            $statement = $this->database->prepare(
                'SELECT requests.*, accounts.display_name AS requester_character_name
                 FROM quest_requests requests
                 JOIN character_accounts accounts ON accounts.id = requests.requester_account_id
                 WHERE requests.id = ?');
            $statement->execute([$requestId]);
            $request = $statement->fetch();
            if (!is_array($request)) {
                throw new BrokerHttpException(
                    404,
                    'quest_request_not_found',
                    'The quest request was not found.');
            }
            if ((string)$request['status'] !== 'pending') {
                throw new BrokerHttpException(
                    409,
                    'quest_request_already_decided',
                    'The quest request has already been decided.');
            }

            $questsById = [];
            foreach ($this->loadQuests() as $quest) {
                $questsById[$quest['id']] = $quest;
            }
            $quest = $questsById[(string)$request['quest_id']] ?? null;
            if (!is_array($quest)) {
                throw new BrokerHttpException(
                    409,
                    'quest_unavailable',
                    'The requested quest is no longer configured.');
            }

            $decision = (string)$body['decision'];
            if ($decision === 'approved'
                && !in_array($quest['state'], array_merge(self::REQUESTABLE_STATES, ['active']), true)) {
                throw new BrokerHttpException(
                    409,
                    'quest_not_available',
                    'The requested quest can no longer be activated.');
            }

            $now = time();
            $this->database->prepare(
                'UPDATE quest_requests
                 SET status = ?, decided_at = ?, decided_by_account_id = ?
                 WHERE id = ? AND status = \'pending\'')
                ->execute([$decision, $now, (string)$account['id'], $requestId]);

            if ($decision === 'approved' && in_array($quest['state'], self::REQUESTABLE_STATES, true)) {
                $this->database->prepare(
                    'INSERT INTO quest_state_overrides (
                        quest_id, base_state, state, updated_at, updated_by_account_id
                     ) VALUES (?, ?, \'active\', ?, ?)
                     ON CONFLICT(quest_id) DO UPDATE SET
                        base_state = excluded.base_state,
                        state = excluded.state,
                        updated_at = excluded.updated_at,
                        updated_by_account_id = excluded.updated_by_account_id')
                    ->execute([
                        $quest['id'],
                        $quest['base_state'],
                        $now,
                        (string)$account['id'],
                    ]);
                $quest['state'] = 'active';
            }

            $this->database->exec('COMMIT');
            $request['status'] = $decision;
            $request['decided_at'] = $now;
            return [
                'schema_version' => 1,
                'request' => $this->publicRequest($request, [$quest['id'] => $quest]),
                'quest_state' => $quest['state'],
            ];
        } catch (Throwable $exception) {
            if ($this->database->inTransaction()) {
                $this->database->exec('ROLLBACK');
            }
            throw $exception;
        }
    }

    public function acknowledge(array $account, string $requestId): array
    {
        if ((string)($account['role'] ?? '') !== 'player') {
            throw new BrokerHttpException(
                403,
                'quest_notification_not_authorized',
                'Only the requesting player may dismiss this notification.');
        }

        $statement = $this->database->prepare(
            'SELECT status FROM quest_requests
             WHERE id = ? AND requester_account_id = ?');
        $statement->execute([$requestId, (string)$account['id']]);
        $request = $statement->fetch();
        if (!is_array($request)) {
            throw new BrokerHttpException(
                404,
                'quest_request_not_found',
                'The quest request was not found.');
        }
        if (!in_array((string)$request['status'], ['approved', 'denied'], true)) {
            throw new BrokerHttpException(
                409,
                'quest_request_pending',
                'A pending quest request has no decision to dismiss.');
        }
        $this->database->prepare(
            'UPDATE quest_requests
             SET requester_acknowledged_at = COALESCE(requester_acknowledged_at, ?)
             WHERE id = ? AND requester_account_id = ?')
            ->execute([time(), $requestId, (string)$account['id']]);

        return [
            'schema_version' => 1,
            'acknowledged' => true,
            'request_id' => $requestId,
        ];
    }

    private function questForAccount(string $questId, array $account): array
    {
        $characterKey = strtolower(trim((string)($account['character_key'] ?? '')));
        $quests = $this->loadQuests();
        $questsById = [];
        foreach ($quests as $quest) {
            $questsById[$quest['id']] = $quest;
        }
        foreach ($quests as $quest) {
            if ($quest['id'] !== $questId) {
                continue;
            }
            if ($quest['visibility'] === 'individual-only'
                && !in_array($characterKey, $quest['gated_by'], true)) {
                break;
            }
            if (!self::isUnlocked($quest, $questsById)) {
                break;
            }
            return $quest;
        }
        throw new BrokerHttpException(
            404,
            'quest_not_found',
            'The requested quest is not visible to this character.');
    }

    private function latestRequestsByQuest(string $accountId): array
    {
        $statement = $this->database->prepare(
            'SELECT quest_id, status
             FROM quest_requests
             WHERE requester_account_id = ?
             ORDER BY created_at DESC, rowid DESC');
        $statement->execute([$accountId]);
        $latest = [];
        foreach ($statement->fetchAll() as $request) {
            $questId = (string)$request['quest_id'];
            if (!isset($latest[$questId])) {
                $latest[$questId] = $request;
            }
        }
        return $latest;
    }

    private function pendingRequests(array $questsById): array
    {
        $rows = $this->database->query(
            'SELECT requests.*, accounts.display_name AS requester_character_name
             FROM quest_requests requests
             JOIN character_accounts accounts ON accounts.id = requests.requester_account_id
             WHERE requests.status = \'pending\'
             ORDER BY requests.created_at, requests.id')->fetchAll();
        $requests = [];
        foreach ($rows as $row) {
            if (isset($questsById[(string)$row['quest_id']])) {
                $requests[] = $this->publicRequest($row, $questsById);
            }
        }
        return $requests;
    }

    private function unacknowledgedNotifications(string $accountId, array $questsById): array
    {
        $statement = $this->database->prepare(
            'SELECT requests.*, accounts.display_name AS requester_character_name
             FROM quest_requests requests
             JOIN character_accounts accounts ON accounts.id = requests.requester_account_id
             WHERE requests.requester_account_id = ?
               AND requests.status IN (\'approved\', \'denied\')
               AND requests.requester_acknowledged_at IS NULL
             ORDER BY requests.decided_at, requests.id');
        $statement->execute([$accountId]);
        $notifications = [];
        foreach ($statement->fetchAll() as $row) {
            if (isset($questsById[(string)$row['quest_id']])) {
                $notifications[] = $this->publicRequest($row, $questsById);
            }
        }
        return $notifications;
    }

    private function publicRequest(array $request, array $questsById): array
    {
        $quest = $questsById[(string)$request['quest_id']] ?? null;
        if (!is_array($quest)) {
            throw new RuntimeException('A quest request references an unknown quest.');
        }
        return [
            'id' => (string)$request['id'],
            'quest_id' => (string)$request['quest_id'],
            'quest_title' => (string)$quest['title'],
            'requester_character_name' => (string)$request['requester_character_name'],
            'status' => (string)$request['status'],
            'created_at' => gmdate(DATE_ATOM, (int)$request['created_at']),
            'decided_at' => $request['decided_at'] === null
                ? null
                : gmdate(DATE_ATOM, (int)$request['decided_at']),
        ];
    }

    private function loadQuests(): array
    {
        if (!is_file($this->dataPath) || !is_readable($this->dataPath)) {
            throw new RuntimeException('The quest data file is unavailable.');
        }
        $size = filesize($this->dataPath);
        if ($size === false || $size < 2 || $size > 262144) {
            throw new RuntimeException('The quest data file has an invalid size.');
        }
        $json = file_get_contents($this->dataPath);
        if ($json === false) {
            throw new RuntimeException('The quest data file could not be read.');
        }
        try {
            $payload = json_decode($json, true, 64, JSON_THROW_ON_ERROR);
        } catch (JsonException $exception) {
            throw new RuntimeException('The quest data file is not valid JSON.', 0, $exception);
        }
        $rootFields = is_array($payload) ? array_keys($payload) : [];
        sort($rootFields);
        if (!is_array($payload)
            || $rootFields !== ['quests', 'schema_version']
            || ($payload['schema_version'] ?? null) !== 1
            || !is_array($payload['quests'] ?? null)
            || array_is_list($payload['quests'])
            || count($payload['quests']) < 1
            || count($payload['quests']) > 100) {
            throw new RuntimeException('The quest data file has an invalid root schema.');
        }

        $overrides = [];
        foreach ($this->database->query(
            'SELECT quest_id, base_state, state FROM quest_state_overrides')->fetchAll() as $override) {
            $overrides[(string)$override['quest_id']] = $override;
        }

        $quests = [];
        foreach ($payload['quests'] as $id => $quest) {
            $validated = $this->validateQuest($id, $quest);
            $override = $overrides[$validated['id']] ?? null;
            if (is_array($override)
                && (string)$override['base_state'] === $validated['base_state']
                && (string)$override['state'] === 'active') {
                $validated['state'] = 'active';
            } elseif (is_array($override)
                && (string)$override['base_state'] !== $validated['base_state']) {
                $this->database->prepare(
                    'DELETE FROM quest_state_overrides
                     WHERE quest_id = ? AND base_state <> ?')
                    ->execute([$validated['id'], $validated['base_state']]);
            }
            $quests[] = $validated;
        }
        $questIds = array_fill_keys(array_column($quests, 'id'), true);
        foreach ($quests as $quest) {
            foreach ($quest['unlocked_by'] as $requiredQuestId) {
                if ($requiredQuestId === $quest['id'] || !isset($questIds[$requiredQuestId])) {
                    throw new RuntimeException("Quest '{$quest['id']}' has an invalid unlocked-by value.");
                }
            }
        }
        DataInvariantContract::assertQuests($quests);
        return $quests;
    }

    private static function isUnlocked(array $quest, array $questsById): bool
    {
        foreach ($quest['unlocked_by'] as $requiredQuestId) {
            $requiredQuest = $questsById[$requiredQuestId] ?? null;
            if (!is_array($requiredQuest) || $requiredQuest['state'] !== 'completed') {
                return false;
            }
        }
        return true;
    }

    private static function isGated(array $quest, array $questsById): bool
    {
        foreach ($quest['unlocked_by'] as $requiredQuestId) {
            $requiredQuest = $questsById[$requiredQuestId] ?? null;
            if (!is_array($requiredQuest) || $requiredQuest['state'] !== 'completed') {
                return true;
            }
        }
        return false;
    }

    private function validateQuest(mixed $id, mixed $quest): array
    {
        if (!is_string($id)
            || preg_match('/^[a-z0-9]+(?:-[a-z0-9]+)*$/D', $id) !== 1
            || !is_array($quest)) {
            throw new RuntimeException('A quest entry has an invalid identifier or shape.');
        }
        $actualFields = array_keys($quest);
        $requiredFields = self::QUEST_FIELDS;
        $allowedFields = array_merge($requiredFields, ['meta-date']);
        if (array_diff($requiredFields, $actualFields) !== []
            || array_diff($actualFields, $allowedFields) !== []) {
            throw new RuntimeException("Quest '$id' does not match the required schema.");
        }

        $this->requireText($quest['title'], 200, "Quest '$id' title");
        $this->requireText($quest['summary'], 1000, "Quest '$id' summary");
        $this->requireText($quest['giver'], 200, "Quest '$id' giver");
        $this->requireText($quest['reward'], 500, "Quest '$id' reward", true);
        if (!in_array($quest['visibility'], self::VISIBILITY_VALUES, true)
            || !in_array($quest['state'], self::STATE_VALUES, true)) {
            throw new RuntimeException("Quest '$id' has an invalid visibility or state.");
        }
        if (!is_array($quest['objectives'])
            || !array_is_list($quest['objectives'])
            || count($quest['objectives']) < 1
            || count($quest['objectives']) > 20) {
            throw new RuntimeException("Quest '$id' has invalid objectives.");
        }
        foreach ($quest['objectives'] as $objective) {
            $this->requireText($objective, 500, "Quest '$id' objective");
        }

        $dates = $quest['dates'];
        if (!is_array($dates)
            || array_diff(['accepted', 'expires'], array_keys($dates)) !== []
            || array_diff(array_keys($dates), ['accepted', 'expires', 'completed']) !== []) {
            throw new RuntimeException("Quest '$id' has invalid dates.");
        }
        $this->requireText($dates['accepted'], 100, "Quest '$id' accepted date", true);
        $this->requireText($dates['expires'], 100, "Quest '$id' expiration date", true);
        $completedOn = $dates['completed'] ?? '';
        $this->requireText($completedOn, 100, "Quest '$id' completed date", true);
        $metaDate = $quest['meta-date'] ?? '';
        $this->requireText($metaDate, 100, "Quest '$id' meta-date", true);


        $gatedBy = $quest['gated-by'];
        if (!is_array($gatedBy)
            || !array_is_list($gatedBy)
            || count($gatedBy) > 100) {
            throw new RuntimeException("Quest '$id' has invalid gates.");
        }
        $normalizedGates = [];
        foreach ($gatedBy as $characterKey) {
            if (!is_string($characterKey)
                || preg_match('/^[a-z0-9]+(?:-[a-z0-9]+)*$/D', $characterKey) !== 1
                || in_array($characterKey, $normalizedGates, true)) {
                throw new RuntimeException("Quest '$id' has an invalid gated-by value.");
            }
            $normalizedGates[] = $characterKey;
        }

        $unlockedBy = $quest['unlocked-by'];
        if (!is_array($unlockedBy)
            || !array_is_list($unlockedBy)
            || count($unlockedBy) > 100) {
            throw new RuntimeException("Quest '$id' has invalid prerequisites.");
        }
        $normalizedUnlocks = [];
        foreach ($unlockedBy as $requiredQuestId) {
            if (!is_string($requiredQuestId)
                || preg_match('/^[a-z0-9]+(?:-[a-z0-9]+)*$/D', $requiredQuestId) !== 1
                || in_array($requiredQuestId, $normalizedUnlocks, true)) {
                throw new RuntimeException("Quest '$id' has an invalid unlocked-by value.");
            }
            $normalizedUnlocks[] = $requiredQuestId;
        }

        $wikiUrl = $quest['wiki-url'];
        $this->requireText($wikiUrl, 500, "Quest '$id' wiki URL");
        if (preg_match(
            '~^https://publish\.obsidian\.md/scarlethorizons/(?:Locations|Meta|NPCs|Player-Contributed|Powers|Quests|Writings)/[^?#]+$~D',
            $wikiUrl) !== 1) {
            throw new RuntimeException("Quest '$id' has an invalid wiki URL.");
        }

        return [
            'id' => $id,
            'title' => $quest['title'],
            'summary' => $quest['summary'],
            'quest_giver' => $quest['giver'],
            'visibility' => $quest['visibility'],
            'state' => $quest['state'],
            'base_state' => $quest['state'],
            'objectives' => $quest['objectives'],
            'reward' => $quest['reward'],
            'accepted_on' => $dates['accepted'],
            'expires_on' => $dates['expires'],
            'completed_on' => $completedOn,
            'completed_meta_date' => $metaDate,
            'wiki_url' => $wikiUrl,
            'gated_by' => $normalizedGates,
            'unlocked_by' => $normalizedUnlocks,
        ];
    }

    private function requireText(
        mixed $value,
        int $maximumLength,
        string $label,
        bool $allowEmpty = false): void
    {
        if (!is_string($value)
            || strlen($value) > $maximumLength
            || (!$allowEmpty && trim($value) === '')) {
            throw new RuntimeException("$label is invalid.");
        }
    }
}
