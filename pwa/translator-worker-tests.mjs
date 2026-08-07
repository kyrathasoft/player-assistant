import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('./translator-worker.js', import.meta.url), 'utf8');

const createWorker = (payloadFactory) => {
    const messages = [];
    let messageHandler;
    const sandbox = {
        console,
        fetch: async () => ({
            ok: true,
            status: 200,
            json: async () => payloadFactory()
        }),
        self: {
            addEventListener: (type, handler) => {
                if (type === 'message') messageHandler = handler;
            },
            postMessage: (message) => messages.push(message)
        }
    };
    vm.runInNewContext(source, sandbox, { filename: 'translator-worker.js' });
    return {
        translate: async (message) => {
            await messageHandler({ data: { type: 'translate', language: 'orcish', ...message } });
            return messages.findLast((entry) => entry.type === 'translation' && entry.id === message.id);
        }
    };
};

const worker = createWorker(() => ({
    entryCount: 6,
    maxPhraseWords: 3,
    terms: {
        'silver   moon': 'luna',
        'battle cry': 'zug zug',
        hello: 'lok',
        'dark fire': 'shadow   flame',
        first: 'same',
        second: 'same'
    }
}));

assert.equal((await worker.translate({ id: 1, text: 'Silver\t  moon' })).translation, 'Luna');
assert.equal((await worker.translate({ id: 2, text: 'BATTLE CRY' })).translation, 'ZUG ZUG');
assert.equal((await worker.translate({ id: 3, text: 'Hello, hello!' })).translation, 'Lok, lok!');
assert.equal((await worker.translate({ id: 4, text: 'shadow \t flame', reverse: true })).translation, 'dark fire');
assert.equal((await worker.translate({ id: 5, text: 'same', reverse: true })).translation, 'first');

const malformedWorker = createWorker(() => { throw new Error('malformed lexicon'); });
const malformedResult = await malformedWorker.translate({ id: 6, text: 'hello' });
assert.match(malformedResult.error, /malformed lexicon/u);

console.log('Translator worker runtime tests passed.');
