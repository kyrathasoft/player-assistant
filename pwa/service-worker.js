'use strict';

importScripts('./version.js?v=1');

const VERSION_METADATA = globalThis.PLAYER_ASSISTANT_VERSION_METADATA;
if (!VERSION_METADATA) {
    throw new Error('Player Assistant version metadata is unavailable.');
}
const CACHE_VERSION = `player-assistant-pwa-${VERSION_METADATA.pwaVersion}-v${VERSION_METADATA.cacheRevision}`;
const SHELL_CACHE = `${CACHE_VERSION}-shell`;
const DATA_CACHE = `${CACHE_VERSION}-data`;
const CACHE_GENERATION_PATTERN = /^player-assistant-pwa-(\d+(?:\.\d+)*)-v(\d+)-(?:shell|data)$/;
const SHELL_ASSETS = [
    './',
    './index.html',
    `./version.js?v=${VERSION_METADATA.metadataRevision}`,
    `./styles.css?v=${VERSION_METADATA.stylesRevision}`,
    `./app.js?v=${VERSION_METADATA.appRevision}`,
    `./modules/translator.js?v=${VERSION_METADATA.appRevision}`,
    `./modules/search.js?v=${VERSION_METADATA.appRevision}`,
    `./modules/dice.js?v=${VERSION_METADATA.appRevision}`,
    './translator-worker.js',
    `./campaign-search-worker.js?v=${VERSION_METADATA.appRevision}`,
    './offline.html',
    './manifest.webmanifest',
    './icons/icon-192.png',
    './icons/icon-512.png',
    './icons/dragon-mark.png',
    './magic-items.json',
    './party-funds.json',
    './level-progression.json'
];
const OFFLINE_DATA_ASSETS = [
    './data/orcish.json',
    './data/elvish.json',
    './data/ghukliak.json',
    './campaign-search.json'
];
const canonicalRequestKey = (asset) => {
    const url = new URL(asset, self.location.href);
    return `${url.pathname}${url.search}`;
};
const SHELL_REQUEST_KEYS = new Set(SHELL_ASSETS.map(canonicalRequestKey));
const OFFLINE_DATA_REQUEST_KEYS = new Set(OFFLINE_DATA_ASSETS.map(canonicalRequestKey));

const parseCacheGeneration = (cacheName) => {
    const match = CACHE_GENERATION_PATTERN.exec(cacheName);
    if (!match) return null;
    return [...match[1].split('.').map(Number), Number(match[2])];
};

const compareCacheGenerations = (left, right) => {
    const width = Math.max(left.length, right.length);
    for (let index = 0; index < width; index += 1) {
        const difference = (left[index] || 0) - (right[index] || 0);
        if (difference !== 0) return difference;
    }
    return 0;
};

const rejectObsoleteWorker = (cacheNames) => {
    const currentGeneration = parseCacheGeneration(SHELL_CACHE);
    const newerCache = cacheNames.find((cacheName) => {
        const generation = parseCacheGeneration(cacheName);
        return generation && compareCacheGenerations(generation, currentGeneration) > 0;
    });
    if (newerCache) {
        throw new Error(`A newer service-worker generation already owns cache ${newerCache}.`);
    }
};

const deleteCurrentCaches = async () => {
    await Promise.all([caches.delete(SHELL_CACHE), caches.delete(DATA_CACHE)]);
};

const cacheAssets = async (cacheName, assets) => {
    const cache = await caches.open(cacheName);
    try {
        await cache.addAll(assets.map((asset) => new Request(asset, { cache: 'reload' })));
    } catch (error) {
        await caches.delete(cacheName);
        throw error;
    }
};

const safeCachePut = async (cache, request, response) => {
    try {
        await cache.put(request, response.clone());
    } catch (error) {
        if (error?.name !== 'QuotaExceededError') throw error;
    }
};

const isRecord = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);

const hasExpectedContentType = (pathname, response) => {
    const contentType = response.headers.get('Content-Type')?.toLowerCase() || '';
    if (pathname.endsWith('.json')) return contentType.includes('application/json');
    if (pathname.endsWith('.webmanifest')) {
        return contentType.includes('application/manifest+json') || contentType.includes('application/json');
    }
    if (pathname.endsWith('.html')) return contentType.includes('text/html');
    if (pathname.endsWith('.css')) return contentType.includes('text/css');
    if (pathname.endsWith('.js')) return contentType.includes('javascript');
    if (pathname.endsWith('.png')) return contentType.includes('image/png');
    if (pathname.endsWith('.webp')) return contentType.includes('image/webp');
    return true;
};

