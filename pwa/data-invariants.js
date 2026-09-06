export function assertDataInvariant(condition, name) {
    if (!condition) throw new Error(`Invariant failed: ${name}`);
}

export function assertXpRecords(records) {
    assertDataInvariant(Array.isArray(records) && records.length > 0, 'xp.shape');
    const keys = new Set();
    for (const record of records) {
        assertDataInvariant(record && typeof record.character_name === 'string' && Number.isInteger(record.xp_total) && record.xp_total >= 0, 'xp.authoritative-shape');
        const key = record.character_name.toLowerCase();
        assertDataInvariant(!keys.has(key), 'xp.unique-character'); keys.add(key);
    }
}

export function assertAwardRecords(records) {
    assertDataInvariant(Array.isArray(records) && records.length > 0, 'awards.shape');
    const keys = new Set(); let last = '';
    for (const record of records) {
        assertDataInvariant(record && typeof record.character_name === 'string' && Number.isInteger(record.xp_award) && record.xp_award >= 0, 'awards.authoritative-shape');
        const date = String(record.xp_award_date);
        assertDataInvariant(date >= last, 'awards.monotonic-date');
        const key = JSON.stringify([record.character_name, date, record.xp_award, record.level_before_award, record.level_after_award]);
        assertDataInvariant(!keys.has(key), 'awards.unique-event'); keys.add(key); last = date;
    }
}

export function assertScopedMessages(messages, accountId) {
    assertDataInvariant(Array.isArray(messages) && typeof accountId === 'string' && /^[a-f0-9]{32}$/.test(accountId), 'messages.account-scope');
    const ids = new Set();
    for (const message of messages) {
        assertDataInvariant(message && typeof message.id === 'string' && !ids.has(message.id), 'messages.unique-id');
        ids.add(message.id);
    }
}
