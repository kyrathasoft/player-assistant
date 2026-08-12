import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('./service-worker.js', import.meta.url), 'utf8');
const scopeUrl = 'https://example.test/scarlethorizons/pwa/service-worker.js';

const requestKey = (request) => new URL(
    typeof request === 'string' ? request : request.url,
    scopeUrl).href;

class ScopeRequest extends Request {
    constructor(input, init) {
        super(typeof input === 'string' ? new URL(input, scopeUrl) : input, init);
    }
}

class MemoryCache {
    constructor(entries = [], { addAllError = null, putError = null } = {}) {
        this.entries = new Map(entries.map(([request, response]) => [requestKey(request), response]));
        this.deleted = [];
        this.addAllError = addAllError;
        this.putError = putError;
    }

    async addAll() {
        if (this.addAllError) throw this.addAllError;
    }

    async delete(request) {
        const key = requestKey(request);
        this.deleted.push(key);
        return this.entries.delete(key);
    }

    async match(request) {
        return this.entries.get(requestKey(request));
    }

    async put(request, response) {
        if (this.putError) throw this.putError;
        this.entries.set(requestKey(request), response.clone());
    }
}

const createHarness = ({ cacheEntries = {}, fetchImpl } = {}) => {
    const listeners = new Map();
    const cacheMap = new Map(Object.entries(cacheEntries).map(
        ([name, entries]) => [name, entries instanceof MemoryCache ? entries : new MemoryCache(entries)]));
    let clientsClaimed = false;
    const self = {
        location: { href: scopeUrl, origin: new URL(scopeUrl).origin },
        clients: {
            claim: async () => { clientsClaimed = true; }
        },
        addEventListener(type, listener) {
            listeners.set(type, listener);
        },
        skipWaiting() { }
    };
    const caches = {
        async delete(name) { return cacheMap.delete(name); },
        async keys() { return [...cacheMap.keys()]; },
        async open(name) {
            if (!cacheMap.has(name)) cacheMap.set(name, new MemoryCache());
            return cacheMap.get(name);
        }
    };
    const context = vm.createContext({
        URL,
        Request: ScopeRequest,
        Response,
        AbortController,
        DOMException,
        setTimeout,
        clearTimeout,
        caches,
        console,
        fetch: fetchImpl || (async () => { throw new Error('Unexpected fetch.'); }),
        globalThis: null,
        importScripts() {
            context.PLAYER_ASSISTANT_VERSION_METADATA = Object.freeze({
                pwaVersion: '0.9.8',
                metadataRevision: 1,
                stylesRevision: 43,
                appRevision: 70,
                cacheRevision: 87
            });
        },
        self
    });
    context.globalThis = context;
    vm.runInContext(source, context, { filename: 'service-worker.js' });
    return {
        cacheMap,
        clientsClaimed: () => clientsClaimed,
        dispatch(type, event) {
            const listener = listeners.get(type);
            assert.ok(listener, `Missing ${type} listener.`);
            listener(event);
        }
    };
};

const currentDataCache = 'player-assistant-pwa-0.9.8-v87-data';
const currentShellCache = 'player-assistant-pwa-0.9.8-v87-shell';
const translatorPayload = Object.freeze({
    schemaVersion: 1,
    language: 'Orcish',
    entryCount: 1,
    maxPhraseWords: 1,
    contentHash: 'a'.repeat(64),
    terms: { hello: 'zug' }
});

const testCorruptCachedJsonIsDeletedAndRefetched = async () => {
    const request = new Request('https://example.test/scarlethorizons/pwa/data/orcish.json');
    const corrupt = new Response('{not-json', {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
    });
    const fresh = Response.json(translatorPayload, {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
    });
    const harness = createHarness({
        cacheEntries: { [currentDataCache]: [[request, corrupt]] },
        fetchImpl: async () => fresh.clone()
    });
    let responsePromise;

    harness.dispatch('fetch', {
        request,
        respondWith(value) { responsePromise = Promise.resolve(value); }
    });

    const response = await responsePromise;
    assert.deepEqual(await response.json(), translatorPayload);
    const cache = harness.cacheMap.get(currentDataCache);
    assert.deepEqual(cache.deleted, [request.url]);
};

