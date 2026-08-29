import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash, webcrypto } from 'node:crypto';
import vm from 'node:vm';

const source = await readFile(new URL('./optional-pack-loader.js', import.meta.url), 'utf8');
const payload = {
    schemaVersion: 1,
    language: 'Orcish',
    entryCount: 1,
    maxPhraseWords: 1,
    reverseMaxPhraseWords: 1,
    terms: { hello: 'zug' }
};
const bytes = Buffer.from(JSON.stringify(payload));
const hash = createHash('sha256').update(bytes).digest('hex');
const manifest = {
    schemaVersion: 1,
    manifestVersion: 1,
    packs: [{
        id: 'translator-orcish',
        kind: 'translator',
        language: 'orcish',
        url: 'data/orcish.json',
        schemaVersion: 1,
        contentHash: hash,
        byteSize: bytes.length,
        recordCount: 1,
        validation: { entryCount: 1, maxPhraseWords: 1, reverseMaxPhraseWords: 1 }
    }]
};

class MemoryCache {
    constructor() { this.entries = new Map(); }
    async match(request) { return this.entries.get(String(request.url || request)); }
    async put(request, response) { this.entries.set(String(request.url || request), response.clone()); }
    async delete(request) { return this.entries.delete(String(request.url || request)); }
    async keys() { return [...this.entries.keys()].map((url) => new Request(url)); }
}

const createHarness = (responses, cacheMap = new Map(), manifestFailures = 0) => {
    let packFetches = 0;
    let manifestFetches = 0;
    const cacheApi = {
        async open(name) {
            if (!cacheMap.has(name)) cacheMap.set(name, new MemoryCache());
            return cacheMap.get(name);
        },
        async delete(name) { return cacheMap.delete(name); },
        async keys() { return [...cacheMap.keys()]; }
    };
    const context = vm.createContext({
        console,
        URL,
        Request,
        Response,
        TextDecoder,
        TextEncoder,
        Uint8Array,
        Map,
        Set,
        Promise,
        setTimeout,
        crypto: webcrypto,
        caches: cacheApi,
        location: { href: 'https://example.test/scarlethorizons/pwa/optional-pack-loader.js' },
        fetch: async (request) => {
            const url = String(request.url || request);
            if (url.endsWith('/optional-packs.json') || url === 'optional-packs.json') {
                manifestFetches += 1;
                if (manifestFetches <= manifestFailures) throw new Error('manifest unavailable');
                return Response.json(manifest, { headers: { 'Content-Type': 'application/json' } });
            }
            packFetches += 1;
            const next = responses.length > 1 ? responses.shift() : responses[0];
            if (next instanceof Error) throw next;
            return (await next).clone();
        }
    });
    context.globalThis = context;
    vm.runInContext(source, context, { filename: 'optional-pack-loader.js' });
    return {
        loader: context.PlayerAssistantPackLoader,
        cacheMap,
        packFetches: () => packFetches,
        manifestFetches: () => manifestFetches
    };
};

const validResponse = () => new Response(bytes, { headers: { 'Content-Type': 'application/json' } });
const sharedCaches = new Map();
const first = createHarness([validResponse()], sharedCaches);
assert.equal(JSON.stringify(await first.loader.loadPack('translator-orcish')), JSON.stringify(payload));
assert.equal(first.packFetches(), 1);
const optionalCache = sharedCaches.get('player-assistant-optional-pack-translator-orcish');
assert.equal(optionalCache.entries.size, 1, 'validated packs are stored in an isolated optional cache');
const [storedKey] = optionalCache.entries.keys();
assert.match(storedKey, /pack-hash=/u, 'cache key is content-addressed');
assert.equal((await optionalCache.entries.get(storedKey).clone().arrayBuffer()).byteLength, bytes.length, 'cache stores exact response bytes');

const invalidCacheMap = new Map();
const invalidCache = new MemoryCache();
const cacheKey = `https://example.test/scarlethorizons/pwa/data/orcish.json?pack-hash=${hash}`;
await invalidCache.put(cacheKey, new Response('{bad', { headers: { 'Content-Type': 'application/json' } }));
invalidCacheMap.set('player-assistant-optional-pack-translator-orcish', invalidCache);
const invalidCached = createHarness([validResponse()], invalidCacheMap);
assert.equal(JSON.stringify(await invalidCached.loader.loadPack('translator-orcish')), JSON.stringify(payload));
assert.equal(invalidCached.packFetches(), 1, 'invalid cached bytes are removed before refetch');
assert.equal(invalidCache.entries.size, 1, 'the validated replacement remains the only optional entry');

const invalidNetwork = createHarness([new Response(JSON.stringify({ schemaVersion: 1, language: 'Orcish' }), { headers: { 'Content-Type': 'application/json' } })]);
await assert.rejects(invalidNetwork.loader.loadPack('translator-orcish'), /content verification|schema validation/u);
assert.equal(invalidNetwork.cacheMap.get('player-assistant-optional-pack-translator-orcish')?.entries.size || 0, 0, 'invalid network payload is never cached');

const replacementFailure = createHarness([new Response('offline', { status: 503, headers: { 'Content-Type': 'application/json' } })], sharedCaches);
await assert.rejects(
    replacementFailure.loader.loadPack('translator-orcish', { force: true }),
    /503/u);
const retained = createHarness([new Response('offline', { status: 503, headers: { 'Content-Type': 'application/json' } })], sharedCaches);
assert.equal(JSON.stringify(await retained.loader.loadPack('translator-orcish')), JSON.stringify(payload));
assert.equal(retained.packFetches(), 0, 'valid cached pack should survive failed replacement');

const manifestRetry = createHarness([validResponse()], new Map(), 1);
await assert.rejects(manifestRetry.loader.loadPack('translator-orcish'), /manifest unavailable/u);
assert.equal(JSON.stringify(await manifestRetry.loader.loadPack('translator-orcish')), JSON.stringify(payload));
assert.equal(manifestRetry.manifestFetches(), 2, 'failed manifest requests are retryable');

const retry = createHarness([
    new Response('temporary', { status: 503, headers: { 'Content-Type': 'application/json' } }),
    new Response('temporary', { status: 503, headers: { 'Content-Type': 'application/json' } }),
    validResponse()
]);
assert.equal(JSON.stringify(await retry.loader.loadPack('translator-orcish')), JSON.stringify(payload));
assert.equal(retry.packFetches(), 3, 'transient pack failures should use bounded retries');

const delayedResponses = [];
const delayed = createHarness([new Promise((resolve) => delayedResponses.push(resolve))]);
const pendingLoad = delayed.loader.loadPack('translator-orcish');
await new Promise((resolve) => setTimeout(resolve, 0));
await delayed.loader.removePack('translator-orcish');
delayedResponses[0](validResponse());
await assert.rejects(pendingLoad, /removed|generation|cancel/i);
assert.equal(delayed.cacheMap.has('player-assistant-optional-pack-translator-orcish'), false, 'late load cannot resurrect removed pack');

await retained.loader.removePack('translator-orcish');
assert.equal(retained.cacheMap.has('player-assistant-optional-pack-translator-orcish'), false);

console.log('Optional-pack lifecycle retention, retry, and removal tests passed.');
