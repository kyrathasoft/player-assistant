import assert from 'node:assert/strict';
const channels = new Map();
class FakeBroadcastChannel {
    constructor(name) { this.name = name; this.listeners = []; (channels.get(name) || channels.set(name, []).get(name)).push(this); }
    addEventListener(type, listener) { if (type === 'message') this.listeners.push(listener); }
    postMessage(data) { for (const peer of channels.get(this.name) || []) if (peer !== this) peer.listeners.forEach((listener) => listener({ data })); }
    close() {}
}
globalThis.BroadcastChannel = FakeBroadcastChannel;
globalThis.addEventListener = () => {};
globalThis.localStorage = { setItem() {} };
const { createAccountSessionController } = await import('./modules/account-session.js');
let hiddenAccount = { id: 'account-a' };
let hiddenTransitions = 0;
const hidden = createAccountSessionController({ restore: async () => hiddenAccount, onChange: (account) => { if (account === null) hiddenTransitions++; } });
const visible = createAccountSessionController({ restore: async () => null });
hidden.setAccount(hiddenAccount);
visible.setAccount({ id: 'account-a' });
visible.beginTransition();
assert.equal(hidden.account(), null);
assert.equal(hiddenTransitions, 1);
hidden.shutdown(); visible.shutdown();
console.log('Cross-tab authentication transition tests passed.');
