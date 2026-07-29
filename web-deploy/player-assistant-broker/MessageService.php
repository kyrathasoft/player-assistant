<?php

declare(strict_types=1);

final class MessageService
{
    public function __construct(private readonly PDO $database)
    {
        $this->ensureSchema();
    }

    public function sendForAccount(array $account, array $body): array
    {
        $senderRole = (string)($account['role'] ?? '');
        $message = is_string($body['message'] ?? null)
            ? trim((string)$body['message'])
            : '';
        if ($message === '' || strlen($message) > 5000) {
            throw new BrokerHttpException(
                400,
                'invalid_message',
                'The message must be a non-empty text string up to 5000 characters.');
        }

        if ($senderRole === 'dm' && (string)($body['recipient_role'] ?? '') === 'all_players') {
            $recipients = $this->loadPlayerAccounts();
            if ($recipients === []) {
                throw new BrokerHttpException(
                    404,
                    'players_unavailable',
                    'No player accounts are available.');
            }
            $statement = $this->database->prepare(
                'INSERT INTO message_notifications (
                    id,
                    sender_account_id,
                    recipient_account_id,
                    message,
                    sent_at,
                    read_at
                ) VALUES (?, ?, ?, ?, ?, NULL)');
            $now = time();
            $this->database->beginTransaction();
            try {
                foreach ($recipients as $recipient) {
                    $statement->execute([
                        bin2hex(random_bytes(16)),
                        (string)($account['id'] ?? ''),
                        (string)$recipient['id'],
                        $message,
                        $now,
                    ]);
                }
                $this->database->commit();
            } catch (Throwable $exception) {
                if ($this->database->inTransaction()) {
                    $this->database->rollBack();
                }
                throw $exception;
            }
            return [
                'schema_version' => 1,
                'message' => [
                    'recipient_character_name' => 'Every player',
                    'sender_character_name' => (string)($account['character_name'] ?? ''),
                    'message' => $message,
                    'sent_at' => gmdate(DATE_ATOM, $now),
                    'recipient_count' => count($recipients),
                    'status' => 'sent',
                ],
            ];
        }

        if ($senderRole === 'dm') {
            $recipientId = is_string($body['recipient_account_id'] ?? null)
                ? (string)$body['recipient_account_id']
                : '';
            if (!preg_match('/^[a-f0-9]{32}$/D', $recipientId)) {
                throw new BrokerHttpException(
                    400,
                    'invalid_message_recipient',
                    'The selected player account identifier is invalid.');
            }
            $recipient = $this->loadAccountById($recipientId);
            if (!is_array($recipient) || (string)($recipient['role'] ?? '') !== 'player') {
                throw new BrokerHttpException(
                    400,
                    'invalid_message_recipient',
                    'The selected recipient is not a player account.');
            }
        } elseif ($senderRole === 'player') {
            if ((string)($body['recipient_role'] ?? '') === 'dm') {
                $recipient = $this->loadDungeonMasterAccount();
                if (!is_array($recipient)) {
                    throw new BrokerHttpException(
                        404,
                        'dm_unavailable',
                        'The Dungeon Master account is not available.');
                }
                $recipientId = (string)$recipient['id'];
            } else {
                $recipientId = is_string($body['recipient_account_id'] ?? null)
                    ? (string)$body['recipient_account_id']
                    : '';
                if (!preg_match('/^[a-f0-9]{32}$/D', $recipientId)
                    || hash_equals((string)($account['id'] ?? ''), $recipientId)) {
                    throw new BrokerHttpException(
                        400,
                        'invalid_message_recipient',
                        'The selected player account identifier is invalid.');
                }
                $recipient = $this->loadAccountById($recipientId);
                if (!is_array($recipient) || (string)($recipient['role'] ?? '') !== 'player') {
                    throw new BrokerHttpException(
                        400,
                        'invalid_message_recipient',
                        'The selected recipient is not a player account.');
                }
            }
        } else {
            throw new BrokerHttpException(
                403,
                'messages_not_authorized',
                'Only characters and the Dungeon Master may send messages.');
        }

        $messageId = bin2hex(random_bytes(16));
        $now = time();
        $this->database->prepare(
            'INSERT INTO message_notifications (
                id,
                sender_account_id,
                recipient_account_id,
                message,
                sent_at,
                read_at
            ) VALUES (?, ?, ?, ?, ?, NULL)'
        )->execute([
            $messageId,
            (string)($account['id'] ?? ''),
            $recipientId,
            $message,
            $now,
        ]);

