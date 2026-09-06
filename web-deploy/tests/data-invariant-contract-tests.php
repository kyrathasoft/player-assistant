<?php

declare(strict_types=1);
require_once __DIR__ . '/../player-assistant-broker/DataInvariantContract.php';
function invariantAssert(bool $ok, string $message): void { if (!$ok) throw new RuntimeException($message); }
function invariantReject(callable $fn, string $name): void {
    try { $fn(); } catch (RuntimeException $e) {
        invariantAssert(str_contains($e->getMessage(), "Invariant failed: $name"), "Wrong invariant for negative fixture: $name"); return;
    }
    throw new RuntimeException("Expected invariant failure: $name");
}
$xp = [['character_name'=>'Alpha','level'=>1,'xp_total'=>100], ['character_name'=>'Beta','level'=>2,'xp_total'=>200]];
DataInvariantContract::assertXpSnapshot($xp);
invariantReject(fn()=>DataInvariantContract::assertXpSnapshot([...$xp, ['character_name'=>'alpha','level'=>1,'xp_total'=>100]]), 'xp.unique-character');
$award = [['character_name'=>'Alpha','character_class'=>'Fighter','level_before_award'=>1,'xp_award'=>50,'xp_award_date'=>'8.1.2026','level_after_award'=>1], ['character_name'=>'Alpha','character_class'=>'Fighter','level_before_award'=>1,'xp_award'=>25,'xp_award_date'=>'8.1.2026','level_after_award'=>1]];
DataInvariantContract::assertAwards($award, 'alpha-xp');
invariantReject(fn()=>DataInvariantContract::assertAwards([$award[0], $award[0]], 'alpha-xp'), 'awards.unique-event');
invariantReject(fn()=>DataInvariantContract::assertAwards([$award[0], array_replace($award[1], ['xp_award_date'=>'7.31.2026'])], 'alpha-xp'), 'awards.monotonic-date');
$words = ['wiki'=>['pages'=>2,'words'=>10], 'ic'=>['files'=>1,'words'=>5], 'ooc'=>['files'=>1,'words'=>3]];
DataInvariantContract::assertWordCounts($words);
$lower = $words; $lower['wiki']['words'] = 9;
invariantReject(fn()=>DataInvariantContract::assertWordCounts($lower, $words), 'word-count.monotonic');
$quests = [['id'=>'start','unlocked_by'=>[]], ['id'=>'next','unlocked_by'=>['start']]];
DataInvariantContract::assertQuests($quests);
invariantReject(fn()=>DataInvariantContract::assertQuests([['id'=>'next','unlocked_by'=>['missing']]]), 'quests.referential-join');
invariantReject(fn()=>DataInvariantContract::assertQuests([['id'=>'a','unlocked_by'=>['b']],['id'=>'b','unlocked_by'=>['a']]]), 'quests.acyclic-prerequisites');
$messages = [['id'=>str_repeat('a',32),'message'=>'hello','sent_at'=>100,'recipient_account_id'=>str_repeat('b',32)]];
DataInvariantContract::assertMessages($messages, str_repeat('b',32));
invariantReject(fn()=>DataInvariantContract::assertMessages([$messages[0], $messages[0]], str_repeat('b',32)), 'messages.unique-id');
invariantReject(fn()=>DataInvariantContract::assertMessages([['id'=>str_repeat('c',32),'message'=>'x','sent_at'=>100,'recipient_account_id'=>str_repeat('d',32)]], str_repeat('b',32)), 'messages.authoritative-ownership');
$roster = [['character_key'=>'alpha'],['character_key'=>'beta']];
DataInvariantContract::assertRosterJoin($roster, [['character_key'=>'alpha'],['character_key'=>'beta']], 'alpha');
invariantReject(fn()=>DataInvariantContract::assertRosterJoin($roster, [['character_key'=>'orphan']], 'alpha'), 'roster.xp-referential-join');
invariantReject(fn()=>DataInvariantContract::assertRosterJoin($roster, [['character_key'=>'alpha'],['character_key'=>'alpha']], 'alpha'), 'roster.xp-referential-join');
echo "Data invariant contract tests passed.\n";
