<?php
declare(strict_types=1);
require_once __DIR__ . '/../player-assistant-broker/BrokerHttpException.php';
require_once __DIR__ . '/../player-assistant-broker/MessageService.php';
function hardeningAssert(bool $ok, string $message): void { if (!$ok) throw new RuntimeException($message); }
$db = new PDO('sqlite::memory:', null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION, PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC]);
$db->exec("CREATE TABLE character_accounts (id TEXT PRIMARY KEY, display_name TEXT NOT NULL, role TEXT NOT NULL, enabled INTEGER NOT NULL);
CREATE TABLE message_notifications (id TEXT PRIMARY KEY, sender_account_id TEXT NOT NULL, recipient_account_id TEXT NOT NULL, message TEXT NOT NULL, sent_at INTEGER NOT NULL, read_at INTEGER NULL);
CREATE TABLE message_send_rate_limits (account_id TEXT PRIMARY KEY, window_started_at INTEGER NOT NULL, send_count INTEGER NOT NULL);");
$dm = str_repeat('a', 32); $p1 = str_repeat('b', 32); $p2 = str_repeat('c', 32);
$insert = $db->prepare('INSERT INTO character_accounts VALUES (?, ?, ?, 1)'); $insert->execute([$dm, 'DM', 'dm']); $insert->execute([$p1, 'P1', 'player']); $insert->execute([$p2, 'P2', 'player']);
$service = new MessageService($db); $account = ['id' => $dm, 'role' => 'dm', 'character_name' => 'DM'];
for ($i = 0; $i < 19; $i++) $service->sendForAccount($account, ['recipient_account_id' => $p1, 'message' => 'hello']);
$broadcast = $service->sendForAccount($account, ['recipient_role' => 'all_players', 'message' => 'broadcast']);
hardeningAssert($broadcast['message']['recipient_count'] === 2, 'A DM broadcast must remain a single throttled operation for all players.');
try { $service->sendForAccount($account, ['recipient_account_id' => $p1, 'message' => 'blocked']); throw new RuntimeException('The per-account message throttle did not reject the boundary send.'); }
catch (BrokerHttpException $e) { hardeningAssert($e->status === 429 && $e->errorName === 'message_rate_limited', 'The throttle returned the wrong error contract.'); }
$other = ['id' => $p1, 'role' => 'player', 'character_name' => 'P1']; $service->sendForAccount($other, ['recipient_role' => 'dm', 'message' => 'independent']);
echo "Message hardening tests passed.\n";