const testSchemaInvalidCachedJsonIsDeletedAndRefetched = async () => {
    const request = new Request('https://example.test/scarlethorizons/pwa/data/orcish.json');
    const invalid = Response.json({ hello: 'stale' });
    const fresh = Response.json(translatorPayload);
    const harness = createHarness({
        cacheEntries: { [currentDataCache]: [[request, invalid]] },
        fetchImpl: async () => fresh.clone()
    });
    let responsePromise;

    harness.dispatch('fetch', {
        request,
        respondWith(value) { responsePromise = Promise.resolve(value); }
    });

    const response = await responsePromise;
    assert.deepEqual(await response.json(), translatorPayload);
    const cache = harness.cacheMap.get(currentDataCache);
    assert.deepEqual(cache.deleted, [request.url]);
};

const testSchemaInvalidNetworkResponseIsNotCached = async () => {
    const request = new Request('https://example.test/scarlethorizons/pwa/data/orcish.json');
    const invalid = Response.json({ hello: 'network-corruption' });
    const harness = createHarness({
        cacheEntries: { [currentDataCache]: [] },
        fetchImpl: async () => invalid.clone()
    });
    let responsePromise;

    harness.dispatch('fetch', {
        request,
        respondWith(value) { responsePromise = Promise.resolve(value); }
    });

    await assert.rejects(responsePromise, /network response failed PWA validation/i);
    const cache = harness.cacheMap.get(currentDataCache);
    assert.equal(cache.entries.size, 0);
};

const testWrongMimeCachedShellAssetIsDeletedAndRefetched = async () => {
    const request = new Request('https://example.test/scarlethorizons/pwa/styles.css?v=43');
    const corrupt = new Response('<html>not css</html>', {
        status: 200,
        headers: { 'Content-Type': 'text/html' }
    });
    const fresh = new Response('body { color: green; }', {
        status: 200,
        headers: { 'Content-Type': 'text/css' }
    });
    const harness = createHarness({
        cacheEntries: { [currentShellCache]: [[request, corrupt]] },
        fetchImpl: async () => fresh.clone()
    });
    let responsePromise;

    harness.dispatch('fetch', {
        request,
        respondWith(value) { responsePromise = Promise.resolve(value); }
    });

    const response = await responsePromise;
    assert.equal(await response.text(), 'body { color: green; }');
    const cache = harness.cacheMap.get(currentShellCache);
    assert.deepEqual(cache.deleted, [request.url]);
};

const testEmptyCachedShellAssetIsDeletedAndRefetched = async () => {
    const request = new Request('https://example.test/scarlethorizons/pwa/styles.css?v=43');
    const empty = new Response('', {
        status: 200,
        headers: { 'Content-Type': 'text/css' }
    });
    const fresh = new Response('body { color: green; }', {
        status: 200,
        headers: { 'Content-Type': 'text/css' }
    });
    const harness = createHarness({
        cacheEntries: { [currentShellCache]: [[request, empty]] },
        fetchImpl: async () => fresh.clone()
    });
    let responsePromise;

    harness.dispatch('fetch', {
        request,
        respondWith(value) { responsePromise = Promise.resolve(value); }
    });

    const response = await responsePromise;
    assert.equal(await response.text(), 'body { color: green; }');
    const cache = harness.cacheMap.get(currentShellCache);
    assert.deepEqual(cache.deleted, [request.url]);
};

