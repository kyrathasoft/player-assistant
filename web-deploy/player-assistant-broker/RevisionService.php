<?php

declare(strict_types=1);

final class RevisionService
{
    public function __construct(
        private readonly PDO $database,
        private readonly string $questDataPath = '')
    {
    }

    public function forAccount(array $account): array
    {
        $accountId = (string)($account['id'] ?? '');
        $isDungeonMaster = (string)($account['role'] ?? '') === 'dm';
        $ownsTransaction = !$this->database->inTransaction();
        if ($ownsTransaction) {
            $this->database->beginTransaction();
        }
        try {
            $messageStatement = $this->database->prepare(
                'SELECT messages.id,
                        messages.sender_account_id,
                        sender.display_name AS sender_character_name,
                        messages.recipient_account_id,
                        recipient.display_name AS recipient_character_name,
                        messages.message,
                        messages.sent_at,
                        messages.read_at
                   FROM message_notifications messages
                   JOIN character_accounts sender ON sender.id = messages.sender_account_id
                   JOIN character_accounts recipient ON recipient.id = messages.recipient_account_id
                  WHERE messages.recipient_account_id = ? AND messages.read_at IS NULL
                  ORDER BY messages.sent_at ASC, messages.id ASC');
            $messageStatement->execute([$accountId]);
            $messageRows = $messageStatement->fetchAll();

            $recipientStatement = $this->database->prepare(
                "SELECT id, display_name
                   FROM character_accounts
                  WHERE role = 'player' AND enabled = 1 AND id <> ?
                  ORDER BY display_name COLLATE NOCASE, id");
            $recipientStatement->execute([$accountId]);
            $messageProjection = [
                'activities' => $messageRows,
                'player_recipients' => $recipientStatement->fetchAll(),
            ];

            if ($isDungeonMaster) {
                $questStatement = $this->database->query(
                    "SELECT requests.id,
                            requests.quest_id,
                            requests.requester_account_id,
                            accounts.display_name AS requester_character_name,
                            requests.status,
                            requests.created_at,
                            requests.decided_at,
                            requests.decided_by_account_id,
                            requests.requester_acknowledged_at
                       FROM quest_requests requests
                       JOIN character_accounts accounts ON accounts.id = requests.requester_account_id
                      WHERE requests.status = 'pending'
                      ORDER BY requests.created_at ASC, requests.id ASC");
            } else {
                $questStatement = $this->database->prepare(
                    "SELECT requests.id,
                            requests.quest_id,
                            requests.requester_account_id,
                            accounts.display_name AS requester_character_name,
                            requests.status,
                            requests.created_at,
                            requests.decided_at,
                            requests.decided_by_account_id,
                            requests.requester_acknowledged_at
                       FROM quest_requests requests
                       JOIN character_accounts accounts ON accounts.id = requests.requester_account_id
                      WHERE requests.requester_account_id = ?
                      ORDER BY requests.created_at ASC, requests.id ASC");
                $questStatement->execute([$accountId]);
            }
            $questRows = $questStatement->fetchAll();
            $questActivities = array_values(array_filter(
                $questRows,
                static fn(array $row): bool => $isDungeonMaster
                    || (in_array((string)$row['status'], ['approved', 'denied'], true)
                        && $row['requester_acknowledged_at'] === null)));
            $questProjection = [
                'account_requests' => $questRows,
                'state_overrides' => $this->database->query(
                    'SELECT quest_id, base_state, state, updated_at, updated_by_account_id
                       FROM quest_state_overrides
                      ORDER BY quest_id')->fetchAll(),
                'quest_data_sha256' => $this->questDataHash(),
            ];
            $result = [
                'schema_version' => 1,
                'observed_at' => gmdate(DATE_ATOM),
                'messages' => $this->revisionPayload($messageProjection, count($messageRows), true),
                'quests' => $this->revisionPayload($questProjection, count($questActivities), false),
            ];
            if ($ownsTransaction) {
                $this->database->commit();
            }
            return $result;
        } catch (Throwable $exception) {
            if ($ownsTransaction && $this->database->inTransaction()) {
                $this->database->rollBack();
            }
            throw $exception;
        }
    }

    private function revisionPayload(array $projection, int $activityCount, bool $isMessage): array
    {
        $payload = [
            'revision' => hash('sha256', json_encode($projection, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR)),
            'activity_count' => $activityCount,
        ];
        if ($isMessage) {
            $payload['unread_count'] = $activityCount;
        }
        return $payload;
    }

    private function questDataHash(): string
    {
        if ($this->questDataPath === '' || !is_file($this->questDataPath)) {
            return '';
        }
        $hash = hash_file('sha256', $this->questDataPath);
        if (!is_string($hash)) {
            throw new RuntimeException('Unable to hash quest data for revision metadata.');
        }
        return $hash;
    }
}
