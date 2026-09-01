<?php

declare(strict_types=1);

require_once __DIR__ . '/DataInvariantContract.php';

final class MessageService
{
    private int $retentionDays;
    private int $maxReadMessagesPerAccount;
    private const SEND_WINDOW_SECONDS = 60;
    private const MAX_SENDS_PER_WINDOW = 20;

    public function __construct(private readonly PDO $database, array $config = [])
    {
        $this->retentionDays = $this->parseRetentionSetting(
            $config['retention_days'] ?? 90,
            1,
            3650,
            'retention_days');
        $this->maxReadMessagesPerAccount = $this->parseRetentionSetting(
            $config['max_read_messages_per_account'] ?? 500,
            0,
            10000,
            'max_read_messages_per_account');
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
                $this->enforceSendThrottle((string)($account['id'] ?? ''));
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

        $ownsTransaction = !$this->database->inTransaction();
        if ($ownsTransaction) $this->database->beginTransaction();
        try {
            $this->enforceSendThrottle((string)($account['id'] ?? ''));
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
            if ($ownsTransaction) $this->database->commit();
        } catch (Throwable $exception) {
            if ($ownsTransaction && $this->database->inTransaction()) $this->database->rollBack();
            throw $exception;
        }

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

    public function forAccount(array $account, array $query = []): array
    {
        $ownsTransaction = !$this->database->inTransaction();
        if ($ownsTransaction) {
            $this->database->beginTransaction();
        }
        try {
            $limit = $this->parseLimit($query['limit'] ?? null);
            $cursor = $this->parseCursor($query['cursor'] ?? null);
            $accountId = (string)($account['id'] ?? '');
            $where = 'messages.recipient_account_id = ? AND messages.read_at IS NULL';
            $parameters = [$accountId];
            if ($cursor !== null) {
                $where .= ' AND (messages.sent_at < ? OR (messages.sent_at = ? AND messages.id < ?))';
                $parameters[] = $cursor['sent_at'];
                $parameters[] = $cursor['sent_at'];
                $parameters[] = $cursor['id'];
            }
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
                  WHERE ' . $where . '
                  ORDER BY messages.sent_at DESC, messages.id DESC
                  LIMIT ?');
            $parameters[] = $limit + 1;
            $statement->execute($parameters);

            $rows = $statement->fetchAll();
            $invariantRows = array_map(static function (array $row) use ($accountId): array {
                return ['id' => (string)$row['id'], 'message' => (string)$row['message'],
                    'sent_at' => (int)$row['sent_at'], 'recipient_account_id' => $accountId];
            }, $rows);
            DataInvariantContract::assertMessages($invariantRows, $accountId);
            $hasMore = count($rows) > $limit;
            if ($hasMore) {
                array_pop($rows);
            }
            $messages = [];
            foreach ($rows as $row) {
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

            $countStatement = $this->database->prepare(
                'SELECT COUNT(*) FROM message_notifications WHERE recipient_account_id = ? AND read_at IS NULL');
            $countStatement->execute([$accountId]);
            $unreadCount = (int)$countStatement->fetchColumn();
            $nextCursor = null;
            if ($hasMore && $rows !== []) {
                $last = $rows[count($rows) - 1];
                $nextCursor = $this->encodeCursor((int)$last['sent_at'], (string)$last['id']);
            }

            $recipientStatement = $this->database->prepare(
                'SELECT id, display_name
                   FROM character_accounts
                  WHERE role = \'player\'
                    AND enabled = 1
                    AND id <> ?
                  ORDER BY display_name COLLATE NOCASE, id');
            $recipientStatement->execute([$accountId]);
            $recipients = [];
            foreach ($recipientStatement->fetchAll() as $row) {
                $recipients[] = [
                    'account_id' => (string)$row['id'],
                    'character_name' => (string)$row['display_name'],
                ];
            }

            $result = [
                'schema_version' => 3,
                'messages' => $messages,
                'unread_count' => $unreadCount,
                'next_cursor' => $nextCursor,
                'player_recipients' => $recipients,
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

    public function markRead(array $account, string $messageId): array
    {
        if (!preg_match('/^[a-f0-9]{32}$/D', $messageId)) {
            throw new BrokerHttpException(
                404,
                'message_not_found',
                'The unread message was not found.');
        }
        $ownsTransaction = !$this->database->inTransaction();
        if ($ownsTransaction) {
            $this->database->beginTransaction();
        }
        try {
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
            $this->applyRetention((string)($account['id'] ?? ''));
            if ($ownsTransaction) {
                $this->database->commit();
            }
        } catch (Throwable $exception) {
            if ($ownsTransaction && $this->database->inTransaction()) {
                $this->database->rollBack();
            }
            throw $exception;
        }
        return [
            'schema_version' => 1,
            'message' => [
                'id' => $messageId,
                'status' => 'read',
            ],
        ];
    }

    private function enforceSendThrottle(string $accountId): void
    {
        $now = time();
        $row = $this->database->prepare('SELECT window_started_at, send_count FROM message_send_rate_limits WHERE account_id = ?');
        $row->execute([$accountId]);
        $current = $row->fetch();
        if (!is_array($current) || $now - (int)$current['window_started_at'] >= self::SEND_WINDOW_SECONDS) {
            $this->database->prepare('INSERT INTO message_send_rate_limits(account_id, window_started_at, send_count) VALUES (?, ?, 1) ON CONFLICT(account_id) DO UPDATE SET window_started_at = excluded.window_started_at, send_count = 1')->execute([$accountId, $now]);
            return;
        }
        if ((int)$current['send_count'] >= self::MAX_SENDS_PER_WINDOW) {
            throw new BrokerHttpException(429, 'message_rate_limited', 'Message sending is temporarily rate limited.');
        }
        $updated = $this->database->prepare('UPDATE message_send_rate_limits SET send_count = send_count + 1 WHERE account_id = ? AND window_started_at = ? AND send_count < ?');
        $updated->execute([$accountId, (int)$current['window_started_at'], self::MAX_SENDS_PER_WINDOW]);
        if ($updated->rowCount() !== 1) throw new BrokerHttpException(429, 'message_rate_limited', 'Message sending is temporarily rate limited.');
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

    private function parseLimit(mixed $value): int
    {
        if ($value === null || $value === '') {
            return 50;
        }
        if ((!is_string($value) && !is_int($value))
            || preg_match('/^[1-9][0-9]{0,2}$/D', (string)$value) !== 1) {
            throw new BrokerHttpException(400, 'invalid_message_limit', 'The message page size is invalid.');
        }
        $limit = (int)$value;
        if ($limit > 100) {
            throw new BrokerHttpException(400, 'invalid_message_limit', 'The message page size must not exceed 100.');
        }
        return $limit;
    }

    private function parseRetentionSetting(mixed $value, int $minimum, int $maximum, string $name): int
    {
        if ((!is_int($value) && !is_string($value))
            || preg_match('/^(?:0|[1-9][0-9]*)$/D', (string)$value) !== 1) {
            throw new InvalidArgumentException("The message $name configuration is invalid.");
        }
        $parsed = (int)$value;
        if ($parsed < $minimum || $parsed > $maximum) {
            throw new InvalidArgumentException("The message $name configuration is out of range.");
        }
        return $parsed;
    }

    private function parseCursor(mixed $value): ?array
    {
        if ($value === null || $value === '') {
            return null;
        }
        if (!is_string($value) || strlen($value) > 256 || preg_match('/^[A-Za-z0-9_-]+$/D', $value) !== 1) {
            $this->invalidCursor();
        }
        $padding = (4 - strlen($value) % 4) % 4;
        $decoded = base64_decode(strtr($value, '-_', '+/') . str_repeat('=', $padding), true);
        if (!is_string($decoded)) {
            $this->invalidCursor();
        }
        try {
            $cursor = json_decode($decoded, true, 4, JSON_THROW_ON_ERROR);
        } catch (JsonException) {
            $this->invalidCursor();
        }
        if (!is_array($cursor)
            || array_keys($cursor) !== ['sent_at', 'id']
            || !is_int($cursor['sent_at'])
            || $cursor['sent_at'] < 0
            || !is_string($cursor['id'])
            || preg_match('/^[a-f0-9]{32}$/D', $cursor['id']) !== 1) {
            $this->invalidCursor();
        }
        return $cursor;
    }

    private function invalidCursor(): never
    {
        throw new BrokerHttpException(400, 'invalid_message_cursor', 'The message cursor is invalid.');
    }

    private function encodeCursor(int $sentAt, string $id): string
    {
        $json = json_encode(['sent_at' => $sentAt, 'id' => $id], JSON_THROW_ON_ERROR);
        return rtrim(strtr(base64_encode($json), '+/', '-_'), '=');
    }

    private function applyRetention(string $recipientAccountId): void
    {
        $cutoff = time() - ($this->retentionDays * 86400);
        $this->database->prepare(
            'DELETE FROM message_notifications
              WHERE recipient_account_id = ? AND read_at IS NOT NULL AND read_at < ?')
            ->execute([$recipientAccountId, $cutoff]);
        $this->database->prepare(
            'DELETE FROM message_notifications
              WHERE recipient_account_id = ? AND read_at IS NOT NULL
                AND id IN (
                    SELECT retained.id FROM message_notifications retained
                     WHERE retained.recipient_account_id = ?
                       AND retained.read_at IS NOT NULL
                     ORDER BY retained.read_at DESC, retained.id DESC
                     LIMIT -1 OFFSET ?
                )')
            ->execute([
                $recipientAccountId,
                $recipientAccountId,
                $this->maxReadMessagesPerAccount,
            ]);
    }

}