const testCorruptNetworkFirstFallbackIsDeleted = async () => {
    const request = new Request('https://example.test/scarlethorizons/pwa/campaign-search.json');
    const corrupt = new Response('{not-json', {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
    });
    const harness = createHarness({
        cacheEntries: { [currentDataCache]: [[request, corrupt]] },
        fetchImpl: async () => { throw new TypeError('Network unavailable.'); }
    });
    let responsePromise;

    harness.dispatch('fetch', {
        request,
        respondWith(value) { responsePromise = Promise.resolve(value); }
    });

    await assert.rejects(responsePromise, /cached PWA data are unavailable/i);
    const cache = harness.cacheMap.get(currentDataCache);
    assert.deepEqual(cache.deleted, [request.url]);
};

const testInvalidNetworkDataUsesValidCachedCopy = async () => {
    const request = new Request('https://example.test/scarlethorizons/pwa/campaign-search.json');
    const cachedPayload = {
        schemaVersion: 2,
        pageCount: 0,
        pages: [],
        wordCount: 0,
        termIndexVersion: 1,
        termIndex: {}
    };
    const invalidResponses = [
        new Response('not found', { status: 404, headers: { 'Content-Type': 'text/html' } }),
        new Response('temporarily unavailable', { status: 503, headers: { 'Content-Type': 'text/html' } }),
        new Response('{}', { status: 200, headers: { 'Content-Type': 'text/html' } }),
        new Response('<html>login portal</html>', { status: 200, headers: { 'Content-Type': 'text/html' } })
    ];

    for (const invalid of invalidResponses) {
        const harness = createHarness({
            cacheEntries: { [currentDataCache]: [[request, Response.json(cachedPayload)]] },
            fetchImpl: async () => invalid.clone()
        });
        let responsePromise;
        harness.dispatch('fetch', {
            request,
            respondWith(value) { responsePromise = Promise.resolve(value); }
        });
        const response = await responsePromise;
        assert.deepEqual(await response.json(), cachedPayload);
    }
};

const testInvalidNetworkNavigationUsesValidCachedShell = async () => {
    const request = {
        method: 'GET',
        mode: 'navigate',
        url: 'https://example.test/scarlethorizons/pwa/#dashboard'
    };
    const indexRequest = 'https://example.test/scarlethorizons/pwa/index.html';
    const cached = new Response('<!doctype html><title>Cached app</title>', {
        status: 200,
        headers: { 'Content-Type': 'text/html' }
    });
    const harness = createHarness({
        cacheEntries: { [currentShellCache]: [[indexRequest, cached]] },
        fetchImpl: async () => new Response('<html>captive portal</html>', {
            status: 200,
            headers: { 'Content-Type': 'text/html' }
        })
    });
    let responsePromise;
    harness.dispatch('fetch', {
        request,
        respondWith(value) { responsePromise = Promise.resolve(value); }
    });
    const response = await responsePromise;
    assert.equal(await response.text(), '<!doctype html><title>Cached app</title>');
};

const testNavigationFetchHasBoundedTimeout = async () => {
    const request = {
        method: 'GET',
        mode: 'navigate',
        url: 'https://example.test/scarlethorizons/pwa/#dashboard'
    };
    const offline = new Response('<!doctype html><title>Offline</title>', {
        status: 200,
        headers: { 'Content-Type': 'text/html' }
    });
    const harness = createHarness({
        cacheEntries: { [currentShellCache]: [['./offline.html', offline]] },
        fetchImpl: async (_request, { signal } = {}) => new Promise((resolve, reject) => {
            signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
        })
    });
    let responsePromise;
    harness.dispatch('fetch', {
        request,
        respondWith(value) { responsePromise = Promise.resolve(value); }
    });
    const response = await responsePromise;
    assert.equal(await response.text(), '<!doctype html><title>Offline</title>');
};

