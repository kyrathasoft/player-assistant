<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/MessageService.php';

function messagePaginationAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

final class ConcurrentMessageReadPdo extends PDO
{
    private bool $armed = false;

    public function __construct(string $dsn, private readonly Closure $concurrentWrite)
    {
        parent::__construct($dsn, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
        $this->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);
    }

    public function armConcurrentWrite(): void
    {
        $this->armed = true;
    }

    public function prepare(string $query, array $options = []): PDOStatement|false
    {
        if ($this->armed && str_contains($query, 'SELECT COUNT(*) FROM message_notifications')) {
            $this->armed = false;
            ($this->concurrentWrite)();
        }
        return parent::prepare($query, $options);
    }
}

$database = new PDO('sqlite::memory:', null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
$database->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);
$database->exec("CREATE TABLE character_accounts (
    id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    role TEXT NOT NULL,
    enabled INTEGER NOT NULL
)");
$senderId = str_repeat('a', 32);
$recipientId = str_repeat('b', 32);
$database->prepare('INSERT INTO character_accounts (id, display_name, role, enabled) VALUES (?, ?, ?, 1)')
    ->execute([$senderId, 'Sender', 'player']);
$database->prepare('INSERT INTO character_accounts (id, display_name, role, enabled) VALUES (?, ?, ?, 1)')
    ->execute([$recipientId, 'Recipient', 'player']);

