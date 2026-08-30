import assert from 'node:assert/strict';
import { createAccountSessionController } from './modules/account-session.js';
import { createMessagesActivityController } from './modules/messages-activity.js';
import { createPresenceController } from './modules/presence.js';
import { createUpdateLifecycleController } from './modules/update-lifecycle.js';

const testAccountGenerationRejectsStaleRestore = async () => {
    let resolveRestore;
    const restore = new Promise((resolve) => { resolveRestore = resolve; });
    const states = [];
    const controller = createAccountSessionController({ restore: () => restore, onChange: (state) => states.push(state) });
    const pending = controller.restoreSession();
    controller.beginTransition();
    resolveRestore({ id: 'old' });
    await pending;
    assert.equal(controller.account(), null);
    assert.equal(states.length, 0);
};

const testMessagesRefreshIsSingleFlight = async () => {
    let calls = 0;
    let release;
    const controller = createMessagesActivityController({
        load: async () => { calls++; await new Promise((resolve) => { release = resolve; }); }
    });
    const first = controller.refresh();
    const second = controller.refresh();
    await Promise.resolve();
    assert.equal(first, second);
    release();
    await first;
    assert.equal(calls, 1);
};

const testPresenceStopsPollingWhenNotEligible = () => {
    let timers = 0;
    const controller = createPresenceController({ canPoll: () => false, setInterval: () => { timers++; return 1; }, clearInterval: () => {} });
    controller.start();
    assert.equal(timers, 0);
};

const testAccountControllerOwnsCurrentIdentity = () => {
    const states = [];
    const controller = createAccountSessionController({ onChange: (state) => states.push(state) });
    controller.setAccount({ id: 'account-1' });
    assert.deepEqual(controller.account(), { id: 'account-1' });
    controller.beginTransition();
    assert.equal(controller.account(), null);
    assert.deepEqual(states, [{ id: 'account-1' }]);
};

const testUpdateLifecycleCancelsOnShutdown = () => {
    let updates = 0;
    const controller = createUpdateLifecycleController({ apply: () => { updates++; } });
    controller.requestApply();
    controller.shutdown();
    controller.requestApply();
    assert.equal(updates, 1);
};

await testAccountGenerationRejectsStaleRestore();
testAccountControllerOwnsCurrentIdentity();
await testMessagesRefreshIsSingleFlight();
testPresenceStopsPollingWhenNotEligible();
testUpdateLifecycleCancelsOnShutdown();
console.log('Orchestration boundary tests passed.');
