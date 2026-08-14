<?php

declare(strict_types=1);

require_once __DIR__ . '/../player-assistant-broker/RevisionService.php';

function revisionAssert(bool $condition, string $message): void
{
    if (!$condition) {
        throw new RuntimeException($message);
    }
}

$database = new PDO('sqlite::memory:', null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
$database->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);
$database->exec("CREATE TABLE message_notifications (
    id TEXT PRIMARY KEY, sender_account_id TEXT NOT NULL, recipient_account_id TEXT NOT NULL,
    message TEXT NOT NULL, sent_at INTEGER NOT NULL, read_at INTEGER NULL
)");
$database->exec("CREATE TABLE character_accounts (
    id TEXT PRIMARY KEY, display_name TEXT NOT NULL, role TEXT NOT NULL, enabled INTEGER NOT NULL
)");
$database->exec("CREATE TABLE quest_state_overrides (
    quest_id TEXT PRIMARY KEY, base_state TEXT NOT NULL, state TEXT NOT NULL,
    updated_at INTEGER NOT NULL, updated_by_account_id TEXT NOT NULL
)");
$database->exec("CREATE TABLE quest_requests (
    id TEXT PRIMARY KEY, quest_id TEXT NOT NULL, requester_account_id TEXT NOT NULL,
    status TEXT NOT NULL, created_at INTEGER NOT NULL, decided_at INTEGER NULL,
    decided_by_account_id TEXT NULL, requester_acknowledged_at INTEGER NULL
)");

$playerId = str_repeat('a', 32);
$otherPlayerId = str_repeat('b', 32);
$dmId = str_repeat('d', 32);
$database->prepare('INSERT INTO character_accounts (id, display_name, role, enabled) VALUES (?, ?, ?, 1)')
    ->execute([$playerId, 'Player One', 'player']);
$database->prepare('INSERT INTO character_accounts (id, display_name, role, enabled) VALUES (?, ?, ?, 1)')
    ->execute([$otherPlayerId, 'Player Two', 'player']);
$database->prepare('INSERT INTO character_accounts (id, display_name, role, enabled) VALUES (?, ?, ?, 1)')
    ->execute([$dmId, 'Dungeon Master', 'dm']);
$questDataPath = tempnam(sys_get_temp_dir(), 'revision-quests-');
if ($questDataPath === false) {
    throw new RuntimeException('Unable to create the quest revision fixture.');
}
file_put_contents($questDataPath, '{"schema_version":1,"quests":[{"id":"test-quest","title":"Original title"}]}');
$service = new RevisionService($database, $questDataPath);
$player = ['id' => $playerId, 'role' => 'player'];
$dm = ['id' => $dmId, 'role' => 'dm'];

$empty = $service->forAccount($player);
revisionAssert($empty['schema_version'] === 1, 'Revision schema version is invalid.');
revisionAssert($empty['messages']['unread_count'] === 0, 'Empty message count is invalid.');
revisionAssert($empty['quests']['activity_count'] === 0, 'Empty quest activity count is invalid.');
revisionAssert(preg_match('/^[a-f0-9]{64}$/D', $empty['messages']['revision']) === 1, 'Message revision token is invalid.');

