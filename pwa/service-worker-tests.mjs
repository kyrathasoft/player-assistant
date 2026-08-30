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
    constructor(entries = [], { addAllError = null, putError = null, addAllFetch = null } = {}) {
        this.entries = new Map(entries.map(([request, response]) => [requestKey(request), response]));
        this.deleted = [];
        this.addAllError = addAllError;
        this.putError = putError;
        this.addAllFetch = addAllFetch;
    }

    async addAll(requests) {
        if (this.addAllError) throw this.addAllError;
        if (this.addAllFetch) {
            for (const request of requests) {
                const response = await this.addAllFetch(request);
                this.entries.set(requestKey(request), response.clone());
            }
        }
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
        caches,
        console,
        fetch: fetchImpl || (async () => { throw new Error('Unexpected fetch.'); }),
        globalThis: null,
        importScripts() {
            context.PLAYER_ASSISTANT_VERSION_METADATA = Object.freeze({
                pwaVersion: '0.9.8',
                metadataRevision: 1,
                stylesRevision: 43,
                appRevision: 61,
                cacheRevision: 114
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

const currentDataCache = 'player-assistant-pwa-0.9.8-v114-data';
const currentShellCache = 'player-assistant-pwa-0.9.8-v114-shell';
const translatorPayload = Object.freeze({
    schemaVersion: 1,
    language: 'Orcish',
    entryCount: 1,
    maxPhraseWords: 1,
    terms: { hello: 'zug' }
});

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

const testOptionalPackRequestsBypassServiceWorker = async () => {
    const optionalPaths = [
        'https://example.test/scarlethorizons/pwa/data/orcish.json',
        'https://example.test/scarlethorizons/pwa/data/elvish.json',
        'https://example.test/scarlethorizons/pwa/campaign-search.json'
    ];
    for (const url of optionalPaths) {
        const harness = createHarness({ fetchImpl: async () => { throw new Error('must not be intercepted'); } });
        let responsePromise;
        harness.dispatch('fetch', {
            request: new Request(url),
            respondWith(value) { responsePromise = Promise.resolve(value); }
        });
        assert.equal(responsePromise, undefined, `service worker must not own optional request ${url}`);
    }
};

const testPartialInstallDeletesVersionedCaches = async () => {
    const harness = createHarness({
        cacheEntries: {
            [currentShellCache]: new MemoryCache(),
            [currentDataCache]: new MemoryCache()
        },
        fetchImpl: async () => { throw new Error('Injected addAll failure.'); }
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
    const request = new Request('https://example.test/scarlethorizons/pwa/app.js?v=61');
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
    const newerShell = 'player-assistant-pwa-0.9.8-v115-shell';
    const newerData = 'player-assistant-pwa-0.9.8-v115-data';
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

const testHttpErrorPrefersValidCachedShell = async () => {
    const request = new Request('https://example.test/scarlethorizons/pwa/styles.css?v=43');
    const cached = new Response('body { color: blue; }', {
        status: 200,
        headers: { 'Content-Type': 'text/css' }
    });
    const harness = createHarness({
        cacheEntries: { [currentShellCache]: [[request, cached]] },
        fetchImpl: async () => new Response('temporarily unavailable', {
            status: 503,
            headers: { 'Content-Type': 'text/html' }
        })
    });
    let responsePromise;
    harness.dispatch('fetch', { request, respondWith(value) { responsePromise = Promise.resolve(value); } });
    const response = await responsePromise;
    assert.equal(response.status, 200);
    assert.equal(await response.text(), 'body { color: blue; }');
};

const testMalformedJsonNetworkPrefersValidCachedData = async () => {
    const request = new Request('https://example.test/scarlethorizons/pwa/party-funds.json');
    const cached = new Response(JSON.stringify({ schema_version: 1, coins: { gold: 3 } }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
    });
    const harness = createHarness({
        cacheEntries: { [currentDataCache]: [[request, cached]] },
        fetchImpl: async () => new Response('{not-json', {
            status: 200,
            headers: { 'Content-Type': 'application/json' }
        })
    });
    let responsePromise;
    harness.dispatch('fetch', { request, respondWith(value) { responsePromise = Promise.resolve(value); } });
    const response = await responsePromise;
    assert.equal(await response.text(), JSON.stringify({ schema_version: 1, coins: { gold: 3 } }));
};

const testMandatoryPrecacheRejectsInvalidJsonAndDeletesShell = async () => {
    const invalid = new Response('<html>captive portal</html>', {
        status: 200,
        headers: { 'Content-Type': 'text/html' }
    });
    const harness = createHarness({
        cacheEntries: {
            [currentShellCache]: new MemoryCache([], {
                addAllFetch: async (request) => request.url.endsWith('/party-funds.json') ? invalid.clone() : new Response('x', {
                    status: 200,
                    headers: { 'Content-Type': request.url.endsWith('.json') ? 'application/json' : 'text/html' }
                })
            }),
            [currentDataCache]: new MemoryCache()
        },
        fetchImpl: async () => invalid.clone()
    });
    let installation;
    harness.dispatch('install', { waitUntil(value) { installation = Promise.resolve(value); } });
    await assert.rejects(installation, /invalid|precache|content/i);
    assert.equal(harness.cacheMap.has(currentShellCache), false);
    assert.equal(harness.cacheMap.has(currentDataCache), false);
};

const testNavigationFallsBackAfterBoundedNetworkTimeout = async () => {
    const request = { method: 'GET', mode: 'navigate', url: 'https://example.test/scarlethorizons/pwa/#dashboard' };
    const offline = new Response('<!doctype html><title>Offline</title>', {
        status: 200,
        headers: { 'Content-Type': 'text/html' }
    });
    const harness = createHarness({
        cacheEntries: { [currentShellCache]: [['https://example.test/scarlethorizons/pwa/offline.html', offline]] },
        fetchImpl: async () => new Promise(() => { })
    });
    let responsePromise;
    harness.dispatch('fetch', { request, respondWith(value) { responsePromise = Promise.resolve(value); } });
    const response = await Promise.race([
        responsePromise,
        new Promise((_, reject) => setTimeout(() => reject(new Error('navigation timeout was not bounded')), 250))
    ]);
    assert.equal(await response.text(), '<!doctype html><title>Offline</title>');
};

const testNonCanonicalNavigationDoesNotPromoteShell = async () => {
    const shellRequest = 'https://example.test/scarlethorizons/pwa/index.html';
    const originalShell = '<!doctype html><title>Original shell</title>';
    const alternate = new Response('<!doctype html><title>Offline page</title>', {
        status: 200,
        headers: { 'Content-Type': 'text/html' }
    });
    let fetchCount = 0;
    const harness = createHarness({
        cacheEntries: {
            [currentShellCache]: [[shellRequest, new Response(originalShell, {
                status: 200,
                headers: { 'Content-Type': 'text/html' }
            })]]
        },
        fetchImpl: async () => {
            fetchCount += 1;
            return new Response('<!doctype html><title>Offline page</title>', {
                status: 200,
                headers: { 'Content-Type': 'text/html' }
            });
        }
    });
    let responsePromise;
    harness.dispatch('fetch', {
        request: { method: 'GET', mode: 'navigate', url: 'https://example.test/scarlethorizons/pwa/offline.html' },
        respondWith(value) { responsePromise = Promise.resolve(value); }
    });
    assert.equal(await (await responsePromise).text(), originalShell);
    assert.equal(fetchCount, 1, 'non-canonical navigation should still try the network');
    const cache = harness.cacheMap.get(currentShellCache);
    assert.equal(cache.entries.has(shellRequest), true, 'canonical shell cache entry must remain');
    assert.equal(cache.entries.has('https://example.test/scarlethorizons/pwa/offline.html'), false, 'non-canonical page must not become shell');
};

const tests = [
    testNonCanonicalNavigationDoesNotPromoteShell,
    testHttpErrorPrefersValidCachedShell,
    testMalformedJsonNetworkPrefersValidCachedData,
    testMandatoryPrecacheRejectsInvalidJsonAndDeletesShell,
    testNavigationFallsBackAfterBoundedNetworkTimeout,
    testWrongMimeCachedShellAssetIsDeletedAndRefetched,
    testEmptyCachedShellAssetIsDeletedAndRefetched,
    testCorruptNavigationFallbackUsesValidOfflineShell,
    testOptionalPackRequestsBypassServiceWorker,
    testPartialInstallDeletesVersionedCaches,
    testQuotaFailureReturnsNetworkResponse,
    testObsoleteWorkerCannotDeleteNewerCaches
];

for (const test of tests) {
    await test();
    console.log(`PASS ${test.name}`);
}
