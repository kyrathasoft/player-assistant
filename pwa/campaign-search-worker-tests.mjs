import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('./campaign-search-worker.js', import.meta.url), 'utf8');
const messages = [];
let handler;
const sandbox = {
    fetch: async () => ({
        ok: true,
        status: 200,
        json: async () => ({
            termIndexVersion: 1,
            termIndex: {
                silver: [0],
                moon: [0],
                sun: [1]
            },
            pages: [
                { title: 'Silver Moon', url: 'https://example.test/silver', content: 'A moonlit page.' },
                { title: 'Sun Chapel', url: 'https://example.test/sun', content: 'A bright page.' },
                { title: 'Unindexed Silver Page', url: 'https://example.test/unindexed', content: 'A silver page.' }
            ]
        })
    }),
    self: {
        addEventListener: (type, callback) => {
            if (type === 'message') handler = callback;
        },
        postMessage: (message) => messages.push(message)
    }
};
vm.runInNewContext(source, sandbox, { filename: 'campaign-search-worker.js' });

await handler({ data: { type: 'search', id: 1, query: 'silver' } });
const result = messages.findLast((message) => message.id === 1);
assert.equal(result.type, 'search-results');
assert.equal(result.results.length, 1);
assert.equal(result.results[0].title, 'Silver Moon');

await handler({ data: { type: 'search', id: 2, query: 'sun' } });
const secondResult = messages.findLast((message) => message.id === 2);
assert.equal(secondResult.results[0].title, 'Sun Chapel');

await handler({ data: { type: 'search', id: 3, query: 'silver moon' } });
const multiTermResult = messages.findLast((message) => message.id === 3);
assert.equal(multiTermResult.results[0].title, 'Silver Moon');

await handler({ data: { type: 'search', id: 4, query: '*silver' } });
const wildcardResult = messages.findLast((message) => message.id === 4);
assert.equal(wildcardResult.results.length, 2);

console.log('Campaign search worker runtime tests passed.');