        return [
            'schema_version' => 1,
            'message' => [
                'id' => $messageId,
                'recipient_character_name' => (string)($recipient['display_name'] ?? ''),
                'sender_character_name' => (string)($account['character_name'] ?? ''),
                'message' => $message,
                'sent_at' => gmdate(DATE_ATOM, $now),
                'status' => 'sent',
            ],
        ];
    }

    public function forAccount(array $account): array
    {
        $statement = $this->database->prepare(
            'SELECT messages.id,
                    messages.message,
                    messages.sent_at,
                    messages.read_at,
                    sender.display_name AS sender_character_name,
                    recipient.display_name AS recipient_character_name
               FROM message_notifications messages
               JOIN character_accounts sender ON sender.id = messages.sender_account_id
               JOIN character_accounts recipient ON recipient.id = messages.recipient_account_id
              WHERE messages.recipient_account_id = ?
                AND messages.read_at IS NULL
              ORDER BY messages.sent_at DESC, messages.id DESC');
        $statement->execute([(string)($account['id'] ?? '')]);

        $messages = [];
        foreach ($statement->fetchAll() as $row) {
            $messages[] = [
                'id' => (string)$row['id'],
                'sender_character_name' => (string)$row['sender_character_name'],
                'recipient_character_name' => (string)$row['recipient_character_name'],
                'message' => (string)$row['message'],
                'sent_at' => gmdate(DATE_ATOM, (int)$row['sent_at']),
                'read_at' => $row['read_at'] === null
                    ? null
                    : gmdate(DATE_ATOM, (int)$row['read_at']),
            ];
        }

        $recipientStatement = $this->database->prepare(
            'SELECT id, display_name
               FROM character_accounts
              WHERE role = \'player\'
                AND enabled = 1
                AND id <> ?
              ORDER BY display_name COLLATE NOCASE, id');
        $recipientStatement->execute([(string)($account['id'] ?? '')]);
        $recipients = [];
        foreach ($recipientStatement->fetchAll() as $row) {
            $recipients[] = [
                'account_id' => (string)$row['id'],
                'character_name' => (string)$row['display_name'],
            ];
        }

        return [
            'schema_version' => 2,
            'messages' => $messages,
            'player_recipients' => $recipients,
        ];
    }

    public function markRead(array $account, string $messageId): array
    {
        if (!preg_match('/^[a-f0-9]{32}$/D', $messageId)) {
            throw new BrokerHttpException(
                404,
                'message_not_found',
                'The unread message was not found.');
        }
        $statement = $this->database->prepare(
            'UPDATE message_notifications
                SET read_at = COALESCE(read_at, ?)
              WHERE id = ? AND recipient_account_id = ? AND read_at IS NULL'
        );
        $statement->execute([
            time(),
            $messageId,
            (string)($account['id'] ?? ''),
        ]);
        if ($statement->rowCount() !== 1) {
            throw new BrokerHttpException(
                404,
                'message_not_found',
                'The unread message was not found.');
        }
        return [
            'schema_version' => 1,
            'message' => [
                'id' => $messageId,
                'status' => 'read',
            ],
        ];
    }

    private function loadDungeonMasterAccount(): ?array
    {
        $statement = $this->database->prepare(
            'SELECT id, display_name, role
               FROM character_accounts
              WHERE role = \'dm\'
                AND enabled = 1
              LIMIT 1');
        $statement->execute();
        $dm = $statement->fetch();
        return is_array($dm) ? $dm : null;
    }

    private function loadAccountById(string $accountId): ?array
    {
        $statement = $this->database->prepare(
            'SELECT id, display_name, role
               FROM character_accounts
              WHERE id = ? AND enabled = 1
              LIMIT 1');
        $statement->execute([$accountId]);
        $account = $statement->fetch();
        return is_array($account) ? $account : null;
    }

    private function loadPlayerAccounts(): array
    {
        $statement = $this->database->prepare(
            'SELECT id, display_name, role
               FROM character_accounts
              WHERE role = \'player\' AND enabled = 1
              ORDER BY display_name COLLATE NOCASE, id');
        $statement->execute();
        return $statement->fetchAll();
    }

    private function ensureSchema(): void
    {
        $this->database->exec(
            'CREATE TABLE IF NOT EXISTS message_notifications (
                id TEXT PRIMARY KEY,
                sender_account_id TEXT NOT NULL,
                recipient_account_id TEXT NOT NULL,
                message TEXT NOT NULL,
                sent_at INTEGER NOT NULL,
                read_at INTEGER NULL,
                FOREIGN KEY (sender_account_id) REFERENCES character_accounts(id) ON DELETE CASCADE,
                FOREIGN KEY (recipient_account_id) REFERENCES character_accounts(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_message_notifications_recipient_read
                ON message_notifications(recipient_account_id, read_at, sent_at);');
    }
}