$service = new MessageService($database, [
    'retention_days' => 30,
    'max_read_messages_per_account' => 2,
]);
$insert = $database->prepare(
    'INSERT INTO message_notifications (id, sender_account_id, recipient_account_id, message, sent_at, read_at)
     VALUES (?, ?, ?, ?, ?, ?)');
for ($index = 1; $index <= 205; $index++) {
    $insert->execute([
        str_pad(dechex($index), 32, '0', STR_PAD_LEFT),
        $senderId,
        $recipientId,
        'Unread ' . $index,
        1700000000 + intdiv($index, 3),
        null,
    ]);
}
$account = ['id' => $recipientId, 'role' => 'player'];

foreach ([
    ['retention_days' => 'invalid'],
    ['max_read_messages_per_account' => -1],
] as $invalidConfig) {
    try {
        new MessageService($database, $invalidConfig);
        throw new RuntimeException('Invalid retention configuration was accepted.');
    } catch (InvalidArgumentException) {
    }
}

$first = $service->forAccount($account, ['limit' => '100']);
messagePaginationAssert($first['schema_version'] === 3, 'Paginated messages must use schema version 3.');
messagePaginationAssert(count($first['messages']) === 100, 'The first page must respect the requested limit.');
messagePaginationAssert($first['unread_count'] === 205, 'The response must report the total unread count.');
messagePaginationAssert(is_string($first['next_cursor']) && $first['next_cursor'] !== '', 'The first page must return a continuation cursor.');
messagePaginationAssert(count(array_unique(array_column($first['messages'], 'id'))) === 100, 'The first page contains duplicate messages.');

$second = $service->forAccount($account, ['limit' => '100', 'cursor' => $first['next_cursor']]);
$third = $service->forAccount($account, ['limit' => '100', 'cursor' => $second['next_cursor']]);
$allIds = array_merge(
    array_column($first['messages'], 'id'),
    array_column($second['messages'], 'id'),
    array_column($third['messages'], 'id'));
messagePaginationAssert(count($second['messages']) === 100, 'The second page must contain the next 100 messages.');
messagePaginationAssert(count($third['messages']) === 5 && $third['next_cursor'] === null, 'The final page must contain the remainder and no cursor.');
messagePaginationAssert(count(array_unique($allIds)) === 205, 'Cursor navigation must neither duplicate nor skip messages.');
messagePaginationAssert($second['unread_count'] === 205, 'Unread count must remain total-count metadata on later pages.');

$concurrencyPath = tempnam(sys_get_temp_dir(), 'pa-message-concurrency-');
if ($concurrencyPath === false) {
    throw new RuntimeException('Unable to create the message concurrency fixture.');
}
try {
    $writer = new PDO('sqlite:' . $concurrencyPath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
    $writer->exec('PRAGMA journal_mode = WAL');
    $writer->exec("CREATE TABLE character_accounts (
        id TEXT PRIMARY KEY,
        display_name TEXT NOT NULL,
        role TEXT NOT NULL,
        enabled INTEGER NOT NULL
    )");
    $writer->prepare('INSERT INTO character_accounts (id, display_name, role, enabled) VALUES (?, ?, ?, 1)')
        ->execute([$senderId, 'Sender', 'player']);
    $writer->prepare('INSERT INTO character_accounts (id, display_name, role, enabled) VALUES (?, ?, ?, 1)')
        ->execute([$recipientId, 'Recipient', 'player']);
    $concurrentId = str_repeat('c', 32);
    $reader = new ConcurrentMessageReadPdo(
        'sqlite:' . $concurrencyPath,
        static function () use ($writer, $senderId, $recipientId, $concurrentId): void {
            $writer->prepare(
                'INSERT INTO message_notifications
                    (id, sender_account_id, recipient_account_id, message, sent_at, read_at)
                 VALUES (?, ?, ?, ?, ?, NULL)')
                ->execute([$concurrentId, $senderId, $recipientId, 'Concurrent unread', 1700000100]);
        });
    $reader->exec('PRAGMA journal_mode = WAL');
    $concurrentService = new MessageService($reader);
    $writer->prepare(
        'INSERT INTO message_notifications
            (id, sender_account_id, recipient_account_id, message, sent_at, read_at)
         VALUES (?, ?, ?, ?, ?, NULL)')
        ->execute([str_repeat('d', 32), $senderId, $recipientId, 'Snapshot unread', 1700000000]);
    $reader->armConcurrentWrite();
    $snapshot = $concurrentService->forAccount($account, ['limit' => '10']);
    messagePaginationAssert(
        count($snapshot['messages']) === 1 && $snapshot['unread_count'] === 1,
        'A concurrent write between the page and count queries escaped the shared read snapshot.');
    messagePaginationAssert(
        (int)$writer->query('SELECT COUNT(*) FROM message_notifications')->fetchColumn() === 2,
        'The concurrency fixture did not commit its second-connection write.');
} finally {
    unset($reader, $writer, $concurrentService);
    @unlink($concurrencyPath . '-wal');
    @unlink($concurrencyPath . '-shm');
    @unlink($concurrencyPath);
}

try {
    $service->forAccount($account, ['cursor' => 'not-a-valid-cursor']);
    throw new RuntimeException('An invalid message cursor was accepted.');
} catch (BrokerHttpException $exception) {
    messagePaginationAssert(
        $exception->status === 400 && $exception->errorName === 'invalid_message_cursor',
        'Invalid cursors must fail with the documented client error.');
}

$now = time();
for ($index = 301; $index <= 304; $index++) {
    $insert->execute([
        str_pad(dechex($index), 32, '0', STR_PAD_LEFT),
        $senderId,
        $recipientId,
        'Read ' . $index,
        $now - ($index === 301 ? 40 * 86400 : $index),
        $now - ($index === 301 ? 40 * 86400 : $index),
    ]);
}
$service->markRead($account, str_pad(dechex(205), 32, '0', STR_PAD_LEFT));
$readCount = (int)$database->query(
    "SELECT COUNT(*) FROM message_notifications WHERE recipient_account_id = '$recipientId' AND read_at IS NOT NULL")
    ->fetchColumn();
$unreadCount = (int)$database->query(
    "SELECT COUNT(*) FROM message_notifications WHERE recipient_account_id = '$recipientId' AND read_at IS NULL")
    ->fetchColumn();
messagePaginationAssert($readCount === 2, 'Retention must remove expired and excess read messages.');
messagePaginationAssert($unreadCount === 204, 'Retention must never remove unread messages other than the acknowledged message.');

echo "Message pagination tests passed.\n";