const isValidJsonPayload = (pathname, value) => {
    if (!isRecord(value)) return false;
    if (/\/data\/(?:orcish|elvish|ghukliak)\.json$/u.test(pathname)) {
        return Number.isInteger(value.schemaVersion)
            && typeof value.language === 'string'
            && Number.isInteger(value.entryCount)
            && value.entryCount > 0
            && Number.isInteger(value.maxPhraseWords)
            && value.maxPhraseWords > 0
            && isRecord(value.terms)
            && Object.keys(value.terms).length === value.entryCount;
    }
    if (pathname.endsWith('/campaign-search.json')) {
        return Number.isInteger(value.schemaVersion)
            && Number.isInteger(value.pageCount)
            && value.pageCount >= 0
            && Array.isArray(value.pages)
            && value.pages.length === value.pageCount
            && Number.isInteger(value.wordCount)
            && value.wordCount >= 0;
    }
    if (pathname.endsWith('/magic-items.json')) {
        return Number.isInteger(value.schema_version) && Array.isArray(value.items);
    }
    if (pathname.endsWith('/party-funds.json')) {
        return Number.isInteger(value.schema_version) && isRecord(value.coins);
    }
    if (pathname.endsWith('/level-progression.json')) {
        return Number.isInteger(value.schema_version) && isRecord(value.classes);
    }
    if (pathname.endsWith('/data/heroes.json')) {
        return Array.isArray(value.heroes) && value.heroes.length > 0;
    }
    if (pathname.endsWith('/manifest.webmanifest')) {
        return typeof value.name === 'string'
            && typeof value.start_url === 'string'
            && typeof value.scope === 'string'
            && Array.isArray(value.icons)
            && value.icons.length > 0;
    }
    return true;
};

const isValidCachedResponse = async (request, response) => {
    if (!response?.ok) return false;
    const pathname = new URL(request.url || request, self.location.href).pathname;
    try {
        if (!hasExpectedContentType(pathname, response)) return false;
        if ((await response.clone().arrayBuffer()).byteLength === 0) return false;
        if (!pathname.endsWith('.json') && !pathname.endsWith('.webmanifest')) return true;
        const value = await response.clone().json();
        return isValidJsonPayload(pathname, value);
    } catch {
        return false;
    }
};

const cacheResponseIfValid = async (cache, request, response) => {
    if (await isValidCachedResponse(request, response)) {
        await safeCachePut(cache, request, response);
    }
};

self.addEventListener('install', (event) => {
    event.waitUntil(
        Promise.all([
            cacheAssets(SHELL_CACHE, SHELL_ASSETS),
            cacheAssets(DATA_CACHE, OFFLINE_DATA_ASSETS)
        ])
            .catch(async (error) => {
                await deleteCurrentCaches();
                throw error;
            })
    );
});

self.addEventListener('activate', (event) => {
    event.waitUntil((async () => {
        const keys = await caches.keys();
        rejectObsoleteWorker(keys);
        await Promise.all(keys
            .filter((key) => key.startsWith('player-assistant-pwa-') && ![SHELL_CACHE, DATA_CACHE].includes(key))
            .map((key) => caches.delete(key)));
        await self.clients.claim();
    })());
});

const cacheFirst = async (request, cacheName) => {
    let cache = null;
    try {
        cache = await caches.open(cacheName);
        const cached = await cache.match(request);
        if (await isValidCachedResponse(request, cached)) return cached;
        if (cached) await cache.delete(request);
    } catch {
        cache = null;
    }
    const response = await fetch(request);
    if (cache) await cacheResponseIfValid(cache, request, response);
    return response;
};

const networkFirstData = async (request) => {
    const cache = await caches.open(DATA_CACHE);
    try {
        const response = await fetch(new Request(request, { cache: 'reload' }));
        await cacheResponseIfValid(cache, request, response);
        return response;
    } catch {
        const cached = await cache.match(request);
        if (await isValidCachedResponse(request, cached)) return cached;
        if (cached) await cache.delete(request);
        throw new Error('Network and cached PWA data are unavailable.');
    }
};

const networkFirstNavigation = async (request) => {
    const cache = await caches.open(SHELL_CACHE);
    try {
        const response = await fetch(request);
        await cacheResponseIfValid(cache, './index.html', response);
        return response;
    } catch {
        for (const fallbackRequest of ['./index.html', './offline.html']) {
            const cached = await cache.match(fallbackRequest);
            if (await isValidCachedResponse(fallbackRequest, cached)) return cached;
            if (cached) await cache.delete(fallbackRequest);
        }
        throw new Error('Network and cached PWA navigation shells are unavailable.');
    }
};

self.addEventListener('fetch', (event) => {
    const request = event.request;
    if (request.method !== 'GET') return;
    const url = new URL(request.url);
    if (url.origin !== self.location.origin) return;
    if (url.pathname.startsWith('/scarlethorizons/api/')) return;

    if (request.mode === 'navigate') {
        event.respondWith(networkFirstNavigation(request));
        return;
    }

    if (url.pathname.endsWith('/data/heroes.json')
        || url.pathname.includes('/data/hero-tokens/')) {
        event.respondWith(networkFirstData(request));
        return;
    }

    if (url.pathname.endsWith('/party-funds.json')) {
        event.respondWith(networkFirstData(request));
        return;
    }

    if (url.pathname.endsWith('/campaign-search.json')) {
        event.respondWith(networkFirstData(request));
        return;
    }


    const requestKey = `${url.pathname}${url.search}`;
    if (OFFLINE_DATA_REQUEST_KEYS.has(requestKey)) {
        event.respondWith(cacheFirst(request, DATA_CACHE));
        return;
    }

    if (SHELL_REQUEST_KEYS.has(requestKey)) {
        event.respondWith(cacheFirst(request, SHELL_CACHE));
    }
});

self.addEventListener('message', (event) => {
    if (event.data?.type === 'SKIP_WAITING') self.skipWaiting();
});
