const STORAGE_KEY = 'player-assistant:offline-actions:v1';
const MAX_ACTIONS = 100;
const MAX_AGE_MS = 7 * 24 * 60 * 60 * 1000;
const QUEUE_STATES = Object.freeze({ QUEUED: 'queued', SENDING: 'sending', DUPLICATE: 'duplicate', CONFLICT: 'conflict', DISCARDED: 'discarded', EXHAUSTED: 'exhausted' });
const MUTATING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
const RETRYABLE_STATUS = new Set([408, 425, 429, 500, 502, 503, 504]);

const hashBody = (body) => JSON.stringify(body === undefined ? null : body);
const clone = (value) => JSON.parse(JSON.stringify(value));
const validIdentity = (value) => typeof value === 'string' && value.length > 0 && value.length <= 128;

export { QUEUE_STATES, MUTATING_METHODS };

export function createOfflineActionQueue({ storage = globalThis.localStorage, now = Date.now, maxAttempts = 3, maxActions = MAX_ACTIONS, maxAgeMs = MAX_AGE_MS, onState = () => {} } = {}) {
    const read = () => {
        try {
            const parsed = JSON.parse(storage?.getItem(STORAGE_KEY) || '[]');
            return Array.isArray(parsed) ? parsed.filter((item) => item && typeof item === 'object') : [];
        } catch { return []; }
    };
    let actions = read();
    const persist = () => {
        actions = actions.filter((item) => now() - item.createdAt <= maxAgeMs && [QUEUE_STATES.QUEUED, QUEUE_STATES.SENDING, QUEUE_STATES.CONFLICT, QUEUE_STATES.EXHAUSTED, QUEUE_STATES.DISCARDED].includes(item.state));
        actions.sort((a, b) => a.sequence - b.sequence);
        if (actions.length > maxActions) actions = actions.slice(-maxActions);
        try { storage?.setItem(STORAGE_KEY, JSON.stringify(actions)); } catch { /* fail closed: memory state remains safe */ }
    };
    const emit = (item, state, reason = '') => {
        item.state = state;
        item.reason = reason;
        item.updatedAt = now();
        onState({ id: item.id, state, reason, item: clone(item) });
    };
    const discardStale = ({ accountId, generation }) => {
        for (const item of actions) {
            if (item.accountId !== accountId || item.generation !== generation) emit(item, QUEUE_STATES.DISCARDED, 'stale_generation_or_identity');
        }
        persist();
    };
    return {
        enqueue(input) {
            if (!input || !MUTATING_METHODS.has(String(input.method).toUpperCase()) || !validIdentity(input.accountId) || !validIdentity(input.generation) || !validIdentity(input.idempotencyKey) || typeof input.route !== 'string' || !input.route.startsWith('/')) {
                throw new TypeError('Offline action identity, route, or method is invalid.');
            }
            const method = String(input.method).toUpperCase();
            const bodyHash = hashBody(input.body);
            const same = actions.find((item) => item.accountId === input.accountId && item.generation === input.generation && item.idempotencyKey === input.idempotencyKey && item.method === method && item.route === input.route);
            if (same) {
                if (same.bodyHash !== bodyHash) { emit(same, QUEUE_STATES.CONFLICT, 'idempotency_key_collision'); persist(); return clone(same); }
                return { ...clone(same), state: QUEUE_STATES.DUPLICATE };
            }
            const item = { id: `${now()}-${Math.random().toString(36).slice(2, 10)}`, sequence: actions.reduce((max, entry) => Math.max(max, Number(entry.sequence) || 0), 0) + 1, createdAt: now(), updatedAt: now(), attempts: 0, state: QUEUE_STATES.QUEUED, reason: 'offline', accountId: input.accountId, generation: input.generation, method, route: input.route, idempotencyKey: input.idempotencyKey, body: clone(input.body === undefined ? null : input.body), bodyHash };
            actions.push(item); emit(item, QUEUE_STATES.QUEUED, 'offline'); persist(); return clone(item);
        },
        list() { persist(); return clone(actions); },
        cancel(id) { const before = actions.length; actions = actions.filter((item) => item.id !== id); persist(); return actions.length !== before; },
        clearForIdentity(accountId, generation = null) { actions = actions.filter((item) => !(item.accountId === accountId && (generation === null || item.generation === generation))); persist(); },
        async flush({ accountId, generation, send }) {
            if (!validIdentity(accountId) || !validIdentity(generation) || typeof send !== 'function') return 0;
            discardStale({ accountId, generation });
            let completed = 0;
            for (const item of [...actions].filter((entry) => entry.accountId === accountId && entry.generation === generation && entry.state === QUEUE_STATES.QUEUED)) {
                emit(item, QUEUE_STATES.SENDING, 'reconnect'); persist();
                try {
                    const response = await send(clone(item));
                    const status = Number(response?.status || 0);
                    if (status >= 200 && status < 300 || status === 409 && response?.error === 'idempotency_replay') {
                        actions = actions.filter((entry) => entry.id !== item.id); completed++; onState({ id: item.id, state: 'completed', item: clone(item) }); persist(); continue;
                    }
                    if (status === 409 || (status >= 400 && !RETRYABLE_STATUS.has(status))) emit(item, QUEUE_STATES.CONFLICT, response?.error || 'server_conflict');
                    else { item.attempts += 1; emit(item, item.attempts >= maxAttempts ? QUEUE_STATES.EXHAUSTED : QUEUE_STATES.QUEUED, response?.error || 'retryable_failure'); }
                } catch (error) {
                    item.attempts += 1;
                    emit(item, item.attempts >= maxAttempts ? QUEUE_STATES.EXHAUSTED : QUEUE_STATES.QUEUED, error?.code || 'network_error');
                }
                persist();
            }
            return completed;
        }
    };
}
