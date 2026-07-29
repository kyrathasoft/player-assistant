<?php

declare(strict_types=1);

final class QuestService
{
    private const VISIBILITY_VALUES = [
        'individual-only',
        'party-only',
        'individual-or-party',
    ];

    private const STATE_VALUES = [
        'available',
        'active',
        'available (abandoned)',
        'completed',
        'withdrawn',
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
        'wiki-url',
    ];

    public function __construct(private readonly string $dataPath)
    {
        if (trim($dataPath) === '') {
            throw new RuntimeException('The quest data path is not configured.');
        }
    }

    public function forAccount(array $account): array
    {
        $characterKey = strtolower(trim((string)($account['character_key'] ?? '')));
        $isDungeonMaster = (string)($account['role'] ?? '') === 'dm';
        $visible = array_values(array_filter(
            $this->loadQuests(),
            static fn(array $quest): bool =>
                $isDungeonMaster
                || $quest['visibility'] !== 'individual-only'
                || in_array($characterKey, $quest['gated_by'], true)));

        return [
            'schema_version' => 1,
            'status_values' => array_merge(self::VISIBILITY_VALUES, self::STATE_VALUES),
            'quests' => array_map(
                static function (array $quest): array {
                    unset($quest['gated_by']);
                    return $quest;
                },
                $visible),
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

        $quests = [];
        foreach ($payload['quests'] as $id => $quest) {
            $quests[] = $this->validateQuest($id, $quest);
        }
        return $quests;
    }

    private function validateQuest(mixed $id, mixed $quest): array
    {
        if (!is_string($id)
            || preg_match('/^[a-z0-9]+(?:-[a-z0-9]+)*$/D', $id) !== 1
            || !is_array($quest)) {
            throw new RuntimeException('A quest entry has an invalid identifier or shape.');
        }
        $actualFields = array_keys($quest);
        $expectedFields = self::QUEST_FIELDS;
        sort($actualFields);
        sort($expectedFields);
        if ($actualFields !== $expectedFields) {
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
            || array_keys($dates) !== ['accepted', 'expires']) {
            throw new RuntimeException("Quest '$id' has invalid dates.");
        }
        $this->requireText($dates['accepted'], 100, "Quest '$id' accepted date", true);
        $this->requireText($dates['expires'], 100, "Quest '$id' expiration date", true);

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

        $wikiUrl = $quest['wiki-url'];
        $this->requireText($wikiUrl, 500, "Quest '$id' wiki URL");
        if (preg_match(
            '~^https://publish\.obsidian\.md/scarlethorizons/(?:Quests|NPCs|Meta/IC|Writings)/[^?#]+$~D',
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
            'objectives' => $quest['objectives'],
            'reward' => $quest['reward'],
            'accepted_on' => $dates['accepted'],
            'expires_on' => $dates['expires'],
            'wiki_url' => $wikiUrl,
            'gated_by' => $normalizedGates,
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
