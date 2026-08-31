<?php

declare(strict_types=1);

/**
 * Cross-domain publication/recovery invariants. Messages intentionally contain
 * invariant names only; protected record values and ownership details never do.
 */
final class DataInvariantContract
{
    public static function fail(string $name): never
    {
        throw new RuntimeException("Invariant failed: $name");
    }

    public static function assertXpSnapshot(array $characters, string $scope = 'party'): void
    {
        if (!in_array($scope, ['party', 'character'], true) || $characters === []) self::fail('xp.shape');
        $seen = [];
        foreach ($characters as $character) {
            if (!is_array($character)
                || !is_string($character['character_name'] ?? null)
                || trim($character['character_name']) !== $character['character_name']
                || $character['character_name'] === ''
                || !is_int($character['level'] ?? null) || $character['level'] < 0
                || !is_int($character['xp_total'] ?? null) || $character['xp_total'] < 0) self::fail('xp.authoritative-shape');
            $key = strtolower($character['character_name']);
            if (isset($seen[$key])) self::fail('xp.unique-character');
            $seen[$key] = true;
        }
    }

    public static function assertAwards(array $entries, string $progressionKey): void
    {
        if ($entries === [] || !preg_match('/^[a-z0-9]+(?:-[a-z0-9]+)*$/D', $progressionKey)) self::fail('awards.shape');
        $previousDate = null; $seen = [];
        foreach ($entries as $entry) {
            $fields = ['character_name','character_class','level_before_award','xp_award','xp_award_date','level_after_award'];
            if (!is_array($entry) || array_keys($entry) !== $fields
                || !is_string($entry['character_name']) || $entry['character_name'] === ''
                || !is_string($entry['xp_award_date']) || !is_int($entry['level_before_award'])
                || !is_int($entry['level_after_award']) || !is_int($entry['xp_award'])
                || $entry['level_before_award'] < 0 || $entry['level_after_award'] < 0 || $entry['xp_award'] < 0) self::fail('awards.authoritative-shape');
            $date = DateTimeImmutable::createFromFormat('!n.j.Y', trim($entry['xp_award_date']));
            if (!$date || ($previousDate !== null && $date < $previousDate)) self::fail('awards.monotonic-date');
            $fingerprint = $entry['character_name'].'\0'.$entry['xp_award_date'].'\0'.$entry['xp_award'].'\0'.$entry['level_before_award'].'\0'.$entry['level_after_award'];
            if (isset($seen[$fingerprint])) self::fail('awards.unique-event');
            $seen[$fingerprint] = true; $previousDate = $date;
        }
    }

    public static function assertWordCounts(array $snapshot, ?array $previous = null): void
    {
        foreach (['wiki'=>'pages','ic'=>'files','ooc'=>'files'] as $section=>$unit) {
            if (!is_array($snapshot[$section] ?? null) || !is_int($snapshot[$section][$unit] ?? null)
                || !is_int($snapshot[$section]['words'] ?? null) || $snapshot[$section][$unit] < 1
                || $snapshot[$section]['words'] < 0) self::fail('word-count.bounded-shape');
        }
        if ($previous !== null) {
            foreach (['wiki'=>'pages','ic'=>'files','ooc'=>'files'] as $section=>$unit) {
                if ($snapshot[$section][$unit] < $previous[$section][$unit]
                    || $snapshot[$section]['words'] < $previous[$section]['words']) self::fail('word-count.monotonic');
            }
        }
    }

    public static function assertQuests(array $quests): void
    {
        $ids = []; foreach ($quests as $quest) {
            if (!is_array($quest) || !is_string($quest['id'] ?? null) || isset($ids[$quest['id']])) self::fail('quests.unique-id');
            $ids[$quest['id']] = true;
        }
        foreach ($quests as $quest) {
            foreach (($quest['unlocked_by'] ?? []) as $required) {
                if (!is_string($required) || $required === $quest['id'] || !isset($ids[$required])) self::fail('quests.referential-join');
            }
        }
        self::assertQuestAcyclic($quests, $ids);
    }

    private static function assertQuestAcyclic(array $quests, array $ids): void
    {
        $graph = []; foreach ($quests as $quest) $graph[$quest['id']] = $quest['unlocked_by'] ?? [];
        $visiting = []; $visited = [];
        $visit = function (string $id) use (&$visit, &$graph, &$visiting, &$visited): void {
            if (isset($visiting[$id])) self::fail('quests.acyclic-prerequisites');
            if (isset($visited[$id])) return;
            $visiting[$id] = true; foreach ($graph[$id] as $next) $visit($next);
            unset($visiting[$id]); $visited[$id] = true;
        };
        foreach (array_keys($ids) as $id) $visit($id);
    }

    public static function assertMessages(array $messages, string $accountId): void
    {
        if (!preg_match('/^[a-f0-9]{32}$/D', $accountId)) self::fail('messages.account-scope');
        $seen = [];
        foreach ($messages as $message) {
            if (!is_array($message) || !is_string($message['id'] ?? null)
                || !is_string($message['message'] ?? null) || trim($message['message']) === ''
                || !is_int($message['sent_at'] ?? null) || $message['sent_at'] < 0
                || array_key_exists('recipient_account_id', $message) && $message['recipient_account_id'] !== $accountId) self::fail('messages.authoritative-ownership');
            if (isset($seen[$message['id']])) self::fail('messages.unique-id');
            $seen[$message['id']] = true;
        }
    }

    public static function assertRosterJoin(array $roster, array $xp, ?string $accountCharacterKey = null): void
    {
        $rosterIds = []; foreach ($roster as $row) {
            $id = is_array($row) ? ($row['character_key'] ?? null) : null;
            if (!is_string($id) || preg_match('/^[a-z0-9][a-z0-9._:-]{0,99}$/D', $id) !== 1 || isset($rosterIds[$id])) self::fail('roster.unique-canonical-id');
            $rosterIds[$id] = true;
        }
        $xpIds = []; foreach ($xp as $row) {
            $id = is_array($row) ? ($row['character_key'] ?? null) : null;
            if (!is_string($id) || !isset($rosterIds[$id]) || isset($xpIds[$id])) self::fail('roster.xp-referential-join');
            $xpIds[$id] = true;
        }
        if ($accountCharacterKey !== null && !isset($rosterIds[$accountCharacterKey])) self::fail('roster.account-ownership');
    }
}
