import assert from 'node:assert/strict';
import { createOfflineActionQueue, QUEUE_STATES } from './modules/offline-action-queue.js';

const storage = () => {
    const values = new Map();
    return { getItem: (key) => values.get(key) ?? null, setItem: (key, value) => values.set(key, value), removeItem: (key) => values.delete(key) };
};
const action = (overrides = {}) => ({
    accountId: 'a'.repeat(32), generation: 'g1', method: 'POST', route: '/messages',
    idempotencyKey: 'message-1', body: { message: 'hello' }, ...overrides
});

const events = [];
const queue = createOfflineActionQueue({ storage: storage(), now: () => 1000, maxAttempts: 2, onState: (event) => events.push(event) });
assert.equal(queue.enqueue(action()).state, QUEUE_STATES.QUEUED);
assert.equal(queue.enqueue(action()).state, QUEUE_STATES.DUPLICATE);
assert.equal(queue.list().length, 1);
assert.equal(queue.enqueue(action({ body: { message: 'changed' } })).state, QUEUE_STATES.CONFLICT);
assert.equal(queue.list()[0].state, QUEUE_STATES.CONFLICT);
assert.equal(queue.cancel(queue.list()[0].id), true);
assert.equal(queue.list().length, 0);

const replayStorage = storage();
const replay = createOfflineActionQueue({ storage: replayStorage, now: () => 2000, maxAttempts: 2 });
replay.enqueue(action({ idempotencyKey: 'replay-1' }));
const sent = [];
assert.equal(await replay.flush({ accountId: 'a'.repeat(32), generation: 'g1', send: async (item) => { sent.push(item); return { status: 201 }; } }), 1);
assert.equal(sent.length, 1);
assert.equal(replay.list().length, 0);
const restored = createOfflineActionQueue({ storage: replayStorage, now: () => 2001, maxAttempts: 2 });
assert.equal(restored.list().length, 0);

const stale = createOfflineActionQueue({ storage: storage(), now: () => 3000, maxAttempts: 2 });
stale.enqueue(action({ idempotencyKey: 'stale-1' }));
assert.equal(await stale.flush({ accountId: 'a'.repeat(32), generation: 'g2', send: async () => ({ status: 201 }) }), 0);
assert.equal(stale.list()[0].state, QUEUE_STATES.DISCARDED);

const partial = createOfflineActionQueue({ storage: storage(), now: () => 4000, maxAttempts: 2 });
partial.enqueue(action({ idempotencyKey: 'partial-1' }));
partial.enqueue(action({ idempotencyKey: 'partial-2' }));
let attempts = 0;
assert.equal(await partial.flush({ accountId: 'a'.repeat(32), generation: 'g1', send: async (item) => { attempts++; if (item.idempotencyKey === 'partial-1') return { status: 409, error: 'server_conflict' }; return { status: 201 }; } }), 1);
assert.equal(partial.list().find((item) => item.idempotencyKey === 'partial-1').state, QUEUE_STATES.CONFLICT);
assert.equal(partial.list().find((item) => item.idempotencyKey === 'partial-2'), undefined);

const exhausted = createOfflineActionQueue({ storage: storage(), now: () => 5000, maxAttempts: 2 });
exhausted.enqueue(action({ idempotencyKey: 'retry-1' }));
await exhausted.flush({ accountId: 'a'.repeat(32), generation: 'g1', send: async () => { throw new Error('offline'); } });
await exhausted.flush({ accountId: 'a'.repeat(32), generation: 'g1', send: async () => { throw new Error('offline'); } });
assert.equal(exhausted.list()[0].state, QUEUE_STATES.EXHAUSTED);
assert.ok(events.some((event) => event.state === QUEUE_STATES.CONFLICT));
console.log('Offline action queue tests passed.');