$database->prepare(
    'INSERT INTO message_notifications (id, sender_account_id, recipient_account_id, message, sent_at, read_at)
     VALUES (?, ?, ?, ?, ?, NULL)')
    ->execute([str_repeat('1', 32), $dmId, $playerId, 'New message', 100]);
$database->prepare(
    'INSERT INTO quest_requests
        (id, quest_id, requester_account_id, status, created_at, decided_at, decided_by_account_id, requester_acknowledged_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, NULL)')
    ->execute([str_repeat('2', 32), 'test-quest', $playerId, 'approved', 90, 110, $dmId]);
$playerRevision = $service->forAccount($player);
revisionAssert($playerRevision['messages']['unread_count'] === 1, 'Player unread count is invalid.');
revisionAssert($playerRevision['quests']['activity_count'] === 1, 'Player quest decision count is invalid.');
revisionAssert($playerRevision['messages']['revision'] !== $empty['messages']['revision'], 'Message revision did not change.');
revisionAssert($playerRevision['quests']['revision'] !== $empty['quests']['revision'], 'Quest revision did not change.');

$messageBeforeRename = $playerRevision['messages']['revision'];
$database->prepare('UPDATE character_accounts SET display_name = ? WHERE id = ?')
    ->execute(['Renamed Dungeon Master', $dmId]);
$messageAfterRename = $service->forAccount($player)['messages']['revision'];
revisionAssert($messageAfterRename !== $messageBeforeRename, 'A message sender rename did not change the message resource revision.');

$recipientRevisionBeforeRename = $service->forAccount($dm)['messages']['revision'];
$database->prepare('UPDATE character_accounts SET display_name = ? WHERE id = ?')
    ->execute(['Renamed Player Two', $otherPlayerId]);
$recipientRevisionAfterRename = $service->forAccount($dm)['messages']['revision'];
revisionAssert($recipientRevisionAfterRename !== $recipientRevisionBeforeRename, 'A recipient-list rename did not change the message resource revision.');

$questRevisionBeforeTitleChange = $service->forAccount($player)['quests']['revision'];
file_put_contents($questDataPath, '{"schema_version":1,"quests":[{"id":"test-quest","title":"Updated title"}]}');
clearstatcache(true, $questDataPath);
$questRevisionAfterTitleChange = $service->forAccount($player)['quests']['revision'];
revisionAssert($questRevisionAfterTitleChange !== $questRevisionBeforeTitleChange, 'A quest title change did not change the quest resource revision.');

$database->prepare(
    'INSERT INTO message_notifications (id, sender_account_id, recipient_account_id, message, sent_at, read_at)
     VALUES (?, ?, ?, ?, ?, NULL)')
    ->execute([str_repeat('9', 32), $dmId, $playerId, 'Same-second high message', 100]);
$messageSetBeforeReplacement = $service->forAccount($player);
$database->prepare('UPDATE message_notifications SET read_at = ? WHERE id = ?')
    ->execute([101, str_repeat('1', 32)]);
$database->prepare(
    'INSERT INTO message_notifications (id, sender_account_id, recipient_account_id, message, sent_at, read_at)
     VALUES (?, ?, ?, ?, ?, NULL)')
    ->execute([str_repeat('4', 32), $dmId, $playerId, 'Same-second replacement message', 100]);
$messageSetAfterReplacement = $service->forAccount($player);
revisionAssert(
    $messageSetBeforeReplacement['messages']['activity_count'] === $messageSetAfterReplacement['messages']['activity_count'],
    'Message replacement fixture changed the activity count.');
revisionAssert(
    $messageSetBeforeReplacement['messages']['revision'] !== $messageSetAfterReplacement['messages']['revision'],
    'Distinct same-second unread message sets produced the same revision token.');

$database->prepare(
    'INSERT INTO quest_requests
        (id, quest_id, requester_account_id, status, created_at, decided_at, decided_by_account_id, requester_acknowledged_at)
     VALUES (?, ?, ?, \'pending\', ?, NULL, NULL, NULL)')
    ->execute([str_repeat('3', 32), 'other-quest', $otherPlayerId, 120]);
$dmRevision = $service->forAccount($dm);
revisionAssert($dmRevision['messages']['unread_count'] === 0, 'DM received another account\'s unread count.');
revisionAssert($dmRevision['quests']['activity_count'] === 1, 'DM pending-request count is invalid.');
revisionAssert($service->forAccount($player)['quests']['activity_count'] === 1, 'Player received another account\'s pending request.');

$database->prepare(
    'INSERT INTO quest_requests
        (id, quest_id, requester_account_id, status, created_at, decided_at, decided_by_account_id, requester_acknowledged_at)
     VALUES (?, ?, ?, \'pending\', ?, NULL, NULL, NULL)')
    ->execute([str_repeat('9', 32), 'high-pending-quest', $playerId, 120]);
$questSetBeforeReplacement = $service->forAccount($dm);
$database->prepare(
    "UPDATE quest_requests SET status = 'approved', decided_at = ?, decided_by_account_id = ? WHERE id = ?")
    ->execute([121, $dmId, str_repeat('3', 32)]);
$database->prepare(
    'INSERT INTO quest_requests
        (id, quest_id, requester_account_id, status, created_at, decided_at, decided_by_account_id, requester_acknowledged_at)
     VALUES (?, ?, ?, \'pending\', ?, NULL, NULL, NULL)')
    ->execute([str_repeat('5', 32), 'replacement-pending-quest', $otherPlayerId, 120]);
$questSetAfterReplacement = $service->forAccount($dm);
revisionAssert(
    $questSetBeforeReplacement['quests']['activity_count'] === $questSetAfterReplacement['quests']['activity_count'],
    'Quest replacement fixture changed the activity count.');
revisionAssert(
    $questSetBeforeReplacement['quests']['revision'] !== $questSetAfterReplacement['quests']['revision'],
    'Distinct same-second pending quest sets produced the same revision token.');

@unlink($questDataPath);
echo "Revision service tests passed.\n";
