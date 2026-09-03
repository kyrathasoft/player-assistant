import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('./version.js', import.meta.url), 'utf8');
const context = vm.createContext({ globalThis: null });
context.globalThis = context;
vm.runInContext(source, context, { filename: 'version.js' });
const metadata = context.PLAYER_ASSISTANT_VERSION_METADATA;
assert.match(metadata.pwaVersion, /^\d+\.\d+\.\d+$/);
assert.ok(Number.isInteger(metadata.cacheRevision) && metadata.cacheRevision > 0);
assert.ok(Number.isInteger(metadata.appRevision) && metadata.appRevision > 0);

const cacheName = (version, cacheRevision, appRevision) =>
    `player-assistant-pwa-${version}-v${cacheRevision}-app${appRevision}`;
const oldGeneration = cacheName('0.9.7', 114, 99);
const currentGeneration = cacheName(metadata.pwaVersion, metadata.cacheRevision, metadata.appRevision);
assert.notEqual(oldGeneration, currentGeneration, 'cache generations must not alias');

const state = new Map([
    [oldGeneration, new Map([['index.html', 'prior shell'], ['data/orcish.json', 'prior pack']])],
    [currentGeneration, new Map([['index.html', 'current shell']])]
]);
const beforeRollback = JSON.stringify([...state].map(([name, entries]) => [name, [...entries]]));
// Interrupted promotion removes only the candidate generation; the prior generation remains byte-identical.
state.delete(currentGeneration);
const afterRollback = JSON.stringify([...state].map(([name, entries]) => [name, [...entries]]));
assert.equal(afterRollback, JSON.stringify([
    [oldGeneration, [['index.html', 'prior shell'], ['data/orcish.json', 'prior pack']]]
]));
assert.notEqual(beforeRollback, afterRollback);

const finalized = new Set([oldGeneration]);
assert.throws(() => { if (finalized.has(oldGeneration)) throw new Error('rollback forbidden'); }, /rollback forbidden/);
console.log('PWA downgrade and rollback compatibility tests passed.');