const testCorruptNavigationFallbackUsesValidOfflineShell = async () => {
    const request = {
        method: 'GET',
        mode: 'navigate',
        url: 'https://example.test/scarlethorizons/pwa/#dashboard'
    };
    const indexRequest = 'https://example.test/scarlethorizons/pwa/index.html';
    const offlineRequest = 'https://example.test/scarlethorizons/pwa/offline.html';
    const emptyIndex = new Response('', {
        status: 200,
        headers: { 'Content-Type': 'text/html' }
    });
    const offline = new Response('<!doctype html><title>Offline</title>', {
        status: 200,
        headers: { 'Content-Type': 'text/html' }
    });
    const harness = createHarness({
        cacheEntries: {
            [currentShellCache]: [[indexRequest, emptyIndex], [offlineRequest, offline]]
        },
        fetchImpl: async () => { throw new TypeError('Network unavailable.'); }
    });
    let responsePromise;

    harness.dispatch('fetch', {
        request,
        respondWith(value) { responsePromise = Promise.resolve(value); }
    });

    const response = await responsePromise;
    assert.equal(await response.text(), '<!doctype html><title>Offline</title>');
    const cache = harness.cacheMap.get(currentShellCache);
    assert.deepEqual(cache.deleted, [indexRequest]);
};

const testPartialInstallDeletesVersionedCaches = async () => {
    const harness = createHarness({
        cacheEntries: {
            [currentShellCache]: new MemoryCache([], { addAllError: new Error('Injected addAll failure.') }),
            [currentDataCache]: new MemoryCache()
        }
    });
    let installation;

    harness.dispatch('install', {
        waitUntil(value) { installation = Promise.resolve(value); }
    });

    await assert.rejects(installation, /Injected addAll failure/);
    assert.equal(harness.cacheMap.has(currentShellCache), false);
    assert.equal(harness.cacheMap.has(currentDataCache), false);
};

const testQuotaFailureReturnsNetworkResponse = async () => {
    const request = new Request('https://example.test/scarlethorizons/pwa/app.js?v=70');
    const fresh = new Response("console.log('ready');", {
        status: 200,
        headers: { 'Content-Type': 'text/javascript' }
    });
    const quotaError = new Error('Injected quota failure.');
    quotaError.name = 'QuotaExceededError';
    const harness = createHarness({
        cacheEntries: {
            [currentShellCache]: new MemoryCache([], { putError: quotaError })
        },
        fetchImpl: async () => fresh.clone()
    });
    let responsePromise;

    harness.dispatch('fetch', {
        request,
        respondWith(value) { responsePromise = Promise.resolve(value); }
    });

    const response = await responsePromise;
    assert.equal(await response.text(), "console.log('ready');");
};

const testObsoleteWorkerCannotDeleteNewerCaches = async () => {
    const newerShell = 'player-assistant-pwa-0.9.8-v88-shell';
    const newerData = 'player-assistant-pwa-0.9.8-v88-data';
    const harness = createHarness({
        cacheEntries: {
            [newerShell]: [],
            [newerData]: []
        }
    });
    let activation;

    harness.dispatch('activate', {
        waitUntil(value) { activation = Promise.resolve(value); }
    });

    await assert.rejects(activation, /newer service-worker generation/i);
    assert.equal(harness.cacheMap.has(newerShell), true);
    assert.equal(harness.cacheMap.has(newerData), true);
    assert.equal(harness.clientsClaimed(), false);
};

const tests = [
    testCorruptCachedJsonIsDeletedAndRefetched,
    testSchemaInvalidCachedJsonIsDeletedAndRefetched,
    testSchemaInvalidNetworkResponseIsNotCached,
    testWrongMimeCachedShellAssetIsDeletedAndRefetched,
    testEmptyCachedShellAssetIsDeletedAndRefetched,
    testCorruptNetworkFirstFallbackIsDeleted,
    testInvalidNetworkDataUsesValidCachedCopy,
    testInvalidNetworkNavigationUsesValidCachedShell,
    testNavigationFetchHasBoundedTimeout,
    testCorruptNavigationFallbackUsesValidOfflineShell,
    testPartialInstallDeletesVersionedCaches,
    testQuotaFailureReturnsNetworkResponse,
    testObsoleteWorkerCannotDeleteNewerCaches
];

for (const test of tests) {
    await test();
    console.log(`PASS ${test.name}`);
}
